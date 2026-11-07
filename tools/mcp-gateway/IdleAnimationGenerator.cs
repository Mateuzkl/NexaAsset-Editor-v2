using System.Text.Json;

namespace NexaAsset.Mcp;

/// <summary>Shared nearest-neighbor engine for localized breathing, bobbing and sway.</summary>
public static class IdleAnimationGenerator
{
    private readonly record struct Region(int X, int Y, int Width, int Height);
    internal sealed record Options(bool Breathing, bool Bobbing, bool Sway, int Breath, int Bob, int Swing, int[] Durations, bool Anchor, bool RequireAllDirections = false, int? Direction = null);

    /// <summary>Validate the unified UI/MCP contract before generating an isolated transaction candidate.</summary>
    public static void Generate(ArtState state, JsonElement p, CancellationToken ct = default)
    {
        Keys(p, "editId", "expectedDocumentRevision", "frame", "layer", "breathing", "bobbing", "sway", "breathingAmplitude", "bobbingAmplitude", "swayAmplitude", "frameCount", "durationsMs", "regionMode", "regions", "anchorBase", "direction");
        int count = Integer(p, "frameCount", 4, 3, 16);
        var options = new Options(Boolean(p, "breathing", true), Boolean(p, "bobbing", false), Boolean(p, "sway", false),
            Integer(p, "breathingAmplitude", 1, 1, 2), Integer(p, "bobbingAmplitude", 1, 1, 2), Integer(p, "swayAmplitude", 1, 1, 2),
            Durations(p, count, count == 4 ? [500, 300, 500, 300] : Enumerable.Repeat(100, count).ToArray()), Boolean(p, "anchorBase", true),
            Direction: p.TryGetProperty("direction", out var direction) ? Number(direction, 0, state.Directions - 1, "direction") : null);
        if (!options.Breathing && !options.Bobbing && !options.Sway) throw new InvalidDataException("Select at least one idle motion.");
        Render(state, p, options, ct);
    }

    /// <summary>Render each phase from original layers, preserving one logical edit and bounded memory.</summary>
    internal static void Render(ArtState state, JsonElement p, Options options, CancellationToken ct)
    {
        ArtDocument.Validate(state);
        if (state.Frames.Count != state.Directions) throw new InvalidOperationException("Idle generation needs one source time step. Open a new draft from the desired pose; existing animation was preserved.");
        if (state.Frames.Any(f => f.Layers.Any(l => l.Locked))) throw new InvalidOperationException("Unlock every source layer before generating idle motion; partial layer movement would break alignment.");
        if (options.Durations.Length * state.Directions > 64 || state.Frames.Sum(f => f.Layers.Sum(l => (long)l.Rgba.Length)) * options.Durations.Length > ArtDocument.PixelBudget)
            throw new InvalidDataException("Idle animation exceeds the 64-cell or 2 MiB draft budget.");
        int margin = Math.Max(options.Breathing ? options.Breath : 0, Math.Max(options.Bobbing ? options.Bob : 0, options.Sway ? options.Swing : 0));
        var regions = ReadRegions(state, p, margin, options);
        var output = new List<ArtFrame>(options.Durations.Length * state.Directions);
        for (int phase = 0; phase < options.Durations.Length; phase++) for (int d = 0; d < state.Directions; d++)
        {
            ct.ThrowIfCancellationRequested();
            var original = state.Frames[d]; var pose = ArtDocument.CopyFrame(original); pose.Duration = options.Durations[phase];
            if (phase > 0)
            {
                double angle = 2 * Math.PI * phase / options.Durations.Length;
                int lift = options.Breathing ? Wave(options.Breath * (1 - Math.Cos(angle)) / 2) : 0;
                int expand = options.Breathing ? Wave(options.Breath * Math.Max(0, -Math.Cos(angle))) : 0;
                int bob = options.Bobbing ? Wave(options.Bob * Math.Sin(angle)) : 0, swing = options.Sway ? Wave(options.Swing * Math.Sin(angle)) : 0;
                pose.Id = Guid.NewGuid().ToString("N");
                for (int l = 0; l < pose.Layers.Count; l++)
                {
                    pose.Layers[l].Id = Guid.NewGuid().ToString("N");
                    if (regions[d].Width > 0) Warp(original.Layers[l].Rgba, pose.Layers[l].Rgba, state.Width, regions[d], lift, expand, bob, swing, options.Anchor);
                }
            }
            output.Add(pose);
        }
        state.Frames = output; ArtDocument.Validate(state);
    }

