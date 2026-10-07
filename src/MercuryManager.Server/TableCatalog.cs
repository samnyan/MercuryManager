using System.Text.Json;
namespace MercuryManager.Server;
public static class TableCatalog
{
    public static readonly string[] Names = ReadNames();
    private static string[] ReadNames()
    {
        using var stream=typeof(TableCatalog).Assembly.GetManifestResourceStream("MercuryManager.TableCatalog.json")??throw new InvalidDataException("Missing table catalog.");
        using var json=JsonDocument.Parse(stream);
        return json.RootElement.EnumerateArray().Select(t=>t.GetProperty("name").GetString()!).Where(n=>n!="MusicParameterTable").ToArray();
    }
}
