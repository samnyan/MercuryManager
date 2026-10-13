using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace MercuryManager.Server;

public sealed record MaterialCombinedSampler(string UniformName, string ImageName, string SamplerName);

public static class MaterialCombinedSamplerReader
{
    // 从 OpSampledImage 的真实操作数回溯 OpLoad，不从 descriptor binding 猜寄存器。
    public static IReadOnlyList<MaterialCombinedSampler> Read(byte[] spirv, string glsl)
    {
        if (spirv.Length < 20 || spirv.Length % 4 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(spirv) != 0x07230203)
            throw new InvalidDataException("Invalid SPIR-V module.");
        var names = new Dictionary<uint, string>();
        var loads = new Dictionary<uint, uint>();
        var pairs = new List<(uint Image, uint Sampler)>();
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(spirv.AsSpan(offset * 4, 4));
        for (int at = 5; at < spirv.Length / 4;)
        {
            uint header = Word(at); int count = (int)(header >> 16), opcode = (int)(header & 65535);
            if (count == 0 || count > spirv.Length / 4 - at) throw new InvalidDataException("Truncated SPIR-V instruction.");
            if (opcode == 5)
            {
                if (count < 3) throw new InvalidDataException("Invalid OpName.");
                var bytes = spirv.AsSpan((at + 2) * 4, (count - 2) * 4);
                int end = bytes.IndexOf((byte)0);
                if (end < 0) throw new InvalidDataException("Unterminated SPIR-V name.");
                names[Word(at + 1)] = Encoding.UTF8.GetString(bytes[..end]);
            }
            else if (opcode == 61)
            {
                if (count < 4) throw new InvalidDataException("Invalid OpLoad.");
                loads[Word(at + 2)] = Word(at + 3);
            }
            else if (opcode == 86)
            {
                if (count != 5) throw new InvalidDataException("Invalid OpSampledImage.");
                pairs.Add((Word(at + 3), Word(at + 4)));
            }
            at += count;
        }
        var result = new Dictionary<string, MaterialCombinedSampler>(StringComparer.Ordinal);
        foreach (var (image, sampler) in pairs)
        {
            if (!loads.TryGetValue(image, out var imageId) || !loads.TryGetValue(sampler, out var samplerId) ||
                !names.TryGetValue(imageId, out var imageName) || !names.TryGetValue(samplerId, out var samplerName))
                throw new NotSupportedException("Indirect sampled image operands are unsupported.");
            string uniform = "SPIRV_Cross_Combined" + imageName + samplerName;
            if (!Regex.IsMatch(glsl, @"\buniform\s+(?:(?:lowp|mediump|highp)\s+)?sampler2D\s+" + Regex.Escape(uniform) + @"\s*;"))
                throw new NotSupportedException("Combined sampler naming/type is not verified in generated GLSL.");
            result.TryAdd(uniform, new(uniform, imageName, samplerName));
        }
        return result.Values.ToArray();
    }
}
