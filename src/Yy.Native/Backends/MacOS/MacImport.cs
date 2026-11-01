namespace Yy.Native.MacOS;

internal static class MacLibraries
{
    public const string System = "/usr/lib/libSystem.B.dylib";
    public const string ObjectiveC = "/usr/lib/libobjc.A.dylib";
    public const string Foundation =
        "/System/Library/Frameworks/Foundation.framework/Versions/C/Foundation";
    public const string AppKit =
        "/System/Library/Frameworks/AppKit.framework/Versions/C/AppKit";

    public static IReadOnlyList<string> All { get; } =
        [System, ObjectiveC, Foundation, AppKit];
}

internal sealed record MacImport(string Library, string Symbol);
