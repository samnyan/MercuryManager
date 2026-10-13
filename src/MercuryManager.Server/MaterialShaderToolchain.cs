using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MercuryManager.Server;

public sealed record ToolchainStatus(
    bool Available,
    string? Vkd3dPath,
    string? SpirvCrossPath,
    IReadOnlyList<string> MissingTools,
    string Hint
);

public static class MaterialShaderToolchain
{
    private static ToolchainStatus? _cachedStatus;
    private static readonly object _lock = new();

    public static ToolchainStatus Status
    {
        get
        {
            if (_cachedStatus == null)
            {
                lock (_lock)
                {
                    _cachedStatus ??= DetectInternal();
                }
            }
            return _cachedStatus;
        }
    }

    public static ToolchainStatus Refresh()
    {
        lock (_lock)
        {
            _cachedStatus = DetectInternal();
            return _cachedStatus;
        }
    }

    public static string GetVkd3dCompilerPath() =>
        Status.Vkd3dPath ?? throw new FileNotFoundException("vkd3d-compiler executable was not found. " + Status.Hint);

    public static string GetSpirvCrossPath() =>
        Status.SpirvCrossPath ?? throw new FileNotFoundException("spirv-cross executable was not found. " + Status.Hint);

    private static ToolchainStatus DetectInternal()
    {
        var vkd3d = FindExecutable("vkd3d-compiler");
        var spirvCross = FindExecutable("spirv-cross");

        var missing = new List<string>();
        if (vkd3d == null) missing.Add("vkd3d-compiler");
        if (spirvCross == null) missing.Add("spirv-cross");

        var hint = GetInstallHint(missing);

        return new ToolchainStatus(
            Available: missing.Count == 0,
            Vkd3dPath: vkd3d,
            SpirvCrossPath: spirvCross,
            MissingTools: missing,
            Hint: hint
        );
    }

    private static string? FindExecutable(string name)
    {
        var isWindows = OperatingSystem.IsWindows();
        var binaryName = isWindows ? $"{name}.exe" : name;

        // 1. 检查程序当前运行目录 (AppContext.BaseDirectory)
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var p1 = Path.Combine(baseDir, binaryName);
            if (File.Exists(p1)) return p1;

            // 1.1 检查 tools/ 或 runtimes/{rid}/native/ 子目录
            var pTools = Path.Combine(baseDir, "tools", binaryName);
            if (File.Exists(pTools)) return pTools;

            var rid = RuntimeInformation.RuntimeIdentifier;
            var pNative = Path.Combine(baseDir, "runtimes", rid, "native", binaryName);
            if (File.Exists(pNative)) return pNative;
        }

        // 2. 检查当前工作目录
        var cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrEmpty(cwd) && !string.Equals(cwd, baseDir, StringComparison.OrdinalIgnoreCase))
        {
            var pCwd = Path.Combine(cwd, binaryName);
            if (File.Exists(pCwd)) return pCwd;

            var pCwdTools = Path.Combine(cwd, "tools", binaryName);
            if (File.Exists(pCwdTools)) return pCwdTools;
        }

        // 3. 检查系统 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var separator = isWindows ? ';' : ':';
            var dirs = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var dir in dirs)
            {
                try
                {
                    var full = Path.Combine(dir, binaryName);
                    if (File.Exists(full)) return full;
                }
                catch
                {
                    // Ignore invalid path characters
                }
            }
        }

        // 4. macOS 常见 Homebrew / MacPorts 默认路径兜底
        if (OperatingSystem.IsMacOS())
        {
            var macFallbacks = new[] { "/opt/homebrew/bin", "/usr/local/bin" };
            foreach (var dir in macFallbacks)
            {
                var full = Path.Combine(dir, binaryName);
                if (File.Exists(full)) return full;
            }
        }

        return null;
    }

    private static string GetInstallHint(List<string> missing)
    {
        if (missing.Count == 0) return string.Empty;

        var missingList = string.Join(", ", missing);
        if (OperatingSystem.IsWindows())
        {
            return $"缺少着色器转译工具 ({missingList})。请下载编译好的 {missingList} (.exe) 放置于 MercuryManager.Server 同级目录，或将其加入系统环境变量 PATH。";
        }
        if (OperatingSystem.IsMacOS())
        {
            return $"缺少着色器转译工具 ({missingList})。请使用 brew install vkd3d spirv-cross 安装，或将二进制文件放置于 MercuryManager 同级目录。";
        }
        return $"缺少着色器转译工具 ({missingList})。请使用系统包管理器安装 (例如 sudo apt install vkd3d-compiler spirv-cross) 或放置于 MercuryManager 同级目录。";
    }
}
