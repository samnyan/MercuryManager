using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

namespace MercuryManager.Server;

/// <summary>Local, on-demand translation of user-owned cooked shader code. No generated shaders are shipped.</summary>
public static class MaterialShaderCompiler
{
    public sealed record ShaderCandidate(string Hash,string Stage,int Offset,byte[] Bytecode);
    public sealed record Translation(string Hash,string Stage,string Glsl,string Reflection,string[] Diagnostics);
    private const int MaxDecodedBytes=4*1024*1024;

    // Discovery only: offsets are NOT shader-map identities or proof of a Slate permutation.
    public static IReadOnlyList<ShaderCandidate> Discover(string path)
    {
        var asset=new UAsset(path,EngineVersion.VER_UE4_19);
        var export=asset.Exports.OfType<NormalExport>().SingleOrDefault(e=>e.GetExportClassType().ToString()=="Material")
            ??throw new InvalidDataException("Compiled shaders must be read from a master Material asset.");
        var data=export.Extras??[];
        var found=new Dictionary<string,ShaderCandidate>(StringComparer.Ordinal);
        for(int offset=0;offset<data.Length-2;offset++)
        {
            if(data[offset]!=0x78||((data[offset]<<8|data[offset+1])%31)!=0)continue;
            try
            {
                using var z=new ZLibStream(new MemoryStream(data,offset,data.Length-offset,false),CompressionMode.Decompress);
                using var decoded=new MemoryStream();
                var buffer=new byte[8192];
                int count;
                while((count=z.Read(buffer))>0)
                {
                    if(decoded.Length+count>MaxDecodedBytes)throw new InvalidDataException("Shader discovery decompression limit exceeded.");
                    decoded.Write(buffer,0,count);
                }
                var bytes=decoded.ToArray();
                for(int start=0;start<=bytes.Length-32;start++)
                {
                    if(!bytes.AsSpan(start,4).SequenceEqual("DXBC"u8))continue;
                    if(!TryReadContainer(bytes.AsSpan(start),out var bytecode,out var stage))continue;
                    var hash=Convert.ToHexStringLower(SHA256.HashData(bytecode));
                    found.TryAdd(hash,new(hash,stage,offset,bytecode));
                    start+=bytecode.Length-1;
                }
            }
            catch(Exception ex) when(ex is IOException or InvalidDataException) { /* false-positive zlib headers */ }
        }
        return found.Values.OrderBy(x=>x.Offset).ToArray();
    }

    internal static bool TryReadContainer(ReadOnlySpan<byte> input,out byte[] bytecode,out string stage)
    {
        bytecode=[];stage="unknown";
        if(input.Length<32||!input[..4].SequenceEqual("DXBC"u8))return false;
        uint size=BinaryPrimitives.ReadUInt32LittleEndian(input[24..]);
        uint chunks=BinaryPrimitives.ReadUInt32LittleEndian(input[28..]);
        if(size<32||size>input.Length||chunks>128||32L+chunks*4L>size)return false;
        for(int i=0;i<chunks;i++)
        {
            uint offset=BinaryPrimitives.ReadUInt32LittleEndian(input[(32+i*4)..]);
            if(offset<32+chunks*4L||offset+8L>size)return false;
            uint length=BinaryPrimitives.ReadUInt32LittleEndian(input[(int)(offset+4)..]);
            if(offset+8L+length>size)return false;
            var tag=input.Slice((int)offset,4);
            if(tag.SequenceEqual("SHEX"u8)||tag.SequenceEqual("SHDR"u8))
            {
                if(length<8)return false;
                uint version=BinaryPrimitives.ReadUInt32LittleEndian(input[(int)(offset+8)..]);
                stage=(version>>16) switch {0=>"fragment",1=>"vertex",2=>"geometry",3=>"hull",4=>"domain",5=>"compute",_=>"unknown"};
            }
        }
        if(stage=="unknown")return false;
        bytecode=input[..(int)size].ToArray();return true;
    }

    public static async Task<Translation> Translate(ShaderCandidate candidate,string cacheRoot,CancellationToken cancellationToken=default)
    {
        // Isolate each job; caches belong outside project drafts and distribution directories.
        Directory.CreateDirectory(cacheRoot);
        var job=Path.Combine(cacheRoot,"shader-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        try
        {
            var dxbc=Path.Combine(job,"source.dxbc");var spirv=Path.Combine(job,"program.spv");
            var glsl=Path.Combine(job,"program.glsl");var reflection=Path.Combine(job,"reflection.json");
            await File.WriteAllBytesAsync(dxbc,candidate.Bytecode,cancellationToken);
            await Run("vkd3d-compiler",["-x","dxbc-tpf","-b","spirv-binary",dxbc,"-o",spirv],cancellationToken);
            await Run("spirv-cross",[spirv,"--es","--version","300","--output",glsl],cancellationToken);
            await Run("spirv-cross",[spirv,"--reflect","--output",reflection],cancellationToken);
            return new(candidate.Hash,candidate.Stage,await File.ReadAllTextAsync(glsl,cancellationToken),await File.ReadAllTextAsync(reflection,cancellationToken),
                ["Bytecode translated; UE uniform-expression evaluation and shader-map permutation identity are not yet resolved. Do not render with guessed bindings."]);
        }
        finally {Directory.Delete(job,true);}
    }

    private static async Task Run(string tool,string[] arguments,CancellationToken cancellationToken)
    {
        var start=new ProcessStartInfo(tool){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true};
        foreach(var arg in arguments)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new InvalidOperationException($"Unable to start {tool}.");
        var stderr=process.StandardError.ReadToEndAsync(cancellationToken);
        var stdout=process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try {await process.WaitForExitAsync(timeout.Token);}
        catch {if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);throw;}
        await stdout;
        if(process.ExitCode!=0)throw new InvalidDataException($"{tool} failed ({process.ExitCode}): {await stderr}");
        await stderr;
    }
}
