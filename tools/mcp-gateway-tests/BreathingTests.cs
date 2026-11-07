using System.Security.Cryptography;
using System.Text.Json;
using NexaAsset.Mcp;
using NexaModern;

internal static class BreathingTests
{
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, ArtDocument.Json);
    /// <summary>Fail the suite immediately when a pixel, history or protocol invariant is violated.</summary>
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Hash(ArtState state) => Convert.ToHexString(SHA256.HashData(state.Frames.SelectMany(f => f.Layers.SelectMany(l => l.Rgba)).ToArray()));

    /// <summary>Create deterministic asymmetric silhouettes with partial alpha and hidden RGB to expose direction, palette and sampling errors.</summary>
    public static ArtState Fixture(int w, int h, int directions = 1, bool unusual = false)
    {
        var state = new ArtState { Width = w, Height = h, Directions = directions, Category = directions == 4 ? "outfit" : "item" };
        for (int d = 0; d < directions; d++)
        {
            byte[] rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int at = (y * w + x) * 4;
                rgba[at] = 17; rgba[at + 1] = 21; rgba[at + 2] = 31; // hidden RGB
                bool visible = (y >= 4 && y < h / 3 && x >= w / 3 && x < w * 2 / 3)
                    || (y >= h / 3 && y < h - 9 && x >= w / 4 && x < w * 3 / 4)
                    || (y >= h - 9 && y < h - 3 && (x >= w / 4 && x < w / 3 || x >= w * 2 / 3 && x < w * 3 / 4));
                if (unusual) visible |= x >= 2 && x < w / 2 && y > h / 3 && y < h / 2 + x / 4;
                if (visible) { rgba[at] = (byte)(70 + d * 35); rgba[at + 1] = (byte)(80 + y * 2); rgba[at + 2] = (byte)(90 + x); rgba[at + 3] = (byte)((x + y) % 5 == 0 ? 127 : 255); }
            }
            state.Frames.Add(new() { Layers = [new() { Name = "Body " + d, Rgba = rgba }] });
        }
        return state;
    }

    /// <summary>Exercise supported shapes, exact RGBA, local regions, histories, rejected inputs and UI/MCP parity; write legacy and visual fixtures.</summary>
    public static async Task Run(string root)
    {
        foreach (int w in new[] { 32, 64 }) foreach (int h in new[] { 32, 64 }) foreach (int dirs in new[] { 1, 4 })
        {
            var source = Fixture(w, h, dirs);
            var doc = ArtDocument.Create("breathing", 0, w, h);
            doc.Load(0, JsonSerializer.SerializeToUtf8Bytes(source, ArtDocument.Json));
            byte[] before = doc.Sidecar();
            byte[] reference = doc.Reference!.ToArray();
            doc.Edit(1, s => BreathingGenerator.Generate(s, J(new { amplitude = 1 })));
            Check(doc.State.Frames.Count == dirs * 4 && doc.Revision == 2, "Breathing time/direction/revision count");
            Check(doc.Reference.AsSpan().SequenceEqual(reference), "Breathing changed the reference image");
            int[] times = [500, 300, 500, 300];
            for (int phase = 0; phase < 4; phase++) for (int d = 0; d < dirs; d++)
            {
                var frame = doc.State.Frames[phase * dirs + d];
                Check(frame.Duration == times[phase], "Breathing direction timing/order");
                var original = source.Frames[d].Layers[0].Rgba; var output = frame.Layers[0].Rgba;
                if (phase == 0) Check(output.AsSpan().SequenceEqual(original), "Neutral pose changed exact RGBA");
                else Check(!output.AsSpan().SequenceEqual(original), "Breathing generated no visible movement");
                Check(output.AsSpan((h - 8) * w * 4).SequenceEqual(original.AsSpan((h - 8) * w * 4)), "Auto breathing moved the feet/base");
                var palette = Enumerable.Range(0, w * h).Select(p => BitConverter.ToUInt32(original, p * 4)).ToHashSet();
                Check(Enumerable.Range(0, w * h).All(p => palette.Contains(BitConverter.ToUInt32(output, p * 4))), "Breathing interpolated colors/alpha");
                Check(!ReferenceEquals(original, output), "Breathing aliased original pixels");
            }
            Check(doc.State.Frames.Skip(dirs).Select(f => f.Id).Distinct().Count() == dirs * 3, "New phase IDs collide");
            string hash = Hash(doc.State);
            var again = ArtDocument.Copy(source); BreathingGenerator.Generate(again, J(new { amplitude = 1 }));
            Check(Hash(again) == hash, "Breathing was not deterministic");
            var sidecar = ArtDocument.Create("reopen", 0, w, h); sidecar.Load(0, doc.Sidecar());
            Check(sidecar.Sidecar().AsSpan().SequenceEqual(doc.Sidecar()), "Breathing .nexaart roundtrip");
            doc.History(2, false); Check(doc.Sidecar().AsSpan().SequenceEqual(before), "Breathing was not one undo step");
            doc.History(3, true); Check(Hash(doc.State) == hash, "Breathing redo changed pixels");

            var manual = ArtDocument.Copy(source);
            var regions = Enumerable.Range(0, dirs).Select(d => new { direction = d, x = 3 + d, y = 3, width = w - 10, height = h - 14 }).ToArray();
            BreathingGenerator.Generate(manual, J(new { amplitude = 2, regionMode = "rectangle", regions }));
            for (int phase = 0; phase < 4; phase++) for (int d = 0; d < dirs; d++)
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    if (x < regions[d].x || x >= regions[d].x + regions[d].width || y < 3 || y >= h - 11)
                        Check(manual.Frames[phase * dirs + d].Layers[0].Rgba.AsSpan((y * w + x) * 4, 4)
                            .SequenceEqual(source.Frames[d].Layers[0].Rgba.AsSpan((y * w + x) * 4, 4)), "Manual region changed outside RGBA");
        }

        foreach (object invalid in new object[] {
            new { amplitude = 0 }, new { amplitude = 3 }, new { amplitude = 1.5 }, new { amplitude = "1" },
            new { durationsMs = new[] { 500, 300, 500 } }, new { durationsMs = new[] { 0, 300, 500, 300 } },
            new { durationsMs = "500" }, new { regionMode = "AI" }, new { regionMode = false }, new { regions = "bad" },
            new { unknown = true }, new { regionMode = "rectangle", regions = Array.Empty<object>() },
            new { regionMode = "rectangle", regions = new[] { new { direction = 0, x = 31, y = 0, width = 2, height = 2 } } },
            new { regionMode = "rectangle", regions = new[] { new { direction = 0, x = 1, y = 1, width = 0, height = 2 } } },
            new { regionMode = "rectangle", regions = new[] { new { direction = 1, x = 1, y = 1, width = 2, height = 2 } } }
        }) Reject(Fixture(32, 32), J(invalid));
        Reject(Fixture(32,32,4), J(new { regionMode = "rectangle", regions = new[] {
            new { direction=0,x=1,y=1,width=2,height=2 }, new { direction=0,x=1,y=1,width=2,height=2 },
            new { direction=2,x=1,y=1,width=2,height=2 }, new { direction=3,x=1,y=1,width=2,height=2 }} }));
        Reject(ArtDocument.Create("empty",0,32,32).State, J(new {}));
        var animated = Fixture(32,32); ArtDocument.SetAnimation(animated,2,1,100); Reject(animated,J(new {}));
        var locked = Fixture(32,32); locked.Frames[0].Layers[0].Locked=true; Reject(locked,J(new {}));
        locked.Frames[0].Layers.Add(new() { Rgba=new byte[4096] }); Reject(locked,J(new {}));
        var huge = Fixture(64,64,4);
        foreach(var f in huge.Frames) for(int i=1;i<9;i++)f.Layers.Add(new(){Rgba=new byte[16384]});
        Reject(huge,J(new {}));
        var tiny = ArtDocument.Create("tiny",0,64,64).State; tiny.Frames[0].Layers[0].Rgba[(32*64+32)*4+3]=255;
        BreathingGenerator.Generate(tiny,J(new{regionMode="rectangle",regions=new[]{new{direction=0,x=32,y=32,width=1,height=1}}}));
        Check(tiny.Frames.All(f=>f.Layers[0].Rgba.AsSpan().SequenceEqual(tiny.Frames[0].Layers[0].Rgba)),"1px region should be a stable no-op");

        await ServiceParity();
        foreach(bool fallback in new[]{false,true})
        {
            var state=Fixture(64,64,4);
            BreathingGenerator.Generate(state,fallback?J(new{durationsMs=new[]{100,100,100,100}}):J(new{}));
            var packet=ArtDocument.Create("breathing-legacy",8,64,64);packet.Load(0,JsonSerializer.SerializeToUtf8Bytes(state,ArtDocument.Json));
            File.WriteAllText(Path.Combine(root,fallback?"breathing-legacy-100.json":"breathing-legacy-variable.json"),JsonSerializer.Serialize(packet.CommitPacket(),ArtDocument.Json));
        }
        foreach (bool unusual in new[] { false, true })
        {
            var sample=Fixture(64,64,1,unusual);BreathingGenerator.Generate(sample,J(new{}));
            byte[] atlas=new byte[256*64*4];
            for(int p=0;p<4;p++)for(int y=0;y<64;y++)sample.Frames[p].Layers[0].Rgba.AsSpan(y*64*4,64*4).CopyTo(atlas.AsSpan((y*256+p*64)*4));
            File.WriteAllBytes(Path.Combine(root,unusual?"breathing-unusual.png":"breathing-biped.png"),Png.Encode(new(256,64,atlas)));
        }
        Console.WriteLine("PASS: breathing four geometries, 1/4 directions, phases/timing, RGBA/palette/anchors, rectangles, deterministic pixels, history, sidecar, locks, budgets, strict errors and manual/MCP parity");
    }

    /// <summary>Assert an invalid generation leaves both document revision and serialized pixels/metadata unchanged.</summary>
    private static void Reject(ArtState state, JsonElement parameters)
    {
        var doc=ArtDocument.Create("reject",0,state.Width,state.Height);doc.Load(0,JsonSerializer.SerializeToUtf8Bytes(state,ArtDocument.Json));
        byte[] before=doc.Sidecar();long revision=doc.Revision;
        try {doc.Edit(revision,s=>BreathingGenerator.Generate(s,parameters));throw new Exception("Invalid breathing configuration accepted");}
        catch(Exception e) when(e is InvalidDataException or InvalidOperationException) {}
        Check(doc.Revision==revision&&doc.Sidecar().AsSpan().SequenceEqual(before),"Rejected breathing mutated the draft");
    }

    private sealed class Bridge : IEditorBridge
    {
        public string Session="breathing";
        public int Commits;
        public Task EmitAsync(object value)=>Task.CompletedTask;
        /// <summary>Simulate a legacy editor that forbids variable timings and count only actual asset publication requests.</summary>
        public Task<JsonElement> RequestAsync(string method,object args,CancellationToken cancellation)
        {
            if(method=="get_project_info")return Task.FromResult(J(new {projectSessionId=Session,revision=0,capabilities=new{timing=false}}));
            if(method=="commit_art"){Commits++;return Task.FromResult(J(new{selected=100}));}
            throw new InvalidOperationException(method);
        }
    }
    /// <summary>Compare local and MCP generation, then verify timing warnings, approval, atlas import and expired-session rejection.</summary>
    private static async Task ServiceParity()
    {
        var bridge=new Bridge();var studio=new StudioService(bridge);
        async Task<JsonElement> Run(string method,object args,bool local=true)=>J(await studio.Run(method,J(args),local,default));
        await Run("ui_enable",new{enabled=true});
        var ids=new List<string>();
        foreach(bool local in new[]{true,false}) {
            string id=(await Run("begin_edit",new{projectSessionId=bridge.Session,expectedRevision=0,width=32,height=64,category="outfit"},local)).GetProperty("editId").GetString()!;ids.Add(id);
            studio.Document(id).Load(0,JsonSerializer.SerializeToUtf8Bytes(Fixture(32,64,4),ArtDocument.Json));
            await Run("generate_breathing",new{editId=id,expectedDocumentRevision=1,amplitude=1},local);
        }
        Check(Hash(studio.Document(ids[0]).State)==Hash(studio.Document(ids[1]).State),"Manual/MCP generated different breathing pixels");
        var preview=await Run("get_edit_preview",new{editId=ids[0]});
        Check(preview.GetProperty("warnings").EnumerateArray().Any(v=>v.GetString()!.Contains("100 ms")),"Legacy timing warning missing before Apply");
        Check(!preview.GetProperty("timingCompatible").GetBoolean(),"Legacy Apply was not visibly marked incompatible");
        try{await Run("ui_apply",new{editId=ids[0],expectedDocumentRevision=2});throw new Exception("Unsupported legacy timings were applied");}catch(InvalidOperationException){}
        Check(bridge.Commits==0,"Breathing inserted assets before local approval");
        var approval=await Run("commit_edit",new{editId=ids[1],expectedDocumentRevision=2},false);
        Check(approval.GetProperty("status").GetString()=="awaiting-human-approval"&&bridge.Commits==0,"Remote breathing bypassed approval");
        try{await Run("generate_breathing",new{editId=ids[0],expectedDocumentRevision=1});throw new Exception("Stale breathing revision accepted");}catch(InvalidOperationException e){Check(e.Message.Contains("DOCUMENT_CONFLICT"),"Wrong stale revision error");}
        var atlas=await Run("export_spritesheet",new{editId=ids[0],columns=4});
        string imported=(await Run("begin_edit",new{projectSessionId=bridge.Session,expectedRevision=0,category="outfit"})).GetProperty("editId").GetString()!;
        await Run("import_spritesheet",new{editId=imported,expectedDocumentRevision=0,png=atlas.GetProperty("png").GetString(),cellWidth=32,cellHeight=64,directions=4,cells=atlas.GetProperty("metadata").GetProperty("cells")});
        Check(Hash(studio.Document(imported).State)==Hash(studio.Document(ids[0]).State),"Breathing atlas import changed RGBA/direction order");
        await Run("ui_enable",new{enabled=false});
        try{await Run("generate_breathing",new{editId=ids[0],expectedDocumentRevision=2},false);throw new Exception("Disabled MCP accepted breathing");}catch(ModelContextProtocol.McpException){}
        bridge.Session="another";
        try{await Run("get_edit_preview",new{editId=ids[0]});throw new Exception("Expired breathing draft survived project change");}catch(InvalidOperationException){}
    }
}
