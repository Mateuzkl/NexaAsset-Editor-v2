using System.Buffers.Binary;
using System.Text;

namespace NexaModern;

// Independently implemented protobuf wire reader. Unknown fields remain in the
// source slices; this browser does not rewrite or claim to convert their meaning.
public readonly record struct Field(int Number, int Wire, ulong Scalar, ReadOnlyMemory<byte> Data);

public sealed class Proto
{
    private readonly ReadOnlyMemory<byte> data;
    private int position;
    public Proto(ReadOnlyMemory<byte> data) => this.data = data;
    public bool End => position == data.Length;
    public int Position => position;
    public static ulong Varint(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong value = 0;
        for (int shift = 0; shift <= 63; shift += 7)
        {
            if (offset >= bytes.Length) throw new InvalidDataException("Truncated protobuf varint.");
            byte next = bytes[offset++];
            if (shift == 63 && next > 1) throw new InvalidDataException("Protobuf varint overflow.");
            value |= (ulong)(next & 127) << shift;
            if ((next & 128) == 0) return value;
        }
        throw new InvalidDataException("Invalid protobuf varint.");
    }
    public Field Next()
    {
        ulong key = Varint(data.Span, ref position);
        if (key >> 3 is 0 or > 536870911) throw new InvalidDataException("Invalid protobuf field number.");
        int number = (int)(key >> 3), wire = (int)(key & 7);
        ulong scalar = 0;
        ReadOnlyMemory<byte> payload = default;
        switch (wire)
        {
            case 0: scalar = Varint(data.Span, ref position); break;
            case 1: payload = Take(8); scalar = BinaryPrimitives.ReadUInt64LittleEndian(payload.Span); break;
            case 2:
                ulong length = Varint(data.Span, ref position);
                if (length > int.MaxValue) throw new InvalidDataException("Protobuf field too large.");
                payload = Take((int)length); break;
            case 5: payload = Take(4); scalar = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span); break;
            default: throw new InvalidDataException($"Unsupported protobuf wire type {wire}.");
        }
        return new Field(number, wire, scalar, payload);
    }
    private ReadOnlyMemory<byte> Take(int count)
    {
        if (count > data.Length - position) throw new InvalidDataException("Truncated protobuf field.");
        var result = data.Slice(position, count); position += count; return result;
    }
    public static List<Field> Fields(ReadOnlyMemory<byte> bytes)
    {
        var result = new List<Field>(); var reader = new Proto(bytes);
        while (!reader.End)
        {
            if (result.Count == 65536) throw new InvalidDataException("Too many protobuf fields.");
            result.Add(reader.Next());
        }
        return result;
    }
    public static uint UInt(Field field)
    {
        if (field.Wire != 0 || field.Scalar > uint.MaxValue) throw new InvalidDataException("Expected uint32.");
        return (uint)field.Scalar;
    }
    public static ReadOnlyMemory<byte> Bytes(Field field)
    {
        if (field.Wire != 2) throw new InvalidDataException("Expected protobuf bytes.");
        return field.Data;
    }
    public static string Text(Field field)
    {
        var bytes = Bytes(field);
        if (bytes.Length > 16384) throw new InvalidDataException("Text field too large.");
        return new UTF8Encoding(false, true).GetString(bytes.Span);
    }
}