    /// <summary>Calculate independent visible bounds per face, or require one explicit rectangle per direction.</summary>
    private static Region[] ReadRegions(ArtState state, JsonElement p, int margin, Options options)
    {
        string mode = p.TryGetProperty("regionMode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : p.TryGetProperty("regionMode", out _) ? throw new InvalidDataException("regionMode must be auto or rectangle.") : "auto";
        if (mode is not ("auto" or "rectangle")) throw new InvalidDataException("regionMode must be auto or rectangle.");
        bool has = p.TryGetProperty("regions", out var supplied);
        if (has && supplied.ValueKind != JsonValueKind.Array) throw new InvalidDataException("regions must be an array.");
        var regions = new Region[state.Directions];
        if (mode == "auto")
        {
            if (has && supplied.GetArrayLength() != 0) throw new InvalidDataException("Auto mode does not accept explicit regions.");
            for (int d = 0; d < state.Directions; d++)
            {
                if (options.Direction is int selected && d != selected) continue;
                var rgba = ArtDocument.Composite(state, d); int left = state.Width, top = state.Height, right = -1, bottom = -1;
                for (int y = 0; y < state.Height; y++) for (int x = 0; x < state.Width; x++) if (rgba[(y * state.Width + x) * 4 + 3] > 0)
                { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                if (right < 0)
                {
                    if (options.RequireAllDirections) throw new InvalidDataException($"Idle direction {d} is fully transparent. Add visible pixels first.");
                    continue;
                }
                int end = options.Anchor ? Math.Max(top + 1, bottom + 1 - Math.Max(1, (bottom - top + 1) / 4)) : Math.Min(state.Height, bottom + margin + 1);
                left = Math.Max(0, left - margin); top = Math.Max(0, top - margin);
                regions[d] = new(left, top, Math.Min(state.Width, right + margin + 1) - left, end - top);
            }
            if (regions.All(r => r.Width == 0)) throw new InvalidDataException("The source pose is fully transparent. Add visible pixels first.");
        }
        else
        {
            int required = options.Direction.HasValue ? 1 : state.Directions;
            if (!has || supplied.GetArrayLength() != required) throw new InvalidDataException("Rectangle mode needs one region for every enabled direction (N/E/S/W).");
            var seen = new HashSet<int>();
            foreach (var r in supplied.EnumerateArray())
            {
                Keys(r, "direction", "x", "y", "width", "height");
                int d = Required(r, "direction", 0, state.Directions - 1), x = Required(r, "x", 0, state.Width - 1), y = Required(r, "y", 0, state.Height - 1), w = Required(r, "width", 1, state.Width), h = Required(r, "height", 1, state.Height);
                if ((options.Direction.HasValue && d != options.Direction) || !seen.Add(d) || x + w > state.Width || y + h > state.Height)
                    throw new InvalidDataException("Unexpected or duplicate direction, or region outside the canvas.");
                regions[d] = new(x, y, w, h);
            }
        }
        return regions;
    }

    /// <summary>Resample within the ROI only; anchored motion tapers to zero at its bottom row.</summary>
    private static void Warp(byte[] source, byte[] target, int width, Region r, int lift, int expansion, int bob, int swing, bool anchor)
    {
        double center = (r.Width - 1) / 2.0;
        for (int y = 0; y < r.Height; y++)
        {
            double weight = r.Height <= 1 ? 0 : (r.Height - 1 - y) / (double)(r.Height - 1), motion = anchor ? weight : 1;
            int sy = Math.Clamp(y + Round(lift * weight) - Round(bob * motion), 0, r.Height - 1);
            double chest = r.Height <= 1 ? 0 : 1 - Math.Abs(2.0 * y / (r.Height - 1) - 1), scale = r.Width > 1 ? (r.Width - 1) / (r.Width - 1 + 2.0 * expansion * chest) : 1;
            for (int x = 0; x < r.Width; x++)
            {
                int sx = Math.Clamp(Round(center + (x - center) * scale) - Round(swing * motion), 0, r.Width - 1);
                source.AsSpan(((r.Y + sy) * width + r.X + sx) * 4, 4).CopyTo(target.AsSpan(((r.Y + y) * width + r.X + x) * 4, 4));
            }
        }
    }
    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    // Stabilize sinusoid values at half-pixel boundaries before integer quantization.
    private static int Wave(double value) => Round(Math.Round(value, 9));
    /// <summary>Require exactly one bounded integer duration per phase.</summary>
    internal static int[] Durations(JsonElement p, int count, int[] fallback)
    {
        if (!p.TryGetProperty("durationsMs", out var v)) return fallback;
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != count) throw new InvalidDataException("durationsMs must contain exactly frameCount values (10–10000 ms).");
        return v.EnumerateArray().Select(x => Number(x, 10, 10000, "durationsMs")).ToArray();
    }
    /// <summary>Reject malformed objects, unknown keys and duplicate properties before editing.</summary>
    internal static void Keys(JsonElement p, params string[] keys)
    {
        if (p.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Idle parameters and regions must be objects.");
        var seen = new HashSet<string>(); foreach (var k in p.EnumerateObject()) if (!keys.Contains(k.Name) || !seen.Add(k.Name)) throw new InvalidDataException("Unknown or duplicate idle parameter: " + k.Name);
    }
    /// <summary>Reject non-integer or out-of-range numeric parameters.</summary>
    internal static int Number(JsonElement v, int min, int max, string key) => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) && n >= min && n <= max ? n : throw new InvalidDataException($"{key} must be an integer between {min} and {max}.");
    /// <summary>Default optional integers only when they are absent.</summary>
    internal static int Integer(JsonElement p, string key, int fallback, int min, int max) => p.TryGetProperty(key, out var v) ? Number(v, min, max, key) : fallback;
    /// <summary>Reject absent rectangle coordinates/sizes instead of inventing a region.</summary>
    private static int Required(JsonElement p, string key, int min, int max) => p.TryGetProperty(key, out var v) ? Number(v, min, max, key) : throw new InvalidDataException("Missing region " + key + ".");
    /// <summary>Default absent switches while rejecting other JSON types.</summary>
    private static bool Boolean(JsonElement p, string key, bool fallback) => p.TryGetProperty(key, out var v) ? v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new InvalidDataException(key + " must be boolean.") : fallback;
}
