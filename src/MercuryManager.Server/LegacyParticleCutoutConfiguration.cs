using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;

namespace MercuryManager.Server;

/// <summary>Serialized configuration evidence, not a default-value or geometry decoder.</summary>
public static class LegacyParticleCutoutConfiguration
{
    public sealed record Evidence(string State, int? TextureIndex, bool DefaultBufferEligible,
        int OpaqueBytes, string Limitation)
    {
        public LegacyParticleCutoutGeometry.Geometry? CookedGeometry { get; init; }
        public bool? HasCookedGeometryResource => CookedGeometry == null ? null : CookedGeometry.VertexCount != 0;
    }

    public static Evidence Inspect(NormalExport module)
    {
        if (module.GetExportClassType().ToString() != "ParticleModuleRequired")
            throw new NotSupportedException("Cutout configuration requires ParticleModuleRequired.");
        var fields = module.Data.Where(p => p.Name.ToString() == "CutoutTexture").ToArray();
        int opaqueBytes = module.Extras?.Length ?? 0;
        LegacyParticleCutoutGeometry.Geometry? geometry = null;
        string geometryLimitation = "Runtime cutout vertex count/default selection remains unverified.";
        try { geometry = LegacyParticleCutoutGeometry.Read(module.Extras ?? []); }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException) { geometryLimitation = ex.Message; }
        if (fields.Length == 1 && fields[0] is ObjectPropertyData texture)
            return new(texture.Value.Index == 0 ? "serialized-null-texture" : "serialized-texture",
                texture.Value.Index, false, opaqueBytes,
                geometryLimitation) { CookedGeometry = geometry };
        return new("unverified", null, false, opaqueBytes,
            geometryLimitation) { CookedGeometry = geometry };
    }
}
