using System.Buffers.Binary;
using System.IO.Compression;
using SharpCompress.Compressors.LZMA;

namespace NexaModern;

public sealed record Pixels(int Width, int Height, byte[] Rgba)
{
    public Pixels Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width < 1 || height < 1 || x + width > Width || y + height > Height)
            throw new InvalidDataException("Crop outside sheet.");
        var target = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
            Rgba.AsSpan(((y + row) * Width + x) * 4, width * 4).CopyTo(target.AsSpan(row * width * 4));
        return new Pixels(width, height, target);
    }
}

public sealed class SheetCache
{
    public const long Limit = 64 * 1024 * 1024;
    private readonly AssetSet assets;
    private readonly Dictionary<string, LinkedListNode<(string Key, Pixels Value)>> index = [];
    private readonly LinkedList<(string Key, Pixels Value)> lru = [];
    public long Bytes { get; private set; }
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    private readonly Dictionary<uint,Pixels> changedSprites=[];
    public long ChangedBytes { get; private set; }
    public IReadOnlyCollection<uint> ChangedIds => changedSprites.Keys;
    internal void AcceptSaved()
    {
        changedSprites.Clear();ChangedBytes=0;index.Clear();lru.Clear();Bytes=0;
    }
    public void SetSprite(uint id,Pixels image,bool add=false)
    {
        if(image.Width is not (32 or 64) || image.Height is not (32 or 64) || image.Rgba.Length!=image.Width*image.Height*4)
            throw new InvalidDataException("Invalid native sprite dimensions/payload.");
        long previous=changedSprites.TryGetValue(id,out var old) ? old.Rgba.Length : 0;
        if(ChangedBytes-previous+image.Rgba.Length>64*1024*1024)throw new InvalidDataException("Working-copy sprite edits exceed 64 MiB; export and reopen a project before continuing.");
        if(!add)assets.FindSheet(id);
        assets.SetSpriteShape(id,image.Width,image.Height,add);
        changedSprites[id]=image;ChangedBytes+=image.Rgba.Length-previous;
    }
    public SheetCache(AssetSet assets) => this.assets = assets;
    public Pixels Sheet(Sheet sheet)
    {
        if (index.TryGetValue(sheet.File, out var node))
        {
            Hits++; lru.Remove(node); lru.AddFirst(node); return node.Value.Value;
        }
        Misses++;
        var image = DecodeSheet(assets.Input(sheet.File));
        while (Bytes + image.Rgba.Length > Limit && lru.Last != null)
        {
            var last = lru.Last; Bytes -= last.Value.Value.Rgba.Length;
            index.Remove(last.Value.Key); lru.RemoveLast();
        }
        index[sheet.File] = lru.AddFirst((sheet.File, image)); Bytes += image.Rgba.Length;
        return image;
    }
    public Pixels Sprite(uint id)
    {
        if(changedSprites.TryGetValue(id,out var image))return image;
        var sheet = assets.FindSheet(id);
        uint offset = id - sheet.First; int columns = 384 / sheet.Width;
        return Sheet(sheet).Crop((int)(offset % columns) * sheet.Width, (int)(offset / columns) * sheet.Height,
            sheet.Width, sheet.Height);
    }
    public static Pixels DecodeSheet(string file)
    {
        var bytes = AssetSet.ReadBounded(file, 4 * 1024 * 1024);
        int start = 0;
        while (start < 32 && start < bytes.Length && bytes[start] == 0) start++;
        ReadOnlySpan<byte> magic = [0x70, 0x0A, 0xFA, 0x80, 0x24];
        if (start + 5 > bytes.Length || !bytes.AsSpan(start, 5).SequenceEqual(magic))
            throw new InvalidDataException("Invalid CIP LZMA header.");
        start += 5;
        ulong size = Proto.Varint(bytes, ref start);
        if (start != 32 || size != (ulong)(bytes.Length - 32) || bytes.Length < 50)
            throw new InvalidDataException("Invalid CIP compressed size/padding.");
        var properties = bytes.AsSpan(start, 5).ToArray();
        uint dictionary = BinaryPrimitives.ReadUInt32LittleEndian(properties.AsSpan(1));
        if (properties[0] >= 225 || dictionary is 0 or > 64 * 1024 * 1024)
            throw new InvalidDataException("LZMA dictionary/properties exceed limits.");
        // Before producing at most 1 MiB, no valid match can refer to more than
        // 1 MiB of history. Avoid a fresh 32/64 MiB window for each small sheet.
        // Inputs exceeding that output cap are rejected below, not truncated.
        BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(1), Math.Min(dictionary, 1024 * 1024));
        // CIP's 8-byte size slot is not a reliable decompressed length. Decode
        // incrementally into a fixed cap instead of trusting it or allocating it.
        using var input = new MemoryStream(bytes, start + 13, bytes.Length - start - 13, false);
        using var decoder = new LzmaStream(properties, input, input.Length, -1);
        using var output = new MemoryStream();
        var chunk = new byte[8192]; int count;
        while ((count = decoder.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (output.Length + count > 1024 * 1024) throw new InvalidDataException("Decompressed sheet exceeds 1 MiB.");
            output.Write(chunk, 0, count);
        }
        return DecodeBmp(output.ToArray());
    }
    public static Pixels DecodeBmp(byte[] bmp)
    {
        if (bmp.Length < 54 || bmp[0] != 'B' || bmp[1] != 'M') throw new InvalidDataException("Invalid BMP header.");
        int U32(int p) => BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(p, 4));
        int offset = U32(10), header = U32(14), width = U32(18), height = U32(22), compression = U32(30);
        if (header is not (40 or 108 or 124) || width != 384 || Math.Abs((long)height) != 384 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bmp.AsSpan(26)) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bmp.AsSpan(28)) != 32 || compression is not (0 or 3) ||
            offset < 14 + header || offset > bmp.Length - 384 * 384 * 4)
            throw new InvalidDataException("Expected a complete 384x384 32-bit BMP sheet.");
        if (compression == 3 && (offset < 66 || U32(54) != 0x00FF0000 || U32(58) != 0x0000FF00 || U32(62) != 0x000000FF))
            throw new InvalidDataException("Unsupported BMP channel masks.");
        bool alpha = header >= 108 && U32(66) == unchecked((int)0xFF000000);
        var rgba = new byte[384 * 384 * 4];
        for (int y = 0; y < 384; y++)
        for (int x = 0; x < 384; x++)
        {
            int src = offset + ((height > 0 ? 383 - y : y) * 384 + x) * 4, dst = (y * 384 + x) * 4;
            byte r = bmp[src + 2], g = bmp[src + 1], b = bmp[src];
            rgba[dst] = r; rgba[dst + 1] = g; rgba[dst + 2] = b;
            rgba[dst + 3] = r == 255 && g == 0 && b == 255 ? (byte)0 : alpha ? bmp[src + 3] : (byte)255;
        }
        return new Pixels(384, 384, rgba);
    }
    public Pixels Render(Group group, int frame, int x, int y, int z)
    {
        if (frame < 0 || frame >= group.Frames || x < 0 || x >= group.PatternX || y < 0 || y >= group.PatternY || z < 0 || z >= group.PatternZ)
            throw new InvalidDataException("Frame/pattern outside appearance.");
        int first = checked((int)((((frame * group.PatternZ + z) * group.PatternY + y) * group.PatternX + x) * group.Layers));
        var tiles = new List<Pixels>();
        for (int layer = 0; layer < group.Layers; layer++) tiles.Add(Sprite(group.Sprites[first + layer]));
        int width = tiles.Max(t => t.Width), height = tiles.Max(t => t.Height);
        var result = new Pixels(width, height, new byte[width * height * 4]);
        foreach (var tile in tiles)
        for (int row = 0; row < tile.Height; row++)
        for (int column = 0; column < tile.Width; column++)
        {
            int s = (row * tile.Width + column) * 4;
            int d = ((row + height - tile.Height) * width + column + width - tile.Width) * 4;
            int a = tile.Rgba[s + 3], old = result.Rgba[d + 3], combined = a * 255 + old * (255 - a);
            if (combined == 0) continue;
            for (int channel = 0; channel < 3; channel++)
                result.Rgba[d + channel] = (byte)((tile.Rgba[s + channel] * a * 255 + result.Rgba[d + channel] * old * (255 - a)) / combined);
            result.Rgba[d + 3] = (byte)((combined + 127) / 255);
        }
        return result;
    }
}

public static class Png
{
    public static void Write(string file, Pixels image)
    {
        AssetSet.RejectLinks(File.Exists(file) ? file : Path.GetDirectoryName(file)!);
        // Only the helper's two fixed preview slots are written, never source assets.
        using var output = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; header[9] = 6; Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        for (int y = 0; y < image.Height; y++)
        {
            zlib.WriteByte(0); zlib.Write(image.Rgba, y * image.Width * 4, image.Width * 4);
        }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
    }
    private static void Chunk(Stream output, string name, byte[] data)
    {
        Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(word, data.Length); output.Write(word);
        byte[] type = System.Text.Encoding.ASCII.GetBytes(name); output.Write(type); output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte b in type.Concat(data))
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320);
        }
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); output.Write(word);
    }
}
