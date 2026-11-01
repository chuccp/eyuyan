using System.Runtime.InteropServices;

namespace Yy.Compiler;

public enum YyOperatingSystem
{
    Windows,
    MacOS
}

public enum YyCpuArchitecture
{
    X64,
    Arm64
}

public sealed record YyBuildTarget(
    string Id,
    YyOperatingSystem OperatingSystem,
    YyCpuArchitecture Architecture)
{
    public bool IsWindows => OperatingSystem == YyOperatingSystem.Windows;
    public bool IsMacOS => OperatingSystem == YyOperatingSystem.MacOS;
    public string ExecutableExtension => IsWindows ? ".exe" : string.Empty;
}

public static class YyBuildTargets
{
    public const string Auto = "auto";
    public const string WindowsX64 = "windows-x64";
    public const string MacOSArm64 = "macos-arm64";
    public const string MacOSX64 = "macos-x64";

    public static IReadOnlyList<string> All { get; } =
        [Auto, WindowsX64, MacOSArm64, MacOSX64];

    public static YyBuildTarget Resolve(string? requested = null)
    {
        var value = string.IsNullOrWhiteSpace(requested)
            ? Auto
            : requested.Trim().ToLowerInvariant();
        if (value == Auto)
        {
            return Current;
        }

        return value switch
        {
            WindowsX64 => new(
                WindowsX64,
                YyOperatingSystem.Windows,
                YyCpuArchitecture.X64),
            MacOSArm64 => new(
                MacOSArm64,
                YyOperatingSystem.MacOS,
                YyCpuArchitecture.Arm64),
            MacOSX64 => new(
                MacOSX64,
                YyOperatingSystem.MacOS,
                YyCpuArchitecture.X64),
            _ => throw new ArgumentException(
                $"未知目标平台“{requested}”。可选值：{string.Join("、", All)}。",
                nameof(requested))
        };
    }

    public static YyBuildTarget Current
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    throw new PlatformNotSupportedException(
                        "当前 Windows ARM64 原生后端尚未实现。");
                }

                return Resolve(WindowsX64);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? Resolve(MacOSArm64)
                    : Resolve(MacOSX64);
            }

            throw new PlatformNotSupportedException(
                "易语 Studio 当前只定义 Windows 和 macOS 原生目标。");
        }
    }

    public static bool CanRunOnCurrentHost(YyBuildTarget target)
    {
        if (target.IsWindows)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        return target.IsMacOS &&
               RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    public static string NormalizeOutputPath(string path, YyBuildTarget target)
    {
        if (!target.IsWindows ||
            string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path + ".exe";
    }
}
