using System.Text.RegularExpressions;
namespace MercuryManager.Server;

/// <summary>Linear buffer texels stored row-major in a 2D texture. Caller supplies identical typed texels.</summary>
public static class ParticleBufferTextureAdapter
{
    public static ParticleCubeArrayPreviewAdapter.Result Adapt(string source)
    {
        var declarations = Regex.Matches(source, @"\buniform\s+highp\s+(u?samplerBuffer)\s+(\w+)\s*;");
        if (declarations.Count == 0) return new(source, []);
        const string helper = "mercuryBufferFetch";
        if (source.Contains(helper, StringComparison.Ordinal)) throw new NotSupportedException("Buffer helper collision.");
        var functions = new List<string>();
        var notes = new List<string>();
        foreach (Match d in declarations)
        {
            bool unsigned = d.Groups[1].Value == "usamplerBuffer";
            string name = d.Groups[2].Value, sampler = unsigned ? "usampler2D" : "sampler2D";
            var calls = Regex.Matches(source, @"\btexelFetch\s*\(\s*" + Regex.Escape(name) + @"\s*,");
            if (Regex.Matches(source, @"\b" + Regex.Escape(name) + @"\b").Count != calls.Count + 1)
                throw new NotSupportedException("Unsupported buffer operation: " + name);
            source = source.Replace(d.Value, "uniform highp " + sampler + " " + name + ";", StringComparison.Ordinal);
            source = Regex.Replace(source, @"\btexelFetch\s*\(\s*" + Regex.Escape(name) + @"\s*,", helper + "_" + name + "(");
            functions.Add("highp " + (unsigned ? "uvec4" : "vec4") + " " + helper + "_" + name + "(int i) { int w = textureSize(" + name + ", 0).x; return texelFetch(" + name + ", ivec2(i % w, i / w), 0); }");
            notes.Add("Buffer texture " + name + " requires typed row-major 2D storage with native texel values; no scene data supplied.");
        }
        int main = source.IndexOf("void main()", StringComparison.Ordinal);
        if (main < 0) throw new NotSupportedException("Buffer adapter requires canonical main.");
        source = source.Insert(main, string.Join("\n", functions) + "\n");
        if (!Regex.IsMatch(source, @"\b[ui]?samplerBuffer\b"))
            source = Regex.Replace(source, @"(?m)^\s*#extension GL_EXT_texture_buffer\s*:[^\r\n]*", "");
        return new(source, notes.ToArray());
    }
}
