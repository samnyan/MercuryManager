using System.Numerics;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace MercuryManager.Server;

public sealed record LegacyMaterialPreviewSource(UAsset Asset, NormalExport Material, string Path,
    MaterialUniformContext Context, string[] ParentChain, IReadOnlyDictionary<string, string> TextureOverrides);

public static class LegacyMaterialInstanceResolver
{
    public static LegacyMaterialPreviewSource Resolve(string path, Func<string, string> resolveDependency,
        MaterialUniformContext overrides)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<string>();
        var scalars = new Dictionary<string, float>(StringComparer.Ordinal);
        var vectors = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        var instances = new List<(UAsset Asset, NormalExport Export)>();
        UAsset asset;
        NormalExport material;
        while (true)
        {
            if (chain.Count >= 32 || !active.Add(System.IO.Path.GetFullPath(path)))
                throw new InvalidDataException("Cyclic or excessively deep Material parent chain.");
            chain.Add(path);
            asset = new UAsset(path, UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
            var masters = asset.Exports.OfType<NormalExport>().Where(e => e.GetExportClassType().ToString() == "Material").ToArray();
            if (masters.Length == 1) { material = masters[0]; break; }
            var instance = asset.Exports.OfType<NormalExport>().SingleOrDefault(e => e.GetExportClassType().ToString() == "MaterialInstanceConstant")
                ?? throw new NotSupportedException("Material preview requires a master or constant instance.");
            if (instance.Data.OfType<BoolPropertyData>().Any(p => p.Name.ToString() == "bHasStaticPermutationResource" && p.Value))
                throw new NotSupportedException("Instance static shader permutations are unsupported.");
            foreach (var field in new[] { "StaticParameters" })
                if (instance.Data.FirstOrDefault(p => p.Name.ToString() == field) is { } value &&
                    (value is not ArrayPropertyData array || array.Value.Length != 0))
                    throw new NotSupportedException("Instance texture/static overrides require separate shader resource support.");
            if (instance.Data.FirstOrDefault(p => p.Name.ToString() == "BasePropertyOverrides") is StructPropertyData properties &&
                properties.Value.OfType<BoolPropertyData>().Any(p => p.Name.ToString().StartsWith("bOverride_", StringComparison.Ordinal) && p.Value))
                throw new NotSupportedException("Instance base-property overrides require permutation/blend validation.");
            instances.Add((asset, instance));
            var parent = instance.Data.OfType<ObjectPropertyData>().SingleOrDefault(p => p.Name.ToString() == "Parent")
                ?? throw new InvalidDataException("Material instance has no parent.");
            var reference = GenericResourceViewer.ResolveReference(parent.Value.Index, asset);
            var dependency = reference["ContentFile"]?.ToString()
                ?? throw new NotSupportedException("Material parent cannot be resolved through the project overlay.");
            path = resolveDependency(dependency);
        }
        foreach (var (instanceAsset, instance) in instances.AsEnumerable().Reverse())
        {
            Read(instance, "TextureParameterValues", (name, value) =>
            {
                if (value is not ObjectPropertyData reference || reference.Value.Index == 0)
                    throw new NotSupportedException("Null instance texture overrides require native fallback validation.");
                var target = GenericResourceViewer.ResolveReference(reference.Value.Index, instanceAsset);
                string dependency = target["ContentFile"]?.ToString()
                    ?? throw new NotSupportedException("Instance texture cannot be resolved through the project overlay.");
                var textureAsset = new UAsset(resolveDependency(dependency), UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_19);
                if (!textureAsset.Exports.OfType<NormalExport>().Any(e => e.GetExportClassType().ToString() == "Texture2D"))
                    throw new NotSupportedException("Only Texture2D instance overrides are supported.");
                textures[name] = dependency;
            });
            Read(instance, "ScalarParameterValues", (name, value) =>
            {
                if (value is not FloatPropertyData number || !float.IsFinite(number.Value))
                    throw new InvalidDataException("Invalid instance scalar value.");
                scalars[name] = number.Value;
            });
            Read(instance, "VectorParameterValues", (name, value) =>
            {
                if (value is not StructPropertyData color || color.Value.SingleOrDefault() is not LinearColorPropertyData rgba)
                    throw new NotSupportedException("Unsupported instance color layout.");
                var v = new Vector4(rgba.Value.R, rgba.Value.G, rgba.Value.B, rgba.Value.A);
                if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W))
                    throw new InvalidDataException("Invalid instance color value.");
                vectors[name] = v;
            });
        }
        foreach (var value in overrides.Scalars) scalars[value.Key] = value.Value;
        foreach (var value in overrides.Vectors) vectors[value.Key] = value.Value;
        return new(asset, material, path, new(scalars, vectors, overrides.Time, overrides.RealTime), chain.ToArray(), textures);
    }

    private static void Read(NormalExport instance, string field, Action<string, PropertyData> accept)
    {
        var property = instance.Data.FirstOrDefault(p => p.Name.ToString() == field);
        if (property == null) return;
        if (property is not ArrayPropertyData array || array.Value.Length > 128)
            throw new InvalidDataException("Invalid instance parameter array.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in array.Value)
        {
            if (entry is not StructPropertyData item || item.Value.FirstOrDefault(p => p.Name.ToString() == "ParameterInfo") is not StructPropertyData info)
                throw new NotSupportedException("Unsupported instance parameter identity.");
            var association = info.Value.OfType<BytePropertyData>().FirstOrDefault(p => p.Name.ToString() == "Association");
            var index = info.Value.OfType<IntPropertyData>().FirstOrDefault(p => p.Name.ToString() == "Index");
            if ((association != null && association.EnumValue?.ToString()?.Split("::").Last() != "GlobalParameter") || (index != null && index.Value != -1))
                throw new NotSupportedException("Layer-associated instance parameters are unsupported.");
            string name = info.Value.FirstOrDefault(p => p.Name.ToString() == "Name")?.RawValue?.ToString()
                ?? throw new InvalidDataException("Missing instance parameter name.");
            if (name.Length is 0 or > 256 || !names.Add(name)) throw new InvalidDataException("Invalid/duplicate instance parameter name.");
            accept(name, item.Value.SingleOrDefault(p => p.Name.ToString() == "ParameterValue")
                ?? throw new InvalidDataException("Missing instance parameter value."));
        }
    }
}
