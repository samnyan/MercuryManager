namespace MercuryManager.Server;

public sealed record MaterialRuntimeWrite(int BufferIndex, int ByteOffset, int Width, bool Previous, LegacyUniformNode Expression);
public sealed record MaterialRuntimeProgram(int Version, IReadOnlyList<MaterialRuntimeWrite> Writes,
    IReadOnlyDictionary<string, float> Scalars, IReadOnlyDictionary<string, float[]> Vectors);

public static class MaterialRuntimeProgramBuilder
{
    public static MaterialRuntimeProgram Build(LegacyUniformSet set, LegacySlatePixelBindings bindings,
        MaterialUniformContext context, int materialBytes)
        => Build(set, bindings.Material, bindings.FrameBindings, context, materialBytes);

    public static MaterialRuntimeProgram Build(LegacyUniformSet set, LegacyMaterialShaderBindings material,
        LegacyConstantBinding[][] frameBindings, MaterialUniformContext context, int materialBytes)
    {
        if (set.Arrays.Length != 9 || frameBindings.Length != 4 || materialBytes < 0 ||
            (!material.MaterialBufferBound && materialBytes != 0))
            throw new InvalidDataException("Invalid runtime Material binding layout.");
        for (int group = 0; group < 4; group++)
            if (frameBindings[group].Length != set.Arrays[group + 5].Length)
                throw new InvalidDataException("Runtime frame expression count mismatch.");
        var writes = new List<MaterialRuntimeWrite>();
        for (int i = 0; i < set.Arrays[0].Length; i++)
            if (i * 16 + 16 <= materialBytes)
                writes.Add(new(material.MaterialBufferIndex, i * 16, 16, false, set.Arrays[0][i]));
        for (int i = 0; i < set.Arrays[1].Length; i++)
        {
            int offset = set.Arrays[0].Length * 16 + i * 4;
            if (offset + 4 <= materialBytes)
                writes.Add(new(material.MaterialBufferIndex, offset, 4, false, set.Arrays[1][i]));
        }
        for (int group = 0; group < 4; group++)
        for (int i = 0; i < frameBindings[group].Length; i++)
        {
            var slot = frameBindings[group][i];
            if (slot.NumBytes != 0)
                writes.Add(new(slot.BufferIndex, slot.BaseIndex, slot.NumBytes, group >= 2, set.Arrays[group + 5][i]));
        }
        int nodes = 0;
        void Validate(LegacyUniformNode node, int depth)
        {
            if (++nodes > 32768 || depth > 64) throw new NotSupportedException("Material runtime expression budget exceeded.");
            _ = MaterialUniformEvaluator.Evaluate(node, context);
            foreach (var child in node.Children) Validate(child, depth + 1);
        }
        foreach (var write in writes) Validate(write.Expression, 0);
        return new(1, writes, context.Scalars, context.Vectors.ToDictionary(p => p.Key,
            p => new[] { p.Value.X, p.Value.Y, p.Value.Z, p.Value.W }, StringComparer.Ordinal));
    }
}
