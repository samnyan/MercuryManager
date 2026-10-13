using System.Buffers.Binary;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace MercuryManager.Server;

public sealed record LegacyMaterialCollectionInput(int BufferIndex, byte[] Data, string AssetPath, string ValueSource);

public static class LegacyMaterialCollectionInputs
{
    // 预览使用资源默认值，不冒充游戏 World 中由逻辑更新的 collection instance。
    public static IReadOnlyList<LegacyMaterialCollectionInput> Evaluate(UAsset asset, NormalExport material,
        LegacyUniformSet set, LegacySlatePixelBindings bindings, string reflection, Func<string, string> resolve)
    {
        if (set.Collections.Length != bindings.CollectionBindings.Length)
            throw new InvalidDataException("Collection expression and binding counts disagree.");
        var infos = material.Data.FirstOrDefault(p => p.Name.ToString() == "MaterialParameterCollectionInfos") as ArrayPropertyData;
        using var doc = JsonDocument.Parse(reflection);
        var ubos = doc.RootElement.GetProperty("ubos").EnumerateArray().ToArray();
        var result = new List<LegacyMaterialCollectionInput>();
        for (int i = 0; i < set.Collections.Length; i++)
        {
            var binding = bindings.CollectionBindings[i];
            if (!binding.Bound) continue;
            var matches = infos?.Value.OfType<StructPropertyData>().Where(p => State(p.Value) == set.Collections[i]).ToArray() ?? [];
            if (matches.Length != 1) throw new NotSupportedException("Collection StateId cannot be uniquely resolved.");
            var reference = matches[0].Value.OfType<ObjectPropertyData>().SingleOrDefault(p => p.Name.ToString() == "ParameterCollection")
                ?? throw new InvalidDataException("Missing collection reference.");
            string path = GenericResourceViewer.ResolveReference(reference.Value.Index, asset)["ContentFile"]?.ToString()
                ?? throw new NotSupportedException("Collection asset path cannot be resolved.");
            var dependency = new UAsset(resolve(path), EngineVersion.VER_UE4_19);
            var collection = dependency.Exports.OfType<NormalExport>().SingleOrDefault(e => e.GetExportClassType().ToString() == "MaterialParameterCollection")
                ?? throw new InvalidDataException("Referenced asset is not a material parameter collection.");
            if (State(collection.Data) != set.Collections[i])
                throw new NotSupportedException("Collection StateId differs from the compiled material layout.");
            var reflected = ubos.Where(u => u.GetProperty("binding").GetInt32() == binding.BufferIndex).ToArray();
            if (reflected.Length != 1 || binding.BufferIndex == bindings.GammaValues.BufferIndex ||
                (bindings.Material.MaterialBufferBound && binding.BufferIndex == bindings.Material.MaterialBufferIndex) ||
                result.Any(x => x.BufferIndex == binding.BufferIndex))
                throw new InvalidDataException("Collection buffer is not uniquely identified.");
            var data = PackDefaults(collection);
            var ubo = reflected[0];
            var members = doc.RootElement.GetProperty("types").GetProperty(ubo.GetProperty("type").GetString()!).GetProperty("members");
            if (members.GetArrayLength() != 1 || members[0].GetProperty("type").GetString() != "vec4" ||
                members[0].GetProperty("offset").GetInt32() != 0 || members[0].GetProperty("array_stride").GetInt32() != 16 ||
                members[0].GetProperty("array")[0].GetInt32() * 16 != ubo.GetProperty("block_size").GetInt32() ||
                ubo.GetProperty("block_size").GetInt32() > data.Length)
                throw new NotSupportedException("Collection reflected layout differs from the native vec4 layout.");
            // 编译器可裁掉末尾未访问参数，上传反射所需的原始前缀。
            data = data[..ubo.GetProperty("block_size").GetInt32()];
            result.Add(new(binding.BufferIndex, data, path, "asset-defaults"));
        }
        return result;
    }

    private static Guid? State(IEnumerable<PropertyData> properties) =>
        (properties.FirstOrDefault(p => p.Name.ToString() == "StateId") as StructPropertyData)?.Value.OfType<GuidPropertyData>().SingleOrDefault()?.Value;

    public static byte[] PackDefaults(NormalExport collection)
    {
        StructPropertyData[] Parameters(string field)
        {
            var property = collection.Data.FirstOrDefault(p => p.Name.ToString() == field);
            if (property == null) return [];
            if (property is not ArrayPropertyData array || array.Value.Length > 1024 || array.Value.Any(p => p is not StructPropertyData))
                throw new InvalidDataException("Invalid collection parameter array.");
            return array.Value.Cast<StructPropertyData>().ToArray();
        }
        var scalars = Parameters("ScalarParameters");
        var vectors = Parameters("VectorParameters");
        int vectorOffset = ((scalars.Length + 3) / 4) * 16;
        var data = new byte[vectorOffset + vectors.Length * 16];
        void Write(int offset, float value)
        {
            if (!float.IsFinite(value)) throw new InvalidDataException("Collection default must be finite.");
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset, 4), value);
        }
        for (int i = 0; i < scalars.Length; i++)
        {
            var property = scalars[i].Value.FirstOrDefault(p => p.Name.ToString() == "DefaultValue");
            if (property != null && property is not FloatPropertyData)
                throw new NotSupportedException("Unsupported collection scalar layout.");
            Write(i * 4, (property as FloatPropertyData)?.Value ?? 0);
        }
        for (int i = 0; i < vectors.Length; i++)
        {
            var property = vectors[i].Value.FirstOrDefault(p => p.Name.ToString() == "DefaultValue");
            if (property == null) continue; // Native collection constructor default is zero.
            if (property is not StructPropertyData color || color.Value.SingleOrDefault() is not LinearColorPropertyData value)
                throw new NotSupportedException("Unsupported collection color layout.");
            Write(vectorOffset + i * 16, value.Value.R);
            Write(vectorOffset + i * 16 + 4, value.Value.G);
            Write(vectorOffset + i * 16 + 8, value.Value.B);
            Write(vectorOffset + i * 16 + 12, value.Value.A);
        }
        return data;
    }
}
