using System.Text.Json;
using NexaModern;

internal static class NativeSaveTests
{
    public static void Run(string tests)
    {
        string root=Path.Combine(tests,"native-save-source");Directory.CreateDirectory(root);
        var sheet=new Pixels(384,384,new byte[384*384*4]);sheet.Rgba[0]=19;sheet.Rgba[1]=83;sheet.Rgba[2]=171;sheet.Rgba[3]=128;
        File.WriteAllBytes(Path.Combine(root,"sheet.lzma"),NativeSave.EncodeSheet(sheet));
        File.WriteAllText(Path.Combine(root,"extra.json"),"{\"opaque\":true}");
        byte[] info=Enumerable.Range(1,4).SelectMany(n=>NativeWorkspace.Scalar(n,1)).Concat(NativeWorkspace.Scalar(5,1)).ToArray();
        byte[] source=NativeWorkspace.Scalar(1,100).Concat(NativeWorkspace.Blob(2,NativeWorkspace.Scalar(1,2).Concat(NativeWorkspace.Blob(3,info)).ToArray())).Concat(NativeWorkspace.Blob(99,new byte[]{77})).ToArray();
        byte[] topUnknown=NativeWorkspace.Blob(99,new byte[]{11,12});
        File.WriteAllBytes(Path.Combine(root,"appearances.dat"),NativeWorkspace.Blob(1,source).Concat(topUnknown).ToArray());
        File.WriteAllText(Path.Combine(root,"catalog-content.json"),"[{\"type\":\"appearances\",\"file\":\"appearances.dat\",\"unknown\":13},{\"type\":\"sprite\",\"file\":\"sheet.lzma\",\"spritetype\":0,\"firstspriteid\":1,\"lastspriteid\":3,\"unknown\":17},{\"type\":\"staticdata\",\"file\":\"extra.json\"}]");
        var assets=new AssetSet(root);var cache=new SheetCache(assets);var workspace=new NativeWorkspace(assets,cache);
        workspace.Flag("item",100,6,true);workspace.Duplicate("item",[100]);workspace.Remove("item",[100]);
        var pixels=new Pixels(64,32,new byte[64*32*4]);pixels.Rgba[0]=99;pixels.Rgba[1]=55;pixels.Rgba[2]=17;pixels.Rgba[3]=127;
        cache.SetSprite(1,pixels);workspace.DuplicateSprite(1);
        string before=assets.Fingerprint,target=Path.Combine(tests,"native-save-roundtrip");NativeSave.Export(assets,cache,target);
        var reopened=new AssetSet(target);var decoded=new SheetCache(reopened);
        Check(reopened.Appearances.Count==1 && reopened.Find("item",101).Source.Span.SequenceEqual(assets.Find("item",101).Source.Span),"native metadata edits/removals/IDs");
        Check(Proto.Fields(reopened.AppearanceBytes).Any(f=>f.Number==99 && f.Data.Span.SequenceEqual(new byte[]{11,12})),"root unknown fields");
        Check(decoded.Sprite(1).Rgba.AsSpan().SequenceEqual(pixels.Rgba) && decoded.Sprite(4).Rgba.AsSpan().SequenceEqual(pixels.Rgba),"changed dimensions/new sprite/alpha");
        Check(decoded.Sprite(2).Rgba.AsSpan().SequenceEqual(cache.Sprite(2).Rgba),"unchanged neighbor after range splitting");
        Check(File.ReadAllText(Path.Combine(target,"extra.json"))==File.ReadAllText(Path.Combine(root,"extra.json")),"catalog dependencies");
        Check(new AssetSet(root).Fingerprint==before,"source unchanged");
        Reject(()=>NativeSave.Export(assets,cache,target),"existing generation");Reject(()=>NativeSave.Export(assets,cache,Path.Combine(root,"child")),"source overlap");
        var live=new AssetSet(root);var liveCache=new SheetCache(live);var liveWorkspace=new NativeWorkspace(live,liveCache);
        byte[] oldCatalog=File.ReadAllBytes(Path.Combine(root,"catalog-content.json"));byte[] oldSheet=File.ReadAllBytes(Path.Combine(root,"sheet.lzma"));
        NativeSave.InPlace(live,liveCache);
        Check(File.ReadAllBytes(Path.Combine(root,"catalog-content.json")).AsSpan().SequenceEqual(oldCatalog),"Unedited native save rewrote the catalog");
        liveWorkspace.Flag("item",100,6,true);NativeSave.InPlace(live,liveCache);
        Check((bool)NativeAssets.Properties(new AssetSet(root).Find("item",100))["stackable"],"In-place metadata save");
        Check(File.ReadAllBytes(Path.Combine(root,"sheet.lzma")).AsSpan().SequenceEqual(oldSheet),"Metadata-only save touched a sheet");
        Check(Directory.GetFiles(root,".nexa-backup-*.catalog.json").Any(f=>File.ReadAllBytes(f).AsSpan().SequenceEqual(oldCatalog)),"Native previous-catalog backup");
        foreach(var shape in new[]{(32,32),(32,64),(64,32),(64,64)}){
            var image=new Pixels(shape.Item1,shape.Item2,new byte[shape.Item1*shape.Item2*4]);
            for(int i=0;i<image.Rgba.Length;i+=4){image.Rgba[i]=27;image.Rgba[i+1]=43;image.Rgba[i+2]=91;image.Rgba[i+3]=(byte)((i/4)%256);}
            uint next=live.Sheets[^1].Last+1;liveCache.SetSprite(next,image,true);liveWorkspace.LinkSprite("item",100,0,0,0,0,0,0,next);
            NativeSave.InPlace(live,liveCache);var saved=new AssetSet(root);var savedCache=new SheetCache(saved);
            Check(savedCache.Sprite(next).Rgba.AsSpan().SequenceEqual(image.Rgba),"In-place RGBA dimensions "+shape);
            Check(AssetSet.Groups(saved.Find("item",100))[0].Sprites[0]==next,"In-place sprite reference");
            Check(liveCache.ChangedBytes==0 && liveCache.ChangedIds.Count==0,"Saved native edit budget not released");
        }
        byte[] lastHead=File.ReadAllBytes(Path.Combine(root,"catalog-content.json"));liveWorkspace.Flag("item",100,13,true);
        foreach(string point in new[]{"payload-written","payloads-published","catalog-published"}){
            Reject(()=>NativeSave.InPlace(live,liveCache,checkpoint:p=>{if(p==point)throw new IOException("Simulated disk-full/interrupted write at "+p);}),point);
            Check(File.ReadAllBytes(Path.Combine(root,"catalog-content.json")).AsSpan().SequenceEqual(lastHead),"Failed native save did not retain the previous catalog: "+point);
            Check(!Directory.GetDirectories(root,".nexa-save-*.partial").Any(),"Failed stage left behind");
        }
        using(var locked=new FileStream(Path.Combine(root,"catalog-content.json"),FileMode.Open,FileAccess.Read,FileShare.None))
            Reject(()=>NativeSave.InPlace(live,liveCache),"locked catalog/write permission failure");
        Check(File.ReadAllBytes(Path.Combine(root,"catalog-content.json")).AsSpan().SequenceEqual(lastHead),"Locked save damaged catalog");
        string head=Path.Combine(root,"catalog-content.json");var attributes=File.GetAttributes(head);
        try{File.SetAttributes(head,attributes|FileAttributes.ReadOnly);Reject(()=>NativeSave.InPlace(live,liveCache),"read-only catalog permission");}
        finally{File.SetAttributes(head,attributes);}
        Check(File.ReadAllBytes(head).AsSpan().SequenceEqual(lastHead),"Read-only save damaged catalog");
        NativeSave.InPlace(live,liveCache);Check((bool)NativeAssets.Properties(new AssetSet(root).Find("item",100))["isUnpassable"],"Retry lost unsaved metadata");
        uint newId=JsonSerializer.SerializeToElement(liveWorkspace.Add("item")).GetProperty("selected").GetUInt32();
        var newInfo=Proto.Fields(Proto.Fields(Proto.Fields(live.Find("item",newId).Source).Single(f=>f.Number==2).Data).Single(f=>f.Number==3).Data);
        Check(Enumerable.Range(1,4).All(n=>newInfo.Any(f=>f.Number==n && f.Scalar==1)),"New objects must declare dimensions/layers for real protobuf consumers");
        var magenta=new Pixels(32,32,new byte[4096]);magenta.Rgba[0]=255;magenta.Rgba[2]=255;magenta.Rgba[3]=255;cache.SetSprite(2,magenta);
        string failed=Path.Combine(tests,"native-save-failed");Reject(()=>NativeSave.Export(assets,cache,failed),"reserved opaque magenta");Check(!Directory.Exists(failed)&&!Directory.Exists(failed+".partial"),"failed native publication cleanup");
        Console.WriteLine("PASS: native Save in place, no-edit and metadata-only round trips, four RGBA dimensions, references, backup, edit-budget reset, locked catalog and injected disk-full/interruption rollback; Save As source preservation");
    }
    private static void Check(bool value,string text){if(!value)throw new Exception(text);}
    private static void Reject(Action action,string text){try{action();}catch(InvalidDataException){return;}throw new Exception("Did not reject "+text);}
}
