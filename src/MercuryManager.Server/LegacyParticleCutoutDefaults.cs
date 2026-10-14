namespace MercuryManager.Server;

/// <summary>Native default cutout allocation 0x14179FFA0: 32 zero bytes,
/// SRV stride 8, PF_G32R32F (ordinal 17, native enum table 0x1426C0680).</summary>
public static class LegacyParticleCutoutDefaults
{
    public sealed record Input(int NumCutoutVerticesPerFrame, byte[] BufferData,
        int NativeStride, int NativeFormat);

    public sealed record TextureTransport(int Width, int Height, string Format, float[] Data);

    /// <summary>Lossless RG32F 2D transport for the verified disabled default.
    /// Consumers still need a validated samplerBuffer-to-texelFetch shader adapter.</summary>
    public static TextureTransport CreateDisabledTexture(int numCutoutVerticesPerFrame)
    {
        var input = CreateDisabled(numCutoutVerticesPerFrame);
        var values = new float[input.BufferData.Length / sizeof(float)];
        for (int i = 0; i < values.Length; i++)
            values[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                input.BufferData.AsSpan(i * sizeof(float), sizeof(float)));
        return new(values.Length / 2, 1, "RG32F", values);
    }

    public static Input CreateDisabled(int numCutoutVerticesPerFrame)
    {
        // The default zero SRV does not contain authored per-frame cutout geometry.
        if (numCutoutVerticesPerFrame != 0)
            throw new NotSupportedException("Authored particle cutout geometry requires its actual per-frame buffer.");
        return new(0, new byte[32], 8, 17);
    }
}
