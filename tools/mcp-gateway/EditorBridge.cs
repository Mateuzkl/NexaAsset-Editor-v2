using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace NexaAsset.Mcp;

/// <summary>Private inherited process pipes: only AIR can answer editor requests.</summary>
public sealed class EditorBridge
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim outputLock = new(1);
    public async Task EmitAsync(object value)
    {
        string json = JsonSerializer.Serialize(value, ArtDocument.Json);
        await outputLock.WaitAsync();
        try { await Console.Out.WriteLineAsync(json); await Console.Out.FlushAsync(); }
        finally { outputLock.Release(); }
    }
    public async Task<JsonElement> RequestAsync(string method, object args, CancellationToken cancellation)
    {
        if (pending.Count >= 16) throw new InvalidOperationException("Editor bridge queue is full.");
        string id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));
        try { await EmitAsync(new { type = "editorRequest", id, method, args }); return await completion.Task; }
        finally { pending.TryRemove(id, out _); }
    }
    public void Accept(JsonElement message)
    {
        string id = message.GetProperty("id").GetString()!;
        if (!pending.TryGetValue(id, out var completion)) return;
        if (message.TryGetProperty("error", out var error)) completion.TrySetException(new InvalidOperationException(error.GetString() ?? "Editor request failed."));
        else completion.TrySetResult(message.GetProperty("result").Clone());
    }
    public void Close() { foreach (var value in pending.Values) value.TrySetCanceled(); pending.Clear(); }
    public static async Task<string?> ReadLineAsync(Stream input, CancellationToken token)
    {
        // Do not allocate an unbounded string for a corrupt/malicious pipe peer.
        using var buffer = new MemoryStream(); byte[] one = new byte[1];
        while (await input.ReadAsync(one, token) != 0)
        {
            if (one[0] == 10) return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            if (buffer.Length >= 4 * 1024 * 1024) throw new InvalidDataException("Bridge line exceeds 4 MiB.");
            buffer.WriteByte(one[0]);
        }
        return buffer.Length == 0 ? null : throw new EndOfStreamException("Incomplete bridge line.");
    }
}
