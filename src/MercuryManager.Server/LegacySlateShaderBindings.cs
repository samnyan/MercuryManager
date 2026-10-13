using System.Buffers.Binary;

namespace MercuryManager.Server;

public sealed record LegacyConstantBinding(ushort BaseIndex, ushort NumBytes, ushort BufferIndex);
public sealed record LegacyResourceBinding(ushort BaseIndex, ushort NumResources);
public sealed record LegacyUniformBufferBinding(ushort BufferIndex, bool Bound);
public sealed record LegacyMaterialShaderBindings(ushort MaterialBufferIndex, bool MaterialBufferBound,
    int VectorCount, int ScalarCount, int Texture2DCount, int TextureCubeCount, int PerFrameScalarCount, int PerFrameVectorCount);
public sealed record LegacySlatePixelBindings(LegacyMaterialShaderBindings Material,
    LegacyConstantBinding GammaValues, LegacyConstantBinding ShaderParams,
    LegacyResourceBinding ElementTextureSampler, LegacyResourceBinding ElementTexture)
{
    public LegacyUniformBufferBinding[] CollectionBindings { get; init; } = [];
    public LegacyConstantBinding[][] FrameBindings { get; init; } = [[], [], [], []];
}

public static class LegacySlateShaderBindings
{
    // 从虚序列化入口读取 Material 基类，再读取 Slate 字段；资源字节码位于其后，不是条目尾部。
    public static LegacySlatePixelBindings ReadDefaultFalse(byte[] extras, LegacyShaderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Type != "TSlateMaterialShaderPSDefaultfalse")
            throw new NotSupportedException($"Slate binding layout is not verified for {entry.Type}.");
        if (entry.StartOffset < 0 || entry.EndOffset > extras.Length || entry.EndOffset < entry.StartOffset)
            throw new InvalidDataException("Invalid Slate shader entry boundaries.");
        var reader = new BindingReader(extras, entry.StartOffset, entry.EndOffset);
        ushort materialIndex = reader.U16();
        int bound = reader.Int();
        if (bound is not (0 or 1)) throw new InvalidDataException("Invalid material buffer binding flag.");
        var collections = new LegacyUniformBufferBinding[reader.Count()];
        for (int i = 0; i < collections.Length; i++)
        {
            ushort slot = reader.U16();
            int collectionBound = reader.Int();
            if (collectionBound is not (0 or 1)) throw new InvalidDataException("Invalid collection buffer binding flag.");
            collections[i] = new(slot, collectionBound != 0);
        }
        reader.Skip(146); // Scene texture and uniform-buffer bindings.
        reader.Skip(8); // SceneColorCopy texture/sampler.
        int vectors = reader.Count(), scalars = reader.Count(), frameScalars = reader.Count(), frameVectors = reader.Count();
        int textures = reader.Count(), cubes = reader.Count();
        reader.Skip(16); // Uniform layout name and two int32 fields.
        reader.Array(1); // Uniform layout bytes.
        reader.String(); // Material description.
        reader.Skip(4); // EyeAdaptation resource.
        var frames = new LegacyConstantBinding[4][];
        for (int i = 0; i < 4; i++)
        {
            frames[i] = new LegacyConstantBinding[reader.Count()];
            for (int j = 0; j < frames[i].Length; j++) frames[i][j] = reader.Constant();
        }
        reader.Skip(18); // InstanceCount, InstanceOffset, VertexOffset.
        return new(new(materialIndex, bound != 0, vectors, scalars, textures, cubes, frameScalars, frameVectors),
            reader.Constant(), reader.Constant(), reader.Resource(), reader.Resource()) { FrameBindings = frames, CollectionBindings = collections };
    }

    private sealed class BindingReader(byte[] data, int start, int end)
    {
        private int position = start;
        public void Skip(int count)
        {
            if (count < 0 || position > end - count) throw new InvalidDataException("Truncated Slate binding data.");
            position += count;
        }
        public int Int()
        {
            int at = position; Skip(4); return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at, 4));
        }
        public ushort U16()
        {
            int at = position; Skip(2); return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at, 2));
        }
        public int Count()
        {
            int count = Int();
            if (count < 0 || count > 1048576) throw new InvalidDataException("Shader binding count exceeds limits.");
            return count;
        }
        public void Array(int stride)
        {
            int count = Int();
            if (count < 0 || count > 1048576) throw new InvalidDataException("Shader binding array exceeds limits.");
            Skip(checked(count * stride));
        }
        public void String()
        {
            int count = Int();
            if (Math.Abs((long)count) > 1048576) throw new InvalidDataException("Shader description exceeds limits.");
            Skip(checked(count < 0 ? -count * 2 : count));
        }
        public LegacyConstantBinding Constant() => new(U16(), U16(), U16());
        public LegacyResourceBinding Resource() => new(U16(), U16());
    }
}
