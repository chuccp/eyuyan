using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Language.Syntax;

namespace Yy.Native.Backends;

internal sealed record NativeBackendRequest(
    YyProgram Program,
    string SourceName,
    YyBuildTarget Target,
    string OutputPath,
    IReadOnlyList<YyFormModel> Forms,
    YyFormModel? StartupForm,
    string? ProjectDirectory);

/// <summary>
/// 一个原生平台后端。新增操作系统或架构时，只需实现该接口并注册到
/// <see cref="NativeBackendRegistry"/>，编译器、CLI 和 IDE 不需要平台分支。
/// </summary>
internal interface INativeBackend
{
    string Name { get; }
    IReadOnlyList<YyBuildTarget> Targets { get; }
    NativeBuildResult Compile(NativeBackendRequest request);
}

internal static class NativeBackendRegistry
{
    private static readonly IReadOnlyList<INativeBackend> Registered =
    [
        new WindowsNativeBackend(),
        new MacNativeBackend()
    ];

    public static IReadOnlyList<INativeBackend> Backends => Registered;

    public static IReadOnlyList<string> SupportedTargetIds { get; } =
        Registered.SelectMany(backend => backend.Targets)
            .Select(target => target.Id)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static INativeBackend Resolve(YyBuildTarget target) =>
        Registered.FirstOrDefault(backend =>
            backend.Targets.Any(item =>
                string.Equals(item.Id, target.Id, StringComparison.Ordinal)))
        ?? throw new PlatformNotSupportedException(
            $"目标“{target.Id}”的原生后端尚未实现。" +
            $"当前已注册后端：{string.Join("、", SupportedTargetIds)}。");
}
