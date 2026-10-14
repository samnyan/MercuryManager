using UAssetAPI;
using UAssetAPI.ExportTypes;

namespace MercuryManager.Server;

/// <summary>Non-Slate evidence handoff only. Never substitutes a particle PS for a Slate program.</summary>
public static class LegacyParticleProgramDiagnostics
{
    public sealed record Evidence(string VertexFactory, bool IdentityVerified, int? VertexOffset,
        int? FragmentOffset, LegacyParticleSpriteBindings? SpriteBindings, string[] Limitations)
    {
        public IReadOnlyList<FogStageEvidence> FogBindings { get; init; } = [];
    }
    public sealed record FogStageEvidence(string Stage, LegacyFogBindings Bindings)
    {
        public LegacyMaterialShaderBindings? MaterialBindings { get; init; }
        public LegacyConstantBinding[][] FrameBindings { get; init; } = [[], [], [], []];
    }

    public sealed record StageEvidence(string Stage, int Offset, bool TranslationSucceeded,
        MaterialShaderCompiler.Translation? Translation, string[] Limitations)
    {
        public IReadOnlyList<ParticleSceneResourceRequirements.Requirement> SceneResources { get; init; } = [];
        public ParticleMaterialNumericInputs.Evidence? MaterialNumericInput { get; init; }
        public ParticleMaterialFrameInputs.Evidence? MaterialFrameInput { get; init; }
    }
    public sealed record ProgramStages(string VertexFactory, bool RenderingReady, StageEvidence[] Stages)
    {
        public ParticleStageInterface.Evidence? StageInterface { get; init; }
    }

    /// <summary>Opt-in development payload. Translation is not browser execution readiness.</summary>
    public static async Task<IReadOnlyList<ProgramStages>> TranslateStages(
        IReadOnlyList<Evidence> evidence, IReadOnlyList<MaterialShaderCompiler.ShaderCandidate> candidates,
        string cacheRoot, CancellationToken cancellationToken = default,
        LegacyUniformSet? uniforms = null, MaterialUniformContext? context = null,
        MaterialUniformContext? previousContext = null)
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
                    var translation = await MaterialShaderCompiler.Translate(candidate, cacheRoot, cancellationToken, diagnosticSamplers: true);
                    var binding = program.FogBindings.SingleOrDefault(f => f.Stage == stage);
                    var numeric = binding?.MaterialBindings is { } materialBinding && uniforms != null && context != null
                        ? ParticleMaterialNumericInputs.Inspect(translation.Reflection, materialBinding, uniforms, context) : null;
                    stages.Add(new(stage, candidate.Offset, true, translation,
                        ["Translated source still requires WebGL validation and native resource/geometry adapters."])
                    {
                        SceneResources = ParticleSceneResourceRequirements.Inspect(translation, binding?.Bindings),
                        MaterialNumericInput = numeric,
                        MaterialFrameInput = binding?.MaterialBindings != null && uniforms != null && context != null &&
                            previousContext != null && numeric != null && (!numeric.Bound || numeric.UploadValidated)
                            ? ParticleMaterialFrameInputs.Inspect(translation.Reflection, binding.MaterialBindings,
                                binding.FrameBindings, uniforms, context, previousContext, numeric.Data?.Length ?? 0) : null
                    });
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or System.ComponentModel.Win32Exception)
                {
                    stages.Add(new(stage, candidate.Offset, false, null, [ex.Message]));
                }
            }
            var vertex = stages.Single(s => s.Stage == "vertex").Translation;
            var fragment = stages.Single(s => s.Stage == "fragment").Translation;
            result.Add(new(program.VertexFactory, false, stages.ToArray())
            {
                StageInterface = vertex != null && fragment != null
                    ? ParticleStageInterface.Inspect(vertex.Reflection, fragment.Reflection) : null
            });
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
                var fog = new List<FogStageEvidence>();
                var limitations = new List<string>
                {
                    "Particle scene/numeric inputs and native geometry are not provided; WebGL vertex/fragment execution is not validated."
                };
                foreach (var (stage, type) in new[] { ("vertex", "TBasePassVSFNoLightMapPolicy"), ("fragment", "TBasePassPSFNoLightMapPolicy") })
                {
                    try
                    {
                        var entry = maps.Single(m => m.VertexFactory == factory).Shaders.Single(s => s.Type == type);
                        var layout = LegacyBasePassFogLocator.Inspect(asset, material.Extras, entry,
                            material.SerialOffset + material.SerialSize - material.Extras.Length, factory);
                        fog.Add(new(stage, LegacyFogShaderBindings.Read(material.Extras, entry, layout.FogOffset))
                            { MaterialBindings = layout.Material, FrameBindings = layout.FrameBindings });
                    }
                    catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
                    {
                        limitations.Add($"{stage} fog bindings unavailable: {ex.Message}");
                    }
                }
                result.Add(new(factory, true, pair.Vertex.Offset, pair.Fragment.Offset, bindings, limitations.ToArray())
                    { FogBindings = fog });
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
            {
                result.Add(new(factory, false, null, null, null, [ex.Message]));
            }
        }
        return result;
    }
}
