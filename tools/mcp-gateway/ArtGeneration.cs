using System.Net.Http.Headers;
using System.Text.Json;
using NexaModern;
namespace NexaAsset.Mcp;

public sealed record ArtGenerationRequest(string Prompt, int Width, int Height, byte[] ReferenceRgba);
public interface IArtGenerationProvider
{
    Task<byte[]> Generate(ArtGenerationRequest request, CancellationToken cancellation);
}

/// <summary>Optional, explicitly consented provider adapter. The URL and token come only from the user's process environment.</summary>
public sealed class HttpArtGenerationProvider(HttpClient client, Uri endpoint, string? token) : IArtGenerationProvider
{
    public async Task<byte[]> Generate(ArtGenerationRequest request, CancellationToken cancellation)
    {
        if (endpoint.Scheme != "https" || !string.IsNullOrEmpty(endpoint.UserInfo)) throw new InvalidOperationException("Configure an HTTPS art provider without embedded credentials.");
        if (request.Prompt.Length is < 1 or > 2048) throw new InvalidDataException("Prompt must have 1–2048 characters.");
        ArtDocument.CheckShape(request.Width, request.Height);
        if (request.ReferenceRgba.Length != request.Width * request.Height * 4) throw new InvalidDataException("Invalid generation reference.");
        byte[] reference = Png.Encode(new(request.Width, request.Height, request.ReferenceRgba));
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrEmpty(token)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = JsonContent.Create(new { schemaVersion = 1, prompt = request.Prompt, width = request.Width, height = request.Height, referencePng = Convert.ToBase64String(reference), pixelArt = true, transparent = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Art provider returned HTTP {(int)response.StatusCode}. No draft change was applied.");
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Art provider response exceeds 1 MiB.");
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token); using var data = new MemoryStream(); byte[] buffer = new byte[8192];
        int read; while ((read = await input.ReadAsync(buffer, timeout.Token)) != 0) { if (data.Length + read > 1024 * 1024) throw new InvalidDataException("Art provider response exceeds 1 MiB."); data.Write(buffer, 0, read); }
        using var json = JsonDocument.Parse(data.ToArray()); var pixels = NativePng.Read(Convert.FromBase64String(json.RootElement.GetProperty("png").GetString()!));
        if (pixels.Width != request.Width || pixels.Height != request.Height) throw new InvalidDataException("Provider returned a different canvas size. Pixel art is never blurred/rescaled automatically.");
        return pixels.Rgba;
    }
}
