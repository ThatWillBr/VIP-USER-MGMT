using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using VIP1132.Services;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;

internal static class Program
{
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        var folder = Path.Combine(Path.GetTempPath(), "VIP1132 tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Task.Run(() => VerifyDownloadAsync(folder, args.Contains("--live-download"))).GetAwaiter().GetResult();
            _ = new Application();
            foreach (var name in new[] { "vip1132-logo-black.png", "vip1132.ico" })
            {
                var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/VIP1132;component/Assets/{name}"));
                Check(resource is not null, $"WPF can resolve the embedded {name}");
                using var embedded = resource!.Stream;
                using var expected = File.OpenRead(Path.Combine(Directory.GetCurrentDirectory(), "assets", name));
                Check(SHA256.HashData(embedded).SequenceEqual(SHA256.HashData(expected)), $"Embedded {name} matches the current artwork");
                var bitmap = new BitmapImage(new Uri($"pack://application:,,,/VIP1132;component/Assets/{name}"));
                Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, $"WPF decodes {name}");
            }
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

    private static async Task VerifyDownloadAsync(string folder, bool live)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("Download validation must run after closing the writer.");
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            var header = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, timeout.Token);
            await stream.WriteAsync(payload, timeout.Token);
        });
        var destination = Path.Combine(folder, "download.bin");
        File.WriteAllText(destination, "Previous cached file");
        try
        {
            await ZoomService.DownloadAsync($"http://127.0.0.1:{port}/package", destination, null,
                TimeSpan.Zero, 0, path =>
                {
                    using var validated = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    using var data = new MemoryStream();
                    validated.CopyTo(data);
                    return data.ToArray().SequenceEqual(payload);
                }, "Validation failed", timeout.Token);
            await serve;
            Check(File.ReadAllBytes(destination).SequenceEqual(payload), "Downloaded file can be reopened exclusively and replaces the cache");
            Check(!File.Exists(destination + ".download"), "Successful download leaves no partial file");
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
            try { await serve; } catch (OperationCanceledException) { }
        }
        if (live)
        {
            using var liveTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var msi = Path.Combine(folder, "ZoomInstallerFull.msi");
            await ZoomService.DownloadAsync(ZoomService.ZoomMsiUrl, msi, null, TimeSpan.Zero,
                10 * 1024 * 1024, ZoomService.IsValidZoomMsi, "Live MSI validation failed", liveTimeout.Token);
            Check(ZoomService.IsValidZoomMsi(msi), "Live official Zoom download passes Windows Installer validation after closing the file");
            using (var locked = new FileStream(msi, FileMode.Open, FileAccess.Read, FileShare.None))
                Check(!ZoomService.IsValidZoomMsi(msi), "An exclusive file lock reproduces the previous false invalid-MSI result");
            Check(ZoomService.IsValidZoomMsi(msi), "The same MSI validates again after releasing the exclusive lock");
            Console.WriteLine($"Live Zoom MSI verified: {new FileInfo(msi).Length} bytes.");
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
