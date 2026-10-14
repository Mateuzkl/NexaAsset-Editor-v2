namespace NexaModern;

/// <summary>Interoperability field names, not a copy of an editor or generated schema.</summary>
public static class NativeFlags
{
    private static readonly string[] Names=["", "Ground speed", "Ground border", "Bottom", "Top", "Container", "Stackable", "Usable", "Force use", "Multi-use", "Writable", "Write once", "Liquid pool", "Unpassable", "Unmoveable", "Block sight", "Avoid pathfinding", "No movement animation", "Pickupable", "Liquid container", "Hangable", "Wall hook", "Rotatable", "Light", "Do not hide", "Translucent", "Offset", "Elevation", "Lying object", "Animate always", "Automap", "Lens help", "Full ground", "Ignore look", "Equipment slot", "Default action", "Market", "Wrappable", "Unwrappable", "Top effect", "NPC sale data", "Changed to expire", "Corpse", "Player corpse", "Cyclopedia item", "Ammo", "Show-off socket", "Reportable", "Upgrade classification", "Reverse addons east", "Reverse addons west", "Reverse addons south", "Reverse addons north", "Wear out", "Clock expiry", "Expiry", "Stop expiry", "Decoration kit", "Skill-wheel gem", "Dual wielding", "Imbuement slots", "Proficiency", "Vocation restrictions", "Minimum level", "Weapon type"];
    public static readonly HashSet<int> Booleans=[2,3,4,5,6,7,8,9,12,13,14,15,16,17,18,19,20,22,24,25,28,29,32,33,37,38,39,42,43,45,46,47,49,50,51,52,53,54,55,56,57,59,70,71];
    public static string Name(int number)=>number<Names.Length ? Names[number] : number switch {70=>"Hook south",71=>"Hook east",72=>"Transparency level",100=>"Wings offset",200=>"Imported legacy-only properties",_=>"Unknown modern field"};
    public static List<Field> Fields(Appearance a)=>Proto.Fields(a.Source).Where(f=>f.Number==3).SelectMany(f=>Proto.Fields(Proto.Bytes(f))).ToList();
    public static string Describe(Field f) {
        if(NativeFieldSchema.Editable(f.Number)){
            try{return NativeFieldSchema.Describe(f.Number,f.Number is 62 or 63 or 64 ? [f with {Number=0}] : Proto.Fields(Proto.Bytes(f)));}catch(Exception e) when(e is InvalidDataException or System.Text.DecoderFallbackException){return "Preserved unrecognized field: "+NativeFieldSchema.Raw(f);}
        }
        if(f.Wire==0)return Booleans.Contains(f.Number) ? (f.Scalar==0 ? "False" : "True") : f.Scalar.ToString();
        return "Preserved unknown data: "+NativeFieldSchema.Raw(f);
    }
    public static object[] Rows(Appearance a) {
        var fields=Fields(a).GroupBy(f=>f.Number).ToDictionary(g=>g.Key,g=>g.ToArray());
        var numbers=Enumerable.Range(1,64).Concat(new[]{70,71,72,100}).Concat(fields.Keys).Distinct().Order().ToArray();
        return numbers.Select(n=>{
            var instances=NativeFieldSchema.SafeInstances(n,fields.GetValueOrDefault(n)??[]);
            return (object)new {number=n,label=Name(n),present=fields.ContainsKey(n),editable=Booleans.Contains(n),structured=instances.Length>0,instances,enabled=fields.TryGetValue(n,out var values) && values.Any(f=>f.Wire==0 && f.Scalar!=0),
                value=fields.TryGetValue(n,out var rows) ? string.Join("\n",rows.Select(Describe)) : "Not present",legacy=n>39 ? "Modern-only: conversion may omit this field." : "Mapped according to the target profile."};}).ToArray();
    }
}
