using System.Text.Json;

namespace NexaAsset.Mcp;

/// <summary>Backward-compatible four-pose breathing contract implemented by the shared idle engine.</summary>
public static class BreathingGenerator
{
    /// <summary>Retain the original parameter names, four durations, amplitudes and anchored breathing poses.</summary>
    public static void Generate(ArtState state, JsonElement p, CancellationToken cancellation = default)
    {
        IdleAnimationGenerator.Keys(p, "editId", "expectedDocumentRevision", "frame", "layer", "amplitude", "durationsMs", "regionMode", "regions");
        int amplitude = IdleAnimationGenerator.Integer(p, "amplitude", 1, 1, 2);
        int[] durations = IdleAnimationGenerator.Durations(p, 4, [500, 300, 500, 300]);
        IdleAnimationGenerator.Render(state, p, new(true, false, false, amplitude, 0, 0, durations, true, RequireAllDirections: true), cancellation);
    }
}
