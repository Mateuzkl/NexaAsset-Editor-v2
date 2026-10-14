using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using NexaModern;
using SharpCompress.Compressors.LZMA;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Reject(Action test, string name)
{
    try { test(); } catch (Exception e) when (e is InvalidDataException or System.Text.DecoderFallbackException or FileNotFoundException or JsonException) { return; }
    throw new Exception("Did not reject: " + name);
}
static byte[] V(ulong value)
{
    var bytes = new List<byte>(); do { byte b = (byte)(value & 127); value >>= 7; bytes.Add((byte)(b | (value > 0 ? 128 : 0))); } while (value > 0); return bytes.ToArray();
}
static byte[] Scalar(int field, ulong value) => V((ulong)field << 3).Concat(V(value)).ToArray();
static byte[] Blob(int field, byte[] bytes) => V(((ulong)field << 3) | 2).Concat(V((ulong)bytes.Length)).Concat(bytes).ToArray();
static byte[] Message(params byte[][] fields) => fields.SelectMany(f => f).ToArray();
static byte[] Appearance(uint id = 100) => Message(Scalar(1, id), Blob(4, System.Text.Encoding.UTF8.GetBytes("Fixture")), Blob(2,
    Message(Scalar(1, 2), Blob(3, Message(Scalar(1, 1), Scalar(2, 1), Scalar(3, 1), Scalar(4, 1), Scalar(5, 1))))), Blob(99, [42]));

if (args.Length == 0) throw new Exception("Specify a test output folder on D:.");
string tests = Path.GetFullPath(args[0]); Directory.CreateDirectory(tests);
NativeSaveTests.Run(tests);
NativeObjectImportTests.Run(tests);
string root = Path.Combine(tests, "synthetic-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
void Fixture(object[] catalog, byte[]? appearances = null)
{
    File.WriteAllText(Path.Combine(root, "catalog-content.json"), JsonSerializer.Serialize(catalog));
    File.WriteAllBytes(Path.Combine(root, "appearances.dat"), appearances ?? Blob(1, Appearance()));
    File.WriteAllBytes(Path.Combine(root, "sheet.lzma"), []);
}
object appearanceEntry = new { type = "appearances", file = "appearances.dat" };
object sheetEntry = new { type = "sprite", file = "sheet.lzma", spritetype = 0, firstspriteid = 0, lastspriteid = 143 };
Fixture([appearanceEntry, sheetEntry]);
var synthetic = new AssetSet(root);
Check(synthetic.Find("item", 100).Name == "Fixture" && AssetSet.Groups(synthetic.Find("item", 100))[0].Sprites[0] == 1, "Wire model");
Check(Proto.Fields(synthetic.Find("item", 100).Source).Single(f => f.Number == 99).Data.Span.SequenceEqual(new byte[] { 42 }), "unknown field retained");
var random = new Random(617);
for (int i = 0; i < 10000; i++)
{
    byte[] fuzz = new byte[random.Next(0, 128)]; random.NextBytes(fuzz);
    try { Proto.Fields(fuzz); } catch (InvalidDataException) { }
}
Reject(() => Proto.Fields(new byte[] { 0x0A, 0x03, 0x01 }), "truncated length");
Reject(() => Proto.Fields(new byte[] { 0x08, 0x80 }), "truncated varint");
Reject(() => Proto.Fields(new byte[] { 0x08, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 }), "varint overflow");
Reject(() => Proto.Fields(new byte[] { 0 }), "zero field");
Reject(() => Proto.Fields(new byte[] { 0x0B }), "group wire type");
Fixture([appearanceEntry, sheetEntry], Message(Blob(1, Appearance()), Blob(1, Appearance())));
Reject(() => new AssetSet(root), "duplicate IDs");
Fixture([sheetEntry]); Reject(() => new AssetSet(root), "no explicit appearances entry");
Fixture([appearanceEntry, sheetEntry, sheetEntry]); Reject(() => new AssetSet(root), "overlapping ranges");
Fixture([appearanceEntry, new { type = "sprite", file = "../escape.lzma", spritetype = 0, firstspriteid = 0, lastspriteid = 143 }]);
Reject(() => new AssetSet(root), "path traversal");
Fixture([appearanceEntry, new { type = "sprite", file = "sheet.lzma", spritetype = 3, firstspriteid = 0, lastspriteid = 36 }]);
Reject(() => new AssetSet(root), "capacity");
Fixture([appearanceEntry, sheetEntry]);
var badGroups = new Appearance(100, "item", "", 1, Blob(2, Blob(3, Message(Scalar(1, 0), Scalar(5, 1)))));
Reject(() => AssetSet.Groups(badGroups), "explicit zero dimension");
badGroups = new Appearance(100, "item", "", 1, Blob(2, Blob(3, Message(Scalar(1, 2), Scalar(5, 1)))));
Reject(() => AssetSet.Groups(badGroups), "sprite count mismatch");
Reject(() => SheetCache.DecodeSheet(Path.Combine(root, "sheet.lzma")), "truncated LZMA");
Reject(() => SheetCache.DecodeBmp(new byte[54]), "bad BMP");
// A valid LZMA stream expanding beyond the fixed cap must fail, not allocate
// according to its forged length header or quietly return a partial bitmap.
using (var compressed = new MemoryStream())
{
    byte[] props;
    using (var encoder = new LzmaStream(new LzmaEncoderProperties(true, 1 << 20), false, compressed))
    { props = encoder.Properties.ToArray(); encoder.Write(new byte[1024 * 1024 + 1]); }
    var payload = props.Concat(new byte[8]).Concat(compressed.ToArray()).ToArray();
    var header = new byte[] { 0x70, 0x0A, 0xFA, 0x80, 0x24 }.Concat(V((ulong)payload.Length)).ToArray();
    var cip = new byte[32 - header.Length].Concat(header).Concat(payload).ToArray();
    string bomb = Path.Combine(root, "bomb.lzma"); File.WriteAllBytes(bomb, cip);
    Reject(() => SheetCache.DecodeSheet(bomb), "decompression bomb");
    BinaryPrimitives.WriteUInt32LittleEndian(cip.AsSpan(33, 4), uint.MaxValue); File.WriteAllBytes(bomb, cip);
    Reject(() => SheetCache.DecodeSheet(bomb), "dictionary bomb");
}
var bmp = new byte[122 + 384 * 384 * 4]; bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
void I(int p, int v) => BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(p), v);
I(10, 122); I(14, 108); I(18, 384); I(22, 384); bmp[26] = 1; bmp[28] = 32; I(30, 3);
I(54, 0xFF0000); I(58, 0xFF00); I(62, 0xFF); I(66, unchecked((int)0xFF000000));
// Different top/bottom pixels catch vertical flips, not merely image dimensions.
int top = 122 + 383 * 384 * 4; bmp[top] = 30; bmp[top + 1] = 20; bmp[top + 2] = 10; bmp[top + 3] = 128;
bmp[122] = 255; bmp[124] = 255; bmp[125] = 255;
var decoded = SheetCache.DecodeBmp(bmp);
Check(decoded.Rgba.AsSpan(0, 4).SequenceEqual(new byte[] { 10, 20, 30, 128 }), "RGBA/orientation");
Check(decoded.Rgba[(383 * 384) * 4 + 3] == 0, "magenta transparency");
foreach (var (width, height) in new[] { (32, 32), (32, 64), (64, 32), (64, 64) })
    Check(decoded.Crop(0, 0, width, height).Rgba.Length == width * height * 4, "sprite crop type");
