using System.Text.RegularExpressions;
namespace MercuryManager.Server;

/// <summary>GLSL ES300 scalar unsigned bitfield operations used by translated DXBC.</summary>
public static class ParticleBitfieldAdapter
{
    public static string Adapt(string source)
    {
        if (!Regex.IsMatch(source, @"\bbitfield(?:Extract|Insert)\s*\(")) return source;
        const string prefix = "mercuryBitfield";
        if (source.Contains(prefix, StringComparison.Ordinal)) throw new NotSupportedException("Bitfield helper collision.");
        source = Regex.Replace(source, @"\bbitfieldExtract\s*\(", prefix + "Extract(");
        source = Regex.Replace(source, @"\bbitfieldInsert\s*\(", prefix + "Insert(");
        const string functions = """
            highp uint mercuryBitfieldMask(int bits) { return bits == 32 ? 0xffffffffu : ((1u << uint(bits)) - 1u); }
            highp uint mercuryBitfieldExtract(highp uint value, int offset, int bits) {
                if (bits == 0) return 0u;
                return (value >> uint(offset)) & mercuryBitfieldMask(bits);
            }
            highp uint mercuryBitfieldInsert(highp uint baseValue, highp uint insertValue, int offset, int bits) {
                if (bits == 0) return baseValue;
                highp uint mask = mercuryBitfieldMask(bits) << uint(offset);
                return (baseValue & ~mask) | ((insertValue << uint(offset)) & mask);
            }

            """;
        int main = source.IndexOf("void main()", StringComparison.Ordinal);
        if (main < 0) throw new NotSupportedException("Bitfield adapter requires canonical main.");
        return source.Insert(main, functions);
    }
}
