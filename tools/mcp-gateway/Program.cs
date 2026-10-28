using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ModelContextProtocol.AspNetCore;
using NexaAsset.Mcp;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders(); // stdout is exclusively the private AIR JSONL channel. No secrets in HTTP logs.
builder.WebHost.ConfigureKestrel(o => { o.Listen(IPAddress.Loopback, 0); o.Limits.MaxRequestBodySize = 3 * 1024 * 1024; o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10); o.Limits.MaxConcurrentConnections = 16; });
var bridge = new EditorBridge();
using var providerClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(22) };
var providerUrl = Environment.GetEnvironmentVariable("NEXA_ART_PROVIDER_URL");
IArtGenerationProvider? provider = Uri.TryCreate(providerUrl, UriKind.Absolute, out var configured) && configured.Scheme == "https" ? new HttpArtGenerationProvider(providerClient, configured, Environment.GetEnvironmentVariable("NEXA_ART_PROVIDER_TOKEN")) : null;
using var sdClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
var studio = new StudioService(bridge) { Provider = provider, StableDiffusionFactory = endpoint => new(sdClient, endpoint, Environment.GetEnvironmentVariable("NEXA_SD_USERNAME"), Environment.GetEnvironmentVariable("NEXA_SD_PASSWORD")) };
builder.Services.AddSingleton(bridge); builder.Services.AddSingleton(studio);
builder.Services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools<StudioTools>().WithResources<StudioResources>();
await using var app = builder.Build();

using var limiter = new TokenBucketRateLimiter(new() { TokenLimit = 60, TokensPerPeriod = 30, ReplenishmentPeriod = TimeSpan.FromSeconds(1), AutoReplenishment = true, QueueLimit = 0 });
app.Use(async (ctx, next) =>
{
    // Numeric loopback only; never trust forwarded headers, DNS names or browser Origins.
    if (ctx.Request.Host.Host != "127.0.0.1" || !IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? IPAddress.None) || ctx.Request.Headers.ContainsKey("Origin")) { ctx.Response.StatusCode = 403; return; }
    string auth = ctx.Request.Headers.Authorization.ToString();
    byte[] supplied;
    try { supplied = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? Convert.FromHexString(auth[7..]) : []; } catch (FormatException) { supplied = []; }
    if (!CryptographicOperations.FixedTimeEquals(supplied, studio.Secret)) { ctx.Response.StatusCode = 401; return; }
    if (!studio.Enabled) { ctx.Response.StatusCode = 403; return; }
    using var lease = limiter.AttemptAcquire(); if (!lease.IsAcquired) { ctx.Response.StatusCode = 429; return; }
    await next(ctx);
});
app.MapMcp("/mcp");
await app.StartAsync();
string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
await bridge.EmitAsync(new { type = "ready", protocolVersion = 1, endpoint = address + "/mcp", token = Convert.ToHexString(studio.Secret), enabled = false });
using var input = Console.OpenStandardInput();
var lines = new BoundedJsonLineReader(input);
var tasks = new List<Task>();
try
{
    while (await lines.ReadLineAsync(app.Lifetime.ApplicationStopping) is { } line)
    {
        using var parsed = JsonDocument.Parse(line, new() { MaxDepth = 64 }); var message = parsed.RootElement.Clone();
        if (message.GetProperty("type").GetString() == "editorResponse") { bridge.Accept(message); continue; }
        tasks.RemoveAll(t => t.IsCompleted);
        if (tasks.Count >= 16) { await bridge.EmitAsync(new { type = "uiResponse", id = message.GetProperty("id").GetString(), error = "Studio queue is full." }); continue; }
        tasks.Add(Handle(message));
    }
}
catch (OperationCanceledException) { }
finally { bridge.Close(); app.Lifetime.StopApplication(); await studio.StopAsync(); await app.StopAsync(); await Task.WhenAll(tasks); }
async Task Handle(JsonElement message)
{
    string? id = message.GetProperty("id").GetString();
    try { var result = await studio.Run(message.GetProperty("method").GetString()!, message.GetProperty("args"), true, app.Lifetime.ApplicationStopping); await bridge.EmitAsync(new { type = "uiResponse", id, result }); }
    catch (Exception error) when (error is not OutOfMemoryException) { await bridge.EmitAsync(new { type = "uiResponse", id, error = error is OperationCanceledException ? "Editor request timed out or was cancelled. Refresh the project before retrying." : error.Message }); }
}
