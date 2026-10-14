using System.Buffers.Binary;
using System.Text;
using SharpCompress.Compressors.LZMA;

namespace NexaModern;

public sealed record ImportedObject(Appearance Appearance, Pixels[] Sprites);
public sealed record ObjectImportBatch(ImportedObject[] Objects, string[] Notices);

/// <summary>Independent OBD/AEC interoperability reader. Decode fully before mutating the working copy.</summary>
public static class NativeObjectImport
{
    public const int FileLimit=32*1024*1024, PixelLimit=32*1024*1024;
    private const int RawLimit=34*1024*1024;
    private static byte[] S(int field,ulong value)=>NativeWorkspace.Scalar(field,value);
    private static byte[] B(int field,ReadOnlyMemory<byte> value)=>NativeWorkspace.Blob(field,value);
    private static byte[] Join(params byte[][] values)=>values.SelectMany(v=>v).ToArray();
    public static ObjectImportBatch Read(string file)
    {
        AssetSet.RejectLinks(file);byte[] bytes=AssetSet.ReadBounded(file,FileLimit);
        return Path.GetExtension(file).ToLowerInvariant() switch {
            ".obd"=>Obd(bytes), ".aec"=>Aec(bytes),
            _=>throw new InvalidDataException("Choose an OBD or Assets Editor AEC object container.")
        };
    }
    public static ObjectImportBatch Aec(byte[] bytes)
    {
        if(bytes.Length>FileLimit)throw new InvalidDataException("AEC input exceeds 32 MiB.");
        var objects=new List<ImportedObject>();long pixels=0,metadata=0;var reader=new Proto(bytes);
        while(!reader.End){
            var top=reader.Next();if(top.Number is <1 or >4)continue;
            if(objects.Count==100)throw new InvalidDataException("AEC supports at most 100 objects per import.");
            string category=AssetSet.Categories[top.Number-1];var source=Proto.Bytes(top);
            if(source.Length>4*1024*1024)throw new InvalidDataException("AEC object exceeds 4 MiB.");
            var fields=Proto.Fields(source);var ids=fields.Where(f=>f.Number==1).ToArray();
            if(ids.Length!=1)throw new InvalidDataException("AEC object needs exactly one ID.");
            foreach(var type in fields.Where(f=>f.Number==6))if(Proto.UInt(type)!=top.Number)throw new InvalidDataException("AEC appearance category mismatch.");
            string name=fields.Where(f=>f.Number==4).Select(Proto.Text).LastOrDefault()??"";
            var appearance=new Appearance(Proto.UInt(ids[0]),category,name,fields.Count(f=>f.Number==2),source);
            NativeFlags.Fields(appearance); // Validate the flag message wire framing, preserving unknown payloads verbatim.
            var groups=AssetSet.Groups(appearance);int count=groups.Sum(g=>g.Sprites.Length);var data=fields.Where(f=>f.Number==7).ToArray();
            if(count is <1 or >8192 || data.Length!=count)throw new InvalidDataException("AEC embedded pixels must match every sprite reference.");
            var sprites=new List<Pixels>();
            foreach(var field in data){var image=NativePng.Read(Proto.Bytes(field).ToArray());pixels+=image.Rgba.Length;if(pixels>PixelLimit)throw new InvalidDataException("Imported pixels exceed 32 MiB.");sprites.Add(image);}
            uint next=0;byte[] remapped=Remap(source,groups.Select(g=>g.Sprites.Select(_=>next++).ToArray()).ToArray());
            remapped=NativeWorkspace.Rewrite(remapped,7,[]); // Container-only images must not leak into appearances.dat.
            metadata+=remapped.Length;if(metadata>4*1024*1024)throw new InvalidDataException("Imported metadata exceeds 4 MiB.");
            objects.Add(new(appearance with {Source=remapped},sprites.ToArray()));
        }
        if(objects.Count==0)throw new InvalidDataException("AEC contains no objects.");
        return new(objects.ToArray(),[]);
    }
    public static byte[] Remap(ReadOnlyMemory<byte> source,uint[][] references)
    {
        int index=0;var frames=Proto.Fields(source).Where(f=>f.Number==2).Select(f=>{
            var info=Proto.Fields(Proto.Bytes(f)).Single(v=>v.Number==3);
            return B(2,NativeWorkspace.Rewrite(Proto.Bytes(f),3,B(3,NativeWorkspace.Rewrite(Proto.Bytes(info),5,references[index++].SelectMany(id=>S(5,id)).ToArray()))));
        }).ToArray();
        if(index!=references.Length)throw new InvalidDataException("Frame group count mismatch.");
        return NativeWorkspace.Rewrite(source,2,Join(frames));
    }
    public static ObjectImportBatch Obd(byte[] compressed)
    {
        var r=new Reader(Expand(compressed));int format=r.U16(),version=format is 200 or 300 ? r.U16() : format;
        if(format is not (200 or 300) && format<710)throw new InvalidDataException("Expected OBD v1, v2 or v3.");
        var profile=LegacyProfile.For(version);string category;
        if(format is 200 or 300){int type=r.Byte();if(type is <1 or >4)throw new InvalidDataException("Invalid OBD category.");category=AssetSet.Categories[type-1];}
        else {category=r.Utf();if(!AssetSet.Categories.Contains(category))throw new InvalidDataException("Invalid OBD category.");}
        uint texture=format is 200 or 300 ? r.U32() : 0;var notices=new List<string>();string name="";
        byte[] flags=Flags(r,format is 200 or 300 ? 6 : profile.Family,format is 200 or 300,notices,ref name);
        if(texture!=0 && texture!=r.Position)throw new InvalidDataException("OBD texture offset does not match the property boundary.");
        int groupCount=format==300 && category=="outfit" ? r.Byte() : 1;
        if(groupCount is <1 or >2)throw new InvalidDataException("OBD supports one or two outfit groups.");
        var groups=new List<byte[]>();var sprites=new List<Pixels>();var types=new HashSet<int>();
        for(int gi=0;gi<groupCount;gi++){
            int type=format==300 && category=="outfit" ? r.Byte() : category=="outfit" ? 1 : 2;
            if(category=="outfit" && (type>1 || !types.Add(type)))throw new InvalidDataException("Invalid/duplicate outfit group type.");
            int w=r.Byte(),h=r.Byte(),exact=w>1||h>1 ? r.Byte() : 32,layers=r.Byte(),x=r.Byte(),y=r.Byte(),z=r.Byte(),frames=r.Byte();
            if(w is <1 or >2 || h is <1 or >2)throw new InvalidDataException("OBD tiles larger than 64x64 require unsupported native sprite dimensions.");
            if(new[]{layers,x,y,z,frames,exact}.Any(n=>n<1))throw new InvalidDataException("Invalid OBD dimensions.");
            long count=(long)w*h*layers*x*y*z*frames;if(count>4096)throw new InvalidDataException("OBD group exceeds 4,096 tile references.");
            var info=new List<byte[]>([S(1,(uint)x),S(2,(uint)y),S(3,(uint)z),S(4,(uint)layers),S(7,(uint)exact)]);
            if(frames>1){
                int mode=0,loops=0,start=0;if(format is 200 or 300){mode=r.Byte();loops=r.I32();start=r.I8();}
                if(mode>1||loops< -1||start< -1||start>=frames)throw new InvalidDataException("Invalid OBD animation settings.");
                var animation=new List<byte[]>([S(1,(uint)Math.Max(0,start)),S(2,(uint)mode),S(3,start==-1 ? 1u : 0u),S(4,unchecked((ulong)(long)(loops==-1 ? -1 : loops==0 ? 0 : 1))),S(5,(uint)Math.Max(0,loops)),S(7,(uint)mode)]);
                for(int f=0;f<frames;f++){uint min=100,max=100;if(format is 200 or 300){min=r.U32();max=r.U32();}if(min>max||max>3600000)throw new InvalidDataException("Invalid OBD duration.");animation.Add(B(6,Join(S(1,min),S(2,max))));}
                if(format is not (200 or 300))notices.Add("OBD v1 has no frame timing; imported animation uses 100 ms per frame.");
                info.Add(B(6,Join(animation.ToArray())));
            }
            for(int cell=0;cell<count/(w*h);cell++){
                var image=new Pixels(w*32,h*32,new byte[w*h*4096]);
                for(int row=0;row<h;row++)for(int col=0;col<w;col++){
                    r.U32();uint size=format==200 ? 4096u : r.U32();if(size!=4096)throw new InvalidDataException("OBD sprite must contain exactly 4,096 ARGB bytes.");
                    var argb=r.Bytes(4096);
                    for(int py=0;py<32;py++)for(int px=0;px<32;px++){
                        int src=(py*32+px)*4,dst=(((h-row-1)*32+py)*image.Width+(w-col-1)*32+px)*4;
                        image.Rgba[dst]=argb[src+1];image.Rgba[dst+1]=argb[src+2];image.Rgba[dst+2]=argb[src+3];image.Rgba[dst+3]=argb[src];
                    }
                }
                info.Add(S(5,(uint)sprites.Count));sprites.Add(image);
            }
            groups.Add(B(2,Join(S(1,(uint)type),B(3,Join(info.ToArray())))));
        }
        if(!r.End)throw new InvalidDataException("Trailing OBD data.");
        byte[] source=Join(S(1,category=="item" ? 100u : 1u),Join(groups.ToArray()),B(3,flags),B(4,Encoding.UTF8.GetBytes(name)));
        var a=new Appearance(category=="item" ? 100u : 1u,category,name,groupCount,source);AssetSet.Groups(a);
        return new([new(a,sprites.ToArray())],notices.Distinct().ToArray());
    }
    private static byte[] Expand(byte[] bytes)
    {
        if(bytes.Length is <14 or >FileLimit)throw new InvalidDataException("Invalid OBD compressed length.");
        byte[] props=bytes.AsSpan(0,5).ToArray();uint dictionary=BinaryPrimitives.ReadUInt32LittleEndian(props.AsSpan(1));ulong length=BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(5));
        if(props[0]>=225 || dictionary is 0 or >64*1024*1024 || length!=ulong.MaxValue && length>RawLimit)throw new InvalidDataException("OBD LZMA properties/output exceed limits.");
        BinaryPrimitives.WriteUInt32LittleEndian(props.AsSpan(1),Math.Min(dictionary,(uint)RawLimit));
        using var input=new MemoryStream(bytes,13,bytes.Length-13,false);using var decoder=new LzmaStream(props,input,input.Length,length==ulong.MaxValue ? -1 : (long)length);
        using var output=new MemoryStream();var chunk=new byte[8192];int count;
        while((count=decoder.Read(chunk))>0){if(output.Length+count>RawLimit)throw new InvalidDataException("OBD decompression exceeds 34 MiB.");output.Write(chunk,0,count);}
        if(length!=ulong.MaxValue && output.Length!=(long)length)throw new InvalidDataException("Truncated OBD LZMA payload.");
        return output.ToArray();
    }
    private static int Canonical(int wire,int family,bool obd)
    {
        if(wire==255||obd)return wire;
        if(wire==39 && family>=4)return 39;
        if(family==6)return wire<=38||wire==254 ? wire : -1;
        if(family==5)return wire<=15 ? wire : wire is >=16 and <=33 ? wire+1 : -1;
        if(family==4)return wire==8 ? 252 : wire==24 ? 253 : wire is >=9 and <=16 ? wire-1 : wire<=32 ? wire : wire is 36 or 37 ? wire : -1;
        if(family==3)return wire==23 ? 253 : wire<=15 ? wire : wire is >=16 and <=30 && wire!=22 ? wire+1 : -1;
        int[] old=[0,2,3,4,5,7,6,8,9,10,11,12,13,14,15,17,22,253,31,26,25,-1,29,21,27];
        if(wire<old.Length)return old[wire];
        if(family==1)return wire switch {25=>28,26=>30,36=>36,37=>37,38=>38,_=>-1};
        return wire switch {25=>18,26=>19,27=>20,28=>28,29=>30,36=>36,37=>37,38=>38,_=>-1};
    }
    private static byte[] Flags(Reader r,int family,bool obd,List<string> notices,ref string name)
    {
        var fields=new List<byte[]>();var seen=new HashSet<int>();
        var boolean=new Dictionary<int,int>{[1]=2,[2]=3,[3]=4,[4]=5,[5]=6,[6]=8,[7]=9,[10]=19,[11]=12,[12]=13,[13]=14,[14]=15,[15]=16,[16]=17,[17]=18,[18]=20,[21]=22,[23]=24,[24]=25,[27]=28,[28]=29,[31]=32,[32]=33,[36]=37,[37]=38,[38]=39,[254]=7};
        while(true){int flag=Canonical(r.Byte(),family,obd);if(flag==255)break;if(flag<0||!seen.Add(flag))throw new InvalidDataException("Unknown/duplicate OBD property.");
            if(boolean.TryGetValue(flag,out int field)){fields.Add(S(field,1));continue;}
            switch(flag){
                case 0:fields.Add(B(1,S(1,r.U16())));break;
                case 8:case 9:fields.Add(B(flag==8 ? 10 : 11,S(1,r.U16())));break;
                case 19:case 20:if(seen.Contains(flag==19 ? 20 : 19))throw new InvalidDataException("Conflicting OBD hook directions.");fields.Add(B(21,S(1,flag==19 ? 1u : 2u)));break;
                case 22:fields.Add(B(23,Join(S(1,r.U16()),S(2,r.U16()))));break;
                case 25:fields.Add(B(26,Join(S(1,unchecked((ulong)(long)(family<=2&&!obd ? 8 : r.I16()))),S(2,unchecked((ulong)(long)(family<=2&&!obd ? 8 : r.I16()))))));break;
                case 26:case 29:case 30:case 33:case 35:fields.Add(B(flag switch {26=>27,29=>30,30=>31,33=>34,_=>35},S(1,r.U16())));break;
                case 34:
                    uint market=r.U16(),trade=r.U16(),show=r.U16();name=r.Latin();uint profession=r.U16(),level=r.U16();
                    fields.Add(B(36,Join(S(1,market),S(2,trade),S(3,show),S(6,level),B(7,Encoding.UTF8.GetBytes(name)))));
                    if(profession!=0){fields.Add(B(200,Join(S(1,34),S(2,profession))));notices.Add("Legacy market profession mask retained in custom flag #200; no modern vocation meaning is guessed.");}break;
                case 252:case 253:fields.Add(B(200,S(1,(uint)flag)));notices.Add("Legacy charges/floor-change retained in custom flag #200; these have no direct modern boolean equivalent.");break;
                case 39:if(obd)throw new InvalidDataException("Bones are not part of the OBD v2/v3 property contract.");fields.Add(B(200,Join(S(1,39),B(2,r.Bytes(16).ToArray()))));notices.Add("Legacy bone offsets retained in custom flag #200; they are not guessed to be native wings offsets.");break;
                default:throw new InvalidDataException("Unsupported OBD property #"+flag+".");
            }
        }
        return Join(fields.ToArray());
    }
    private sealed class Reader(byte[] bytes)
    {
        public int Position {get;private set;}
        public bool End=>Position==bytes.Length;
        public ReadOnlySpan<byte> Bytes(int count){if(count<0||count>bytes.Length-Position)throw new InvalidDataException("Truncated OBD data.");var data=bytes.AsSpan(Position,count);Position+=count;return data;}
        public int Byte()=>Bytes(1)[0];public int I8()=>unchecked((sbyte)Byte());
        public ushort U16()=>BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));public short I16()=>BinaryPrimitives.ReadInt16LittleEndian(Bytes(2));
        public uint U32()=>BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));public int I32()=>BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));
        public string Utf(){int size=U16();if(size>32)throw new InvalidDataException("Invalid OBD category length.");return new UTF8Encoding(false,true).GetString(Bytes(size));}
        public string Latin(){int size=U16();if(size>16384)throw new InvalidDataException("OBD market name exceeds limit.");return Encoding.Latin1.GetString(Bytes(size));}
    }
}
