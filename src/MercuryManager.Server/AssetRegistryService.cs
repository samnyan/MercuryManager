namespace MercuryManager.Server;

public sealed class AssetRegistryService(MusicWorkspaceStore assets)
{
    public sealed record SaveResult(string Revision);

    private string Draft(string id) => Path.Combine(assets.WorkspaceDirectory(id), "Registry", "AssetRegistry.bin");

    private string ContentRoot(string id) => assets.Get(id).ContentRoot
        ?? throw new InvalidOperationException("Import a game first.");

    private string Original(string id)
    {
        var full = Path.GetFullPath(ContentRoot(id)).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetFileName(full) != "Content")
            throw new NotSupportedException("Registry discovery requires a Content directory.");
        var target = Path.Combine(Directory.GetParent(full)!.FullName, "AssetRegistry.bin");
        MusicWorkspaceStore.RejectLinks(target);
        if (!File.Exists(target)) throw new FileNotFoundException("AssetRegistry.bin not found beside Content.");
        return target;
    }

    private string Resolve(string id)
    {
        var draft = Draft(id);
        MusicWorkspaceStore.RejectLinks(draft);
        return File.Exists(draft) ? draft : Original(id);
    }

    public bool Exists(string id)
    {
        try { return File.Exists(Resolve(id)); }
        catch (FileNotFoundException) { return false; }
    }

    public AssetRegistryEditor.Document Read(string id) => AssetRegistryEditor.Read(Download(id));

    public SaveResult Save(string id, AssetRegistryEditor.Request request)
    {
        lock (assets)
        {
            var original = Original(id);
            var draft = Draft(id);
            MusicWorkspaceStore.RejectLinks(draft);
            var source = File.Exists(draft) ? draft : original;
            var before = File.ReadAllBytes(source);
            var output = AssetRegistryEditor.Apply(before, request);
            var revision = AssetRegistryEditor.Read(output).Revision;

            EnsureSourceBaseline(original, draft);
            if (!File.ReadAllBytes(source).AsSpan().SequenceEqual(before))
                throw new InvalidOperationException("Registry draft changed during save.");
            PublishDraft(draft, output);
            return new(revision);
        }
    }

    private static void EnsureSourceBaseline(string original, string draft)
    {
        var directory = Path.GetDirectoryName(draft)!;
        var baseline = Path.Combine(directory, "AssetRegistry.source.sha256");
        MusicWorkspaceStore.RejectLinks(baseline);
        var actual = AssetRegistryEditor.ContentHash(File.ReadAllBytes(original));
        if (!File.Exists(baseline))
        {
            if (File.Exists(draft))
                throw new InvalidOperationException("This project already has a Registry draft without source-version metadata. Discard/reload it before editing to avoid overwriting stale data.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(baseline, actual);
        }
        if (!StringComparer.Ordinal.Equals(File.ReadAllText(baseline).Trim(), actual))
            throw new InvalidOperationException("Original AssetRegistry.bin changed since this project draft was created. Reload/reimport before saving.");
    }

    private static void PublishDraft(string destination, byte[] output)
    {
        MusicWorkspaceStore.RejectLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var stage = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(stage, output);
            File.Move(stage, destination, true);
        }
        finally
        {
            if (File.Exists(stage)) File.Delete(stage);
        }
    }

    public AssetRegistryAudit.Report Audit(string id)
    {
        lock (assets)
            return AssetRegistryAudit.Check(Read(id), ContentRoot(id), assets.WorkspaceDirectory(id));
    }

    public byte[] Download(string id) => File.ReadAllBytes(Resolve(id));
}
