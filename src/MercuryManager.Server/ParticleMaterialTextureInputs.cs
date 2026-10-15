namespace MercuryManager.Server;

/// <summary>Per-sampler mapping keeps unsupported scene resources separate from asset textures.</summary>
public static class ParticleMaterialTextureInputs
{
    public sealed record Evidence(LegacyMaterialSamplerSlot[] SamplerSlots, string[] Limitations);

    public static Evidence Inspect(byte[] resourcePrefix, LegacyMaterialShaderBindings material,
        IReadOnlyList<LegacyMaterialTextureExpression> expressions, IReadOnlyList<MaterialCombinedSampler> combined)
    {
        var slots = new List<LegacyMaterialSamplerSlot>();
        var limitations = new List<string>();
        var table = LegacyShaderResourceTable.Read(resourcePrefix);
        foreach (var pair in combined)
        {
            if (!material.MaterialBufferBound)
            {
                limitations.Add($"{pair.UniformName}: no bound Material resource owner; scene resources require separate adapters.");
                continue;
            }
            // Only Material-owned images enter this adapter. Do not silently provide generated View textures.
            if (!table.Textures(material.MaterialBufferIndex).Any(t => pair.ImageName == "t" + t.BindIndex))
            {
                limitations.Add($"{pair.UniformName}: image is not Material-owned; no asset source supplied.");
                continue;
            }
            if (pair.SamplerType != "sampler2D")
            {
                limitations.Add($"{pair.UniformName}: unsupported Material sampler type {pair.SamplerType}.");
                continue;
            }
            try
            {
                slots.AddRange(LegacyMaterialSamplerMapping.Resolve(table, material, expressions, [pair]));
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                // APPROXIMATION: preserve a verified Material image when its sampler is
                // shared by View. Use preview linear-repeat filtering, not a guessed image.
                var images = table.Textures(material.MaterialBufferIndex)
                    .Where(t => pair.ImageName == "t" + t.BindIndex).ToArray();
                var owners = Enumerable.Range(0, 32).SelectMany(table.Samplers)
                    .Where(t => pair.SamplerName == "s" + t.BindIndex).ToArray();
                var image = images.Length == 1 ? images[0] : null;
                var expression = image != null && image.ResourceIndex >= 0 && image.ResourceIndex % 2 == 0 &&
                    material.TextureCubeCount == 0 && expressions.Count == material.Texture2DCount
                    ? expressions.SingleOrDefault(e => e.ExpressionIndex == image.ResourceIndex / 2) : null;
                if (expression != null && owners.Length == 1 && owners[0].BufferIndex == 1)
                {
                    slots.Add(new(pair.UniformName, pair.ImageName, pair.SamplerName,
                        expression.ExpressionIndex, expression.AssetPath, 1, expression.ParameterName));
                    limitations.Add($"APPROXIMATION: {pair.UniformName} retains Material image {expression.AssetPath}; shared View sampler uses preview linear-repeat filtering.");
                }
                else limitations.Add($"{pair.UniformName}: {ex.Message}");
            }
        }
        return new(slots.ToArray(), limitations.ToArray());
    }
}
