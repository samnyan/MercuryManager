namespace MercuryManager.Server;

/// <summary>Material-only diagnostic upload; does not supply scene, frame or geometry inputs.</summary>
public static class ParticleMaterialNumericInputs
{
    public sealed record Evidence(bool Bound, int BufferIndex, bool UploadValidated,
        int NativeByteCount, byte[]? Data, string[] Limitations);

    public static Evidence Inspect(string reflection, LegacyMaterialShaderBindings bindings,
        LegacyUniformSet uniforms, MaterialUniformContext context)
    {
        if (!bindings.MaterialBufferBound)
            return new(false, bindings.MaterialBufferIndex, false, 0, null,
                ["Stage has no native Material binding; no buffer is supplied."]);
        int nativeBytes = 0;
        try
        {
            var constants = LegacyMaterialUniformPacker.PackNumericConstants(uniforms, bindings, context);
            nativeBytes = constants.Length;
            // The validator accepts only a contiguous native register prefix, not arbitrary reflected members.
            var upload = LegacySlateProgramSelector.PrepareMaterialBuffer(reflection, bindings, constants);
            return new(true, bindings.MaterialBufferIndex, true, nativeBytes, upload,
                ["Material numeric prefix only; per-frame writes, textures and scene inputs are not supplied."]);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return new(true, bindings.MaterialBufferIndex, false, nativeBytes, null, [ex.Message]);
        }
    }
}
