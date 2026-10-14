using System.Text.Json;
using NexaModern;

internal static class NativeProjectExportTests
{
    private static void Check(bool value,string text){if(!value)throw new Exception(text);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Expected modern project rejection.");}
    public static void Run(string tests)
    {
        string root=Path.Combine(tests,"legacy-project-input"),seed=Path.Combine(tests,"legacy-project-seed"),target=Path.Combine(tests,"legacy-project-output");Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root,"object-0.obd"),NativeObjectImportTests.Obd(100,800));
        File.WriteAllBytes(Path.Combine(root,"object-1.obd"),NativeObjectImportTests.Obd(200,800,true));
        File.WriteAllBytes(Path.Combine(root,"object-2.obd"),NativeObjectImportTests.Obd(300,1098,true));
        var rows=new[]{new {id=700,category="item",file="object-0.obd"},new {id=44,category="outfit",file="object-1.obd"},new {id=45,category="outfit",file="object-2.obd"}};
        string manifest=JsonSerializer.Serialize(new {objects=rows});File.WriteAllText(Path.Combine(root,"manifest.json"),manifest);
        var project=NativeProjectSeed.FromLegacy(root,seed);
        try {
            var groups=AssetSet.Groups(project.Assets.Find("outfit",44));
            Check(groups.Length==2&&groups[0].Type==0&&groups[0].Frames==1&&groups[1].Type==1&&groups[1].Frames==2,"Missing legacy idle/moving group normalization");
            Check(groups[0].Sprites.SequenceEqual(groups[1].Sprites.Take(groups[0].Sprites.Length))&&groups[1].Phases[0].Min==120&&groups[1].Phases[0].Max==130,"Generated idle must reuse frame zero, preserving moving timing");
            Check(AssetSet.Groups(project.Assets.Find("outfit",45)).Length==2,"Existing legacy groups changed");
            NativeSave.Export(project.Assets,project.Cache,target);var saved=new AssetSet(target);var pixels=new SheetCache(saved);
            Check(saved.Appearances.Count==3&&saved.Find("item",700).Id==700&&AssetSet.Groups(saved.Find("outfit",44)).Length==2,"Native project definitions/IDs not reopened");
            foreach(var a in project.Assets.Appearances)foreach(var group in AssetSet.Groups(a))foreach(uint id in group.Sprites)Check(project.Cache.Sprite(id).Rgba.SequenceEqual(pixels.Sprite(id).Rgba),"Legacy native project changed RGBA on disk");
            string session=Path.Combine(tests,"selected-project-session");Directory.CreateDirectory(session);
            foreach(string category in new[]{"item","outfit"}) {
                uint id=category=="item" ? 700u : 44u;string output=Path.Combine(tests,"selected-native-"+category);
                NativeProjectSeed.ExportSelected(saved,pixels,category,[id],output,session);
                var selected=new AssetSet(output);var cache=new SheetCache(selected);Check(selected.Appearances.Count==1&&selected.Find(category,id).Id==id,"Selected modern project scope/ID");
                var before=AssetSet.Groups(saved.Find(category,id));var after=AssetSet.Groups(selected.Find(category,id));
                for(int g=0;g<before.Length;g++){Check(before[g].Type==after[g].Type&&before[g].Frames==after[g].Frames,"Modern export changed native groups");for(int i=0;i<before[g].Sprites.Length;i++)Check(pixels.Sprite(before[g].Sprites[i]).Rgba.SequenceEqual(cache.Sprite(after[g].Sprites[i]).Rgba),"Selected native pixels changed");}
                Reject(()=>NativeProjectSeed.ExportSelected(saved,pixels,category,[id],output,session));
            }
            Reject(()=>NativeProjectSeed.ExportSelected(saved,pixels,"item",[700],Path.Combine(target,"forbidden-child"),session));
            Reject(()=>NativeProjectSeed.ExportSelected(saved,pixels,"item",[700,700],Path.Combine(tests,"duplicate-project"),session));
            Check(!Directory.EnumerateFileSystemEntries(session).Any()&&!Directory.Exists(Path.Combine(target,"forbidden-child")),"Modern project export leaked seed or touched source");
            // Real JSON-lines startup mode must open the bridge and process Save As.
            string ipc=Path.Combine(tests,"legacy-project-ipc"),ipcOutput=Path.Combine(tests,"legacy-project-ipc-save");Directory.CreateDirectory(ipc);
            var savedIn=Console.In;var savedOut=Console.Out;using var captured=new StringWriter();
            try{Console.SetIn(new StringReader(JsonSerializer.Serialize(new {requestId="save",generation=1,command="native-save-as",target=ipcOutput})+"\n{\"requestId\":\"quit\",\"generation\":1,\"command\":\"quit\"}\n"));Console.SetOut(captured);Check(NexaModern.Program.Main(["--legacy-input",root,"--output",ipc])==0,"Legacy bridge protocol failed");}finally{Console.SetIn(savedIn);Console.SetOut(savedOut);}
            Check(!captured.ToString().Contains("\"type\":\"error\"")&&new AssetSet(ipcOutput).Appearances.Count==3,"Legacy bridge Save As failed");NativeProjectSeed.Cleanup(Path.Combine(ipc,"legacy-seed"));
        }finally{NativeProjectSeed.Cleanup(seed);}
        File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(new {objects=new[]{rows[0],rows[0]}}));
        Reject(()=>NativeProjectSeed.FromLegacy(root,Path.Combine(tests,"invalid-project-seed")));
        Check(!Directory.Exists(Path.Combine(tests,"invalid-project-seed")),"Invalid batch created persistent output");File.WriteAllText(Path.Combine(root,"manifest.json"),manifest);
        Console.WriteLine("PASS: legacy 8.00 to native project, explicit first-frame idle/moving groups, preserved grouped outfits, source IDs/RGBA, selected modern projects, new-folder/source guards, seed cleanup and real bridge Save As protocol");
    }
}
