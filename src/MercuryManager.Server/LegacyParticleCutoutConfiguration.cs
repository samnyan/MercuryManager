using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;

namespace MercuryManager.Server;

/// <summary>Serialized configuration evidence, not a default-value or geometry decoder.</summary>
public static class LegacyParticleCutoutConfiguration
{
    public sealed record Evidence(string State, int? TextureIndex, bool DefaultBufferEligible,
        int OpaqueBytes, string Limitation);

    public static Evidence Inspect(NormalExport module)
    {
        if (module.GetExportClassType().ToString() != "ParticleModuleRequired")
            throw new NotSupportedException("Cutout configuration requires ParticleModuleRequired.");
        var fields = module.Data.Where(p => p.Name.ToString() == "CutoutTexture").ToArray();
        int opaqueBytes = module.Extras?.Length ?? 0;
        if (fields.Length == 1 && fields[0] is ObjectPropertyData texture)
            return new(texture.Value.Index == 0 ? "serialized-null-texture" : "serialized-texture",
                texture.Value.Index, false, opaqueBytes,
                "Texture reference alone does not establish runtime cutout vertex count or cooked geometry.");
        return new("unverified", null, false, opaqueBytes,
            "Cutout configuration is omitted or ambiguous; native defaults and opaque geometry must be verified before selecting the disabled buffer.");
    }
}
