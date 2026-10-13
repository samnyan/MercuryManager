using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;

namespace MercuryManager.Server;

public static class LegacyMaterialTextureTable
{
    // 按 Expressions 的序列执行去重；不能用导入表或名称排序替代。
    public static IReadOnlyList<string> ReadDirect(UAsset asset, NormalExport material)
    {
        var expressions = material.Data.OfType<ArrayPropertyData>().SingleOrDefault(p => p.Name.ToString() == "Expressions")
            ?? throw new NotSupportedException("Material expression list is unavailable.");
        var indices = new List<int>();
        foreach (var item in expressions.Value)
        {
            if (item is not ObjectPropertyData reference) throw new InvalidDataException("Invalid expression reference.");
            int index = reference.Value.Index;
            if (index == 0) continue;
            if (index < 0 || index > asset.Exports.Count || asset.Exports[index - 1] is not NormalExport expression)
                throw new NotSupportedException("External material expression requires dependency traversal.");
            string type = expression.GetExportClassType().ToString();
            if (type is "MaterialExpressionMaterialFunctionCall" or "MaterialExpressionMaterialAttributeLayers")
                throw new NotSupportedException("Function/layer texture dependencies require native-order traversal.");
            var texture = expression.Data.OfType<ObjectPropertyData>().SingleOrDefault(p => p.Name.ToString() == "Texture");
            if (texture == null || texture.Value.Index == 0) continue;
            if (!type.StartsWith("MaterialExpressionTextureSample", StringComparison.Ordinal) &&
                !type.StartsWith("MaterialExpressionTextureObject", StringComparison.Ordinal))
                throw new NotSupportedException("Unknown texture-producing material expression.");
            if (!indices.Contains(texture.Value.Index)) indices.Add(texture.Value.Index);
        }
        return indices.Select(index => GenericResourceViewer.ResolveReference(index, asset)["ContentFile"]?.ToString()
            ?? throw new NotSupportedException("Unresolved material texture reference.")).ToArray();
    }
}
