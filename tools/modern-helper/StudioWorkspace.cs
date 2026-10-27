using System.Security.Cryptography;
using System.Text.Json;

namespace NexaModern;

/// <summary>Art Studio integration uses the same serialized workspace, never a second asset reader/writer.</summary>
public sealed partial class NativeWorkspace
{
    private readonly Dictionary<string, object> artCommits = [];
    public object StudioRead(JsonElement request)
    {
        string action = request.GetProperty("action").GetString()!;
        string category = request.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
        if (action == "get_sprite")
        {
            uint id = request.GetProperty("spriteId").GetUInt32(); var image = cache.Sprite(id);
            byte[] argb = new byte[image.Rgba.Length]; for (int p = 0; p < argb.Length; p += 4) { argb[p] = image.Rgba[p + 3]; image.Rgba.AsSpan(p, 3).CopyTo(argb.AsSpan(p + 1)); }
            return new { spriteId = id, width = image.Width, height = image.Height, argb = Convert.ToBase64String(argb), sha256 = Convert.ToHexString(SHA256.HashData(image.Rgba)) };
        }
        if (action == "list_assets")
        {
            int page = request.GetProperty("page").GetInt32(); string query = request.GetProperty("query").GetString() ?? "";
            if (page < 0 || page > 100000 || query.Length > 256) throw new InvalidDataException("Invalid pagination/search.");
            if (category == "sprite") { var ids = assets.Sheets.SelectMany(s => Enumerable.Range(0, checked((int)((long)s.Last - s.First + 1))).Select(i => s.First + (uint)i)); return new { items = ids.Skip(checked(page * 100)).Take(100).Select(id => new { id }), page }; }
            if (!AssetSet.Categories.Contains(category)) throw new InvalidDataException("Invalid category.");
            var rows = assets.Appearances.Where(a => a.Category == category && (query.Length == 0 || a.Id.ToString().Contains(query, StringComparison.Ordinal) || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
            return new { items = rows.Skip(checked(page * 100)).Take(100).Select(a => new { id = a.Id, name = a.Name.Length > 256 ? a.Name[..256] : a.Name, category = a.Category }), page };
        }
        var appearance = assets.Find(category, request.GetProperty("assetId").GetUInt32()); var groups = AssetSet.Groups(appearance);
        if (action == "get_asset")
        {
            int remaining = 4096;
            var models = groups.Select((g, index) => { int take = Math.Min(remaining, g.Sprites.Length); remaining -= take; return new { index, type = g.Type, g.Layers, g.PatternX, g.PatternY, g.PatternZ, g.Frames, references = g.Sprites.Take(take).ToArray(), referenceCount = g.Sprites.Length, referencesTruncated = take < g.Sprites.Length, phases = g.Phases }; }).ToArray();
            return new { assetId = appearance.Id, category, name = appearance.Name.Length > 512 ? appearance.Name[..512] : appearance.Name, referenceLimit = 4096, groups = models };
        }
        if (action == "get_frame")
        {
            int group = request.GetProperty("group").GetInt32(), frame = request.GetProperty("frame").GetInt32(), direction = request.GetProperty("direction").GetInt32(), layer = request.GetProperty("layer").GetInt32();
            if (group < 0 || group >= groups.Length) throw new InvalidDataException("Invalid group."); var g = groups[group];
            if (frame < 0 || frame >= g.Frames || direction < 0 || direction >= g.PatternX || layer < 0 || layer >= g.Layers) throw new InvalidDataException("Invalid frame/direction/layer.");
            return new { assetId = appearance.Id, group, frame, direction, layer, spriteId = g.Sprites[checked((((frame * (int)g.PatternZ) * (int)g.PatternY) * (int)g.PatternX + direction) * (int)g.Layers + layer)] };
        }
        throw new InvalidDataException("Unsupported studio read action.");
    }
    public object CommitArtFile(string file, string checksum, string sessionDirectory, long expectedRevision)
    {
        string root = Path.GetFullPath(sessionDirectory), full = Path.GetFullPath(file);
        if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"^art-[a-f0-9]{32}\.json$")) throw new InvalidDataException("Art packet must belong to the active helper session.");
        AssetSet.RejectLinks(full); byte[] bytes = AssetSet.ReadBounded(full, 2 * 1024 * 1024);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), checksum, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Art packet checksum mismatch.");
        using var json = JsonDocument.Parse(bytes); var packet = json.RootElement;
        string editId = packet.GetProperty("editId").GetString() ?? "";
        if (!Guid.TryParseExact(editId, "N", out _)) throw new InvalidDataException("Invalid edit identity.");
        if (artCommits.TryGetValue(editId, out var previous)) return previous;
        if (artCommits.Count >= 256) throw new InvalidDataException("Session commit limit reached; save and reopen.");
        if (Revision != expectedRevision || packet.GetProperty("expectedRevision").GetInt64() != Revision) throw new InvalidOperationException("PROJECT_CONFLICT: working copy changed before commit.");
        if (packet.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unknown art packet schema.");
        string category = packet.GetProperty("category").GetString()!; CheckNewObject(category);
        int width = packet.GetProperty("width").GetInt32(), height = packet.GetProperty("height").GetInt32(), directions = packet.GetProperty("directions").GetInt32();
        if (width is not (32 or 64) || height is not (32 or 64) || directions is not (1 or 4)) throw new InvalidDataException("Invalid art geometry.");
        var frames = packet.GetProperty("frames").EnumerateArray().ToArray();
        if (frames.Length is < 1 or > 64 || frames.Length % directions != 0) throw new InvalidDataException("Invalid art frame count.");
        var images = new List<Pixels>(); var durations = new List<int>();
        foreach (var frame in frames)
        {
            int duration = frame.GetProperty("duration").GetInt32(); byte[] rgba = Convert.FromBase64String(frame.GetProperty("rgba").GetString()!);
            if (duration is < 10 or > 10000 || rgba.Length != width * height * 4) throw new InvalidDataException("Invalid art pixels/timing.");
            images.Add(new(width, height, rgba)); durations.Add(duration);
        }
        for (int i = 0; i < frames.Length; i += directions) if (durations.Skip(i).Take(directions).Any(d => d != durations[i])) throw new InvalidDataException("Direction timings must match.");
        uint next = NextSprite(); if ((ulong)next + (uint)images.Count > uint.MaxValue || cache.ChangedBytes + images.Sum(i => i.Rgba.Length) > SheetCache.Limit) throw new InvalidDataException("Sprite ID/pixel budget exceeded.");
        byte[] refs = Enumerable.Range(0, images.Count).SelectMany(i => Scalar(5, next + (uint)i)).ToArray();
        byte[] animation = Scalar(2, 0).Concat(Scalar(4, 0)).Concat(Enumerable.Range(0, frames.Length / directions).SelectMany(i => Blob(6, Scalar(1, (uint)durations[i * directions]).Concat(Scalar(2, (uint)durations[i * directions])).ToArray()))).ToArray();
        byte[] info = Scalar(1, (uint)directions).Concat(Scalar(2, 1)).Concat(Scalar(3, 1)).Concat(Scalar(4, 1)).Concat(refs).Concat(Blob(6, animation)).Concat(Scalar(7, (uint)Math.Max(width, height))).ToArray();
        byte[] group = Scalar(1, category == "outfit" && frames.Length > directions ? 1u : category == "outfit" ? 0u : 2u).Concat(Blob(3, info)).ToArray();
        byte[] encodedGroups = Blob(2, group); int groupCount = 1;
        if (category == "outfit" && frames.Length > directions)
        {
            // Existing first-time directions form idle; the actual edited timeline is the walking group.
            // No artwork is synthesized, and both groups share those already validated sprite IDs.
            byte[] idleInfo = Scalar(1, (uint)directions).Concat(Scalar(2, 1)).Concat(Scalar(3, 1)).Concat(Scalar(4, 1)).Concat(Enumerable.Range(0, directions).SelectMany(i => Scalar(5, next + (uint)i))).Concat(Scalar(7, (uint)Math.Max(width, height))).ToArray();
            encodedGroups = Blob(2, Scalar(1, 0).Concat(Blob(3, idleInfo)).ToArray()).Concat(encodedGroups).ToArray(); groupCount = 2;
        }
        uint id = NextId(category);
        var appearance = new Appearance(id, category, "", groupCount, Scalar(1, id).Concat(encodedGroups).ToArray());
        _ = AssetSet.Groups(appearance); assets.ValidateAppearanceEdits([appearance]);
        // All data, budgets, references and metadata are validated before the first mutation.
        // No disk I/O, cancellation points or callbacks occur inside this publish section.
        for (int i = 0; i < images.Count; i++) cache.SetSprite(next + (uint)i, images[i], true);
        assets.SetAppearance(appearance); Changed();
        object result = new { selected = id, sprite = next, category, revision = Revision, editId };
        artCommits.Add(editId, result); return result;
    }
}
