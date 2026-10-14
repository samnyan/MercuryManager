using UAssetAPI;
using UAssetAPI.ExportTypes;

namespace MercuryManager.Server;

/// <summary>Non-Slate evidence handoff only. Never substitutes a particle PS for a Slate program.</summary>
public static class LegacyParticleProgramDiagnostics
{
    public sealed record Evidence(string VertexFactory, bool IdentityVerified, int? VertexOffset,
        int? FragmentOffset, LegacyParticleSpriteBindings? SpriteBindings, string[] Limitations);

    public sealed record StageEvidence(string Stage, int Offset, bool TranslationSucceeded,
        MaterialShaderCompiler.Translation? Translation, string[] Limitations);
    public sealed record ProgramStages(string VertexFactory, bool RenderingReady, StageEvidence[] Stages);

    /// <summary>Opt-in development payload. Translation is not browser execution readiness.</summary>
    public static async Task<IReadOnlyList<ProgramStages>> TranslateStages(
        IReadOnlyList<Evidence> evidence, IReadOnlyList<MaterialShaderCompiler.ShaderCandidate> candidates,
        string cacheRoot, CancellationToken cancellationToken = default)
    {
        var result = new List<ProgramStages>();
        foreach (var program in evidence.Where(p => p.IdentityVerified))
        {
            var stages = new List<StageEvidence>();
            foreach (var (stage, offset) in new[] { ("vertex", program.VertexOffset), ("fragment", program.FragmentOffset) })
            {
                var matches = candidates.Where(c => c.Stage == stage && c.Offset == offset).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException("Verified particle stage candidate is not unique.");
                var candidate = matches[0];
                try
                {
                    var translation = await MaterialShaderCompiler.Translate(candidate, cacheRoot, cancellationToken);
                    stages.Add(new(stage, candidate.Offset, true, translation,
                        ["Translated source still requires WebGL validation and native resource/geometry adapters."]));
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or System.ComponentModel.Win32Exception)
                {
                    stages.Add(new(stage, candidate.Offset, false, null, [ex.Message]));
                }
            }
            result.Add(new(program.VertexFactory, false, stages.ToArray()));
        }
        return result;
    }

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
