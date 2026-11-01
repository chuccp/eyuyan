using Yy.Compiler;
using Yy.Native.MacOS;

namespace Yy.Native.Backends;

internal sealed class MacNativeBackend : INativeBackend
{
    public string Name => "macOS AppKit/Mach-O";

    public IReadOnlyList<YyBuildTarget> Targets { get; } =
        [YyBuildTargets.Resolve(YyBuildTargets.MacOSArm64)];

    public NativeBuildResult Compile(NativeBackendRequest request)
    {
        var generated = new MacNativeEmitter(
            request.Program,
            request.SourceName,
            request.Target,
            request.Forms,
            request.StartupForm,
            request.ProjectDirectory).Generate();
        var image = MachOImageWriter.Build(
            generated.Code,
            generated.Data,
            generated.Imports,
            generated.Libraries);
        File.WriteAllBytes(request.OutputPath, image);
        MachOImageWriter.MarkExecutable(request.OutputPath);
        return new NativeBuildResult(
            request.OutputPath,
            image.Length,
            0x1000,
            request.Target);
    }
}
