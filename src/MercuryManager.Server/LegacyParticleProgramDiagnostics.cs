using UAssetAPI;
using UAssetAPI.ExportTypes;

namespace MercuryManager.Server;

/// <summary>Non-Slate evidence handoff only. Never substitutes a particle PS for a Slate program.</summary>
public static class LegacyParticleProgramDiagnostics
{
    public sealed record Evidence(string VertexFactory, bool IdentityVerified, int? VertexOffset,
        int? FragmentOffset, LegacyParticleSpriteBindings? SpriteBindings, string[] Limitations);

    public static IReadOnlyList<Evidence> Inspect(UAsset asset, NormalExport material,
        LegacyCookedMaterial cooked, IReadOnlyList<MaterialShaderCompiler.ShaderCandidate> candidates)
    {
        var result = new List<Evidence>();
        foreach (var factory in new[] { "FParticleSpriteVertexFactory", "FMeshParticleVertexFactory" })
        {
            var maps = cooked.VertexFactoryMaps ?? [];
            if (!maps.Any(m => m.VertexFactory == factory)) continue;
            try
            {
                var pair = MaterialProgramSelector.SelectVertexFactoryPair(candidates, maps, factory,
                    "TBasePassVSFNoLightMapPolicy", "TBasePassPSFNoLightMapPolicy");
                LegacyParticleSpriteBindings? bindings = null;
                if (factory == "FParticleSpriteVertexFactory")
                {
                    var entry = maps.Single(m => m.VertexFactory == factory).Shaders.Single(s => s.Type == "TBasePassVSFNoLightMapPolicy");
                    bindings = LegacyParticleSpriteBindingReader.Read(asset, material.Extras, entry,
                        material.SerialOffset + material.SerialSize - material.Extras.Length);
                }
                result.Add(new(factory, true, pair.Vertex.Offset, pair.Fragment.Offset, bindings,
                    ["Particle scene/numeric inputs and native geometry are not provided; WebGL vertex/fragment execution is not validated."]));
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
            {
                result.Add(new(factory, false, null, null, null, [ex.Message]));
            }
        }
        return result;
    }
}
