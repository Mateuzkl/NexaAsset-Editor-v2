using System.IO;

namespace NexaAsset.Mcp;

/// <summary>
/// Four deterministic, palette-preserving idle breathing poses for a single-time-step draft.
/// The edit is geometric, not anatomical recognition: the user can review and retouch the cells.
/// </summary>
public static class BreathingAnimation
{
    private static readonly int[] Durations = [500, 300, 500, 300];

    public static void Generate(ArtState state, int amplitude = 1)
    {
        ArtDocument.Validate(state);
        if (amplitude is < 1 or > 2)
            throw new InvalidDataException("Breathing amplitude must be 1 or 2 pixels.");
        if (state.Frames.Count != state.Directions)
            throw new InvalidOperationException("Breathing needs one starting time step. Use a new draft to preserve existing animation.");
        if (state.Frames.SelectMany(f => f.Layers).All(l => l.Locked))
            throw new InvalidOperationException("Unlock a paint layer before generating breathing.");

        int directions = state.Directions;
        var original = state.Frames.Select(ArtDocument.CopyFrame).ToArray();
        var frames = new List<ArtFrame>(4 * directions);
        for (int time = 0; time < 4; time++)
        {
            // Original, inhale, full inhale, exhale. Pixel colors are never interpolated.
            int lift = time == 0 ? 0 : amplitude;
            int expand = time == 2 ? amplitude : 0;
            for (int direction = 0; direction < directions; direction++)
            {
                var frame = ArtDocument.CopyFrame(original[direction]);
                frame.Id = Guid.NewGuid().ToString("N");
                frame.Duration = Durations[time];
                for (int layer = 0; layer < frame.Layers.Count; layer++)
                {
                    if (frame.Layers[layer].Locked) continue;
                    frame.Layers[layer].Rgba = Deform(original[direction].Layers[layer].Rgba,
                        state.Width, state.Height, lift, expand);
                }
                frames.Add(frame);
            }
        }
        state.Frames = frames;
    }

    private static byte[] Deform(byte[] source, int width, int height, int lift, int expand)
    {
        if (lift == 0 && expand == 0) return source.ToArray();

        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (source[(y * width + x) * 4 + 3] != 0)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        if (right < left) return source.ToArray();

        int spanY = bottom - top + 1, spanX = right - left + 1;
        int anchor = top + Math.Max(1, spanY * 4 / 5);
        int shoulder = top + Math.Max(1, spanY / 2);
        int chestTop = top + spanY * 3 / 10;
        int chestBottom = top + spanY * 13 / 20;
        int center = (left + right) / 2;
        int chestRadius = Math.Max(2, spanX / 4);
        byte[] target = source.ToArray();

        for (int y = Math.Max(0, top - lift); y < Math.Min(height, anchor); y++)
        {
            int rise = y < shoulder ? lift :
                Math.Max(0, (int)Math.Round((double)lift * (anchor - y) / Math.Max(1, anchor - shoulder)));
            int sy = Math.Min(height - 1, y + rise);
            for (int x = Math.Max(0, left - expand); x <= Math.Min(width - 1, right + expand); x++)
            {
                int sx = x;
                if (expand > 0 && y >= chestTop && y <= chestBottom &&
                    x >= center - chestRadius && x <= center + chestRadius)
                {
                    if (x < center) sx = Math.Min(width - 1, x + expand);
                    else if (x > center) sx = Math.Max(0, x - expand);
                }
                Buffer.BlockCopy(source, (sy * width + sx) * 4, target, (y * width + x) * 4, 4);
            }
        }
        return target;
    }
}
