namespace MercuryManager.Server;

/// <summary>Native default cutout allocation 0x14179FFA0: 32 zero bytes,
/// SRV stride 8, native format ordinal 17. No guessed browser texture format.</summary>
public static class LegacyParticleCutoutDefaults
{
    public sealed record Input(int NumCutoutVerticesPerFrame, byte[] BufferData,
        int NativeStride, int NativeFormat);

    public static Input CreateDisabled(int numCutoutVerticesPerFrame)
    {
        // The default zero SRV does not contain authored per-frame cutout geometry.
        if (numCutoutVerticesPerFrame != 0)
            throw new NotSupportedException("Authored particle cutout geometry requires its actual per-frame buffer.");
        return new(0, new byte[32], 8, 17);
    }
}
