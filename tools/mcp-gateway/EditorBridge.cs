using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace NexaAsset.Mcp;

/// <summary>Private inherited process pipes: only AIR can answer editor requests.</summary>
public interface IEditorBridge
{
    Task EmitAsync(object value);
    Task<JsonElement> RequestAsync(string method, object args, CancellationToken cancellation);
}

public sealed class EditorBridge : IEditorBridge
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
}

/// <summary>One bounded UTF-8 JSONL reader per pipe. Retains bytes after LF for the next line.</summary>
public sealed class BoundedJsonLineReader(Stream input)
{
    public const int MaxLineBytes = 4 * 1024 * 1024;
    private readonly byte[] buffer = new byte[32768];
    private int position, count;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public async Task<string?> ReadLineAsync(CancellationToken token)
    {
        using var line = new MemoryStream();
        while (true) {
            if (position == count) { count = await input.ReadAsync(buffer, token); position = 0; }
            if (count == 0) return line.Length == 0 ? null : throw new EndOfStreamException("Incomplete bridge line.");
            int newline = Array.IndexOf(buffer, (byte)10, position, count - position);
            int length = (newline < 0 ? count : newline) - position;
            if (line.Length + length > MaxLineBytes) throw new InvalidDataException("Bridge line exceeds 4 MiB.");
            line.Write(buffer, position, length); position += length;
            if (newline >= 0) { position++; return Utf8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r'); }
        }
    }
}
