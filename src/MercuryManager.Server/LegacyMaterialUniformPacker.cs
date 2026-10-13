using System.Buffers.Binary;

namespace MercuryManager.Server;

/// <summary>UE4.19 Material numeric constants; texture/sampler resources are bound separately.</summary>
public static class LegacyMaterialUniformPacker
{
    public static byte[] PackNumericConstants(LegacyUniformSet set, LegacyMaterialShaderBindings bindings,
        MaterialUniformContext context)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(bindings);
        if (set.Arrays.Length != 9 || set.Arrays.Any(x => x is null))
            throw new InvalidDataException("Invalid material uniform set.");
        if (set.Arrays[0].Length != bindings.VectorCount || set.Arrays[1].Length != bindings.ScalarCount ||
            set.Arrays[2].Length != bindings.Texture2DCount || set.Arrays[3].Length != bindings.TextureCubeCount)
            throw new InvalidDataException("Shader and uniform expression counts disagree.");
        if (set.Arrays[4].Length != 0)
            throw new NotSupportedException("External textures require separate bindings.");
        if (set.Arrays[5].Length != bindings.PerFrameScalarCount || set.Arrays[6].Length != bindings.PerFrameVectorCount)
            throw new InvalidDataException("Per-frame expression counts disagree.");
        var data = new byte[checked(bindings.VectorCount * 16 + ((bindings.ScalarCount + 3) / 4) * 16)];
        for (int i = 0; i < bindings.VectorCount; i++)
        {
            var value = MaterialUniformEvaluator.Evaluate(set.Arrays[0][i], context);
            for (int channel = 0; channel < 4; channel++) Write(data, i * 16 + channel * 4, value[channel]);
        }
        for (int i = 0; i < bindings.ScalarCount; i++)
            Write(data, bindings.VectorCount * 16 + i * 4, MaterialUniformEvaluator.Evaluate(set.Arrays[1][i], context).X);
        return data;
    }

    private static void Write(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
}
