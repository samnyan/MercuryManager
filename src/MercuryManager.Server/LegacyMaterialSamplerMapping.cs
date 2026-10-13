namespace MercuryManager.Server;

public sealed record LegacyMaterialSamplerSlot(string UniformName, string ImageName, string SamplerName,
    int ExpressionIndex, string AssetPath, int SamplerSource, string? ParameterName);

public static class LegacyMaterialSamplerMapping
{
    // Material 资源成员按 Texture2D、Sampler 成对排列，末尾为共享 Wrap/Clamp。
    public static IReadOnlyList<LegacyMaterialSamplerSlot> Resolve(LegacyShaderResourceTable table,
        LegacyMaterialShaderBindings material, IReadOnlyList<LegacyMaterialTextureExpression> expressions,
        IReadOnlyList<MaterialCombinedSampler> combined)
    {
        if (material.TextureCubeCount != 0 || expressions.Count != material.Texture2DCount)
            throw new NotSupportedException("Only complete Material 2D resource layouts are supported.");
        var textures = table.Textures(material.MaterialBufferIndex);
        var samplers = table.Samplers(material.MaterialBufferIndex);
        var result = new List<LegacyMaterialSamplerSlot>();
        foreach (var pair in combined)
        {
            var image = textures.Where(x => pair.ImageName == "t" + x.BindIndex).ToArray();
            var sampler = samplers.Where(x => pair.SamplerName == "s" + x.BindIndex).ToArray();
            if (image.Length != 1 || sampler.Length != 1)
                throw new NotSupportedException("Sampled resources are not uniquely owned by the Material buffer.");
            int resource = image[0].ResourceIndex;
            if (resource % 2 != 0 || resource / 2 >= expressions.Count)
                throw new NotSupportedException("Sampled texture is outside the Material 2D member layout.");
            var expr = expressions.SingleOrDefault(x => x.ExpressionIndex == resource / 2)
                ?? throw new InvalidDataException("Missing Material texture expression identity.");
            int expected = expr.SamplerSource switch
            {
                0 => resource + 1,
                1 => expressions.Count * 2,
                2 => expressions.Count * 2 + 1,
                _ => throw new NotSupportedException("Unknown Material sampler source.")
            };
            if (sampler[0].ResourceIndex != expected)
                throw new NotSupportedException("Sampled sampler does not match the expression's resource member.");
            result.Add(new(pair.UniformName, pair.ImageName, pair.SamplerName, expr.ExpressionIndex,
                expr.AssetPath, expr.SamplerSource, expr.ParameterName));
        }
        return result;
    }
}
