using System.Text.Json;

namespace MercuryManager.Server;

/// <summary>Select by serialized shader identity and entry boundary, never discovery order.</summary>
public static class LegacySlateProgramSelector
{
    public static MaterialShaderCompiler.ShaderCandidate SelectDefaultFalse(
        IReadOnlyList<MaterialShaderCompiler.ShaderCandidate> candidates, IReadOnlyList<LegacyShaderEntry> entries)
    {
        var matches = entries.Where(x => x.Type == "TSlateMaterialShaderPSDefaultfalse").ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("Expected one unambiguous Slate Defaultfalse shader entry.");
        var entry = matches[0];
        if (entry.StartOffset < 0 || entry.EndOffset <= entry.StartOffset)
            throw new InvalidDataException("Invalid Slate shader directory boundary.");
        var programs = candidates.Where(x => x.Offset >= entry.StartOffset && x.Offset < entry.EndOffset).ToArray();
        if (programs.Length != 1 || programs[0].Stage != "fragment")
            throw new NotSupportedException("Slate entry does not contain one independently identified inline fragment program.");
        return programs[0];
    }

    public static void ValidateMaterialBuffer(string reflection, LegacyMaterialShaderBindings bindings, byte[] constants)
    {
        _ = GetMaterialBufferSize(reflection, bindings, constants);
    }

    public static byte[] PrepareMaterialBuffer(string reflection, LegacyMaterialShaderBindings bindings, byte[] constants)
    {
        int size = GetMaterialBufferSize(reflection, bindings, constants);
        return constants[..size];
    }

    public static void ValidateFrameWrites(string reflection, LegacyMaterialShaderBindings material,
        int materialUploadBytes, IReadOnlyList<LegacyFrameConstantWrite> writes)
    {
        using var json = JsonDocument.Parse(reflection);
        if (writes.Count == 0) return;
        if (!json.RootElement.TryGetProperty("ubos", out var ubos))
            throw new InvalidDataException("Frame writes require reflected uniform buffers.");
        foreach (var write in writes)
        {
            var matches = ubos.EnumerateArray().Where(x => x.GetProperty("binding").GetInt32() == write.BufferIndex).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Ambiguous or missing frame UBO slot.");
            int size = matches[0].GetProperty("block_size").GetInt32();
            if (write.Data.Length is not (4 or 16) || write.ByteOffset % 4 != 0 ||
                size <= 0 || write.ByteOffset > size - write.Data.Length)
                throw new InvalidDataException("Frame write exceeds reflected UBO bounds.");
            if (material.MaterialBufferBound && write.BufferIndex == material.MaterialBufferIndex &&
                write.ByteOffset < materialUploadBytes)
                throw new InvalidDataException("Frame write overlaps uploaded Material constants.");
            // 不把任意反射布局当作原生寄存器数组，避免将字节写到重映射后的成员。
            var type = json.RootElement.GetProperty("types").GetProperty(matches[0].GetProperty("type").GetString()!);
            var members = type.GetProperty("members").EnumerateArray().ToArray();
            if (members.Length != 1 || members[0].GetProperty("type").GetString() != "vec4" ||
                members[0].GetProperty("offset").GetInt32() != 0 || members[0].GetProperty("array_stride").GetInt32() != 16 ||
                members[0].GetProperty("array").GetArrayLength() != 1 || size % 16 != 0 ||
                members[0].GetProperty("array")[0].GetInt32() != size / 16)
                throw new NotSupportedException("Frame UBO is not a contiguous native register layout.");
        }
    }

    private static int GetMaterialBufferSize(string reflection, LegacyMaterialShaderBindings bindings, byte[] constants)
    {
        if (!bindings.MaterialBufferBound)
            throw new NotSupportedException("Selected shader has no material uniform buffer binding.");
        using var json = JsonDocument.Parse(reflection);
        if (!json.RootElement.TryGetProperty("ubos", out var ubos))
            throw new InvalidDataException("Translated shader has no reflected uniform buffers.");
        var matches = ubos.EnumerateArray().Where(x => x.GetProperty("binding").GetInt32() == bindings.MaterialBufferIndex).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Ambiguous reflected Material UBO slot.");
        int size = matches[0].GetProperty("block_size").GetInt32();
        if (size <= 0 || size > constants.Length || size % 16 != 0)
            throw new InvalidDataException("Reflected Material UBO exceeds native numeric layout.");
        // DXBC translation can omit unused trailing registers, but never relocate live fields.
        var type = json.RootElement.GetProperty("types").GetProperty(matches[0].GetProperty("type").GetString()!);
        var members = type.GetProperty("members").EnumerateArray().ToArray();
        if (members.Length != 1 || members[0].GetProperty("type").GetString() != "vec4" ||
            members[0].GetProperty("offset").GetInt32() != 0 || members[0].GetProperty("array_stride").GetInt32() != 16 ||
            members[0].GetProperty("array").GetArrayLength() != 1 || members[0].GetProperty("array")[0].GetInt32() != size / 16)
            throw new NotSupportedException("Translated Material UBO is not a contiguous native register prefix.");
        return size;
    }
}
