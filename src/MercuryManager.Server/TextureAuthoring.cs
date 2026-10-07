using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using SkiaSharp;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
namespace MercuryManager.Server;

// Deliberately restricted cooked UE4.19 layout; unknown layouts fail closed.
public static class TextureAuthoring
{
    public sealed record Layout(int Width,int Height,int Pixels,int Length,int Skip,int Bulk);
    public static Layout Inspect(byte[] extra)
    {
        using var r=new BinaryReader(new MemoryStream(extra));
        void Require(bool condition){if(!condition)throw new InvalidDataException("Unsupported texture: requires single inline BGRA mip (UE4.19).");}
        Require(r.ReadUInt16()==1 && r.ReadUInt16()==1 && r.ReadInt32()==1);
        r.ReadInt32();Require(r.ReadInt32()==0);int skip=(int)r.BaseStream.Position;r.ReadInt32();
        int w=r.ReadInt32(),h=r.ReadInt32();Require(w>0&&h>0&&w<=4096&&h<=4096&&r.ReadInt32()==1);
        int length=r.ReadInt32();Require(length==12);Require(System.Text.Encoding.ASCII.GetString(r.ReadBytes(length))=="PF_B8G8R8A8\0");
        Require(r.ReadInt32()==0&&r.ReadInt32()==1&&r.ReadInt32()==1&&r.ReadUInt32()==0x48);
        int bytes=checked(w*h*4);Require(r.ReadInt32()==bytes&&r.ReadInt32()==bytes);
        int bulk=(int)r.BaseStream.Position;r.ReadInt64();int start=(int)r.BaseStream.Position;
        Require(extra.Length==start+bytes+16);r.BaseStream.Position=start+bytes;
        Require(r.ReadInt32()==w&&r.ReadInt32()==h);r.ReadInt32();Require(r.ReadInt32()==0);
        return new(w,h,start,bytes,skip,bulk);
    }
    public static UTexture2D Read(string path,out DefaultFileProvider provider)
    {
        provider=new DefaultFileProvider(Path.GetDirectoryName(path)!,SearchOption.TopDirectoryOnly,new VersionContainer(EGame.GAME_UE4_19),StringComparer.OrdinalIgnoreCase);
        try{provider.Initialize();var file=provider.Files.Values.Single(f=>f.Path.EndsWith('/'+Path.GetFileName(path),StringComparison.OrdinalIgnoreCase)||f.Path.Equals(Path.GetFileName(path),StringComparison.OrdinalIgnoreCase));return provider.LoadPackage(file.Path).GetExports().OfType<UTexture2D>().Single();}catch{provider.Dispose();throw;}
    }
    public static byte[] Preview(string path)
    {
        var t=Read(path,out var provider);using(provider){if(t.PlatformData.SizeX>4096||t.PlatformData.SizeY>4096)throw new InvalidDataException("Texture too large.");using var bmp=t.Decode(512)?.ToSkBitmap()??throw new InvalidDataException("No readable image.");using var png=bmp.Encode(SKEncodedImageFormat.Png,100);return png.ToArray();}
    }
    public static void Build(string template,byte[] image,string relative,string output)
    {
        MusicWorkspaceStore.RejectLinks(template);
        var a=new UAsset(template,EngineVersion.VER_UE4_19);
        if(!a.VerifyBinaryEquality()||a.Exports.Count!=1||a.Exports[0] is not NormalExport n||n.GetExportClassType().ToString()!="Texture2D")throw new InvalidDataException("Unsupported texture template.");
        var layout=Inspect(n.Extras);var original=n.ObjectName.ToString();var target=Path.GetFileNameWithoutExtension(relative);
        using var codec=SKCodec.Create(new SKMemoryStream(image))??throw new InvalidDataException("Invalid image.");
        if(codec.Info.Width<=0||codec.Info.Height<=0||(long)codec.Info.Width*codec.Info.Height>16777216)throw new InvalidDataException("Image too large.");
        using var source=SKBitmap.Decode(image)??throw new InvalidDataException("Invalid image.");
        using var resized=new SKBitmap(new SKImageInfo(layout.Width,layout.Height,SKColorType.Bgra8888,SKAlphaType.Unpremul));
        using(var canvas=new SKCanvas(resized)){using var paint=new SKPaint{FilterQuality=SKFilterQuality.High,BlendMode=SKBlendMode.Src};canvas.DrawBitmap(source,new SKRect(0,0,layout.Width,layout.Height),paint);}
        var pixels=resized.Bytes;pixels.CopyTo(n.Extras,layout.Pixels);
        var names=a.GetNameMapIndexList().ToArray();
        for(int i=0;i<names.Length;i++){var value=names[i].ToString();if(value==original)a.SetNameReference(i,new FString(target));else if(value.StartsWith("/Game/")&&value.EndsWith('/'+original,StringComparison.Ordinal))a.SetNameReference(i,new FString("/Game/"+relative[..^7]));}
        n.ObjectName=new FName(a,target);
        // First write measures relocation introduced by name-map changes. Patch absolute offsets before final write.
        a.Write(output);var measured=new UAsset(output,EngineVersion.VER_UE4_19);
        // UAsset.Write mutates SerialOffset, so measure opaque pointers against serialized payload location instead.
        var oldPayload=BitConverter.ToInt64(n.Extras,layout.Bulk);long newPayload=measured.Exports[0].SerialOffset+measured.Exports[0].SerialSize-n.Extras.Length+layout.Pixels;
        long delta=newPayload-oldPayload;
        BitConverter.GetBytes(checked((int)(BitConverter.ToInt32(n.Extras,layout.Skip)+delta))).CopyTo(n.Extras,layout.Skip);
        BitConverter.GetBytes(newPayload).CopyTo(n.Extras,layout.Bulk);a.Write(output);
        var reread=new UAsset(output,EngineVersion.VER_UE4_19);if(!reread.VerifyBinaryEquality()||reread.Exports.Single().ObjectName.ToString()!=target)throw new InvalidDataException("Written asset identity verification failed.");
        var texture=Read(output,out var provider);using(provider){var data=texture.PlatformData;if(data.SizeX!=layout.Width||data.SizeY!=layout.Height||data.PixelFormat!="PF_B8G8R8A8"||data.Mips.Length!=1||!data.Mips[0].BulkData!.Data!.SequenceEqual(pixels))throw new InvalidDataException("Written texture pixel verification failed.");}
    }
}
