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
                limitations.Add($"{pair.UniformName}: {ex.Message}");
            }
        }
        return new(slots.ToArray(), limitations.ToArray());
    }
}
