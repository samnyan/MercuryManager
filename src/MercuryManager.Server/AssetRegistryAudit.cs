namespace MercuryManager.Server;

public static class AssetRegistryAudit
{
    public sealed record Item(string PackageName, string[] ObjectPaths, string? File, string Status);
    public sealed record Report(int AssetCount, int RegisteredPackages, int AvailablePackages, int MatchedPackages, Item[] Missing, Item[] Unregistered, Item[] CaseMismatches);

    public static Report Check(AssetRegistryEditor.Document registry, string contentRoot, string workspace)
    {
        if (GameContent.IsPacked(contentRoot))
            throw new NotSupportedException("Resource audit currently requires loose packages; PAK contents are not scanned.");

        return ComparePackages(registry, CollectPackages(contentRoot, workspace));
    }

    private static Dictionary<string, string> CollectPackages(string contentRoot, string workspace)
    {
        var clean = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetFileName(clean) != "Content")
            throw new NotSupportedException("Resource audit requires a standard Content directory.");

        var game = Directory.GetParent(clean)!;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        ScanContent(clean, "/Game/", files);
        if (game.Parent is { } installation)
        {
            ScanContent(Path.Combine(installation.FullName, "Engine", "Content"), "/Engine/", files);
            ScanPlugins(Path.Combine(installation.FullName, "Engine", "Plugins"), files);
        }
        ScanPlugins(Path.Combine(game.FullName, "Plugins"), files);
        // Match resource resolution precedence: drafts override imported and original packages.
        ScanContent(Path.Combine(workspace, "Imported"), "/Game/", files);
        ScanContent(Path.Combine(workspace, "Resources"), "/Game/", files);
        return files;
    }

    private static void ScanContent(string root, string mount, Dictionary<string, string> files)
    {
        foreach (var file in EnumerateSafeFiles(root))
        {
            var extension = Path.GetExtension(file);
            if (!extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".umap", StringComparison.OrdinalIgnoreCase)) continue;

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            files[mount + relative[..relative.LastIndexOf('.')]] = file;
        }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string directory)
    {
        MusicWorkspaceStore.RejectLinks(directory);
        if (!Directory.Exists(directory)) yield break;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            MusicWorkspaceStore.RejectLinks(entry);
            if (Directory.Exists(entry))
            {
                foreach (var file in EnumerateSafeFiles(entry)) yield return file;
            }
            else yield return entry;
        }
    }

    private static void ScanPlugins(string root, Dictionary<string, string> files)
    {
        MusicWorkspaceStore.RejectLinks(root);
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            MusicWorkspaceStore.RejectLinks(directory);
            var content = Path.Combine(directory, "Content");
            if (Directory.Exists(content)) ScanContent(content, "/" + Path.GetFileName(directory) + "/", files);
            else ScanPlugins(directory, files);
        }
    }

    private static Report ComparePackages(AssetRegistryEditor.Document registry, Dictionary<string, string> files)
    {
        var groups = registry.Entries.GroupBy(e => e.PackageName, StringComparer.Ordinal).ToArray();
        var registered = groups.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var insensitive = files.Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var missing = new List<Item>();
        var casing = new List<Item>();
        var matched = 0;
        foreach (var group in groups)
        {
            if (files.ContainsKey(group.Key))
            {
                matched++;
                continue;
            }
            var objects = group.Select(e => e.ObjectPath).ToArray();
            if (insensitive.TryGetValue(group.Key, out var actual))
                casing.Add(new(group.Key, objects, files[actual], "case-mismatch"));
            else missing.Add(new(group.Key, objects, null, "missing"));
        }
        var unregistered = files.Where(p => !registered.Contains(p.Key))
            .Select(p => new Item(p.Key, [], p.Value, "unregistered"));
        return new(registry.Entries.Length, groups.Length, files.Count, matched,
            SortItems(missing), SortItems(unregistered), SortItems(casing));
    }

    private static Item[] SortItems(IEnumerable<Item> items) =>
        items.OrderBy(x => x.PackageName, StringComparer.Ordinal).ToArray();
}
