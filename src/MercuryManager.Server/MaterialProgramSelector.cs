namespace MercuryManager.Server;

/// <summary>Exact serialized identities only; selection does not validate bindings or enable rendering.</summary>
public static class MaterialProgramSelector
{
    public sealed record VertexFragmentPair(
        MaterialShaderCompiler.ShaderCandidate Vertex,
        MaterialShaderCompiler.ShaderCandidate Fragment);

    public static VertexFragmentPair SelectVertexFactoryPair(
        IReadOnlyList<MaterialShaderCompiler.ShaderCandidate> candidates,
        IReadOnlyList<LegacyVertexFactoryShaderMap> maps,
        string vertexFactory, string vertexType, string fragmentType)
    {
        var matches = maps.Where(m => m.VertexFactory == vertexFactory).ToArray();
        if (matches.Length != 1)
            throw new NotSupportedException("Expected one exact vertex factory shader map.");
        var map = matches[0];
        var vertex = Select(candidates, map.Shaders, vertexType, "vertex");
        var fragment = Select(candidates, map.Shaders, fragmentType, "fragment");
        return new(vertex, fragment);
    }

    private static MaterialShaderCompiler.ShaderCandidate Select(
        IReadOnlyList<MaterialShaderCompiler.ShaderCandidate> candidates,
        IReadOnlyList<LegacyShaderEntry> entries, string type, string stage)
    {
        var matches = entries.Where(e => e.Type == type).ToArray();
        if (matches.Length != 1)
            throw new NotSupportedException("Expected one exact shader permutation within the selected factory.");
        var entry = matches[0];
        if (entry.StartOffset < 0 || entry.EndOffset <= entry.StartOffset)
            throw new InvalidDataException("Invalid shader directory boundary.");
        var programs = candidates.Where(c => c.Offset >= entry.StartOffset && c.Offset < entry.EndOffset).ToArray();
        if (programs.Length != 1 || programs[0].Stage != stage)
            throw new NotSupportedException($"Selected {type} entry does not contain one independently identified inline {stage} program.");
        return programs[0];
    }
}
