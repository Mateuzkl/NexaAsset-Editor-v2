using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexaModern;

/// <summary>Post-commit GC restricted to writer-formatted backups and hash-verified, unreferenced generated payloads.</summary>
public static class NativeRetention
{
    public const int BackupsToKeep=3;
    private static readonly Regex BackupName=new(@"^\.nexa-backup-\d{8}T\d{9}-[0-9a-f]{32}\.catalog\.json$",RegexOptions.CultureInvariant);
    private static readonly Regex PayloadName=new(@"^(?:appearances-([0-9a-f]{64})\.dat|sprites-([0-9a-f]{64})\.bmp\.lzma)$",RegexOptions.CultureInvariant);
    private sealed record Catalog(string File,byte[] Bytes,HashSet<string> References);
    public static void Prune(string root,Action<string,int,int>? progress=null)
    {
        // A cleanup failure never invalidates an already validated save.
        try{
            root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));AssetSet.RejectLinks(root);
            Catalog Read(string file){
                AssetSet.RejectLinks(file);byte[] bytes=AssetSet.ReadBounded(file,16*1024*1024);
                using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16});
                if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Invalid retention catalog.");
                var references=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in doc.RootElement.EnumerateArray()){
                    if(!entry.TryGetProperty("file",out var value))continue;
                    string relative=value.GetString()??"";
                    if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(':'))throw new InvalidDataException("Unsafe retention resource.");
                    string path=Path.GetFullPath(Path.Combine(root,relative));
                    if(!NativeSave.Within(root,path)||path==root)throw new InvalidDataException("Retention path escapes assets.");
                    AssetSet.RejectLinks(path);references.Add(path);
                }
                return new(file,bytes,references);
            }
            var current=Read(Path.Combine(root,"catalog-content.json"));
            // Parse every backup before deleting anything. Unknown/malformed backups conservatively block pruning.
            var backups=Directory.EnumerateFiles(root,".nexa-backup-*.catalog.json",SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName,StringComparer.Ordinal).Select(Read).ToArray();
            var protectedFiles=new HashSet<string>(current.References,StringComparer.OrdinalIgnoreCase);
            var owned=backups.Where(b=>BackupName.IsMatch(Path.GetFileName(b.File))).ToArray();
            foreach(var b in backups.Where(b=>!BackupName.IsMatch(Path.GetFileName(b.File))).Concat(owned.Take(BackupsToKeep)))protectedFiles.UnionWith(b.References);
            var candidates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int removed=0;
            foreach(var b in owned.Skip(BackupsToKeep)){
                try{
                    AssetSet.RejectLinks(b.File);
                    if(!AssetSet.ReadBounded(b.File,16*1024*1024).AsSpan().SequenceEqual(b.Bytes))throw new IOException("Backup changed during retention cleanup.");
                    File.Delete(b.File);candidates.UnionWith(b.References);removed++;
                }catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException){protectedFiles.UnionWith(b.References);}
            }
            // Recheck the head before payload deletion in case a non-cooperating external writer changed it.
            protectedFiles.UnionWith(Read(Path.Combine(root,"catalog-content.json")).References);
            // Retry unreferenced generated files left by an earlier locked-file cleanup or interrupted publication.
            foreach(string path in Directory.EnumerateFiles(root,"appearances-*.dat").Concat(Directory.EnumerateFiles(root,"sprites-*.bmp.lzma")))
                if(PayloadName.IsMatch(Path.GetFileName(path)))candidates.Add(path);
            int payloads=0;
            foreach(string path in candidates.Where(p=>!protectedFiles.Contains(p))){
                var match=PayloadName.Match(Path.GetFileName(path));if(!match.Success||!string.Equals(Path.GetDirectoryName(path),root,StringComparison.OrdinalIgnoreCase))continue;
                try{
                    AssetSet.RejectLinks(path);string hash=match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                        if(stream.Length>128*1024*1024||!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash,StringComparison.OrdinalIgnoreCase))continue;
                    }
                    File.Delete(path);payloads++;
                }catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException){}
            }
            Report($"Native retention: {removed} expired backups and {payloads} unreferenced generated payloads removed");
        }catch(Exception error)when(error is not OutOfMemoryException){Report("Native retention deferred: "+error.Message);}
        void Report(string message){try{progress?.Invoke(message,1,1);}catch(Exception error)when(error is not OutOfMemoryException){}}
    }
}
