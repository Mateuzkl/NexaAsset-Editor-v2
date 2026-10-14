using System.Buffers.Binary;
using System.Text.Json;

namespace NexaModern;

/// <summary>Native catalog views. No DAT/SPR serialization or legacy profile is used.</summary>
public sealed class NativeAssets(AssetSet assets, SheetCache cache, string output)
{
    private const int PageSize = 100;
    public object Inventory() => new {
        categories = AssetSet.Categories.ToDictionary(c=>c,c=>new {
            count=assets.Appearances.Count(a=>a.Category==c),
            first=assets.Appearances.Where(a=>a.Category==c).Select(a=>a.Id).DefaultIfEmpty().Min(),
            last=assets.Appearances.Where(a=>a.Category==c).Select(a=>a.Id).DefaultIfEmpty().Max() }),
        spriteCount=assets.Sheets.Sum(s=>(long)s.Last-s.First+1), firstSprite=assets.Sheets[0].First, lastSprite=assets.Sheets[^1].Last,
        sheetCount=assets.Sheets.Count,dimensions=assets.Sheets.Select(s=>s.Width+"x"+s.Height).Distinct().ToArray(),
        format="Protobuf appearances + CIP LZMA spritesheets",fingerprint=assets.Fingerprint,
        multipleGroups=assets.Appearances.Count(a=>a.Groups>1)
    };
    private static uint Number(IEnumerable<Field> fields,int number,uint fallback=0) => fields.Where(f=>f.Number==number).Select(Proto.UInt).DefaultIfEmpty(fallback).Last();
    private object Model(Appearance a, bool detailed)
    {
        var groups=AssetSet.Groups(a);
        var shapes=groups.Select((g,index)=> {
            int w=1,h=1;
            foreach(uint id in g.Sprites) { var s=assets.FindSheet(id); w=Math.Max(w,s.Width/32);h=Math.Max(h,s.Height/32); }
            var groupField=Proto.Fields(a.Source).Where(f=>f.Number==2).ElementAt(index);
            var info=Proto.Fields(Proto.Bytes(Proto.Fields(Proto.Bytes(groupField)).Single(f=>f.Number==3)));
            var animations=info.Where(f=>f.Number==6).ToArray(); var animation=animations.Length==0 ? [] : Proto.Fields(Proto.Bytes(animations[0]));
            int loop=unchecked((int)animation.Where(f=>f.Number==4).Select(f=>f.Scalar).DefaultIfEmpty().Last());
            return new { index, originalType=g.Type, type=index,
                width=w,height=h,exactSize=Number(info,7,(uint)Math.Max(w,h)*32),g.Layers,g.PatternX,g.PatternY,g.PatternZ,g.Frames,
                spriteCount=g.Sprites.Length,mode=Number(animation,7,Number(animation,2)),loops=loop==-1 ? -1 : loop==1 ? (int)Number(animation,5) : 0,
                start=Number(animation,3)==1 ? -1 : (int)Number(animation,1),phases=detailed ? g.Phases : [] };
        }).ToArray();
        return new { a.Id,a.Category,a.Name,groups=shapes,properties=detailed ? Properties(a) : null,flags=detailed ? Flags(a) : null,flagRows=detailed ? NativeFlags.Rows(a) : null,metadataRows=detailed ? NativeMetadata.Rows(a) : null };
    }
    public object Detail(string category,uint id) => Model(assets.Find(category,id),true);
    public object ObjectExport(string category,uint id,int version,uint dat,uint spr)
    {
        var export=new LegacyExport(assets,version,true,[assets.Find(category,id)],datSignature:dat,sprSignature:spr);
        var obj=export.Objects.Single();
        if(obj.Groups.Any(g=>g.References.Length>4096))throw new InvalidDataException("OBD groups support at most 4,096 normalized sprite references.");
        return new {file=Write("native-obd.bin",export.ObjectPixels(cache)),summary=export.Summary(),id,category,family=export.Profile.Family,
            properties=Convert.ToHexString(obj.Properties),groups=obj.Groups.Select(g=>new {width=g.Width,height=g.Height,exactSize=g.ExactSize,layers=g.Source.Layers,patternX=g.Source.PatternX,patternY=g.Source.PatternY,patternZ=g.Source.PatternZ,frames=g.Source.Frames,mode=g.Mode,loops=g.Loops,start=g.Start,references=g.References,phases=g.Source.Phases})};
    }
    public object Objects(string category,uint target,string query,bool hideEmpty)
    {
        if(!AssetSet.Categories.Contains(category) || query.Length>256) throw new InvalidDataException("Invalid category/search.");
        var rows=assets.Appearances.Where(a=>a.Category==category && (query.Length==0 || a.Id.ToString().Contains(query,StringComparison.Ordinal) || a.Name.Contains(query,StringComparison.OrdinalIgnoreCase)))
            .Where(a=>!hideEmpty || AssetSet.Groups(a).Any(g=>g.Sprites.Any(id=>id!=0))).ToArray();
        int at=Array.FindIndex(rows,a=>a.Id>=target); if(at<0) at=Math.Max(0,rows.Length-1); int start=at/PageSize*PageSize;
        var page=rows.Skip(start).Take(PageSize).ToArray();
        string file=Write("native-things.bin",page.Select(a=> {
            var g=AssetSet.Groups(a)[0]; return (a.Id,cache.Render(g,0,a.Category=="outfit" ? Math.Min(2,(int)g.PatternX-1) : 0,0,0)); }));
        return new { file,total=rows.Length,selected=page.Length==0 ? 0 : rows[at].Id,
            previous=start==0 ? -1L : rows[Math.Max(0,start-PageSize)].Id,next=start+page.Length>=rows.Length ? -1L : rows[start+page.Length].Id,
            items=page.Select(a=>Model(a,false)) };
    }
    public object Sprites(uint target)
    {
        long total=assets.Sheets.Sum(s=>(long)s.Last-s.First+1), index=0; bool found=false;
        foreach(var s in assets.Sheets) {
            if(target<=s.Last) { index+=target<=s.First ? 0 : target-s.First;found=true;break; }
            index+=(long)s.Last-s.First+1;
        }
        if(!found) index=total-1;
        long start=index/PageSize*PageSize, offset=0;var ids=new List<uint>();
        foreach(var s in assets.Sheets) {
            long length=(long)s.Last-s.First+1;
            if(offset+length>start && ids.Count<PageSize) {
                uint first=s.First+(uint)Math.Max(0,start-offset);
                for(uint id=first;id<=s.Last && ids.Count<PageSize;id++) { ids.Add(id); if(id==uint.MaxValue) break; }
            }
            offset+=length;
        }
        string file=Write("native-sprites.bin",ids.Select(id=>(id,cache.Sprite(id))));
        return new {file,selected=ids[(int)(index-start)],total,ids};
    }
    public object Frame(string category,uint id,int group,int frame,int x,int y,int z,int layer,bool grid)
    {
        var groups=AssetSet.Groups(assets.Find(category,id)); if(group<0 || group>=groups.Length) throw new InvalidDataException("Invalid native group.");
        var g=groups[group]; if(frame<0 || frame>=g.Frames || x<0 || x>=g.PatternX || y<0 || y>=g.PatternY || z<0 || z>=g.PatternZ || layer< -1 || layer>=g.Layers) throw new InvalidDataException("Invalid native frame/pattern/layer.");
        Pixels Render(int px,int py) {
            if(layer<0) return cache.Render(g,frame,px,py,z);
            int i=checked((((frame*(int)g.PatternZ+z)*(int)g.PatternY+py)*(int)g.PatternX+px)*(int)g.Layers+layer);
            return cache.Sprite(g.Sprites[i]);
        }
        var image=Render(x,y);
        if(grid) {
            int cellWidth=32,cellHeight=32;
            foreach(uint sprite in g.Sprites){var sheet=assets.FindSheet(sprite);cellWidth=Math.Max(cellWidth,sheet.Width);cellHeight=Math.Max(cellHeight,sheet.Height);}
            int width=checked(cellWidth*(int)g.PatternX),height=checked(cellHeight*(int)g.PatternY);
            if((long)width*height*4>8*1024*1024) throw new InvalidDataException("Native pattern grid exceeds 8 MiB; use the single-pattern preview.");
            var composed=new byte[width*height*4];
            for(int py=0;py<g.PatternY;py++) for(int px=0;px<g.PatternX;px++) {
                var tile=Render(px,py);
                for(int row=0;row<tile.Height;row++) tile.Rgba.AsSpan(row*tile.Width*4,tile.Width*4).CopyTo(composed.AsSpan(((py*cellHeight+cellHeight-tile.Height+row)*width+px*cellWidth+cellWidth-tile.Width)*4));
            }
            image=new Pixels(width,height,composed);
        }
        return new {file=Write("native-frame.bin",[(id,image)]),image.Width,image.Height,cacheBytes=cache.Bytes};
    }
    private string Write(string name,IEnumerable<(uint Id,Pixels Image)> records)
    {
        string file=Path.Combine(output,name);using var stream=new FileStream(file,FileMode.Create,FileAccess.Write);using var writer=new BinaryWriter(stream);
        writer.Write(0x3142584Eu); writer.Write(0u); uint count=0;
        foreach(var (id,image) in records) {
            long length=(long)image.Width*image.Height*4; if(length>8*1024*1024 || stream.Position+length+16>(name=="native-obd.bin" ? 20 : 16)*1024*1024) throw new InvalidDataException("Native pixel response exceeds its limit.");
            writer.Write(id);writer.Write(image.Width);writer.Write(image.Height);writer.Write((int)length);
            var argb=new byte[(int)length];
            for(int i=0;i<argb.Length;i+=4) {argb[i]=image.Rgba[i+3];argb[i+1]=image.Rgba[i];argb[i+2]=image.Rgba[i+1];argb[i+3]=image.Rgba[i+2];}
            writer.Write(argb);count++;
        }
        stream.Position=4;writer.Write(count);return file;
    }
    private static readonly Dictionary<int,string> BooleanProperties=new() {
        [2]="isGroundBorder",[3]="isOnBottom",[4]="isOnTop",[5]="isContainer",[6]="stackable",[7]="usable",[8]="forceUse",[9]="multiUse",[12]="isFluid",[13]="isUnpassable",[14]="isUnmoveable",[15]="blockMissile",[16]="blockPathfind",[17]="noMoveAnimation",[18]="pickupable",[19]="isFluidContainer",[20]="hangable",[22]="rotatable",[24]="dontHide",[25]="isTranslucent",[28]="isLyingObject",[29]="animateAlways",[32]="isFullGround",[33]="ignoreLook",[37]="wrappable",[38]="unwrappable",[39]="topEffect" };
    private static List<Field> SourceFlags(Appearance a) => Proto.Fields(a.Source).Where(f=>f.Number==3).SelectMany(f=>Proto.Fields(Proto.Bytes(f))).ToList();
    public static Dictionary<string,object> Properties(Appearance a)
    {
        var values=new Dictionary<string,object>();
        foreach(var flag in SourceFlags(a)) {
            if(BooleanProperties.TryGetValue(flag.Number,out string? key)) { values[key]=Proto.UInt(flag)!=0;continue; }
            if(flag.Wire!=2 || flag.Number>36) continue;
            var f=Proto.Fields(Proto.Bytes(flag));uint n=flag.Number==26 ? 0 : Number(f,1);
            switch(flag.Number) {
                case 1: values["isGround"]=true;values["groundSpeed"]=n;break;
                case 10:values["writable"]=true;values["maxReadWriteChars"]=n;break;
                case 11:values["writableOnce"]=true;values["maxReadChars"]=n;break;
                case 21:values["isVertical"]=n==1;values["isHorizontal"]=n==2;break;
                case 23:values["hasLight"]=true;values["lightLevel"]=n;values["lightColor"]=Number(f,2);break;
                case 26:values["hasOffset"]=true;values["offsetX"]=unchecked((int)f.Where(v=>v.Number==1).Select(v=>v.Scalar).DefaultIfEmpty().Last());values["offsetY"]=unchecked((int)f.Where(v=>v.Number==2).Select(v=>v.Scalar).DefaultIfEmpty().Last());break;
                case 27:values["hasElevation"]=true;values["elevation"]=n;break;
                case 30:values["miniMap"]=true;values["miniMapColor"]=n;break;
                case 31:values["isLensHelp"]=true;values["lensHelp"]=n;break;
                case 34:values["cloth"]=true;values["clothSlot"]=n;break;
                case 35:values["hasDefaultAction"]=true;values["defaultAction"]=n;break;
                case 36:values["isMarketItem"]=true;values["marketCategory"]=n;values["marketTradeAs"]=Number(f,2,a.Id);values["marketShowAs"]=Number(f,3,a.Id);values["marketRestrictLevel"]=Number(f,6);values["marketName"]=f.Where(v=>v.Number==7).Select(Proto.Text).LastOrDefault()??a.Name;break;
            }
        }
        return values;
    }
    public static string Flags(Appearance a) => string.Join("\n",SourceFlags(a).Select(f=> {
        string name=BooleanProperties.GetValueOrDefault(f.Number) ?? (f.Number switch {1=>"Ground / speed",10=>"Writable limit",11=>"Write-once limit",21=>"Wall hook",23=>"Light (brightness/color)",26=>"Offset (x/y)",27=>"Elevation",30=>"Automap color",31=>"Lens help",34=>"Equipment slot",35=>"Default action",36=>"Market data",40=>"NPC sale data",44=>"Cyclopedia",48=>"Upgrade classification",58=>"Skill-wheel gem",60=>"Imbuement slots",61=>"Proficiency",62=>"Vocation restriction",63=>"Minimum level",64=>"Weapon type",72=>"Transparency level",_=>"Modern field"});
        // Unknown/packed byte fields are not necessarily protobuf submessages.
        string value=f.Wire==0 ? f.Scalar.ToString() : "wire "+f.Wire;
        if(f.Wire==2) {
            value="bytes["+f.Data.Length+"]";
            if(new[]{1,10,11,21,23,26,27,30,31,34,35,36,40}.Contains(f.Number))
                value=string.Join(", ",Proto.Fields(f.Data).Select(n=>"#"+n.Number+"="+(n.Wire==0 ? unchecked((long)n.Scalar).ToString() : "bytes["+n.Data.Length+"]")));
        }
        return name+" (#"+f.Number+"): "+value;
    }));
}
