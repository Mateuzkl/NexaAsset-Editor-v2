using System.Text;
using System.Text.Json;

namespace NexaModern;

/// <summary>Session-only edits. The original catalog, protobuf and sheets are never opened for writing.</summary>
public sealed class NativeWorkspace(AssetSet assets,SheetCache cache)
{
    public long Revision { get; private set; }
    private Appearance? clipboard;
    private static byte[] Varint(ulong n){var bytes=new List<byte>();do{byte b=(byte)(n&127);n>>=7;bytes.Add((byte)(b|(n>0 ? 128 : 0)));}while(n>0);return bytes.ToArray();}
    public static byte[] Scalar(int field,ulong n)=>Varint((ulong)field<<3).Concat(Varint(n)).ToArray();
    public static byte[] Blob(int field,ReadOnlyMemory<byte> bytes)=>Varint(((ulong)field<<3)|2).Concat(Varint((ulong)bytes.Length)).Concat(bytes.ToArray()).ToArray();
    public static byte[] Rewrite(ReadOnlyMemory<byte> source,int field,byte[] replacement)
    {
        using var bytes=new MemoryStream();var reader=new Proto(source);
        while(!reader.End){int start=reader.Position;var f=reader.Next();if(f.Number!=field)bytes.Write(source.Span.Slice(start,reader.Position-start));}
        bytes.Write(replacement);return bytes.ToArray(); // Untouched fields retain their exact encoded bytes and order.
    }
    private Appearance WithSource(Appearance a,byte[] source,uint? id=null)=>new(id??a.Id,a.Category,a.Name,AssetSet.Groups(a with {Source=source}).Length,source);
    private uint NextId(string category)=>checked(assets.Appearances.Where(a=>a.Category==category).Select(a=>a.Id).DefaultIfEmpty(category=="item" ? 99u : 0u).Max()+1);
    private uint NextSprite()=>checked(assets.Sheets[^1].Last+1);
    private void Changed()=>Revision++;
    public object Duplicate(string category,uint[] ids)
    {
        CheckIds(ids);var originals=ids.Select(id=>assets.Find(category,id)).ToArray();uint next=NextId(category);var added=new List<uint>();
        if((ulong)next+(uint)originals.Length>uint.MaxValue || assets.Appearances.Count+originals.Length>=200000)throw new InvalidDataException("Working-copy record limit.");
        var edits=originals.Select(a=>{uint id=next++;return WithSource(a,Rewrite(a.Source,1,Scalar(1,id)),id);}).ToArray();
        assets.SetAppearances(edits);added.AddRange(edits.Select(edit=>edit.Id));
        Changed();return new {selected=added[0],revision=Revision};
    }
    public object Add(string category)
    {
        CheckNewObject(category);
        uint sprite=NextSprite();assets.ValidateAppearanceEdits([BlankAppearance(category,sprite)]);cache.SetSprite(sprite,new Pixels(32,32,new byte[4096]),true);return AddWithSprite(category,sprite);
    }
    public object AddWithSprite(string category,uint sprite)
    {
        CheckNewObject(category);
        assets.FindSheet(sprite);var appearance=BlankAppearance(category,sprite);
        assets.SetAppearance(appearance);Changed();return new {selected=appearance.Id,revision=Revision};
    }
    public object ImportPngObject(string category,int width,int height,string argb)
    {
        CheckNewObject(category);
        uint sprite=NextSprite();assets.ValidateAppearanceEdits([BlankAppearance(category,sprite)]);Sprite(null,width,height,argb);return AddWithSprite(category,sprite);
    }
    public object ImportPng(string file,uint? id,string? category=null)
    {
        if(category!=null)CheckNewObject(category);
        var image=NativePng.Read(file);uint sprite=id??NextSprite();
        if(category!=null)assets.ValidateAppearanceEdits([BlankAppearance(category,sprite)]);
        cache.SetSprite(sprite,image,id==null);
        if(category!=null)return AddWithSprite(category,sprite);
        Changed();return new {sprite,revision=Revision};
    }
    public object Remove(string category,uint[] ids){CheckIds(ids);assets.RemoveAppearances(category,ids);Changed();return new {selected=assets.Appearances.Where(a=>a.Category==category).Select(a=>a.Id).FirstOrDefault(),revision=Revision};}
    public object Copy(string category,uint id){clipboard=assets.Find(category,id);return new {clipboard=true};}
    public object Paste(string category,uint[] ids,bool deleteAfterPaste)
    {
        CheckIds(ids);if(clipboard==null || clipboard.Category!=category)throw new InvalidDataException("Copy an object from the same category before pasting.");
        foreach(uint id in ids)assets.Find(category,id);
        var edits=ids.Select(id=>WithSource(clipboard,Rewrite(clipboard.Source,1,Scalar(1,id)),id)).ToArray();
        assets.SetAppearances(edits);
        if(deleteAfterPaste)clipboard=null;Changed();return new {selected=ids[0],clipboard=clipboard!=null,revision=Revision};
    }
    public object Flag(string category,uint id,int number,bool enabled)
    {
        if(!NativeFlags.Booleans.Contains(number))throw new InvalidDataException("This flag requires structured data; it cannot be edited as a boolean.");
        var a=assets.Find(category,id);var sourceFlags=Proto.Fields(a.Source).Where(f=>f.Number==3).SelectMany(f=>Proto.Bytes(f).ToArray()).ToArray();
        byte[] flags=Rewrite(sourceFlags,number,Scalar(number,enabled ? 1u : 0u));
        assets.SetAppearance(WithSource(a,Rewrite(a.Source,3,Blob(3,flags))));Changed();return new {selected=id,revision=Revision};
    }
    public object EditFlag(string category,uint id,int number,bool enabled,JsonElement instances)
    {
        var a=assets.Find(category,id);byte[] replacement=NativeFieldSchema.Edit(a,number,enabled,instances);
        var sourceFlags=Proto.Fields(a.Source).Where(f=>f.Number==3).SelectMany(f=>Proto.Bytes(f).ToArray()).ToArray();
        byte[] flags=Rewrite(sourceFlags,number,replacement);
        assets.SetAppearance(WithSource(a,Rewrite(a.Source,3,Blob(3,flags))));Changed();return new {selected=id,revision=Revision};
    }
    public object Sprite(uint? id,int width,int height,string argb)
    {
        if(width is not (32 or 64) || height is not (32 or 64) || argb.Length>21848)throw new InvalidDataException("Expected a 32/64-pixel PNG sprite.");
        byte[] raw=Convert.FromBase64String(argb);if(raw.Length!=width*height*4)throw new InvalidDataException("Invalid sprite payload.");
        byte[] rgba=new byte[raw.Length];for(int i=0;i<raw.Length;i+=4){rgba[i]=raw[i+1];rgba[i+1]=raw[i+2];rgba[i+2]=raw[i+3];rgba[i+3]=raw[i];}
        uint selected=id??NextSprite();cache.SetSprite(selected,new(width,height,rgba),id==null);Changed();return new {sprite=selected,revision=Revision};
    }
    public object ClearSprite(uint id){var shape=assets.FindSheet(id);cache.SetSprite(id,new(shape.Width,shape.Height,new byte[shape.Width*shape.Height*4]));Changed();return new {sprite=id,revision=Revision};}
    public object LinkSprite(string category,uint id,int group,int frame,int x,int y,int z,int layer,uint sprite)
    {
        assets.FindSheet(sprite);var a=assets.Find(category,id);var groups=AssetSet.Groups(a);
        if(group<0||group>=groups.Length)throw new InvalidDataException("Invalid group.");var g=groups[group];
        if(frame<0||frame>=g.Frames||x<0||x>=g.PatternX||y<0||y>=g.PatternY||z<0||z>=g.PatternZ||layer<0||layer>=g.Layers)throw new InvalidDataException("Invalid sprite reference selection.");
        int index=checked(((((frame*(int)g.PatternZ+z)*(int)g.PatternY+y)*(int)g.PatternX+x)*(int)g.Layers+layer));
        uint[] refs=g.Sprites.ToArray();refs[index]=sprite;
        var frames=Proto.Fields(a.Source).Where(f=>f.Number==2).ToArray();var target=frames[group];var info=Proto.Fields(target.Data).Single(f=>f.Number==3);
        byte[] newFrame=Rewrite(target.Data,3,Blob(3,Rewrite(info.Data,5,refs.SelectMany(i=>Scalar(5,i)).ToArray())));
        var encoded=frames.SelectMany((f,i)=>Blob(2,i==group ? newFrame : f.Data)).ToArray();
        assets.SetAppearance(WithSource(a,Rewrite(a.Source,2,encoded)));Changed();return new {selected=id,sprite,revision=Revision};
    }
    public object DuplicateSprite(uint id){uint next=NextSprite();var p=cache.Sprite(id);cache.SetSprite(next,new(p.Width,p.Height,p.Rgba.ToArray()),true);Changed();return new {sprite=next,revision=Revision};}
    public object ExportObject(string category,uint id,string file)
    {
        ValidateExport(file);var a=assets.Find(category,id);var refs=AssetSet.Groups(a).SelectMany(g=>g.Sprites).Distinct().ToArray();
        if(refs.Length>256)throw new InvalidDataException("Portable object packages currently support at most 256 distinct sprites. Export DAT/SPR for larger objects.");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=1,category,source=Convert.ToBase64String(a.Source.Span),sprites=refs.Select(i=>{var p=cache.Sprite(i);return new {id=i,width=p.Width,height=p.Height,rgba=Convert.ToBase64String(p.Rgba)};}).ToArray()});
        if(bytes.Length>8*1024*1024)throw new InvalidDataException("Object package exceeds 8 MiB.");
        using var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write);stream.Write(bytes);return new {file};
    }
    public object ImportObject(string category,string file,uint? target=null)
    {
        AssetSet.RejectLinks(file);using var doc=JsonDocument.Parse(AssetSet.ReadBounded(file,8*1024*1024),new JsonDocumentOptions{MaxDepth=16});var root=doc.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1 || root.GetProperty("category").GetString()!=category)throw new InvalidDataException("Package version/category mismatch.");
        byte[] source=Convert.FromBase64String(root.GetProperty("source").GetString()??"");if(source.Length>1024*1024)throw new InvalidDataException("Object protobuf exceeds 1 MiB.");
        var raw=Proto.Fields(source);uint originalId=Proto.UInt(raw.Single(f=>f.Number==1));
        string name=raw.Where(f=>f.Number==4).Select(Proto.Text).LastOrDefault()??"";var a=new Appearance(originalId,category,name,1,source);var groups=AssetSet.Groups(a);
        var rows=root.GetProperty("sprites");if(rows.GetArrayLength() is <1 or >256)throw new InvalidDataException("Invalid package sprite count.");
        var staged=new Dictionary<uint,Pixels>();long bytes=0;
        foreach(var row in rows.EnumerateArray()){uint id=row.GetProperty("id").GetUInt32();int w=row.GetProperty("width").GetInt32(),h=row.GetProperty("height").GetInt32();byte[] pixels=Convert.FromBase64String(row.GetProperty("rgba").GetString()??"");if(w is not (32 or 64)||h is not (32 or 64)||pixels.Length!=w*h*4||!staged.TryAdd(id,new(w,h,pixels)))throw new InvalidDataException("Invalid package pixels/duplicate ID.");bytes+=pixels.Length;}
        if(groups.SelectMany(g=>g.Sprites).Any(id=>!staged.ContainsKey(id)))throw new InvalidDataException("Package has missing sprite dependencies.");
        if(cache.ChangedBytes+bytes>64*1024*1024 || assets.Appearances.Count>=199999 || (ulong)assets.Sheets[^1].Last+(uint)staged.Count>=uint.MaxValue)throw new InvalidDataException("Working-copy memory/ID limit.");
        if(target!=null)assets.Find(category,target.Value);
        uint next=NextSprite();var map=staged.Keys.ToDictionary(id=>id,_=>next++);uint appearance=target??NextId(category);
        var frames=new List<byte>();int groupIndex=0;foreach(var field in raw.Where(f=>f.Number==2)){var frame=Proto.Bytes(field);var info=Proto.Fields(frame).Single(f=>f.Number==3);var g=groups[groupIndex++];byte[] ids=g.Sprites.SelectMany(id=>Scalar(5,map[id])).ToArray();frames.AddRange(Blob(2,Rewrite(frame,3,Blob(3,Rewrite(info.Data,5,ids)))));}
        source=Rewrite(Rewrite(source,2,frames.ToArray()),1,Scalar(1,appearance));
        var imported=new Appearance(appearance,category,name,groups.Length,source);
        assets.ValidateAppearanceEdits([imported]);
        foreach(var (old,pixels) in staged)cache.SetSprite(map[old],pixels,true);
        assets.SetAppearance(imported);Changed();return new {selected=appearance,revision=Revision};
    }
    public object ExportPng(string category,uint id,string file,bool sprite,int group=0,int frame=0,int x=0,int y=0,int z=0)
    {
        ValidateExport(file);Pixels image;
        if(sprite)image=cache.Sprite(id);else {var groups=AssetSet.Groups(assets.Find(category,id));if(group<0||group>=groups.Length)throw new InvalidDataException("Invalid group.");image=cache.Render(groups[group],frame,x,y,z);}
        Png.Write(file,image);return new {file};
    }
    public object ImportAssets(string[] files,string category,uint? target=null)
    {
        if(files.Length is <1 or >100)throw new InvalidDataException("Choose 1–100 OBD/AEC files.");
        var imports=new List<ImportedObject>();var notices=new List<string>();long bytes=0,metadata=0;
        foreach(string file in files){var batch=NativeObjectImport.Read(file);imports.AddRange(batch.Objects);notices.AddRange(batch.Notices);bytes+=batch.Objects.Sum(o=>o.Sprites.Sum(p=>(long)p.Rgba.Length));metadata+=batch.Objects.Sum(o=>(long)o.Appearance.Source.Length);if(imports.Count>100||bytes>NativeObjectImport.PixelLimit||metadata>4*1024*1024)throw new InvalidDataException("Import exceeds 100 objects, 32 MiB of pixels or 4 MiB of metadata.");}
        if(target!=null){assets.Find(category,target.Value);if(imports.Count!=1||imports[0].Appearance.Category!=category)throw new InvalidDataException("Replacement needs one object of the selected category.");}
        if(cache.ChangedBytes+bytes>64*1024*1024)throw new InvalidDataException("Working-copy pixels exceed 64 MiB; save before importing.");
        int spriteCount=imports.Sum(o=>o.Sprites.Length);
        if((ulong)assets.Sheets[^1].Last+(uint)spriteCount>=uint.MaxValue)throw new InvalidDataException("Sprite ID limit.");
        uint nextSprite=NextSprite();var nextIds=new Dictionary<string,uint>();var edits=new List<Appearance>();var pixels=new List<(uint Id,Pixels Image)>();
        foreach(var imported in imports){
            var a=imported.Appearance;var groups=AssetSet.Groups(a);
            foreach(uint id in groups.SelectMany(g=>g.Sprites))if(id>=imported.Sprites.Length)throw new InvalidDataException("Missing imported sprite.");
            uint idNew;
            if(target.HasValue)idNew=target.Value;
            else{if(!nextIds.TryGetValue(a.Category,out uint next))next=NextId(a.Category);idNew=next;nextIds[a.Category]=checked(next+1);}
            uint first=nextSprite;
            foreach(var image in imported.Sprites){if(image.Width is not (32 or 64)||image.Height is not (32 or 64)||image.Rgba.Length!=image.Width*image.Height*4)throw new InvalidDataException("Invalid imported pixels.");pixels.Add((nextSprite++,image));}
            byte[] source=NativeObjectImport.Remap(a.Source,groups.Select(g=>g.Sprites.Select(id=>checked(first+id)).ToArray()).ToArray());
            source=Rewrite(source,1,Scalar(1,idNew));edits.Add(a with {Id=idNew,Source=source});
        }
        assets.ValidateAppearanceEdits(edits); // The complete batch is checked before allocating IDs or changing the catalog.
        foreach(var (id,image) in pixels)cache.SetSprite(id,image,true);
        assets.SetAppearances(edits);Changed();
        return new {selected=edits[0].Id,category=edits[0].Category,imported=edits.Count,revision=Revision,notices=notices.Distinct().ToArray()};
    }
    public object ExportAec(string category,uint[] ids,string file)
    {
        CheckIds(ids);ValidateExport(file);var appearances=ids.Select(id=>assets.Find(category,id)).ToArray();
        using var output=new MemoryStream();uint next=0;long pixels=0;
        foreach(var a in appearances){
            var groups=AssetSet.Groups(a);var refs=groups.SelectMany(g=>g.Sprites).ToArray();
            if(refs.Length>8192)throw new InvalidDataException("AEC object exceeds 8,192 references.");
            byte[] source=NativeObjectImport.Remap(a.Source,groups.Select(g=>g.Sprites.Select(_=>next++).ToArray()).ToArray());source=Rewrite(source,7,[]);
            source=Rewrite(source,6,Scalar(6,(uint)Array.IndexOf(AssetSet.Categories,category)+1));
            using var record=new MemoryStream();record.Write(source);
            foreach(uint id in refs){var image=cache.Sprite(id);pixels+=image.Rgba.Length;if(pixels>NativeObjectImport.PixelLimit)throw new InvalidDataException("AEC export exceeds 32 MiB of pixels.");record.Write(Blob(7,Png.Encode(image)));if(record.Length>4*1024*1024)throw new InvalidDataException("AEC object exceeds 4 MiB.");}
            if(record.Length>4*1024*1024)throw new InvalidDataException("AEC object exceeds 4 MiB.");
            output.Write(Blob(Array.IndexOf(AssetSet.Categories,category)+1,record.ToArray()));
            if(output.Length>NativeObjectImport.FileLimit)throw new InvalidDataException("AEC export exceeds 32 MiB.");
        }
        string stage=file+".partial";if(File.Exists(stage)||Directory.Exists(stage))throw new InvalidDataException("AEC export staging file already exists.");
        bool owned=false;
        try{using(var stream=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None)){owned=true;output.Position=0;output.CopyTo(stream);stream.Flush(true);}File.Move(stage,file,false);}
        catch{if(owned&&File.Exists(stage))File.Delete(stage);throw;}
        return new {file,count=appearances.Length};
    }
    public object ExportBatch(string category,uint[] ids,string target,bool sprite,bool package)
    {
        CheckIds(ids);if(!AssetSet.Categories.Contains(category))throw new InvalidDataException("Invalid category.");
        target=Path.GetFullPath(target);ValidateExport(target);string stage=target+".partial";
        if(File.Exists(stage)||Directory.Exists(stage))throw new InvalidDataException("Export staging directory already exists.");
        Directory.CreateDirectory(stage);var files=new List<string>();
        try{
            foreach(uint id in ids){string file=Path.Combine(stage,(sprite ? "sprite" : category)+"-"+id+(!sprite && package ? ".nexaobject" : ".png"));files.Add(file);if(!sprite&&package)ExportObject(category,id,file);else ExportPng(category,id,file,sprite);}
            Directory.Move(stage,target);return new {directory=target,count=ids.Length};
        }catch{foreach(string file in files)try{File.Delete(file);}catch(IOException){}try{Directory.Delete(stage,false);}catch(IOException){}throw;}
    }
    private void ValidateExport(string file){file=Path.GetFullPath(file);AssetSet.RejectLinks(Path.GetDirectoryName(file)!);if(File.Exists(file)||Directory.Exists(file)||file.StartsWith(assets.Root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose a new file outside the source assets; existing files are not overwritten.");}
    private static void CheckIds(uint[] ids){if(ids.Length is <1 or >100 || ids.Distinct().Count()!=ids.Length)throw new InvalidDataException("Select 1–100 distinct object IDs.");}
    private Appearance BlankAppearance(string category,uint sprite)
    {
        uint id=NextId(category);
        // Real protobuf consumers read these scalars directly; omitted dimensions
        // default to zero even though the preview reader accepts a one-cell fallback.
        byte[] info=Enumerable.Range(1,4).SelectMany(n=>Scalar(n,1)).Concat(Scalar(5,sprite)).ToArray();
        byte[] group=Scalar(1,category=="outfit" ? 0u : 2u).Concat(Blob(3,info)).ToArray();
        return new(id,category,"",1,Scalar(1,id).Concat(Blob(2,group)).ToArray());
    }
    private void CheckNewObject(string category){if(!AssetSet.Categories.Contains(category)||assets.Appearances.Count>=199999)throw new InvalidDataException("Invalid category or working-copy object limit.");NextId(category);}
}
