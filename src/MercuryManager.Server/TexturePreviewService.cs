using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;
using Microsoft.Extensions.Caching.Memory;

namespace MercuryManager.Server;

public sealed class TexturePreviewService(MusicWorkspaceStore projects) : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 });
    private readonly object gate = new();
    private static readonly Dictionary<string,Dictionary<string,string>> Bindings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(typeof(TexturePreviewService).Assembly.GetManifestResourceStream("MercuryManager.TextureBindings.json")!)!;
    public static string ResolveRelativePath(string table, string field, string value)
    {
        if(!Bindings.TryGetValue(table,out var fields)||!fields.TryGetValue(field,out var folder))throw new ArgumentException("Unsupported image field.");
        if(table=="EMoneyBrandTable" && value.StartsWith("/Game/UI/Textures/EMoney/",StringComparison.Ordinal))value=value["/Game/UI/Textures/EMoney/".Length..];
        // GATE also contains materials; do not pretend an animated material is a static image.
        if(value.StartsWith("Material/",StringComparison.Ordinal))throw new InvalidDataException("Material previews are not supported.");
        if(string.IsNullOrWhiteSpace(value)||value.Length>256||value.Contains('\\')||value.Split('/').Any(p=>p.Length==0||p is "." or ".."||p.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'&&c!='-')))
            throw new ArgumentException("Invalid texture reference.");
        return $"UI/Textures/{(folder.Length==0?"":folder+"/")}{value}.uasset";
    }
    public byte[] Load(string id,string table,string field,string value)
    {
        var relative=ResolveRelativePath(table,field,value);
        var root=projects.Get(id).ContentRoot ?? throw new InvalidOperationException("Import game tables first.");
        var path=Path.Combine(root,relative);MusicWorkspaceStore.RejectLinks(path);
        // Monitor references omit the three-digit expression index; preview its first frame only.
        if(!File.Exists(path)&&table=="NavigateCharacterTable"&&field=="MonitorTextureName"){path=Path.Combine(root,relative[..^7]+"000.uasset");MusicWorkspaceStore.RejectLinks(path);}
        if(!File.Exists(path))throw new FileNotFoundException("Texture not found.");
        var stamps=new List<string>();
        foreach(var ext in new[]{".uasset",".uexp",".ubulk"})
        {
            var file=Path.ChangeExtension(path,ext);MusicWorkspaceStore.RejectLinks(file);
            if(File.Exists(file)){var info=new FileInfo(file);stamps.Add($"{ext}:{info.Length}:{info.LastWriteTimeUtc.Ticks}");}
        }
        var key=path+string.Join('|',stamps);
        lock(gate)
        {
            if(cache.TryGetValue<byte[]>(key,out var found))return found!;
            // A narrow provider avoids indexing the entire game and follows no other directories.
            using var provider=new DefaultFileProvider(Path.GetDirectoryName(path)!,SearchOption.TopDirectoryOnly,new VersionContainer(EGame.GAME_UE4_19),StringComparer.OrdinalIgnoreCase);
            provider.Initialize();
            var file=provider.Files.Values.SingleOrDefault(f=>f.Path.EndsWith('/'+Path.GetFileName(path),StringComparison.OrdinalIgnoreCase)||f.Path.Equals(Path.GetFileName(path),StringComparison.OrdinalIgnoreCase)) ?? throw new FileNotFoundException("Texture package not found.");
            var texture=provider.LoadPackage(file.Path).GetExports().OfType<UTexture2D>().FirstOrDefault() ?? throw new InvalidDataException("Asset is not a Texture2D.");
            if(texture.PlatformData.SizeX<=0||texture.PlatformData.SizeY<=0||texture.PlatformData.SizeX>4096||texture.PlatformData.SizeY>4096)throw new InvalidDataException("Texture dimensions exceed preview limits.");
            var decoded=texture.Decode(512) ?? throw new InvalidDataException("Texture has no readable mip data.");
            using var bitmap=decoded.ToSkBitmap();using var png=bitmap.Encode(SKEncodedImageFormat.Png,100);
            var bytes=png.ToArray();cache.Set(key,bytes,new MemoryCacheEntryOptions().SetSize(bytes.Length).SetSlidingExpiration(TimeSpan.FromMinutes(10)));
            return bytes;
        }
    }
    public void Dispose()=>cache.Dispose();
}
