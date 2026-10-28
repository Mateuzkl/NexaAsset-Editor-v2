using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NexaModern;

namespace NexaAsset.Mcp;

/// <summary>Bounded controls for one WebUI/Forge image; model selection remains with the local server.</summary>
public sealed record StableDiffusionSettings
{
    public string Mode { get; init; } = "auto";
    public string NegativePrompt { get; init; } = "";
    public long Seed { get; init; } = -1;
    public int Steps { get; init; } = 20;
    public double CfgScale { get; init; } = 6;
    public double Strength { get; init; } = .35;
    public int PaletteColors { get; init; } = 32;
    public string Background { get; init; } = "auto";
    public void Validate()
    {
        if (Mode is not ("auto" or "create" or "variation") || Background is not ("auto" or "reference" or "corners" or "opaque")) throw new InvalidDataException("Invalid generation mode/transparency policy.");
        if (NegativePrompt == null || NegativePrompt.Length > 2048 || Seed < -1 || Seed > uint.MaxValue || Steps is < 1 or > 40 || !double.IsFinite(CfgScale) || CfgScale is < 1 or > 15 || !double.IsFinite(Strength) || Strength is < 0 or > 1 || PaletteColors is not (0 or 16 or 32 or 64)) throw new InvalidDataException("Use seed -1..4294967295, steps 1..40, CFG 1..15, strength 0..1 and palette 0/16/32/64.");
    }
}

