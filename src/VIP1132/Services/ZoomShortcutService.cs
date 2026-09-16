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

    public static string ResolveExecutablePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WILL", "VIP 1132", "VIP1132.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WILL", "VIP 1132", "VIP1132.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WILL", "VIP 1132", "VIP1132.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentPath) || !File.Exists(currentPath))
            throw new InvalidOperationException("The VIP 1132 application path is unavailable.");

        var tempPath = Path.GetTempPath();
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        bool isTemp = currentPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase) ||
                      currentPath.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase);

        bool isUserPrivate = !string.IsNullOrEmpty(userProfilePath) &&
                             currentPath.StartsWith(userProfilePath, StringComparison.OrdinalIgnoreCase);

        if (isTemp || isUserPrivate)
        {
            try
            {
                var targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WILL", "VIP 1132");
                Directory.CreateDirectory(targetDir);

                var baseDir = AppContext.BaseDirectory;
                CopyDirectoryContents(baseDir, targetDir);

                var permanentExe = Path.Combine(targetDir, Path.GetFileName(currentPath));
                if (File.Exists(permanentExe))
                    return permanentExe;
            }
            catch
            {
                // Fall back to current path if copy fails
            }
        }

        return currentPath;
    }

    private static void CopyDirectoryContents(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            try
            {
                File.Copy(file, destFile, overwrite: true);
            }
            catch
            {
            }
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            var destSubDir = Path.Combine(targetDir, dirName);
            CopyDirectoryContents(subDir, destSubDir);
        }
    }

    public static string Create(string username)
    {
        var executable = ResolveExecutablePath();
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

            var savedTarget = (string)saved.TargetPath;
            var savedIcon = (string)saved.IconLocation;

            bool targetMatches = string.Equals(savedTarget, executable, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(Path.GetFullPath(savedTarget), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);

            bool iconMatches = string.Equals(savedIcon.Replace(" ", ""), icon.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

            if (!targetMatches || (string)saved.Arguments != arguments || !iconMatches)
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
