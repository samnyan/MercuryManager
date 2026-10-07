using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace MercuryManager.AssetProbe;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: AssetProbe <private-copy.uasset> <new-output-directory>"); return 2; }
        try
        {
            var result = MusicProbe.Run(args[0], args[1]);
            var json = JsonSerializer.Serialize(result, MusicProbe.JsonOptions);
            File.WriteAllText(Path.Combine(args[1], "probe-result.json"), json);
            Console.WriteLine(json);
            return result.Passed ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

public sealed record PropertySnapshot(string Name, string Type, int ArrayIndex, object? Value);
public sealed record RowSnapshot(string Name, string StructType, PropertySnapshot[] Properties);
public sealed record FileComparison(string Extension, long InputBytes, long OutputBytes, string InputSha256, string OutputSha256, bool BytesEqual);
public sealed record ProbeResult(string Dependency, string EngineAssumption, string Input, string Output,
    string[] ExportClasses, string RowStruct, PropertySnapshot[] ObjectProperties, int RowCount,
    string[] RowNames, string[] UniqueIds, PropertySnapshot[] Schema, RowSnapshot[] Samples,
    int FallbackCount, bool VerifyBinaryEquality, FileComparison[] Files, bool SemanticsEqual, bool Passed);

public static class MusicProbe
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ProbeResult Run(string input, string outputDirectory)
    {
        input = Path.GetFullPath(input);
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (!File.Exists(input)) throw new FileNotFoundException("Private fixture missing", input);
        if (!File.Exists(Path.ChangeExtension(input, ".uexp"))) throw new FileNotFoundException("Companion .uexp missing", input);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("Output directory must be NEW; never overwrite a source or previous run.");

        // Explicit candidate, not automatic engine detection: cooked header versions are zero.
        var asset = new UAsset(input, EngineVersion.VER_UE4_19);
        var classes = asset.Exports.Select(e => e.GetType().FullName + " (" + e.GetExportClassType() + ")").ToArray();
        Console.Error.WriteLine("Exports: " + string.Join(", ", classes));
        if (asset.Exports.Any(e => e is RawExport)) throw new InvalidDataException("RawExport compatibility gate failed.");
        var table = asset.Exports.OfType<DataTableExport>().Single();
        var objectProperties = table.Data.Select(Snapshot).ToArray();
        var rowStructProperty = table.Data.OfType<ObjectPropertyData>().Single(p => p.Name.ToString() == "RowStruct");
        var rowStruct = rowStructProperty.ToImport(asset).ObjectName.ToString();
        var rows = Rows(table);
        if (rows.Length == 0 || rows.Any(r => r.Properties.Length == 0)) throw new InvalidDataException("Empty/uninterpretable table gate failed.");
        var schema = rows.SelectMany(r => r.Properties).DistinctBy(p => (p.Name, p.Type, p.ArrayIndex))
            .Select(p => p with { Value = null }).ToArray();
        var ids = rows.Select(r => r.Properties.Single(p => p.Name == "UniqueID").Value?.ToString() ?? "").ToArray();
        var before = SemanticJson(objectProperties, rows);
        var verified = asset.VerifyBinaryEquality();
        if (!verified) throw new InvalidDataException("VerifyBinaryEquality failed; do not proceed to editing.");

        Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, Path.GetFileName(input));
        asset.Write(out MemoryStream uassetStream, out MemoryStream uexpStream);
        using (uassetStream)
        using (uexpStream)
        {
            File.WriteAllBytes(output, uassetStream.ToArray());
            File.WriteAllBytes(Path.ChangeExtension(output, ".uexp"), uexpStream.ToArray());
        }
        var files = new[] { ".uasset", ".uexp" }.Select(ext => Compare(Path.ChangeExtension(input, ext), Path.ChangeExtension(output, ext), ext)).ToArray();
        var reopened = new UAsset(output, EngineVersion.VER_UE4_19);
        if (reopened.Exports.Any(e => e is RawExport)) throw new InvalidDataException("Reopened RawExport gate failed.");
        var reopenedTable = reopened.Exports.OfType<DataTableExport>().Single();
        var semanticsEqual = before == SemanticJson(reopenedTable.Data.Select(Snapshot).ToArray(), Rows(reopenedTable));
        var passed = verified && files.All(f => f.BytesEqual) && semanticsEqual;
        return new("UAssetAPI NuGet 1.1.0 (pinned; net8.0 library on net10.0)", "VER_UE4_19 explicitly supplied, NOT auto-detected", input, output,
            classes, rowStruct, objectProperties, rows.Length, rows.Select(r => r.Name).ToArray(), ids, schema,
            rows.Take(3).ToArray(), 0, verified, files, semanticsEqual, passed);
    }

    private static RowSnapshot[] Rows(DataTableExport table) => table.Table.Data.Select(r =>
        new RowSnapshot(r.Name.ToString(), r.StructType.ToString(), r.Value.Select(Snapshot).ToArray())).ToArray();

    private static string SemanticJson(PropertySnapshot[] properties, RowSnapshot[] rows) =>
        JsonSerializer.Serialize(new { ObjectProperties = properties, Rows = rows }, JsonOptions);

    private static PropertySnapshot Snapshot(PropertyData p)
    {
        object? value = p switch
        {
            UInt32PropertyData v => v.Value,
            UInt64PropertyData v => v.Value.ToString(CultureInfo.InvariantCulture), // JSON-safe uint64, particularly WorkBuffer
            IntPropertyData v => v.Value,
            FloatPropertyData v => v.Value,
            BoolPropertyData v => v.Value,
            StrPropertyData v => v.Value?.ToString(),
            NamePropertyData v => v.Value?.ToString(),
            ObjectPropertyData v => v.Value.Index,
            BytePropertyData v => v.ByteType == BytePropertyType.Byte ? (object)v.Value : v.EnumValue?.ToString(),
            StructPropertyData v => v.Value.Select(Snapshot).ToArray(),
            _ => throw new InvalidDataException($"Uninterpretable/unsupported property gate: {p.Name} ({p.GetType().FullName})")
        };
        return new(p.Name.ToString(), p.GetType().Name, p.ArrayIndex, value);
    }

    private static FileComparison Compare(string input, string output, string extension)
    {
        var a = File.ReadAllBytes(input);
        var b = File.ReadAllBytes(output);
        return new(extension, a.LongLength, b.LongLength, Convert.ToHexString(SHA256.HashData(a)), Convert.ToHexString(SHA256.HashData(b)), a.AsSpan().SequenceEqual(b));
    }
}