Png.Write(Path.Combine(root, "fixture.png"), decoded.Crop(0, 0, 32, 32));
Console.WriteLine("PASS: 10,000 wire fuzz inputs, unknown fields, catalog/ID/range/path checks, LZMA bombs, BMP RGBA/orientation and four tile types");
{
    var source=new Pixels(64,32,new byte[64*32*4]);random.NextBytes(source.Rgba);string file=Path.Combine(tests,"png-straight-rgba.png");
    Png.Write(file,source);Check(NativePng.Read(file).Rgba.SequenceEqual(source.Rgba),"PNG import must preserve exact straight RGBA, including transparent RGB");
    using(var png=new MemoryStream()){
        png.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string name,byte[] payload){byte[] kind=System.Text.Encoding.ASCII.GetBytes(name),word=new byte[4];BinaryPrimitives.WriteInt32BigEndian(word,payload.Length);png.Write(word);png.Write(kind);png.Write(payload);uint crc=uint.MaxValue;foreach(byte b in kind.Concat(payload)){crc^=b;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)==0 ? 0u : 0xEDB88320u);}BinaryPrimitives.WriteUInt32BigEndian(word,~crc);png.Write(word);}
        byte[] header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,64);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),32);header[8]=8;header[9]=6;Chunk("IHDR",header);
        using var compressed=new MemoryStream();using(var zlib=new System.IO.Compression.ZLibStream(compressed,System.IO.Compression.CompressionLevel.Fastest,true)){
            for(int y=0;y<32;y++){int mode=y%5;zlib.WriteByte((byte)mode);for(int x=0;x<256;x++){int a=x<4 ? 0 : source.Rgba[y*256+x-4],b=y==0 ? 0 : source.Rgba[(y-1)*256+x],c=y==0||x<4 ? 0 : source.Rgba[(y-1)*256+x-4],p=a+b-c;int pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);int predicted=mode switch {0=>0,1=>a,2=>b,3=>(a+b)/2,_=>pa<=pb&&pa<=pc ? a : pb<=pc ? b : c};zlib.WriteByte(unchecked((byte)(source.Rgba[y*256+x]-predicted)));}}
        }
        Chunk("IDAT",compressed.ToArray());Chunk("IEND",[]);string filtered=Path.Combine(tests,"png-all-filters.png");File.WriteAllBytes(filtered,png.ToArray());Check(NativePng.Read(filtered).Rgba.SequenceEqual(source.Rgba),"PNG filters 0–4");
    }
    byte[] broken=File.ReadAllBytes(file);broken[29]^=1;string malformed=Path.Combine(tests,"png-bad-crc.png");File.WriteAllBytes(malformed,broken);Reject(()=>NativePng.Read(malformed),"PNG CRC validation");
    Png.Write(Path.Combine(tests,"png-oversized.png"),new Pixels(128,32,new byte[128*32*4]));Reject(()=>NativePng.Read(Path.Combine(tests,"png-oversized.png")),"PNG geometry limit before decompression");
    Console.WriteLine("PASS: bounded PNG geometry/CRC and exact straight RGBA round trip (no AIR premultiplication)");
}

