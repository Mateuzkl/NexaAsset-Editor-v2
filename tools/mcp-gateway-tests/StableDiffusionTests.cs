using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NexaAsset.Mcp;
using NexaModern;

internal static class StableDiffusionTests
{
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, ArtDocument.Json);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or FormatException or JsonException or OperationCanceledException) { return; }
        throw new Exception("Accepted invalid Stable Diffusion input: " + message);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => run(request, ct);
    }
    private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static byte[] Solid(int w, int h)
    {
        byte[] rgba = new byte[w * h * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 170; rgba[i + 1] = 30; rgba[i + 2] = 70; rgba[i + 3] = 255; }
        return rgba;
    }
    private static HttpResponseMessage Image(int w = 512, int h = 512) => Response(new { images = new[] { Convert.ToBase64String(Png.Encode(new(w, h, Solid(w, h)))) } });
    public static async Task Run()
    {
        foreach (string url in new[] { "https://example.com", "http://localhost:7860", "http://127.0.0.1:7860/path", "http://127.0.0.1/?secret=x", "http://u:p@127.0.0.1", "file:///tmp/test", "http://192.168.1.1:7860" })
            await Reject(() => { StableDiffusionWebUiProvider.ValidateEndpoint(new(url)); return Task.CompletedTask; }, "non-loopback origin");
        StableDiffusionWebUiProvider.ValidateEndpoint(new("http://[::1]:7860"));
        JsonElement sent = default; string? route = null; int calls = 0;
        using var client = new HttpClient(new Handler(async (request, ct) => {
            calls++; route = request.RequestUri!.AbsolutePath;
            Check(request.Headers.Authorization?.Scheme == "Basic", "API authentication missing");
            if (request.Method == HttpMethod.Get) return Response(new[] { new { title = "test checkpoint" } });
            sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            return Image(sent.GetProperty("width").GetInt32(), sent.GetProperty("height").GetInt32());
        })) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new StableDiffusionWebUiProvider(client, new("http://127.0.0.1:7860"), "test-user", "test-password");
        await provider.Probe(CancellationToken.None); Check(route == "/sdapi/v1/sd-models", "WebUI probe route");
        var reference = new byte[32 * 64 * 4]; reference[0] = 14; reference[1] = 80; reference[2] = 200; reference[3] = 127;
        byte[] savedReference = reference.ToArray();
        var result = await provider.Generate(new("Sauron helmet", 32, 64, reference), new() { Seed = 42, NegativePrompt = "pink", Strength = .25 }, CancellationToken.None);
        Check(route == "/sdapi/v1/img2img" && sent.GetProperty("width").GetInt32() == 256 && sent.GetProperty("height").GetInt32() == 512, "Variation route/aspect ratio");
        Check(sent.GetProperty("batch_size").GetInt32() == 1 && sent.GetProperty("n_iter").GetInt32() == 1 && !sent.GetProperty("save_images").GetBoolean() && sent.GetProperty("do_not_save_samples").GetBoolean(), "Inference batch/save limits");
        Check(sent.GetProperty("seed").GetInt32() == 42 && sent.GetProperty("denoising_strength").GetDouble() == .25 && sent.GetProperty("negative_prompt").GetString()!.Contains("pink"), "Generation settings lost");
        var init = NativePng.ReadArtSheet(Convert.FromBase64String(sent.GetProperty("init_images")[0].GetString()!));
        Check(init.Width == 256 && init.Height == 512 && init.Rgba.AsSpan(0, 4).SequenceEqual(reference.AsSpan(0, 4)), "Nearest-neighbour reference upload");
        Check(result.Length == reference.Length && result[3] == 127 && result[7] == 0 && reference.AsSpan().SequenceEqual(savedReference), "Reference alpha/source pixels changed");
        result = await provider.Generate(new("ring", 32, 32, new byte[4096]), new() { Mode = "create", Background = "opaque" }, CancellationToken.None);
        Check(route == "/sdapi/v1/txt2img" && !sent.TryGetProperty("init_images", out _) && result[3] == 255, "Text-to-image route/output");
        int before = calls;
        await Reject(() => provider.Generate(new("ring", 32, 32, new byte[4096]), new() { Mode = "variation" }, CancellationToken.None), "empty variation");
        await Reject(() => provider.Generate(new("ring", 32, 32, new byte[4096]), new() { Background = "reference" }, CancellationToken.None), "empty alpha reference");
        foreach (var settings in new StableDiffusionSettings[] { new() { Steps = 41 }, new() { Seed = -2 }, new() { CfgScale = double.NaN }, new() { PaletteColors = 256 }, new() { Mode = "workflow" }, new() { NegativePrompt = new('a', 2049) } })
            await Reject(() => provider.Generate(new("ring", 32, 32, new byte[4096]), settings, CancellationToken.None), "invalid options");
        Check(calls == before, "Invalid generation transmitted a reference");

        byte[] pattern = Solid(32, 32);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) if (x < 8 || x >= 24 || y < 8 || y >= 24) { int at = (y * 32 + x) * 4; pattern[at] = pattern[at + 1] = pattern[at + 2] = 240; }
        var transparent = StableDiffusionWebUiProvider.Finish(new(32, 32, pattern), new("ring", 32, 32, new byte[4096]), new());
        Check(transparent[3] == 0 && transparent[(16 * 32 + 16) * 4 + 3] == 255, "Connected border removal erased subject");
        byte[] noise = Solid(32, 32); var random = new Random(7); random.NextBytes(noise); for (int i = 3; i < noise.Length; i += 4) noise[i] = 255;
        var reduced = StableDiffusionWebUiProvider.Finish(new(32, 32, noise), new("ring", 32, 32, new byte[4096]), new() { Background = "opaque", PaletteColors = 16 });
        Check(Enumerable.Range(0, 1024).Select(i => Convert.ToHexString(reduced.AsSpan(i * 4, 3))).Distinct().Count() <= 16, "Palette exceeds requested bound");
        Check(reduced.AsSpan().SequenceEqual(StableDiffusionWebUiProvider.Finish(new(32, 32, noise), new("ring", 32, 32, new byte[4096]), new() { Background = "opaque", PaletteColors = 16 })), "Non-deterministic palette conversion");
        foreach (var response in new Func<HttpResponseMessage>[] {
            () => new(HttpStatusCode.NotFound), () => new(HttpStatusCode.Unauthorized), () => Response(new { images = new[] { "invalid" } }),
            () => Response(new { images = new[] { "a", "b" } }), () => Image(64, 64),
            () => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 8 * 1024 * 1024 + 1)) }
        }) {
            using var badClient = new HttpClient(new Handler((_, _) => Task.FromResult(response())));
            var bad = new StableDiffusionWebUiProvider(badClient, new("http://127.0.0.1:7860"));
            await Reject(() => bad.Generate(new("ring", 32, 32, new byte[4096]), new(), CancellationToken.None), "malformed/oversized/error response");
        }
        using (var emptyClient = new HttpClient(new Handler((_, _) => Task.FromResult(Response(Array.Empty<object>())))))
            await Reject(() => new StableDiffusionWebUiProvider(emptyClient, new("http://127.0.0.1:7860")).Probe(CancellationToken.None), "missing checkpoint");
        using (var stalled = new HttpClient(new Handler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Response(new { }); })) { Timeout = Timeout.InfiniteTimeSpan }) {
            try { await new StableDiffusionWebUiProvider(stalled, new("http://127.0.0.1:7860")).Probe(CancellationToken.None); throw new Exception("Stalled probe did not time out"); }
            catch (InvalidOperationException error) { Check(error.Message.Contains("5 seconds"), "Probe timeout was not actionable"); }
        }
        await Jobs(); await Loopback();
        Console.WriteLine("PASS: Stable Diffusion WebUI HTTP contract, limits, conversion, nonblocking jobs, cancellation, consent/session revocation and stale-result protection (stub server, no GPU inference)");
    }
    private sealed class Bridge : IEditorBridge
    {
        public string Session = "sd-test";
        public Task EmitAsync(object value) => Task.CompletedTask;
        public Task<JsonElement> RequestAsync(string method, object args, CancellationToken cancellation) => Task.FromResult(J(new { projectSessionId = Session, revision = 0 }));
    }
    private static async Task Jobs()
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0;
        using var client = new HttpClient(new Handler(async (_, ct) => { calls++; started.TrySetResult(); await released.Task.WaitAsync(ct); return Image(); })) { Timeout = Timeout.InfiniteTimeSpan };
        var bridge = new Bridge(); var service = new StudioService(bridge) { StableDiffusionFactory = uri => new(client, uri) };
        async Task<JsonElement> Run(string method, object args) => J(await service.Run(method, J(args), true, CancellationToken.None));
        var draft = await Run("begin_edit", new { projectSessionId = bridge.Session, expectedRevision = 0, category = "item" }); string id = draft.GetProperty("editId").GetString()!;
        await Run("ui_sd_configure", new { url = "http://127.0.0.1:7860" }); await Run("ui_provider_consent", new { consent = true });
        var doc = service.Document(id);
        object Parameters() => new { editId = id, expectedDocumentRevision = doc.Revision, prompt = "ring", background = "opaque" };
        async Task<string> Wait()
        {
            using var deadline = new CancellationTokenSource(5000);
            while (true) { var info = await Run("get_generation_status", new { editId = id }); string status = info.GetProperty("status").GetString()!; if (status is not ("running" or "cancelling")) return status; await Task.Delay(10, deadline.Token); }
        }
        void Reset() { started = new(TaskCreationOptions.RunContinuationsAsynchronously); released = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        doc.State.Frames[0].Layers[0].Locked = true;
        await Reject(() => Run("generate_stable_diffusion", Parameters()), "locked layer job"); Check(calls == 0, "Locked layer leaked a reference"); doc.State.Frames[0].Layers[0].Locked = false;
        var watch = Stopwatch.StartNew(); var job = await Run("generate_stable_diffusion", Parameters()); Check(watch.ElapsedMilliseconds < 1000 && job.GetProperty("status").GetString() == "running", "Generation blocked request");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        watch.Restart(); await Run("get_edit_preview", new { editId = id }); Check(watch.ElapsedMilliseconds < 1000, "Provider blocked preview gate");
        await Reject(() => Run("generate_stable_diffusion", Parameters()), "concurrent job");
        await Run("set_pixels", new { editId = id, expectedDocumentRevision = 0, pixels = new[] { new { x = 1, y = 1, rgba = new[] { 2, 3, 4, 255 } } } });
        released.SetResult(); Check(await Wait() == "conflict" && doc.Revision == 1 && doc.State.Frames[0].Layers[0].Rgba[(32 + 1) * 4] == 2, "Late image overwrote manual stroke");
        Reset(); job = await Run("generate_stable_diffusion", Parameters()); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Run("cancel_generation", new { editId = id, jobId = "old-window" }); Check((await Run("get_generation_status", new { editId = id })).GetProperty("status").GetString() == "running", "Old window cancelled another job");
        await Run("cancel_generation", new { editId = id, jobId = job.GetProperty("jobId").GetString() }); Check(await Wait() == "cancelled" && doc.Revision == 1, "Cancel published pixels");
        Reset(); await Run("generate_stable_diffusion", Parameters()); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Run("ui_provider_consent", new { consent = false }); Check(await Wait() == "cancelled", "Consent revocation published pixels");
        Reset(); await Run("ui_provider_consent", new { consent = true }); await Run("generate_stable_diffusion", Parameters()); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        released.SetResult(); Check(await Wait() == "complete" && doc.Revision == 2, "Successful generation did not publish exactly one revision");
        doc.History(2, false); Check(doc.State.Frames[0].Layers[0].Rgba[(32 + 1) * 4] == 2, "Generated image cannot undo");
        Reset(); await Run("generate_stable_diffusion", Parameters()); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closing = await Run("ui_begin_close", new { }); Check(closing.GetProperty("interrupted").GetBoolean() && await Wait() == "cancelled", "Close did not cancel slow SD work");
        await Run("ui_end_close", new { }); await Run("ui_provider_consent", new { consent = true });
        Reset(); await Run("generate_stable_diffusion", Parameters()); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bridge.Session = "other-project"; await Run("get_project_info", new { }); await service.StopAsync(); Check(doc.Revision == 3, "Old session received a generation");
    }
    private static async Task Loopback()
    {
        // Exercise actual HTTP with a local WebUI-shaped server, independently of the handler unit tests.
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
        var work = Task.Run(async () => {
            for (int i = 0; i < 2; i++) {
                var context = await server.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                object result;
                if (context.Request.HttpMethod == "GET") { Check(context.Request.Url!.AbsolutePath == "/sdapi/v1/sd-models", "HTTP probe path"); result = new[] { new { title = "stub" } }; }
                else { using var reader = new StreamReader(context.Request.InputStream); using var json = JsonDocument.Parse(await reader.ReadToEndAsync()); Check(context.Request.Url!.AbsolutePath == "/sdapi/v1/txt2img" && json.RootElement.GetProperty("width").GetInt32() == 512, "HTTP generation contract"); result = new { images = new[] { Convert.ToBase64String(Png.Encode(new(512, 512, Solid(512, 512)))) } }; }
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result); context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
            }
        });
        try {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
            var provider = new StableDiffusionWebUiProvider(http, new($"http://127.0.0.1:{port}"));
            await provider.Probe(CancellationToken.None); var pixels = await provider.Generate(new("potion", 32, 32, new byte[4096]), new() { Background = "opaque" }, CancellationToken.None);
            Check(pixels.Length == 4096 && pixels[0] == 170 && pixels[3] == 255, "Real HTTP output conversion"); await work;
        } finally { server.Stop(); }
    }
}
