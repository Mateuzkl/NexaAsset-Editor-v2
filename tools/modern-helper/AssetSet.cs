using System.Security.Cryptography;
using System.Text.Json;

namespace NexaModern;

public sealed record Sheet(string File, int Type, uint First, uint Last)
{
    public int Width => Type is 0 or 1 ? 32 : 64;
    public int Height => Type is 0 or 2 ? 32 : 64;
}
public sealed record Appearance(uint Id, string Category, string Name, int Groups, ReadOnlyMemory<byte> Source);
public sealed record Phase(uint? Min, uint? Max);
public sealed record Group(uint Type, uint PatternX, uint PatternY, uint PatternZ, uint Layers,
    uint[] Sprites, Phase[] Phases, int[] UnknownInfoFields)
{
    public int Frames => Math.Max(1, Phases.Length);
}

public sealed class AssetSet
{
    public string Root { get; private set; }
    public string Fingerprint { get; private set; }
    public ReadOnlyMemory<byte> AppearanceBytes { get; private set; }
    public ReadOnlyMemory<byte> CatalogBytes { get; private set; }
    private readonly string? fallbackRoot;
    public IReadOnlyList<Sheet> Sheets => sheets;
    public IReadOnlyList<Appearance> Appearances => appearances;
    private readonly List<Sheet> sheets = [];
    private readonly List<Appearance> appearances = [];
    private readonly Dictionary<(string, uint), Appearance> lookup = [];
    private readonly Dictionary<uint, Sheet> changedSpriteShapes = [];
    private readonly Dictionary<(string,uint),int> changedAppearanceSizes=[];
    private long changedAppearanceBytes;
    public void ValidateAppearanceEdits(IReadOnlyList<Appearance> edits)
    {
        long bytes=changedAppearanceBytes; int added=0;
        var keys=new HashSet<(string,uint)>();
        foreach(var appearance in edits)
        {
            var key=(appearance.Category,appearance.Id);
            if(!Categories.Contains(appearance.Category) || appearance.Id<(appearance.Category=="item" ? 100u : 1u) || !keys.Add(key))
                throw new InvalidDataException("Invalid working-copy category/ID or duplicate edit.");
            if(!lookup.ContainsKey(key))added++;
            bytes+=appearance.Source.Length-changedAppearanceSizes.GetValueOrDefault(key);
        }
        if(appearances.Count+added>200000)throw new InvalidDataException("Working-copy record limit.");
        if(bytes>32*1024*1024)throw new InvalidDataException("Working-copy metadata edits exceed 32 MiB; export and reopen a project before continuing.");
    }
    public void SetAppearance(Appearance appearance)=>SetAppearances([appearance]);
    public void SetAppearances(IReadOnlyList<Appearance> edits)
    {
        ValidateAppearanceEdits(edits);
        foreach(var appearance in edits) foreach(var group in Groups(appearance)) foreach(uint id in group.Sprites) FindSheet(id);
        foreach(var appearance in edits)
        {
            int previousSize=changedAppearanceSizes.GetValueOrDefault((appearance.Category,appearance.Id));
            if(lookup.TryGetValue((appearance.Category,appearance.Id),out var old)) appearances.Remove(old);
            lookup[(appearance.Category,appearance.Id)]=appearance;appearances.Add(appearance);
            changedAppearanceBytes+=appearance.Source.Length-previousSize;changedAppearanceSizes[(appearance.Category,appearance.Id)]=appearance.Source.Length;
        }
        appearances.Sort((a,b)=>a.Category==b.Category ? a.Id.CompareTo(b.Id) : Array.IndexOf(Categories,a.Category).CompareTo(Array.IndexOf(Categories,b.Category)));
    }
    public void RemoveAppearances(string category,uint[] ids)
    {
        foreach(uint id in ids) Find(category,id); // Validate the whole batch before mutation.
        foreach(uint id in ids.Distinct()){var old=lookup[(category,id)];lookup.Remove((category,id));appearances.Remove(old);changedAppearanceBytes-=changedAppearanceSizes.GetValueOrDefault((category,id));changedAppearanceSizes.Remove((category,id));}
    }
    public void SetSpriteShape(uint id,int width,int height,bool add)
    {
        if (width is not (32 or 64) || height is not (32 or 64)) throw new InvalidDataException("Native sprites must be 32/64 pixels per axis.");
        var shape=new Sheet("session-sprite-"+id,(width==64 ? 2 : 0)+(height==64 ? 1 : 0),id,id);
        if(add){if(id<=sheets[^1].Last)throw new InvalidDataException("New sprite ID is already allocated.");sheets.Add(shape);}
        changedSpriteShapes[id]=shape;
    }
    public static readonly string[] Categories = ["item", "outfit", "effect", "missile"];
    internal void AcceptSaved(AssetSet saved)
    {
        Root=saved.Root;Fingerprint=saved.Fingerprint;AppearanceBytes=saved.AppearanceBytes;CatalogBytes=saved.CatalogBytes;
        sheets.Clear();sheets.AddRange(saved.sheets);appearances.Clear();appearances.AddRange(saved.appearances);
        lookup.Clear();foreach(var pair in saved.lookup)lookup.Add(pair.Key,pair.Value);
        changedSpriteShapes.Clear();changedAppearanceSizes.Clear();changedAppearanceBytes=0;
    }
    public AssetSet(string root, Action<int, int>? progress = null, string? fallbackRoot=null)
    {
        Root = Path.GetFullPath(root);
        this.fallbackRoot=fallbackRoot==null ? null : Path.GetFullPath(fallbackRoot);
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException("Assets folder not found.");
        RejectLinks(Root);
        var catalogBytes = ReadBounded(Input("catalog-content.json"), 16 * 1024 * 1024);
        CatalogBytes = catalogBytes;
        using var catalog = JsonDocument.Parse(catalogBytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (catalog.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Catalog must be an array.");
        string? appearanceFile = null;
        int processed = 0, total = catalog.RootElement.GetArrayLength();
        if (total > 100000) throw new InvalidDataException("Catalog too large.");
        foreach (var entry in catalog.RootElement.EnumerateArray())
        {
            string type = entry.GetProperty("type").GetString() ?? throw new InvalidDataException("Missing catalog type.");
            if (type is "appearances" or "sprite")
            {
                string file = entry.GetProperty("file").GetString() ?? throw new InvalidDataException("Missing catalog file.");
                Input(file); // Check existence and containment before announcing readiness.
                if (type == "appearances")
                {
                    if (appearanceFile != null) throw new InvalidDataException("Multiple appearances entries.");
                    appearanceFile = file;
                }
                else
                {
                    int spriteType = entry.GetProperty("spritetype").GetInt32();
                    uint first = entry.GetProperty("firstspriteid").GetUInt32(), last = entry.GetProperty("lastspriteid").GetUInt32();
                    if (spriteType is < 0 or > 3 || last < first) throw new InvalidDataException("Invalid sprite range/type.");
                    var sheet = new Sheet(file, spriteType, first, last);
                    if ((ulong)last - first + 1 > (ulong)((384 / sheet.Width) * (384 / sheet.Height)))
                        throw new InvalidDataException("Sprite range exceeds sheet capacity.");
                    sheets.Add(sheet);
                }
            }
            if (++processed % 128 == 0) progress?.Invoke(processed, total);
        }
        if (appearanceFile == null || sheets.Count == 0) throw new InvalidDataException("Explicit appearances and sprite entries are required.");
        sheets.Sort((a, b) => a.First.CompareTo(b.First));
        for (int i = 1; i < sheets.Count; i++)
            if (sheets[i].First <= sheets[i - 1].Last) throw new InvalidDataException("Overlapping sprite ranges.");
        var bytes = ReadBounded(Input(appearanceFile), 128 * 1024 * 1024);
        AppearanceBytes = bytes;
        Fingerprint = Convert.ToHexString(SHA256.HashData(catalogBytes.Concat(bytes).ToArray()));
        var reader = new Proto(bytes);
        while (!reader.End)
        {
            var top = reader.Next();
            if (top.Number is < 1 or > 4) continue; // original bytes retained by each source slice
            var source = Proto.Bytes(top);
            var fields = Proto.Fields(source);
            var idFields = fields.Where(f => f.Number == 1).ToArray();
            if (idFields.Length != 1) throw new InvalidDataException("Appearance needs exactly one ID.");
            uint id = Proto.UInt(idFields[0]);
            string category = Categories[top.Number - 1];
            string name = fields.Where(f => f.Number == 4).Select(Proto.Text).LastOrDefault() ?? "";
            var appearance = new Appearance(id, category, name, fields.Count(f => f.Number == 2), source);
            if (!lookup.TryAdd((category, id), appearance)) throw new InvalidDataException($"Duplicate {category} ID {id}.");
            if (appearances.Count >= 1000000) throw new InvalidDataException("Too many appearances.");
            appearances.Add(appearance);
        }
        appearances.Sort((a, b) => a.Id.CompareTo(b.Id));
        progress?.Invoke(total, total);
    }
    public string Input(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("Invalid catalog path.");
        string full = Path.GetFullPath(Path.Combine(Root, relative));
        if (!full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog path escapes asset root.");
        if(!File.Exists(full) && fallbackRoot!=null)
        {
            full=Path.GetFullPath(Path.Combine(fallbackRoot,relative));
            if(!full.StartsWith(fallbackRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Fallback asset path escapes root.");
        }
        RejectLinks(full);
        if (!File.Exists(full)) throw new FileNotFoundException("Catalog asset missing.", relative);
        return full;
    }
    public static void RejectLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current != null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Junctions and symbolic links are not accepted for asset I/O.");
            current = Path.GetDirectoryName(current);
        }
    }
    public static byte[] ReadBounded(string file, int limit)
    {
        using var stream = File.OpenRead(file);
        if (stream.Length > limit) throw new InvalidDataException("Input file exceeds limit.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    public Appearance Find(string category, uint id) => lookup.TryGetValue((category, id), out var result)
        ? result : throw new InvalidDataException("Appearance not found.");
    public Sheet FindSheet(uint id)
    {
        if(changedSpriteShapes.TryGetValue(id,out var changed))return changed;
        int lo = 0, hi = sheets.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2; var sheet = sheets[mid];
            if (id < sheet.First) hi = mid - 1;
            else if (id > sheet.Last) lo = mid + 1;
            else return sheet;
        }
        throw new InvalidDataException($"Missing sprite {id}; no placeholder is substituted.");
    }
    public static Group[] Groups(Appearance appearance)
    {
        var result = new List<Group>();
        foreach (var frame in Proto.Fields(appearance.Source).Where(f => f.Number == 2))
        {
            if (result.Count >= 16) throw new InvalidDataException("Too many frame groups.");
            var fields = Proto.Fields(Proto.Bytes(frame));
            uint type = fields.Where(f => f.Number == 1).Select(Proto.UInt).LastOrDefault();
            var info = fields.Where(f => f.Number == 3).ToArray();
            if (info.Length != 1) throw new InvalidDataException("Missing/duplicate sprite info.");
            var spriteFields = Proto.Fields(Proto.Bytes(info[0]));
            uint dimension(int n) => spriteFields.Where(f => f.Number == n).Select(Proto.UInt).DefaultIfEmpty(1u).Last();
            uint x = dimension(1), y = dimension(2), z = dimension(3), layers = dimension(4);
            if (new[] { x, y, z, layers }.Any(n => n is 0 or > 256)) throw new InvalidDataException("Invalid sprite patterns/layers.");
            var ids = new List<uint>();
            foreach (var f in spriteFields.Where(f => f.Number == 5))
            {
                if (f.Wire == 0) ids.Add(Proto.UInt(f));
                else
                {
                    var data = Proto.Bytes(f).Span; int offset = 0;
                    while (offset < data.Length)
                    {
                        ulong value = Proto.Varint(data, ref offset);
                        if (value > uint.MaxValue) throw new InvalidDataException("Sprite ID overflow.");
                        ids.Add((uint)value);
                        if (ids.Count > 65536) throw new InvalidDataException("Too many sprite references.");
                    }
                }
            }
            var phases = new List<Phase>();
            foreach (var animation in spriteFields.Where(f => f.Number == 6))
            foreach (var phase in Proto.Fields(Proto.Bytes(animation)).Where(f => f.Number == 6))
            {
                var durations = Proto.Fields(Proto.Bytes(phase));
                // Absence is not silently presented as an explicit zero.
                var min = durations.Where(f => f.Number == 1).ToArray();
                var max = durations.Where(f => f.Number == 2).ToArray();
                if (min.Length > 1 || max.Length > 1) throw new InvalidDataException("Duplicate animation duration.");
                uint? a = min.Length == 0 ? null : Proto.UInt(min[0]), b = max.Length == 0 ? null : Proto.UInt(max[0]);
                if (a > b || a > 3600000 || b > 3600000 || phases.Count == 256) throw new InvalidDataException("Invalid animation duration/count.");
                phases.Add(new Phase(a, b));
            }
            ulong expected = checked((ulong)x * y * z * layers * (uint)Math.Max(1, phases.Count));
            if (expected > 65536 || expected != (ulong)ids.Count) throw new InvalidDataException($"Sprite count mismatch ({ids.Count}/{expected}).");
            result.Add(new Group(type, x, y, z, layers, ids.ToArray(), phases.ToArray(),
                spriteFields.Where(f => f.Number is < 1 or > 9).Select(f => f.Number).Distinct().ToArray()));
        }
        if (result.Count == 0) throw new InvalidDataException("Appearance has no frame groups.");
        return result.ToArray();
    }
}
