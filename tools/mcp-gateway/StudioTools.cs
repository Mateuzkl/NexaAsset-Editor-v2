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
    /// <summary>Read the opted-in recolor session as metadata and PNG; this operation never publishes its preview to the project.</summary>
    [McpServerTool(Name = "nexa_get_recolor_preview", ReadOnly = true), Description("Inspect the open Recolor window after the user enables MCP there. Returns sessionId, revision, texture coordinates, HSL/replacement settings, selection mask, original and adjusted pixels, plus a PNG preview. Coordinates are unscaled image pixels. Changes remain a preview until the user clicks Create New or Replace Current.")]
    public async Task<CallToolResult> RecolorPreview(CancellationToken ct)
        => await RecolorCall("get_recolor_preview", new { }, true, ct);

    /// <summary>Return editor failures explicitly instead of an opaque MCP invocation error.</summary>
    private async Task<CallToolResult> RecolorCall(string method, object args, bool pixels, CancellationToken ct)
    {
        try
        {
            var metadata = JsonSerializer.SerializeToElement(await Read(method, args, ct), ArtDocument.Json);
            return pixels ? ImageResult(metadata, ArtDocument.FromArgb(Convert.FromBase64String(metadata.GetProperty("argb").GetString()!)),
                metadata.GetProperty("width").GetInt32(), metadata.GetProperty("height").GetInt32())
                : new() { StructuredContent = metadata, Content = [new TextContentBlock { Text = metadata.GetRawText() }] };
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or OperationCanceledException or TimeoutException)
        {
            string message = error is OperationCanceledException or TimeoutException
                ? "EDITOR_TIMEOUT: the editor did not return the Recolor preview within 25 seconds. Check whether the editor is responding; no preview was applied."
                : error.Message;
            return new() { IsError = true, Content = [new TextContentBlock { Text = message }] };
        }
    }
    [McpServerTool(Name = "nexa_adjust_recolor"), Description("Adjust the visible Recolor preview using its exact sessionId and expectedRevision. parameters may contain hue (-180..180), saturation/lightness (-100..100), hsl/colorize/replace/shading/maskOnly/applyAllFrames/showOverlay (boolean), source/target (six hex digits), tolerance (0..100 percent), zoom (1..10), group/frame/direction/patternY/patternZ/layer (zero based). Outfit dye template layers are protected. Read nexa_get_recolor_preview again to inspect changes. The human applies the result in Recolor.")]
    public Task<CallToolResult> AdjustRecolor(string sessionId, long expectedRevision, JsonElement parameters, CancellationToken ct)
        => RecolorCall("adjust_recolor", new { sessionId, expectedRevision, parameters }, false, ct);
    [McpServerTool(Name = "nexa_select_recolor_area"), Description("Edit the current Recolor selection with exact sessionId and expectedRevision. parameters: action (all/clear/invert/wand/brush/erase/rectangle). Wand uses x,y,tolerance (0..100 percent),add (boolean). Brush/erase use x,y,endX,endY,size (1..4). Rectangle uses x,y,endX,endY. All coordinates are unscaled image pixels. Only changes the live preview selection; the user applies the result.")]
    public Task<CallToolResult> RecolorSelection(string sessionId, long expectedRevision, JsonElement parameters, CancellationToken ct)
        => RecolorCall("select_recolor_area", new { sessionId, expectedRevision, parameters }, false, ct);
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
    [McpServerTool(Name = "nexa_begin_edit"), Description("Create an isolated art draft against an exact project identity/revision; category: item, outfit, effect, missile. Set outfit for characters (four directions initialized automatically), use width=32,height=64 for tall outfits; optional name and real spriteId reference. Category is preserved through approval and insertion into its matching editor section.")]
    public Task<object> Begin(string projectSessionId, long expectedRevision, string category, CancellationToken ct, int width = 32, int height = 32, uint spriteId = 0, string name = "") => Read("begin_edit", new { projectSessionId, expectedRevision, width, height, spriteId, category, name }, ct);
    /// <summary>Compose all tiles and directions of an explicit selected pose into an isolated editable draft.</summary>
    [McpServerTool(Name="nexa_begin_asset_edit"),Description("Open a complete object pose as a NEW draft with exact projectSessionId/expectedRevision, category and assetId. Optional group/frame/patternY/patternZ are zero based. Composes 32/64 pixel canvases and all native directions/layers; outfit template layers are hidden references. Does not replace the original or discard its animations. Apply creates a new raster asset, not a dye-preserving clone.")]
    public Task<object> BeginAsset(string projectSessionId,long expectedRevision,string category,uint assetId,CancellationToken ct,int group=0,int frame=0,int patternY=0,int patternZ=0)
        =>Read("begin_asset_edit",new{projectSessionId,expectedRevision,category,assetId,group,frame,patternY,patternZ},ct);
    [McpServerTool(Name = "nexa_duplicate_sprite"), Description("Copy exact reference pixels into a new draft without changing the original.")]
    public Task<object> Duplicate(string projectSessionId, long expectedRevision, uint spriteId, CancellationToken ct) => Read("duplicate_sprite", new { projectSessionId, expectedRevision, spriteId }, ct);
    [McpServerTool(Name = "nexa_get_edit_preview", ReadOnly = true), Description("Read straight RGBA preview, hash, pixel diff, timeline and compatibility warnings.")]
    public async Task<CallToolResult> Preview(string editId, CancellationToken ct, int frame = 0)
    {
        var metadata = JsonSerializer.SerializeToElement(await Read("get_edit_preview", new { editId, frame }, ct), ArtDocument.Json);
        var document = metadata.GetProperty("document");
        return ImageResult(metadata, Convert.FromBase64String(metadata.GetProperty("rgba").GetString()!), document.GetProperty("width").GetInt32(), document.GetProperty("height").GetInt32());
    }
    [McpServerTool(Name = "nexa_get_edit_sheet", ReadOnly = true), Description("Review every draft cell together. Returns exact flattened RGBA per cell, time/direction/duration and native directionOrder north/east/south/west. Outfit columns are directions; rows are time steps. No provider/API key is needed for MCP pixel drawing.")]
    public Task<object> Sheet(string editId, CancellationToken ct) => Read("get_edit_sheet", new { editId }, ct);
    private static CallToolResult ImageResult(JsonElement metadata, byte[] rgba, int width, int height) => new()
    {
        StructuredContent = metadata,
        Content = [new TextContentBlock { Text = metadata.GetRawText() }, ImageContentBlock.FromBytes(Png.Encode(new(width, height, rgba)), "image/png")]
    };
    [McpServerTool(Name = "nexa_cancel_edit"), Description("Discard an uncommitted draft.")]
    public Task<object> Cancel(string editId, CancellationToken ct) => Read("cancel_edit", new { editId }, ct);
    [McpServerTool(Name = "nexa_export_spritesheet", ReadOnly = true), Description("Return PNG base64 and exact JSON cell/time/direction mapping. No filesystem access.")]
    public Task<object> Export(string editId, CancellationToken ct, int columns = 4) => Read("export_spritesheet", new { editId, columns }, ct);
    [McpServerTool(Name = "nexa_set_asset_type"), Description("Set draft target category (item/outfit/effect/missile) and optional name. parameters: category,name. Apply inserts into this exact editor section; changing to outfit initializes four directions.")]
    public Task<object> AssetType(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("set_asset_type", editId, expectedDocumentRevision, parameters, ct);
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
    [McpServerTool(Name = "nexa_set_animation"), Description("parameters: frames (time steps),directions (1 or 4), optional duration (ms; omitted preserves timings). Cells preserve time/direction mapping and duplicate the last time reference and require deliberate pixel edits for distinct animation.")]
    public Task<object> Animation(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("set_animation", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_import_sprite_png"), Description("parameters: frame,layer,png (base64). Exact bounded 32/64 PNG decoder.")]
    public Task<object> Import(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("import_sprite_png", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_undo_edit"), Description("Undo one draft operation; this never pretends to undo a published asset.")]
    public Task<object> Undo(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("undo_edit", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_commit_edit"), Description("Request human approval in the editor. The remote caller cannot approve, replace, save or compile.")]
    public Task<object> Commit(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("commit_edit", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_generate_stable_diffusion"), Description("Start one background job through local AUTOMATIC1111/Forge after configuring and consenting in the editor. parameters: prompt,negativePrompt,mode(auto/create/variation),frame,layer,seed(-1/random or uint32),steps(1-40),cfgScale(1-15),strength(0-1),paletteColors(0/16/32/64),background(auto/reference/corners/opaque). Working resolution is at most 512px, nearest-neighbour conversion returns the exact draft size. Poll nexa_get_generation_status. No endpoint, model download, arbitrary workflow or automatic asset application is exposed.")]
    public Task<object> StableDiffusion(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("generate_stable_diffusion", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_get_generation_status", ReadOnly = true), Description("Read the last Stable Diffusion job for this draft: running/cancelling/complete/cancelled/failed/conflict/none. Completion remains an isolated draft edit.")]
    public Task<object> GenerationStatus(string editId, CancellationToken ct) => Read("get_generation_status", new { editId }, ct);
    [McpServerTool(Name = "nexa_cancel_generation"), Description("Cancel the local wait and prevent this draft's pending Stable Diffusion result from publishing. The external GPU server may finish its work; other server jobs are never interrupted.")]
    public Task<object> CancelGeneration(string editId, CancellationToken ct) => Read("cancel_generation", new { editId }, ct);
    [McpServerTool(Name = "nexa_generate_variation"), Description("Generate a referenced image using the optional HTTPS provider after explicit consent in the editor. parameters: prompt,frame,layer. Result stays in the draft.")]
    public Task<object> Variation(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("generate_variation", editId, expectedDocumentRevision, parameters, ct);
    [McpServerTool(Name = "nexa_generate_animation"), Description("Derive deterministic offset animation. parameters: offsets:[[dx,dy],...]. Copies all first-time directions. Does not claim to invent a walk cycle.")]
    public Task<object> Motion(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("generate_animation", editId, expectedDocumentRevision, parameters, ct);
    /// <summary>Validate the parameter object before forwarding breathing through the same revision-checked draft edit used by the UI.</summary>
    [McpServerTool(Name = "nexa_generate_breathing"), Description("Generate four deterministic localized breathing poses from one source time step, without changing the project. Same engine/history as the manual Breathing action. parameters: amplitude (1 or 2 pixels), durationsMs (exactly four 10..10000 ms values; default 500,300,500,300), regionMode (auto or rectangle), regions (rectangle mode: one {direction,x,y,width,height} per native direction, N/E/S/W). Supports 32/64 pixel axes, 1/4 directions, unlocked layers and a 2 MiB draft budget. Auto uses visible bounds, not anatomical AI. Outside each region and lower auto anchor stay exact. Undo is one step. Legacy formats without phase timings require an explicit [100,100,100,100] profile for Apply. Does not guarantee in-game idle playback; human approval and Save/Compile remain separate.")]
    public Task<object> Breathing(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct)
        => parameters.ValueKind == JsonValueKind.Object ? Edit("generate_breathing", editId, expectedDocumentRevision, parameters, ct)
            : throw new ModelContextProtocol.McpException("Breathing parameters must be an object.");
    /// <summary>Expose the shared idle engine with revision checks and draft-only publication.</summary>
    [McpServerTool(Name="nexa_generate_idle_animation"),Description("Generate deterministic idle poses through the same engine as the UI. parameters: breathing/bobbing/sway (booleans), breathingAmplitude/bobbingAmplitude/swayAmplitude (1..2 px), frameCount (3..16, total cells max 64), durationsMs (one 10..10000 ms integer per phase), anchorBase (boolean), direction (optional zero-based face, omit for all), regionMode (auto/rectangle), regions (one {direction,x,y,width,height} per enabled direction). Defaults: breathing, 4 phases, 500/300/500/300 ms, anchored. Existing animations are rejected; each pose samples the original and preserves outside-ROI bytes. Undo is one step. Human Apply and Save remain separate; in-game idle playback is not guaranteed.")]
    public Task<object> Idle(string editId,long expectedDocumentRevision,JsonElement parameters,CancellationToken ct)
        =>parameters.ValueKind==JsonValueKind.Object?Edit("generate_idle_animation",editId,expectedDocumentRevision,parameters,ct):throw new ModelContextProtocol.McpException("Idle parameters must be an object.");
    [McpServerTool(Name = "nexa_import_spritesheet"), Description("Import a bounded PNG atlas into the draft. parameters: png (base64),cellWidth,cellHeight,directions,duration,cells (optional exported mapping). Cells are row-major, time then native direction.")]
    public Task<object> ImportSheet(string editId, long expectedDocumentRevision, JsonElement parameters, CancellationToken ct) => Edit("import_spritesheet", editId, expectedDocumentRevision, parameters, ct);
}
