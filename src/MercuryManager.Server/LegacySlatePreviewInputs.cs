using System.Text.Json;

namespace MercuryManager.Server;

public static class LegacySlatePreviewInputs
{
    // 默认矩形预览只提供顶点色、UV、Gamma 和已求值常量；场景 View 不以零缓冲代替。
    public static void Validate(string reflection, LegacySlatePixelBindings bindings,
        IReadOnlyList<LegacySlateBufferInput>? sceneInputs = null)
    {
        using var doc = JsonDocument.Parse(reflection);
        var root = doc.RootElement;
        foreach (var input in root.GetProperty("inputs").EnumerateArray())
            if (input.GetProperty("type").GetString() != "vec4" ||
                input.GetProperty("name").GetString() is not ("v1" or "v4"))
                throw new NotSupportedException("Slate preview requires unsupported vertex inputs.");
        if (bindings.ShaderParams.NumBytes != 0 || bindings.ElementTexture.NumResources != 0 ||
            bindings.ElementTextureSampler.NumResources != 0)
            throw new NotSupportedException("Slate brush/shader inputs are not provided by rectangle preview.");
        var gamma = bindings.GammaValues;
        if (gamma.NumBytes != 8 || gamma.BaseIndex % 4 != 0)
            throw new NotSupportedException("Unsupported Slate gamma binding.");
        var ubos = root.GetProperty("ubos").EnumerateArray().ToArray();
        foreach (var ubo in ubos)
        {
            int slot = ubo.GetProperty("binding").GetInt32(), size = ubo.GetProperty("block_size").GetInt32();
            if (slot == bindings.Material.MaterialBufferIndex && bindings.Material.MaterialBufferBound) continue;
            if (sceneInputs?.Any(input => input.BufferIndex == slot && input.Data.Length == size) == true) continue;
            if (slot != gamma.BufferIndex || gamma.BaseIndex + 8 > size || size > 256)
                throw new NotSupportedException("Slate View/scene uniform buffers are not supported by rectangle preview.");
        }
        if (ubos.Count(u => u.GetProperty("binding").GetInt32() == gamma.BufferIndex) != 1 ||
            (bindings.Material.MaterialBufferBound && gamma.BufferIndex == bindings.Material.MaterialBufferIndex))
            throw new NotSupportedException("Slate gamma buffer is not uniquely identified.");
    }
}
