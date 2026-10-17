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
    public sealed record ShaderCandidate(string Hash,string Stage,int Offset,byte[] Bytecode)
    {
        public byte[] ResourceTablePrefix { get; init; } = [];
    }
    public sealed record Translation(string Hash,string Stage,string Glsl,string Reflection,string[] Diagnostics)
    {
        public IReadOnlyList<MaterialCombinedSampler> CombinedSamplers { get; init; } = [];
        public string[] RequiredAdapters { get; init; } = [];
    }
    private const int MaxDecodedBytes=4*1024*1024;
    private const int MaxCachedTranslations = 16;
    private static readonly object TranslationLock = new();
    private static readonly Dictionary<string, Lazy<Task<Translation>>> Translations = new(StringComparer.Ordinal);

    // 只缓存成功转译所需的共享任务；调用者取消等待不能中断其他请求。
    public static async Task<Translation> Translate(ShaderCandidate candidate, string cacheRoot, CancellationToken cancellationToken = default, bool diagnosticSamplers = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Convert.ToHexStringLower(SHA256.HashData(candidate.Bytecode)) + (diagnosticSamplers ? ":diagnostic" : ":2d");
        Lazy<Task<Translation>> job;
        lock (TranslationLock)
        {
            if (!Translations.TryGetValue(key, out job!))
            {
                if (Translations.Count >= MaxCachedTranslations)
                {
                    var finished = Translations.FirstOrDefault(x => x.Value.IsValueCreated && x.Value.Value.IsCompleted);
                    if (finished.Key != null) Translations.Remove(finished.Key);
                }
                // 全部正在转译时不增加持久缓存，避免无界增长。
                job = new(() => TranslateUncached(candidate, cacheRoot, CancellationToken.None, diagnosticSamplers), LazyThreadSafetyMode.ExecutionAndPublication);
                if (Translations.Count < MaxCachedTranslations) Translations.Add(key, job);
            }
        }
        var task = job.Value;
        try { return await task.WaitAsync(cancellationToken); }
        catch
        {
            if (task.IsFaulted || task.IsCanceled)
                lock (TranslationLock)
                    if (Translations.TryGetValue(key, out var current) && ReferenceEquals(current, job)) Translations.Remove(key);
            throw;
        }
    }

    // Discovery only: offsets are NOT shader-map identities or proof of a Slate permutation.
    public static IReadOnlyList<ShaderCandidate> Discover(string path)
    {
        var asset=new UAsset(path,AssetEngineVersion.ForUAsset(path));
        var export=asset.Exports.OfType<NormalExport>().SingleOrDefault(e=>e.GetExportClassType().ToString()=="Material")
            ??throw new InvalidDataException("Compiled shaders must be read from a master Material asset.");
        return Discover(export.Extras??[]);
    }

    public static IReadOnlyList<ShaderCandidate> Discover(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        // 相同字节码可属于不同排列；必须保留各目录位置供身份选择。
        var found=new Dictionary<(string Hash, int Offset),ShaderCandidate>();
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
                    found.TryAdd((hash,offset),new(hash,stage,offset,bytecode)
                        { ResourceTablePrefix = bytes[..start] });
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

    private static async Task<Translation> TranslateUncached(ShaderCandidate candidate,string cacheRoot,CancellationToken cancellationToken,bool diagnosticSamplers)
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
            await Run(MaterialShaderToolchain.GetVkd3dCompilerPath(),["-x","dxbc-tpf","-b","spirv-binary",dxbc,"-o",spirv],cancellationToken);
            await Run(MaterialShaderToolchain.GetSpirvCrossPath(),[spirv,"--reflect","--output",reflection],cancellationToken);
            var reflected = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(reflection,cancellationToken))!;
            var arguments = new List<string> { spirv, "--es", "--version", "300", "--output", glsl };
            if (diagnosticSamplers && candidate.Stage is "vertex" or "fragment")
            {
                string direction = candidate.Stage == "vertex" ? "out" : "in";
                string field = candidate.Stage == "vertex" ? "outputs" : "inputs";
                var locations = new HashSet<int>();
                foreach (var variable in reflected[field]?.AsArray() ?? [])
                {
                    int location = variable!["location"]?.GetValue<int>()
                        ?? throw new NotSupportedException("Particle interface variable has no location.");
                    if (location < 0 || !locations.Add(location))
                        throw new NotSupportedException("Particle interface locations are ambiguous.");
                    string name = $"mercury_varying_{location}";
                    arguments.AddRange(["--rename-interface-variable", direction,
                        location.ToString(System.Globalization.CultureInfo.InvariantCulture), name]);
                    variable["name"] = name;
                }
            }
            bool adaptedSprite = false;
            try { await Run(MaterialShaderToolchain.GetSpirvCrossPath(),arguments.ToArray(),cancellationToken); }
            catch (InvalidDataException ex) when (diagnosticSamplers && candidate.Stage == "vertex" &&
                ex.Message.Contains("BaseVertex not supported in ES profile", StringComparison.Ordinal))
            {
                var desktopArguments = arguments.ToArray();
                desktopArguments[1] = "--no-es";
                desktopArguments[3] = "330";
                await Run(MaterialShaderToolchain.GetSpirvCrossPath(), desktopArguments, cancellationToken);
                var adapted = ParticleSpriteEsAdapter.Adapt(await File.ReadAllTextAsync(glsl, cancellationToken));
                await File.WriteAllTextAsync(glsl, adapted, cancellationToken);
                adaptedSprite = true;
            }
            var source = await File.ReadAllTextAsync(glsl,cancellationToken);
            var preview = diagnosticSamplers
                ? ParticleCubeArrayPreviewAdapter.Adapt(source, candidate.Stage == "fragment")
                : new ParticleCubeArrayPreviewAdapter.Result(source, []);
            var buffers = diagnosticSamplers
                ? ParticleBufferTextureAdapter.Adapt(preview.Source)
                : new ParticleCubeArrayPreviewAdapter.Result(preview.Source, []);
            source = diagnosticSamplers ? ParticleBitfieldAdapter.Adapt(buffers.Source) : buffers.Source;
            var samplers = MaterialCombinedSamplerReader.Read(await File.ReadAllBytesAsync(spirv,cancellationToken), source, diagnosticSamplers);
            foreach (var ubo in reflected["ubos"]!.AsArray())
            {
                int slot = ubo!["binding"]!.GetValue<int>();
                var blocks = System.Text.RegularExpressions.Regex.Matches(source,
                    @"uniform\s+(\w+)\s*\{[^}]*\}\s*cb" + slot + @"_0(?:_\d+)?\s*;");
                if (blocks.Count != 1) throw new NotSupportedException("Translated UBO identity is not unique.");
                ubo["name"] = blocks[0].Groups[1].Value;
            }
            return new(candidate.Hash,candidate.Stage,source,reflected.ToJsonString(),
                ["Translation metadata does not establish native resource-table mapping or game rendering fidelity.", ..preview.Limitations, ..buffers.Limitations])
                { CombinedSamplers = samplers, RequiredAdapters = adaptedSprite
                    ? ["Explicit SPIRV_Cross_BaseVertex integer uniform.", "Cutout t0 requires a one-row RG32F sampler2D with exact texel indices; reflection describes the original buffer texture.", "Desktop330 arithmetic lowering is diagnostic; native rounding parity is unverified."] : [] };
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
