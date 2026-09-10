using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using VIP1132.Services;

internal static class Program
{
    private static int _passed;

    [STAThread]
    private static int Main()
    {
        var folder = Path.Combine(Path.GetTempPath(), "VIP1132 tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "VIP1132.exe");
            var target = Path.Combine(folder, "App with spaces.exe");
            File.Copy(source, target);
            var icon = ZoomService.FindZoomExecutable() ?? target;

            var link = ZoomShortcutService.WriteShortcut(folder, target, icon, "42", "S-1-5-21-1-2-3-1042");
            Check(File.Exists(link), "Shortcut is saved to the requested directory");
            VerifyShortcut(link, target, icon, "42", "S-1-5-21-1-2-3-1042");
            ZoomShortcutService.WriteShortcut(folder, target, icon, "43", "S-1-5-21-1-2-3-1043");
            VerifyShortcut(link, target, icon, "43", "S-1-5-21-1-2-3-1043");
            Check(Directory.GetFiles(folder, "*.lnk").Length == 1, "Account rotation updates the same shortcut");

            var original = File.ReadAllBytes(link);
            try
            {
                ZoomShortcutService.WriteShortcut(folder, target + ".missing", icon, "44", "S-1-5-21-1-2-3-1044");
                throw new Exception("Missing target unexpectedly accepted");
            }
            catch (FileNotFoundException) { }
            Check(original.SequenceEqual(File.ReadAllBytes(link)), "Failed replacement preserves the previous shortcut");

            Check(!ZoomShortcutService.MatchesManagedUser("42", "invalid", "43"), "Stale account shortcut rejected");
            Check(!ZoomShortcutService.MatchesManagedUser("042", "invalid", "042"), "Noncanonical account rejected");
            Check(!ZoomShortcutService.MatchesManagedUser("-1", "invalid", "-1"), "Negative account rejected");
            Check(!ZoomShortcutService.MatchesManagedUser("2147483648", "invalid", "2147483648"), "Overflow account rejected");
            Check(!ZoomShortcutService.MatchesManagedUser("42", "invalid", null), "Missing managed state rejected");

            var users = new WindowsUserService();
            foreach (var invalid in new[] { "name", "42\n", "42 & whoami", "٤٢" })
            {
                try { users.CreateAsync(invalid, "unused").GetAwaiter().GetResult(); throw new Exception("Invalid account accepted"); }
                catch (ArgumentException) { _passed++; }
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { users.CreateAsync("42", "unused", cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { _passed++; }

            using var current = Process.GetCurrentProcess();
            using var identity = WindowsIdentity.GetCurrent();
            Check(NativeSessionLauncher.TryGetProcessOwner(current) == identity.Name, "Native process ownership lookup works");
            Console.WriteLine($"PASS: {_passed} checks. No accounts, Zoom processes, or desktop files were changed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            // Only this invocation's explicitly created temporary directory.
            Directory.Delete(folder, true);
        }
    }

    private static void VerifyShortcut(string path, string target, string icon, string username, string sid)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(path);
        try
        {
            Check(string.Equals((string)link.TargetPath, target, StringComparison.OrdinalIgnoreCase), "Target with spaces survives round-trip");
            Check((string)link.Arguments == $"--launch-zoom {username} {sid}", "Launch-only arguments survive round-trip");
            Check(string.Equals((string)link.IconLocation, icon + ",0", StringComparison.OrdinalIgnoreCase), "Zoom's icon resource survives round-trip");
            Check(string.Equals((string)link.WorkingDirectory, Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase), "Working directory survives round-trip");
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        _passed++;
    }
}
