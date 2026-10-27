using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexaAsset.Mcp;
using NexaModern;

internal static class ProviderTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => send(request, cancellation);
    }
    public static async Task Run()
    {
        byte[] rgba = new byte[4096]; rgba[0] = 195; rgba[1] = 27; rgba[2] = 67; rgba[3] = 127;
        using var client = new HttpClient(new Handler(async (request, cancellation) => {
            if (request.Headers.Authorization?.Parameter != "test-only-token") throw new Exception("Provider auth contract");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            var reference = NativePng.Read(Convert.FromBase64String(json.RootElement.GetProperty("referencePng").GetString()!));
            if (!reference.Rgba.AsSpan().SequenceEqual(rgba)) throw new Exception("Provider lost reference pixels");
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { png = Convert.ToBase64String(Png.Encode(new(32, 32, rgba))) }) };
        }));
        var provider = new HttpArtGenerationProvider(client, new("https://provider.example/art"), "test-only-token");
        byte[] result = await provider.Generate(new("Blue armour", 32, 32, rgba), CancellationToken.None);
        if (!result.AsSpan().SequenceEqual(rgba)) throw new Exception("Provider output changed RGBA");
        try { await new HttpArtGenerationProvider(client, new("http://provider.example"), null).Generate(new("x", 32, 32, rgba), CancellationToken.None); throw new Exception("Provider allowed HTTP"); } catch (InvalidOperationException) { }
        using var oversized = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1024 * 1024 + 1)) })));
        try { await new HttpArtGenerationProvider(oversized, new("https://provider.example"), null).Generate(new("x", 32, 32, rgba), CancellationToken.None); throw new Exception("Provider allowed oversized output"); } catch (InvalidDataException) { }
        using var malformed = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { png = Convert.ToBase64String(new byte[64]) }) })));
        try { await new HttpArtGenerationProvider(malformed, new("https://provider.example"), null).Generate(new("x", 32, 32, rgba), CancellationToken.None); throw new Exception("Provider allowed malformed PNG"); } catch (InvalidDataException) { }
        Console.WriteLine("PASS: optional provider contract, exact reference/output, auth header, HTTPS, oversized and malformed image rejection (in-process test provider; no external request/cost)");
    }
}
