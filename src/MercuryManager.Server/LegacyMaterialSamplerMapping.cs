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
            {
                // Report all owners without inventing a View/Scene texture source.
                var imageOwners = Enumerable.Range(0, 32).SelectMany(table.Textures)
                    .Where(x => pair.ImageName == "t" + x.BindIndex).ToArray();
                var samplerOwners = Enumerable.Range(0, 32).SelectMany(table.Samplers)
                    .Where(x => pair.SamplerName == "s" + x.BindIndex).ToArray();

                // View resource mapping (Buffer 1):
                // Resource 35: PerlinNoiseGradientTexture, Resource 36: PerlinNoiseGradientTextureSampler
                if (imageOwners.Length == 1 && samplerOwners.Length == 1 &&
                    imageOwners[0].BufferIndex == 1 && samplerOwners[0].BufferIndex == 1 &&
                    imageOwners[0].ResourceIndex == 35 && samplerOwners[0].ResourceIndex == 36)
                {
                    result.Add(new(pair.UniformName, pair.ImageName, pair.SamplerName, -1,
                        "/Engine/EngineResources/PerlinNoiseGradientTexture", 0, null));
                    continue;
                }

                static string Owners(IEnumerable<LegacyShaderResourceToken> tokens) =>
                    string.Join(", ", tokens.Select(x => $"buffer={x.BufferIndex}/resource={x.ResourceIndex}/register={x.BindIndex}"));
                throw new NotSupportedException($"Unresolved sampled resource {pair.UniformName}: " +
                    $"image {pair.ImageName} [{Owners(imageOwners)}], sampler {pair.SamplerName} [{Owners(samplerOwners)}]. " +
                    "An explicit native resource-source adapter is required; no default texture was substituted.");
            }
            int resource = image[0].ResourceIndex;
            if (resource % 2 != 0 || resource / 2 >= expressions.Count)
                throw new NotSupportedException("Sampled texture is outside the Material 2D member layout.");
            var expr = expressions.SingleOrDefault(x => x.ExpressionIndex == resource / 2)
                ?? throw new InvalidDataException("Missing Material texture expression identity.");
            int samplerResource = sampler[0].ResourceIndex;
            int samplerSource;
            if (samplerResource == resource + 1) samplerSource = 0;
            else if (samplerResource == expressions.Count * 2) samplerSource = 1;
            else if (samplerResource == expressions.Count * 2 + 1) samplerSource = 2;
            else throw new NotSupportedException("Cross-texture asset sampler metadata is required.");
            // 编译后的采样可对同一图像使用多个共享 sampler，以资源 token 为准。
            result.Add(new(pair.UniformName, pair.ImageName, pair.SamplerName, expr.ExpressionIndex,
                expr.AssetPath, samplerSource, expr.ParameterName));
        }
        return result;
    }
}
