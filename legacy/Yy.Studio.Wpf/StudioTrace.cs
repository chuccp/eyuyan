using System.IO;

namespace Yy.Studio;

internal static class StudioTrace
{
    private static readonly string? LogPath = Environment.GetEnvironmentVariable("YYSTUDIO_TRACE");

    public static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(LogPath))
        {
            return;
        }

        try
        {
            File.AppendAllText(
                LogPath,
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never prevent the IDE from starting.
        }
    }
}

