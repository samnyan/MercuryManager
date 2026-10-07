using System.Text.Json;
using System.Security.Cryptography;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
namespace MercuryManager.Server;

public sealed class MessageWorkspaceStore(MusicWorkspaceStore projects)
{
    private string SourceRoot(string id) => Directory.Exists(Path.Combine(Root(id),"Imported")) ? Path.Combine(Root(id),"Imported") : projects.Get(id).ContentRoot!;
    private string Root(string id) => projects.WorkspaceDirectory(id);
    public string[] List(string id)
    {
        var directory = Path.Combine(SourceRoot(id), "Message");
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*.uasset").Select(Path.GetFileNameWithoutExtension).Order().ToArray()!;
    }
    public string[] Tables(string id) => TableCatalog.Names.Where(name => File.Exists(Path.Combine(SourceRoot(id), "Table", name + ".uasset"))).ToArray();
    private static string Folder(string name) => name.StartsWith("Table__", StringComparison.Ordinal) ? "Table" : "Message";
    private static string AssetName(string name) => name.StartsWith("Table__", StringComparison.Ordinal) ? name[7..] : name;
    private string Base(string id, string name)
    {
        if (!(Folder(name) == "Table" ? Tables(id).Contains(AssetName(name)) : List(id).Contains(name))) throw new ArgumentException("Unknown table.");
        var directory = Path.Combine(Root(id), "Message", name);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".uasset");
        if (!File.Exists(path))
        {
            foreach (var ext in new[]{".uasset",".uexp"})
            {
                var source = Path.Combine(SourceRoot(id), Folder(name), AssetName(name) + ext);
                MusicWorkspaceStore.RejectLinks(source);
                File.Copy(source, Path.ChangeExtension(path,ext), false);
            }
        }
        return path;
    }
    private string Draft(string id,string name) => Path.Combine(Path.GetDirectoryName(Base(id,name))!, "draft.json");
    private Dictionary<string,Dictionary<string,JsonElement>> Drafts(string id,string name) => File.Exists(Draft(id,name)) ? JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,JsonElement>>>(File.ReadAllText(Draft(id,name)))! : new();
    private UAsset Load(string id,string name,bool apply=true)
    {
        var asset = new UAsset(Base(id,name),EngineVersion.VER_UE4_19);
        if (asset.Exports.Any(e=>e is RawExport) || asset.Exports.OfType<DataTableExport>().Count()!=1) throw new InvalidDataException("Unsupported Message asset.");
        if(apply) foreach(var row in Drafts(id,name)) Apply(asset,row.Key,row.Value);
        return asset;
    }
    private static void Apply(UAsset asset,string rowName,Dictionary<string,JsonElement> changes)
    {
        var table=asset.Exports.OfType<DataTableExport>().Single();
        var row=table.Table.Data.SingleOrDefault(r=>r.Name.ToString()==rowName);
        if(row is null)
        {
            row=(StructPropertyData)table.Table.Data[0].Clone(); row.Name=FName.FromString(asset,rowName);
            foreach(var p in row.Value)
            {
                if(p is EnumPropertyData)continue;
                using var empty=JsonDocument.Parse(p switch {StrPropertyData=>"\"\"",BoolPropertyData=>"false",ArrayPropertyData or MapPropertyData=>"[]",_=>"0"});PropertyCodec.Set(p,empty.RootElement,asset);
            }
            table.Table.Data.Add(row);
        }
        foreach(var pair in changes) PropertyCodec.Set(row.Value.SingleOrDefault(p=>p.Name.ToString()==pair.Key) ?? throw new ArgumentException("Unknown field."),pair.Value,asset);
    }
    private static MusicRow[] Rows(UAsset a) => a.Exports.OfType<DataTableExport>().Single().Table.Data.Select(r=>new MusicRow(r.Name.ToString(),r.Value.Select(p=>new MusicField(p.Name.ToString(),p.GetType().Name,PropertyCodec.Value(p),false)).ToArray())).ToArray();
    public MusicRow[] Read(string id,string name)=>Rows(Load(id,name));
    public void Edit(string id,string name,string rowName,Dictionary<string,JsonElement> changes,bool add)
    {
        lock(this)
        {
            if(string.IsNullOrWhiteSpace(rowName) || rowName.Length>256) throw new ArgumentException("Row key required (maximum 256 characters).");
            var asset=Load(id,name);var exists=Rows(asset).Any(r=>r.RowName==rowName);
            if(add==exists) throw new ArgumentException(add ? "Duplicate row key." : "Unknown row key.");
            Apply(asset,rowName,changes);Rows(asset);
            var drafts=Drafts(id,name);
            if(!drafts.TryGetValue(rowName,out var row)) drafts[rowName]=row=new();
            foreach(var pair in changes) row[pair.Key]=pair.Value.Clone();
            var path=Draft(id,name);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(drafts));File.Move(path+".tmp",path,true);
        }
    }
    private IEnumerable<string> All(string id) => List(id).Concat(Tables(id).Select(n=>"Table__"+n));
    public string[] Changes(string id) => All(id).Where(n=>File.Exists(Path.Combine(Root(id),"Message",n,"draft.json"))).SelectMany(n=>new[]{Folder(n)+"/"+AssetName(n)+".uasset",Folder(n)+"/"+AssetName(n)+".uexp"}).ToArray();
    public string[] Write(string id,string mode,string? output,bool backup)
    {
        lock(this)
        {
            if(mode is not ("overwrite" or "directory"))throw new ArgumentException("Invalid mode.");
            var content=projects.Get(id).ContentRoot!;
            if(mode=="directory" && (string.IsNullOrWhiteSpace(output)||!Path.IsPathFullyQualified(output)))throw new ArgumentException("Absolute output directory required.");
            var destination=mode=="overwrite" ? content : Path.GetFullPath(output!);
            if(mode=="directory" && destination==content)throw new ArgumentException("Use overwrite mode.");
            var written=new List<string>();
            foreach(var name in All(id).Where(n=>File.Exists(Path.Combine(Root(id),"Message",n,"draft.json"))))
            {
                var input=Base(id,name);var asset=Load(id,name);var expected=JsonSerializer.Serialize(Rows(asset));
                var stage=Path.Combine(Path.GetDirectoryName(input)!,"stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
                try
                {
                    var staged=Path.Combine(stage,name+".uasset");asset.Write(staged);
                    if(JsonSerializer.Serialize(Rows(new UAsset(staged,EngineVersion.VER_UE4_19)))!=expected)throw new InvalidDataException("Message export validation failed.");
                    var target=Path.Combine(destination,Folder(name),AssetName(name)+".uasset");
                    var old=new Dictionary<string,byte[]?>();
                    foreach(var ext in new[]{".uasset",".uexp"})
                    {
                        var path=Path.ChangeExtension(target,ext);MusicWorkspaceStore.RejectLinks(path);MusicWorkspaceStore.RejectLinks(path+"_bak");
                        if(mode=="overwrite" && !File.ReadAllBytes(Path.ChangeExtension(input,ext)).SequenceEqual(File.ReadAllBytes(path)))throw new InvalidDataException("Source Message changed externally.");
                        if(mode=="overwrite" && backup && File.Exists(path+"_bak"))throw new IOException("Backup exists: "+path+"_bak");
                        old[path]=File.Exists(path)?File.ReadAllBytes(path):null;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if(mode=="overwrite" && backup) foreach(var pair in old)File.WriteAllBytes(pair.Key+"_bak",pair.Value!);
                    try
                    {
                        foreach(var pair in old)File.Copy(Path.ChangeExtension(staged,Path.GetExtension(pair.Key)),pair.Key,true);
                        if(JsonSerializer.Serialize(Rows(new UAsset(target,EngineVersion.VER_UE4_19)))!=expected)throw new InvalidDataException("Message write validation failed.");
                    }
                    catch {foreach(var pair in old){if(pair.Value is null)File.Delete(pair.Key);else File.WriteAllBytes(pair.Key,pair.Value);}throw;}
                    written.AddRange(old.Keys);
                    if(mode=="overwrite") {foreach(var ext in new[]{".uasset",".uexp"})File.Copy(Path.ChangeExtension(target,ext),Path.ChangeExtension(input,ext),true);File.Delete(Draft(id,name));}
                }
                finally{Directory.Delete(stage,true);}
            }
            return written.ToArray();
        }
    }
}
