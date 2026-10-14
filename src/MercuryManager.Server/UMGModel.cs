using System.Text.Json;
using System.Text.Json.Serialization;

namespace MercuryManager.Server;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UMGDocument))]
internal partial class UMGJsonContext : JsonSerializerContext;

public sealed record UMGIssue(string Code,string Severity,string? NodeId,string Message);
public sealed record UMGResource(string Role,string AssetPath,string? ClassName);
public sealed record UMGSlot(int ExportIndex,string ClassName,Dictionary<string,object?> Values);
public sealed record UMGPreview(string Layout,string Appearance,List<string> Issues);
public sealed record UMGChildWidget(string? AssetPath,string? LocalTreeId);
public sealed record UMGNode(string Id,int ExportIndex,string TreeId,string Name,string ClassName,
    string? ParentId,List<string> Children,UMGSlot? Slot,Dictionary<string,object?> Visual,
    Dictionary<string,string> Sources,List<UMGResource> Resources,UMGChildWidget? ChildWidget,UMGPreview Preview);
public sealed record UMGRoot(string Id,string TreeId,string? RootNodeId,string OwnerName,string Kind);
public sealed record UMGAnimationKey(double Frame,double Value,string Interpolation,double ArriveTangent,double LeaveTangent);
public sealed record UMGAnimationChannel(string Property,string ChannelType,double DefaultValue,bool HasDefault,List<UMGAnimationKey> Keys);
public sealed record UMGAnimationMaterialParameter(string Name,int Index,UMGAnimationChannel Channel);
public sealed record UMGAnimationTrack(string Name,string Type,string? Property,string TargetGuid,List<UMGAnimationChannel> Channels,List<UMGAnimationMaterialParameter>? MaterialParameters=null);
public sealed record UMGAnimation(string Id,string Name,double Start,double End,double TickRate,double DisplayRate,List<UMGAnimationBinding> Bindings,List<UMGAnimationTrack> Tracks,string? OwnerId=null,string? OwnerPath=null,string? OwnerTreeId=null,string? Scope=null);
public sealed record UMGAnimationBinding(string Guid,string WidgetName,string? SlotWidgetName,bool IsRoot);
public sealed record UMGDocument(int SchemaVersion,string Revision,string AssetPath,List<UMGRoot> Roots,
    string? SuggestedRootId,List<UMGNode> Nodes,List<UMGIssue> Diagnostics,Dictionary<string,System.Text.Json.JsonElement>? Materials=null,
    List<UMGAnimation>? Animations=null,Dictionary<string,System.Text.Json.JsonElement>? Particles=null);
