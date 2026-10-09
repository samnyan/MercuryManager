using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace MercuryManager.Server;

public sealed record Workspace(string Id, string Profile, string UassetHash, string UexpHash, int RowCount, string? ContentRoot = null);
public sealed record MusicField(string Name, string Type, object? Value, bool ReadOnly);
public sealed record MusicRow(string RowName, MusicField[] Fields);

public sealed class MusicWorkspaceStore
{
    private readonly string root;
    public MusicWorkspaceStore(IConfiguration configuration)
    {
        root = Path.GetFullPath(configuration["workspace-root"] ?? Path.Combine(
            AppContext.BaseDirectory, "workspace"));
        Directory.CreateDirectory(root);
    }

    public string ProjectRoot => root;
    public Workspace Open(string path)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
        {
            RejectLinks(Path.Combine(path, "Table", "MusicParameterTable.uasset"));
            path = Path.Combine(path, "Table", "MusicParameterTable.uasset");
        }
        if (!path.EndsWith("MusicParameterTable.uasset", StringComparison.Ordinal))
            throw new ArgumentException("Select MusicParameterTable.uasset.");
        RejectLinks(path);
        var companion = Path.ChangeExtension(path, ".uexp");
        RejectLinks(companion);
        if (!File.Exists(path) || !File.Exists(companion)) throw new IOException("Asset pair missing.");
        var contentRoot = Path.GetDirectoryName(Path.GetDirectoryName(path));
        foreach (var existing in Directory.GetDirectories(root))
        {
            var manifestPath = Path.Combine(existing, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            var saved = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(manifestPath));
            if (saved is not null && saved.ContentRoot == contentRoot) return saved;
        }
        var a = File.ReadAllBytes(path);
        var b = File.ReadAllBytes(companion);
        var id = Guid.NewGuid().ToString("N");
        var directory = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "MusicParameterTable.uasset"), a);
            File.WriteAllBytes(Path.Combine(directory, "MusicParameterTable.uexp"), b);
            var rows = ReadRows(id);
            var workspace = new Workspace(id, "ue4.19", Hash(a), Hash(b), rows.Length, Path.GetDirectoryName(Path.GetDirectoryName(path)));
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(workspace));
            return workspace;
        }
        catch { Directory.Delete(directory, true); throw; }
    }

    public Workspace Get(string id) => JsonSerializer.Deserialize<Workspace>(
        File.ReadAllText(Path.Combine(DirectoryFor(id), "manifest.json"))) ?? throw new InvalidDataException("Invalid manifest.");

    public MusicRow[] ReadRows(string id)
    {
        var asset = Load(id);
        if (asset.Exports.Any(e => e is RawExport)) throw new InvalidDataException("Unsupported raw export.");
        var tables = asset.Exports.OfType<DataTableExport>().ToArray();
        if (tables.Length != 1) throw new InvalidDataException("Expected one DataTable export.");
        var table = tables[0];
        var rowStruct = table.Data.OfType<ObjectPropertyData>().SingleOrDefault(p => p.Name.ToString() == "RowStruct");
        if (rowStruct?.ToImport(asset).ObjectName.ToString() != "MusicParameterTableData")
            throw new InvalidDataException("Unsupported row structure.");
        var draft = Path.Combine(DirectoryFor(id), "draft.json");
        if (File.Exists(draft)) Apply(asset, JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllText(draft))!);
        return Snapshot(asset);
    }

    public object Patch(string id, string rowName, Dictionary<string, JsonElement> changes)
    {
        lock (this)
        {
            var directory = DirectoryFor(id);
            var draftPath = Path.Combine(directory, "draft.json");
            var drafts = File.Exists(draftPath)
                ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllText(draftPath))!
                : new Dictionary<string, Dictionary<string, JsonElement>>();
            var asset = Load(id);
            Apply(asset, drafts);
            Apply(asset, new() { [rowName] = changes });
            if (!drafts.TryGetValue(rowName, out var row)) drafts[rowName] = row = new();
            foreach (var change in changes) row[change.Key] = change.Value.Clone();
            File.WriteAllText(draftPath + ".tmp", JsonSerializer.Serialize(drafts));
            File.Move(draftPath + ".tmp", draftPath, true);
            return new { rowName, changedFields = changes.Keys };
        }
    }

    public object Export(string id)
    {
        lock (this)
        {
            var directory = DirectoryFor(id);
            var manifest = Get(id);
            var input = Path.Combine(directory, "MusicParameterTable.uasset");
            if (Hash(File.ReadAllBytes(input)) != manifest.UassetHash ||
                Hash(File.ReadAllBytes(Path.ChangeExtension(input, ".uexp"))) != manifest.UexpHash)
                throw new InvalidDataException("Workspace base hash conflict.");
            var asset = Load(id);
            var draft = Path.Combine(directory, "draft.json");
            if (File.Exists(draft)) Apply(asset, JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllText(draft))!);
            var expected = JsonSerializer.Serialize(Snapshot(asset));
            var exportId = Guid.NewGuid().ToString("N");
            var staging = Path.Combine(directory, "staging-" + exportId);
            Directory.CreateDirectory(staging);
            try
            {
                var output = Path.Combine(staging, "MusicParameterTable.uasset");
                asset.Write(output);
                var reopened = new UAsset(output, EngineVersion.VER_UE4_19);
                if (JsonSerializer.Serialize(Snapshot(reopened)) != expected)
                    throw new InvalidDataException("Export reload comparison failed.");
                var destination = Path.Combine(directory, "export-" + exportId);
                Directory.Move(staging, destination);
                return new { exportId, directory = destination, verified = true, rowCount = manifest.RowCount };
            }
            catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
        }
    }

    public object SaveProject(string id)
    {
        lock (this)
        {
            var manifest = Get(id);
            var projectPath = Path.Combine(DirectoryFor(id), "project.mercury.json");
            File.WriteAllText(projectPath + ".tmp", JsonSerializer.Serialize(new { version = 1, workspaceId = id, contentRoot = manifest.ContentRoot }));
            File.Move(projectPath + ".tmp", projectPath, true);
            return new { projectPath, workspaceId = id };
        }
    }

    public string[] ChangedFiles(string id)
    {
        var asset = Load(id);
        if (File.Exists(Path.Combine(DirectoryFor(id), "additions.json"))) return ["Table/MusicParameterTable.uasset", "Table/MusicParameterTable.uexp"];
        var before = JsonSerializer.Serialize(Snapshot(asset));
        var draft = Path.Combine(DirectoryFor(id), "draft.json");
        if (File.Exists(draft)) Apply(asset, JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(File.ReadAllText(draft))!);
        return before == JsonSerializer.Serialize(Snapshot(asset)) ? [] : ["Table/MusicParameterTable.uasset", "Table/MusicParameterTable.uexp"];
    }

    public object WriteFiles(string id, string mode, string? outputDirectory, bool backup)
    {
        lock (this)
        {
            if (mode is not ("overwrite" or "directory")) throw new ArgumentException("Invalid output mode.");
            if (ChangedFiles(id).Length == 0) return new { writtenFiles = Array.Empty<string>() };
            var manifest = Get(id);
            var contentRoot = manifest.ContentRoot ?? throw new ArgumentException("Reopen the Content project first.");
            if (mode == "directory" && (string.IsNullOrWhiteSpace(outputDirectory) || !Path.IsPathFullyQualified(outputDirectory)))
                throw new ArgumentException("Absolute server output path required.");
            var outputRoot = mode == "overwrite" ? contentRoot : Path.GetFullPath(outputDirectory!);
            if (!Path.IsPathFullyQualified(outputRoot)) throw new ArgumentException("Absolute server path required.");
            var targetDir = Path.Combine(outputRoot, "Table");
            var target = Path.Combine(targetDir, "MusicParameterTable.uasset");
            RejectLinks(target);
            RejectLinks(Path.ChangeExtension(target, ".uexp"));
            var source = Path.Combine(contentRoot, "Table", "MusicParameterTable.uasset");
            if (mode == "directory" && Path.GetFullPath(target) == Path.GetFullPath(source))
                throw new ArgumentException("Use overwrite mode to replace original files.");
            if (mode == "overwrite" && (Hash(File.ReadAllBytes(source)) != manifest.UassetHash || Hash(File.ReadAllBytes(Path.ChangeExtension(source, ".uexp"))) != manifest.UexpHash))
                throw new InvalidDataException("Source files changed externally; reopen project before writing.");
            var previousExports = Directory.GetDirectories(DirectoryFor(id), "export-*").ToHashSet();
            Export(id);
            var exportDir = Directory.GetDirectories(DirectoryFor(id), "export-*").Single(path => !previousExports.Contains(path));
            Directory.CreateDirectory(targetDir);
            var written = new List<string>();
            var oldFiles = new Dictionary<string, byte[]?>();
            foreach (var ext in new[] { ".uasset", ".uexp" })
            {
                var file = Path.ChangeExtension(target, ext);
                RejectLinks(file + "_bak");
                RejectLinks(file + ".mercury-tmp");
                oldFiles[file] = File.Exists(file) ? File.ReadAllBytes(file) : null;
                if (mode == "overwrite" && backup && File.Exists(file))
                    File.Copy(file, file + "_bak", false);
            }
            try
            {
                foreach (var pair in oldFiles)
                {
                    var ext = Path.GetExtension(pair.Key);
                    File.Copy(Path.Combine(exportDir, "MusicParameterTable" + ext), pair.Key + ".mercury-tmp", false);
                    File.Move(pair.Key + ".mercury-tmp", pair.Key, true);
                    written.Add(pair.Key);
                }
                var actual = new UAsset(target, EngineVersion.VER_UE4_19);
                var expected = new UAsset(Path.Combine(exportDir, "MusicParameterTable.uasset"), EngineVersion.VER_UE4_19);
                if (JsonSerializer.Serialize(Snapshot(actual)) != JsonSerializer.Serialize(Snapshot(expected))) throw new InvalidDataException("Written asset verification failed.");
            }
            catch
            {
                foreach (var pair in oldFiles)
                {
                    if (pair.Value is null) File.Delete(pair.Key); else File.WriteAllBytes(pair.Key, pair.Value);
                    File.Delete(pair.Key + ".mercury-tmp");
                }
                throw;
            }
            if (mode == "overwrite")
            {
                var basePath = Path.Combine(DirectoryFor(id), "MusicParameterTable.uasset");
                File.Copy(target, basePath, true);
                File.Copy(Path.ChangeExtension(target, ".uexp"), Path.ChangeExtension(basePath, ".uexp"), true);
                var updated = manifest with { UassetHash = Hash(File.ReadAllBytes(target)), UexpHash = Hash(File.ReadAllBytes(Path.ChangeExtension(target, ".uexp"))) };
                File.WriteAllText(Path.Combine(DirectoryFor(id), "manifest.json"), JsonSerializer.Serialize(updated));
                File.Delete(Path.Combine(DirectoryFor(id), "draft.json"));
                File.Delete(Path.Combine(DirectoryFor(id), "additions.json"));
            }
            return new { writtenFiles = written.ToArray(), backup, mode };
        }
    }

    private UAsset Load(string id)
    {
        var asset = new UAsset(Path.Combine(DirectoryFor(id), "MusicParameterTable.uasset"), EngineVersion.VER_UE4_19);
        var path = Path.Combine(DirectoryFor(id), "additions.json");
        if (File.Exists(path)) foreach (var rowName in JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))!) AddZeroRow(asset, rowName);
        return asset;
    }

    private static void AddZeroRow(UAsset asset, string rowName)
    {
        if (!uint.TryParse(rowName, out var uniqueId) || uniqueId == 0 || rowName != uniqueId.ToString()) throw new ArgumentException("Positive canonical uint32 song ID required.");
        var table = asset.Exports.OfType<DataTableExport>().Single();
        if (table.Table.Data.Any(r => r.Name.ToString() == rowName || r.Value.OfType<UInt32PropertyData>().Any(p => p.Name.ToString() == "UniqueID" && p.Value == uniqueId))) throw new ArgumentException("Duplicate song ID.");
        var row = (UAssetAPI.PropertyTypes.Structs.StructPropertyData)table.Table.Data[0].Clone();
        row.Name = new FName(asset, rowName);
        foreach (var p in row.Value)
        {
            switch (p)
            {
                case UInt32PropertyData v: v.Value = p.Name.ToString() == "UniqueID" ? uniqueId : 0; break;
                case UInt64PropertyData v: v.Value = 0; break;
                case IntPropertyData v: v.Value = 0; break;
                case FloatPropertyData v: v.Value = 0; break;
                case BoolPropertyData v: v.Value = false; break;
                case StrPropertyData v: v.Value = new FString(""); break;
                case BytePropertyData v when v.ByteType == BytePropertyType.Byte: v.Value = 0; break;
                default: throw new InvalidDataException("Unsupported new-row field " + p.Name);
            }
        }
        table.Table.Data.Add(row);
    }

    public MusicRow AddSong(string id, string rowName, Dictionary<string, JsonElement> changes)
    {
        lock (this)
        {
            var asset = Load(id);
            AddZeroRow(asset, rowName);
            Apply(asset, new() { [rowName] = changes });
            var path = Path.Combine(DirectoryFor(id), "additions.json");
            var names = File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))! : new List<string>();
            names.Add(rowName);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(names));
            File.Move(path + ".tmp", path, true);
            Patch(id, rowName, changes);
            return ReadRows(id).Single(r => r.RowName == rowName);
        }
    }
    private static MusicRow[] Snapshot(UAsset asset) => asset.Exports.OfType<DataTableExport>().Single().Table.Data
        .Select(row => new MusicRow(row.Name.ToString(), row.Value.Select(Field).ToArray())).ToArray();

    private static void Apply(UAsset asset, Dictionary<string, Dictionary<string, JsonElement>> drafts)
    {
        var table = asset.Exports.OfType<DataTableExport>().Single();
        foreach (var draft in drafts)
        {
            var row = table.Table.Data.SingleOrDefault(r => r.Name.ToString() == draft.Key)
                ?? throw new ArgumentException("Unknown row.");
            foreach (var edit in draft.Value)
            {
                var field = row.Value.SingleOrDefault(p => p.Name.ToString() == edit.Key)
                    ?? throw new ArgumentException("Unknown field.");
                if (Field(field).ReadOnly) throw new ArgumentException("Read-only field.");
                try
                {
                    switch (field)
                    {
                        case StrPropertyData v: v.Value = edit.Value.ValueKind == JsonValueKind.Null ? null : new FString(edit.Value.GetString()!); break;
                        case UInt64PropertyData v: v.Value = ulong.Parse(edit.Value.GetString() ?? throw new ArgumentException("uint64 requires decimal string."), CultureInfo.InvariantCulture); break;
                        case UInt32PropertyData v: v.Value = edit.Value.GetUInt32(); break;
                        case IntPropertyData v: v.Value = edit.Value.GetInt32(); break;
                        case BytePropertyData v when v.ByteType == BytePropertyType.Byte: v.Value = edit.Value.GetByte(); break;
                        case BoolPropertyData v: v.Value = edit.Value.GetBoolean(); break;
                        case FloatPropertyData v:
                            var number = edit.Value.GetSingle();
                            if (!float.IsFinite(number)) throw new ArgumentException("Number must be finite.");
                            v.Value = number; break;
                        default: throw new ArgumentException("Unsupported edit type.");
                    }
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
                { throw new ArgumentException("Invalid field value: " + edit.Key, ex); }
            }
        }
    }

    private string DirectoryFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid workspace ID.");
        return File.Exists(Path.Combine(root,id,"project-info.json")) ? Path.Combine(root,id,"working") : Path.Combine(root,id);
    }

    private static MusicField Field(PropertyData p)
    {
        object? value = p switch
        {
            UInt32PropertyData v => v.Value,
            UInt64PropertyData v => v.Value.ToString(CultureInfo.InvariantCulture),
            IntPropertyData v => v.Value,
            FloatPropertyData v => v.Value,
            BoolPropertyData v => v.Value,
            StrPropertyData v => v.Value?.ToString(),
            BytePropertyData v when v.ByteType == BytePropertyType.Byte => v.Value,
            _ => throw new InvalidDataException($"Unsupported field: {p.Name} ({p.GetType().Name}).")
        };
        var name = p.Name.ToString();
        return new(name, p.GetType().Name, value, name == "UniqueID");
    }

    public string WorkspaceDirectory(string id) => DirectoryFor(id);
    public static void RejectLinks(string path)
    {
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null)
        {
            if (current.LinkTarget is not null) throw new ArgumentException("Symbolic links are not accepted.");
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
