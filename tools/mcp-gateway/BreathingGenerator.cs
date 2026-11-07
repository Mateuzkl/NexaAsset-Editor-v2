using System.Text.Json;

namespace NexaAsset.Mcp;

/// <summary>Four non-cumulative nearest-neighbor poses, restricted to a region per native direction.</summary>
public static class BreathingGenerator
{
    private readonly record struct Region(int X, int Y, int Width, int Height);

    /// <summary>Transform an isolated Edit candidate only; validation failures never publish a partial cycle.</summary>
    public static void Generate(ArtState state, JsonElement parameters, CancellationToken cancellation = default)
    {
        ArtDocument.Validate(state);
        Keys(parameters, "editId", "expectedDocumentRevision", "frame", "layer", "amplitude", "durationsMs", "regionMode", "regions");
        int amplitude = Integer(parameters, "amplitude", 1, 1, 2);
        int[] durations = [500, 300, 500, 300];
        if (parameters.TryGetProperty("durationsMs", out var times))
        {
            if (times.ValueKind != JsonValueKind.Array || times.GetArrayLength() != 4)
                throw new InvalidDataException("Breathing needs exactly four durationsMs (10–10000 ms).");
            durations = times.EnumerateArray().Select(t => Number(t, 10, 10000, "durationsMs")).ToArray();
        }
        string mode = parameters.TryGetProperty("regionMode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! :
            parameters.TryGetProperty("regionMode", out _) ? throw new InvalidDataException("regionMode must be auto or rectangle.") : "auto";
        if (mode is not ("auto" or "rectangle")) throw new InvalidDataException("regionMode must be auto or rectangle.");
        if (state.Frames.Count != state.Directions)
            throw new InvalidOperationException("Breathing needs a single source time step. Open a new draft from the desired pose; existing animation was preserved.");
        if (state.Frames.Any(f => f.Layers.Any(l => l.Locked)))
            throw new InvalidOperationException("Unlock every source layer before breathing; moving only some layers could break their alignment.");
        long bytes = state.Frames.Sum(f => f.Layers.Sum(l => (long)l.Rgba.Length)) * 4;
        if (bytes > ArtDocument.PixelBudget || state.Directions * 4 > 64)
            throw new InvalidDataException("Breathing would exceed the 2 MiB draft pixel budget or 64-cell limit.");

        var regions = new Region[state.Directions];
        bool hasRegions = parameters.TryGetProperty("regions", out var supplied);
        if (hasRegions && supplied.ValueKind != JsonValueKind.Array) throw new InvalidDataException("regions must be an array.");
        if (mode == "auto")
        {
            if (hasRegions && supplied.GetArrayLength() != 0) throw new InvalidDataException("Auto mode does not accept explicit regions.");
            for (int d = 0; d < state.Directions; d++) regions[d] = Automatic(state, d, amplitude);
        }
        else
        {
            if (!hasRegions || supplied.GetArrayLength() != state.Directions)
                throw new InvalidDataException("Rectangle mode needs one region for every direction (north/east/south/west).");
            var seen = new HashSet<int>();
            foreach (var item in supplied.EnumerateArray())
            {
                Keys(item, "direction", "x", "y", "width", "height");
                int d = Required(item, "direction", 0, state.Directions - 1);
                if (!seen.Add(d)) throw new InvalidDataException("Duplicate breathing direction region.");
                int x = Required(item, "x", 0, state.Width - 1), y = Required(item, "y", 0, state.Height - 1);
                int w = Required(item, "width", 1, state.Width), h = Required(item, "height", 1, state.Height);
                if (x + w > state.Width || y + h > state.Height) throw new InvalidDataException("Breathing region must stay inside the canvas.");
                regions[d] = new(x, y, w, h);
            }
        }

        var output = new List<ArtFrame>(4 * state.Directions);
        for (int phase = 0; phase < 4; phase++) for (int d = 0; d < state.Directions; d++)
        {
            cancellation.ThrowIfCancellationRequested();
            var original = state.Frames[d];
            var pose = ArtDocument.CopyFrame(original);
            pose.Duration = durations[phase];
            if (phase > 0)
            {
                pose.Id = Guid.NewGuid().ToString("N");
                for (int l = 0; l < pose.Layers.Count; l++)
                {
                    pose.Layers[l].Id = Guid.NewGuid().ToString("N");
                    Warp(original.Layers[l].Rgba, pose.Layers[l].Rgba, state.Width, regions[d], amplitude, phase == 2);
                }
            }
            output.Add(pose);
        }
        state.Frames = output;
        ArtDocument.Validate(state);
    }

    /// <summary>Use the visible composite bounds and anchor the lower quarter; this is geometric, not anatomy detection.</summary>
    private static Region Automatic(ArtState state, int direction, int amplitude)
    {
        byte[] rgba = ArtDocument.Composite(state, direction);
        int left = state.Width, top = state.Height, right = -1, bottom = -1;
        for (int y = 0; y < state.Height; y++) for (int x = 0; x < state.Width; x++)
            if (rgba[(y * state.Width + x) * 4 + 3] > 0)
            { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < 0) throw new InvalidDataException($"Breathing direction {direction} is fully transparent. Add visible pixels first.");
        int anchor = Math.Max(top + 1, bottom + 1 - Math.Max(1, (bottom - top + 1) / 4));
        left = Math.Max(0, left - amplitude); top = Math.Max(0, top - amplitude);
        return new(left, top, Math.Min(state.Width, right + amplitude + 1) - left, anchor - top);
    }

    /// <summary>Sample original RGBA with integer coordinates; outside the ROI remains an untouched clone.</summary>
    private static void Warp(byte[] source, byte[] target, int width, Region r, int amplitude, bool expanded)
    {
        double center = (r.Width - 1) / 2.0;
        for (int y = 0; y < r.Height; y++)
        {
            double weight = r.Height <= 1 ? 0 : (r.Height - 1 - y) / (double)(r.Height - 1);
            int sy = Math.Min(r.Height - 1, y + (int)Math.Round(amplitude * weight, MidpointRounding.AwayFromZero));
            double chest = r.Height <= 1 ? 0 : 1 - Math.Abs(2.0 * y / (r.Height - 1) - 1);
            double scale = expanded && r.Width > 1 ? (r.Width - 1) / (r.Width - 1 + 2.0 * amplitude * chest) : 1;
            for (int x = 0; x < r.Width; x++)
            {
                int sx = Math.Clamp((int)Math.Round(center + (x - center) * scale, MidpointRounding.AwayFromZero), 0, r.Width - 1);
                source.AsSpan(((r.Y + sy) * width + r.X + sx) * 4, 4).CopyTo(target.AsSpan(((r.Y + y) * width + r.X + x) * 4, 4));
            }
        }
    }

    /// <summary>Reject non-object, unknown and duplicate properties before any candidate pixels are changed.</summary>
    private static void Keys(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Breathing parameters/regions must be objects.");
        var seen = new HashSet<string>();
        foreach (var property in value.EnumerateObject())
            if (!keys.Contains(property.Name) || !seen.Add(property.Name)) throw new InvalidDataException("Unknown or duplicate breathing parameter: " + property.Name);
    }
    /// <summary>Read an integer within the domain bounds and report the offending parameter by name.</summary>
    private static int Number(JsonElement value, int min, int max, string key)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) && n >= min && n <= max ? n
            : throw new InvalidDataException($"{key} must be an integer between {min} and {max}.");
    /// <summary>Use a default only when an optional integer is absent; malformed values are rejected.</summary>
    private static int Integer(JsonElement value, string key, int fallback, int min, int max)
        => value.TryGetProperty(key, out var n) ? Number(n, min, max, key) : fallback;
    /// <summary>Require and validate one rectangle coordinate or size without silently supplying missing fields.</summary>
    private static int Required(JsonElement value, string key, int min, int max)
        => value.TryGetProperty(key, out var n) ? Number(n, min, max, key) : throw new InvalidDataException("Missing breathing region " + key + ".");
}
