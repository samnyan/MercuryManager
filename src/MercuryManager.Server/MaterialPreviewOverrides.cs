using System.Numerics;
using System.Text.Json;

namespace MercuryManager.Server;

/// <summary>Temporary preview inputs; does not write asset defaults or select static permutations.</summary>
public static class MaterialPreviewOverrides
{
    public static MaterialUniformContext Parse(string? scalars, string? vectors, float time, float realTime)
    {
        if (!float.IsFinite(time) || !float.IsFinite(realTime))
            throw new ArgumentException("Material time must be finite.");
        var scalarValues = new Dictionary<string, float>(StringComparer.Ordinal);
        var vectorValues = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        Read(scalars, (name, value) =>
        {
            if (!scalarValues.TryAdd(name, Number(value)))
                throw new ArgumentException("Duplicate scalar override.");
        });
        Read(vectors, (name, value) =>
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 4)
                throw new ArgumentException("Vector overrides require exactly four numeric components.");
            var color = new Vector4(Number(value[0]), Number(value[1]), Number(value[2]), Number(value[3]));
            if (!vectorValues.TryAdd(name, color)) throw new ArgumentException("Duplicate vector override.");
        });
        return new(scalarValues, vectorValues, time, realTime);
    }

    private static float Number(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) || !float.IsFinite(number))
            throw new ArgumentException("Material overrides require finite numeric values.");
        return number;
    }

    private static void Read(string? text, Action<string, JsonElement> accept)
    {
        if (text is null) return;
        if (text.Length > 16384) throw new ArgumentException("Material override payload exceeds limits.");
        try
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Material overrides must be JSON objects.");
            int count = 0;
            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (++count > 128 || property.Name.Length is 0 or > 256)
                    throw new ArgumentException("Material override names/count exceed limits.");
                accept(property.Name, property.Value);
            }
        }
        catch (JsonException ex) { throw new ArgumentException("Invalid material override JSON.", ex); }
    }
}
