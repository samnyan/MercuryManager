using System.Buffers.Binary;
using UAssetAPI;

namespace MercuryManager.Server;

/// <summary>UE4.19 BasePass traversal to the fog block, traced through native serializers
/// 0x140EBB030, 0x140B10DB0, 0x140EBB4F0, 0x140BBCDB0 and 0x140BBCAB0.</summary>
public static class LegacyBasePassFogLocator
{
    public static int Locate(UAsset asset, byte[] extras, LegacyShaderEntry entry,
        long extrasFileOffset, string expectedFactory)
    {
        bool pixel = entry.Type == "TBasePassPSFNoLightMapPolicy";
        if (!pixel && entry.Type != "TBasePassVSFNoLightMapPolicy")
            throw new NotSupportedException("Unverified BasePass fog serializer.");
        if (entry.StartOffset < 0 || entry.EndOffset > extras.Length || entry.EndOffset < entry.StartOffset)
            throw new InvalidDataException("Invalid BasePass entry bounds.");
        int p = entry.StartOffset;
        void Skip(int n) { if (n < 0 || p > entry.EndOffset - n) throw new InvalidDataException("Truncated BasePass binding block."); p += n; }
        int Int() { int at = p; Skip(4); return BinaryPrimitives.ReadInt32LittleEndian(extras.AsSpan(at)); }
        int Count() { int n = Int(); if (n < 0 || n > 1048576) throw new InvalidDataException("Invalid BasePass count."); return n; }
        void Flag() { int n = Int(); if (n is not (0 or 1)) throw new InvalidDataException("Invalid BasePass binding flag."); }
        void Uniform() { Skip(2); Flag(); }
        Uniform(); // Material.
        int collections = Count(); for (int i = 0; i < collections; i++) Uniform();
        Skip(146 + 8); // Scene parameters and SceneColorCopy resource pair.
        for (int i = 0; i < 6; i++) Count(); // Numeric/frame/texture counts.
        Skip(16); Skip(Count()); // Uniform layout identity and bytes.
        int length = Int();
        if (Math.Abs((long)length) > 1048576) throw new InvalidDataException("Invalid BasePass description length.");
        int stringBytes = checked(length < 0 ? -length * 2 : length);
        Skip(stringBytes);
        if (stringBytes > 0 && (extras[p - 1] != 0 || (length < 0 && extras[p - 2] != 0)))
            throw new InvalidDataException("Unterminated BasePass description.");
        Skip(4); // Eye adaptation resource.
        for (int i = 0; i < 4; i++) Skip(checked(Count() * 6));
        Skip(18); // InstanceCount/InstanceOffset/VertexOffset.
        int index = Int(), number = Int();
        if (index < 0 || index >= asset.GetNameMapIndexList().Count || number != 0 ||
            asset.GetNameReference(index).ToString() != expectedFactory)
            throw new InvalidDataException("BasePass factory envelope identity mismatch.");
        int frequencyAt = p; Skip(1);
        if (extras[frequencyAt] != (pixel ? 3 : 0)) throw new InvalidDataException("BasePass factory frequency mismatch.");
        Skip(20); // Factory shader hash.
        long factoryEnd = (long)Int() - extrasFileOffset;
        if (factoryEnd < p || factoryEnd > entry.EndOffset)
            throw new InvalidDataException("Invalid factory parameter end offset.");
        // Factory-specific payload is explicitly bounded by the native serialized end offset.
        p = (int)factoryEnd;
        Skip(6); // Mesh-material constant binding.
        Uniform(); // PrecomputedLightingBuffer.
        if (pixel) Skip(134 + 66); // Native 0x140BD5520 and 0x140BD5A70 blocks.
        if (p > entry.EndOffset - 70) throw new InvalidDataException("Truncated BasePass fog block.");
        return p;
    }
}
