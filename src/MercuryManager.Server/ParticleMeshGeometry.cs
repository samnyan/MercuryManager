using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Versions;

namespace MercuryManager.Server;

/// <summary>Native LOD0 geometry, without preview scaling or material substitution.</summary>
public static class ParticleMeshGeometry
{
    public sealed record Section(int MaterialIndex, int FirstIndex, int NumTriangles);
    public sealed record Geometry(float[] Positions, float[][] TexCoords, uint[] Indices, Section[] Sections,
        float[] TangentX, float[] TangentZ);
    public static Geometry Read(string path)
    {
        using var provider = new DefaultFileProvider(Path.GetDirectoryName(path)!, SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_UE4_19), StringComparer.OrdinalIgnoreCase);
        provider.Initialize();
        var file = provider.Files.Values.Single(f => f.Path.EndsWith('/' + Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)
            || f.Path.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
        var mesh = provider.LoadPackage(file.Path).GetExports().OfType<UStaticMesh>().Single();
        var lod = mesh.RenderData?.LODs.FirstOrDefault() ?? throw new NotSupportedException("No cooked mesh LOD0.");
        if (lod.SkipLod) throw new NotSupportedException("Mesh buffers are stripped.");
        var vertices = lod.PositionVertexBuffer!.Verts;
        var uv = lod.VertexBuffer!;
        if (vertices.Length == 0 || vertices.Length > 1_000_000 || uv.UV.Length != vertices.Length)
            throw new InvalidDataException("Invalid mesh vertex count.");
        // CUE4Parse converts high precision tangents to 8-bit packed normals; do not silently lose precision.
        if (uv.UseHighPrecisionTangentBasis) throw new NotSupportedException("High precision mesh tangents require a lossless adapter.");
        if (uv.NumTexCoords < 1 || uv.NumTexCoords > 8 || uv.UV.Any(v => v.UV.Length != uv.NumTexCoords || v.Normal.Length != 3))
            throw new InvalidDataException("Invalid mesh UV/tangent layout.");
        float[] Tangents(int index) => uv.UV.SelectMany(v => {
            var n = v.Normal[index]; return new[] { n.X, n.Y, n.Z, n.W };
        }).ToArray();
        // Slot 1 is an absent TangentY placeholder, not a serialized basis vector.
        var tangentX = Tangents(0);
        var tangentZ = Tangents(2);
        var positions = vertices.SelectMany(v => new[] { v.X, v.Y, v.Z }).ToArray();
        var channels = Enumerable.Range(0, uv.NumTexCoords).Select(c => uv.UV.SelectMany(v => new[] { v.UV[c].U, v.UV[c].V }).ToArray()).ToArray();
        var indices = lod.IndexBuffer!.Buffer;
        if (indices.Length > 6_000_000 || indices.Any(i => i >= vertices.Length) || positions.Any(v => !float.IsFinite(v)) || channels.Any(c => c.Any(v => !float.IsFinite(v))))
            throw new InvalidDataException("Invalid mesh geometry.");
        var sections = lod.Sections.Select(s => new Section(s.MaterialIndex, s.FirstIndex, s.NumTriangles)).ToArray();
        if (sections.Any(s => s.FirstIndex < 0 || s.NumTriangles < 0 || (long)s.FirstIndex + (long)s.NumTriangles * 3 > indices.Length))
            throw new InvalidDataException("Invalid mesh section range.");
        return new(positions, channels, indices, sections, tangentX, tangentZ);
    }
}
