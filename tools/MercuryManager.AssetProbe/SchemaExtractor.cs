using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace MercuryManager.AssetProbe;

public static class SchemaExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Run(string contentRoot, string webRoot)
    {
        var tableDir = Path.Combine(contentRoot, "Table");
        var msgDir = Path.Combine(contentRoot, "Message");
        var catalogPath = Path.Combine(webRoot, "src", "tableCatalog.json");
        var localesPath = Path.Combine(webRoot, "src", "locales.json");

        var catalogJson = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(catalogPath));
        var localesJson = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(localesPath));

        var zhTables = localesJson.GetProperty("zh").GetProperty("tables");
        var enTables = localesJson.GetProperty("en").GetProperty("tables");
        var zhFields = localesJson.GetProperty("zh").GetProperty("fields");
        var enFields = localesJson.GetProperty("en").GetProperty("fields");

        // 1. 读取所有 Message 表的 RowName 集合
        Console.WriteLine("Reading Message tables...");
        var messageRows = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(msgDir, "*.uasset").OrderBy(f => f))
        {
            var msgName = Path.GetFileNameWithoutExtension(file);
            try
            {
                var asset = new UAsset(file, EngineVersion.VER_UE4_19);
                var dt = asset.Exports.OfType<DataTableExport>().FirstOrDefault();
                if (dt != null)
                {
                    var set = new HashSet<string>(dt.Table.Data.Select(r => r.Name.ToString()));
                    messageRows[msgName] = set;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to load message {msgName}: {ex.Message}");
            }
        }
        Console.WriteLine($"Loaded {messageRows.Count} message tables with row keys.");

        // 2. 读取并处理所有 Table
        var tableSchemas = new List<object>();

        foreach (var item in catalogJson.EnumerateArray())
        {
            var tableName = item.GetProperty("name").GetString()!;
            var group = item.GetProperty("group").GetString()!;
            var tableFile = Path.Combine(tableDir, tableName + ".uasset");

            var nameCn = zhTables.TryGetProperty(tableName, out var zt) ? zt.GetString()! : item.GetProperty("title").GetString()!;
            var nameEn = enTables.TryGetProperty(tableName, out var et) ? et.GetString()! : EnglishName(tableName);

            if (!File.Exists(tableFile))
            {
                Console.Error.WriteLine($"Table file not found: {tableFile}");
                continue;
            }

            try
            {
                var asset = new UAsset(tableFile, EngineVersion.VER_UE4_19);
                var dt = asset.Exports.OfType<DataTableExport>().FirstOrDefault();
                if (dt == null || dt.Table.Data.Count == 0) continue;

                var firstRow = dt.Table.Data[0];
                var fields = new List<object>();

                // 添加 RowName 虚拟主键字段
                fields.Add(new
                {
                    key = "RowName",
                    nameCn = "行键",
                    nameEn = "Row Key",
                    type = "string",
                    rawType = "FName",
                    isId = true,
                    readOnly = true,
                    messageLink = (object?)null,
                    tableMinWidth = 140,
                    tableWidthFluid = false
                });

                // 收集所有属性的名称及类型
                foreach (var prop in firstRow.Value)
                {
                    var propName = prop.Name.ToString();
                    var rawType = prop.GetType().Name;
                    var schemaType = MapType(rawType);

                    var fieldCn = zhFields.TryGetProperty(propName, out var zf) ? zf.GetString()! : EnglishName(propName);
                    var fieldEn = enFields.TryGetProperty(propName, out var ef) ? ef.GetString()! : EnglishName(propName);

                    // 分析 messageLink
                    object? messageLink = null;
                    var linkedMessage = ResolveMessageLink(tableName, propName, dt, messageRows);
                    if (linkedMessage != null)
                    {
                        messageLink = new { messageTable = linkedMessage };
                    }

                    bool fluid = Regex.IsMatch(propName, "Name|Message|Path|Text|Directory|Description", RegexOptions.IgnoreCase);
                    int minWidth = fluid ? 180 : (schemaType == "boolean" ? 100 : (schemaType == "int64" ? 150 : 120));

                    fields.Add(new
                    {
                        key = propName,
                        nameCn = fieldCn,
                        nameEn = fieldEn,
                        type = schemaType,
                        rawType = rawType,
                        isId = false,
                        readOnly = false,
                        messageLink = messageLink,
                        tableMinWidth = minWidth,
                        tableWidthFluid = fluid
                    });
                }

                tableSchemas.Add(new
                {
                    id = tableName,
                    nameCn = nameCn,
                    nameEn = nameEn,
                    group = group,
                    fields = fields
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error scanning table {tableName}: {ex.Message}");
            }
        }

        var outputPath = Path.Combine(webRoot, "src", "tableSchemas.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(tableSchemas, JsonOptions));
        Console.WriteLine($"Successfully generated schema for {tableSchemas.Count} tables to {outputPath}");
    }

    private static string MapType(string rawType) => rawType switch
    {
        "BoolPropertyData" => "boolean",
        "IntPropertyData" or "UInt32PropertyData" or "Int16PropertyData" or "Int8PropertyData" or "BytePropertyData" or "FloatPropertyData" => "number",
        "Int64PropertyData" or "UInt64PropertyData" => "int64",
        "ArrayPropertyData" => "array",
        "MapPropertyData" => "map",
        "EnumPropertyData" => "enum",
        _ => "string"
    };

    private static string? ResolveMessageLink(string tableName, string propName, DataTableExport dt, Dictionary<string, HashSet<string>> messageRows)
    {
        // 1. 用户特别指定的排除项与通用非 Message 字段
        if (tableName == "TrophyTable") return null;
        if (tableName == "GradeTable" && propName != "ExplanationTextTag") return null;
        if (Regex.IsMatch(propName, @"^Value\d+$", RegexOptions.IgnoreCase)) return null;

        // 2. 选曲设置系列统一规则
        if ((tableName.StartsWith("MusicSelectOption") || tableName == "SnapNoteColorSupportTable" || tableName == "SlideNoteColorSupportTable") &&
            (propName == "MenuName" || propName == "MenuDescription" || propName == "ParamName" || propName == "ParamMes"))
        {
            return "MusicSelectOptionMenuMessage";
        }

        // 3. 通用按键/系统提示
        if (propName is "ButtonTextTagA" or "ButtonTextTagB" or "HelpTextTag")
        {
            return "SystemMessage";
        }

        // 4. 采样行值检查
        var sampleValues = dt.Table.Data
            .Select(r => r.Value.FirstOrDefault(p => p.Name.ToString() == propName))
            .Where(p => p is StrPropertyData sp && !string.IsNullOrWhiteSpace(sp.Value?.ToString()))
            .Select(p => ((StrPropertyData)p!).Value!.ToString()!.Trim())
            .Take(10)
            .ToList();

        if (sampleValues.Count == 0) return null;

        // 检查样本值是否命中了某个 Message 表
        // 优先检查与表名同名的 Message 表
        var candidateMsgName = tableName.Replace("Table", "Message");
        if (messageRows.TryGetValue(candidateMsgName, out var candidateRows))
        {
            if (sampleValues.Any(v => candidateRows.Contains(v)))
            {
                return candidateMsgName;
            }
        }

        // 检查其它 Message 表
        foreach (var (msgName, rowSet) in messageRows)
        {
            int hits = sampleValues.Count(v => rowSet.Contains(v));
            if (hits > 0 && hits >= Math.Min(2, sampleValues.Count))
            {
                return msgName;
            }
        }

        return null;
    }

    private static string EnglishName(string name) =>
        Regex.Replace(Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1 $2"), @"([A-Z]+)([A-Z][a-z])", "$1 $2");
}
