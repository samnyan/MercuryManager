namespace MercuryManager.Server;

public sealed record LegacyMaterialTextureExpression(int ExpressionIndex, string AssetPath,
    int ReferencedTextureIndex, int SamplerSource, string? ParameterName);

public static class LegacyMaterialTextureResolver
{
    // 表达式顺序是 Material 资源表顺序，不等同于 GLSL sampler 或 DXBC 寄存器号。
    public static IReadOnlyList<LegacyMaterialTextureExpression> Resolve2D(LegacyUniformSet set,
        IReadOnlyList<string> referencedTextures, IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (set.Arrays.Length != 9) throw new InvalidDataException("Invalid material expression arrays.");
        if (set.Arrays[3].Length != 0 || set.Arrays[4].Length != 0)
            throw new NotSupportedException("Cube and external texture expressions require separate resource mapping.");
        var result = new List<LegacyMaterialTextureExpression>();
        for (int i = 0; i < set.Arrays[2].Length; i++)
        {
            var node = set.Arrays[2][i];
            if (node.Type is not ("FMaterialUniformExpressionTexture" or "FMaterialUniformExpressionTextureParameter") ||
                node.Metadata.Length != 2)
                throw new NotSupportedException("Unsupported material texture expression.");
            if (node.Association is not (null or 2) || node.ParameterIndex is not (null or -1))
                throw new NotSupportedException("Layer-associated texture overrides are unsupported.");
            int index = node.Metadata[0], source = node.Metadata[1];
            if (index < 0 || index >= referencedTextures.Count)
                throw new InvalidDataException("Material texture reference index out of range.");
            if (source is < 0 or > 2) throw new NotSupportedException("Unknown material sampler source.");
            string path = referencedTextures[index];
            if (node.ParameterName != null && overrides != null && overrides.TryGetValue(node.ParameterName, out var value))
                path = value;
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Material texture reference could not be resolved.");
            result.Add(new(i, path, index, source, node.ParameterName));
        }
        return result;
    }
}
