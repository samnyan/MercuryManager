using System.Buffers.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MercuryManager.Server;

public sealed record LegacySlateBufferInput(int BufferIndex, byte[] Data);

public static class LegacySlateTimeInputs
{
    // 只允许读取已验证的时间分量；未使用的字节置零不代表支持其他场景输入。
    public static IReadOnlyList<LegacySlateBufferInput> Evaluate(string reflection, string glsl,
        LegacySlatePixelBindings bindings, float time, float realTime,
        IReadOnlyList<LegacySlateBufferInput>? suppliedInputs = null)
    {
        if (!float.IsFinite(time) || !float.IsFinite(realTime))
            throw new ArgumentException("Slate time inputs must be finite.");
        using var doc = JsonDocument.Parse(reflection);
        var root = doc.RootElement;
        var result = new List<LegacySlateBufferInput>();
        foreach (var ubo in root.GetProperty("ubos").EnumerateArray())
        {
            int slot = ubo.GetProperty("binding").GetInt32();
            if (suppliedInputs?.Any(input => input.BufferIndex == slot &&
                input.Data.Length == ubo.GetProperty("block_size").GetInt32()) == true) continue;
            if (slot == bindings.GammaValues.BufferIndex ||
                (bindings.Material.MaterialBufferBound && slot == bindings.Material.MaterialBufferIndex)) continue;
            int size = ubo.GetProperty("block_size").GetInt32();
            var members = root.GetProperty("types").GetProperty(ubo.GetProperty("type").GetString()!).GetProperty("members");
            if (members.GetArrayLength() != 1) throw Unsupported();
            var member = members[0];
            if (member.GetProperty("name").GetString() != "_m0" || member.GetProperty("type").GetString() != "vec4" ||
                member.GetProperty("offset").GetInt32() != 0 || member.GetProperty("array_stride").GetInt32() != 16 ||
                member.GetProperty("array")[0].GetInt32() * 16 != size || size != 2128) throw Unsupported();
            string block = Regex.Escape(ubo.GetProperty("name").GetString()!);
            var declaration = Regex.Match(glsl, @"\buniform\s+" + block + @"\s*\{[^}]*\}\s*(\w+)\s*;");
            if (!declaration.Success) throw Unsupported();
            string instance = Regex.Escape(declaration.Groups[1].Value);
            var mentions = Regex.Matches(glsl, @"\b" + instance + @"\b");
            var reads = Regex.Matches(glsl, @"\b" + instance + @"\._m0\[132u?\]\.([zw]+)\b");
            // 动态索引、整向量读取和别名传递一律拒绝，不以缓冲大小推断其语义。
            if (reads.Count == 0 || mentions.Count != reads.Count + 1) throw Unsupported();
            var data = new byte[size];
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(2120, 4), time);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(2124, 4), realTime);
            result.Add(new(slot, data));
        }
        return result;
    }
    private static NotSupportedException Unsupported() => new("Slate scene buffer reads outside the verified GameTime/RealTime profile are unsupported.");
}
