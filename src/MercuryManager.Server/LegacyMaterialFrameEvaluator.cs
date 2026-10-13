using System.Buffers.Binary;

namespace MercuryManager.Server;

public sealed record LegacyFrameConstantWrite(ushort BufferIndex, ushort ByteOffset, byte[] Data);

public static class LegacyMaterialFrameEvaluator
{
    // 每帧字段不在 Material UBO 中；保留各自的缓冲区和字节位置供前端上传。
    public static IReadOnlyList<LegacyFrameConstantWrite> Evaluate(LegacyUniformSet set,
        LegacySlatePixelBindings bindings, MaterialUniformContext current, MaterialUniformContext previous)
    {
        if (set.Arrays.Length != 9 || bindings.FrameBindings.Length != 4)
            throw new InvalidDataException("Invalid per-frame expression layout.");
        var writes = new List<LegacyFrameConstantWrite>();
        for (int group = 0; group < 4; group++)
        {
            var expressions = set.Arrays[group + 5];
            var slots = bindings.FrameBindings[group];
            if (expressions.Length != slots.Length)
                throw new InvalidDataException("Per-frame expression binding count mismatch.");
            int width = group % 2 == 0 ? 4 : 16;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.NumBytes == 0) continue;
                if (slot.NumBytes != width || slot.BaseIndex % 4 != 0)
                    throw new NotSupportedException("Unsupported per-frame constant size/alignment.");
                var value = MaterialUniformEvaluator.Evaluate(expressions[i], group < 2 ? current : previous);
                var bytes = new byte[width];
                for (int channel = 0; channel < width / 4; channel++)
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(channel * 4, 4), BitConverter.SingleToInt32Bits(value[channel]));
                if (writes.Any(w => w.BufferIndex == slot.BufferIndex &&
                    w.ByteOffset < slot.BaseIndex + width && slot.BaseIndex < w.ByteOffset + w.Data.Length))
                    throw new InvalidDataException("Overlapping per-frame constant bindings.");
                writes.Add(new(slot.BufferIndex, slot.BaseIndex, bytes));
            }
        }
        return writes;
    }
}
