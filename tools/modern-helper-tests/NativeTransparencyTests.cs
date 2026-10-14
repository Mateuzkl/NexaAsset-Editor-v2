using System.Security.Cryptography;
using System.Text.Json;
using NexaModern;

internal static class NativeTransparencyTests
{
    public static void Synthetic(string tests)
    {
        string root=Path.Combine(tests,"transparency-source");Directory.CreateDirectory(root);
        var sheet=new Pixels(384,384,new byte[384*384*4]);
        for(int y=0;y<384;y++)for(int x=0;x<384;x++) {
            int p=(y*384+x)*4;sheet.Rgba[p]=255;sheet.Rgba[p+2]=255;
            // Transparent colored pixels surround partially opaque and opaque pixels in every quadrant.
            if(x<256 && y<64 && x%32>=8 && y%32>=8){sheet.Rgba[p]=12;sheet.Rgba[p+1]=34;sheet.Rgba[p+2]=56;sheet.Rgba[p+3]=(byte)(x%2==0 ? 128 : 255);}
        }
        File.WriteAllBytes(Path.Combine(root,"sheet.lzma"),NativeSave.EncodeSheet(sheet));
        byte[] S(int n,ulong v)=>NativeWorkspace.Scalar(n,v);
        byte[] B(int n,byte[] v)=>NativeWorkspace.Blob(n,v);
        byte[] Join(params byte[][] v)=>v.SelectMany(p=>p).ToArray();
        byte[] Group(int type,uint sprite)=>Join(S(1,(uint)type),B(3,Join(S(1,1),S(2,1),S(3,1),S(4,1),S(5,sprite),S(5,sprite),B(6,Join(B(6,Join(S(1,100),S(2,100))),B(6,Join(S(1,100),S(2,100))))))));
        byte[] Appearance(uint id,uint sprite,params int[] types)=>Join(S(1,id),Join(types.Select(t=>B(2,Group(t,sprite))).ToArray()));
        File.WriteAllBytes(Path.Combine(root,"appearances.dat"),Join(B(1,Appearance(100,1,2)),B(2,Appearance(1,2,0,1)),B(3,Appearance(1,3,3)),B(4,Appearance(1,4,4))));
        File.WriteAllText(Path.Combine(root,"catalog-content.json"),"[{\"type\":\"appearances\",\"file\":\"appearances.dat\"},{\"type\":\"sprite\",\"file\":\"sheet.lzma\",\"spritetype\":3,\"firstspriteid\":1,\"lastspriteid\":36}]");
        Generate(tests,new AssetSet(root),"synthetic");
    }
    public static void Generate(string tests,AssetSet assets,string name)
    {
        var cache=new SheetCache(assets);
        foreach(string category in AssetSet.Categories) {
            // Reproduce the reported outfit, and select colored-alpha-zero examples for every other category.
            var appearance=category=="outfit" && assets.Appearances.Any(a=>a.Category==category&&a.Id==1945) ? assets.Find(category,1945) :
                assets.Appearances.First(a=>a.Category==category && AssetSet.Groups(a).SelectMany(g=>g.Sprites).Any(id=>HasTransparentColor(cache.Sprite(id))));
            foreach(int version in new[]{860,960,1098,1310}) {
                string output=Path.Combine(tests,$"transparency-{name}-{category}-{version}");Directory.CreateDirectory(output);
                var plan=new LegacyExport(assets,version,true,[appearance]);
                var native=new NativeAssets(assets,cache,output);
                object model=native.ObjectExport(category,appearance.Id,version,plan.Profile.DatSignature,plan.Profile.SprSignature);
                var pixels=plan.ObjectPixels(cache).Select(r=> {
                    var raw=new byte[4096];var rgb=new byte[4096];var rgba=new byte[4096];int hidden=0,visible=0;
                    for(int i=0;i<1024;i++) {
                        int p=i*4;byte alpha=r.Image.Rgba[p+3];raw[p]=alpha;r.Image.Rgba.AsSpan(p,3).CopyTo(raw.AsSpan(p+1));
                        if(alpha==0){if(raw[p+1]!=0 || raw[p+2]!=0 || raw[p+3]!=0)hidden++;continue;}
                        visible++;raw.AsSpan(p,4).CopyTo(rgba.AsSpan(p));raw.AsSpan(p,4).CopyTo(rgb.AsSpan(p));rgb[p]=255;
                    }
                    string Hash(byte[] b)=>Convert.ToHexString(MD5.HashData(b)).ToLowerInvariant();
                    return new {id=r.Id,raw=Hash(raw),rgb=Hash(rgb),rgba=Hash(rgba),hidden,visible};
                }).ToArray();
                if(pixels.Sum(p=>p.hidden)==0 || pixels.Sum(p=>p.visible)==0)throw new Exception("Transparency fixture lacks visible/hidden-color pixels.");
                File.WriteAllText(Path.Combine(output,"transparency.json"),JsonSerializer.Serialize(new {model,version,source=assets.Root,dat=plan.Profile.DatSignature,spr=plan.Profile.SprSignature,pixels}));
                foreach(bool alpha in new[]{false,true})new LegacyExport(assets,version,alpha,[appearance]).Export(Path.Combine(output,alpha ? "direct-rgba" : "direct-rgb"),cache,true);
                Console.WriteLine($"Transparency fixture: {name}/{category}/{appearance.Id} -> {version}, {pixels.Sum(p=>p.hidden)} alpha-zero colored pixels");
            }
        }
    }
    private static bool HasTransparentColor(Pixels image)
    {
        for(int p=0;p<image.Rgba.Length;p+=4)if(image.Rgba[p+3]==0 && (image.Rgba[p]!=0 || image.Rgba[p+1]!=0 || image.Rgba[p+2]!=0))return true;
        return false;
    }
}
