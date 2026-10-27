using System.Security.Cryptography;
using System.Text.Json;
using NexaModern;

namespace NexaAsset.Mcp;

public sealed class ArtLayer
{
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public byte Opacity { get; set; } = 255;
    public byte[] Rgba { get; set; } = [];
}
public sealed class ArtFrame
{
    public int Duration { get; set; } = 100;
    public List<ArtLayer> Layers { get; set; } = [];
}
public sealed class ArtState
{
    public int SchemaVersion { get; set; } = 1;
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
    public bool TargetTransparency { get; set; } = true;
    public bool ApprovalRequested { get; set; }
    public bool Committed { get; set; }
    public ArtState State { get; private set; } = new();
    public byte[]? Reference { get; set; }
    private readonly List<byte[]> undo = [], redo = [];
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static ArtDocument Create(string session, long revision, int width, int height, byte[]? pixels = null)
    {
        CheckShape(width, height);
        if (pixels != null && pixels.Length != width * height * 4) throw new InvalidDataException("Invalid RGBA length.");
        var doc = new ArtDocument { ProjectSessionId = session, BaseRevision = revision };
        doc.State = new ArtState { Width = width, Height = height, Frames = [new() { Layers = [new() { Name = "Base", Rgba = pixels?.ToArray() ?? new byte[width * height * 4] }] }] };
        doc.Reference = pixels?.ToArray() ?? new byte[width * height * 4];
        return doc;
    }
    public static void CheckShape(int w, int h)
    {
        if (w is not (32 or 64) || h is not (32 or 64)) throw new InvalidDataException("Sprite axes must be 32 or 64 pixels.");
    }
    public static void Validate(ArtState state)
    {
        CheckShape(state.Width, state.Height);
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
    public void CheckRevision(long expected)
    {
        if (Committed) throw new InvalidOperationException("This draft has already been committed. Begin another edit.");
        if (Revision != expected) throw new InvalidOperationException($"DOCUMENT_CONFLICT: expected {expected}, current {Revision}. Refresh the preview.");
    }
    private static void Trim(List<byte[]> history)
    {
        while (history.Count > 32 || history.Sum(b => b.Length) > 4 * 1024 * 1024) history.RemoveAt(0);
    }
    public void Edit(long expected, Action<ArtState> action)
    {
        CheckRevision(expected);
        byte[] before = JsonSerializer.SerializeToUtf8Bytes(State, Json);
        var candidate = JsonSerializer.Deserialize<ArtState>(before, Json)!;
        action(candidate); Validate(candidate);
        undo.Add(before); Trim(undo); redo.Clear(); State = candidate; Revision++; ApprovalRequested = false;
    }
    public void History(long expected, bool forward)
    {
        CheckRevision(expected); var from = forward ? redo : undo; var to = forward ? undo : redo;
        if (from.Count == 0) throw new InvalidOperationException("History is empty.");
        to.Add(JsonSerializer.SerializeToUtf8Bytes(State, Json)); Trim(to);
        State = JsonSerializer.Deserialize<ArtState>(from[^1], Json)!; from.RemoveAt(from.Count - 1); Revision++; ApprovalRequested = false;
    }
    public static ArtLayer Layer(ArtState state, int frame, int layer, bool writing = true)
    {
        if (frame < 0 || frame >= state.Frames.Count || layer < 0 || layer >= state.Frames[frame].Layers.Count) throw new InvalidDataException("Invalid frame/layer index.");
        var result = state.Frames[frame].Layers[layer];
        if (writing && result.Locked) throw new InvalidOperationException("Layer is locked.");
        return result;
    }
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
    public object Info() => new { editId = Id, projectSessionId = ProjectSessionId, baseRevision = BaseRevision, documentRevision = Revision, State.Width, State.Height, State.Directions, frameCount = State.Frames.Count, approvalRequested = ApprovalRequested, committed = Committed, dirty = !Committed, canUndo = undo.Count > 0, canRedo = redo.Count > 0 };
    public object Preview(int frame, bool includeLayers = false)
    {
        var rgba = Composite(State, frame);
        int changed = 0;
        if (Reference?.Length == rgba.Length) for (int p = 0; p < rgba.Length; p += 4) { if (!rgba.AsSpan(p, 4).SequenceEqual(Reference.AsSpan(p, 4))) changed++; }
        else changed = State.Width * State.Height;
        return new { document = Info(), frame, duration = State.Frames[frame].Duration, rgba = Convert.ToBase64String(rgba), reference = Reference == null ? null : Convert.ToBase64String(Reference), sha256 = Convert.ToHexString(SHA256.HashData(rgba)), changedPixels = changed,
            layers = State.Frames[frame].Layers.Select((l, i) => new { index = i, l.Name, l.Visible, l.Locked, l.Opacity, rgba = includeLayers ? Convert.ToBase64String(l.Rgba) : null }),
            timeline = State.Frames.Select((f, i) => new { cell = i, time = i / State.Directions, direction = i % State.Directions, f.Duration }),
            warnings = new[] { "Apply creates a NEW asset with one native raster layer. Originals and outfit masks are not replaced. Save/Compile remains a separate editor action.", TargetTransparency ? "Target supports RGBA. Editable layer bytes remain in .nexaart." : "RGB SPR target: nonzero alpha becomes opaque; alpha-zero RGB is not retained by SPR. Preserve .nexaart for exact editable pixels." } };
    }
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
    public static byte[] ToArgb(byte[] rgba)
    {
        if (rgba.Length % 4 != 0) throw new InvalidDataException("RGBA byte count must be divisible by four.");
        byte[] argb = new byte[rgba.Length]; for (int p = 0; p < rgba.Length; p += 4) { argb[p] = rgba[p + 3]; rgba.AsSpan(p, 3).CopyTo(argb.AsSpan(p + 1)); } return argb;
    }
    public static byte[] FromArgb(byte[] argb)
    {
        if (argb.Length % 4 != 0) throw new InvalidDataException("ARGB byte count must be divisible by four.");
        byte[] rgba = new byte[argb.Length]; for (int p = 0; p < argb.Length; p += 4) { argb.AsSpan(p + 1, 3).CopyTo(rgba.AsSpan(p)); rgba[p + 3] = argb[p]; } return rgba;
    }
    public object CommitPacket(string category) => new { schemaVersion = 1, projectSessionId = ProjectSessionId, expectedRevision = BaseRevision, editId = Id, category, width = State.Width, height = State.Height, directions = State.Directions,
        frames = State.Frames.Select((f, i) => new { duration = f.Duration, rgba = Convert.ToBase64String(Composite(State, i)) }) };
    public byte[] Sidecar() => JsonSerializer.SerializeToUtf8Bytes(State, Json);
    public void Load(long expected, byte[] bytes)
    {
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Sidecar exceeds 4 MiB.");
        var state = JsonSerializer.Deserialize<ArtState>(bytes, Json) ?? throw new InvalidDataException("Empty art document."); Validate(state);
        Edit(expected, s => { s.Width = state.Width; s.Height = state.Height; s.Directions = state.Directions; s.Frames = state.Frames; });
    }
}
