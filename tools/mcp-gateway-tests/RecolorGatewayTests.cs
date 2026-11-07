using System.Text.Json;
using ModelContextProtocol.Protocol;
using NexaAsset.Mcp;

internal static class RecolorGatewayTests
{
    private sealed class TimedOutEditor : IEditorBridge
    {
        public Task EmitAsync(object value) => Task.CompletedTask;
        public Task<JsonElement> RequestAsync(string method, object args, CancellationToken cancellation)
            => Task.FromCanceled<JsonElement>(new CancellationToken(true));
    }

    public static async Task Run()
    {
        var studio = new StudioService(new TimedOutEditor());
        await studio.Run("ui_enable", JsonSerializer.SerializeToElement(new { enabled = true }), true, default);
        var result = await new StudioTools(studio).RecolorPreview(default);
        if (result.IsError != true || !result.Content.OfType<TextContentBlock>().Any(t => t.Text.Contains("EDITOR_TIMEOUT")))
            throw new Exception("Unresponsive editor returned an opaque Recolor error.");
        Console.WriteLine("PASS: unresponsive editor returns an explicit MCP Recolor timeout without publishing assets");
    }
}
