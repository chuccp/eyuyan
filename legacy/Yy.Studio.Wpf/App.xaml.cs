using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yy.Studio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        StudioTrace.Write("App.OnStartup begin");
        base.OnStartup(e);
        StudioTrace.Write("App.OnStartup base complete");
        if (string.Equals(Environment.GetEnvironmentVariable("YYSTUDIO_SOFTWARE_RENDER"), "1", StringComparison.Ordinal))
        {
            // 图形驱动/DComp 异常导致窗口上屏空白时,可用 YYSTUDIO_SOFTWARE_RENDER=1 强制软件渲染。
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            StudioTrace.Write("Software rendering forced");
        }

        DispatcherUnhandledException += OnUnhandledException;
        var configuration = StudioConfiguration.Load(e.Args);
        var window = new MainWindow(configuration);
        MainWindow = window;
        window.Show();
        window.McpAttach(e.Args);
        if (e.Args.Length > 0 && !e.Args[0].StartsWith("--", StringComparison.Ordinal))
        {
            window.OpenStartupPath(e.Args[0]);
        }

        StudioTrace.Write("App.OnStartup complete");
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StudioTrace.Write("Unhandled: " + e.Exception);
        MessageBox.Show(
            $"应用遇到未处理的错误：\n\n{e.Exception.Message}",
            "易语 Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

