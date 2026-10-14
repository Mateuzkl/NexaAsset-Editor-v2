using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexaModern;

public static class NativeAttributeTests
{
    public static void Run(string root)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        byte[] S(int n,ulong v)=>NativeWorkspace.Scalar(n,v);
        byte[] B(int n,byte[] v)=>NativeWorkspace.Blob(n,v);
        byte[] T(int n,string v)=>B(n,Encoding.UTF8.GetBytes(v));
        byte[] M(params byte[][] rows)=>rows.SelectMany(b=>b).ToArray();
        var assets=new AssetSet(root);var cache=new SheetCache(assets);var work=new NativeWorkspace(assets,cache);
        var original=assets.Find("item",100);
        byte[] flags=M(B(36,M(S(1,15),S(2,100),S(3,101),B(5,[1,2]),S(6,70),T(7,"Market α"),S(8,ulong.MaxValue),B(99,[55]))),
            B(40,M(T(1,"First NPC"),T(2,"First city"),S(3,10),S(4,20),S(5,3031),T(6,"Quest A"),B(77,[1]))),
            B(40,M(T(1,"Second NPC"),T(2,"Second city"),S(3,30),S(4,40),S(5,3035),T(6,"Quest B"),B(77,[2]))),
            B(62,[1,5]),S(63,10),S(64,8),B(100,M(S(1,unchecked((ulong)-8L)),S(2,12),B(33,[99]))));
        assets.SetAppearance(original with {Source=NativeWorkspace.Rewrite(original.Source,3,B(3,flags))});
        JsonObject Row(int n)=>JsonSerializer.SerializeToNode(NativeFlags.Rows(assets.Find("item",100)))!.AsArray().Select(j=>j!.AsObject()).Single(j=>j["number"]!.GetValue<int>()==n);
        void Edit(int n,JsonArray records,bool enabled=true)=>work.EditFlag("item",100,n,enabled,JsonSerializer.SerializeToElement(records));
        JsonObject Field(JsonArray records,int n)=>records[0]!["fields"]!.AsArray().Select(j=>j!.AsObject()).Single(j=>j["number"]!.GetValue<int>()==n);
        var records=Row(36)["instances"]!.DeepClone().AsArray();
        Check(Field(records,5)["value"]!.GetValue<string>()=="1, 2" && Field(records,8)["value"]!.GetValue<string>()=="-1","Packed market vocations / signed enum");
        Field(records,6)["value"]="80";Field(records,7)["value"]="Complete market β";Edit(36,records);
        var market=Proto.Fields(NativeFlags.Fields(assets.Find("item",100)).Single(f=>f.Number==36).Data);
        Check(Proto.UInt(market.Single(f=>f.Number==6))==80 && Proto.Text(market.Single(f=>f.Number==7))=="Complete market β" && market.Single(f=>f.Number==99).Data.Span.SequenceEqual(new byte[]{55}),"Typed market edit lost fields");
        records=Row(40)["instances"]!.DeepClone().AsArray();records.RemoveAt(0);Field(records,1)["value"]="Second NPC edited";
        var added=records[0]!.DeepClone().AsObject();added["index"]=-1;records.Add(added);Edit(40,records);
        var npcs=NativeFlags.Fields(assets.Find("item",100)).Where(f=>f.Number==40).Select(f=>Proto.Fields(f.Data)).ToArray();
        Check(npcs.Length==2 && npcs[0].Single(f=>f.Number==77).Data.Span.SequenceEqual(new byte[]{2}) && npcs[1].All(f=>f.Number!=77),"NPC removal/add preserved the wrong record's unknown fields");
        records=Row(62)["instances"]!.DeepClone().AsArray();Field(records,0)["value"]="1, 5, -1";Edit(62,records);
        Check(Field(Row(62)["instances"]!.AsArray(),0)["value"]!.GetValue<string>()=="1, 5, -1","Repeated scalar vocation edit");
        records=Row(100)["instances"]!.DeepClone().AsArray();Field(records,1)["value"]="-2147483648";Edit(100,records);
        Check(Field(Row(100)["instances"]!.AsArray(),1)["value"]!.GetValue<string>()=="-2147483648","Signed wings offset");
        records=Row(60)["instances"]!.DeepClone().AsArray();Field(records,1)["present"]=true;Field(records,1)["value"]="3";Edit(60,records);
        Check(Row(60)["present"]!.GetValue<bool>(),"Absent structured flag creation");
        byte[] before=assets.Find("item",100).Source.ToArray();long revision=work.Revision;
        Field(records,1)["value"]="4294967296";
        try{Edit(60,records);throw new Exception("Uint overflow was accepted");}catch(InvalidDataException){}
        Check(work.Revision==revision && before.AsSpan().SequenceEqual(assets.Find("item",100).Source.Span),"Invalid typed edit mutated the working copy");
        Check(Proto.Fields(assets.Find("item",100).Source).Single(f=>f.Number==99).Data.Span.SequenceEqual(new byte[]{42}),"Typed edit lost outer unknown data");
        var unknown=original with {Source=NativeWorkspace.Rewrite(original.Source,3,B(3,B(100,[255])))};
        var bad=JsonSerializer.SerializeToElement(NativeFlags.Rows(unknown)).EnumerateArray().Single(j=>j.GetProperty("number").GetInt32()==100);
        Check(!bad.GetProperty("structured").GetBoolean() && bad.GetProperty("value").GetString()!.Contains("FF"),"Malformed future structure must remain safely inspectable");
        Check(NativeMetadata.Rows(original).Length>8,"Native appearance metadata missing");
        Console.WriteLine("PASS: complete typed flag attributes, NPC add/remove record ownership, packed/signed vocations, int32 offsets, invalid-edit atomicity, unknown bytes preserved");
    }
}
