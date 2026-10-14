namespace NexaModern;

/// <summary>Named native metadata inspection without flattening or rewriting source fields.</summary>
public static class NativeMetadata
{
    public static object[] Rows(Appearance appearance)
    {
        var result=new List<object>();
        void Add(string label,string value)=>result.Add(new {label,value});
        void Walk(IEnumerable<Field> fields,string path,Dictionary<int,string> names,Dictionary<int,Dictionary<int,string>>? nested=null)
        {
            var counts=new Dictionary<int,int>();
            foreach(var f in fields){
                int index=counts.GetValueOrDefault(f.Number);counts[f.Number]=index+1;
                string label=path+names.GetValueOrDefault(f.Number,"Unknown #"+f.Number)+(index>0 ? " ["+index+"]" : "");
                if(nested!=null && nested.TryGetValue(f.Number,out var children))Walk(Proto.Fields(Proto.Bytes(f)),label+" / ",children);
                else Add(label,NativeFieldSchema.Raw(f));
            }
        }
        var outer=Proto.Fields(appearance.Source);
        Add("Appearance ID",appearance.Id.ToString());Add("Category",appearance.Category);Add("Name",appearance.Name);
        foreach(var f in outer.Where(f=>f.Number is 5 or 6 or 7 || f.Number>7))
            Add(f.Number==5 ? "Description" : f.Number==6 ? "Appearance type (enum)" : f.Number==7 ? "Embedded sprite data" : "Unknown appearance #"+f.Number,f.Number==5 ? TextOrRaw(f) : NativeFieldSchema.Raw(f));
        int group=0;var parsedGroups=AssetSet.Groups(appearance);
        foreach(var source in outer.Where(f=>f.Number==2)){
            string path="Group "+group++ +" / ";
            foreach(var f in Proto.Fields(Proto.Bytes(source))){
                if(f.Number!=3){Add(path+(f.Number==1 ? "Fixed group type" : f.Number==2 ? "Group ID" : "Unknown #"+f.Number),NativeFieldSchema.Raw(f));continue;}
                var info=Proto.Fields(Proto.Bytes(f));
                Walk(info.Where(s=>s.Number is not (5 or 6 or 9)),path,new() {[1]="Pattern width",[2]="Pattern height",[3]="Pattern depth",[4]="Layers",[7]="Bounding square",[8]="Opaque",[10]="Custom pattern size",[11]="Custom pattern layers",[12]="Custom pattern X",[13]="Custom pattern Y",[14]="Custom pattern Z",[15]="Custom pattern frames",[16]="Custom is-animation"});
                var sprites=parsedGroups[group-1].Sprites;
                Add(path+"Sprite IDs ("+sprites.Length+")",string.Join(", ",sprites));
                int box=0;
                foreach(var b in info.Where(s=>s.Number==9))Walk(Proto.Fields(Proto.Bytes(b)),path+"Bounding box "+box++ +" / ",new() {[1]="X",[2]="Y",[3]="Width",[4]="Height"});
                foreach(var a in info.Where(s=>s.Number==6)){
                    var animation=Proto.Fields(Proto.Bytes(a));
                    Walk(animation.Where(s=>s.Number!=6),path+"Animation / ",new() {[1]="Default start phase",[2]="Synchronized",[3]="Random start",[4]="Loop type (enum)",[5]="Loop count",[7]="Animation mode (enum)"});
                    int phase=0;foreach(var p in animation.Where(s=>s.Number==6))Walk(Proto.Fields(Proto.Bytes(p)),path+"Phase "+phase++ +" / ",new() {[1]="Minimum duration (ms)",[2]="Maximum duration (ms)"});
                }
            }
        }
        return result.ToArray();
    }
    private static string TextOrRaw(Field field)
    {
        try{return Proto.Text(field);}
        catch(Exception e) when(e is InvalidDataException or System.Text.DecoderFallbackException){return "Preserved non-UTF-8/unknown text bytes: "+NativeFieldSchema.Raw(field);}
    }
}
