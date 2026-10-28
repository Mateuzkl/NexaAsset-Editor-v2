using System.Text.Json;

namespace NexaAsset.Mcp;

public sealed partial class StudioService
{
    public Func<Uri, StableDiffusionWebUiProvider>? StableDiffusionFactory { get; init; }
    private StableDiffusionWebUiProvider? stableDiffusion;
    private StableDiffusionJob? stableJob;
    private Task stableWork = Task.CompletedTask;
    private sealed record StableDiffusionProbe(StableDiffusionWebUiProvider Provider);
    private sealed class StableDiffusionJob(ArtDocument document, long revision, int frame, int layer, long consent, StableDiffusionWebUiProvider provider)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public ArtDocument Document { get; } = document;
        public long Revision { get; } = revision;
        public int Frame { get; } = frame;
        public int Layer { get; } = layer;
        public long Consent { get; } = consent;
        public StableDiffusionWebUiProvider Provider { get; } = provider;
        public CancellationTokenSource Cancellation { get; } = new();
        public string State { get; set; } = "running";
        public string Message { get; set; } = "Stable Diffusion is generating. The editor remains available.";
        public bool Running => State is "running" or "cancelling";
        public object Info() => new { jobId = Id, editId = Document.Id, status = State, message = Message, expectedDocumentRevision = Revision, documentRevision = Document.Revision, frame = Frame, layer = Layer };
    }
    private object StableDiffusionInfo() => new { configured = stableDiffusion != null, url = stableDiffusion?.Endpoint.AbsoluteUri ?? "http://127.0.0.1:7860/", consent = ProviderConsent, running = stableJob?.Running == true, backend = "AUTOMATIC1111 / Forge WebUI API" };
    private object ConfigureStableDiffusion(string url)
    {
        if (generation != null || stableJob?.Running == true) throw new InvalidOperationException("Cancel or finish the current generation before changing the endpoint.");
        if (StableDiffusionFactory == null) throw new InvalidOperationException("The local Stable Diffusion adapter is unavailable.");
        var endpoint = StableDiffusionWebUiProvider.ValidateEndpoint(new Uri(url, UriKind.Absolute));
        RevokeProvider(); stableDiffusion = StableDiffusionFactory(endpoint); return StableDiffusionInfo();
    }
    private void CancelStableDiffusion()
    {
        if (stableJob?.Running != true) return;
        stableJob.State = "cancelling"; stableJob.Cancellation.Cancel();
    }
    /// <summary>Return a job immediately so AIR/MCP request timeouts never abandon a slow inference call.</summary>
    private object StartStableDiffusion(ArtDocument doc, long revision, JsonElement args)
    {
        if (!ProviderConsent || stableDiffusion == null) throw new InvalidOperationException("Open Stable Diffusion in the Studio, configure/test WebUI or Forge and allow generation first.");
        if (generation != null || stableJob?.Running == true) throw new InvalidOperationException("A generation is already running. Wait or cancel it first.");
        int frame = I(args, "frame"), layer = I(args, "layer");
        byte[] reference = ArtDocument.Layer(doc.State, frame, layer, true).Rgba.ToArray();
        string prompt = S(args, "prompt"); if (prompt.Length is < 1 or > 2048) throw new InvalidDataException("Prompt must have 1–2048 characters.");
        var settings = JsonSerializer.Deserialize<StableDiffusionSettings>(args, ArtDocument.Json) ?? new(); settings.Validate();
        var request = new ArtGenerationRequest(prompt, doc.State.Width, doc.State.Height, reference);
        var job = new StableDiffusionJob(doc, revision, frame, layer, consentGeneration, stableDiffusion); stableJob = job;
        stableWork = Task.Run(() => RunStableDiffusion(job, request, settings)); return job.Info();
    }
    private async Task RunStableDiffusion(StableDiffusionJob job, ArtGenerationRequest request, StableDiffusionSettings settings)
    {
        bool held = false;
        try {
            byte[] rgba = await job.Provider.Generate(request, settings, job.Cancellation.Token);
            await gate.WaitAsync(); held = true;
            job.Cancellation.Token.ThrowIfCancellationRequested();
            await Project(job.Cancellation.Token);
            if (job.Consent != consentGeneration || !ProviderConsent || !ReferenceEquals(stableDiffusion, job.Provider)) throw new OperationCanceledException();
            if (!documents.TryGetValue(job.Document.Id, out var current) || !ReferenceEquals(current, job.Document)) throw new InvalidOperationException("Draft not found or project session expired.");
            current.Edit(job.Revision, state => ArtDocument.Layer(state, job.Frame, job.Layer).Rgba = rgba);
            job.State = "complete"; job.Message = "Generation added to the draft. Review pixels/transparency before applying as a new asset.";
            await Changed(current);
        }
        catch (Exception error) when (error is not OutOfMemoryException) {
            if (!held) { await gate.WaitAsync(); held = true; }
            if (job.State != "complete") {
                job.State = error is OperationCanceledException || job.Cancellation.IsCancellationRequested ? "cancelled" : error.Message.StartsWith("DOCUMENT_CONFLICT", StringComparison.Ordinal) ? "conflict" : "failed";
                job.Message = job.State == "cancelled" ? "Generation cancelled for this draft. The external server may still finish its work." : error is InvalidOperationException or InvalidDataException or FormatException or JsonException ? error.Message : "Stable Diffusion generation failed. No draft change was applied.";
            }
        }
        finally { job.Cancellation.Dispose(); if (held) gate.Release(); }
    }
    /// <summary>The owning AIR process cancels and joins background work before the gateway exits.</summary>
    public async Task StopAsync()
    {
        await gate.WaitAsync(); try { RevokeProvider(); } finally { gate.Release(); }
        await stableWork;
    }
}
