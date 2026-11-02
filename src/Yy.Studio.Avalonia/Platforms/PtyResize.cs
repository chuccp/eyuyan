using System.Runtime.InteropServices;
using Terminal.Pty;

namespace Yy.Studio.Avalonia.Platforms;

/// <summary>
/// 把终端网格尺寸同步给系统 PTY。
/// Apple Silicon 的变长参数按调用栈传递，而 <c>Terminal.Pty</c> 用固定三参数
/// P/Invoke 调用 <c>ioctl</c>，内核从栈上读到的是垃圾值，导致 shell 认为窗口有
/// 数万列，每次提示符都被折行成上百行空白。这里按 Apple ARM64 ABI 把 winsize
/// 指针作为第 9 个参数压栈，让变长参数落到 <c>ioctl</c> 期望的位置。
/// </summary>
internal static class PtyResize
{
    private const ulong Tiocswinsz = 0x80087467;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort Row;
        public ushort Column;
        public ushort XPixel;
        public ushort YPixel;
    }

    // 前 8 个参数进入 x0-x7；第 9 个参数位于调用栈，正是 ioctl 读取第一个变长参数的位置。
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int IoctlWinsize(
        int fd,
        ulong request,
        nint arg3,
        nint arg4,
        nint arg5,
        nint arg6,
        nint arg7,
        nint arg8,
        nint winsize);

    public static void Apply(IPtyConnection pty, int columns, int rows)
    {
        if (OperatingSystem.IsMacOS() &&
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 &&
            TryGetMasterFd(pty, out var fd) &&
            TryApplyMacArm64(fd, columns, rows))
        {
            return;
        }

        pty.Resize(columns, rows);
    }

    private static bool TryApplyMacArm64(int fd, int columns, int rows)
    {
        var size = new WinSize
        {
            Row = (ushort)Math.Clamp(rows, 1, ushort.MaxValue),
            Column = (ushort)Math.Clamp(columns, 1, ushort.MaxValue)
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinSize>());
        try
        {
            Marshal.StructureToPtr(size, pointer, fDeleteOld: false);
            return IoctlWinsize(fd, Tiocswinsz, 0, 0, 0, 0, 0, 0, pointer) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static bool TryGetMasterFd(IPtyConnection pty, out int fd)
    {
        fd = -1;
        if (pty.Output is not FileStream stream)
        {
            return false;
        }

        try
        {
            fd = stream.SafeFileHandle.DangerousGetHandle().ToInt32();
        }
        catch (Exception exception) when (
            exception is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }

        return fd >= 0;
    }
}