if (args.Contains("--convert"))
{
    // A controlled four-quadrant sheet catches flipped/chopped geometry and
    // verifies partial alpha independently of any real client's palette.
    Array.Clear(bmp, 122, bmp.Length - 122);
    byte[][] colors = [[10,20,30,64], [40,50,60,128], [70,80,90,192], [100,110,120,255]];
    for (int y = 0; y < 64; y++)
    for (int x = 0; x < 64; x++)
    {
        byte[] c = colors[y / 32 * 2 + x / 32]; int p = 122 + ((383 - y) * 384 + x) * 4;
        bmp[p] = c[2]; bmp[p+1] = c[1]; bmp[p+2] = c[0]; bmp[p+3] = c[3];
    }
    byte[] Group(int type)
    {
        byte[] animation = Message(Scalar(1,0), Scalar(2,1), Scalar(3,0), Scalar(4,0),
            Blob(6, Message(Scalar(1,0), Scalar(2,0))), Blob(6, Message(Scalar(1,100), Scalar(2,100))));
        byte[] info = Message(Scalar(1,1), Scalar(2,1), Scalar(3,1), Scalar(4,1), Scalar(5,1), Scalar(5,1), Blob(6,animation));
        return Message(Scalar(1,(uint)type), Blob(3,info));
    }
    byte[] Make(uint id, byte[] flags, params int[] groupTypes) => Message(Scalar(1,id), Blob(4,System.Text.Encoding.UTF8.GetBytes("Fixture")), Blob(3,flags),
        Message(groupTypes.Select(t => Blob(2, Group(t))).ToArray()), Blob(99,[42]));
    byte[] signedOffset = Message(Scalar(1,unchecked((ulong)-5L)), Scalar(2,7));
    Fixture([appearanceEntry, new { type="sprite", file="sheet.lzma", spritetype=3, firstspriteid=1, lastspriteid=36 }],
        Message(Blob(1,Make(100,Message(Blob(1,Scalar(1,150)), Scalar(13,1), Blob(26,signedOffset), Blob(62,[8,1]), Blob(62,[8,2])),2)),
            Blob(2,Make(1,[],0,1)), Blob(3,Make(1,Scalar(39,1),3)), Blob(4,Make(1,[],4))));
    using (var compressed = new MemoryStream())
    {
        byte[] props;
        using (var encoder = new LzmaStream(new LzmaEncoderProperties(true,1<<20),false,compressed))
        { props = encoder.Properties.ToArray(); encoder.Write(bmp); }
        byte[] payload = props.Concat(new byte[8]).Concat(compressed.ToArray()).ToArray();
        byte[] header = new byte[] {0x70,0x0A,0xFA,0x80,0x24}.Concat(V((ulong)payload.Length)).ToArray();
        File.WriteAllBytes(Path.Combine(root,"sheet.lzma"), new byte[32-header.Length].Concat(header).Concat(payload).ToArray());
    }
    var controlled = new AssetSet(root); var controlledCache = new SheetCache(controlled);
    NativeAttributeTests.Run(root);
    var native = new NativeAssets(controlled,controlledCache,tests);
    var nativeDetail = JsonSerializer.SerializeToElement(native.Detail("item",100),new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
    Check(nativeDetail.GetProperty("groups")[0].GetProperty("width").GetInt32()==2 && nativeDetail.GetProperty("groups")[0].GetProperty("phases")[0].GetProperty("min").GetInt32()==0,"Native 64px geometry and zero timing");
    Check(nativeDetail.GetProperty("flags").GetString()!.Contains("Vocation restriction"),"Packed flags must not be parsed as arbitrary submessages");
    native.Objects("item",100,"",false);native.Sprites(1);native.Frame("item",100,0,0,0,0,0,-1,false);
    using(var packet = new BinaryReader(File.OpenRead(Path.Combine(tests,"native-frame.bin")))) {
        Check(packet.ReadUInt32()==0x3142584E && packet.ReadUInt32()==1 && packet.ReadUInt32()==100,"Native packet identity");
        Check(packet.ReadInt32()==64 && packet.ReadInt32()==64 && packet.ReadInt32()==16384,"Native sprite must not be truncated to 32px");
        Check(packet.ReadBytes(4).SequenceEqual(new byte[]{64,10,20,30}),"Native ARGB ordering/partial alpha");
    }
    native.Frame("item",100,0,1,0,0,0,0,true);
    Reject(()=>native.Frame("item",100,0,2,0,0,0,-1,false),"Native out-of-range frame");
    Reject(()=>native.Frame("item",100,0,0,1,0,0,-1,false),"Native out-of-range pattern");
    Reject(()=>native.Objects("invalid",100,"",false),"Native invalid category");
    var packed = new Appearance(100,"item","",1,Blob(3,Message(Blob(62,[1,2,3]),Blob(100,[255]))));
    Check(NativeAssets.Flags(packed).Contains("Modern field (#100)"),"Unknown native flag bytes retained safely");
    Console.WriteLine("PASS: native pages, real 64px sprites, partial alpha/ARGB, zero timing, packed/unknown flags, frame bounds");
    {
        // A separate in-memory workspace must never change the on-disk fixture.
        var edited=new AssetSet(root);var editCache=new SheetCache(edited);var work=new NativeWorkspace(edited,editCache);
        uint ResultId(object result)=>JsonSerializer.SerializeToElement(result).GetProperty("selected").GetUInt32();
        uint duplicate=ResultId(work.Duplicate("item",[100]));Check(duplicate==101,"Native duplicate ID");
        Check(Proto.Fields(edited.Find("item",duplicate).Source).Single(f=>f.Number==99).Data.Span.SequenceEqual(new byte[]{42}),"Duplicate lost unknown field");
        work.Flag("item",duplicate,13,false);Check(!(bool)NativeAssets.Properties(edited.Find("item",duplicate))["isUnpassable"],"Native boolean edit");
        Check(NativeFlags.Rows(edited.Find("item",duplicate)).Length>=68,"Modern flag options");
        Reject(()=>work.Flag("item",duplicate,60,true),"Structured flag cannot be edited as boolean");
        work.Copy("item",duplicate);work.Paste("item",[100],true);Reject(()=>work.Paste("item",[100],false),"Delete-after-paste clipboard");
        Check(!(bool)NativeAssets.Properties(edited.Find("item",100))["isUnpassable"],"Native paste lost edits");
        var originalPixels=editCache.Sprite(1);byte[] argb=new byte[originalPixels.Rgba.Length];
        for(int i=0;i<argb.Length;i+=4){argb[i]=128;argb[i+1]=11;argb[i+2]=22;argb[i+3]=33;}
        work.Sprite(1,64,64,Convert.ToBase64String(argb));Check(editCache.Sprite(1).Rgba.AsSpan(0,4).SequenceEqual(new byte[]{11,22,33,128}),"Native PNG replacement/alpha");
        string package=Path.Combine(tests,"fixture.nexaobject");work.ExportObject("item",duplicate,package);
        uint imported=ResultId(work.ImportObject("item",package));var importedAppearance=edited.Find("item",imported);
        Check(AssetSet.Groups(importedAppearance)[0].Sprites.All(id=>id!=1),"Native package dependencies must be remapped");
        Check(Proto.Fields(importedAppearance.Source).Single(f=>f.Number==99).Data.Span.SequenceEqual(new byte[]{42}),"Package lost unknown metadata");
        Check(editCache.Sprite(AssetSet.Groups(importedAppearance)[0].Sprites[0]).Rgba.AsSpan(0,4).SequenceEqual(new byte[]{11,22,33,128}),"Package pixel round trip");
        Reject(()=>work.ImportObject("outfit",package),"Package category mismatch");
        Reject(()=>work.ExportObject("item",duplicate,package),"Native export cannot overwrite");
        Reject(()=>work.ExportPng("item",duplicate,Path.Combine(root,"source-write.png"),false),"Native export must not modify source");
        work.ExportPng("item",duplicate,Path.Combine(tests,"working-copy.png"),false);
        Check(NativePng.Read(Path.Combine(tests,"working-copy.png")).Rgba.SequenceEqual(editCache.Render(AssetSet.Groups(edited.Find("item",duplicate))[0],0,0,0,0).Rgba),"Exported native PNG pixels");
        string batch=Path.Combine(tests,"native-batch");work.ExportBatch("item",[100,duplicate],batch,false,true);Check(Directory.GetFiles(batch).Length==2,"Native selected export batch");
        var bounded=new AssetSet(root);var boundedCache=new SheetCache(bounded);var boundedWork=new NativeWorkspace(bounded,boundedCache);
        uint boundedCopy=ResultId(boundedWork.Duplicate("item",[100]));
        var budgetField=typeof(AssetSet).GetField("changedAppearanceBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        budgetField.SetValue(bounded,32L*1024*1024-bounded.Find("item",100).Source.Length);
        int boundedCount=bounded.Appearances.Count;long boundedRevision=boundedWork.Revision;
        Reject(()=>boundedWork.Duplicate("item",[100,boundedCopy]),"Whole duplicate batch metadata budget");
        Check(bounded.Appearances.Count==boundedCount && boundedWork.Revision==boundedRevision,"Rejected duplicate partially changed the workspace");
        budgetField.SetValue(bounded,0L);uint blank=ResultId(boundedWork.Add("item"));boundedWork.Copy("item",100);
        byte[] beforePaste=bounded.Find("item",blank).Source.ToArray();boundedRevision=boundedWork.Revision;
        budgetField.SetValue(bounded,32L*1024*1024-bounded.Find("item",100).Source.Length);
        Reject(()=>boundedWork.Paste("item",[100,blank],true),"Whole paste batch metadata budget");
        Check(bounded.Find("item",blank).Source.Span.SequenceEqual(beforePaste) && boundedWork.Revision==boundedRevision,"Rejected paste partially changed the workspace");
        budgetField.SetValue(bounded,32L*1024*1024);long pixelBytes=boundedCache.ChangedBytes;uint lastSprite=bounded.Sheets[^1].Last;
        Reject(()=>boundedWork.Add("item"),"New object metadata capacity before sprite allocation");
        Reject(()=>boundedWork.ImportObject("item",package),"Package metadata capacity before sprite allocation");
        Check(bounded.Sheets[^1].Last==lastSprite && boundedCache.ChangedBytes==pixelBytes && boundedWork.Revision==boundedRevision,"Rejected object allocated orphan sprite pixels");
        Console.WriteLine("PASS: whole-batch duplicate/paste metadata rejection and no orphan pixels on failed object/package import");
        var editedPlan=new LegacyExport(edited,1098,true,[importedAppearance]);string editedOutput=Path.Combine(tests,"legacy-working-copy");
        editedPlan.Export(editedOutput,editCache,true);WriteOracle(editedPlan,editedOutput,edited,editCache);
        using(var metadata=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(editedOutput,"working-copy-appearances.json"))))
            Check(Convert.FromBase64String(metadata.RootElement.GetProperty("objects")[0].GetProperty("source").GetString()!).AsSpan().SequenceEqual(importedAppearance.Source.Span),"Current edited metadata sidecar lost unsupported fields");
        Check(new AssetSet(root).Fingerprint==controlled.Fingerprint && (bool)NativeAssets.Properties(new AssetSet(root).Find("item",100))["isUnpassable"],"Native session modified source");
        work.Remove("item",[duplicate,imported]);Check(edited.Appearances.Count==controlled.Appearances.Count,"Native remove accounting");
        Console.WriteLine("PASS: native flags, duplicate/copy/paste/remove, RGBA replacement, portable packages/unknown fields, edited legacy export, source preservation");
    }
    Reject(() => new LegacyExport(controlled,1234,true),"unsupported legacy profile");
    Check(LegacyProfile.All.Count>80 && LegacyProfile.All.Any(p=>p.Version==710) && LegacyProfile.All.Any(p=>p.Version==1310),"Registered legacy targets missing");
    var versionAssets=new AssetSet(root);
    var flagSource=versionAssets.Find("item",100);
    byte[] allFlags=Message(NativeFlags.Booleans.Where(n=>n<=39 && n is not (2 or 3 or 4 or 39)).Select(n=>Scalar(n,1)).Concat(new[] {
        Blob(1,Scalar(1,150)),Blob(10,Scalar(1,120)),Blob(11,Scalar(1,60)),Blob(21,Scalar(1,1)),Blob(23,Message(Scalar(1,5),Scalar(2,215))),
        Blob(26,signedOffset),Blob(27,Scalar(1,8)),Blob(30,Scalar(1,99)),Blob(31,Scalar(1,14)),Blob(34,Scalar(1,2)),Blob(35,Scalar(1,1)),
        Blob(36,Message(Scalar(1,15),Scalar(2,100),Scalar(3,100),Blob(7,System.Text.Encoding.UTF8.GetBytes("Market fixture")),Scalar(6,80)))
    }).ToArray());
    versionAssets.SetAppearance(flagSource with {Source=NativeWorkspace.Rewrite(flagSource.Source,3,Blob(3,allFlags))});
    var versionCache=new SheetCache(versionAssets);
    var canonicalByProperty=new Dictionary<string,int> { ["isGroundBorder"]=1,["isOnBottom"]=2,["isOnTop"]=3,["isContainer"]=4,["stackable"]=5,["forceUse"]=6,["multiUse"]=7,["isFluidContainer"]=10,["isFluid"]=11,["isUnpassable"]=12,["isUnmoveable"]=13,["blockMissile"]=14,["blockPathfind"]=15,["noMoveAnimation"]=16,["pickupable"]=17,["hangable"]=18,["rotatable"]=21,["dontHide"]=23,["isTranslucent"]=24,["isLyingObject"]=27,["animateAlways"]=28,["isFullGround"]=31,["ignoreLook"]=32,["wrappable"]=36,["unwrappable"]=37,["topEffect"]=38,["usable"]=254 };
    foreach(var profile in LegacyProfile.All){
        var plan=new LegacyExport(versionAssets,profile.Version,true,datSignature:profile.DatSignature,sprSignature:profile.SprSignature);
        string target=Path.Combine(tests,$"legacy-contract-{profile.Version}-{profile.DatSignature:X}");
        plan.Export(target,versionCache,true);WriteOracle(plan,target,versionAssets,versionCache);
        var expected=canonicalByProperty.ToDictionary(p=>p.Key,p=>(object)(p.Key is not ("isGroundBorder" or "isOnBottom" or "isOnTop" or "topEffect") && profile.Flag(p.Value).HasValue));
        expected["isGround"]=true;expected["groundSpeed"]=150;expected["hasOffset"]=true;
        expected["offsetX"]=profile.PatternDepth ? -5 : 8;expected["offsetY"]=profile.PatternDepth ? 7 : 8;
        foreach(var pair in new[]{("writable",8),("writableOnce",9),("isVertical",19),("hasLight",22),("hasElevation",26),("miniMap",29),("isLensHelp",30),("cloth",33),("hasDefaultAction",35),("isMarketItem",34)})expected[pair.Item1]=profile.Flag(pair.Item2).HasValue;
        expected["maxReadWriteChars"]=120;expected["maxReadChars"]=60;expected["lightLevel"]=5;expected["lightColor"]=215;expected["elevation"]=8;expected["miniMapColor"]=99;expected["lensHelp"]=14;
        if(profile.Flag(33).HasValue)expected["clothSlot"]=2;
        if(profile.Flag(35).HasValue)expected["defaultAction"]=1;
        if(profile.Flag(34).HasValue){expected["marketName"]="Market fixture";expected["marketCategory"]=15;expected["marketTradeAs"]=100;expected["marketShowAs"]=100;expected["marketRestrictLevel"]=80;}
        File.WriteAllText(Path.Combine(target,"all-properties-oracle.json"),JsonSerializer.Serialize(expected));
    }
    Console.WriteLine($"PASS: all {LegacyProfile.All.Count} registered version/signature contracts exported with mapped boolean/scalar attributes");
    Reject(() => new LegacyExport(controlled,1098,true,[controlled.Find("item",100),controlled.Find("item",100)]),"duplicate conversion selection");
    foreach (var (version, rgba) in new[] {(860,false),(960,true),(1098,true),(1098,false)})
    {
        var plan = new LegacyExport(controlled,version,rgba);
        Check(plan.Objects.Single(o=>o.Source.Category=="item").Groups[0].References.SequenceEqual(new uint[] {1,2,3,4,1,2,3,4}),"quadrant reference order");
        Check(plan.Objects.Single(o=>o.Source.Category=="outfit").Groups.Length == (version==1098 ? 2 : 1),"profile idle-group handling");
        Check(plan.Objects.Single(o=>o.Source.Category=="item").Groups[0].Source.Phases[0].Min == 0,"explicit zero duration retained");
        string target = Path.Combine(tests,$"legacy-synthetic-{version}-{(rgba ? "rgba" : "rgb")}");
        Reject(() => plan.Export(Path.Combine(root,"forbidden-output"),controlledCache,true),"output inside source");
        plan.Export(target,controlledCache,true); WriteOracle(plan,target,controlled,controlledCache);
        File.WriteAllText(Path.Combine(target,"properties-oracle.json"), JsonSerializer.Serialize(new {
            groundSpeed=150, unpassable=true, offsetX=-5, offsetY=7, topEffect=version==1098
        }));
    }
    foreach (string category in AssetSet.Categories)
    {
        var plan = new LegacyExport(controlled,1098,true,[controlled.Appearances.Single(a=>a.Category==category)]);
        string target = Path.Combine(tests,"legacy-selection-"+category);
        plan.Export(target,controlledCache,true); WriteOracle(plan,target,controlled,controlledCache);
        byte[] header = File.ReadAllBytes(Path.Combine(target,"Tibia.dat"));
        Check(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)) >= 100 &&
            Enumerable.Range(0,3).All(i=>BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6+i*2)) >= 1),
            "Selected export must retain standard editor minimum categories");
    }
    // A failed sprite decode must clean only the freshly owned staging files.
    File.WriteAllBytes(Path.Combine(root,"sheet.lzma"),[]);
    string failed = Path.Combine(tests,"failed-conversion");
    Reject(() => new LegacyExport(controlled,1098,true).Export(failed,new SheetCache(controlled),true),"bad sprite prevents publication");
    Check(!Directory.Exists(failed) && !Directory.Exists(failed+".partial"),"failed export staging cleanup");
    Console.WriteLine("PASS: synthetic RGB/RGBA quadrant order, signed offsets, zero durations, profile losses, unknown sidecars and failed-export cleanup");
}

