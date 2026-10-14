using System.Buffers.Binary;

namespace MercuryManager.Server;

/// <summary>UE4.19 Required Serialize 0x14175AFD0: cooked bool32 followed by
/// FVector2D array, serialized by 0x1417A3120. Does not infer runtime vertex count.</summary>
public static class LegacyParticleCutoutGeometry
{
    public sealed record Geometry(int VertexCount, float[] Coordinates);

    public static Geometry Read(byte[] extras)
    {
        if (extras.Length < 8) throw new InvalidDataException("Truncated cooked cutout geometry header.");
        if (BinaryPrimitives.ReadInt32LittleEndian(extras) != 1)
            throw new NotSupportedException("Required module lacks verified cooked cutout data.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(extras.AsSpan(4));
        if (count < 0 || count > 1048576 || extras.Length != 8L + count * 8L)
            throw new InvalidDataException("Invalid cooked cutout array bounds.");
        var coordinates = new float[count * 2];
        for (int i = 0; i < coordinates.Length; i++)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(extras.AsSpan(8 + i * 4, 4));
            if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite cutout coordinate.");
            coordinates[i] = value;
        }
        return new(count, coordinates);
    }
}
