using System.Buffers.Binary;

namespace MercuryManager.Server;

// 保留原生资源表顺序；数组语义须由资源绑定消费者确认，不猜纹理槽位。
public sealed record LegacyShaderResourceTable(uint ResourceBits, uint[][] Arrays)
{
    public static LegacyShaderResourceTable Read(ReadOnlySpan<byte> prefix)
    {
        int position = 0;
        uint bits = Word(prefix, ref position);
        var arrays = new uint[5][];
        for (int i = 0; i < arrays.Length; i++)
        {
            uint count = Word(prefix, ref position);
            if (count > 1048576 || count > (prefix.Length - position) / 4)
                throw new InvalidDataException("Shader resource table count exceeds prefix bounds.");
            arrays[i] = new uint[(int)count];
            for (int j = 0; j < arrays[i].Length; j++) arrays[i][j] = Word(prefix, ref position);
        }
        if (position != prefix.Length) throw new InvalidDataException("Unexpected bytes between resource table and DXBC.");
        return new(bits, arrays);
    }

    private static uint Word(ReadOnlySpan<byte> bytes, ref int position)
    {
        if (position > bytes.Length - 4) throw new InvalidDataException("Truncated shader resource table.");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[position..]);
        position += 4;
        return value;
    }
}
