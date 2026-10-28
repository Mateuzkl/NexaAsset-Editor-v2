using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NexaAsset.Mcp;
using NexaModern;

internal static class GatewayTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, ArtDocument.Json);
    private static JsonElement Result(CallToolResult result)
    {
        if (result.IsError == true) throw new Exception(JsonSerializer.Serialize(result));
        if (result.StructuredContent is { } structured) return structured;
        return JsonDocument.Parse(string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text))).RootElement.Clone();
    }
    private static async Task<JsonElement> Call(McpClient client, string name, object parameters) => Result(await client.CallToolAsync(name, J(parameters).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value)));
    private static HttpClientTransport Transport(string endpoint, string token) => new(new() { Endpoint = new(endpoint), AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token } });

    public static async Task Live()
    {
        // The AIR harness supplies credentials on a private pipe, never in arguments/files/logs.
        using var credentials = JsonDocument.Parse((await Console.In.ReadLineAsync())!); var values = credentials.RootElement;
        await using var transport = Transport(values.GetProperty("endpoint").GetString()!, values.GetProperty("token").GetString()!);
        await using var client = await McpClient.CreateAsync(transport);
        var info = await Call(client, "nexa_get_project_info", new { });
        var list = await Call(client, "nexa_list_assets", new { category = "item" });
        Check(list.GetProperty("items").GetArrayLength() > 0, "Live editor returned no real items");
        var draft = await Call(client, "nexa_begin_edit", new { projectSessionId = info.GetProperty("projectSessionId").GetString(), expectedRevision = info.GetProperty("revision").GetInt64(), width = 32, height = 64, category = "outfit", name = "MCP regression outfit" });
        string editId = draft.GetProperty("editId").GetString()!;
        await Call(client, "nexa_set_animation", new { editId, expectedDocumentRevision = 0, parameters = new { frames = 4, directions = 4, duration = 100 } });
        var edited = await Call(client, "nexa_set_pixels", new { editId, expectedDocumentRevision = 1, parameters = new { pixels = new[] { new { x = 1, y = 1, rgba = new[] { 23, 85, 171, 127 } } } } });
        var fullSheet = await Call(client, "nexa_get_edit_sheet", new { editId });
        Check(fullSheet.GetProperty("columns").GetInt32() == 4 && fullSheet.GetProperty("rows").GetInt32() == 4 && fullSheet.GetProperty("cells").GetArrayLength() == 16, "MCP outfit sheet preview missing");
        Console.WriteLine(JsonSerializer.Serialize(new { state = "drawing", editId }));
        Check(await Console.In.ReadLineAsync() == "continue", "Editor did not observe live MCP drawing before approval");
        var requested = await Call(client, "nexa_commit_edit", new { editId, expectedDocumentRevision = edited.GetProperty("documentRevision").GetInt64(), parameters = new { } });
        Check(requested.GetProperty("status").GetString() == "awaiting-human-approval", "MCP bypassed approval");
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, editId, format = info.GetProperty("format").GetString() }));
    }

    public static async Task Run()
    {
        string gateway = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../bin-debug/mcp-gateway/nexa-mcp-gateway.exe"));
        using var process = new Process { StartInfo = new(gateway) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        Check(process.Start(), "Gateway launch");
        process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
        var output = Channel.CreateUnbounded<JsonElement>(); var writeLock = new SemaphoreSlim(1);
        int editorCommits = 0; string session = "project-A"; long revision = 9;
        async Task Write(object value)
        {
            await writeLock.WaitAsync();
            try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value)); await process.StandardInput.FlushAsync(); }
            finally { writeLock.Release(); }
        }
        var reader = Task.Run(async () => {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                using var json = JsonDocument.Parse(line); var m = json.RootElement.Clone();
                if (m.GetProperty("type").GetString() == "editorRequest")
                {
                    string method = m.GetProperty("method").GetString()!; object result;
                    if (method == "get_project_info") result = new { projectSessionId = session, revision, format = "test-editor" };
                    else if (method == "get_sprite") result = new { width = 32, height = 32, argb = Convert.ToBase64String(new byte[4096]) };
                    else if (method == "commit_art") { editorCommits++; revision++; result = new { selected = 101, sprite = 4 }; }
                    else result = new { items = new[] { new { id = 100 } } };
                    await Write(new { type = "editorResponse", id = m.GetProperty("id").GetString(), result });
                }
                else await output.Writer.WriteAsync(m);
            }
        });
        async Task<JsonElement> Next(string type, string? id = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true) { var value = await output.Reader.ReadAsync(timeout.Token); if (value.GetProperty("type").GetString() == type && (id == null || value.GetProperty("id").GetString() == id)) return value; }
        }
        try
        {
            var ready = await Next("ready"); string endpoint = ready.GetProperty("endpoint").GetString()!, token = ready.GetProperty("token").GetString()!;
            using var http = new HttpClient();
            Check((await http.PostAsync(endpoint, new StringContent("{}"))).StatusCode == HttpStatusCode.Unauthorized, "Unauthenticated access");
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            Check((await http.PostAsync(endpoint, new StringContent("{}"))).StatusCode == HttpStatusCode.Forbidden, "Disabled gateway");
            using var origin = new HttpRequestMessage(HttpMethod.Post, endpoint); origin.Headers.Add("Origin", "https://evil.invalid");
            Check((await http.SendAsync(origin)).StatusCode == HttpStatusCode.Forbidden, "Remote Origin");
            using var host = new HttpRequestMessage(HttpMethod.Post, endpoint); host.Headers.Host = "evil.invalid";
            Check((await http.SendAsync(host)).StatusCode == HttpStatusCode.Forbidden, "DNS rebinding Host");
            await Write(new { type = "uiRequest", id = "enable", method = "ui_enable", args = new { enabled = true } }); await Next("uiResponse", "enable");
            await using var transport = Transport(endpoint, token); await using var client = await McpClient.CreateAsync(transport);
            var tools = await client.ListToolsAsync(); Check(tools.Count >= 20 && tools.All(t => !t.Name.Contains("save")), "Tool discovery/scopes");
            Check(new[] { "nexa_generate_stable_diffusion", "nexa_get_generation_status", "nexa_cancel_generation" }.All(name => tools.Any(t => t.Name == name)), "Stable Diffusion MCP discovery");
            var project = await Call(client, "nexa_get_project_info", new { }); Check(project.GetProperty("revision").GetInt64() == 9, "Editor bridge read");
            var spriteResponse = await client.CallToolAsync("nexa_get_sprite", new Dictionary<string, object?> { ["spriteId"] = 1 });
            Check(spriteResponse.Content.OfType<ImageContentBlock>().Any(i => i.MimeType == "image/png"), "MCP sprite image block missing");
            var draft = await Call(client, "nexa_begin_edit", new { projectSessionId = session, expectedRevision = 9, category = "item", spriteId = 1 }); string editId = draft.GetProperty("editId").GetString()!;
            var unavailable = await client.CallToolAsync("nexa_generate_stable_diffusion", new Dictionary<string, object?> { ["editId"] = editId, ["expectedDocumentRevision"] = 0, ["parameters"] = new { prompt = "ring" } });
            Check(unavailable.IsError == true && unavailable.Content.OfType<TextContentBlock>().Any(t => t.Text.Contains("configure/test")), "Missing SD configuration did not surface as MCP error");
            Check((await Call(client, "nexa_get_generation_status", new { editId })).GetProperty("status").GetString() == "none", "Unexpected MCP generation job");
            await StableDiffusionGateway(client, Write, Next, session, revision);
            await Call(client, "nexa_set_pixels", new { editId, expectedDocumentRevision = 0, parameters = new { pixels = new[] { new { x = 3, y = 3, rgba = new[] { 1, 2, 3, 128 } } } } });
            var missing = await client.CallToolAsync("nexa_set_pixels", new Dictionary<string, object?> { ["editId"] = editId, ["expectedDocumentRevision"] = 1, ["parameters"] = new { } });
            Check(missing.IsError == true && missing.Content.OfType<TextContentBlock>().Any(t => t.Text.Contains("Missing a required operation parameter")), "Missing nested parameter did not surface as actionable MCP error");
            var preview = await Call(client, "nexa_get_edit_preview", new { editId }); Check(preview.GetProperty("changedPixels").GetInt32() == 1, "MCP pixel diff");
            var approve = await Call(client, "nexa_commit_edit", new { editId, expectedDocumentRevision = 1, parameters = new { } });
            Check(approve.GetProperty("status").GetString() == "awaiting-human-approval" && editorCommits == 0, "Remote approval bypass");
            var stale = await client.CallToolAsync("nexa_set_pixels", new Dictionary<string, object?> { ["editId"] = editId, ["expectedDocumentRevision"] = 0, ["parameters"] = new { pixels = Array.Empty<object>() } }); Check(stale.IsError == true && stale.Content.OfType<TextContentBlock>().Any(c => c.Text.Contains("DOCUMENT_CONFLICT")), "Concurrent stale edit accepted");
            var sheet = await Call(client, "nexa_export_spritesheet", new { editId }); Check(sheet.GetProperty("metadata").GetProperty("cells").GetArrayLength() == 1, "Sheet mapping");
            await Call(client, "nexa_set_animation", new { editId, expectedDocumentRevision = 1, parameters = new { frames = 3, directions = 4, duration = 120 } });
            await Call(client, "nexa_set_pixels", new { editId, expectedDocumentRevision = 2, parameters = new { frame = 4, layer = 0, pixels = new[] { new { x = 6, y = 6, rgba = new[] { 231, 79, 31, 127 } } } } });
            var atlas = await Call(client, "nexa_export_spritesheet", new { editId, columns = 4 });
            byte[] image = Convert.FromBase64String(atlas.GetProperty("png").GetString()!);
            var decoded = NexaModern.NativePng.ReadArtSheet(image);
            Check(decoded.Width == 128 && decoded.Height == 96, "Atlas geometry");
            try { NexaModern.NativePng.Read(image); throw new Exception("Native sprite decoder accepted sheet dimensions"); } catch (InvalidDataException) { }
            var another = await Call(client, "nexa_begin_edit", new { projectSessionId = session, expectedRevision = 9, category = "item" }); string otherId = another.GetProperty("editId").GetString()!;
            await Call(client, "nexa_import_spritesheet", new { editId = otherId, expectedDocumentRevision = 0, parameters = new { png = atlas.GetProperty("png").GetString(), cellWidth = 32, cellHeight = 32, directions = 4, cells = atlas.GetProperty("metadata").GetProperty("cells") } });
            var roundtrip = await Call(client, "nexa_export_spritesheet", new { editId = otherId, columns = 4 });
            Check(atlas.GetProperty("png").GetString() == roundtrip.GetProperty("png").GetString(), "Atlas roundtrip changed cell pixels/order/alpha");
            Console.WriteLine("PASS: spritesheet PNG/JSON import/export with 4 directions x 3 frames, distinct cells, exact partial alpha and duration/mapping");
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.CallToolAsync("nexa_transform_sprite", new Dictionary<string, object?> { ["editId"] = editId, ["expectedDocumentRevision"] = 3, ["parameters"] = new { operation = "flipX", frame = 0, layer = 0 } }).AsTask()));
            Check(concurrent.Count(r => r.IsError == true) == 1, "Two simultaneous clients modified the same document revision");
            revision = 10;
            await Write(new { type = "uiRequest", id = "stale", method = "ui_apply", args = new { editId, expectedDocumentRevision = 4, category = "item" } });
            var conflict = await Next("uiResponse", "stale"); Check(conflict.TryGetProperty("error", out _), "Human concurrent project conflict");
            session = "project-B";
            var expired = await client.CallToolAsync("nexa_get_edit_preview", new Dictionary<string, object?> { ["editId"] = editId }); Check(expired.IsError == true && expired.Content.OfType<TextContentBlock>().Any(c => c.Text.Contains("Draft not found or project session expired.")), "Old project draft remained usable");
            using var oversized = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":55,\"method\":\"tools/list\",\"params\":{\"padding\":\"" + new string('x', 3 * 1024 * 1024) + "\"}}", System.Text.Encoding.UTF8, "application/json") };
            oversized.Headers.Accept.ParseAdd("application/json, text/event-stream");
            var rejected = await http.SendAsync(oversized);
            Check(rejected.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, "Oversized HTTP request was not rejected safely");
            Console.WriteLine("PASS: official MCP client discovery/read/draft/pixels/preview, auth, disabled state, Host/Origin, approval isolation, editor/document conflicts, session switch and sheet mapping");
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(5000)) { process.Kill(true); await process.WaitForExitAsync(); throw new Exception("Gateway failed to terminate after closing its owning editor pipe"); }
            await reader;
        }
    }
    private static async Task StableDiffusionGateway(McpClient client, Func<object, Task> write, Func<string, string?, Task<JsonElement>> next, string session, long revision)
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        using var server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start();
        var response = Task.Run(async () => {
            var context = await server.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            using var input = new StreamReader(context.Request.InputStream); using var body = JsonDocument.Parse(await input.ReadToEndAsync());
            Check(context.Request.Url!.AbsolutePath == "/sdapi/v1/txt2img" && body.RootElement.GetProperty("width").GetInt32() == 512, "MCP SD route/body contract");
            byte[] rgba = new byte[512 * 512 * 4]; for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 19; rgba[i + 3] = 255; }
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { images = new[] { Convert.ToBase64String(Png.Encode(new(512, 512, rgba))) } });
            context.Response.ContentType = "application/json"; context.Response.ContentLength64 = json.Length; await context.Response.OutputStream.WriteAsync(json); context.Response.Close();
        });
        try {
            await write(new { type = "uiRequest", id = "sd-config", method = "ui_sd_configure", args = new { url = $"http://127.0.0.1:{port}" } });
            Check(!(await next("uiResponse", "sd-config")).TryGetProperty("error", out _), "Private SD configuration failed");
            await write(new { type = "uiRequest", id = "sd-consent", method = "ui_provider_consent", args = new { consent = true } }); await next("uiResponse", "sd-consent");
            var draft = await Call(client, "nexa_begin_edit", new { projectSessionId = session, expectedRevision = revision, category = "item" }); string id = draft.GetProperty("editId").GetString()!;
            var job = await Call(client, "nexa_generate_stable_diffusion", new { editId = id, expectedDocumentRevision = 0, parameters = new { prompt = "potion", mode = "create", background = "opaque" } });
            Check(job.GetProperty("status").GetString() == "running", "MCP SD did not return a job immediately");
            using var deadline = new CancellationTokenSource(10000);
            while (true) {
                var status = await Call(client, "nexa_get_generation_status", new { editId = id }); string state = status.GetProperty("status").GetString()!;
                if (state == "complete") { Check(status.GetProperty("documentRevision").GetInt32() == 1, "MCP SD revision count"); break; }
                Check(state == "running", "MCP SD failed: " + state); await Task.Delay(100, deadline.Token);
            }
            var preview = await Call(client, "nexa_get_edit_preview", new { editId = id }); byte[] pixels = Convert.FromBase64String(preview.GetProperty("rgba").GetString()!);
            Check(pixels.Length == 4096 && pixels[0] == 19 && pixels[3] == 255, "MCP SD pixels did not reach the shared draft");
            await Call(client, "nexa_cancel_generation", new { editId = id }); await Call(client, "nexa_cancel_edit", new { editId = id }); await response;
            Console.WriteLine("PASS: official MCP client → live gateway → local WebUI stub → completed shared draft and exact RGBA");
        } finally { server.Stop(); try { await response; } catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { } }
    }
}
