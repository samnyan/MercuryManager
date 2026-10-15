using System.Text.RegularExpressions;

namespace MercuryManager.Server;

/// <summary>APPROXIMATION: all cube-array layers share one caller-provided preview cubemap.
/// Does not invent scene ownership, disable lighting, or establish native rendering readiness.</summary>
public static class ParticleCubeArrayPreviewAdapter
{
    public sealed record Result(string Source, string[] Limitations);

    public static Result Adapt(string source, bool fragmentStage)
    {
        var declarations = Regex.Matches(source,
            @"\buniform\s+(?:(?:highp|mediump|lowp)\s+)?samplerCubeArray\s+(\w+)\s*;");
        if (declarations.Count == 0) return new(source, []);
        const string helper = "mercuryPreviewCubeArray";
        if (source.Contains(helper, StringComparison.Ordinal))
            throw new NotSupportedException("Cube-array preview helper name collision.");
        var limitations = new List<string>();
        var wrappers = new List<string>();
        foreach (Match declaration in declarations)
        {
            string name = declaration.Groups[1].Value;
            source = source.Replace(declaration.Value,
                declaration.Value.Replace("samplerCubeArray", "samplerCube", StringComparison.Ordinal), StringComparison.Ordinal);
            // Only direct texture/textureLod calls are accepted. Overloads preserve bias/LOD;
            // shadow/gather/gradient/size operations require their own adapters.
            var uses = Regex.Matches(source, @"\b" + Regex.Escape(name) + @"\b");
            var calls = Regex.Matches(source, @"\b(textureLod|texture)\s*\(\s*" + Regex.Escape(name) + @"\s*,");
            if (uses.Count != calls.Count + 1)
                throw new NotSupportedException($"Cube-array preview {name} has an unsupported operation.");
            source = Regex.Replace(source,
                @"\b(textureLod|texture)\s*\(\s*" + Regex.Escape(name) + @"\s*,",
                m => helper + (m.Groups[1].Value == "textureLod" ? "Lod" : "Sample") + "(" + name + ",");
            limitations.Add($"APPROXIMATION: {name} cube-array layers collapse to one preview cubemap; direction and bias/LOD retained. Caller must provide the cubemap; native resource semantics remain unverified.");
        }
        wrappers.Add("highp vec4 " + helper + "Sample(highp samplerCube s, highp vec4 p) { return texture(s, p.xyz); }");
        wrappers.Add("highp vec4 " + helper + "Lod(highp samplerCube s, highp vec4 p, highp float lod) { return textureLod(s, p.xyz, lod); }");
        // Bias texture sampling is only legal in fragment shaders.
        if (fragmentStage)
            wrappers.Add("highp vec4 " + helper + "Sample(highp samplerCube s, highp vec4 p, highp float bias) { return texture(s, p.xyz, bias); }");
        int main = source.IndexOf("void main()", StringComparison.Ordinal);
        if (main < 0) throw new NotSupportedException("Cube-array preview requires a canonical main function.");
        source = source.Insert(main, string.Join("\n", wrappers) + "\n\n");
        // ES300 cannot enable the cube-array extension after its last use was lowered.
        source = Regex.Replace(source, @"(?m)^\s*#extension GL_(?:EXT|OES)_texture_cube_map_array\s*:[^\r\n]*", "");
        return new(source, limitations.ToArray());
    }
}
