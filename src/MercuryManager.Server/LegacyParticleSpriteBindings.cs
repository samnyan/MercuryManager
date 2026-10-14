using System.Buffers.Binary;
using UAssetAPI;

namespace MercuryManager.Server;

public sealed record LegacyParticleSpriteBindings(LegacyConstantBinding NumCutoutVerticesPerFrame,
    LegacyResourceBinding CutoutGeometry);

/// <summary>Native FParticleSpriteVertexFactory VS parameters: 0x1417A9710 names,
/// 0x1417BBE20 serializes constant + resource. Does not fabricate cutout geometry.</summary>
public static class LegacyParticleSpriteBindingReader
{
    public static LegacyParticleSpriteBindings Read(UAsset asset, byte[] extras,
        LegacyShaderEntry entry, long extrasFileOffset)
    {
        if (entry.Type != "TBasePassVSFNoLightMapPolicy")
            throw new NotSupportedException("Sprite factory decoder requires the verified BasePass VS.");
        var layout = LegacyBasePassFogLocator.Inspect(asset, extras, entry, extrasFileOffset, "FParticleSpriteVertexFactory");
        if (layout.Frequency != 0 || layout.FactoryPayloadEnd - layout.FactoryPayloadStart != 10)
            throw new NotSupportedException("Unverified Sprite factory parameter layout.");
        int position = layout.FactoryPayloadStart;
        ushort U16() { ushort v = BinaryPrimitives.ReadUInt16LittleEndian(extras.AsSpan(position, 2)); position += 2; return v; }
        return new(new(U16(), U16(), U16()), new(U16(), U16()));
    }
}
