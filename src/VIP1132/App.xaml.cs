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
        base.OnStartup(e);

        // Handle the desktop shortcut before the setup window or its UAC elevation.
        if (e.Args.Contains(ZoomShortcutService.LaunchArgument, StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var state = await new StateService().LoadAsync();
                if (e.Args.Length != 3 || e.Args[0] != ZoomShortcutService.LaunchArgument ||
                    !ZoomShortcutService.MatchesManagedUser(e.Args[1], e.Args[2], state.CurrentUsername))
                    throw new InvalidOperationException("This Zoom shortcut no longer matches the managed Windows user. Finish setup in VIP 1132 to refresh it.");

                // The existing numeric-account convention supplies the password in memory only.
                // Include the session so separate interactive desktops do not block each other.
                using var gate = new Mutex(false, $"Local\\VIP1132-Zoom-{e.Args[2]}");
                bool acquired;
                try { acquired = gate.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (acquired)
                {
                    try
                    {
                        var result = await new ZoomService().LaunchAsUserAsync(e.Args[1], e.Args[1]);
                        if (!result.Success) throw new InvalidOperationException(result.Message);
                    }
                    finally { gate.ReleaseMutex(); }
                }
                Shutdown(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Zoom could not open", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

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

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
