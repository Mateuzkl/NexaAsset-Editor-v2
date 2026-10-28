using System.Text.Json;
using ModelContextProtocol;
using NexaAsset.Mcp;

internal static class DraftLifecycleTests
{
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, ArtDocument.Json);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Bridge : IEditorBridge
    {
        public readonly List<JsonElement> Events = [];
        public Task EmitAsync(object value) { Events.Add(J(value)); return Task.CompletedTask; }
        public Task<JsonElement> RequestAsync(string method, object args, CancellationToken ct) => Task.FromResult(J(new { projectSessionId = "close", revision = 0 }));
    }
    public static async Task Run()
    {
        var untouched = ArtDocument.Create("close", 0, 32, 64, category: "outfit"); ArtDocument.SetAnimation(untouched.State, 1, 4, null);
        Check(!untouched.Dirty, "Untouched revision-zero outfit blocks closing");
        untouched.ApprovalRequested = true; Check(untouched.Dirty, "Approval-only MCP work lost");
        untouched.MarkExported(0, true); Check(!untouched.Dirty, "Exported approval remains dirty");
        untouched.Edit(0, state => { state.Name = "changed metadata"; ArtDocument.SetAnimation(state, 2, 4, 150); state.Frames[7].Layers[0].Rgba[3] = 127; });
        Check(untouched.Dirty, "Non-current frame/metadata/timing edits lost");
        untouched.MarkExported(0, true); Check(untouched.Dirty, "Stale export acknowledged newer draft");
        byte[] exact = untouched.Sidecar(); untouched.MarkExported(1, false); Check(!untouched.Dirty, "Same revision export not acknowledged");
        untouched.History(1, false); Check(untouched.Dirty, "Undo after export incorrectly clean");
        untouched.Committed = true; Check(!untouched.Dirty, "Applied asset blocks draft closing");

        var bridge = new Bridge(); var service = new StudioService(bridge);
        async Task<JsonElement> Run(string method, object args) => J(await service.Run(method, J(args), true, CancellationToken.None));
        async Task<string> Begin(string category) => (await Run("begin_edit", new { projectSessionId = "close", expectedRevision = 0, category })).GetProperty("editId").GetString()!;
        string a = await Begin("outfit"), b = await Begin("item"), c = await Begin("effect");
        await Run("ui_load", new { editId = b, expectedDocumentRevision = 0, data = Convert.ToBase64String(exact) });
        Check(!service.Document(b).Dirty && service.Document(b).Sidecar().AsSpan().SequenceEqual(exact), "Imported persisted sidecar dirty or inexact");
        await Run("set_asset_type", new { editId = b, expectedDocumentRevision = 1, category = "missile" });
        await Run("commit_edit", new { editId = c, expectedDocumentRevision = 0 });
        var list = await Run("list_art_documents", new { });
        Check(list.GetProperty("documents").EnumerateArray().Count(d => d.GetProperty("dirty").GetBoolean()) == 2, "Multi-draft close classification");
        await Run("ui_enable", new { enabled = true }); await Run("ui_provider_consent", new { consent = true });
        await Run("ui_begin_close", new { }); Check(!service.Enabled && !service.ProviderConsent, "Close preparation did not pause/revoke generation");
        try { await Run("set_asset_type", new { editId = a, expectedDocumentRevision = 0, name = "late" }); throw new Exception("Local edit published during save/close"); } catch (InvalidOperationException) { }
        try { await service.Run("ui_discard_drafts", J(new { }), false, CancellationToken.None); throw new Exception("Remote UI discard permitted"); } catch (McpException) { }
        Check((await Run("list_art_documents", new { })).GetProperty("documents").GetArrayLength() == 3, "Cancelled close lost a draft");
        var saved = await Run("ui_sidecar", new { editId = b });
        Check(service.Document(b).Dirty, "Reading export bytes marked file saved prematurely");
        await Run("ui_mark_exported", new { editId = b, documentRevision = saved.GetProperty("documentRevision").GetInt64(), approvalRequested = saved.GetProperty("approvalRequested").GetBoolean() });
        Check(!service.Document(b).Dirty && service.Document(c).Dirty, "Saving one draft cleared another");
        await Run("ui_end_close", new { });
        await Run("set_asset_type", new { editId = b, expectedDocumentRevision = 2, name = "after cancellation" }); Check(service.Document(b).Dirty, "Cancel failed to resume draft editing");
        await Run("ui_begin_close", new { }); await Run("ui_discard_drafts", new { });
        Check((await Run("list_art_documents", new { })).GetProperty("documents").GetArrayLength() == 0, "Explicit local discard did not finish");
        Check(bridge.Events.Any(e => e.TryGetProperty("dirty", out var dirty) && !dirty.GetBoolean()), "Changed events omitted actual persistence state");
        await Run("ui_end_close", new { }); await service.StopAsync();
        Console.WriteLine("PASS: untouched/exported/edited/committed draft lifecycle, approval/metadata/multi-frame work, export acknowledgement CAS, import exactness, close pause/freeze/cancel and local-only discard");
    }
}
