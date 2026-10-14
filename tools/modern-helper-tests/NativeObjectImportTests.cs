using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using NexaModern;
using SharpCompress.Compressors.LZMA;

internal static class NativeObjectImportTests
{
    private static byte[] S(int n,ulong v)=>NativeWorkspace.Scalar(n,v);
    private static byte[] B(int n,byte[] v)=>NativeWorkspace.Blob(n,v);
    private static byte[] Join(params byte[][] values)=>values.SelectMany(v=>v).ToArray();
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject(Action action,string message){try{action();}catch(Exception e)when(e is InvalidDataException or EndOfStreamException){return;}throw new Exception("Import did not reject: "+message);}
    private static byte[] Compress(byte[] raw)
    {
        using var output=new MemoryStream();byte[] props;
        using(var encoder=new LzmaStream(new LzmaEncoderProperties(true,1<<20),false,output)){props=encoder.Properties.ToArray();encoder.Write(raw);}
        var size=new byte[8];BinaryPrimitives.WriteUInt64LittleEndian(size,(ulong)raw.Length);
        return Join(props,size,output.ToArray());
    }
    private static byte[] Obd(int format,int version,bool outfit=false,int width=2)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream,Encoding.UTF8,true);
        bool old=format==100;w.Write((ushort)(old ? version : format));if(!old)w.Write((ushort)version);
        string category=outfit ? "outfit" : "item";
        if(old){w.Write((ushort)category.Length);w.Write(Encoding.UTF8.GetBytes(category));}else w.Write((byte)(outfit ? 2 : 1));
        long offset=stream.Position;if(!old)w.Write(0u);
        w.Write((byte)0);w.Write((ushort)150);w.Write((byte)(old && version<=750 ? 4 : 5));
        int light=old ? version<=750 ? 16 : version<=772 || version is >=860 and <=986 ? 21 : 22 : 22;
        int shift=old ? version<=750 ? 20 : version<=772 || version is >=860 and <=986 ? 24 : 25 : 25;
        w.Write((byte)light);w.Write((ushort)7);w.Write((ushort)215);
        w.Write((byte)shift);if(!old||version>750){w.Write((short)-3);w.Write((short)5);}
        if(!old){w.Write((byte)34);w.Write((ushort)9);w.Write((ushort)222);w.Write((ushort)333);w.Write((ushort)4);w.Write(Encoding.Latin1.GetBytes("Café"));w.Write((ushort)2);w.Write((ushort)35);w.Write((byte)252);w.Write((byte)253);}
        if(old&&version>=800){w.Write((byte)39);for(int n=0;n<8;n++)w.Write((short)(n-4));}
        w.Write((byte)255);long texture=stream.Position;
        if(!old){stream.Position=offset;w.Write((uint)texture);stream.Position=texture;}
        int groups=format==300 && outfit ? 2 : 1;if(format==300&&outfit)w.Write((byte)groups);
        for(int group=0;group<groups;group++){
            if(format==300&&outfit)w.Write((byte)group);
            w.Write((byte)width);w.Write((byte)2);w.Write((byte)64);
            foreach(byte b in new byte[]{1,1,1,1,2})w.Write(b);
            if(!old){w.Write((byte)1);w.Write(3);w.Write((sbyte)-1);for(int frame=0;frame<2;frame++){w.Write(120u);w.Write(130u);}}
            for(int cell=0;cell<2;cell++)for(int tile=0;tile<width*2;tile++){
                w.Write((uint)(100+tile));if(format!=200)w.Write(4096u);
                byte c=(byte)(20+group*32+cell*4+tile);for(int p=0;p<1024;p++)w.Write(new byte[]{128,c,63,71});
            }
        }
        return Compress(stream.ToArray());
    }
    public static void Run(string output)
    {
        string folder=Path.Combine(output,"native-object-import");Directory.CreateDirectory(folder);
        foreach(int format in new[]{100,200,300})foreach(int version in format==100 ? new[]{710,740,760,800,860,1098} : new[]{860,1098}){
            var batch=NativeObjectImport.Obd(Obd(format,version,format==300));var imported=batch.Objects.Single();
            var properties=NativeAssets.Properties(imported.Appearance);var groups=AssetSet.Groups(imported.Appearance);
            Check((uint)properties["groundSpeed"]==150 && (bool)properties["stackable"] && (uint)properties["lightLevel"]==7,"OBD family property mapping");
            Check((int)properties["offsetX"]==(format==100&&version<=750 ? 8 : -3),"OBD signed/implicit offset");
            Check(groups.Length==(format==300 ? 2 : 1) && groups.All(g=>g.Frames==2),"OBD group/frame preservation");
            Check(groups[0].Phases[0].Min==(format==100 ? 100u : 120u),"OBD duration preservation");
            if(format!=100)Check(batch.Notices.Length==2&&NativeFlags.Fields(imported.Appearance).Count(f=>f.Number==200)==3&&imported.Appearance.Name=="Café","Legacy-only masks/flags must be retained and reported, not guessed");
            else if(version>=800)Check(batch.Notices.Any(n=>n.Contains("bone offsets"))&&NativeFlags.Fields(imported.Appearance).Any(f=>f.Number==200),"Legacy bones preserved with notice");
            var image=imported.Sprites[0];Check(image.Width==64&&image.Height==64,"OBD tile composition geometry");
            foreach(var (x,y,c) in new[]{(0,0,23),(32,0,22),(0,32,21),(32,32,20)})Check(image.Rgba.AsSpan((y*64+x)*4,4).SequenceEqual(new byte[]{(byte)c,63,71,128}),"OBD bottom-right tile order / straight alpha");
            File.WriteAllBytes(Path.Combine(folder,$"v{format}-{version}.obd"),Obd(format,version,format==300));
        }
        Reject(()=>NativeObjectImport.Obd(Obd(200,860,width:3)),"unsupported large sprite without mutation");
        byte[] forged=Obd(200,860);BinaryPrimitives.WriteUInt64LittleEndian(forged.AsSpan(5),128*1024*1024);Reject(()=>NativeObjectImport.Obd(forged),"LZMA declared length bomb");
        forged=Obd(200,860);BinaryPrimitives.WriteUInt32LittleEndian(forged.AsSpan(1),uint.MaxValue);Reject(()=>NativeObjectImport.Obd(forged),"LZMA dictionary bomb");
        Reject(()=>NativeObjectImport.Obd(Compress(new byte[]{200,0,92,3,1,0,0,0,0,255})),"truncated group");
        Reject(()=>NativeObjectImport.Obd(Compress(new byte[]{200,0,92,3,1,0,0,0,0,222,255})),"unknown OBD property");

        var rgba=new Pixels(32,64,new byte[8192]);for(int i=0;i<rgba.Rgba.Length;i+=4){rgba.Rgba[i]=81;rgba.Rgba[i+1]=62;rgba.Rgba[i+2]=37;rgba.Rgba[i+3]=(byte)(i%256);}
        byte[] png=Png.Encode(rgba);uint containerId=500;
        byte[] Appearance(string category,uint id){int groups=category=="outfit" ? 2 : 1;var fields=new List<byte[]>([S(1,id),S(6,(uint)Array.IndexOf(AssetSet.Categories,category)+1),B(4,Encoding.UTF8.GetBytes("AEC name")),B(99,new byte[]{44}),B(3,Join(S(6,1),B(88,new byte[]{3,4})))]);
            for(int i=0;i<groups;i++){byte[] info=Join(S(1,1),S(2,1),S(3,1),S(4,1),S(5,containerId++));fields.Add(B(2,Join(S(1,category=="outfit" ? (uint)i : 2),B(3,info))));}
            for(int i=0;i<groups;i++)fields.Add(B(7,png));return Join(fields.ToArray());}
        byte[] aec=Join(AssetSet.Categories.Select((c,i)=>B(i+1,Appearance(c,c=="item" ? 100u : 1u))).ToArray());
        string aecFile=Path.Combine(folder,"all-categories.aec");File.WriteAllBytes(aecFile,aec);
        var decoded=NativeObjectImport.Aec(aec);Check(decoded.Objects.Length==4,"AEC mixed categories");
        Reject(()=>NativeObjectImport.Aec(B(1,NativeWorkspace.Rewrite(Appearance("item",100),3,S(3,1)))),"malformed flags before mutation");
        foreach(var obj in decoded.Objects){Check(obj.Sprites.All(p=>p.Rgba.SequenceEqual(rgba.Rgba)),"AEC embedded PNG straight RGBA");Check(!Proto.Fields(obj.Appearance.Source).Any(f=>f.Number==7)&&Proto.Fields(obj.Appearance.Source).Any(f=>f.Number==99),"AEC container-only pixels removed / unknown metadata preserved");}
        string root=Path.Combine(folder,"assets");Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root,"sheet.lzma"),NativeSave.EncodeSheet(new Pixels(384,384,new byte[384*384*4])));
        byte[] original=NativeWorkspace.Rewrite(Appearance("item",100),7,[]);original=NativeObjectImport.Remap(original,[[1u]]);
        File.WriteAllBytes(Path.Combine(root,"appearances.dat"),B(1,original));
        File.WriteAllText(Path.Combine(root,"catalog-content.json"),"[{\"type\":\"appearances\",\"file\":\"appearances.dat\"},{\"type\":\"sprite\",\"file\":\"sheet.lzma\",\"spritetype\":0,\"firstspriteid\":1,\"lastspriteid\":3}]");
        var assets=new AssetSet(root);var cache=new SheetCache(assets);var workspace=new NativeWorkspace(assets,cache);
        string obd=Path.Combine(folder,"v300-1098.obd");
        workspace.ImportAssets([aecFile,obd],"item");Check(workspace.Revision==1 && assets.Appearances.Count==6,"Single batch revision / mixed category commit");
        Check(assets.Find("item",100).Source.Span.SequenceEqual(original),"Import must not replace a colliding ID implicitly");
        uint id=AssetSet.Groups(assets.Find("item",101))[0].Sprites[0];Check(cache.Sprite(id).Rgba.SequenceEqual(rgba.Rgba),"Fresh sprite IDs without collisions");
        string exported=Path.Combine(folder,"exported.aec");workspace.ExportAec("item",[101],exported);var exportedObject=NativeObjectImport.Read(exported).Objects.Single();Check(exportedObject.Sprites[0].Rgba.SequenceEqual(rgba.Rgba),"AEC interoperable export pixels");
        long before=workspace.Revision,budget=cache.ChangedBytes;int sheets=assets.Sheets.Count,count=assets.Appearances.Count;
        string bad=Path.Combine(folder,"invalid.aec");File.WriteAllBytes(bad,B(1,NativeWorkspace.Rewrite(Appearance("item",100),7,[])));
        Reject(()=>workspace.ImportAssets([aecFile,bad],"item"),"whole batch prevalidation");
        Reject(()=>workspace.ImportAssets([aecFile],"item",100),"multiobject replacement");
        Reject(()=>workspace.ImportAssets([obd],"item",100),"wrong category replacement");
        string large=Path.Combine(folder,"large-metadata.aec");File.WriteAllBytes(large,B(1,Join(Appearance("item",100),B(77,new byte[900000]))));
        Reject(()=>workspace.ImportAssets(Enumerable.Repeat(large,6).ToArray(),"item"),"aggregate metadata budget before mutation");
        Reject(()=>workspace.ExportAec("item",[101],exported),"existing AEC output is never overwritten");
        string blocked=Path.Combine(folder,"blocked.aec");File.WriteAllText(blocked+".partial","not owned by this export");
        Reject(()=>workspace.ExportAec("item",[101],blocked),"existing export staging ownership");Check(File.ReadAllText(blocked+".partial")=="not owned by this export","Export removed an unowned staging file");
        Check(workspace.Revision==before&&cache.ChangedBytes==budget&&assets.Sheets.Count==sheets&&assets.Appearances.Count==count,"Rejected import changed working copy");
        workspace.ImportAssets([exported],"item",100);Check(assets.Appearances.Count==count && assets.Find("item",100).Name=="AEC name","Explicit replacement preserves target ID");
        NativeSave.InPlace(assets,cache);var reopened=new AssetSet(root);var reopenedCache=new SheetCache(reopened);
        Check(reopened.Appearances.Count==count&&reopenedCache.Sprite(AssetSet.Groups(reopened.Find("item",100))[0].Sprites[0]).Rgba.SequenceEqual(rgba.Rgba),"OBD/AEC import Save and native reopen");
        Check(AssetSet.Groups(reopened.Find("outfit",2)).Length==2,"Imported OBD outfit groups survive native Save");
        Console.WriteLine("PASS: native OBD v1/v2/v3, six metadata families, signed offsets, tile order/RGBA/timings; AEC mixed categories, unknown fields, ID remap, export, atomic rejection/replacement and Save/reopen");
    }
}
