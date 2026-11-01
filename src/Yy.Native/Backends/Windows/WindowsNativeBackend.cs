using Yy.Compiler;

namespace Yy.Native.Backends;

internal sealed class WindowsNativeBackend : INativeBackend
{
    public string Name => "Windows Win32/PE";

    public IReadOnlyList<YyBuildTarget> Targets { get; } =
        [YyBuildTargets.Resolve(YyBuildTargets.WindowsX64)];

    public NativeBuildResult Compile(NativeBackendRequest request)
    {
        var startupForm = request.StartupForm;
        var generated = new NativeEmitter(
            request.Program,
            request.SourceName,
            startupForm,
            request.Forms).Generate();
        var image = NativePeWriter.Build(
            generated.Code,
            generated.Data,
            generated.Imports,
            generated.GuiSubsystem);
        File.WriteAllBytes(request.OutputPath, image);
        return new NativeBuildResult(
            request.OutputPath,
            image.Length,
            0x1000,
            request.Target);
    }
}
