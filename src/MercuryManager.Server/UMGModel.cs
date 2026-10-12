namespace MercuryManager.Server;

public sealed record UMGIssue(string Code,string Severity,string? NodeId,string Message);
public sealed record UMGResource(string Role,string AssetPath,string? ClassName);
public sealed record UMGSlot(int ExportIndex,string ClassName,Dictionary<string,object?> Values);
public sealed record UMGPreview(string Layout,string Appearance,List<string> Issues);
public sealed record UMGChildWidget(string? AssetPath,string? LocalTreeId);
public sealed record UMGNode(string Id,int ExportIndex,string TreeId,string Name,string ClassName,
    string? ParentId,List<string> Children,UMGSlot? Slot,Dictionary<string,object?> Visual,
    Dictionary<string,string> Sources,List<UMGResource> Resources,UMGChildWidget? ChildWidget,UMGPreview Preview);
public sealed record UMGRoot(string Id,string TreeId,string? RootNodeId,string OwnerName,string Kind);
public sealed record UMGDocument(int SchemaVersion,string Revision,string AssetPath,List<UMGRoot> Roots,
    string? SuggestedRootId,List<UMGNode> Nodes,List<UMGIssue> Diagnostics);
