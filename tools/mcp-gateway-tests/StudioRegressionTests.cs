using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NexaAsset.Mcp;
using NexaModern;

internal static class StudioRegressionTests
{
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, ArtDocument.Json);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { return; }
        throw new Exception(message);
    }
    private sealed class Bridge : IEditorBridge
    {
        public string Session = "test";
        public long Revision;
        public string? CommittedCategory;
        public Task EmitAsync(object value) => Task.CompletedTask;
        public Task<JsonElement> RequestAsync(string method, object args, CancellationToken cancellation)
        {
            if (method == "get_project_info") return Task.FromResult(J(new { projectSessionId = Session, revision = Revision }));
            if (method == "commit_art") {
                var packet = JsonDocument.Parse(Convert.FromBase64String(J(args).GetProperty("packet").GetString()!));
                CommittedCategory = packet.RootElement.GetProperty("category").GetString(); Revision++;
                return Task.FromResult(J(new { selected = 1, category = CommittedCategory }));
            }
            throw new InvalidOperationException(method);
        }
    }
    private sealed class Provider : IArtGenerationProvider
    {
        public int Calls;
        public TaskCompletionSource<byte[]> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<byte[]> Generate(ArtGenerationRequest request, CancellationToken cancellation)
        {
            Calls++; Started.TrySetResult();
            // Simulate a provider whose response may take 25s; cancellation must remain responsive.
            return await Result.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellation);
        }
        public void Reset() { Result = new(TaskCreationOptions.RunContinuationsAsynchronously); Started = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    }
    public static async Task Run()
    {
        var document = ArtDocument.Create("test", 0, 32, 32);
        document.Edit(0, state => {
            state.Frames = Enumerable.Range(0, 3).Select(i => new ArtFrame { Duration = 100 + i * 20, Layers = [new() { Rgba = Enumerable.Repeat((byte)(10 + i), 4096).ToArray() }] }).ToList();
        });
        document.Edit(1, state => ArtDocument.SetAnimation(state, 3, 4, null));
        for (int i = 0; i < 12; i++) Check(document.State.Frames[i].Layers[0].Rgba[0] == 10 + i / 4 && document.State.Frames[i].Duration == 100 + i / 4 * 20, "1→4 mixed time/directions/timings");
        var sheetReview = J(document.Sheet());
        Check(sheetReview.GetProperty("columns").GetInt32() == 4 && sheetReview.GetProperty("rows").GetInt32() == 3, "Outfit sheet axes changed");
        Check(sheetReview.GetProperty("directionOrder").EnumerateArray().Select(d => d.GetString()).SequenceEqual(new[] { "north", "east", "south", "west" }), "Native direction order changed");
        foreach (var cell in sheetReview.GetProperty("cells").EnumerateArray()) {
            int index = cell.GetProperty("index").GetInt32();
            Check(cell.GetProperty("time").GetInt32() == index / 4 && cell.GetProperty("direction").GetInt32() == index % 4 && Convert.FromBase64String(cell.GetProperty("rgba").GetString()!)[0] == 10 + index / 4, "Sheet review remapped cell pixels/times");
        }
        object retainedSheet = document.Sheet(), retainedPreview = document.Preview(11, true, true);
        document.Edit(2, state => ArtDocument.SetAnimation(state, 5, 1, null));
        Check(J(retainedSheet).GetProperty("cells").GetArrayLength() == 12 && J(retainedPreview).GetProperty("timeline")[11].GetProperty("direction").GetInt32() == 3, "Deferred response observed a newer draft state");
        for (int i = 0; i < 5; i++) Check(document.State.Frames[i].Layers[0].Rgba[0] == 10 + Math.Min(i, 2), "4→1/extended time mapping");
        document.Edit(3, state => ArtDocument.SetAnimation(state, 1, 1, null));
        document.History(4, false); Check(document.State.Frames.Count == 5, "Animation resize undo");

        var stroke = ArtDocument.Create("test", 0, 32, 32);
        var delta = J(new[] { new { x = 0, y = 0, before = new[] { 0, 0, 0, 0 }, rgba = new[] { 7, 8, 9, 127 } } });
        stroke.Edit(0, state => ArtDocument.SetPixels(state, 0, 0, J(new[] { new { x = 3, y = 3, rgba = new[] { 1, 2, 3, 255 } } })));
        stroke.Stroke(0, 0, 0, delta, false);
        Check(stroke.State.Frames[0].Layers[0].Rgba[3] == 127 && stroke.State.Frames[0].Layers[0].Rgba[(3 * 32 + 3) * 4 + 3] == 255, "Concurrent non-overlapping stroke lost pixels");
        byte[] beforeConflict = stroke.Sidecar();
        Reject(() => stroke.Stroke(0, 0, 0, J(new[] { new { x = 0, y = 0, before = new[] { 0, 0, 0, 0 }, rgba = new[] { 4, 5, 6, 255 } } }), false), "Overlapping stroke silently overwrote remote data");
        Check(stroke.Sidecar().AsSpan().SequenceEqual(beforeConflict), "Conflicting stroke was not atomic");
        for (int i = 0; i < 1000; i++) stroke.Edit(stroke.Revision, state => state.Frames[0].Layers[0].Rgba[20] = (byte)(i % 256));
        int undos = 0; while (true) { try { stroke.History(stroke.Revision, false); undos++; } catch (InvalidOperationException) { break; } }
        Check(undos == 32, "History is not bounded to 32 strokes");

        var bridge = new Bridge(); var provider = new Provider(); var service = new StudioService(bridge) { Provider = provider };
        async Task<JsonElement> Run(string method, object args) => J(await service.Run(method, J(args), true, CancellationToken.None));
        var draft = await Run("begin_edit", new { projectSessionId = "test", expectedRevision = 0, width = 32, height = 64, category = "outfit", name = "Armour" });
        string id = draft.GetProperty("editId").GetString()!;
        Check(draft.GetProperty("category").GetString() == "outfit" && draft.GetProperty("directions").GetInt32() == 4, "Outfit draft lost its category or directions");
        var outfit = service.Document(id); var restored = ArtDocument.Create("test", 0, 32, 32); restored.Load(0, outfit.Sidecar());
        Check(restored.State.Category == "outfit" && restored.State.Name == "Armour", "Sidecar lost destination/name");
        await Run("ui_apply", new { editId = id, expectedDocumentRevision = 0 }); Check(bridge.CommittedCategory == "outfit", "Apply inserted an outfit as an item");
        draft = await Run("begin_edit", new { projectSessionId = "test", expectedRevision = bridge.Revision }); id = draft.GetProperty("editId").GetString()!;
        var doc = service.Document(id);
        await Run("ui_provider_consent", new { consent = true }); doc.State.Frames[0].Layers[0].Locked = true;
        try { await Run("generate_variation", new { editId = id, expectedDocumentRevision = 0, prompt = "Potion" }); throw new Exception("Locked layer sent to provider"); } catch (InvalidOperationException) { }
        Check(provider.Calls == 0, "Provider called before checking layer lock"); doc.State.Frames[0].Layers[0].Locked = false;
        var generated = Run("generate_variation", new { editId = id, expectedDocumentRevision = 0, prompt = "Potion" }); await provider.Started.Task;
        var watch = Stopwatch.StartNew(); await Run("get_edit_preview", new { editId = id }); Check(watch.ElapsedMilliseconds < 1000, "Slow provider blocked preview");
        await Run("set_pixels", new { editId = id, expectedDocumentRevision = 0, pixels = new[] { new { x = 1, y = 1, rgba = new[] { 2, 3, 4, 255 } } } });
        provider.Result.SetResult(new byte[4096]);
        try { await generated; throw new Exception("Late generation overwrote a newer stroke"); } catch (InvalidOperationException e) { Check(e.Message.Contains("DOCUMENT_CONFLICT"), "Wrong generation conflict"); }
        provider.Reset(); generated = Run("generate_variation", new { editId = id, expectedDocumentRevision = 1, prompt = "Potion" }); await provider.Started.Task;
        watch.Restart(); await Run("ui_provider_consent", new { consent = false });
        try { await generated; throw new Exception("Consent revocation allowed generation"); } catch (OperationCanceledException) { }
        Check(watch.ElapsedMilliseconds < 1000, "Consent cancellation blocked on provider");
        provider.Reset(); await Run("ui_provider_consent", new { consent = true }); generated = Run("generate_variation", new { editId = id, expectedDocumentRevision = 1, prompt = "Potion" }); await provider.Started.Task;
        await Run("cancel_edit", new { editId = id });
        try { await generated; throw new Exception("Cancelled draft received provider result"); } catch (OperationCanceledException) { }
        byte[] secret = service.Secret; await Run("ui_suspend", new { }); Check(!service.Enabled && !service.ProviderConsent && !secret.AsSpan().SequenceEqual(service.Secret), "Close did not revoke access/consent/token");

        // Every accepted export must be accepted by the same sheet decoder/importer.
        foreach (int size in new[] { 32, 64 }) {
            draft = await Run("begin_edit", new { projectSessionId = "test", expectedRevision = bridge.Revision, width = size, height = size }); id = draft.GetProperty("editId").GetString()!;
            doc = service.Document(id);
            doc.Edit(0, state => { ArtDocument.SetAnimation(state, 64, 1, null); var random = new Random(123); foreach (var frame in state.Frames) random.NextBytes(frame.Layers[0].Rgba); });
            foreach (int columns in new[] { 1, 2, 4, 8, 16 }) {
                if (!NativePng.ValidArtSheetSize(size * columns, size * (64 / columns))) {
                    try { await Run("export_spritesheet", new { editId = id, columns }); throw new Exception("Export produced an unimportable atlas"); } catch (InvalidDataException e) { Check(e.Message.Contains("columns="), "Missing compatible column suggestion"); }
                    continue;
                }
                var sheet = await Run("export_spritesheet", new { editId = id, columns }); byte[] png = Convert.FromBase64String(sheet.GetProperty("png").GetString()!);
                if (size == 64) Check(png.Length > 1024 * 1024, "Entropy fixture did not exercise >1 MiB sheet");
                var pixels = NativePng.ReadArtSheet(png); Check(pixels.Rgba.Length == 64 * size * size * 4, "Atlas pixel count changed");
                await Run("import_spritesheet", new { editId = id, expectedDocumentRevision = doc.Revision, png = sheet.GetProperty("png").GetString(), cellWidth = size, cellHeight = size, directions = 1, cells = sheet.GetProperty("metadata").GetProperty("cells") });
                var roundtrip = await Run("export_spritesheet", new { editId = id, columns }); Check(roundtrip.GetProperty("png").GetString() == sheet.GetProperty("png").GetString(), "High-entropy atlas roundtrip changed pixels");
            }
            await Run("cancel_edit", new { editId = id });
        }
        var retained = await Run("ui_stroke_png", new { width = 32, height = 32, rgba = Convert.ToBase64String(stroke.State.Frames[0].Layers[0].Rgba) });
        Check(NativePng.Read(Convert.FromBase64String(retained.GetProperty("data").GetString()!)).Rgba.AsSpan().SequenceEqual(stroke.State.Frames[0].Layers[0].Rgba), "Retained stroke export changed hidden RGB/alpha");
        await Lines();
        Console.WriteLine("PASS: temporal/directional remapping, sparse stroke merge/conflicts, 1000 strokes/bounded undo, typed outfits/apply/sidecar, locked/slow/stale/cancelled provider, consent/token revocation, 64-cell high-entropy atlases and bounded chunked JSONL");
    }
    private static async Task Lines()
    {
        foreach (int size in new[] { 4096, 65536, 1024 * 1024, BoundedJsonLineReader.MaxLineBytes }) {
            var samples = new List<double>();
            for (int i = 0; i < 5; i++) {
                using var input = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', size) + "\n{\"nome\":\"Poção\"}\n"));
                var reader = new BoundedJsonLineReader(input); var watch = Stopwatch.StartNew();
                Check((await reader.ReadLineAsync(default))!.Length == size, "Fragmented bridge line changed size");
                Check(await reader.ReadLineAsync(default) == "{\"nome\":\"Poção\"}", "Multiple lines/UTF-8 in one block lost bytes");
                Check(await reader.ReadLineAsync(default) == null, "Empty EOF"); samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            samples.Sort(); Console.WriteLine($"BENCH JSONL {size} bytes p50={samples[2]:F2}ms p95={samples[4]:F2}ms");
        }
        foreach (string text in new[] { "incomplete", new string('x', BoundedJsonLineReader.MaxLineBytes + 1) + "\n" }) {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
            try { await new BoundedJsonLineReader(input).ReadLineAsync(default); throw new Exception("Invalid bridge line accepted"); } catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { }
        }
    }
}
