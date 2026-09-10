using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace VIP1132.Services;

public static class ZoomShortcutService
{
    public const string LaunchArgument = "--launch-zoom";
    public const string ShortcutName = "Zoom - VIP 1132.lnk";

    public static string GetUserSid(string username) =>
        ((SecurityIdentifier)new NTAccount(Environment.MachineName, username)
            .Translate(typeof(SecurityIdentifier))).Value;

    public static bool MatchesManagedUser(string username, string sid, string? currentUsername) =>
        username == currentUsername && int.TryParse(username, out var number) && number > 0 &&
        username == number.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
        GetUserSid(username) == sid;

    public static string Create(string username)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The VIP 1132 application path is unavailable.");
        var zoom = ZoomService.FindZoomExecutable()
            ?? throw new FileNotFoundException("Zoom.exe was not found for the desktop shortcut.");
        // CommonDesktop is visible on the operator's desktop even when UAC used another admin account.
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        return WriteShortcut(desktop, executable, zoom, username, GetUserSid(username));
    }

    internal static string WriteShortcut(string desktop, string executable, string zoom, string username, string sid)
    {
        if (!Directory.Exists(desktop) || !File.Exists(executable) || !File.Exists(zoom))
            throw new FileNotFoundException("The desktop, VIP 1132, or Zoom path is unavailable.");
        var path = Path.Combine(desktop, ShortcutName);
        var temporary = Path.Combine(desktop, "VIP1132-" + Guid.NewGuid().ToString("N") + ".lnk");
        var arguments = $"{LaunchArgument} {username} {sid}";
        var icon = zoom + ",0";
        object? shell = null;
        object? link = null;
        object? check = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
            dynamic shortcut = ((dynamic)shell).CreateShortcut(temporary);
            link = shortcut;
            shortcut.TargetPath = executable;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable)!;
            // Use Zoom's own Windows icon resource; no third-party icon or extra download.
            shortcut.IconLocation = icon;
            shortcut.Description = "Open Zoom as the Windows user created by VIP 1132.";
            shortcut.Save();

            dynamic saved = ((dynamic)shell).CreateShortcut(temporary);
            check = saved;
            if (!string.Equals((string)saved.TargetPath, executable, StringComparison.OrdinalIgnoreCase) ||
                (string)saved.Arguments != arguments ||
                !string.Equals((string)saved.IconLocation, icon, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Windows did not save the Zoom shortcut correctly.");
            File.Move(temporary, path, true);
            return path;
        }
        finally
        {
            foreach (var com in new[] { check, link, shell })
                if (com is not null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
