namespace MercuryManager.Server;

/// <summary>Select by serialized shader identity and entry boundary, never discovery order.</summary>
public static class LegacySlateProgramSelector
{
    public static MaterialShaderCompiler.ShaderCandidate SelectDefaultFalse(
        IReadOnlyList<MaterialShaderCompiler.ShaderCandidate> candidates, IReadOnlyList<LegacyShaderEntry> entries)
    {
        var matches = entries.Where(x => x.Type == "TSlateMaterialShaderPSDefaultfalse").ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("Expected one unambiguous Slate Defaultfalse shader entry.");
        var entry = matches[0];
        if (entry.StartOffset < 0 || entry.EndOffset <= entry.StartOffset)
            throw new InvalidDataException("Invalid Slate shader directory boundary.");
        var programs = candidates.Where(x => x.Offset >= entry.StartOffset && x.Offset < entry.EndOffset).ToArray();
        if (programs.Length != 1 || programs[0].Stage != "fragment")
            throw new NotSupportedException("Slate entry does not contain one independently identified inline fragment program.");
        return programs[0];
    }

    // Compatibility entry points; layout validation is shared with non-Slate programs.
    public static void ValidateMaterialBuffer(string reflection, LegacyMaterialShaderBindings bindings, byte[] constants)
        => LegacyMaterialBufferValidation.ValidateMaterialBuffer(reflection, bindings, constants);

    public static byte[] PrepareMaterialBuffer(string reflection, LegacyMaterialShaderBindings bindings, byte[] constants)
        => LegacyMaterialBufferValidation.PrepareMaterialBuffer(reflection, bindings, constants);

    public static void ValidateFrameWrites(string reflection, LegacyMaterialShaderBindings material,
        int materialUploadBytes, IReadOnlyList<LegacyFrameConstantWrite> writes)
        => LegacyMaterialBufferValidation.ValidateFrameWrites(reflection, material, materialUploadBytes, writes);
}
