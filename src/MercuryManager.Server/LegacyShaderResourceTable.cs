using System.Buffers.Binary;

namespace MercuryManager.Server;

public sealed record LegacyShaderResourceToken(int BufferIndex, int ResourceIndex, int BindIndex);

// 资源表 token 区分 UBO 槽、资源索引和着色器寄存器，不能直接当作表达式序号。
public sealed record LegacyShaderResourceTable(uint ResourceBits, uint[][] Arrays)
{
    public IReadOnlyList<LegacyShaderResourceToken> Textures(int bufferIndex) => Tokens(4, bufferIndex);
    public IReadOnlyList<LegacyShaderResourceToken> Samplers(int bufferIndex) => Tokens(1, bufferIndex);

    private IReadOnlyList<LegacyShaderResourceToken> Tokens(int arrayIndex, int bufferIndex)
    {
        if (bufferIndex is < 0 or > 31 || Arrays.Length != 5)
            throw new InvalidDataException("Invalid shader resource buffer index/layout.");
        if ((ResourceBits & (1u << bufferIndex)) == 0) return [];
        var map = Arrays[arrayIndex];
        if (bufferIndex >= map.Length) throw new InvalidDataException("Missing resource-table offset.");
        uint start = map[bufferIndex];
        if (start == 0) return [];
        if (start <= bufferIndex || start >= map.Length)
            throw new InvalidDataException("Invalid resource-table token offset.");
        var result = new List<LegacyShaderResourceToken>();
        for (int at = (int)start; at < map.Length; at++)
        {
            uint token = map[at];
            if ((token >> 24) != bufferIndex) return result;
            var entry = new LegacyShaderResourceToken(bufferIndex, (int)((token >> 8) & 65535), (int)(token & 255));
            if (result.Any(x => x.BindIndex == entry.BindIndex))
                throw new InvalidDataException("Duplicate shader resource register.");
            result.Add(entry);
        }
        throw new InvalidDataException("Unterminated resource-table token run.");
    }

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
