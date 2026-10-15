using System.Text.Json;

namespace NexaModern;

/// <summary>Bounded selected-object projects; no source catalog or legacy file is rewritten.</summary>
public static class NativeProjectSeed
{
    public static (AssetSet Assets,SheetCache Cache,string[] Notices) FromLegacy(string input,string seed,Action<int,int>? progress=null)
    {
        AssetSet.RejectLinks(input);
        using var manifest=JsonDocument.Parse(AssetSet.ReadBounded(Path.Combine(input,"manifest.json"),65536));
        var rows=manifest.RootElement.GetProperty("objects");
        if(rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength() is <1 or >100)throw new InvalidDataException("Select 1–100 legacy objects per modern project export.");
        var objects=new List<ImportedObject>();var notices=new List<string>();long pixels=0;int index=0;
        foreach(var row in rows.EnumerateArray()) {
            string name="object-"+index+".obd";if(row.GetProperty("file").GetString()!=name)throw new InvalidDataException("Unexpected legacy export packet path.");
            var batch=NativeObjectImport.Read(Path.Combine(input,name));var obj=batch.Objects.Single();uint id=row.GetProperty("id").GetUInt32();string category=row.GetProperty("category").GetString()??"";
            if(category!=obj.Appearance.Category || id<(category=="item" ? 100u : 1u) || id>65535)throw new InvalidDataException("Legacy export category/ID mismatch.");
            pixels+=obj.Sprites.Sum(p=>(long)p.Rgba.Length);if(pixels>NativeObjectImport.PixelLimit)throw new InvalidDataException("Selected modern project pixels exceed 32 MiB. Export a smaller selection.");
            obj=obj with {Appearance=obj.Appearance with {Id=id,Source=NativeWorkspace.Rewrite(obj.Appearance.Source,1,NativeWorkspace.Scalar(1,id))}};
            objects.Add(NormalizeOutfit(obj,notices));notices.AddRange(batch.Notices);index++;progress?.Invoke(index,rows.GetArrayLength());
        }
        var result=Create(seed,objects.ToArray());return(result.Assets,result.Cache,notices.Distinct().ToArray());
    }
    private static ImportedObject NormalizeOutfit(ImportedObject obj,List<string> notices)
    {
        if(obj.Appearance.Category!="outfit")return obj;
        var groups=AssetSet.Groups(obj.Appearance);if(groups.Length!=1)return obj;
        var group=groups[0];var field=Proto.Fields(obj.Appearance.Source).Single(f=>f.Number==2);byte[] source=Proto.Bytes(field).ToArray();
        byte[] Group(uint type,byte[] info)=>NativeWorkspace.Blob(2,NativeWorkspace.Rewrite(NativeWorkspace.Rewrite(source,1,NativeWorkspace.Scalar(1,type)),3,NativeWorkspace.Blob(3,info)));
        byte[] infoBytes=Proto.Bytes(Proto.Fields(source).Single(f=>f.Number==3)).ToArray();byte[] rewritten;
        if(group.Frames>1) {
            int plane=checked((int)(group.PatternX*group.PatternY*group.PatternZ*group.Layers));
            byte[] idle=NativeWorkspace.Rewrite(NativeWorkspace.Rewrite(infoBytes,6,[]),5,group.Sprites.Take(plane).SelectMany(id=>NativeWorkspace.Scalar(5,id)).ToArray());
            rewritten=Group(0,idle).Concat(Group(1,infoBytes)).ToArray();
            notices.Add("Outfit "+obj.Appearance.Id+": idle group uses the existing first frame; moving frames, directions, layers and timings are preserved.");
        } else rewritten=Group(0,infoBytes);
        var appearance=obj.Appearance with {Source=NativeWorkspace.Rewrite(obj.Appearance.Source,2,rewritten),Groups=group.Frames>1 ? 2 : 1};
        AssetSet.Groups(appearance);return obj with {Appearance=appearance};
    }
    private static (AssetSet Assets,SheetCache Cache) Create(string seed,ImportedObject[] objects)
    {
        if(objects.Length is <1 or >100 || objects.Select(o=>(o.Appearance.Category,o.Appearance.Id)).Distinct().Count()!=objects.Length)throw new InvalidDataException("Invalid/duplicate modern project selection.");
        long pixels=objects.Sum(o=>o.Sprites.Sum(p=>(long)p.Rgba.Length)),metadata=objects.Sum(o=>(long)o.Appearance.Source.Length);
        if(pixels>NativeObjectImport.PixelLimit || metadata>4*1024*1024)throw new InvalidDataException("Modern project selection exceeds its pixel/metadata limit.");
        if(File.Exists(seed)||Directory.Exists(seed))throw new InvalidDataException("Project seed already exists.");
        AssetSet.RejectLinks(Path.GetDirectoryName(seed)!);Directory.CreateDirectory(seed);
        try {
            File.WriteAllBytes(Path.Combine(seed,"appearances.dat"),[]);
            File.WriteAllBytes(Path.Combine(seed,"sheet.lzma"),NativeSave.EncodeSheet(new Pixels(384,384,new byte[384*384*4])));
            File.WriteAllText(Path.Combine(seed,"catalog-content.json"),"[{\"type\":\"appearances\",\"file\":\"appearances.dat\"},{\"type\":\"sprite\",\"file\":\"sheet.lzma\",\"spritetype\":0,\"firstspriteid\":0,\"lastspriteid\":0}]");
            var assets=new AssetSet(seed);var cache=new SheetCache(assets);uint next=1;
            foreach(var obj in objects) {
                var ids=new uint[obj.Sprites.Length];for(int i=0;i<ids.Length;i++){ids[i]=next++;cache.SetSprite(ids[i],obj.Sprites[i],true);}
                var groups=AssetSet.Groups(obj.Appearance);var refs=groups.Select(g=>g.Sprites.Select(id=>id<ids.Length ? ids[id] : throw new InvalidDataException("Missing selected project pixels.")).ToArray()).ToArray();
                assets.SetAppearance(obj.Appearance with {Source=NativeObjectImport.Remap(obj.Appearance.Source,refs)});
            }
            return(assets,cache);
        }catch{Cleanup(seed);throw;}
    }
    public static string ExportSelected(AssetSet assets,SheetCache cache,string category,uint[] ids,string target,string session,Action<string,int,int>? progress=null)
    {
        target=Path.GetFullPath(target);
        if(NativeSave.Within(assets.Root,target)||NativeSave.Within(target,assets.Root))throw new InvalidDataException("Choose a new modern project outside the source assets.");
        if(NativeSave.Within(session,target)||NativeSave.Within(target,session))throw new InvalidDataException("Persistent project export must be outside the preview session.");
        if(ids.Length is <1 or >100 || ids.Distinct().Count()!=ids.Length)throw new InvalidDataException("Select 1–100 different native objects.");
        var objects=new List<ImportedObject>();long total=0;
        foreach(uint id in ids) {
            var a=assets.Find(category,id);var groups=AssetSet.Groups(a);var unique=groups.SelectMany(g=>g.Sprites).Distinct().ToArray();var images=new List<Pixels>();
            foreach(uint sprite in unique){var image=cache.Sprite(sprite);total+=image.Rgba.Length;if(total>NativeObjectImport.PixelLimit)throw new InvalidDataException("Selected project pixels exceed 32 MiB.");images.Add(image);}
            var map=unique.Select((value,index)=>(value,index)).ToDictionary(v=>v.value,v=>(uint)v.index);
            objects.Add(new(a with {Source=NativeObjectImport.Remap(a.Source,groups.Select(g=>g.Sprites.Select(s=>map[s]).ToArray()).ToArray())},images.ToArray()));
        }
        string seed=Path.Combine(session,"project-seed-"+Guid.NewGuid().ToString("N"));
        var selected=Create(seed,objects.ToArray());
        try{return NativeSave.Export(selected.Assets,selected.Cache,target,progress);}
        finally{Cleanup(seed);}
    }
    public static void Cleanup(string seed)
    {
        // Only the three files created by Create; never recursively remove user data.
        foreach(string name in new[]{"appearances.dat","sheet.lzma","catalog-content.json"})try{File.Delete(Path.Combine(seed,name));}catch(IOException){}catch(UnauthorizedAccessException){}
        try{if(Directory.Exists(seed))Directory.Delete(seed,false);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
