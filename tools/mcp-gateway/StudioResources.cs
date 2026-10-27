using System.Text.Json;
using ModelContextProtocol.Server;
namespace NexaAsset.Mcp;
[McpServerResourceType]
public sealed class StudioResources(StudioService studio)
{
    private async Task<string> Read(string sessionId, string method, object args, CancellationToken ct)
    {
        var info = (JsonElement)await studio.Run("get_project_info", JsonSerializer.SerializeToElement(new { }), false, ct);
        if (sessionId != info.GetProperty("projectSessionId").GetString()) throw new InvalidOperationException("Project session expired.");
        return JsonSerializer.Serialize(await studio.Run(method, JsonSerializer.SerializeToElement(args), false, ct), ArtDocument.Json);
    }
    [McpServerResource(UriTemplate = "nexa://session/{sessionId}/sprite/{spriteId}", MimeType = "application/json")]
    public Task<string> Sprite(string sessionId, uint spriteId, CancellationToken ct) => Read(sessionId, "get_sprite", new { spriteId }, ct);
    [McpServerResource(UriTemplate = "nexa://session/{sessionId}/asset/{category}/{assetId}", MimeType = "application/json")]
    public Task<string> Asset(string sessionId, string category, uint assetId, CancellationToken ct) => Read(sessionId, "get_asset", new { category, assetId }, ct);
}
