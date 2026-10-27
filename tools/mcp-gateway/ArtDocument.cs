using System.Security.Cryptography;
using System.Text.Json;
using NexaModern;

namespace NexaAsset.Mcp;

public sealed class ArtLayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public byte Opacity { get; set; } = 255;
    public byte[] Rgba { get; set; } = [];
}
public sealed class ArtFrame
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Duration { get; set; } = 100;
    public List<ArtLayer> Layers { get; set; } = [];
}
public sealed class ArtState
{
    public int SchemaVersion { get; set; } = 1;
    public string Category { get; set; } = "item";
    public string Name { get; set; } = "";
    public int Width { get; set; } = 32;
    public int Height { get; set; } = 32;
    // Cells are ordered time * Directions + direction, using native patternX order.
    public int Directions { get; set; } = 1;
    public List<ArtFrame> Frames { get; set; } = [];
}

/// <summary>Straight RGBA is authoritative. Bitmap previews never feed pixels back into this model.</summary>
public sealed class ArtDocument
{
    public const int PixelBudget = 2 * 1024 * 1024;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public required string ProjectSessionId { get; init; }
    public required long BaseRevision { get; init; }
    public long Revision { get; private set; }
    public long StructureRevision { get; private set; }
    public bool TargetTransparency { get; set; } = true;
    public bool ApprovalRequested { get; set; }
    public bool Committed { get; set; }
    public ArtState State { get; private set; } = new();
    public byte[]? Reference { get; set; }
    private readonly List<ArtState> undo = [], redo = [];
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    /// <summary>Create an isolated draft from optional straight RGBA; the active project is untouched.</summary>
    public static ArtDocument Create(string session, long revision, int width, int height, byte[]? pixels = null, string category = "item", string name = "")
    {
        CheckShape(width, height);
        if (pixels != null && pixels.Length != width * height * 4) throw new InvalidDataException("Invalid RGBA length.");
        var doc = new ArtDocument { ProjectSessionId = session, BaseRevision = revision };
        doc.State = new ArtState { Category = category, Name = name, Width = width, Height = height, Frames = [new() { Layers = [new() { Name = "Base", Rgba = pixels?.ToArray() ?? new byte[width * height * 4] }] }] };
        Validate(doc.State);
        doc.Reference = pixels?.ToArray() ?? new byte[width * height * 4];
        return doc;
    }
    /// <summary>Reject unsupported canvas axes before allocating pixel buffers.</summary>
    public static void CheckShape(int w, int h)
    {
        if (w is not (32 or 64) || h is not (32 or 64)) throw new InvalidDataException("Sprite axes must be 32 or 64 pixels.");
    }
    /// <summary>Validate geometry, timing, layers and decoded pixel budget as one transaction.</summary>
    public static void Validate(ArtState state)
    {
        CheckShape(state.Width, state.Height);
        if (state.Category is not ("item" or "outfit" or "effect" or "missile") || state.Name == null || state.Name.Length > 80) throw new InvalidDataException("Invalid asset category/name.");
        if (state.Frames == null || state.Frames.Any(f => f == null || f.Layers == null || f.Layers.Any(l => l == null || l.Rgba == null))) throw new InvalidDataException("Null art frames/layers/pixels are invalid.");
        if (state.SchemaVersion != 1 || state.Directions is not (1 or 4) || state.Frames.Count is < 1 or > 64 || state.Frames.Count % state.Directions != 0)
            throw new InvalidDataException("Invalid art schema, directions or cell count (1–64).");
        long bytes = 0;
        foreach (var frame in state.Frames)
        {
            if (frame.Duration is < 10 or > 10000 || frame.Layers.Count is < 1 or > 16) throw new InvalidDataException("Use 1–16 layers and 10–10000 ms durations.");
            foreach (var layer in frame.Layers)
            {
                if (layer.Name == null || layer.Name.Length > 80 || layer.Rgba.Length != state.Width * state.Height * 4) throw new InvalidDataException("Invalid layer name/pixel count.");
                bytes += layer.Rgba.Length;
            }
        }
        if (bytes > PixelBudget) throw new InvalidDataException("Draft exceeds the 2 MiB decoded pixel budget.");
        for (int at = 0; at < state.Frames.Count; at += state.Directions)
            if (state.Frames.Skip(at).Take(state.Directions).Any(f => f.Duration != state.Frames[at].Duration)) throw new InvalidDataException("Directions at the same time must share their duration.");
    }
    /// <summary>Reject writes to committed or stale drafts.</summary>
    public void CheckRevision(long expected)
    {
        if (Committed) throw new InvalidOperationException("This draft has already been committed. Begin another edit.");
        if (Revision != expected) throw new InvalidOperationException($"DOCUMENT_CONFLICT: expected {expected}, current {Revision}. Refresh the preview.");
    }
    /// <summary>Deep-copy editable state while preserving stable frame and layer identities.</summary>
    public static ArtState Copy(ArtState state) => new() { SchemaVersion = state.SchemaVersion, Category = state.Category, Name = state.Name, Width = state.Width, Height = state.Height, Directions = state.Directions, Frames = state.Frames.Select(CopyFrame).ToList() };
    /// <summary>Copy exact layer bytes and timing without JSON/base64 conversion.</summary>
    public static ArtFrame CopyFrame(ArtFrame frame) => new() { Id = frame.Id, Duration = frame.Duration, Layers = frame.Layers.Select(l => new ArtLayer { Id = l.Id, Name = l.Name, Visible = l.Visible, Locked = l.Locked, Opacity = l.Opacity, Rgba = l.Rgba.ToArray() }).ToList() };
    private static void Trim(List<ArtState> history)
    {
        while (history.Count > 32 || history.Sum(s => s.Frames.Sum(f => f.Layers.Sum(l => (long)l.Rgba.Length))) > 4 * 1024 * 1024) history.RemoveAt(0);
    }
    private static string Topology(ArtState s) => $"{s.Width}/{s.Height}/{s.Directions}/" + string.Join(";", s.Frames.Select(f => f.Id + ":" + string.Join(",", f.Layers.Select(l => l.Id))));
    /// <summary>Validate a deep-copy candidate, then publish one revision and one bounded undo step.</summary>
    public void Edit(long expected, Action<ArtState> action)
    {
        CheckRevision(expected);
        var before = State;
        var candidate = Copy(State);
        action(candidate); Validate(candidate);
        if (Topology(State) != Topology(candidate)) StructureRevision++;
        undo.Add(before); Trim(undo); redo.Clear(); State = candidate; Revision++; ApprovalRequested = false;
    }
    /// <summary>Restore a retained state without reusing document revisions.</summary>
    public void History(long expected, bool forward)
    {
        CheckRevision(expected); var from = forward ? redo : undo; var to = forward ? undo : redo;
        if (from.Count == 0) throw new InvalidOperationException("History is empty.");
        to.Add(State); Trim(to);
        if (Topology(State) != Topology(from[^1])) StructureRevision++;
        State = from[^1]; from.RemoveAt(from.Count - 1); Revision++; ApprovalRequested = false;
    }
    /// <summary>Resolve a validated layer and enforce its lock before writing or external transmission.</summary>
    public static ArtLayer Layer(ArtState state, int frame, int layer, bool writing = true)
    {
        if (frame < 0 || frame >= state.Frames.Count || layer < 0 || layer >= state.Frames[frame].Layers.Count) throw new InvalidDataException("Invalid frame/layer index.");
        var result = state.Frames[frame].Layers[layer];
        if (writing && result.Locked) throw new InvalidOperationException("Layer is locked.");
        return result;
    }
    /// <summary>Flatten visible layers using integer straight-alpha composition, preserving base hidden RGB.</summary>
    public static byte[] Composite(ArtState state, int frame)
    {
        if (frame < 0 || frame >= state.Frames.Count) throw new InvalidDataException("Invalid frame index.");
        byte[] output = new byte[state.Width * state.Height * 4];
        bool first = true;
        foreach (var layer in state.Frames[frame].Layers.Where(l => l.Visible && l.Opacity > 0))
        {
            for (int p = 0; p < output.Length; p += 4)
            {
                int sa = (layer.Rgba[p + 3] * layer.Opacity + 127) / 255, da = output[p + 3];
                if (sa == 0) { if (first) layer.Rgba.AsSpan(p, 3).CopyTo(output.AsSpan(p)); continue; }
                int numerator = sa * 255 + da * (255 - sa);
                for (int c = 0; c < 3; c++) output[p + c] = (byte)((layer.Rgba[p + c] * sa * 255 + output[p + c] * da * (255 - sa) + numerator / 2) / numerator);
                output[p + 3] = (byte)((numerator + 127) / 255);
            }
            first = false;
        }
        return output;
    }
    /// <summary>Return shared draft identity, destination, geometry and revision metadata.</summary>
    public object Info() => new { editId = Id, projectSessionId = ProjectSessionId, baseRevision = BaseRevision, documentRevision = Revision, structureRevision = StructureRevision, State.Category, State.Name, State.Width, State.Height, State.Directions, frameCount = State.Frames.Count, approvalRequested = ApprovalRequested, committed = Committed, dirty = !Committed, canUndo = undo.Count > 0, canRedo = redo.Count > 0 };
    /// <summary>Return a bounded preview; raw painting layers are restricted to the private AIR caller.</summary>
    public object Preview(int frame, bool includeLayers = false)
    {
        var rgba = Composite(State, frame);
        int changed = 0;
        if (Reference?.Length == rgba.Length) for (int p = 0; p < rgba.Length; p += 4) { if (!rgba.AsSpan(p, 4).SequenceEqual(Reference.AsSpan(p, 4))) changed++; }
        else changed = State.Width * State.Height;
        return new { document = Info(), frame, duration = State.Frames[frame].Duration, rgba = Convert.ToBase64String(rgba), reference = Reference == null || Reference.Length != rgba.Length ? null : Convert.ToBase64String(Reference), sha256 = Convert.ToHexString(SHA256.HashData(rgba)), changedPixels = changed,
            layers = State.Frames[frame].Layers.Select((l, i) => new { index = i, l.Name, l.Visible, l.Locked, l.Opacity, rgba = includeLayers ? Convert.ToBase64String(l.Rgba) : null }),
            timeline = State.Frames.Select((f, i) => new { cell = i, time = i / State.Directions, direction = i % State.Directions, f.Duration }),
            warnings = new[] { "Apply creates a NEW asset with one native raster layer. Originals and outfit masks are not replaced. Save/Compile remains a separate editor action.", TargetTransparency ? "Target supports RGBA. Editable layer bytes remain in .nexaart." : "RGB SPR target: nonzero alpha becomes opaque; alpha-zero RGB is not retained by SPR. Preserve .nexaart for exact editable pixels." } };
    }
    /// <summary>Apply a bounded exact RGBA batch to a candidate state.</summary>
    public static void SetPixels(ArtState state, int frame, int layer, JsonElement pixels)
    {
        var target = Layer(state, frame, layer).Rgba;
        if (pixels.ValueKind != JsonValueKind.Array || pixels.GetArrayLength() > 4096) throw new InvalidDataException("Use an array with at most 4096 pixels per stroke.");
        foreach (var pixel in pixels.EnumerateArray())
        {
            int x = pixel.GetProperty("x").GetInt32(), y = pixel.GetProperty("y").GetInt32();
            if (x < 0 || y < 0 || x >= state.Width || y >= state.Height) throw new InvalidDataException("Pixel is outside the canvas.");
            var color = pixel.GetProperty("rgba").EnumerateArray().Select(v => v.GetByte()).ToArray();
            if (color.Length != 4) throw new InvalidDataException("RGBA needs four bytes.");
            color.CopyTo(target, (y * state.Width + x) * 4);
        }
    }
    /// <summary>Merge one manual stroke by comparing its base pixels, including hidden RGB.</summary>
    public void Stroke(long expected, int frame, int layer, JsonElement pixels, bool overwrite)
    {
        if (expected < 0 || expected > Revision) throw new InvalidOperationException("DOCUMENT_CONFLICT: invalid stroke revision.");
        if (pixels.ValueKind != JsonValueKind.Array || pixels.GetArrayLength() > 4096) throw new InvalidDataException("Invalid stroke pixel count.");
        var current = Layer(State, frame, layer).Rgba;
        foreach (var pixel in pixels.EnumerateArray()) {
            int x = pixel.GetProperty("x").GetInt32(), y = pixel.GetProperty("y").GetInt32();
            if (x < 0 || y < 0 || x >= State.Width || y >= State.Height) throw new InvalidDataException("Pixel is outside the canvas.");
            byte[] before = pixel.GetProperty("before").EnumerateArray().Select(v => v.GetByte()).ToArray();
            byte[] after = pixel.GetProperty("rgba").EnumerateArray().Select(v => v.GetByte()).ToArray();
            if (before.Length != 4 || after.Length != 4) throw new InvalidDataException("RGBA needs four bytes.");
            var actual = current.AsSpan((y * State.Width + x) * 4, 4);
            if (!overwrite && !actual.SequenceEqual(before) && !actual.SequenceEqual(after)) throw new InvalidOperationException("STROKE_CONFLICT: another edit changed the same pixels. Your stroke is retained; choose which pixels to keep.");
        }
        Edit(Revision, state => SetPixels(state, frame, layer, pixels));
    }
    /// <summary>Resize time/direction cells without mixing directions with time steps.</summary>
    public static void SetAnimation(ArtState state, int frames, int directions, int? duration)
    {
        if (directions is not (1 or 4) || frames < 1 || frames > 64 / directions) throw new InvalidDataException("Use 1/4 directions and up to 64 cells.");
        int oldDirections = state.Directions, oldTimes = state.Frames.Count / oldDirections;
        var old = state.Frames;
        state.Frames = Enumerable.Range(0, frames * directions).Select(i => {
            int time = Math.Min(i / directions, oldTimes - 1), direction = oldDirections == 1 ? 0 : i % directions;
            var copy = CopyFrame(old[time * oldDirections + direction]);
            if (duration.HasValue) copy.Duration = duration.Value;
            return copy;
        }).ToList();
        state.Directions = directions;
    }
    /// <summary>Apply a deterministic pixel transform without filtering or rescaling.</summary>
    public static void Transform(ArtState state, int frame, int layer, string operation, int dx, int dy)
    {
        var target = Layer(state, frame, layer); var src = target.Rgba; byte[] dest = new byte[src.Length];
        if (operation == "rotate90" && state.Width != state.Height) throw new InvalidDataException("90 degree rotation requires a square canvas.");
        if (operation is not ("flipX" or "flipY" or "rotate90" or "offset")) throw new InvalidDataException("Use flipX, flipY, rotate90 or offset.");
        if (Math.Abs((long)dx) > 64 || Math.Abs((long)dy) > 64) throw new InvalidDataException("Offset exceeds canvas bounds.");
        for (int y = 0; y < state.Height; y++) for (int x = 0; x < state.Width; x++)
        {
            int tx = x, ty = y;
            switch (operation) { case "flipX": tx = state.Width - 1 - x; break; case "flipY": ty = state.Height - 1 - y; break; case "rotate90": tx = state.Height - 1 - y; ty = x; break; case "offset": tx += dx; ty += dy; break; }
            if (tx >= 0 && ty >= 0 && tx < state.Width && ty < state.Height) src.AsSpan((y * state.Width + x) * 4, 4).CopyTo(dest.AsSpan((ty * state.Width + tx) * 4));
        }
        target.Rgba = dest;
    }
    /// <summary>Reorder RGBA bytes for the legacy adapter without premultiplication.</summary>
    public static byte[] ToArgb(byte[] rgba)
    {
        if (rgba.Length % 4 != 0) throw new InvalidDataException("RGBA byte count must be divisible by four.");
        byte[] argb = new byte[rgba.Length]; for (int p = 0; p < rgba.Length; p += 4) { argb[p] = rgba[p + 3]; rgba.AsSpan(p, 3).CopyTo(argb.AsSpan(p + 1)); } return argb;
    }
    /// <summary>Reorder legacy ARGB bytes without changing alpha or hidden RGB.</summary>
    public static byte[] FromArgb(byte[] argb)
    {
        if (argb.Length % 4 != 0) throw new InvalidDataException("ARGB byte count must be divisible by four.");
        byte[] rgba = new byte[argb.Length]; for (int p = 0; p < argb.Length; p += 4) { argb.AsSpan(p + 1, 3).CopyTo(rgba.AsSpan(p)); rgba[p + 3] = argb[p]; } return rgba;
    }
    /// <summary>Rasterize the reviewed draft into its stored destination category.</summary>
    public object CommitPacket() => new { schemaVersion = 1, projectSessionId = ProjectSessionId, expectedRevision = BaseRevision, editId = Id, category = State.Category, name = State.Name, width = State.Width, height = State.Height, directions = State.Directions,
        frames = State.Frames.Select((f, i) => new { duration = f.Duration, rgba = Convert.ToBase64String(Composite(State, i)) }) };
    /// <summary>Serialize editable state, destination and exact layer pixels for a local export.</summary>
    public byte[] Sidecar() => JsonSerializer.SerializeToUtf8Bytes(State, Json);
    /// <summary>Validate and load an editable sidecar as a single undoable operation.</summary>
    public void Load(long expected, byte[] bytes)
    {
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Sidecar exceeds 4 MiB.");
        var state = JsonSerializer.Deserialize<ArtState>(bytes, Json) ?? throw new InvalidDataException("Empty art document."); Validate(state);
        Edit(expected, s => { s.Category = state.Category; s.Name = state.Name; s.Width = state.Width; s.Height = state.Height; s.Directions = state.Directions; s.Frames = state.Frames; });
    }
}
