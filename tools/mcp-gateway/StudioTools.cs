using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using NexaModern;

namespace NexaAsset.Mcp;

[McpServerToolType]
public sealed class StudioTools(StudioService studio)
{
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value, ArtDocument.Json);
    private Task<object> Read(string method, object value, CancellationToken ct) => studio.Run(method, Args(value), false, ct);
    private Task<object> Edit(string method, string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct)
    {
        var args = parameters.ValueKind == JsonValueKind.Object ? parameters.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) : [];
        args["editId"] = Args(editId); args["expectedDocumentRevision"] = Args(expectedDocumentRevision);
        return studio.Run(method, Args(args), false, ct);
    }
    [McpServerTool(Name = "nexa_get_project_info", ReadOnly = true), Description("Read the open editor project identity, revision, format and capabilities.")]
    public Task<object> Project(CancellationToken ct) => Read("get_project_info", new { }, ct);
    [McpServerTool(Name = "nexa_get_capabilities", ReadOnly = true), Description("Read real supported operations and limits for the active project.")]
    public Task<object> Capabilities(CancellationToken ct) => Read("get_capabilities", new { }, ct);
    [McpServerTool(Name = "nexa_list_assets", ReadOnly = true), Description("List up to 100 active assets. Category: item, outfit, effect, missile, sprite. Page is zero based.")]
    public Task<object> List(string category, CancellationToken ct, int page = 0, string query = "") => Read("list_assets", new { category, page, query }, ct);
    [McpServerTool(Name = "nexa_get_asset", ReadOnly = true), Description("Read geometry, animation timings and actual sprite references.")]
    public Task<object> Asset(string category, uint assetId, CancellationToken ct) => Read("get_asset", new { category, assetId }, ct);
    [McpServerTool(Name = "nexa_get_sprite", ReadOnly = true), Description("Read exact straight ARGB bytes and dimensions from the active editor storage.")]
    public async Task<CallToolResult> Sprite(uint spriteId, CancellationToken ct)
    {
        var metadata = JsonSerializer.SerializeToElement(await Read("get_sprite", new { spriteId }, ct), ArtDocument.Json);
        return ImageResult(metadata, ArtDocument.FromArgb(Convert.FromBase64String(metadata.GetProperty("argb").GetString()!)), metadata.GetProperty("width").GetInt32(), metadata.GetProperty("height").GetInt32());
    }
    [McpServerTool(Name = "nexa_get_frame", ReadOnly = true), Description("Read native references for a validated frame/direction/layer.")]
    public Task<object> Frame(string category, uint assetId, CancellationToken ct, int group = 0, int frame = 0, int direction = 0, int layer = 0) => Read("get_frame", new { category, assetId, group, frame, direction, layer }, ct);
    [McpServerTool(Name = "nexa_list_art_documents", ReadOnly = true), Description("List the shared manual/MCP drafts.")]
    public Task<object> Documents(CancellationToken ct) => Read("list_art_documents", new { }, ct);
    [McpServerTool(Name = "nexa_begin_edit"), Description("Create an isolated art draft against an exact project identity/revision; optional spriteId is a real reference.")]
    public Task<object> Begin(string projectSessionId, long expectedRevision, CancellationToken ct, int width = 32, int height = 32, uint spriteId = 0) => Read("begin_edit", new { projectSessionId, expectedRevision, width, height, spriteId }, ct);
    [McpServerTool(Name = "nexa_duplicate_sprite"), Description("Copy exact reference pixels into a new draft without changing the original.")]
    public Task<object> Duplicate(string projectSessionId, long expectedRevision, uint spriteId, CancellationToken ct) => Read("duplicate_sprite", new { projectSessionId, expectedRevision, spriteId }, ct);
    [McpServerTool(Name = "nexa_get_edit_preview", ReadOnly = true), Description("Read straight RGBA preview, hash, pixel diff, timeline and compatibility warnings.")]
    public async Task<CallToolResult> Preview(string editId, CancellationToken ct, int frame = 0)
    {
        var metadata = JsonSerializer.SerializeToElement(await Read("get_edit_preview", new { editId, frame }, ct), ArtDocument.Json);
        var document = metadata.GetProperty("document");
        return ImageResult(metadata, Convert.FromBase64String(metadata.GetProperty("rgba").GetString()!), document.GetProperty("width").GetInt32(), document.GetProperty("height").GetInt32());
    }
    private static CallToolResult ImageResult(JsonElement metadata, byte[] rgba, int width, int height) => new()
    {
        StructuredContent = metadata,
        Content = [new TextContentBlock { Text = metadata.GetRawText() }, ImageContentBlock.FromBytes(Png.Encode(new(width, height, rgba)), "image/png")]
    };
    [McpServerTool(Name = "nexa_cancel_edit"), Description("Discard an uncommitted draft.")]
    public Task<object> Cancel(string editId, CancellationToken ct) => Read("cancel_edit", new { editId }, ct);
    [McpServerTool(Name = "nexa_export_spritesheet", ReadOnly = true), Description("Return PNG base64 and exact JSON cell/time/direction mapping. No filesystem access.")]
    public Task<object> Export(string editId, CancellationToken ct, int columns = 4) => Read("export_spritesheet", new { editId, columns }, ct);
    [McpServerTool(Name = "nexa_create_sprite"), Description("parameters: width,height (32 or 64). Reinitialize an empty draft.")]
    public Task<object> Create(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("create_sprite", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_set_pixels"), Description("parameters: frame,layer,pixels:[{x,y,rgba:[r,g,b,a]}]. Up to 4096 pixels, grouped into one undo step.")]
    public Task<object> Pixels(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("set_pixels", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_apply_palette"), Description("parameters: frame,layer,colors:[{from:[r,g,b,a],to:[r,g,b,a]}]. Exact deterministic palette mapping.")]
    public Task<object> Palette(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("apply_palette", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_transform_sprite"), Description("parameters: frame,layer,operation (flipX/flipY/rotate90/offset),dx,dy.")]
    public Task<object> Transform(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("transform_sprite", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_add_layer"), Description("parameters: frame,name,layer,duplicate. Painting layers are separate from native game layers.")]
    public Task<object> AddLayer(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("add_layer", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_update_layer"), Description("parameters: frame,layer,name,visible,locked,opacity (0-255),moveTo,delete,merge. Merge flattens visible paint layers.")]
    public Task<object> Layer(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("update_layer", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_set_animation"), Description("parameters: frames (time steps),directions (1 or 4),duration (ms). New cells duplicate the last reference and require deliberate pixel edits for distinct animation.")]
    public Task<object> Animation(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("set_animation", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_import_sprite_png"), Description("parameters: frame,layer,png (base64). Exact bounded 32/64 PNG decoder.")]
    public Task<object> Import(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("import_sprite_png", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_undo_edit"), Description("Undo one draft operation; this never pretends to undo a published asset.")]
    public Task<object> Undo(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("undo_edit", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_commit_edit"), Description("Request human approval in the editor. The remote caller cannot approve, replace, save or compile.")]
    public Task<object> Commit(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("commit_edit", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_generate_variation"), Description("Generate a referenced image using the optional HTTPS provider after explicit consent in the editor. parameters: prompt,frame,layer. Result stays in the draft.")]
    public Task<object> Variation(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("generate_variation", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_generate_animation"), Description("Derive deterministic offset animation. parameters: offsets:[[dx,dy],...]. Copies all first-time directions. Does not claim to invent a walk cycle.")]
    public Task<object> Motion(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("generate_animation", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_import_spritesheet"), Description("Import a bounded PNG atlas into the draft. parameters: png (base64),cellWidth,cellHeight,directions,duration,cells (optional exported mapping). Cells are row-major, time then native direction.")]
    public Task<object> ImportSheet(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("import_spritesheet", editId, expectedDocumentRevision, parameters, ct);
}
