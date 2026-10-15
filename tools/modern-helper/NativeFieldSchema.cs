using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NexaModern;

public sealed record NativeField(int Number,string Label,string Kind="uint",string Options="");

/// <summary>Independent typed controls based on verified wire-format facts. Unknown data is retained.</summary>
public static class NativeFieldSchema
{
    private const string Vocations="-1 Any; 0 None; 1 Knight; 2 Paladin; 3 Sorcerer; 4 Druid; 5 Monk; 10 Promoted";
    public static NativeField[] For(int n)=>n switch {
        1=>[new(1,"Ground speed / waypoints")],10=>[new(1,"Maximum text length")],11=>[new(1,"Maximum write-once text length")],
        21=>[new(1,"Hook direction","uint","1 South; 2 East")],23=>[new(1,"Brightness"),new(2,"Color")],
        26=>[new(1,"Offset X","int"),new(2,"Offset Y","int"),new(3,"Offset A","int"),new(4,"Offset B","int")],
        27=>[new(1,"Elevation")],30=>[new(1,"Automap color")],31=>[new(1,"Lens help ID")],34=>[new(1,"Equipment slot")],
        35=>[new(1,"Default action","uint","0 None; 1 Look; 2 Use; 3 Open; 4 Autowalk highlight")],
        36=>[new(1,"Market category","uint","1 Armors; 2 Amulets; 3 Boots; 4 Containers; 5 Decoration; 6 Food; 7 Helmets/hats; 8 Legs; 9 Others; 10 Potions; 11 Rings; 12 Runes; 13 Shields; 14 Tools; 15 Valuables; 16 Ammunition; 17 Axes; 18 Clubs; 19 Distance weapons; 20 Swords; 21 Wands/rods; 22 Premium scrolls; 23 Tibia coins; 24 Creature products; 25 Quiver; 26 Soul cores; 27 Fist weapons"),new(2,"Trade-as object ID"),new(3,"Show-as object ID"),new(5,"Restricted vocations","list",Vocations),new(6,"Minimum level"),new(7,"Market name","text"),new(8,"Vocation","int",Vocations)],
        40=>[new(1,"NPC name","text"),new(2,"NPC location","text"),new(3,"Sale price"),new(4,"Buy price"),new(5,"Currency object ID"),new(6,"Currency quest display name","text")],
        41=>[new(1,"Former object ID")],44=>[new(1,"Cyclopedia type ID")],48=>[new(1,"Upgrade classification")],
        58=>[new(1,"Gem quality ID"),new(2,"Vocation ID","uint",Vocations)],60=>[new(1,"Imbuement slot count")],61=>[new(1,"Proficiency ID")],
        62=>[new(0,"Restricted vocations","list",Vocations)],63=>[new(0,"Minimum level")],64=>[new(0,"Weapon type","uint","0 No weapon; 1 Sword; 2 Axe; 3 Club; 4 Fist; 5 Bow; 6 Crossbow; 7 Wand/rod; 8 Throw")],
        72=>[new(1,"Transparency level")],100=>[new(1,"North X","int"),new(2,"North Y","int"),new(3,"East X","int"),new(4,"East Y","int"),new(5,"South X","int"),new(6,"South Y","int"),new(7,"West X","int"),new(8,"West Y","int")],_=>[]};
    public static bool Editable(int n)=>For(n).Length>0;
    private static string ScalarText(Field f,string kind)
    {
        if(kind!="int")return Proto.UInt(f).ToString(CultureInfo.InvariantCulture);
        if(f.Wire!=0 || (f.Scalar>uint.MaxValue && f.Scalar<unchecked((ulong)int.MinValue)))throw new InvalidDataException("Expected int32.");
        return unchecked((int)f.Scalar).ToString(CultureInfo.InvariantCulture);
    }
    public static string ReadValue(IEnumerable<Field> fields,NativeField d)
    {
        var rows=fields.Where(f=>f.Number==d.Number).ToArray();if(rows.Length==0)return "";
        if(d.Kind=="list"){
            var numbers=new List<int>();foreach(var f in rows){if(f.Wire==0)numbers.Add(int.Parse(ScalarText(f,"int"),CultureInfo.InvariantCulture));else{var bytes=Proto.Bytes(f);int offset=0;while(offset<bytes.Length)numbers.Add(int.Parse(ScalarText(new Field(0,0,Proto.Varint(bytes.Span,ref offset),default),"int"),CultureInfo.InvariantCulture));}}
            return string.Join(", ",numbers);
        }
        return d.Kind=="text" ? Proto.Text(rows[^1]) : ScalarText(rows[^1],d.Kind);
    }
    public static object[] Instances(int number,Field[] flags)
    {
        var defs=For(number);if(defs.Length==0)return [];
        var records=number is 62 or 63 or 64 ? new[]{flags.Select(f=>f with {Number=0}).ToList()} : flags.Select(f=>Proto.Fields(Proto.Bytes(f))).ToArray();
        if(records.Length==0)records=[[]];
        if(records.Length>256)throw new InvalidDataException("Too many repeated flag records for the editor.");
        return records.Select((rows,index)=>(object)new {index=flags.Length==0 ? -1 : index,fields=defs.Select(d=>new {number=d.Number,label=d.Label,kind=d.Kind,options=d.Options,present=rows.Any(f=>f.Number==d.Number),value=ReadValue(rows,d)}).ToArray(),
            unknown=rows.Where(f=>!defs.Any(d=>d.Number==f.Number)).Select(f=>"Unknown #"+f.Number+": "+Raw(f)).ToArray()}).ToArray();
    }
    public static object[] SafeInstances(int number,Field[] flags)
    {
        try{return Instances(number,flags);}
        catch(Exception e) when(e is InvalidDataException or DecoderFallbackException){return [];}
    }
    public static string Raw(Field f)=>f.Wire==0 ? f.Scalar.ToString(CultureInfo.InvariantCulture) : "wire "+f.Wire+", "+f.Data.Length+" bytes, hex "+Convert.ToHexString(f.Data.Span);
    public static string Describe(int number,IEnumerable<Field> rows)
    {
        var defs=For(number);var values=new List<string>();foreach(var d in defs)if(rows.Any(f=>f.Number==d.Number))values.Add(d.Label+": "+ReadValue(rows,d));
        values.AddRange(rows.Where(f=>!defs.Any(d=>d.Number==f.Number)).Select(f=>"Unknown #"+f.Number+": "+Raw(f)));
        return string.Join("\n",values);
    }
    private static byte[] Encode(NativeField def,JsonElement row)
    {
        if(!row.GetProperty("present").GetBoolean())return [];
        string value=row.GetProperty("value").GetString()??"";int field=def.Number==0 ? 1 : def.Number;
        if(def.Kind=="text"){byte[] utf8=new UTF8Encoding(false,true).GetBytes(value);if(utf8.Length>16384)throw new InvalidDataException(def.Label+" exceeds 16 KiB.");return NativeWorkspace.Blob(field,utf8);}
        if(def.Kind=="list"){
            string[] parts=value.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);if(parts.Length>64)throw new InvalidDataException("Too many vocation entries.");
            return parts.SelectMany(s=>{if(!int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n))throw new InvalidDataException("Use comma-separated numeric vocation IDs.");return NativeWorkspace.Scalar(field,unchecked((ulong)(long)n));}).ToArray();
        }
        if(def.Kind=="int"){if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n))throw new InvalidDataException(def.Label+" must fit int32.");return NativeWorkspace.Scalar(field,unchecked((ulong)(long)n));}
        if(!uint.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out uint u))throw new InvalidDataException(def.Label+" must fit uint32.");
        return NativeWorkspace.Scalar(field,u);
    }
    public static byte[] Edit(Appearance a,int number,bool enabled,JsonElement instances)
    {
        var defs=For(number);if(defs.Length==0)throw new InvalidDataException("No typed editor for this field.");
        var flags=NativeFlags.Fields(a).Where(f=>f.Number==number).ToArray();
        if(!enabled)return [];
        if(instances.ValueKind!=JsonValueKind.Array || instances.GetArrayLength() is <1 or >256 || (number!=40 && instances.GetArrayLength()!=1))throw new InvalidDataException("Invalid flag record count.");
        var result=new List<byte>();var used=new HashSet<int>();
        foreach(var instance in instances.EnumerateArray()){
            var values=instance.GetProperty("fields").EnumerateArray().ToArray();if(values.Length!=defs.Length || values.Select(v=>v.GetProperty("number").GetInt32()).Distinct().Count()!=defs.Length)throw new InvalidDataException("Invalid typed flag fields.");
            int source=instance.GetProperty("index").GetInt32();
            if(source < -1 || source>=flags.Length || (source>=0 && !used.Add(source)))throw new InvalidDataException("Invalid or duplicate source record index.");
            byte[] child=number is 62 or 63 or 64 || source<0 ? [] : Proto.Bytes(flags[source]).ToArray();
            foreach(var def in defs){var row=values.SingleOrDefault(v=>v.GetProperty("number").GetInt32()==def.Number);if(row.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Missing typed flag field.");byte[] encoded=Encode(def,row);
                if(def.Number==0){foreach(var f in Proto.Fields(encoded))result.AddRange(NativeWorkspace.Scalar(number,f.Scalar));}
                else child=NativeWorkspace.Rewrite(child,def.Number,encoded);
            }
            if(number is not (62 or 63 or 64))result.AddRange(NativeWorkspace.Blob(number,child));
        }
        return result.ToArray();
    }
}
