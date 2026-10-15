using System.Text.Json;
using NexaModern;

internal static class ReviewRegressionTests
{
    private static byte[] S(int n,ulong v)=>NativeWorkspace.Scalar(n,v);
    private static byte[] B(int n,byte[] v)=>NativeWorkspace.Blob(n,v);
    private static byte[] Join(params byte[][] v)=>v.SelectMany(p=>p).ToArray();
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
    private static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Expected atomic rejection.");}
    private static byte[] FirstArgb(string file){using var input=new BinaryReader(File.OpenRead(file));Check(input.ReadUInt32()==0x3142584e,"packet magic");Check(input.ReadUInt32()>0,"packet count");input.ReadUInt32();input.ReadInt32();input.ReadInt32();input.ReadInt32();return input.ReadBytes(4);}
    public static void Run(string tests)
    {
        string root=Path.Combine(tests,"review-source"),output=Path.Combine(tests,"review-packets");Directory.CreateDirectory(root);Directory.CreateDirectory(output);
        var sheet=new Pixels(384,384,new byte[384*384*4]);
        for(int y=0;y<32;y++)for(int x=0;x<64;x++){int at=(y*384+x)*4;sheet.Rgba[at+(x<32 ? 1 : 0)]=255;sheet.Rgba[at+3]=255;}
        File.WriteAllBytes(Path.Combine(root,"sheet.lzma"),NativeSave.EncodeSheet(sheet));
        byte[] info=Join(S(1,1),S(2,1),S(3,1),S(4,2),S(5,1),S(5,2));
        byte[] Appearance(uint id,int category)=>Join(S(1,id),B(2,Join(S(1,category==2 ? 1u : 2u),B(3,info))));
        File.WriteAllBytes(Path.Combine(root,"appearances.dat"),Join(B(1,Appearance(100,1)),B(2,Appearance(1,2))));
        File.WriteAllText(Path.Combine(root,"catalog-content.json"),"[{\"type\":\"appearances\",\"file\":\"appearances.dat\"},{\"type\":\"sprite\",\"file\":\"sheet.lzma\",\"spritetype\":0,\"firstspriteid\":1,\"lastspriteid\":3}]");
        var assets=new AssetSet(root);var cache=new SheetCache(assets);var workspace=new NativeWorkspace(assets,cache);var native=new NativeAssets(assets,cache,output);
        workspace.Copy("item",100);var clipboard=(Appearance)typeof(NativeWorkspace).GetField("clipboard",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(workspace)!;
        Check(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(clipboard.Source,out var clipboardBytes)&&clipboardBytes.Array!.Length==clipboard.Source.Length,"Clipboard retained the complete original protobuf buffer");
        var group=AssetSet.Groups(assets.Find("outfit",1))[0];
        Check(cache.Render(group,0,0,0,0,true).Rgba.AsSpan(0,4).SequenceEqual(new byte[]{0,255,0,255}),"Outfit base layer");
        Check(cache.Render(group,0,0,0,0).Rgba.AsSpan(0,4).SequenceEqual(new byte[]{255,0,0,255}),"Other categories must retain all-layer composition");
        string FileOf(object value)=>JsonSerializer.SerializeToElement(value).GetProperty("file").GetString()!;
        Check(FirstArgb(FileOf(native.Objects("outfit",1,"",false))).SequenceEqual(new byte[]{255,0,255,0}),"Outfit thumbnail leaked color mask");
        Check(FirstArgb(FileOf(native.Frame("outfit",1,0,0,0,0,0,-1,false))).SequenceEqual(new byte[]{255,0,255,0}),"Outfit frame composition");
        Check(FirstArgb(FileOf(native.Frame("outfit",1,0,0,0,0,0,1,false))).SequenceEqual(new byte[]{255,255,0,0}),"Explicit mask layer remains inspectable");
        string png=Path.Combine(tests,"review-outfit.png");workspace.ExportPng("outfit",1,png,false);Check(NativePng.Read(png).Rgba.AsSpan(0,4).SequenceEqual(new byte[]{0,255,0,255}),"Outfit PNG mask export");
        long revision=workspace.Revision,bytes=cache.ChangedBytes;byte[] before=cache.Sprite(1).Rgba;
        Reject(()=>workspace.ClearSprites([1,999999]));Reject(()=>workspace.ClearSprites([1,1]));Reject(()=>workspace.ClearSprites(Enumerable.Range(1,101).Select(n=>(uint)n).ToArray()));
        Check(workspace.Revision==revision&&cache.ChangedBytes==bytes&&cache.Sprite(1).Rgba.SequenceEqual(before),"Sprite batch rejection was partial");
        workspace.ClearSprites([1,2]);Check(workspace.Revision==revision+1&&cache.Sprite(1).Rgba.All(b=>b==0)&&cache.Sprite(2).Rgba.All(b=>b==0),"Atomic sprite cleanup");

        // Protocol recovery is tested against the actual helper loop, not just a parser approximation.
        string session=Path.Combine(tests,"review-ipc");Directory.CreateDirectory(session);var savedIn=Console.In;var savedOut=Console.Out;using var captured=new StringWriter();
        try{
            Console.SetIn(new StringReader(new string('x',70000)+"\n"+new string('é',40000)+"\n"+"{\"requestId\":\"after\",\"generation\":1,\"command\":\"stats\"}\n{\"requestId\":\"quit\",\"generation\":1,\"command\":\"quit\"}\n"));Console.SetOut(captured);
            Check(NexaModern.Program.Main(["--input",root,"--output",session])==0,"Oversized request terminated helper");
        }finally{Console.SetIn(savedIn);Console.SetOut(savedOut);}
        var messages=captured.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>JsonDocument.Parse(line)).ToArray();
        try{Check(messages.Count(m=>m.RootElement.TryGetProperty("errorCode",out var code)&&code.GetString()=="REQUEST_TOO_LARGE")==2,"ASCII/UTF-8 oversized requests were not each rejected once");Check(messages.Any(m=>m.RootElement.TryGetProperty("requestId",out var id)&&id.GetString()=="after"&&m.RootElement.GetProperty("type").GetString()=="result"),"Helper did not recover after oversized line");}
        finally{foreach(var m in messages)m.Dispose();}

        File.WriteAllText(Path.Combine(root,"user-notes.txt"),"must remain");string forged=Path.Combine(root,"appearances-"+new string('0',64)+".dat");File.WriteAllText(forged,"hash mismatch: user file");
        var hashes=new List<string>();
        for(int generation=0;generation<8;generation++){
            var image=new Pixels(32,32,new byte[4096]);image.Rgba[0]=(byte)(generation+1);image.Rgba[3]=255;cache.SetSprite(1,image);
            workspace.Flag("item",100,6,generation%2==0);NativeSave.InPlace(assets,cache);
            hashes.Add(assets.FindSheet(1).File);
            Check(Directory.GetFiles(root,".nexa-backup-*.catalog.json").Length<=NativeRetention.BackupsToKeep,"Backups exceed retention limit");
        }
        Check(!File.Exists(Path.Combine(root,hashes[0]))&&File.Exists(Path.Combine(root,hashes[^1])),"Expired generated payload not pruned / current payload removed");
        foreach(string backup in Directory.GetFiles(root,".nexa-backup-*.catalog.json")){
            string recovery=Path.Combine(tests,"recover-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(recovery);File.Copy(backup,Path.Combine(recovery,"catalog-content.json"));
            var saved=new AssetSet(recovery,fallbackRoot:root);var recovered=new SheetCache(saved);Check(recovered.Sprite(1).Rgba.Length==4096,"Retained backup cannot recover its referenced sheets");saved.Find("item",100);
        }
        Check(File.ReadAllText(forged).Contains("user file")&&File.ReadAllText(Path.Combine(root,"user-notes.txt"))=="must remain"&&File.Exists(Path.Combine(root,"sheet.lzma")),"Pruning touched unrelated/unverified original resources");
        string currentSheet=assets.FindSheet(1).File;
        NativeSave.InPlace(assets,cache,progress:(phase,_,_)=>{if(phase=="Native assets saved")throw new IOException("Broken notification pipe");});
        Check(new AssetSet(root).FindSheet(1).File==currentSheet,"Notification failure rolled back committed save");
        string malformed=Path.Combine(root,".nexa-backup-user.catalog.json");File.WriteAllText(malformed,"malformed");int count=Directory.GetFiles(root).Length;NativeRetention.Prune(root);Check(Directory.GetFiles(root).Length==count,"Malformed backup caused unsafe pruning");
        Console.WriteLine("PASS: outfit base-layer thumbnails/frames/PNG, explicit mask inspection, atomic sprite batches, oversized IPC recovery, three recoverable backups, verified payload pruning and final-notification safety");
    }
}
