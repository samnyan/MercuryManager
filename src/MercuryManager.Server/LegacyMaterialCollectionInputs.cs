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
    public static IReadOnlyDictionary<string, MaterialUniformContext> ParseOverrides(string? text)
    {
        var result = new Dictionary<string, MaterialUniformContext>(StringComparer.Ordinal);
        if (text == null) return result;
        if (text.Length > 65536) throw new ArgumentException("Collection overrides exceed limits.");
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 5 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Collection overrides must be an object keyed by asset path.");
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                string path = property.Name;
                if (result.Count >= 32 || path.Length is 0 or > 512 || path.StartsWith('/') || path.Contains('\\') ||
                    path.Split('/').Any(segment => segment is "" or "." or "..") || !path.EndsWith(".uasset", StringComparison.Ordinal))
                    throw new ArgumentException("Invalid collection override asset path.");
                if (property.Value.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Collection override values must be objects.");
                string? scalars = null, vectors = null;
                var fields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in property.Value.EnumerateObject())
                {
                    if (!fields.Add(field.Name)) throw new ArgumentException("Duplicate collection override field.");
                    if (field.Name == "scalars") scalars = field.Value.GetRawText();
                    else if (field.Name == "vectors") vectors = field.Value.GetRawText();
                    else throw new ArgumentException("Unknown collection override field.");
                }
                if (!result.TryAdd(path, MaterialPreviewOverrides.Parse(scalars, vectors, 0, 0)))
                    throw new ArgumentException("Duplicate collection override asset path.");
            }
        }
        catch (JsonException ex) { throw new ArgumentException("Invalid collection override JSON.", ex); }
        return result;
    }
    // 预览使用资源默认值，不冒充游戏 World 中由逻辑更新的 collection instance。
    public static IReadOnlyList<LegacyMaterialCollectionInput> Evaluate(UAsset asset, NormalExport material,
        LegacyUniformSet set, LegacySlatePixelBindings bindings, string reflection, Func<string, string> resolve,
        IReadOnlyDictionary<string, MaterialUniformContext>? overrides = null)
    {
        if (set.Collections.Length != bindings.CollectionBindings.Length)
            throw new InvalidDataException("Collection expression and binding counts disagree.");
        var infos = material.Data.FirstOrDefault(p => p.Name.ToString() == "MaterialParameterCollectionInfos") as ArrayPropertyData;
        using var doc = JsonDocument.Parse(reflection);
        var ubos = doc.RootElement.GetProperty("ubos").EnumerateArray().ToArray();
        var result = new List<LegacyMaterialCollectionInput>();
        var consumed = new HashSet<string>(StringComparer.Ordinal);
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
            MaterialUniformContext? values = null;
            if (overrides?.TryGetValue(path, out values) == true) consumed.Add(path);
            var data = PackDefaults(collection, values);
            var ubo = reflected[0];
            var members = doc.RootElement.GetProperty("types").GetProperty(ubo.GetProperty("type").GetString()!).GetProperty("members");
            if (members.GetArrayLength() != 1 || members[0].GetProperty("type").GetString() != "vec4" ||
                members[0].GetProperty("offset").GetInt32() != 0 || members[0].GetProperty("array_stride").GetInt32() != 16 ||
                members[0].GetProperty("array")[0].GetInt32() * 16 != ubo.GetProperty("block_size").GetInt32() ||
                ubo.GetProperty("block_size").GetInt32() > data.Length)
                throw new NotSupportedException("Collection reflected layout differs from the native vec4 layout.");
            // 编译器可裁掉末尾未访问参数，上传反射所需的原始前缀。
            data = data[..ubo.GetProperty("block_size").GetInt32()];
            result.Add(new(binding.BufferIndex, data, path, values == null ? "asset-defaults" : "preview-overrides"));
        }
        if (overrides?.Keys.Any(path => !consumed.Contains(path)) == true)
            throw new ArgumentException("Collection override does not target a bound collection.");
        return result;
    }

    private static Guid? State(IEnumerable<PropertyData> properties) =>
        (properties.FirstOrDefault(p => p.Name.ToString() == "StateId") as StructPropertyData)?.Value.OfType<GuidPropertyData>().SingleOrDefault()?.Value;

    public static byte[] PackDefaults(NormalExport collection, MaterialUniformContext? overrides = null)
    {
        StructPropertyData[] Parameters(string field)
        {
            var property = collection.Data.FirstOrDefault(p => p.Name.ToString() == field);
            if (property == null) return [];
            if (property is not ArrayPropertyData array || array.Value.Length > 1024 || array.Value.Any(p => p is not StructPropertyData))
                throw new InvalidDataException("Invalid collection parameter array.");
            return array.Value.Cast<StructPropertyData>().ToArray();
        }
        var usedScalars = new HashSet<string>(StringComparer.Ordinal);
        var usedVectors = new HashSet<string>(StringComparer.Ordinal);
        string Name(StructPropertyData parameter) => parameter.Value.OfType<NamePropertyData>()
            .SingleOrDefault(p => p.Name.ToString() == "ParameterName")?.Value.ToString()
            ?? throw new InvalidDataException("Missing collection parameter name.");
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
            float value = (property as FloatPropertyData)?.Value ?? 0;
            string name = Name(scalars[i]);
            if (overrides?.Scalars.TryGetValue(name, out var supplied) == true) { value = supplied; usedScalars.Add(name); }
            Write(i * 4, value);
        }
        for (int i = 0; i < vectors.Length; i++)
        {
            string name = Name(vectors[i]);
            if (overrides?.Vectors.TryGetValue(name, out var supplied) == true)
            {
                usedVectors.Add(name);
                for (int channel = 0; channel < 4; channel++) Write(vectorOffset + i * 16 + channel * 4, supplied[channel]);
                continue;
            }
            var property = vectors[i].Value.FirstOrDefault(p => p.Name.ToString() == "DefaultValue");
            if (property == null) continue; // Native collection constructor default is zero.
            if (property is not StructPropertyData color || color.Value.SingleOrDefault() is not LinearColorPropertyData value)
                throw new NotSupportedException("Unsupported collection color layout.");
            Write(vectorOffset + i * 16, value.Value.R);
            Write(vectorOffset + i * 16 + 4, value.Value.G);
            Write(vectorOffset + i * 16 + 8, value.Value.B);
            Write(vectorOffset + i * 16 + 12, value.Value.A);
        }
        if (overrides != null && (overrides.Scalars.Keys.Any(n => !usedScalars.Contains(n)) ||
            overrides.Vectors.Keys.Any(n => !usedVectors.Contains(n))))
            throw new ArgumentException("Unknown or wrong-type collection override parameter.");
        return data;
    }
}