if (args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal))
{
    var watch = System.Diagnostics.Stopwatch.StartNew(); var assets = new AssetSet(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new { assets.Fingerprint, sheets = assets.Sheets.Count, appearances = assets.Appearances.Count,
        counts = AssetSet.Categories.ToDictionary(c => c, c => assets.Appearances.Count(a => a.Category == c)), loadMs = watch.ElapsedMilliseconds }));
    var cache = new SheetCache(assets); int valid = 0, invalid = 0;
    var nativeReal = new NativeAssets(assets,cache,tests);
    var failures = new List<object>(); var chosen = new Dictionary<string, Appearance>();
    foreach (var a in assets.Appearances)
    {
        try
        {
            foreach (var g in AssetSet.Groups(a)) foreach (uint id in g.Sprites) assets.FindSheet(id);
            nativeReal.Detail(a.Category,a.Id);
            valid++; chosen.TryAdd(a.Category, a);
        }
        catch (Exception e) when (e is InvalidDataException or OverflowException)
        { invalid++; if (failures.Count < 100) failures.Add(new { a.Category, a.Id, error = e.Message }); }
    }
    File.WriteAllText(Path.Combine(tests, "modern-validation.json"), JsonSerializer.Serialize(new { valid, invalid, failures }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Model validation: {valid} valid; {invalid} rejected (see modern-validation.json).");
    Check(invalid==0,"Native metadata rejected real fixture entries");
    foreach(var c in AssetSet.Categories) {
        var multi=assets.Appearances.FirstOrDefault(a=>a.Category==c && a.Groups>1);
        var animated=assets.Appearances.FirstOrDefault(a=>a.Category==c && AssetSet.Groups(a).Any(g=>g.Frames>1));
        Console.WriteLine($"Native test examples: {c} multiple={multi?.Id} animated={animated?.Id}");
        nativeReal.Objects(c,0,"",false);
    }
    foreach(var sheet in assets.Sheets.GroupBy(s=>s.Type).Select(g=>g.First()))nativeReal.Sprites(sheet.First);
    foreach (var c in AssetSet.Categories)
    {
        var a = chosen[c]; var group = AssetSet.Groups(a)[0];
        var image = cache.Render(group, 0, 0, 0, 0); Png.Write(Path.Combine(tests, c + "-" + a.Id + ".png"), image);
        Console.WriteLine($"Preview: {c}/{a.Id}, {image.Width}x{image.Height}, RGBA SHA256={Convert.ToHexString(SHA256.HashData(image.Rgba))}");
    }
    foreach (int type in Enumerable.Range(0, 4))
    {
        var sheet = assets.Sheets.First(s => s.Type == type); var tile = cache.Sprite(sheet.First);
        Check(tile.Width == sheet.Width && tile.Height == sheet.Height, "real type dimensions");
        Png.Write(Path.Combine(tests, "type-" + type + ".png"), tile);
    }
    int count = 0;
    foreach (var sheet in args.Contains("--quick") ? assets.Sheets.Take(0) : assets.Sheets)
    {
        cache.Sheet(sheet); Check(cache.Bytes <= SheetCache.Limit, "cache bound");
        if (++count % 500 == 0) Console.WriteLine($"Decoded {count}/{assets.Sheets.Count} sheets; cache={cache.Bytes}");
    }
    Console.WriteLine($"PASS: {(args.Contains("--quick") ? "sample" : "all " + count)} real sheet decoding; {watch.Elapsed.TotalSeconds:F2}s including indexing/validation/previews, hits={cache.Hits}, misses={cache.Misses}, cache={cache.Bytes}");
    var fields = assets.Appearances.SelectMany(a => Proto.Fields(a.Source).Where(f => f.Number == 3).SelectMany(f => Proto.Fields(Proto.Bytes(f))));
    var groups = assets.Appearances.SelectMany(AssetSet.Groups).ToArray();
    File.WriteAllText(Path.Combine(tests, "modern-audit.json"), JsonSerializer.Serialize(new {
        assets.Fingerprint, appearanceCount = assets.Appearances.Count, sheets = assets.Sheets.Count,
        counts = AssetSet.Categories.ToDictionary(c => c, c => assets.Appearances.Count(a => a.Category == c)),
        maxIds = AssetSet.Categories.ToDictionary(c => c, c => assets.Appearances.Where(a => a.Category == c).Max(a => a.Id)),
        sheetTypes = assets.Sheets.GroupBy(s => s.Type).ToDictionary(g => g.Key, g => g.Count()),
        flagFields = fields.GroupBy(f => f.Number).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
        multipleGroupAppearances = assets.Appearances.Count(a => a.Groups > 1), animatedGroups = groups.Count(g => g.Frames > 1),
        zeroDurationPhases = groups.SelectMany(g => g.Phases).Count(p => p.Min == 0 || p.Max == 0),
        absentDurationPhases = groups.SelectMany(g => g.Phases).Count(p => p.Min == null || p.Max == null),
        unknownInfoFields = groups.SelectMany(g => g.UnknownInfoFields).Distinct().Order().ToArray()
    }, new JsonSerializerOptions { WriteIndented = true }));
    // Reloading the read-only inputs must yield the same catalog/protobuf hash.
    Check(new AssetSet(args[1]).Fingerprint == assets.Fingerprint, "source unchanged");
    if (args.Contains("--convert"))
    {
        var ringPlan=new LegacyExport(assets,860,true,[assets.Find("item",54266)]);
        string ringTarget=Path.Combine(tests,"legacy-ring-860");ringPlan.Export(ringTarget,cache,true);WriteOracle(ringPlan,ringTarget,assets,cache);
        Check(ringPlan.SpriteCount>0 && JsonSerializer.Serialize(ringPlan.Summary()).Contains("SELECTED OBJECTS ONLY"),"Selected export scope missing");
        var sample = AssetSet.Categories.SelectMany(c => assets.Appearances.Where(a => a.Category == c).Take(16)).ToArray();
        foreach (int version in new[] { 860, 960, 1098 })
        {
            var full = new LegacyExport(assets, version, true);
            File.WriteAllText(Path.Combine(tests, "preflight-" + version + ".json"), JsonSerializer.Serialize(full.Summary(), new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {version} full preflight ({full.Objects.Count} appearances, {full.SpriteCount} tiles, {full.Losses.Count} compatibility notices)");
            var plan = new LegacyExport(assets, version, true, sample);
            string target = Path.Combine(tests, "legacy-" + version);
            Reject(() => plan.Export(target, cache, false), "unaccepted compatibility losses");
            Check(!Directory.Exists(target) && !Directory.Exists(target + ".partial"), "preflight must not create output");
            plan.Export(target, cache, true);
            Reject(() => plan.Export(target, cache, true), "existing output overwrite");
            Check(File.ReadAllBytes(Path.Combine(target, "source-appearances.pb")).AsSpan().SequenceEqual(assets.AppearanceBytes.Span), "source protobuf retained verbatim");
            WriteOracle(plan, target, assets, cache);
            Console.WriteLine($"PASS: {version} selected legacy export, explicit loss acceptance, sidecar preservation and overwrite rejection");
            if (version == 1098 && args.Contains("--convert-all"))
            {
                string complete = Path.Combine(tests, "legacy-all-1098");
                full.Export(complete, cache, true, (phase, done, total) => { if (done % 4096 == 0 || done == total) Console.WriteLine($"{phase}: {done}/{total}"); });
                WriteOracle(full, complete, assets, cache);
                Console.WriteLine($"PASS: full 10.98 export ({full.Objects.Count} appearances, {full.SpriteCount} quadrants)");
            }
        }
        Check(new AssetSet(args[1]).Fingerprint == assets.Fingerprint, "conversion left source unchanged");
    }
    if (args.Contains("--stress"))
    {
        var timings = new double[10000]; var process = System.Diagnostics.Process.GetCurrentProcess();
        for (int i = 0; i < timings.Length; i++)
        {
            var a = assets.Appearances[i % assets.Appearances.Count]; var g = AssetSet.Groups(a)[0];
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var image = cache.Render(g, i % g.Frames, i % (int)g.PatternX, 0, 0);
            Check(image.Rgba.Length <= 64 * 64 * 4 && cache.Bytes <= SheetCache.Limit, "stress bounds");
            timings[i] = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(timings); process.Refresh();
        var metrics = new { previews = 10000, p50Ms = timings[5000], p95Ms = timings[9500], cacheBytes = cache.Bytes, cache.Hits, cache.Misses,
            process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount, managedBytes = GC.GetTotalMemory(false) };
        File.WriteAllText(Path.Combine(tests, "modern-stress.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: preview stress " + JsonSerializer.Serialize(metrics));
    }
}

static void WriteOracle(LegacyExport plan, string target, AssetSet assets, SheetCache cache)
{
    // Expected geometry/indices and ARGB samples are derived from source sheets,
    // not from reading the converter's own SPR output.
    var sourceSamples = plan.Objects.Take(64).Concat(plan.Objects.Where(o => o.Groups.Any(g => g.Source.Frames > 1 || g.Width > 1 || g.Height > 1)).Take(24)).Distinct().ToArray();
    var pixelSamples = new List<object>(); var seen = new HashSet<uint>();
    foreach (var obj in sourceSamples)
    foreach (var g in obj.Groups)
    foreach (int reference in new[] { 0, g.References.Length / 2, g.References.Length - 1 }.Distinct())
    {
        uint id = g.References[reference]; if (id == 0 || !seen.Add(id)) continue;
        int sourceIndex = reference / (g.Width * g.Height), h = reference / g.Width % g.Height, w = reference % g.Width;
        var image = cache.Sprite(g.Source.Sprites[sourceIndex]);
        var tile = image.Crop(image.Width - (w + 1) * 32, image.Height - (h + 1) * 32, 32, 32);
        var argb = new byte[4096];
        for (int i = 0; i < 1024; i++)
        {
            byte alpha = tile.Rgba[i * 4 + 3]; if (alpha == 0) continue;
            argb[i * 4] = plan.Transparency ? alpha : (byte)255;
            tile.Rgba.AsSpan(i * 4, 3).CopyTo(argb.AsSpan(i * 4 + 1));
        }
        pixelSamples.Add(new { id, md5 = Convert.ToHexString(MD5.HashData(argb)).ToLowerInvariant() });
    }
    File.WriteAllText(Path.Combine(target, "oracle.json"), JsonSerializer.Serialize(new
    {
        version = plan.Profile.Version, datSignature = plan.Profile.DatSignature, sprSignature = plan.Profile.SprSignature,
        transparency = plan.Transparency, durations = plan.Profile.Durations, frameGroups = plan.Profile.FrameGroups,
        sprites = plan.SpriteCount, pixels = pixelSamples,
        objects = sourceSamples.Select(o => new { category = o.Source.Category, id = o.Source.Id, groups = o.Groups.Select(g => new
        {
            width = g.Width, height = g.Height, frames = g.Source.Frames, layers = g.Source.Layers,
            x = g.Source.PatternX, y = g.Source.PatternY, z = g.Source.PatternZ, refs = g.References,
            mode = g.Mode, loops = g.Loops, start = g.Start, phases = g.Source.Phases
        }) })
    }));
}
