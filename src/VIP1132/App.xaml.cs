using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using VIP1132.Models;
using VIP1132.Services;

namespace VIP1132;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--zoom-helper", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var reportPath = GetArgument(e.Args, "--report");
            var exitCode = await ZoomHelper.RunAsync(reportPath);
            Shutdown(exitCode);
            return;
        }

        base.OnStartup(e);

        var previewMode = e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase);
#if PREVIEW_UI
        previewMode = true;
#endif
        if (!IsAdministrator() && !previewMode)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch
            {
                MessageBox.Show(
                    "VIP 1132 needs administrator permission to manage Windows users and install Zoom.",
                    "Administrator permission required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static string? GetArgument(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
