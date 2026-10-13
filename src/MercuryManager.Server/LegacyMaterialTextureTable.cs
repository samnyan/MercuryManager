using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;

namespace MercuryManager.Server;

public static class LegacyMaterialTextureTable
{
    public static IReadOnlyList<string> ReadDirect(UAsset asset, NormalExport material) => Read(asset, material, null);

    // 函数依赖先按后序收集，再追加各函数的直接纹理；不能在函数内逐节点混合纹理递归。
    public static IReadOnlyList<string> Read(UAsset asset, NormalExport material, Func<string, UAsset>? load)
    {
        var textures = new List<string>();
        var seenTextures = new HashSet<string>(StringComparer.Ordinal);
        var packages = new Dictionary<string, UAsset>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        int visits = 0;
        string PathOf(UAsset package, int index)
        {
            var reference = GenericResourceViewer.ResolveReference(index, package);
            if (reference["ContentFile"]?.ToString() is string contentPath) return contentPath;
            if (reference["Package"]?.ToString() is string enginePath && enginePath.StartsWith("/Engine/", StringComparison.Ordinal))
                return enginePath + ".uasset";
            throw new NotSupportedException("Unresolved material dependency reference.");
        }
        IEnumerable<NormalExport> Expressions(UAsset package, NormalExport owner, string field)
        {
            var array = owner.Data.OfType<ArrayPropertyData>().SingleOrDefault(p => p.Name.ToString() == field)
                ?? throw new NotSupportedException("Material expression list is unavailable.");
            foreach (var item in array.Value)
            {
                if (++visits > 65536) throw new InvalidDataException("Material dependency traversal exceeds limits.");
                if (item is not ObjectPropertyData reference) throw new InvalidDataException("Invalid expression reference.");
                int index = reference.Value.Index;
                if (index == 0) continue;
                if (index < 0 || index > package.Exports.Count || package.Exports[index - 1] is not NormalExport expression)
                    throw new NotSupportedException("External material expression is unsupported.");
                if (expression.GetExportClassType().ToString() == "MaterialExpressionMaterialAttributeLayers")
                    throw new NotSupportedException("Material layer dependency traversal is unsupported.");
                yield return expression;
            }
        }
        void AppendTexture(UAsset package, NormalExport expression)
        {
            var texture = expression.Data.OfType<ObjectPropertyData>().SingleOrDefault(p => p.Name.ToString() == "Texture");
            if (texture == null || texture.Value.Index == 0) return;
            string type = expression.GetExportClassType().ToString();
            if (!type.StartsWith("MaterialExpressionTextureSample", StringComparison.Ordinal) &&
                !type.StartsWith("MaterialExpressionTextureObject", StringComparison.Ordinal))
                throw new NotSupportedException("Unknown texture-producing material expression.");
            string path = PathOf(package, texture.Value.Index);
            if (seenTextures.Add(path)) textures.Add(path);
        }
        void CollectFunction(UAsset package, NormalExport call, List<(UAsset, NormalExport)> ordered, HashSet<string> seen, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Material function dependency depth exceeds limits.");
            var reference = call.Data.OfType<ObjectPropertyData>().SingleOrDefault(p => p.Name.ToString() == "MaterialFunction");
            if (reference == null || reference.Value.Index == 0) return;
            if (load == null) throw new NotSupportedException("Function textures require a dependency loader.");
            string path = PathOf(package, reference.Value.Index);
            if (!active.Add(path)) throw new InvalidDataException("Cyclic material function dependency.");
            try
            {
                if (seen.Contains(path)) return;
                if (!packages.TryGetValue(path, out var functionAsset)) packages.Add(path, functionAsset = load(path));
                var function = functionAsset.Exports.OfType<NormalExport>().SingleOrDefault(e => e.GetExportClassType().ToString() == "MaterialFunction")
                    ?? throw new NotSupportedException("Material function instances are unsupported.");
                foreach (var expression in Expressions(functionAsset, function, "FunctionExpressions"))
                    if (expression.GetExportClassType().ToString() == "MaterialExpressionMaterialFunctionCall")
                        CollectFunction(functionAsset, expression, ordered, seen, depth + 1);
                if (seen.Add(path)) ordered.Add((functionAsset, function));
            }
            finally { active.Remove(path); }
        }
        foreach (var expression in Expressions(asset, material, "Expressions"))
        {
            if (expression.GetExportClassType().ToString() == "MaterialExpressionMaterialFunctionCall")
            {
                var ordered = new List<(UAsset, NormalExport)>();
                CollectFunction(asset, expression, ordered, new(StringComparer.Ordinal), 0);
                foreach (var (package, function) in ordered)
                    foreach (var node in Expressions(package, function, "FunctionExpressions")) AppendTexture(package, node);
            }
            else AppendTexture(asset, expression);
        }
        return textures;
    }
}