/// <summary>Native HTTP client for local AUTOMATIC1111/Forge. No upstream code, Python, weights or executable workflows are embedded.</summary>
public sealed class StableDiffusionWebUiProvider
{
    private readonly HttpClient client;
    private readonly AuthenticationHeaderValue? authentication;
    public Uri Endpoint { get; }
    public StableDiffusionWebUiProvider(HttpClient client, Uri endpoint, string? username = null, string? password = null)
    {
        Endpoint = ValidateEndpoint(endpoint); this.client = client;
        if (!string.IsNullOrEmpty(username)) authentication = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + (password ?? ""))));
    }
    /// <summary>Only an explicit numeric loopback origin can receive prompts or image references.</summary>
    public static Uri ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https") || !IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out var ip) || !IPAddress.IsLoopback(ip) || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != "/") throw new InvalidDataException("Use a local WebUI/Forge origin such as http://127.0.0.1:7860 (no credentials, path or query).");
        return endpoint;
    }
    private HttpRequestMessage Message(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, new Uri(Endpoint, path)); message.Headers.Authorization = authentication; return message;
    }
    private async Task<JsonElement> Send(HttpRequestMessage message, CancellationToken ct)
    {
        try {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode switch {
                HttpStatusCode.NotFound => "Stable Diffusion API not found. Start AUTOMATIC1111/Forge with --api and check the local port.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Stable Diffusion authentication failed. Configure NEXA_SD_USERNAME/NEXA_SD_PASSWORD before starting Nexa.",
                _ => $"Stable Diffusion returned HTTP {(int)response.StatusCode}. No draft pixels were changed."
            });
            const int limit = 8 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Stable Diffusion response exceeds 8 MiB.");
            using var stream = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); byte[] buffer = new byte[32768];
            int count; while ((count = await stream.ReadAsync(buffer, ct)) != 0) { if (output.Length + count > limit) throw new InvalidDataException("Stable Diffusion response exceeds 8 MiB."); output.Write(buffer, 0, count); }
            using var json = JsonDocument.Parse(output.ToArray(), new() { MaxDepth = 32 }); return json.RootElement.Clone();
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Cannot connect to local Stable Diffusion. Start AUTOMATIC1111/Forge with --api, load a checkpoint and test the connection again."); }
    }
    public async Task<object> Probe(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = Message(HttpMethod.Get, "/sdapi/v1/sd-models");
        JsonElement models;
        try { models = await Send(request, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new InvalidOperationException("Stable Diffusion connection test exceeded 5 seconds. Check that WebUI/Forge is running with --api on this local port."); }
        if (models.ValueKind != JsonValueKind.Array || models.GetArrayLength() == 0) throw new InvalidOperationException("The API is available but no Stable Diffusion checkpoint is installed. Load a model in WebUI/Forge first.");
        return new { connected = true, modelCount = models.GetArrayLength(), message = "WebUI API is available. Generation uses the checkpoint selected in WebUI/Forge." };
    }
    public async Task<byte[]> Generate(ArtGenerationRequest request, StableDiffusionSettings settings, CancellationToken ct)
    {
        settings.Validate(); ArtDocument.CheckShape(request.Width, request.Height);
        if (request.Prompt.Length is < 1 or > 2048 || request.ReferenceRgba.Length != request.Width * request.Height * 4) throw new InvalidDataException("Invalid Stable Diffusion prompt/reference.");
        bool hasReference = HasReference(request.ReferenceRgba), variation = settings.Mode == "variation" || settings.Mode == "auto" && hasReference;
        if ((variation || settings.Background == "reference") && !hasReference) throw new InvalidOperationException("The selected layer is empty. Choose create mode or draw/import a reference first.");
        int scale = 512 / Math.Max(request.Width, request.Height), width = request.Width * scale, height = request.Height * scale;
        var payload = new Dictionary<string, object?> {
            ["prompt"] = "pixel art game sprite, single isolated subject, hard pixel edges, simple readable silhouette, plain background, " + request.Prompt,
            ["negative_prompt"] = "blur, antialiasing, soft edges, text, watermark, photo, 3d render, complex background, " + settings.NegativePrompt,
            ["seed"] = settings.Seed, ["steps"] = settings.Steps, ["cfg_scale"] = settings.CfgScale,
            ["width"] = width, ["height"] = height, ["batch_size"] = 1, ["n_iter"] = 1,
            ["sampler_name"] = "Euler", ["send_images"] = true, ["save_images"] = false,
            ["do_not_save_samples"] = true, ["do_not_save_grid"] = true, ["restore_faces"] = false
        };
        if (variation) {
            byte[] enlarged = Resize(new(request.Width, request.Height, request.ReferenceRgba), width, height);
            payload["init_images"] = new[] { Convert.ToBase64String(Png.Encode(new(width, height, enlarged))) };
            payload["denoising_strength"] = settings.Strength; payload["include_init_images"] = false; payload["resize_mode"] = 0;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(180));
        using var message = Message(HttpMethod.Post, variation ? "/sdapi/v1/img2img" : "/sdapi/v1/txt2img"); message.Content = JsonContent.Create(payload);
        JsonElement result;
        try { result = await Send(message, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new InvalidOperationException("Stable Diffusion exceeded 180 seconds. Try fewer steps or a lighter checkpoint. No draft change was applied."); }
        if (!result.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array || images.GetArrayLength() != 1 || images[0].ValueKind != JsonValueKind.String) throw new InvalidDataException("Stable Diffusion must return exactly one PNG image.");
        string encoded = images[0].GetString()!;
        if (encoded.StartsWith("data:image/png;base64,", StringComparison.Ordinal)) encoded = encoded[22..];
        var image = NativePng.ReadArtSheet(Convert.FromBase64String(encoded));
        if (image.Width != width || image.Height != height) throw new InvalidDataException("Stable Diffusion returned unexpected working dimensions.");
        return Finish(image, request, settings);
    }
    private static bool HasReference(byte[] pixels) { for (int at = 3; at < pixels.Length; at += 4) if (pixels[at] != 0) return true; return false; }
    /// <summary>Nearest-neighbour sampling only; this is a draft conversion, not a promise of authored pixel clusters.</summary>
    public static byte[] Resize(Pixels source, int width, int height)
    {
        byte[] result = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
            int sx = (2 * x + 1) * source.Width / (2 * width), sy = (2 * y + 1) * source.Height / (2 * height);
            source.Rgba.AsSpan((sy * source.Width + sx) * 4, 4).CopyTo(result.AsSpan((y * width + x) * 4));
        }
        return result;
    }
    public static byte[] Finish(Pixels image, ArtGenerationRequest request, StableDiffusionSettings settings)
    {
        byte[] pixels = Resize(image, request.Width, request.Height);
        string background = settings.Background == "auto" ? HasReference(request.ReferenceRgba) ? "reference" : "corners" : settings.Background;
        if (background == "reference") {
            if (!HasReference(request.ReferenceRgba)) throw new InvalidOperationException("Reference transparency requires a non-empty reference layer.");
            for (int at = 3; at < pixels.Length; at += 4) pixels[at] = request.ReferenceRgba[at];
        }
        else if (background == "corners") RemoveBorderBackground(pixels, request.Width, request.Height);
        else if (background == "opaque") for (int at = 3; at < pixels.Length; at += 4) pixels[at] = 255;
        if (settings.PaletteColors > 0) ReducePalette(pixels, settings.PaletteColors);
        return pixels;
    }
    private static void RemoveBorderBackground(byte[] pixels, int width, int height)
    {
        var corners = new[] { 0, width - 1, (height - 1) * width, width * height - 1 }.Select(p => pixels.AsSpan(p * 4, 3).ToArray()).ToArray();
        bool[] seen = new bool[width * height]; var queue = new Queue<int>();
        for (int x = 0; x < width; x++) { queue.Enqueue(x); queue.Enqueue((height - 1) * width + x); }
        for (int y = 0; y < height; y++) { queue.Enqueue(y * width); queue.Enqueue(y * width + width - 1); }
        while (queue.TryDequeue(out int p)) {
            if (seen[p]) continue; seen[p] = true; int at = p * 4;
            if (!corners.Any(c => Math.Abs(c[0] - pixels[at]) + Math.Abs(c[1] - pixels[at + 1]) + Math.Abs(c[2] - pixels[at + 2]) <= 72)) continue;
            pixels.AsSpan(at, 4).Clear(); int x = p % width, y = p / width;
            if (x > 0) queue.Enqueue(p - 1); if (x + 1 < width) queue.Enqueue(p + 1); if (y > 0) queue.Enqueue(p - width); if (y + 1 < height) queue.Enqueue(p + width);
        }
    }
    private sealed record Color(byte R, byte G, byte B, int Count);
    private static void ReducePalette(byte[] pixels, int count)
    {
        var frequencies = new Dictionary<int, int>();
        for (int at = 0; at < pixels.Length; at += 4) if (pixels[at + 3] != 0) { int rgb = pixels[at] << 16 | pixels[at + 1] << 8 | pixels[at + 2]; frequencies[rgb] = frequencies.GetValueOrDefault(rgb) + 1; }
        if (frequencies.Count <= count) return;
        var boxes = new List<List<Color>> { frequencies.OrderBy(p => p.Key).Select(p => new Color((byte)(p.Key >> 16), (byte)(p.Key >> 8), (byte)p.Key, p.Value)).ToList() };
        int Range(List<Color> box, Func<Color, byte> channel) => box.Max(channel) - box.Min(channel);
        while (boxes.Count < count) {
            var box = boxes.Where(b => b.Count > 1).OrderByDescending(b => Math.Max(Range(b, c => c.R), Math.Max(Range(b, c => c.G), Range(b, c => c.B))) * b.Sum(c => c.Count)).FirstOrDefault();
            if (box == null) break;
            int r = Range(box, c => c.R), g = Range(box, c => c.G), b = Range(box, c => c.B);
            var sorted = box.OrderBy(c => r >= g && r >= b ? c.R : g >= b ? c.G : c.B).ToList(); int half = sorted.Sum(c => c.Count) / 2, sum = 0, split = 1;
            for (int i = 0; i < sorted.Count - 1; i++) { sum += sorted[i].Count; split = i + 1; if (sum >= half) break; }
            boxes.Remove(box); boxes.Add(sorted.Take(split).ToList()); boxes.Add(sorted.Skip(split).ToList());
        }
        var palette = boxes.Select(box => { int total = box.Sum(c => c.Count); return new Color((byte)(box.Sum(c => c.R * c.Count) / total), (byte)(box.Sum(c => c.G * c.Count) / total), (byte)(box.Sum(c => c.B * c.Count) / total), total); }).ToArray();
        for (int at = 0; at < pixels.Length; at += 4) if (pixels[at + 3] != 0) {
            int r = pixels[at], g = pixels[at + 1], b = pixels[at + 2];
            var nearest = palette.MinBy(c => (c.R - r) * (c.R - r) + (c.G - g) * (c.G - g) + (c.B - b) * (c.B - b))!;
            pixels[at] = nearest.R; pixels[at + 1] = nearest.G; pixels[at + 2] = nearest.B;
        }
    }
}
