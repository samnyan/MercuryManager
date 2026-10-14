namespace MercuryManager.Server;

/// <summary>Validated individual frame writes, not complete scene buffers.</summary>
public static class ParticleMaterialFrameInputs
{
    public sealed record Evidence(bool WritesValidated, LegacyFrameConstantWrite[] Writes, string[] Limitations);

    public static Evidence Inspect(string reflection, LegacyMaterialShaderBindings material,
        LegacyConstantBinding[][] bindings, LegacyUniformSet uniforms,
        MaterialUniformContext current, MaterialUniformContext previous, int materialUploadBytes)
    {
        try
        {
            var writes = LegacyMaterialFrameEvaluator.Evaluate(uniforms, bindings, current, previous);
            LegacySlateProgramSelector.ValidateFrameWrites(reflection, material, materialUploadBytes, writes);
            return new(true, writes.ToArray(),
                ["Individual frame writes only; untouched scene buffer bytes are not supplied."]);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return new(false, [], [ex.Message]);
        }
    }
}
