namespace MercuryManager.Server;

/// <summary>Exact per-stage scene resource identities; never supplies invented scene images.</summary>
public static class ParticleSceneResourceRequirements
{
    public sealed record Requirement(string UniformName, string SamplerType, string NativeResource,
        int ImageRegister, int SamplerRegister, bool SourceProvided, bool NativeSamplerVerified);

    public static IReadOnlyList<Requirement> Inspect(MaterialShaderCompiler.Translation translation,
        LegacyFogBindings? fog)
    {
        if (fog == null) return [];
        var result = new List<Requirement>();
        foreach (var sampler in translation.CombinedSamplers)
        {
            if (!Register(sampler.ImageName, 't', out int image) || !Register(sampler.SamplerName, 's', out int state))
                continue;
            string? resource = null;
            bool nativeSampler = false;
            if (sampler.SamplerType == "samplerCube" && Matches(fog.InscatteringCubemap, image))
            {
                resource = "FogInscatteringColorCubemap";
                nativeSampler = Matches(fog.InscatteringSampler, state);
            }
            if (sampler.SamplerType == "sampler3D" && Matches(fog.IntegratedLightScattering, image))
            {
                resource = "IntegratedLightScattering";
                nativeSampler = Matches(fog.IntegratedLightScatteringSampler, state);
            }
            if (resource != null) result.Add(new(sampler.UniformName, sampler.SamplerType, resource, image, state, false, nativeSampler));
        }
        return result;
    }

    private static bool Matches(LegacyResourceBinding binding, int register) =>
        binding.NumResources == 1 && binding.BaseIndex == register;

    private static bool Register(string name, char prefix, out int register)
    {
        register = -1;
        return name.Length > 1 && name[0] == prefix && name[1..].All(char.IsAsciiDigit) &&
            int.TryParse(name.AsSpan(1), out register) && register >= 0;
    }
}
