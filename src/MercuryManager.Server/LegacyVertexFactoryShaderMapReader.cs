using System.Buffers.Binary;
using UAssetAPI;

namespace MercuryManager.Server;

public sealed record LegacyShaderPipeline(string Name, IReadOnlyList<LegacyShaderEntry> Shaders);
public sealed record LegacyVertexFactoryShaderMap(string VertexFactory, IReadOnlyList<LegacyShaderEntry> Shaders)
{
    public IReadOnlyList<LegacyShaderPipeline> Pipelines { get; init; } = [];
}

/// <summary>UE4.19 vertex-factory maps following the global material shader directory.
/// Does not consume the trailing material metadata or enable a rendering profile.</summary>
public sealed class LegacyVertexFactoryShaderMapReader(UAsset asset, byte[] data, int offset)
{
    public int Position { get; private set; } = offset;

    public LegacyVertexFactoryShaderMap[] Read(long extrasFileOffset)
    {
        int count = Count(256);
        var maps = new LegacyVertexFactoryShaderMap[count];
        var factories = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string factory = Name();
            if (!factories.Add(factory)) throw new InvalidDataException("Duplicate vertex factory shader map.");
            int shaderCount = Count(4096);
            var entries = new LegacyShaderEntry[shaderCount];
            for (int j = 0; j < shaderCount; j++)
            {
                string type = Name();
                long end = (long)Int() - extrasFileOffset;
                if (end < Position || end > data.Length)
                    throw new InvalidDataException("Vertex factory shader entry boundary out of range.");
                entries[j] = new(type, Position, (int)end);
                Position = (int)end;
            }
            var pipelines = new LegacyShaderPipeline[Count(256)];
            var pipelineNames = new HashSet<string>(StringComparer.Ordinal);
            for (int p = 0; p < pipelines.Length; p++)
            {
                var name = Name();
                if (!pipelineNames.Add(name)) throw new InvalidDataException("Duplicate shader pipeline.");
                var stages = new LegacyShaderEntry[Count(6)];
                if (stages.Length == 0) throw new InvalidDataException("Empty shader pipeline.");
                var stageTypes = new HashSet<string>(StringComparer.Ordinal);
                for (int s = 0; s < stages.Length; s++)
                {
                    var type = Name();
                    if (!stageTypes.Add(type)) throw new InvalidDataException("Duplicate shader pipeline stage type.");
                    long end = (long)Int() - extrasFileOffset;
                    if (end <= Position || end > data.Length)
                        throw new InvalidDataException("Shader pipeline stage boundary out of range.");
                    stages[s] = new(type, Position, (int)end);
                    Position = (int)end;
                }
                pipelines[p] = new(name, stages);
            }
            // Pipeline shaders retain their identity; never merge into standalone candidates.
            maps[i] = new(factory, entries) { Pipelines = pipelines };
        }
        return maps;
    }

    private int Count(int limit)
    {
        int count = Int();
        if (count < 0 || count > limit) throw new InvalidDataException("Vertex factory shader map count out of range.");
        return count;
    }
    private string Name()
    {
        int index = Int(), number = Int();
        if (index < 0 || index >= asset.GetNameMapIndexList().Count || number < 0)
            throw new InvalidDataException("Invalid vertex factory shader FName.");
        string name = asset.GetNameReference(index).ToString();
        return number == 0 ? name : $"{name}_{number - 1}";
    }
    private int Int()
    {
        if (Position < 0 || Position > data.Length - 4) throw new InvalidDataException("Truncated vertex factory shader map.");
        int value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Position));
        Position += 4;
        return value;
    }
}
