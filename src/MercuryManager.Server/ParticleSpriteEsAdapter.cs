using System.Text.RegularExpressions;

namespace MercuryManager.Server;

/// <summary>Bounded diagnostic adapter for the verified Sprite cutout-buffer pattern.
/// Caller must bind an explicit base vertex and one-row RG32F texture; never assumes emitter defaults.</summary>
public static class ParticleSpriteEsAdapter
{
    public static string Adapt(string source)
    {
        const string extension = "#ifdef GL_ARB_shader_draw_parameters\n#extension GL_ARB_shader_draw_parameters : enable\n#endif";
        const string baseVertex = "#ifdef GL_ARB_shader_draw_parameters\n#define SPIRV_Cross_BaseVertex gl_BaseVertexARB\n#else\nuniform int SPIRV_Cross_BaseVertex;\n#endif";
        source = source.Replace("\r\n", "\n");
        if (!source.StartsWith("#version 330\n") || Count(source, extension) != 1 || Count(source, baseVertex) != 1 ||
            Regex.Matches(source, @"\bsamplerBuffer\b").Count != 1 ||
            Regex.Matches(source, @"texelFetch\(t0, floatBitsToInt\(r0\.w\)\)\.xy").Count != 1 ||
            Regex.Matches(source, @"\btexelFetch\s*\(").Count != 1 ||
            !source.Contains("gl_VertexID - SPIRV_Cross_BaseVertex"))
            throw new NotSupportedException("Sprite GLSL does not match the verified cutout adapter pattern.");
        var buffer = Regex.Matches(source, @"layout\(binding = \d+\) uniform samplerBuffer t0;");
        if (buffer.Count != 1) throw new NotSupportedException("Sprite cutout declaration is ambiguous.");
        source = source.Replace("#version 330\n", "#version 300 es\nprecision highp float;\nprecision highp int;\n");
        source = source.Replace("#ifdef GL_ARB_shading_language_420pack\n#extension GL_ARB_shading_language_420pack : require\n#endif", "");
        source = source.Replace(extension, "").Replace(baseVertex, "uniform highp int SPIRV_Cross_BaseVertex;");
        source = source.Replace(buffer[0].Value, "uniform highp sampler2D t0;");
        source = source.Replace("texelFetch(t0, floatBitsToInt(r0.w)).xy", "texelFetch(t0, ivec2(floatBitsToInt(r0.w), 0), 0).xy");
        source = Regex.Replace(source, @"layout\(binding = \d+, std140\)", "layout(std140)");
        source = Regex.Replace(source, @"uniform samplerCube\b", "uniform highp samplerCube");
        if (source.Contains("#extension") || source.Contains("samplerBuffer") || source.Contains("gl_BaseVertexARB") ||
            source.Contains("layout(binding"))
            throw new NotSupportedException("Unadapted desktop GLSL feature remains.");
        return source;
    }
    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;
}
