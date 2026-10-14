using System.Text;
using System.Text.Json;

namespace NexaModern;

public static class Program
{
    public const int Protocol = 1;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static void Emit(object value) { Console.WriteLine(JsonSerializer.Serialize(value, Json)); Console.Out.Flush(); }
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--version") { Console.WriteLine("nexa-modern-helper 0.5.0 protocol=1 native-save legacy-export"); return 0; }
        if (args.Length != 4 || args[0] != "--input" || args[2] != "--output")
        { Console.Error.WriteLine("Usage: nexa-modern-helper --input assets-folder --output existing-empty-session-folder"); return 2; }
        try
        {
            string root = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
            if (!Directory.Exists(output) || Directory.EnumerateFileSystemEntries(output).Any()) throw new InvalidDataException("Session output must be an existing empty folder.");
            AssetSet.RejectLinks(output);
            if (Within(root, output) || Within(output, root)) throw new InvalidDataException("Source and output directories must be disjoint.");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var assets = new AssetSet(root, (processed, total) => Emit(new { type = "progress", schemaVersion = Protocol, phase = "catalog", processed, total }));
            var cache = new SheetCache(assets);
            var native = new NativeAssets(assets,cache,output);
            var workspace = new NativeWorkspace(assets,cache);
            long conversionRevision = -1;
            LegacyExport? conversion = null; string? conversionToken = null;
            Emit(new { type = "ready", schemaVersion = Protocol, fingerprint = assets.Fingerprint, sheets = assets.Sheets.Count,
                categories = AssetSet.Categories.ToDictionary(c => c, c => assets.Appearances.Count(a => a.Category == c)), native = native.Inventory(), elapsedMs = watch.ElapsedMilliseconds });
            while (ReadLine() is string line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string? requestId = null; long generation = 0;
                try
                {
                    using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 16 }); var request = document.RootElement;
                    requestId = request.GetProperty("requestId").GetString(); generation = request.GetProperty("generation").GetInt64();
                    if (string.IsNullOrEmpty(requestId) || requestId.Length > 64) throw new InvalidDataException("Invalid request ID.");
                    string command = request.GetProperty("command").GetString() ?? "";
                    if (command == "quit") return 0;
                    object result;
                    long previousRevision=workspace.Revision;
                    switch (command)
                    {
                        case "native-save":
                            result=new {directory=NativeSave.InPlace(assets,cache,(phase,processed,total)=>Emit(new {type="progress",schemaVersion=Protocol,requestId,generation,phase,processed,total})),revision=workspace.Revision};break;
                        case "native-save-as":
                            string nativeTarget=Path.GetFullPath(request.GetProperty("target").GetString()??"");
                            if(NativeSave.Within(output,nativeTarget)||NativeSave.Within(nativeTarget,output))throw new InvalidDataException("Persistent save must be outside the preview session.");
                            NativeSave.Export(assets,cache,nativeTarget,(phase,processed,total)=>Emit(new {type="progress",schemaVersion=Protocol,requestId,generation,phase,processed,total}));
                            assets.AcceptSaved(new AssetSet(nativeTarget));cache.AcceptSaved();
                            result=new {directory=nativeTarget,revision=workspace.Revision};break;
                        case "native-duplicate":result=workspace.Duplicate(request.GetProperty("category").GetString()??"",Ids(request));break;
                        case "native-add":result=workspace.Add(request.GetProperty("category").GetString()??"");break;
                        case "native-remove":result=workspace.Remove(request.GetProperty("category").GetString()??"",Ids(request));break;
                        case "native-copy":result=workspace.Copy(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32());break;
                        case "native-paste":result=workspace.Paste(request.GetProperty("category").GetString()??"",Ids(request),request.GetProperty("deleteAfterPaste").GetBoolean());break;
                        case "native-flag":result=workspace.Flag(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32(),Get(request,"number"),request.GetProperty("enabled").GetBoolean());break;
                        case "native-edit-flag":result=workspace.EditFlag(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32(),Get(request,"number"),request.GetProperty("enabled").GetBoolean(),request.GetProperty("instances"));break;
                        case "native-sprite-set":result=workspace.Sprite(request.TryGetProperty("spriteId",out var spriteId) && spriteId.ValueKind!=JsonValueKind.Null ? spriteId.GetUInt32() : null,Get(request,"width"),Get(request,"height"),request.GetProperty("argb").GetString()??"");break;
                        case "native-sprite-clear":result=workspace.ClearSprite(request.GetProperty("spriteId").GetUInt32());break;
                        case "native-sprite-duplicate":result=workspace.DuplicateSprite(request.GetProperty("spriteId").GetUInt32());break;
                        case "native-link-sprite":result=workspace.LinkSprite(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32(),Get(request,"group"),Get(request,"frame"),Get(request,"patternX"),Get(request,"patternY"),Get(request,"patternZ"),Get(request,"layer"),request.GetProperty("spriteId").GetUInt32());break;
                        case "native-export-object":result=workspace.ExportObject(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32(),request.GetProperty("file").GetString()??"");break;
                        case "native-import-object":result=workspace.ImportObject(request.GetProperty("category").GetString()??"",request.GetProperty("file").GetString()??"",request.TryGetProperty("target",out var objectTarget) && objectTarget.ValueKind!=JsonValueKind.Null ? objectTarget.GetUInt32() : null);break;
                        case "native-import-image-object":result=workspace.ImportPngObject(request.GetProperty("category").GetString()??"",Get(request,"width"),Get(request,"height"),request.GetProperty("argb").GetString()??"");break;
                        case "native-import-png":result=workspace.ImportPng(request.GetProperty("file").GetString()??"",request.TryGetProperty("spriteId",out var pngSprite) && pngSprite.ValueKind!=JsonValueKind.Null ? pngSprite.GetUInt32() : null,request.TryGetProperty("asObject",out var pngObject) && pngObject.GetBoolean() ? request.GetProperty("category").GetString() : null);break;
                        case "native-export-png":result=workspace.ExportPng(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32(),request.GetProperty("file").GetString()??"",request.GetProperty("sprite").GetBoolean(),Get(request,"group"),Get(request,"frame"),Get(request,"patternX"),Get(request,"patternY"),Get(request,"patternZ"));break;
                        case "native-export-batch":result=workspace.ExportBatch(request.GetProperty("category").GetString()??"",Ids(request),request.GetProperty("target").GetString()??"",request.GetProperty("sprite").GetBoolean(),request.GetProperty("package").GetBoolean());break;
                        case "native-page": result=native.Objects(request.GetProperty("category").GetString()??"",request.GetProperty("target").GetUInt32(),request.GetProperty("query").GetString()??"",request.GetProperty("hideEmpty").GetBoolean());break;
                        case "native-sprites":result=native.Sprites(request.GetProperty("target").GetUInt32());break;
                        case "native-detail":result=native.Detail(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32());break;
                        case "native-object-export":result=native.ObjectExport(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32(),Get(request,"version"),request.GetProperty("datSignature").GetUInt32(),request.GetProperty("sprSignature").GetUInt32());break;
                        case "native-frame":result=native.Frame(request.GetProperty("category").GetString()??"",request.GetProperty("appearanceId").GetUInt32(),Get(request,"group"),Get(request,"frame"),Get(request,"patternX"),Get(request,"patternY"),Get(request,"patternZ"),request.GetProperty("layer").GetInt32(),request.GetProperty("grid").GetBoolean());break;
                        case "preflight":
                            conversion = null; conversionToken = null;
                            IEnumerable<Appearance>? selection = null;
                            if (request.TryGetProperty("selection", out var selected))
                            {
                                if (selected.ValueKind != JsonValueKind.Array || selected.GetArrayLength() is < 1 or > 100)
                                    throw new InvalidDataException("Select 1–100 appearances, or convert the entire asset set.");
                                string selectedCategory = request.GetProperty("category").GetString() ?? "";
                                selection = selected.EnumerateArray().Select(id => assets.Find(selectedCategory, id.GetUInt32())).ToArray();
                            }
                            conversion = new LegacyExport(assets, request.GetProperty("version").GetInt32(), request.GetProperty("transparency").GetBoolean(), selection,
                                (processed, total) => Emit(new { type = "progress", schemaVersion = Protocol, requestId, generation, phase = "Conversion preflight", processed, total }),
                                request.TryGetProperty("datSignature",out var datTarget) ? datTarget.GetUInt32() : null,request.TryGetProperty("sprSignature",out var sprTarget) ? sprTarget.GetUInt32() : null);
                            conversionToken = Guid.NewGuid().ToString("N");
                            conversionRevision = workspace.Revision;
                            result = new { token = conversionToken, summary = conversion.Summary() };
                            break;
                        case "export":
                            if (conversion == null || conversionToken != request.GetProperty("token").GetString() || conversionRevision!=workspace.Revision)
                                throw new InvalidDataException("Conversion options changed; review a new preflight.");
                            string target = request.GetProperty("target").GetString() ?? "";
                            if (Within(output, Path.GetFullPath(target)) || Within(Path.GetFullPath(target), output))
                                throw new InvalidDataException("Persistent export must be outside the preview session.");
                            string exported = conversion.Export(target, cache, request.GetProperty("acceptLosses").GetBoolean(),
                                (phase, processed, total) => Emit(new { type = "progress", schemaVersion = Protocol, requestId, generation, phase, processed, total }));
                            result = new { directory = exported, summary = conversion.Summary(), report = Path.Combine(exported, "conversion-report.json") };
                            break;
                        case "list":
                            string category = request.GetProperty("category").GetString() ?? "";
                            if (!AssetSet.Categories.Contains(category)) throw new InvalidDataException("Invalid category.");
                            string query = request.GetProperty("query").GetString() ?? "";
                            int page = request.GetProperty("page").GetInt32();
                            if (query.Length > 256 || page is < 0 or > 100000) throw new InvalidDataException("Invalid page/query.");
                            var found = assets.Appearances.Where(a => a.Category == category && (query.Length == 0 ||
                                a.Id.ToString().Contains(query, StringComparison.Ordinal) || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
                            result = new { total = found.Length, page, pageSize = 100,
                                items = found.Skip(checked(page * 100)).Take(100).Select(a => new { a.Id, a.Name, a.Groups, label = a.Id + (a.Name.Length == 0 ? "" : " — " + a.Name) }) };
                            break;
                        case "detail":
                        case "render":
                            var appearance = assets.Find(request.GetProperty("category").GetString() ?? "", request.GetProperty("appearanceId").GetUInt32());
                            var groups = AssetSet.Groups(appearance);
                            if (command == "detail")
                            {
                                var fields = Proto.Fields(appearance.Source);
                                result = new { appearance.Id, appearance.Name, appearance.Category,
                                    groups = groups.Select(g => new { g.Type, g.PatternX, g.PatternY, g.PatternZ, g.Layers, g.Frames, spriteCount = g.Sprites.Length, g.Phases, g.UnknownInfoFields }),
                                    flags = fields.Where(f => f.Number == 3).SelectMany(f => Proto.Fields(Proto.Bytes(f))).Select(f => new { field = f.Number, wire = f.Wire, value = f.Wire == 0 ? f.Scalar.ToString() : "message (" + f.Data.Length + " bytes)" }),
                                    unknownAppearanceFields = fields.Where(f => f.Number is < 1 or > 6).Select(f => f.Number).Distinct(),
                                    notice = "Read-only preview. Legacy export maps supported fields by target profile and reports unsupported metadata; source files remain untouched." };
                            }
                            else
                            {
                                int group = Get(request, "group");
                                if (group < 0 || group >= groups.Length) throw new InvalidDataException("Invalid frame group.");
                                var image = cache.Render(groups[group], Get(request, "frame"), Get(request, "patternX"), Get(request, "patternY"), Get(request, "patternZ"));
                                string file = Path.Combine(output, "preview-" + (Get(request, "slot") & 1) + ".png");
                                Png.Write(file, image); result = new { file, image.Width, image.Height, cacheBytes = cache.Bytes, cacheHits = cache.Hits, cacheMisses = cache.Misses };
                            }
                            break;
                        case "stats": result = new { cacheBytes = cache.Bytes, cacheHits = cache.Hits, cacheMisses = cache.Misses }; break;
                        case "validate":
                            int valid = 0; var errors = new List<object>();
                            foreach (var a in assets.Appearances)
                            {
                                try { foreach (var g in AssetSet.Groups(a)) foreach (uint sprite in g.Sprites) assets.FindSheet(sprite); valid++; }
                                catch (Exception error) when (error is InvalidDataException or OverflowException) { if (errors.Count < 100) errors.Add(new { a.Category, a.Id, error = error.Message }); }
                            }
                            result = new { valid, total = assets.Appearances.Count, errors, errorListTruncated = assets.Appearances.Count - valid > errors.Count }; break;
                        default: throw new InvalidDataException("Unsupported modern command.");
                    }
                    Emit(new { type = "result", schemaVersion = Protocol, requestId, generation, command, result, native = previousRevision!=workspace.Revision ? native.Inventory() : null, revision = workspace.Revision });
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Emit(new { type = "error", schemaVersion = Protocol, requestId, generation, errorCode = "INVALID_ASSET_OR_REQUEST", message = error.Message }); }
            }
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Emit(new { type = "error", schemaVersion = Protocol, errorCode = "OPEN_FAILED", message = error.Message }); return 1; }
    }
    private static int Get(JsonElement request, string field) => request.TryGetProperty(field, out var value) ? value.GetInt32() : 0;
    private static uint[] Ids(JsonElement request) {var ids=request.GetProperty("ids");if(ids.ValueKind!=JsonValueKind.Array || ids.GetArrayLength() is <1 or >100)throw new InvalidDataException("Select 1–100 objects.");return ids.EnumerateArray().Select(v=>v.GetUInt32()).ToArray();}
    private static bool Within(string parent, string child) => string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string? ReadLine()
    {
        // ReadLine() alone can allocate an arbitrarily large string from hostile stdin.
        var text = new StringBuilder(); int ch;
        while ((ch = Console.In.Read()) >= 0)
        {
            if (ch == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length == 65536) throw new InvalidDataException("Request exceeds 64 KiB.");
            text.Append((char)ch);
        }
        return text.Length == 0 ? null : text.ToString();
    }
}
