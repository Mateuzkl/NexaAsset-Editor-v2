using System.Text.Json;
using NexaModern;

namespace NexaAsset.Mcp;

public sealed class StudioService(EditorBridge bridge)
{
    private readonly Dictionary<string, ArtDocument> documents = [];
    private readonly SemaphoreSlim gate = new(1);
    private string? projectSession;
    private readonly Queue<object> audit = new();
    public bool Enabled { get; private set; }
    public bool ProviderConsent { get; private set; }
    public IArtGenerationProvider? Provider { get; init; }
    public long Connections;
    public ArtDocument Document(string id) => documents.TryGetValue(id, out var doc) ? doc : throw new InvalidOperationException("Draft not found or project session expired.");
    public async Task<JsonElement> Project(CancellationToken ct)
    {
        var info = await bridge.RequestAsync("get_project_info", new { }, ct);
        string session = info.GetProperty("projectSessionId").GetString()!;
        if (projectSession != session) { documents.Clear(); projectSession = session; ProviderConsent = false; }
        return info;
    }
    public async Task<object> Run(string method, JsonElement args, bool local, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {
            if (!local) await bridge.EmitAsync(new { type = "activity", action = method, state = "working" });
            var result = await Execute(method, args, local, ct);
            if (!local || method == "ui_apply") {
                audit.Enqueue(new { action = method, timestamp = DateTimeOffset.UtcNow, success = true, category = S(args,"category"), assetId = args.TryGetProperty("assetId", out var asset) ? asset.ToString() : null });
                while (audit.Count > 100) audit.Dequeue();
                await bridge.EmitAsync(new { type = "activity", action = method, state = "complete" });
            }
            return result;
        }
        catch {
            if (!local || method == "ui_apply") { audit.Enqueue(new { action = method, timestamp = DateTimeOffset.UtcNow, success = false }); while (audit.Count > 100) audit.Dequeue(); await bridge.EmitAsync(new { type = "activity", action = method, state = "failed" }); }
            throw;
        }
        finally { gate.Release(); }
    }
    private static int I(JsonElement e, string key, int fallback = 0) => e.TryGetProperty(key, out var v) ? v.GetInt32() : fallback;
    private static string S(JsonElement e, string key, string fallback = "") => e.TryGetProperty(key, out var v) ? v.GetString() ?? fallback : fallback;
    private async Task<object> Execute(string method, JsonElement args, bool local, CancellationToken ct)
    {
        if (method == "ui_enable" && local) { Enabled = args.GetProperty("enabled").GetBoolean(); return new { enabled = Enabled }; }
        if (method == "ui_provider_consent" && local) { ProviderConsent = args.GetProperty("consent").GetBoolean(); return new { providerConfigured = Provider != null, consent = ProviderConsent }; }
        if (method == "ui_audit" && local) return new { events = audit.ToArray() };
        if (method == "ui_reset" && local) { documents.Clear(); projectSession = null; return new { reset = true }; }
        if (!local && !Enabled) throw new InvalidOperationException("MCP is disabled in the editor.");
        if (method is "get_project_info" or "get_capabilities") return await Project(ct);
        if (method is "list_assets" or "get_asset" or "get_sprite" or "get_frame")
        {
            await Project(ct); return await bridge.RequestAsync(method, args, ct);
        }
        // Validate project identity even on document reads: reopening a project invalidates all old drafts.
        var info = await Project(ct);
        if (method == "list_art_documents") return new { documents = documents.Values.Select(d => d.Info()).ToArray() };
        if (method == "begin_edit" || method == "duplicate_sprite")
        {
            if (documents.Count >= 4) throw new InvalidOperationException("Close a draft first (four open drafts maximum).");
            if (S(args, "projectSessionId") != projectSession || args.GetProperty("expectedRevision").GetInt64() != info.GetProperty("revision").GetInt64()) throw new InvalidOperationException("PROJECT_CONFLICT: refresh project information before beginning an edit.");
            int w = I(args, "width", 32), h = I(args, "height", 32); byte[]? pixels = null;
            if (args.TryGetProperty("spriteId", out var id) && id.GetUInt32() > 0)
            {
                var sprite = await bridge.RequestAsync("get_sprite", new { spriteId = id.GetUInt32() }, ct);
                w = sprite.GetProperty("width").GetInt32(); h = sprite.GetProperty("height").GetInt32();
                pixels = ArtDocument.FromArgb(Convert.FromBase64String(sprite.GetProperty("argb").GetString()!));
            }
            var created = ArtDocument.Create(projectSession!, info.GetProperty("revision").GetInt64(), w, h, pixels); created.TargetTransparency = !info.TryGetProperty("transparency", out var alpha) || alpha.GetBoolean(); documents.Add(created.Id, created);
            await Changed(created); return created.Info();
        }
        var doc = Document(S(args, "editId"));
        if (method is "get_edit_preview" or "ui_snapshot") return doc.Preview(I(args, "frame"), local);
        if (method == "cancel_edit") { documents.Remove(doc.Id); return new { cancelled = !doc.Committed, closed = true, appliedAssetPreserved = doc.Committed }; }
        if (method == "export_spritesheet")
        {
            int columns = I(args, "columns", doc.State.Directions), rows = (doc.State.Frames.Count + columns - 1) / Math.Max(1, columns);
            if (columns is < 1 or > 16 || rows > 64) throw new InvalidDataException("Use 1-16 columns.");
            int w = columns * doc.State.Width, h = rows * doc.State.Height; byte[] rgba = new byte[w * h * 4];
            for (int i = 0; i < doc.State.Frames.Count; i++) { var image = ArtDocument.Composite(doc.State, i); for (int y = 0; y < doc.State.Height; y++) image.AsSpan(y * doc.State.Width * 4, doc.State.Width * 4).CopyTo(rgba.AsSpan((((i / columns) * doc.State.Height + y) * w + (i % columns) * doc.State.Width) * 4)); }
            return new { mimeType = "image/png", png = Convert.ToBase64String(Png.Encode(new(w, h, rgba))), metadata = new { schemaVersion = 1, cellWidth = doc.State.Width, cellHeight = doc.State.Height, columns, directions = doc.State.Directions, cells = doc.State.Frames.Select((f, i) => new { index = i, x = i % columns, y = i / columns, time = i / doc.State.Directions, direction = i % doc.State.Directions, duration = f.Duration }) } };
        }
        if (method == "ui_sidecar" && local) return new { data = Convert.ToBase64String(doc.Sidecar()) };
        if (method == "ui_png" && local) return new { data = Convert.ToBase64String(Png.Encode(new(doc.State.Width, doc.State.Height, ArtDocument.Composite(doc.State, I(args, "frame"))))) };
        long revision = args.GetProperty("expectedDocumentRevision").GetInt64(); doc.CheckRevision(revision);
        if (method == "commit_edit")
        {
            doc.ApprovalRequested = true; await Changed(doc); return new { status = "awaiting-human-approval", editId = doc.Id, documentRevision = doc.Revision, message = "Review the candidate in Pixel Art Studio and click Apply as new asset. No project mutation has occurred." };
        }
        if (method == "ui_apply" && local)
        {
            if (info.GetProperty("revision").GetInt64() != doc.BaseRevision) throw new InvalidOperationException("PROJECT_CONFLICT: the editor changed since this draft began. Keep/export this draft and begin again from the current project.");
            string category = S(args, "category", "item");
            if (category is not ("item" or "outfit" or "effect" or "missile")) throw new InvalidDataException("Invalid category.");
            var packet = JsonSerializer.SerializeToUtf8Bytes(doc.CommitPacket(category), ArtDocument.Json);
            var result = await bridge.RequestAsync("commit_art", new { packet = Convert.ToBase64String(packet), sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packet)) }, ct);
            doc.Committed = true; doc.ApprovalRequested = false; await Changed(doc); return result;
        }
        if (method == "generate_variation")
        {
            if (!ProviderConsent || Provider == null) throw new InvalidOperationException("Configure NEXA_ART_PROVIDER_URL and explicitly allow sending references in the editor AI panel first. Deterministic drawing/palette tools remain available without a provider.");
            int frame = I(args, "frame"), layer = I(args, "layer");
            var reference = ArtDocument.Layer(doc.State, frame, layer, false).Rgba.ToArray();
            var generated = await Provider.Generate(new(S(args, "prompt"), doc.State.Width, doc.State.Height, reference), ct);
            doc.Edit(revision, state => ArtDocument.Layer(state, frame, layer).Rgba = generated);
            await Changed(doc); return doc.Info();
        }
        if (method == "undo_edit" || method == "ui_redo") doc.History(revision, method == "ui_redo");
        else if (method == "ui_load" && local) doc.Load(revision, Convert.FromBase64String(S(args, "data")));
        else doc.Edit(revision, state =>
        {
            int frame = I(args, "frame"), layer = I(args, "layer");
            if (method != "create_sprite" && (frame < 0 || frame >= state.Frames.Count)) throw new InvalidDataException("Invalid frame index.");
            switch (method)
            {
                case "create_sprite":
                    state.Width = I(args, "width", 32); state.Height = I(args, "height", 32); ArtDocument.CheckShape(state.Width, state.Height); state.Directions = 1;
                    state.Frames = [new() { Layers = [new() { Name = "Base", Rgba = new byte[checked(state.Width * state.Height * 4)] }] }]; break;
                case "set_pixels": ArtDocument.SetPixels(state, frame, layer, args.GetProperty("pixels")); break;
                case "transform_sprite": ArtDocument.Transform(state, frame, layer, S(args, "operation"), I(args, "dx"), I(args, "dy")); break;
                case "apply_palette":
                    var map = args.GetProperty("colors").EnumerateArray().Select(c => (From: c.GetProperty("from").EnumerateArray().Select(v => v.GetByte()).ToArray(), To: c.GetProperty("to").EnumerateArray().Select(v => v.GetByte()).ToArray())).ToArray();
                    if (map.Length > 256 || map.Any(c => c.From.Length != 4 || c.To.Length != 4)) throw new InvalidDataException("Use up to 256 exact RGBA color pairs.");
                    var target = ArtDocument.Layer(state, frame, layer).Rgba;
                    for (int at = 0; at < target.Length; at += 4) foreach (var color in map) if (target.AsSpan(at, 4).SequenceEqual(color.From)) { color.To.CopyTo(target, at); break; }
                    break;
                case "add_layer":
                    if (state.Frames[frame].Layers.Count >= 16) throw new InvalidDataException("Layer limit reached.");
                    var added = new ArtLayer { Name = S(args, "name", "Layer"), Rgba = new byte[state.Width * state.Height * 4] };
                    if (args.TryGetProperty("duplicate", out var duplicate) && duplicate.GetBoolean()) added.Rgba = ArtDocument.Layer(state, frame, layer, false).Rgba.ToArray();
                    state.Frames[frame].Layers.Add(added); break;
                case "update_layer":
                    var selected = ArtDocument.Layer(state, frame, layer, false);
                    if (args.TryGetProperty("name", out var name)) selected.Name = name.GetString() ?? "Layer";
                    if (args.TryGetProperty("visible", out var visible)) selected.Visible = visible.GetBoolean();
                    if (args.TryGetProperty("locked", out var locked)) selected.Locked = locked.GetBoolean();
                    if (args.TryGetProperty("opacity", out var opacity)) selected.Opacity = opacity.GetByte();
                    if (args.TryGetProperty("moveTo", out var move)) { int to = move.GetInt32(); if (to < 0 || to >= state.Frames[frame].Layers.Count) throw new InvalidDataException("Invalid layer destination."); state.Frames[frame].Layers.RemoveAt(layer); state.Frames[frame].Layers.Insert(to, selected); }
                    if (args.TryGetProperty("delete", out var remove) && remove.GetBoolean()) { if (selected.Locked) throw new InvalidOperationException("Layer is locked."); state.Frames[frame].Layers.Remove(selected); }
                    if (args.TryGetProperty("merge", out var merge) && merge.GetBoolean())
                    { if (state.Frames[frame].Layers.Any(l => l.Locked)) throw new InvalidOperationException("Unlock layers before merging."); state.Frames[frame].Layers = [new() { Name = "Merged", Rgba = ArtDocument.Composite(state, frame) }]; }
                    break;
                case "set_animation":
                    int directions = I(args, "directions", state.Directions), frames = I(args, "frames", state.Frames.Count / state.Directions), duration = I(args, "duration", 100);
                    if (directions is not (1 or 4) || frames < 1 || frames * directions > 64) throw new InvalidDataException("Use 1/4 directions and up to 64 cells.");
                    var old = state.Frames;
                    state.Frames = Enumerable.Range(0, frames * directions).Select(i => { var copy = JsonSerializer.Deserialize<ArtFrame>(JsonSerializer.Serialize(old[Math.Min(i, old.Count - 1)], ArtDocument.Json), ArtDocument.Json)!; copy.Duration = duration; return copy; }).ToList(); state.Directions = directions;
                    break;
                case "generate_animation":
                    var offsets = args.GetProperty("offsets").EnumerateArray().Select(v => v.EnumerateArray().Select(n => n.GetInt32()).ToArray()).ToArray();
                    if (offsets.Length is < 1 or > 16 || offsets.Any(v => v.Length != 2) || offsets.Length * state.Directions > 64) throw new InvalidDataException("Use 1–16 explicit [dx,dy] offsets. This derives motion; it does not synthesize a walk cycle.");
                    var originals = state.Frames.Take(state.Directions).ToArray(); state.Frames = [];
                    foreach (var offset in offsets) foreach (var original in originals) state.Frames.Add(JsonSerializer.Deserialize<ArtFrame>(JsonSerializer.Serialize(original, ArtDocument.Json), ArtDocument.Json)!);
                    for (int index = 0; index < state.Frames.Count; index++) for (int l = 0; l < state.Frames[index].Layers.Count; l++) ArtDocument.Transform(state, index, l, "offset", offsets[index / state.Directions][0], offsets[index / state.Directions][1]);
                    break;
                case "import_spritesheet":
                    var sheet = NativePng.ReadArtSheet(Convert.FromBase64String(S(args, "png")));
                    int cw = I(args, "cellWidth", state.Width), ch = I(args, "cellHeight", state.Height), dirs = I(args, "directions", 1), ms = I(args, "duration", 100);
                    ArtDocument.CheckShape(cw, ch);
                    if (sheet.Width % cw != 0 || sheet.Height % ch != 0) throw new InvalidDataException("Sheet dimensions must be divisible by cell geometry.");
                    int cols = sheet.Width / cw, count = cols * (sheet.Height / ch);
                    if (count is < 1 or > 64 || dirs is not (1 or 4) || count % dirs != 0) throw new InvalidDataException("Invalid sheet cell/direction count.");
                    state.Width = cw; state.Height = ch; state.Directions = dirs; state.Frames = [];
                    var metadata = args.TryGetProperty("cells", out var cellMetadata) ? cellMetadata.EnumerateArray().ToArray() : [];
                    if (metadata.Length != 0 && metadata.Length != count) throw new InvalidDataException("Metadata cell count differs from sheet.");
                    for (int cell = 0; cell < count; cell++)
                    {
                        int sx = cell % cols, sy = cell / cols, timing = ms;
                        if (metadata.Length != 0) { var m = metadata[cell]; if (I(m,"index") != cell || I(m,"x") != sx || I(m,"y") != sy || I(m,"direction") != cell % dirs || I(m,"time") != cell / dirs) throw new InvalidDataException("Sheet metadata mapping differs from row-major native direction order."); timing = I(m,"duration",ms); }
                        byte[] cellRgba = new byte[cw * ch * 4];
                        for (int y = 0; y < ch; y++) sheet.Rgba.AsSpan(((sy * ch + y) * sheet.Width + sx * cw) * 4, cw * 4).CopyTo(cellRgba.AsSpan(y * cw * 4));
                        state.Frames.Add(new() { Duration = timing, Layers = [new() { Name = "Sheet " + cell, Rgba = cellRgba }] });
                    }
                    break;
                case "import_sprite_png":
                    var png = NativePng.Read(Convert.FromBase64String(S(args, "png")));
                    if (png.Width != state.Width || png.Height != state.Height) throw new InvalidDataException("PNG dimensions differ from canvas.");
                    ArtDocument.Layer(state, frame, layer).Rgba = png.Rgba; break;
                case "ui_replace_layer" when local:
                    var raw = Convert.FromBase64String(S(args, "rgba")); if (raw.Length != state.Width * state.Height * 4) throw new InvalidDataException("Invalid layer pixels."); ArtDocument.Layer(state, frame, layer).Rgba = raw; break;
                default: throw new InvalidOperationException("Unsupported operation.");
            }
        });
        await Changed(doc); return doc.Info();
    }
    private Task Changed(ArtDocument doc) => bridge.EmitAsync(new { type = "changed", editId = doc.Id, documentRevision = doc.Revision });
}
