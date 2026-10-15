using System.Text;
using System.Text.Json;

namespace NexaModern;

public sealed record ConversionLoss(string Category, uint Id, string Field, string Reason);
public sealed record LegacyGroup(int Width, int Height, int ExactSize, Group Source, uint[] References,
    byte Mode, int Loops, int Start);
public sealed record LegacyObject(Appearance Source, byte[] Properties, LegacyGroup[] Groups);
public sealed record LegacyTile(uint First, int Width, int Height);

/// <summary>Independent interoperability writer. Never edits modern source files.</summary>
public sealed class LegacyExport
{
    public LegacyProfile Profile { get; }
    public bool Transparency { get; }
    public IReadOnlyList<ConversionLoss> Losses => losses;
    public uint SpriteCount { get; private set; }
    public bool Extended => Profile.Version >= 960 || SpriteCount > ushort.MaxValue;
    public IReadOnlyList<LegacyObject> Objects => objects;
    private readonly AssetSet assets;
    private readonly List<ConversionLoss> losses = [];
    private readonly List<LegacyObject> objects = [];
    private readonly SortedDictionary<uint, LegacyTile> tiles = [];
    private readonly bool selectedOnly;
    // The standard editor initially selects item 100 and expects each category
    // to have its base record. Explicit blank entries keep category-only exports editable.
    private readonly uint[] maxima = [100, 1, 1, 1];
    public LegacyExport(AssetSet assets, int version, bool transparency, IEnumerable<Appearance>? selection = null, Action<int, int>? progress = null, uint? datSignature=null, uint? sprSignature=null)
    {
        this.assets = assets; Profile = LegacyProfile.For(version,datSignature,sprSignature); Transparency = transparency; selectedOnly=selection!=null;
        var selected = (selection ?? assets.Appearances).ToArray(); int processed = 0;
        foreach (var a in selected)
        {
            int category = Array.IndexOf(AssetSet.Categories, a.Category);
            if (category < 0 || a.Id < (category == 0 ? 100u : 1u) || a.Id > ushort.MaxValue)
                throw new InvalidDataException("Appearance ID cannot fit a standard legacy DAT category.");
            maxima[category] = Math.Max(maxima[category], a.Id);
            var sourceGroups = AssetSet.Groups(a);
            if (a.Category != "outfit" && sourceGroups.Length != 1)
                throw new InvalidDataException($"Cannot flatten multiple groups in {a.Category}/{a.Id}.");
            if (a.Category == "outfit" && (sourceGroups.Length > 2 || sourceGroups.Any(g => g.Type > 1) ||
                sourceGroups.Select(g => g.Type).Distinct().Count() != sourceGroups.Length))
                throw new InvalidDataException($"Unsupported outfit frame groups in {a.Id}.");
            if (!Profile.FrameGroups && sourceGroups.Length > 1)
            {
                Loss(a, "frame_groups", "Idle group omitted: target profile supports a single outfit group.");
                sourceGroups = [sourceGroups.Single(g => g.Type == 1)];
            }
            sourceGroups = sourceGroups.OrderBy(g => g.Type).ToArray();
            try
            {
                var groups = sourceGroups.Select(g => NormalizeGroup(a, g)).ToArray();
                objects.Add(new(a, Properties(a), groups));
            }
            catch (Exception error) when (error is InvalidDataException or OverflowException)
            { throw new InvalidDataException($"Conversion preflight {a.Category}/{a.Id}: {error.Message}", error); }
            foreach (var field in Proto.Fields(a.Source).Where(f => f.Number is < 1 or > 4))
                Loss(a, "appearance." + field.Number, "Modern-only appearance metadata retained in the source sidecar.");
            if (++processed % 256 == 0) progress?.Invoke(processed, selected.Length);
        }
        if (objects.Count == 0) throw new InvalidDataException("No appearances selected.");
        if (objects.Select(o => (o.Source.Category, o.Source.Id)).Distinct().Count() != objects.Count)
            throw new InvalidDataException("Duplicate conversion selection.");
        if (!Transparency)
            losses.Add(new("*", 0, "alpha", "RGB output promotes nonzero alpha to opaque; choose RGBA to preserve partial alpha (requires client support)."));
        progress?.Invoke(selected.Length, selected.Length);
    }
    public object Summary() => new
    {
        version = Profile.Version, versionName=Profile.Name, transparency = Transparency, extended = Extended, selectedOnly,
        frameGroups = Profile.FrameGroups, improvedAnimations = Profile.Durations,
        appearances = objects.Count, sprites = SpriteCount, lossCount = losses.Count,
        losses = losses.GroupBy(l => (l.Field, l.Reason)).Select(g => new { field = g.Key.Field, reason = g.Key.Reason, count = g.Count() }).ToArray(),
        notice = "New legacy project; original modern files remain untouched. Sprite IDs/counts: " + (Extended ? (Profile.Version<960 ? "32-bit (requires an extended client)." : "32-bit.") : (Profile.Version==860 ? "16-bit standard 8.60." : "16-bit standard.")) + " Frame groups and durations follow the selected target. " + (selectedOnly ? "SELECTED OBJECTS ONLY: original IDs are preserved; other ID slots are blank. " : "ALL OBJECTS. ") + "Review compatibility losses."
    };
    private void Loss(Appearance a, string field, string reason)
    {
        if (losses.Count >= 1000000) throw new InvalidDataException("Conversion loss report exceeds its limit.");
        losses.Add(new(a.Category, a.Id, field, reason));
    }
    private LegacyGroup NormalizeGroup(Appearance a, Group g)
    {
        if(!Profile.PatternDepth && g.PatternZ>1)
        {
            Loss(a,"pattern_depth","Target has no Z pattern dimension; only Z=0 is exported.");
            int plane=checked((int)(g.PatternX*g.PatternY*g.Layers));
            g=g with {PatternZ=1,Sprites=Enumerable.Range(0,g.Frames).SelectMany(f=>g.Sprites.Skip(f*plane*(int)g.PatternZ).Take(plane)).ToArray()};
        }
        if (new[] { g.PatternX, g.PatternY, g.PatternZ, g.Layers, (uint)g.Frames }.Any(n => n is 0 or > 255))
            throw new InvalidDataException($"Legacy pattern dimensions exceed a byte in {a.Category}/{a.Id}.");
        int width = 1, height = 1;
        foreach (uint id in g.Sprites)
        {
            var sheet = assets.FindSheet(id); width = Math.Max(width, sheet.Width / 32); height = Math.Max(height, sheet.Height / 32);
            if (!tiles.ContainsKey(id))
            {
                int count = sheet.Width / 32 * (sheet.Height / 32);
                if (SpriteCount + count > 4000000) throw new InvalidDataException("Converted SPR exceeds the four-million tile limit.");
                tiles[id] = new(SpriteCount + 1, sheet.Width / 32, sheet.Height / 32);
                SpriteCount += (uint)count;
            }
        }
        if ((long)g.Sprites.Length * width * height > 65536) throw new InvalidDataException($"Converted reference count exceeds 65,536 in {a.Category}/{a.Id}.");
        var refs = new List<uint>();
        foreach (uint id in g.Sprites)
        {
            var tile = tiles[id];
            // Legacy indices start at the bottom-right quadrant. Pad small
            // layers at the top/left, preserving the existing render geometry.
            for (int h = 0; h < height; h++)
            for (int w = 0; w < width; w++)
                refs.Add(w < tile.Width && h < tile.Height ? tile.First + (uint)(h * tile.Width + w) : 0);
        }
        var frames = Proto.Fields(a.Source).Where(f => f.Number == 2).ToArray();
        var source = frames.Single(f => Number(Proto.Fields(Proto.Bytes(f)), 1, 0) == g.Type);
        var info = Proto.Fields(Proto.Bytes(Proto.Fields(Proto.Bytes(source)).Single(f => f.Number == 3)));
        int exact = checked((int)Number(info, 7, (uint)(Math.Max(width, height) * 32)));
        if (exact is < 1 or > 255) throw new InvalidDataException("Legacy bounding size exceeds a byte.");
        foreach (var f in info.Where(f => f.Number is 8 or 9 || f.Number > 9))
            Loss(a, "sprite_info." + f.Number, "Modern sprite metadata is not represented in legacy DAT.");
        var animation = info.Where(f => f.Number == 6).ToArray();
        if (animation.Length > 1) throw new InvalidDataException("Duplicate animation message.");
        byte mode = 0; int loops = 0, start = 0;
        if (animation.Length == 1)
        {
            var fields = Proto.Fields(Proto.Bytes(animation[0]));
            uint sync = Number(fields, 2, 0), random = Number(fields, 3, 0), explicitMode = Number(fields, 7, sync);
            if (sync > 1 || random > 1 || explicitMode > 1) throw new InvalidDataException("Unsupported animation mode.");
            mode = (byte)explicitMode;
            int loopType = Signed(fields, 4, 0);
            loops = loopType switch { -1 => -1, 0 => 0, 1 => checked((int)Number(fields, 5, 0)), _ => throw new InvalidDataException("Unsupported animation loop type.") };
            if (loopType == 1 && loops < 1) throw new InvalidDataException("Counted animation requires a positive loop count.");
            start = random != 0 ? -1 : checked((int)Number(fields, 1, 0));
            if (start >= g.Frames || start > 127) throw new InvalidDataException("Animation start phase cannot fit legacy DAT.");
            if (explicitMode != sync) Loss(a, "animation.synchronized", "Explicit animation mode takes precedence over a conflicting synchronized flag.");
            foreach (var f in fields.Where(f => f.Number > 7))
                Loss(a, "animation." + f.Number, "Unknown animation metadata retained in source sidecar.");
            if (!Profile.Durations) Loss(a, "animation_timing", "Target profile stores frame count, not phase durations/synchronization/loop/start metadata.");
        }
        foreach (var phase in g.Phases)
            if (!phase.Min.HasValue || !phase.Max.HasValue) throw new InvalidDataException("Missing animation phase duration requires an explicit conversion policy.");
        return new(width, height, exact, g, refs.ToArray(), mode, loops, start);
    }
    private static uint Number(List<Field> fields, int number, uint fallback)
    {
        var found = fields.Where(f => f.Number == number).ToArray();
        if (found.Length > 1) throw new InvalidDataException("Duplicate scalar field.");
        return found.Length == 0 ? fallback : Proto.UInt(found[0]);
    }
    private static int Signed(List<Field> fields, int number, int fallback)
    {
        var found = fields.Where(f => f.Number == number).ToArray();
        if (found.Length > 1 || (found.Length == 1 && found[0].Wire != 0)) throw new InvalidDataException("Invalid signed scalar.");
        if (found.Length == 0) return fallback;
        ulong value = found[0].Scalar;
        int result = unchecked((int)value);
        if (value != (ulong)(uint)result && value != unchecked((ulong)(long)result)) throw new InvalidDataException("Signed scalar overflow.");
        return result;
    }
    private static ushort Short(uint value) => value <= ushort.MaxValue ? (ushort)value : throw new InvalidDataException("Legacy attribute exceeds uint16.");
    private static short Offset(int value) => value is >= short.MinValue and <= short.MaxValue ? (short)value : throw new InvalidDataException("Legacy offset exceeds int16.");
    private byte[] Properties(Appearance a)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        var messages = Proto.Fields(a.Source).Where(f => f.Number == 3).ToArray();
        if (messages.Length > 1) throw new InvalidDataException("Duplicate flags message.");
        var fields = messages.Length == 0 ? [] : Proto.Fields(Proto.Bytes(messages[0]));
        if (a.Name.Length > 0 && !fields.Any(f => f.Number == 36))
            Loss(a, "appearance.name", "Legacy DAT stores names only in market data; the full name remains in the source sidecar and ID report.");
        if (fields.Where(f => f.Number <= 39).GroupBy(f => f.Number).Any(g => g.Count() > 1))
            throw new InvalidDataException("Duplicate appearance flag.");
        var bools = new Dictionary<int, int> { [2]=1, [3]=2, [4]=3, [5]=4, [6]=5, [8]=6, [9]=7, [19]=10, [12]=11,
            [13]=12, [14]=13, [15]=14, [16]=15, [17]=16, [18]=17, [20]=18, [22]=21, [24]=23, [25]=24,
            [28]=27, [29]=28, [32]=31, [33]=32, [37]=36, [38]=37, [39]=38, [7]=254 };
        void Flag(int value)
        {
            writer.Write((byte)(Profile.Flag(value) ?? throw new InvalidDataException("Unsupported target attribute.")));
        }
        foreach (var f in fields)
        {
            if((f.Number==39 && a.Category!="effect") || (a.Category!="item" && f.Number is not (23 or 26 or 27 or 29 or 39)))
            {Loss(a,"flags."+f.Number,"Flag is not written for this category by the legacy editor; retained in the native source sidecar.");continue;}
            if (bools.TryGetValue(f.Number, out int flag))
            {
                uint value = Proto.UInt(f); if (value > 1) throw new InvalidDataException("Invalid boolean flag.");
                if (value == 0) continue;
                if (!Profile.Flag(flag).HasValue)
                    Loss(a, "flags." + f.Number, "Flag is not supported by the selected legacy profile.");
                else Flag(flag);
                continue;
            }
            int metadataFlag = f.Number switch { 1=>0, 10=>8, 11=>9, 21=>19, 23=>22, 26=>25, 27=>26, 30=>29, 31=>30, 34=>33, 35=>35, 36=>34, _=>-1 };
            if (metadataFlag < 0 || !Profile.Flag(metadataFlag).HasValue)
            { Loss(a, "flags." + f.Number, "Modern-only flag is omitted from legacy DAT and retained in the source sidecar."); continue; }
            var nested = Proto.Fields(Proto.Bytes(f));
            if (f.Number == 21)
            {
                uint direction = Number(nested, 1, 0);
                if (direction is not (1 or 2)) throw new InvalidDataException("Unsupported hook direction.");
                Flag(direction == 1 ? 19 : 20);
            }
            else if (f.Number == 36)
            {
                Flag(metadataFlag);
                writer.Write(Short(Number(nested, 1, 0))); writer.Write(Short(Number(nested, 2, a.Id))); writer.Write(Short(Number(nested, 3, a.Id)));
                var names = nested.Where(n => n.Number == 7).ToArray();
                if (names.Length > 1) throw new InvalidDataException("Duplicate market name.");
                string name = names.Length == 0 ? a.Name : Proto.Text(names[0]);
                if (name.Any(c => c > 255)) Loss(a, "market.name", "Characters outside Latin-1 replaced with '?'; full name remains in the source sidecar.");
                byte[] nameBytes = Encoding.Latin1.GetBytes(name);
                writer.Write(Short((uint)nameBytes.Length)); writer.Write(nameBytes);
                // Do not pretend repeated modern vocation enums are a legacy
                // profession mask. Report their removal explicitly.
                writer.Write((ushort)0); writer.Write(Short(Number(nested, 6, 0)));
                if (nested.Any(n => n.Number is 5 or 8)) Loss(a, "market.vocations", "Modern vocation restrictions are not equivalent to legacy profession encoding; restriction cleared.");
            }
            else
            {
                Flag(metadataFlag);
                if (f.Number == 26)
                { if(Profile.PatternDepth){writer.Write(Offset(Signed(nested, 1, 0))); writer.Write(Offset(Signed(nested, 2, 0)));}else if(Signed(nested,1,0)!=8 || Signed(nested,2,0)!=8)Loss(a,"flags.26","Target uses an implicit 8x8 offset; explicit offsets omitted."); }
                else
                {
                    writer.Write(Short(Number(nested, 1, 0)));
                    if (f.Number == 23) writer.Write(Short(Number(nested, 2, 0)));
                }
            }
            int[] known = f.Number switch { 23 or 26 => [1,2], 36 => [1,2,3,5,6,7,8], _ => [1] };
            foreach (var extra in nested.Where(n => !known.Contains(n.Number)))
                Loss(a, $"flags.{f.Number}.{extra.Number}", "Unknown nested flag data retained in the source sidecar.");
        }
        writer.Write((byte)255); return bytes.ToArray();
    }
    private static readonly string[] Files = ["Tibia.dat", "Tibia.spr", "Tibia.otfi", "nexa-project.json", "conversion-report.json", "source-appearances.pb", "source-catalog.json", "working-copy-appearances.json"];
    public string Export(string target, SheetCache cache, bool acceptLosses, Action<string, int, int>? progress = null)
    {
        if (losses.Count > 0 && !acceptLosses) throw new InvalidDataException("Review and accept the compatibility losses before exporting.");
        target = Path.GetFullPath(target); string parent = Path.GetDirectoryName(target) ?? throw new InvalidDataException("Invalid output parent.");
        AssetSet.RejectLinks(parent);
        if (!Directory.Exists(parent) || File.Exists(target) || Directory.Exists(target)) throw new InvalidDataException("Choose a new output directory; existing files are never overwritten.");
        if (Within(assets.Root, target) || Within(target, assets.Root)) throw new InvalidDataException("Output must be outside the modern source assets.");
        string stage = target + ".partial";
        if (Directory.Exists(stage) || File.Exists(stage)) throw new InvalidDataException("Staging path already exists.");
        Directory.CreateDirectory(stage);
        try
        {
            WriteSprites(Path.Combine(stage, "Tibia.spr"), cache, progress);
            WriteDat(Path.Combine(stage, "Tibia.dat"), progress);
            string b(bool value) => value ? "true" : "false";
            File.WriteAllText(Path.Combine(stage, "Tibia.otfi"), $"DatSpr\n  extended: {b(Extended)}\n  transparency: {b(Transparency)}\n  frame-durations: {b(Profile.Durations)}\n  frame-groups: {b(Profile.FrameGroups)}\n  extended-items: false\n  metadata-file: Tibia.dat\n  sprites-file: Tibia.spr\n  sprite-size: 32\n  sprite-data-size: 4096\n");
            File.WriteAllBytes(Path.Combine(stage, "source-appearances.pb"), assets.AppearanceBytes.ToArray());
            File.WriteAllBytes(Path.Combine(stage, "source-catalog.json"), assets.CatalogBytes.ToArray());
            using(var metadata=new FileStream(Path.Combine(stage,"working-copy-appearances.json"),FileMode.CreateNew,FileAccess.Write))
                JsonSerializer.Serialize(metadata,new {schemaVersion=1,sourceFingerprint=assets.Fingerprint,
                    scope="Selected working-copy appearance records, including unsupported flag edits. Reference metadata only; native sprite/catalog dependencies are not included.",
                    objects=objects.Select(o=>new {o.Source.Category,o.Source.Id,source=Convert.ToBase64String(o.Source.Source.Span)})});
            File.WriteAllText(Path.Combine(stage,"nexa-project.json"),JsonSerializer.Serialize(new {
                schemaVersion=1,version=Profile.Version,datSignature=Profile.DatSignature,sprSignature=Profile.SprSignature,
                selectedOnly,initialSelection=new {category=objects[0].Source.Category,id=objects[0].Source.Id}
            }));
            File.WriteAllText(Path.Combine(stage, "conversion-report.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, sourceFingerprint = assets.Fingerprint, profile = Summary(), losses,
                initialSelection=new {category=objects[0].Source.Category,id=objects[0].Source.Id},
                ids = objects.Select(o => new { o.Source.Category, modernId = o.Source.Id, legacyId = o.Source.Id, o.Source.Name }),
                paddedMissingIds = "Sparse modern IDs are preserved; gaps and empty category base IDs (item 100, other categories 1) are explicit blank legacy objects, not remapped records.",
                sourceSidecar = "Original protobuf/catalog bytes and current selected metadata records (working-copy-appearances.json) retained. Native sprite/catalog dependencies are not included; use native packages for portable working-copy preservation.",
                scope = "Profile-specific 16/32-bit DAT/SPR for legacy editing. Not a modern appearances writer or proof of in-game compatibility."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Directory.Move(stage, target); return target;
        }
        catch
        {
            // This directory was freshly created by this call. Never recurse or
            // touch an existing destination. A forced kill may leave .partial.
            foreach (string file in Files) { try { File.Delete(Path.Combine(stage, file)); } catch (IOException) { } }
            try { Directory.Delete(stage, false); } catch (IOException) { }
            throw;
        }
    }
    private static bool Within(string parent, string child) => string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public IEnumerable<(uint Id,Pixels Image)> ObjectPixels(SheetCache cache)
    {
        if(SpriteCount>4096)throw new InvalidDataException("OBD export exceeds 4,096 normalized sprites. Use a native object package or project export.");
        yield return (0,new Pixels(32,32,new byte[4096]));
        foreach(var (id,tile) in tiles)
        {
            var image=cache.Sprite(id);
            for(int h=0;h<tile.Height;h++)for(int w=0;w<tile.Width;w++)
                yield return (tile.First+(uint)(h*tile.Width+w),image.Crop((tile.Width-1-w)*32,(tile.Height-1-h)*32,32,32));
        }
    }
    public static byte[] Compress(Pixels tile, bool rgba)
    {
        if (tile.Width != 32 || tile.Height != 32) throw new InvalidDataException("Legacy SPR requires 32x32 tiles.");
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        int p = 0; bool any = false;
        while (p < 1024)
        {
            int start = p; while (p < 1024 && tile.Rgba[p * 4 + 3] == 0) p++;
            int transparent = p - start, first = p;
            while (p < 1024 && tile.Rgba[p * 4 + 3] != 0) p++;
            int colored = p - first;
            writer.Write((ushort)transparent); writer.Write((ushort)colored);
            for (int i = first; i < p; i++)
            {
                writer.Write(tile.Rgba, i * 4, 3); if (rgba) writer.Write(tile.Rgba[i * 4 + 3]); any = true;
            }
        }
        return any ? bytes.ToArray() : [];
    }
    private void WriteSprites(string file, SheetCache cache, Action<string, int, int>? progress)
    {
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(stream); writer.Write(Profile.SprSignature); if(Extended)writer.Write(SpriteCount);else writer.Write((ushort)SpriteCount);
        long header=Extended ? 8L : 6L;
        var addresses = new uint[SpriteCount]; stream.Position = header + SpriteCount * 4L;
        int done = 0;
        foreach (var (id, tile) in tiles)
        {
            var image = cache.Sprite(id);
            for (int h = 0; h < tile.Height; h++)
            for (int w = 0; w < tile.Width; w++)
            {
                uint index = tile.First + (uint)(h * tile.Width + w) - 1;
                var pixels = image.Crop((tile.Width - 1 - w) * 32, (tile.Height - 1 - h) * 32, 32, 32);
                byte[] compressed = Compress(pixels, Transparency);
                if (compressed.Length == 0) continue;
                if (stream.Position + 5 + compressed.Length > uint.MaxValue) throw new InvalidDataException("SPR exceeds 4 GiB; no complete output was published.");
                addresses[index] = (uint)stream.Position; writer.Write(new byte[] { 255, 0, 255 }); writer.Write((ushort)compressed.Length); writer.Write(compressed);
            }
            if (++done % 128 == 0) progress?.Invoke("Saving SPR", done, tiles.Count);
        }
        stream.Position = header; foreach (uint address in addresses) writer.Write(address);
        progress?.Invoke("Saving SPR", tiles.Count, tiles.Count);
    }
    private void WriteDat(string file, Action<string, int, int>? progress)
    {
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write); using var writer = new BinaryWriter(stream);
        writer.Write(Profile.DatSignature); foreach (uint max in maxima) writer.Write((ushort)max);
        var lookup = objects.ToDictionary(o => (o.Source.Category, o.Source.Id));
        int done = 0, total = objects.Count;
        for (int c = 0; c < 4; c++)
        for (uint id = c == 0 ? 100u : 1u; id <= maxima[c]; id++)
        {
            if (!lookup.TryGetValue((AssetSet.Categories[c], id), out var obj))
            {
                writer.Write((byte)255);
                if (c == 1 && Profile.FrameGroups) { writer.Write((byte)1); writer.Write((byte)1); }
                writer.Write(Enumerable.Repeat((byte)1,Profile.PatternDepth ? 7 : 6).ToArray()); if(Extended)writer.Write(0u);else writer.Write((ushort)0); continue;
            }
            writer.Write(obj.Properties);
            if (c == 1 && Profile.FrameGroups) writer.Write((byte)obj.Groups.Length);
            foreach (var group in obj.Groups)
            {
                if (c == 1 && Profile.FrameGroups) writer.Write((byte)group.Source.Type);
                writer.Write((byte)group.Width); writer.Write((byte)group.Height);
                if (group.Width > 1 || group.Height > 1) writer.Write((byte)group.ExactSize);
                writer.Write((byte)group.Source.Layers); writer.Write((byte)group.Source.PatternX); writer.Write((byte)group.Source.PatternY);
                if(Profile.PatternDepth)writer.Write((byte)group.Source.PatternZ); writer.Write((byte)group.Source.Frames);
                if (Profile.Durations && group.Source.Frames > 1)
                {
                    writer.Write(group.Mode); writer.Write(group.Loops); writer.Write(unchecked((byte)group.Start));
                    foreach (var phase in group.Source.Phases) { writer.Write(phase.Min!.Value); writer.Write(phase.Max!.Value); }
                }
                foreach (uint sprite in group.References) {if(Extended)writer.Write(sprite);else writer.Write((ushort)sprite);}
            }
            if (++done % 256 == 0) progress?.Invoke("Saving DAT", done, total);
        }
        progress?.Invoke("Saving DAT", total, total);
    }
}
