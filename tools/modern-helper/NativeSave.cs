using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpCompress.Compressors.LZMA;

namespace NexaModern;

/// <summary>Native-only persistence: immutable payloads and one atomic catalogue-head replacement.</summary>
public static class NativeSave
{
    public static bool Within(string parent,string child) => string.Equals(parent,child,StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    public static string Export(AssetSet assets,SheetCache cache,string target,Action<string,int,int>? progress=null)
        => Save(assets,cache,target,false,progress);
    public static string InPlace(AssetSet assets,SheetCache cache,Action<string,int,int>? progress=null,Action<string>? checkpoint=null)
        => Save(assets,cache,assets.Root,true,progress,checkpoint);
    private static string Save(AssetSet assets,SheetCache cache,string target,bool inPlace,Action<string,int,int>? progress,Action<string>? checkpoint=null)
    {
        target=Path.GetFullPath(target); string parent=Path.GetDirectoryName(target) ?? throw new InvalidDataException("Invalid destination.");
        AssetSet.RejectLinks(parent);
        if(!inPlace && (!Directory.Exists(parent) || File.Exists(target) || Directory.Exists(target) || Within(assets.Root,target) || Within(target,assets.Root)))
            throw new InvalidDataException("Choose a new output folder outside the source assets. Existing generations are preserved.");
        AssetSet.RejectLinks(assets.Root);
        string stage=inPlace ? Path.Combine(target,".nexa-save-"+Guid.NewGuid().ToString("N")+".partial") : target+".partial";
        if(File.Exists(stage)||Directory.Exists(stage))throw new InvalidDataException("Staging folder already exists.");
        // Check all references before creating any persistent output.
        foreach(var a in assets.Appearances)foreach(var g in AssetSet.Groups(a))foreach(uint id in g.Sprites)assets.FindSheet(id);
        var catalog=JsonNode.Parse(assets.CatalogBytes.Span)!.AsArray();
        var originalSheets=catalog.Where(n=>n?["type"]?.GetValue<string>()=="sprite").ToDictionary(n=>n!["firstspriteid"]!.GetValue<uint>());
        var changed=cache.ChangedIds.ToHashSet(); var entries=new List<JsonNode>();
        var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var published=new List<string>();string? backup=null;bool committed=false;FileStream? writeLock=null;
        string lockFile=Path.Combine(target,".nexa-save.lock");bool ownsLock=false;
        string phase="Checking native save",currentFile=Path.Combine(assets.Root,"catalog-content.json");
        string Destination(string relative)
        {
            if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(':'))throw new InvalidDataException("Invalid catalog path.");
            string path=Path.GetFullPath(Path.Combine(stage,relative));
            if(!Within(stage,path)||path==stage)throw new InvalidDataException("Output path escapes staging.");
            string directory=Path.GetDirectoryName(path)!;
            var missing=new Stack<string>();
            while(!Directory.Exists(directory)){missing.Push(directory);directory=Path.GetDirectoryName(directory)!;}
            while(missing.Count>0){string d=missing.Pop();Directory.CreateDirectory(d);directories.Add(d);}
            if(File.Exists(path) && !files.Contains(path))throw new InvalidDataException("Output file collision.");
            files.Add(path);return path;
        }
        try
        {
            if(inPlace)
            {
                ownsLock=!File.Exists(lockFile);
                if(File.Exists(lockFile))AssetSet.RejectLinks(lockFile);
                writeLock=new FileStream(lockFile,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                if(!AssetSet.ReadBounded(currentFile,16*1024*1024).AsSpan().SequenceEqual(assets.CatalogBytes.Span))throw new InvalidDataException("The catalog changed on disk. Reopen the project before saving; edits remain unsaved.");
            }
            Directory.CreateDirectory(stage);checkpoint?.Invoke("stage-created");
            phase="Writing native definitions";progress?.Invoke(phase,0,1);
            // Retain root-level unknown protobuf fields byte-for-byte, including ordering.
            using var appearanceBytes=new MemoryStream();
            var emitted=new HashSet<(string,uint)>();var reader=new Proto(assets.AppearanceBytes);
            var byId=assets.Appearances.ToDictionary(a=>(a.Category,a.Id));
            while(!reader.End)
            {
                int start=reader.Position;var field=reader.Next();
                if(field.Number is <1 or >4){appearanceBytes.Write(assets.AppearanceBytes.Span.Slice(start,reader.Position-start));continue;}
                string category=AssetSet.Categories[field.Number-1];uint id=Proto.UInt(Proto.Fields(Proto.Bytes(field)).Single(f=>f.Number==1));
                if(byId.TryGetValue((category,id),out var a)){
                    if(a.Source.Span.SequenceEqual(Proto.Bytes(field).Span))appearanceBytes.Write(assets.AppearanceBytes.Span.Slice(start,reader.Position-start));
                    else appearanceBytes.Write(NativeWorkspace.Blob(field.Number,a.Source));emitted.Add((category,id));}
            }
            foreach(var a in assets.Appearances.Where(a=>!emitted.Contains((a.Category,a.Id))))
                appearanceBytes.Write(NativeWorkspace.Blob(Array.IndexOf(AssetSet.Categories,a.Category)+1,a.Source));
            if(appearanceBytes.Length>128*1024*1024)throw new InvalidDataException("Compiled appearances exceed 128 MiB.");
            byte[] appearance=appearanceBytes.ToArray();string appearanceFile=appearance.AsSpan().SequenceEqual(assets.AppearanceBytes.Span)
                ? catalog.Single(n=>n?["type"]?.GetValue<string>()=="appearances")!["file"]!.GetValue<string>()
                : "appearances-"+Convert.ToHexString(SHA256.HashData(appearance)).ToLowerInvariant()+".dat";
            currentFile=appearanceFile;WritePayload(appearanceFile,appearance);
            foreach(var entry in catalog)
            {
                if(entry is not JsonObject obj)throw new InvalidDataException("Invalid catalog entry.");
                string type=obj["type"]!.GetValue<string>();
                if(type=="sprite")continue;
                var copy=obj.DeepClone().AsObject();
                if(type=="appearances")copy["file"]=appearanceFile;
                else if(copy["file"] is JsonValue file){string relative=file.GetValue<string>();currentFile=relative;string source=assets.Input(relative);if(!inPlace){string path=Destination(relative);if(!File.Exists(path))File.Copy(source,path,false);}}
                entries.Add(copy);
            }
            int done=0;
            foreach(var sheet in assets.Sheets)
            {
                bool modified=changed.Any(id=>id>=sheet.First && id<=sheet.Last);
                if(!modified)
                {
                    var entry=originalSheets[sheet.First]!.DeepClone();currentFile=sheet.File;string source=assets.Input(sheet.File);
                    if(!inPlace){string path=Destination(sheet.File);if(!File.Exists(path))File.Copy(source,path,false);}entries.Add(entry);
                }
                else
                {
                    // Split ranges when an edited sprite changed tile dimensions; never renumber IDs.
                    uint first=sheet.First;
                    while(first<=sheet.Last)
                    {
                        var shape=assets.FindSheet(first);int capacity=(384/shape.Width)*(384/shape.Height);uint last=first;
                        while(last<sheet.Last && (ulong)last-first+1<(uint)capacity)
                        {var next=assets.FindSheet(last+1);if(next.Type!=shape.Type)break;last++;}
                        var image=new Pixels(384,384,new byte[384*384*4]);
                        for(uint id=first;;id++)
                        {
                            var tile=cache.Sprite(id);int offset=checked((int)(id-first)),x=offset%(384/shape.Width)*shape.Width,y=offset/(384/shape.Width)*shape.Height;
                            for(int row=0;row<tile.Height;row++)tile.Rgba.AsSpan(row*tile.Width*4,tile.Width*4).CopyTo(image.Rgba.AsSpan(((y+row)*384+x)*4));
                            if(id==last)break;
                        }
                        phase="Compiling native sprites "+first+"–"+last;
                        byte[] encoded=EncodeSheet(image);string name="sprites-"+Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant()+".bmp.lzma";
                        currentFile=name;WritePayload(name,encoded);
                        var entry=originalSheets.TryGetValue(sheet.First,out var previous) ? previous!.DeepClone().AsObject() : new JsonObject{["type"]="sprite"};
                        entry["file"]=name;entry["spritetype"]=shape.Type;entry["firstspriteid"]=first;entry["lastspriteid"]=last;entries.Add(entry);
                        if(last==uint.MaxValue)break;first=last+1;
                    }
                }
                progress?.Invoke("Saving native spritesheets",++done,assets.Sheets.Count);
            }
            byte[] catalogOutput=appearance.AsSpan().SequenceEqual(assets.AppearanceBytes.Span) && changed.Count==0 ? assets.CatalogBytes.ToArray() :
                System.Text.Encoding.UTF8.GetBytes(new JsonArray(entries.ToArray()).ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
            currentFile=Destination("catalog-content.json");DurableWrite(currentFile,catalogOutput);
            phase="Validating native assets";progress?.Invoke(phase,0,1);
            var reopened=new AssetSet(stage,fallbackRoot:inPlace ? assets.Root : null);Validate(reopened);
            checkpoint?.Invoke("validated");
            if(inPlace)
            {
                phase="Publishing native payloads";
                foreach(string file in files.Where(f=>Path.GetFileName(f)!="catalog-content.json"))
                {
                    string final=Path.Combine(target,Path.GetRelativePath(stage,file));currentFile=final;
                    if(File.Exists(final))throw new InvalidDataException("New native payload path already exists: "+final);
                    File.Move(file,final);published.Add(final);
                }
                checkpoint?.Invoke("payloads-published");
                phase="Replacing native catalog";currentFile=Path.Combine(target,"catalog-content.json");
                if(!AssetSet.ReadBounded(currentFile,16*1024*1024).AsSpan().SequenceEqual(assets.CatalogBytes.Span))throw new InvalidDataException("The catalog changed while saving. Reopen the project; nothing was committed.");
                if(!catalogOutput.AsSpan().SequenceEqual(assets.CatalogBytes.Span))
                {
                    backup=Path.Combine(target,".nexa-backup-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".catalog.json");
                    File.Replace(Path.Combine(stage,"catalog-content.json"),currentFile,backup);committed=true;
                }
                checkpoint?.Invoke("catalog-published");
                phase="Checking committed native assets";var saved=new AssetSet(target);Validate(saved);
                // Move the live model/cache baseline forward only after publication and validation.
                assets.AcceptSaved(saved);cache.AcceptSaved();CleanupStage();
            }
            else{Directory.Move(stage,target);}
            progress?.Invoke("Native assets saved",1,1);return target;
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {
            bool restored=!committed;
            if(committed && backup!=null)try{File.Replace(backup,Path.Combine(target,"catalog-content.json"),null);restored=true;}catch(IOException){}catch(UnauthorizedAccessException){}
            if(restored)foreach(string file in published)try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
            CleanupStage();
            throw new InvalidDataException($"Native save failed during {phase} ({currentFile}): {error.Message}"+(restored ? (committed ? " The previous catalog was restored; edits are unsaved." : " No catalog update was committed; edits are unsaved.") : " Rollback failed. Restore the catalog backup: "+backup),error);
        }
        finally{writeLock?.Dispose();if(ownsLock)try{File.Delete(lockFile);}catch(IOException){}catch(UnauthorizedAccessException){}}
        void WritePayload(string name,byte[] bytes)
        {
            if(inPlace)
            {
                string existing=Path.Combine(target,name);
                if(File.Exists(existing)){AssetSet.RejectLinks(existing);if(!AssetSet.ReadBounded(existing,Math.Max(bytes.Length,1)).AsSpan().SequenceEqual(bytes))throw new InvalidDataException("Existing content-addressed file has different bytes: "+existing);return;}
            }
            string file=Destination(name);if(!File.Exists(file))DurableWrite(file,bytes);checkpoint?.Invoke("payload-written");
        }
        void Validate(AssetSet saved)
        {
            if(saved.Appearances.Count!=assets.Appearances.Count)throw new InvalidDataException("Native object count changed during publication.");
            foreach(var a in assets.Appearances){var other=saved.Find(a.Category,a.Id);if(!a.Source.Span.SequenceEqual(other.Source.Span))throw new InvalidDataException("Native definition changed during publication: "+a.Category+"/"+a.Id);foreach(var g in AssetSet.Groups(other))foreach(uint id in g.Sprites)saved.FindSheet(id);}
            var decoded=new SheetCache(saved);
            foreach(uint id in changed){var before=cache.Sprite(id);var after=decoded.Sprite(id);if(before.Width!=after.Width||before.Height!=after.Height||!before.Rgba.AsSpan().SequenceEqual(after.Rgba))throw new InvalidDataException("Native sprite round-trip mismatch: "+id);}
        }
        void CleanupStage()
        {
            foreach(string file in files)try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
            foreach(string directory in directories.OrderByDescending(d=>d.Length))try{Directory.Delete(directory,false);}catch(IOException){}catch(UnauthorizedAccessException){}
            try{Directory.Delete(stage,false);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private static void DurableWrite(string file,byte[] bytes)
    {using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough);stream.Write(bytes);stream.Flush(true);}
    public static byte[] EncodeSheet(Pixels image)
    {
        if(image.Width!=384||image.Height!=384||image.Rgba.Length!=384*384*4)throw new InvalidDataException("Expected a 384x384 native sheet.");
        byte[] bmp=new byte[122+image.Rgba.Length];bmp[0]=(byte)'B';bmp[1]=(byte)'M';
        void I(int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(offset),value);
        I(2,bmp.Length);I(10,122);I(14,108);I(18,384);I(22,384);bmp[26]=1;bmp[28]=32;I(30,3);I(34,image.Rgba.Length);
        I(54,0xFF0000);I(58,0xFF00);I(62,0xFF);I(66,unchecked((int)0xFF000000));I(70,0x73524742);
        for(int y=0;y<384;y++)for(int x=0;x<384;x++)
        {
            int src=(y*384+x)*4,dst=122+((383-y)*384+x)*4;
            if(image.Rgba[src]==255&&image.Rgba[src+1]==0&&image.Rgba[src+2]==255&&image.Rgba[src+3]!=0)throw new InvalidDataException("Native CIP sheets reserve magenta for transparency; an opaque magenta pixel cannot be encoded losslessly.");
            bmp[dst]=image.Rgba[src+2];bmp[dst+1]=image.Rgba[src+1];bmp[dst+2]=image.Rgba[src];bmp[dst+3]=image.Rgba[src+3];
        }
        using var compressed=new MemoryStream();byte[] properties;
        using(var encoder=new LzmaStream(new LzmaEncoderProperties(true,1<<20),false,compressed)){properties=encoder.Properties.ToArray();encoder.Write(bmp);}
        byte[] length=new byte[8];BinaryPrimitives.WriteInt64LittleEndian(length,bmp.Length);
        byte[] payload=properties.Concat(length).Concat(compressed.ToArray()).ToArray();
        byte[] header=new byte[]{0x70,0x0A,0xFA,0x80,0x24}.Concat(NativeWorkspace.Scalar(1,(ulong)payload.Length).Skip(1)).ToArray();
        return new byte[32-header.Length].Concat(header).Concat(payload).ToArray();
    }
}
