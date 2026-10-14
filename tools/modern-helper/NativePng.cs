using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace NexaModern;

/// <summary>Bounded PNG sprite decoder. Keeps straight RGBA bytes out of AIR's premultiplied bitmap pipeline.</summary>
public static class NativePng
{
    public static Pixels Read(string file)
    {
        AssetSet.RejectLinks(file);byte[] bytes=AssetSet.ReadBounded(file,1024*1024);
        return Read(bytes);
    }
    public static Pixels Read(byte[] bytes)
    {
        if(bytes.Length>1024*1024)throw new InvalidDataException("PNG input exceeds 1 MiB.");
        if(bytes.Length<33 || !bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException("Invalid PNG signature.");
        int at=8,width=0,height=0,depth=0,type=-1;byte[]? palette=null,alpha=null;bool ended=false,idat=false,idatEnded=false;using var compressed=new MemoryStream();
        while(at<bytes.Length){
            if(bytes.Length-at<12)throw new InvalidDataException("Truncated PNG chunk.");
            uint size=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));if(size>bytes.Length-at-12)throw new InvalidDataException("PNG chunk exceeds file.");
            string name=Encoding.ASCII.GetString(bytes,at+4,4);var data=bytes.AsSpan(at+8,(int)size);
            uint crc=uint.MaxValue;foreach(byte b in bytes.AsSpan(at+4,(int)size+4)){crc^=b;for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)==0 ? 0u : 0xEDB88320u);}
            if(~crc!=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at+8+(int)size)))throw new InvalidDataException("PNG CRC mismatch.");
            if(width==0 && name!="IHDR")throw new InvalidDataException("PNG must start with IHDR.");
            switch(name){
                case "IHDR":
                    if(width!=0 || size!=13)throw new InvalidDataException("Invalid/duplicate PNG IHDR.");
                    width=BinaryPrimitives.ReadInt32BigEndian(data);height=BinaryPrimitives.ReadInt32BigEndian(data[4..]);depth=data[8];type=data[9];
                    if(width is not (32 or 64)||height is not (32 or 64)||data[10]!=0||data[11]!=0||data[12]!=0||type is not (0 or 2 or 3 or 4 or 6)||!(depth==8 || (type is 0 or 3 && depth is 1 or 2 or 4)))throw new InvalidDataException("Use a non-interlaced 32/64-pixel PNG (8-bit RGB/RGBA/grayscale, or 1/2/4/8-bit palette/grayscale).");break;
                case "PLTE":if(idat||palette!=null||size==0||size>768||size%3!=0)throw new InvalidDataException("Invalid PNG palette.");palette=data.ToArray();break;
                case "tRNS":if(idat||alpha!=null||size>256||type is 4 or 6)throw new InvalidDataException("Invalid PNG transparency chunk.");alpha=data.ToArray();break;
                case "IDAT":if(idatEnded)throw new InvalidDataException("Non-consecutive PNG image chunks.");compressed.Write(data);idat=true;break;
                case "IEND":if(size!=0||!idat)throw new InvalidDataException("Invalid PNG end.");ended=true;break;
                default:if((bytes[at+4]&32)==0)throw new InvalidDataException("Unsupported critical PNG chunk.");if(idat)idatEnded=true;break;
            }
            at+=12+(int)size;if(ended)break;
        }
        if(!ended||at!=bytes.Length)throw new InvalidDataException("Incomplete/trailing PNG data.");
        if(type==3 && (palette==null || (alpha!=null && alpha.Length>palette.Length/3)))throw new InvalidDataException("Missing PNG palette or excessive palette alpha.");
        if(alpha!=null && ((type==0 && alpha.Length!=2)||(type==2 && alpha.Length!=6)))throw new InvalidDataException("Invalid PNG transparency key.");
        int channels=type switch {2=>3,4=>2,6=>4,_=>1},stride=(width*channels*depth+7)/8,bpp=Math.Max(1,(channels*depth+7)/8);
        byte[] filtered=new byte[(stride+1)*height];compressed.Position=0;
        using(var zlib=new ZLibStream(compressed,CompressionMode.Decompress,true)){zlib.ReadExactly(filtered);if(zlib.ReadByte()!=-1)throw new InvalidDataException("PNG decompression exceeds declared geometry.");}
        byte[] scan=new byte[stride*height];
        for(int y=0;y<height;y++){
            int mode=filtered[y*(stride+1)];if(mode>4)throw new InvalidDataException("Invalid PNG filter.");
            for(int x=0;x<stride;x++){
                int a=x<bpp ? 0 : scan[y*stride+x-bpp],b=y==0 ? 0 : scan[(y-1)*stride+x],c=y==0||x<bpp ? 0 : scan[(y-1)*stride+x-bpp];
                int delta=mode switch {0=>0,1=>a,2=>b,3=>(a+b)/2,_=>Paeth(a,b,c)};scan[y*stride+x]=unchecked((byte)(filtered[y*(stride+1)+1+x]+delta));
            }
        }
        byte[] rgba=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++){
            int dest=(y*width+x)*4,src=y*stride+x*channels;byte r,g,b,a=255;
            int sample=depth==8 ? scan[src] : (scan[y*stride+x*depth/8]>>(8-depth-(x*depth%8)))&((1<<depth)-1);
            switch(type){
                case 3:if(sample>=palette!.Length/3)throw new InvalidDataException("Palette index outside PNG palette.");r=palette[sample*3];g=palette[sample*3+1];b=palette[sample*3+2];a=alpha!=null && sample<alpha.Length ? alpha[sample] : (byte)255;break;
                case 0:r=g=b=(byte)(sample*255/((1<<depth)-1));if(alpha!=null && sample==BinaryPrimitives.ReadUInt16BigEndian(alpha))a=0;break;
                case 4:r=g=b=scan[src];a=scan[src+1];break;
                default:r=scan[src];g=scan[src+1];b=scan[src+2];if(type==6)a=scan[src+3];else if(alpha!=null && r==BinaryPrimitives.ReadUInt16BigEndian(alpha) && g==BinaryPrimitives.ReadUInt16BigEndian(alpha.AsSpan(2)) && b==BinaryPrimitives.ReadUInt16BigEndian(alpha.AsSpan(4)))a=0;break;
            }
            rgba[dest]=r;rgba[dest+1]=g;rgba[dest+2]=b;rgba[dest+3]=a;
        }
        return new(width,height,rgba);
    }
    private static int Paeth(int a,int b,int c){int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);return pa<=pb && pa<=pc ? a : pb<=pc ? b : c;}
}
