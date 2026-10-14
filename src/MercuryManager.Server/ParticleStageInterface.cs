using System.Text.Json.Nodes;

namespace MercuryManager.Server;

/// <summary>Location/type evidence only. ES300 consumers must use shared varying names before linking.</summary>
public static class ParticleStageInterface
{
    public sealed record Varying(int Location, string Type, string VertexName, string FragmentName, string LinkName);
    public sealed record Evidence(bool Compatible, Varying[] Varyings, string[] Limitations);

    public static Evidence Inspect(string vertexReflection, string fragmentReflection)
    {
        var vertex = JsonNode.Parse(vertexReflection) as JsonObject ?? throw new InvalidDataException("Invalid vertex reflection.");
        var fragment = JsonNode.Parse(fragmentReflection) as JsonObject ?? throw new InvalidDataException("Invalid fragment reflection.");
        var outputs = vertex["outputs"] as JsonArray ?? [];
        var inputs = fragment["inputs"] as JsonArray ?? [];
        var varyings = new List<Varying>();
        var limitations = new List<string>();
        var locations = new HashSet<int>();
        foreach (var input in inputs)
        {
            if (input?["location"] is not JsonValue locationValue || !locationValue.TryGetValue<int>(out var location) ||
                location < 0 || !locations.Add(location))
                throw new InvalidDataException("Fragment input location missing or duplicated.");
            var matches = outputs.Where(o => o?["location"]?.GetValue<int>() == location).ToArray();
            string type = input["type"]?.GetValue<string>() ?? "";
            if (matches.Length != 1 || matches[0]?["type"]?.GetValue<string>() != type)
            {
                limitations.Add($"Varying location {location} has no unique matching vertex output type.");
                continue;
            }
            if (input["array"] != null || matches[0]?["array"] != null ||
                type is not ("float" or "vec2" or "vec3" or "vec4"))
            {
                limitations.Add($"Varying location {location} requires an unsupported interface shape.");
                continue;
            }
            var vertexName = matches[0]!["name"]?.GetValue<string>();
            var fragmentName = input["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(vertexName) || string.IsNullOrEmpty(fragmentName))
                throw new InvalidDataException("Varying name missing.");
            varyings.Add(new(location, type, vertexName, fragmentName, $"mercury_varying_{location}"));
        }
        limitations.Add("Reflection matching does not validate interpolation qualifiers, GLSL linkage or native attribute semantics.");
        return new(limitations.Count == 1, varyings.ToArray(), limitations.ToArray());
    }
}
