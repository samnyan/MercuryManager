using System.Collections.Concurrent;
using System.Text.Json;

namespace MercuryManager.Server;

public sealed record VoiceMatch(string Table, string RowKey, string JapaneseMessage);

public sealed class VoiceLookupService(MessageWorkspaceStore messages)
{
    private sealed record CacheEntry(DateTime CheckedUtc, Dictionary<string, List<VoiceMatch>> Index);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public void Invalidate(string workspaceId)
    {
        _cache.TryRemove(workspaceId, out _);
    }

    private Dictionary<string, List<VoiceMatch>> BuildIndex(string workspaceId)
    {
        var dict = new Dictionary<string, List<VoiceMatch>>(StringComparer.OrdinalIgnoreCase);
        var tableNames = messages.List(workspaceId);

        foreach (var tableName in tableNames)
        {
            try
            {
                var rows = messages.Read(workspaceId, tableName);
                if (rows.Length == 0) continue;

                var first = rows[0];
                var hasVoiceCol = first.Fields.Any(f => string.Equals(f.Name, "VoiceIdArray", StringComparison.OrdinalIgnoreCase));
                if (!hasVoiceCol) continue;

                foreach (var row in rows)
                {
                    var voiceField = row.Fields.FirstOrDefault(f => string.Equals(f.Name, "VoiceIdArray", StringComparison.OrdinalIgnoreCase));
                    if (voiceField?.Value is null) continue;

                    var japaneseField = row.Fields.FirstOrDefault(f => string.Equals(f.Name, "JapaneseMessage", StringComparison.OrdinalIgnoreCase));
                    var japanese = japaneseField?.Value?.ToString() ?? "";

                    IEnumerable<string> voiceIds = voiceField.Value switch
                    {
                        object[] arr => arr.Select(o => o?.ToString()!).Where(s => !string.IsNullOrEmpty(s)),
                        JsonElement je when je.ValueKind == JsonValueKind.Array => je.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)),
                        IEnumerable<object> objList => objList.Select(o => o?.ToString()!).Where(s => !string.IsNullOrEmpty(s)),
                        _ => []
                    };

                    foreach (var vId in voiceIds)
                    {
                        if (string.IsNullOrWhiteSpace(vId)) continue;
                        if (!dict.TryGetValue(vId, out var list))
                        {
                            list = [];
                            dict[vId] = list;
                        }
                        list.Add(new VoiceMatch(tableName, row.RowName, japanese));
                    }
                }
            }
            catch
            {
                // Ignore errors reading a specific table
            }
        }

        return dict;
    }

    public VoiceMatch[] Lookup(string workspaceId, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(voiceId)) return [];

        var entry = _cache.GetOrAdd(workspaceId, id => new CacheEntry(DateTime.UtcNow, BuildIndex(id)));
        if (entry.Index.TryGetValue(voiceId, out var matches))
        {
            return matches.ToArray();
        }

        return [];
    }

    public Dictionary<string, VoiceMatch[]> LookupBatch(string workspaceId, string[] voiceIds)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || voiceIds == null || voiceIds.Length == 0)
            return new(StringComparer.OrdinalIgnoreCase);

        var entry = _cache.GetOrAdd(workspaceId, id => new CacheEntry(DateTime.UtcNow, BuildIndex(id)));
        var res = new Dictionary<string, VoiceMatch[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var vId in voiceIds)
        {
            if (string.IsNullOrWhiteSpace(vId)) continue;
            if (entry.Index.TryGetValue(vId, out var matches))
            {
                res[vId] = matches.ToArray();
            }
            else
            {
                res[vId] = [];
            }
        }

        return res;
    }
}
