using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Versions;

namespace MercuryManager.Server;

// Package headers may strip engine versions. Prefer the installed executable's
// explicit release marker; never infer a version merely from successful PAK mounting.
public static class AssetEngineVersion
{
    private static readonly ConcurrentDictionary<string,Detection> Versions=new(StringComparer.Ordinal);
    public static EGame ForAsset(string path)
    {
        for(var dir=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);dir!=null;dir=dir.Parent)
        {
            var metadata=Path.Combine(dir.FullName,"project-info.json");
            if(File.Exists(metadata))
            {
                var project=JsonSerializer.Deserialize<ProjectManager.Project>(File.ReadAllText(metadata));
                if(project?.EngineVersion is {} selected) return Parse(selected);
                if(project?.ContentRoot is {} root) return ForContent(root);
            }
            if(dir.Name.Equals("Content",StringComparison.OrdinalIgnoreCase))return ForContent(dir.FullName);
        }
        return EGame.GAME_UE4_19;
    }
    public sealed record Detection(string Version, bool Detected);
    public static string[] Supported => Enumerable.Range(0,28).Select(n=>$"4.{n}").Where(v=>Enum.TryParse<EGame>("GAME_UE"+v.Replace('.','_'),out _) && Enum.TryParse<UAssetAPI.UnrealTypes.EngineVersion>("VER_UE"+v.Replace('.','_'),out _)).ToArray();
    public static EGame Parse(string version) => Supported.Contains(version)
        ? Enum.Parse<EGame>("GAME_UE"+version.Replace('.','_')) : throw new ArgumentException("Unsupported engine version.");
    public static UAssetAPI.UnrealTypes.EngineVersion ForUAsset(string path) => Enum.Parse<UAssetAPI.UnrealTypes.EngineVersion>(ForAsset(path).ToString().Replace("GAME_", "VER_"));
    public static Detection Inspect(string root)
    {
        if(!Directory.Exists(root))throw new ArgumentException("Content directory does not exist.");
        return Versions.GetOrAdd(Path.GetFullPath(root),Detect);
    }
    public static EGame ForContent(string root)=>Parse(Inspect(root).Version);
    private static Detection Detect(string root)
    {
        var binaries=Path.Combine(Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar))!.FullName,"Binaries","Win64");
        if(!Directory.Exists(binaries))return new("4.19",false);
        var found=new HashSet<EGame>();
        foreach(var exe in Directory.EnumerateFiles(binaries,"*-Shipping.exe"))
        {
            using var input=File.OpenRead(exe);
            var buffer=new byte[1024*1024+256];int carry=0,read;
            while((read=input.Read(buffer,carry,buffer.Length-carry))>0)
            {
                var length=carry+read;
                // ASCII and UTF-16LE release strings; preserve chunk overlap.
                var text=Encoding.Latin1.GetString(buffer,0,length).Replace("\0","");
                foreach(Match match in Regex.Matches(text,@"\+\+UE4\+Release-4\.(\d{1,2})-"))
                    if(Enum.TryParse<EGame>("GAME_UE4_"+match.Groups[1].Value,out var version))found.Add(version);
                carry=Math.Min(256,length);Buffer.BlockCopy(buffer,length-carry,buffer,0,carry);
            }
        }
        return found.Count==1?new(found.Single().ToString().Replace("GAME_UE", "").Replace('_','.'),true):new("4.19",false);
    }
}
