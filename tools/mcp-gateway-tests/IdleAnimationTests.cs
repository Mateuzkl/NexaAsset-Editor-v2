using System.Security.Cryptography;
using System.Text.Json;
using NexaAsset.Mcp;
using NexaModern;

internal static class IdleAnimationTests
{
    private static JsonElement J(object v) => JsonSerializer.SerializeToElement(v, ArtDocument.Json);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Hash(ArtState s) => Convert.ToHexString(SHA256.HashData(s.Frames.SelectMany(f => f.Layers.SelectMany(l => l.Rgba)).ToArray()));
    /// <summary>Verify all effect combinations against exact pixel, anchoring, history and bounds invariants.</summary>
    public static async Task Run(string root)
    {
        foreach (int w in new[] { 32, 64 }) foreach (int h in new[] { 32, 64 }) foreach (int dirs in new[] { 1, 4 }) foreach (int bits in Enumerable.Range(1, 7)) foreach (int amplitude in new[] { 1, 2 })
        {
            var original = BreathingTests.Fixture(w, h, dirs); string before = Hash(original);
            var parameters = new { breathing = (bits & 1) != 0, bobbing = (bits & 2) != 0, sway = (bits & 4) != 0, breathingAmplitude = amplitude, bobbingAmplitude = amplitude, swayAmplitude = amplitude, anchorBase = true };
            var state = ArtDocument.Copy(original); IdleAnimationGenerator.Generate(state, J(parameters));
            Check(state.Frames.Count == dirs * 4, "Idle frame/direction count");
            for (int phase = 0; phase < 4; phase++) for (int d = 0; d < dirs; d++)
            {
                var output = state.Frames[phase * dirs + d].Layers[0].Rgba; var source = original.Frames[d].Layers[0].Rgba;
                Check(state.Frames[phase * dirs + d].Duration == (phase % 2 == 0 ? 500 : 300), "Idle phase timing/order");
                if (phase == 0) Check(output.AsSpan().SequenceEqual(source), "Neutral idle pose changed");
                Check(output.AsSpan((h - 8) * w * 4).SequenceEqual(source.AsSpan((h - 8) * w * 4)), "Anchored idle moved feet/base");
                var palette = Enumerable.Range(0, w * h).Select(p => BitConverter.ToUInt32(source, p * 4)).ToHashSet();
                Check(Enumerable.Range(0, w * h).All(p => palette.Contains(BitConverter.ToUInt32(output, p * 4))), "Idle invented RGBA colors");
            }
            Check(Hash(state) != before, "Idle generated no movement");
            var again = ArtDocument.Copy(original); IdleAnimationGenerator.Generate(again, J(parameters)); Check(Hash(again) == Hash(state), "Idle determinism");
            Check(Hash(original) == before, "Idle modified the input reference");
        }
        foreach (bool anchor in new[] { true, false }) foreach (int count in new[] { 3, 6, 8, 16 })
        {
            var original = BreathingTests.Fixture(64, 32, 4); var state = ArtDocument.Copy(original);
            var regions = Enumerable.Range(0, 4).Select(d => new { direction = d, x = 3 + d, y = 2, width = 44, height = 23 }).ToArray();
            IdleAnimationGenerator.Generate(state, J(new { breathing = true, bobbing = true, sway = true, frameCount = count, durationsMs = Enumerable.Repeat(100, count).ToArray(), anchorBase = anchor, regionMode = "rectangle", regions }));
            Check(state.Frames.Count == count * 4, "Configurable idle frame count");
            for (int p = 0; p < count; p++) for (int d = 0; d < 4; d++) for (int y = 0; y < 32; y++) for (int x = 0; x < 64; x++)
                if (x < regions[d].x || x >= regions[d].x + 44 || y < 2 || y >= 25)
                    Check(state.Frames[p * 4 + d].Layers[0].Rgba.AsSpan((y * 64 + x) * 4, 4).SequenceEqual(original.Frames[d].Layers[0].Rgba.AsSpan((y * 64 + x) * 4, 4)), "Idle changed bytes outside manual ROI");
        }
        foreach (object p in new object[] { new { frameCount = 2 }, new { frameCount = 17 }, new { bobbing = "true" }, new { anchorBase = 1 }, new { breathingAmplitude = 3 }, new { swayAmplitude = 0 }, new { bobbingAmplitude = 1.5 }, new { unknown = 1 }, new { durationsMs = new[] { 100, 100 } }, new { breathing = false, bobbing = false, sway = false }, new { regionMode = "rectangle", regions = Array.Empty<object>() } })
            Reject(BreathingTests.Fixture(32, 32), J(p));
        Reject(ArtDocument.Create("empty", 0, 32, 32).State, J(new { }));
        var originalDirections = BreathingTests.Fixture(32, 64, 4);
        var oneDirection = ArtDocument.Copy(originalDirections);
        IdleAnimationGenerator.Generate(oneDirection, J(new { direction = 2, regionMode = "rectangle", regions = new[] { new { direction = 2, x = 4, y = 2, width = 24, height = 50 } } }));
        Check(!oneDirection.Frames[10].Layers[0].Rgba.AsSpan().SequenceEqual(originalDirections.Frames[2].Layers[0].Rgba), "Selected direction has no motion");
        for (int i = 0; i < oneDirection.Frames.Count; i++)
            if (i % 4 != 2) Check(oneDirection.Frames[i].Layers[0].Rgba.AsSpan().SequenceEqual(originalDirections.Frames[i % 4].Layers[0].Rgba), "Single-direction motion changed another face");
        Reject(originalDirections, J(new { direction = 4 }));
        Reject(originalDirections, J(new { direction = 2, regionMode = "rectangle", regions = new[] { new { direction = 1, x = 4, y = 2, width = 24, height = 50 } } }));
        var locked = BreathingTests.Fixture(32, 32); locked.Frames[0].Layers[0].Locked = true; Reject(locked, J(new { }));
        var animated = BreathingTests.Fixture(32, 32); ArtDocument.SetAnimation(animated, 2, 1, 100); Reject(animated, J(new { }));
        var huge = BreathingTests.Fixture(64, 64, 4); foreach (var frame in huge.Frames) for (int i = 1; i < 16; i++) frame.Layers.Add(new() { Rgba = new byte[16384] }); Reject(huge, J(new { }));
        var doc = ArtDocument.Create("history", 0, 32, 64); doc.Load(0, JsonSerializer.SerializeToUtf8Bytes(BreathingTests.Fixture(32, 64, 4), ArtDocument.Json));
        byte[] initial = doc.Sidecar(); doc.Edit(1, s => IdleAnimationGenerator.Generate(s, J(new { breathing = true, bobbing = true, sway = true }))); string hash = Hash(doc.State);
        doc.History(2, false); Check(doc.Sidecar().AsSpan().SequenceEqual(initial), "Idle undo was not one step"); doc.History(3, true); Check(Hash(doc.State) == hash, "Idle redo changed pixels");
        var reopened = ArtDocument.Create("reload", 0, 32, 64); reopened.Load(0, doc.Sidecar()); Check(reopened.Sidecar().AsSpan().SequenceEqual(doc.Sidecar()), "Idle sidecar round-trip");
        await Service(root);
        Console.WriteLine("PASS: idle 4 shapes/1+4 directions/7 combinations/1+2px, anchored/manual bounds, 3–16 phases, RGBA/palette, history, locks/budget, API parity and strict failures");
    }
    /// <summary>Reject invalid edits without changing revision, layer metadata or authoritative bytes.</summary>
    private static void Reject(ArtState state, JsonElement p)
    {
        var doc = ArtDocument.Create("reject", 0, state.Width, state.Height); doc.Load(0, JsonSerializer.SerializeToUtf8Bytes(state, ArtDocument.Json)); byte[] before = doc.Sidecar();
        try { doc.Edit(1, s => IdleAnimationGenerator.Generate(s, p)); throw new Exception("Invalid idle input accepted"); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { }
        Check(doc.Revision == 1 && doc.Sidecar().AsSpan().SequenceEqual(before), "Rejected idle input changed draft");
    }
    private static readonly ArtState PoseFixture = BreathingTests.Fixture(64, 64, 4);
    /// <summary>Simulate the same bounded header/layer replies used by the real editors.</summary>
    internal static object PoseReply(JsonElement args, long revision)
    {
        object? pixels = null;
        if (args.TryGetProperty("direction", out var direction) && args.TryGetProperty("layer", out var layer))
        {
            var source = PoseFixture.Frames[direction.GetInt32()].Layers[layer.GetInt32()];
            pixels = new { name = source.Name, visible = source.Visible, rgba = Convert.ToBase64String(source.Rgba) };
        }
        return new { revision, assetId = 7, group = 0, frame = 0, width = 64, height = 64, directions = 4, name = "Selected outfit", layerCount = 1, pixels };
    }
    private sealed class Bridge : IEditorBridge
    {
        public string Session = "idle-test"; public int Commits;
        public Task EmitAsync(object value) => Task.CompletedTask;
        public Task<JsonElement> RequestAsync(string method, object args, CancellationToken cancellation)
        {
            if (method == "get_project_info") return Task.FromResult(J(new { projectSessionId = Session, revision = 8, capabilities = new { timing = true } }));
            if (method == "get_asset_pose") return Task.FromResult(J(PoseReply(J(args), 8)));
            if (method == "commit_art") { Commits++; return Task.FromResult(J(new { selected = 100 })); }
            throw new InvalidOperationException(method);
        }
    }
    /// <summary>Use the same selected-pose and idle operations locally/remotely, with session and approval protection.</summary>
    private static async Task Service(string root)
    {
        var bridge = new Bridge(); var studio = new StudioService(bridge); var ids = new List<string>();
        async Task<JsonElement> Run(string method, object args, bool local = true) => J(await studio.Run(method, J(args), local, default));
        await Run("ui_enable", new { enabled = true });
        foreach (bool local in new[] { true, false })
        {
            var info = await Run("begin_asset_edit", new { projectSessionId = bridge.Session, expectedRevision = 8, category = "outfit", assetId = 7, group = 0, frame = 0, patternY = 0, patternZ = 0 }, local); string id = info.GetProperty("editId").GetString()!; ids.Add(id);
            Check(info.GetProperty("frameCount").GetInt32() == 4 && info.GetProperty("sourcePose").GetString()!.Contains("#7"), "Selected pose identity/geometry missing");
            await Run("generate_idle_animation", new { editId = id, expectedDocumentRevision = 0, breathing = true, bobbing = true, sway = true }, local);
        }
        Check(Hash(studio.Document(ids[0]).State) == Hash(studio.Document(ids[1]).State), "Manual/MCP idle bytes differ");
        var sheet = await Run("export_spritesheet", new { editId = ids[0], columns = 4 }); File.WriteAllBytes(Path.Combine(root, "idle-all-directions.png"), Convert.FromBase64String(sheet.GetProperty("png").GetString()!));
        var imported = await Run("begin_edit", new { projectSessionId = bridge.Session, expectedRevision = 8, category = "outfit" }); string importId = imported.GetProperty("editId").GetString()!;
        await Run("import_spritesheet", new { editId = importId, expectedDocumentRevision = 0, png = sheet.GetProperty("png").GetString(), cellWidth = 64, cellHeight = 64, directions = 4, cells = sheet.GetProperty("metadata").GetProperty("cells") });
        Check(Hash(studio.Document(importId).State) == Hash(studio.Document(ids[0]).State), "Idle atlas round-trip changed pixels");
        var request = await Run("commit_edit", new { editId = ids[1], expectedDocumentRevision = 1 }, false); Check(request.GetProperty("status").GetString() == "awaiting-human-approval" && bridge.Commits == 0, "Idle MCP bypassed approval");
        await Run("ui_enable", new { enabled = false }); try { await Run("generate_idle_animation", new { editId = ids[0], expectedDocumentRevision = 1 }, false); throw new Exception("Disabled idle MCP accepted"); } catch (ModelContextProtocol.McpException) { }
        foreach (bool fallback in new[] { true, false })
        {
            var state = BreathingTests.Fixture(64, 64, 4); IdleAnimationGenerator.Generate(state, J(new { breathing = true, bobbing = true, sway = true, durationsMs = fallback ? new[] { 100, 100, 100, 100 } : new[] { 500, 300, 500, 300 } }));
            var doc = ArtDocument.Create("idle-legacy", 8, 64, 64); doc.Load(0, JsonSerializer.SerializeToUtf8Bytes(state, ArtDocument.Json));
            File.WriteAllText(Path.Combine(root, fallback ? "idle-legacy-100.json" : "idle-legacy-variable.json"), JsonSerializer.Serialize(doc.CommitPacket(), ArtDocument.Json));
        }
    }
}
