using System.Buffers.Binary;

namespace MercuryManager.Server;

public sealed record LegacyFogBindings(IReadOnlyList<LegacyConstantBinding> Constants,
    LegacyResourceBinding InscatteringCubemap, LegacyResourceBinding InscatteringSampler,
    LegacyResourceBinding IntegratedLightScattering, LegacyResourceBinding IntegratedLightScatteringSampler);

/// <summary>Native UE4.19 fog block only; caller must prove its serialized offset via the shader serializer.
/// Source: Mercury 0x140C603B0 (serialization), 0x140C61EB0 (named binding).
/// Does not locate the block by scanning or provide substitute scene textures.</summary>
public static class LegacyFogShaderBindings
{
    public static LegacyFogBindings Read(byte[] extras, LegacyShaderEntry entry, int blockOffset)
    {
        if (entry.Type is not ("TBasePassVSFNoLightMapPolicy" or "TBasePassPSFNoLightMapPolicy"))
            throw new NotSupportedException("Fog layout is not verified for this shader type.");
        if (entry.StartOffset < 0 || entry.EndOffset > extras.Length ||
            blockOffset < entry.StartOffset || blockOffset > entry.EndOffset - 70)
            throw new InvalidDataException("Fog binding block exceeds shader entry.");
        int position = blockOffset;
        ushort U16() { var value = BinaryPrimitives.ReadUInt16LittleEndian(extras.AsSpan(position, 2)); position += 2; return value; }
        LegacyConstantBinding Constant() => new(U16(), U16(), U16());
        LegacyResourceBinding Resource() => new(U16(), U16());
        var constants = new List<LegacyConstantBinding>();
        for (int i = 0; i < 4; i++) constants.Add(Constant());
        var cube = Resource();
        var cubeSampler = Resource();
        for (int i = 0; i < 5; i++) constants.Add(Constant());
        return new(constants, cube, cubeSampler, Resource(), Resource());
    }
}
