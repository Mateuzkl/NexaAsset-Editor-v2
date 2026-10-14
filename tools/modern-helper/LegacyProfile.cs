using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace NexaModern;

public sealed record LegacyProfile(int Version, uint DatSignature, uint SprSignature, string Name = "")
{
    public bool Durations => Version >= 1050;
    public bool FrameGroups => Version >= 1057;
    public bool PatternDepth => Version > 750;
    public int Family => Version<=730 ? 1 : Version<=750 ? 2 : Version<=772 ? 3 : Version<=854 ? 4 : Version<=986 ? 5 : 6;
    public static IReadOnlyList<LegacyProfile> All { get; } = Load();
    private static IReadOnlyList<LegacyProfile> Load()
    {
        using var stream=typeof(LegacyProfile).Assembly.GetManifestResourceStream("NexaModern.versions.xml") ?? throw new InvalidDataException("Missing version contracts.");
        using var reader=XmlReader.Create(stream,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});
        return XDocument.Load(reader).Root!.Elements("version").Select(v=>new LegacyProfile(
            (int)v.Attribute("value")!,uint.Parse((string)v.Attribute("dat")!,NumberStyles.HexNumber,CultureInfo.InvariantCulture),
            uint.Parse((string)v.Attribute("spr")!,NumberStyles.HexNumber,CultureInfo.InvariantCulture),(string)v.Attribute("string")!)).ToArray();
    }
    public static LegacyProfile For(int version,uint? dat=null,uint? spr=null)
    {
        var profile=All.LastOrDefault(p=>p.Version==version && (!dat.HasValue || p.DatSignature==dat) && (!spr.HasValue || p.SprSignature==spr));
        return profile ?? throw new InvalidDataException("Choose a registered DAT/SPR target and its matching signatures.");
    }
    // Canonical metadata-family-6 numbers are translated to the existing editor's older readers.
    public int? Flag(int canonical)
    {
        if(Family==6)return canonical<=38 || canonical==254 ? canonical : null;
        if(Family==5)return canonical==16 || canonical>=35 ? null : canonical>=17 ? canonical-1 : canonical;
        if(Family==4)return canonical==16 || canonical==24 || canonical>=33 ? null : canonical is >=8 and <=15 ? canonical+1 : canonical;
        if(Family==3)return canonical==16 || canonical is 23 or 24 || canonical>=32 ? null : canonical>=17 ? canonical-1 : canonical;
        int? value=canonical switch {0=>0,2=>1,3=>2,4=>3,5=>4,6=>6,7=>5,8=>7,9=>8,10=>9,11=>10,12=>11,13=>12,14=>13,15=>14,17=>15,21=>23,22=>16,25=>20,26=>19,27=>24,28=>25,29=>22,30=>26,31=>18,_=>null};
        return Family==2 ? canonical switch {18=>25,19=>26,20=>27,28=>28,30=>29,_=>value} : value;
    }
}
