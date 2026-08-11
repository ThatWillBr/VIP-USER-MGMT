using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using VIP1132.Models;

namespace VIP1132.Services;

public sealed class ZoomService
{
    public const string CleanZoomUrl = "https://assets.zoom.us/docs/msi-templates/CleanZoom.zip";
    public const string ZoomMsiUrl = "https://zoom.us/client/latest/ZoomInstallerFull.msi?archType=x64";

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly TimeSpan CleanZoomCacheAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan ZoomInstallerCacheAge = TimeSpan.FromHours(12);
    private readonly string _publicDownloads = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "..", "Downloads");

    public string PublicDownloads => Path.GetFullPath(_publicDownloads);
    public string ZoomMsiPath => Path.Combine(PublicDownloads, "ZoomInstallerFull.msi");
    public string CleanZoomZipPath => Path.Combine(PublicDownloads, "CleanZoom.zip");

    public async Task StopZoomAsync(CancellationToken cancellationToken = default)
    {
        var names = new[] { "Zoom", "CptHost", "zCrashReport64", "ZoomOutlookMAPI", "ZoomAutoUpdate" };
        var processes = names.SelectMany(Process.GetProcessesByName).ToList();
        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { if (!process.HasExited) process.Kill(true); }
                catch { }
            }

            await Task.WhenAll(processes.Select(process => WaitForExitAsync(process, cancellationToken)));
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public async Task<OperationResult> UninstallCleanlyAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(PublicDownloads);
        await DownloadAsync(
            CleanZoomUrl,
            CleanZoomZipPath,
            progress,
            CleanZoomCacheAge,
            minimumReusableBytes: 64 * 1024,
            IsValidCleanZoomArchive,
            "Zoom's CleanZoom download was not a valid ZIP archive.",
            cancellationToken);

        var extraction = Path.Combine(Path.GetTempPath(), "VIP1132", "CleanZoom", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extraction);
        try
        {
            ZipFile.ExtractToDirectory(CleanZoomZipPath, extraction, true);
            var cleanZoom = Directory.EnumerateFiles(extraction, "CleanZoom.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (cleanZoom is null)
                return new OperationResult(false, "CleanZoom.exe was not present in Zoom's archive.");

            var result = await ProcessRunner.RunAsync(cleanZoom, ["/silent"], TimeSpan.FromMinutes(4), cancellationToken);
            return result.ExitCode is 0 or 1
                ? new OperationResult(true, "Existing Zoom installation and cached data were removed.")
                : new OperationResult(false, "CleanZoom failed: " + result.BestMessage);
        }
        finally
        {
            try { if (Directory.Exists(extraction)) Directory.Delete(extraction, true); } catch { }
        }
    }

    public Task DownloadInstallerAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(PublicDownloads);
        return DownloadAsync(
            ZoomMsiUrl,
            ZoomMsiPath,
            progress,
            ZoomInstallerCacheAge,
            minimumReusableBytes: 10 * 1024 * 1024,
            IsValidZoomMsi,
            "Zoom's installer download was not a valid Windows Installer package.",
            cancellationToken);
    }

    public async Task<OperationResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ZoomMsiPath))
            return new OperationResult(false, "ZoomInstallerFull.msi has not been downloaded.");

        var result = await ProcessRunner.RunAsync(
            "msiexec.exe",
            [
                "/i", ZoomMsiPath,
                "/qn", "/norestart",
                "ALLUSERS=1",
                "MSIRestartManagerControl=Disable",
                "ZoomAutoUpdate=true"
            ],
            TimeSpan.FromMinutes(6), cancellationToken);

        if (!result.Success)
            return new OperationResult(false, "Zoom installation failed: " + result.BestMessage);

        var executable = await WaitForZoomExecutableAsync(TimeSpan.FromSeconds(30), cancellationToken);
        return executable is not null
            ? new OperationResult(true, "Zoom Workplace was installed for all Windows users.")
            : new OperationResult(false, "The installer finished, but Zoom.exe was not found.");
    }

    public async Task<OperationResult> LaunchAsUserAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var executable = FindZoomExecutable();
        if (executable is null)
            return new OperationResult(false, "Zoom.exe was not found after installation.");

        try
        {
            NativeSessionLauncher.LaunchAsUser(username, password, executable, []);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, ex.Message);
        }

        var verifiedProcess = await WaitForZoomProcessAsUserAsync(
            username, TimeSpan.FromSeconds(60), cancellationToken);

        if (verifiedProcess is null)
        {
            return new OperationResult(false,
                $"Zoom did not open visibly as {Environment.MachineName}\\{username} within 60 seconds. The setup was not marked successful.");
        }

        verifiedProcess.Dispose();
        return new OperationResult(true, $"Zoom opened visibly as {Environment.MachineName}\\{username}.");
    }

    public void OpenDownloads()
    {
        Directory.CreateDirectory(PublicDownloads);
        Process.Start(new ProcessStartInfo("explorer.exe", PublicDownloads) { UseShellExecute = true });
    }

    public static string? FindZoomExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zoom", "bin", "Zoom.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zoom", "bin", "Zoom.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zoom", "bin", "Zoom.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string?> WaitForZoomExecutableAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = FindZoomExecutable();
            if (path is not null) return path;
            await Task.Delay(500, cancellationToken);
        }
        return null;
    }

    private static async Task<Process?> WaitForZoomProcessAsUserAsync(
        string username,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var process in Process.GetProcessesByName("Zoom"))
            {
                if (IsVisibleZoomProcessForUser(process, sessionId, username))
                    return process;

                process.Dispose();
            }
            await Task.Delay(250, cancellationToken);
        }
        return null;
    }

    private static bool IsVisibleZoomProcessForUser(Process process, int sessionId, string username)
    {
        try
        {
            if (process.HasExited || process.SessionId != sessionId)
                return false;

            var owner = NativeSessionLauncher.TryGetProcessOwner(process);
            if (owner?.EndsWith("\\" + username, StringComparison.OrdinalIgnoreCase) != true)
                return false;

            process.Refresh();
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
                return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch { }
    }

    private static async Task DownloadAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        TimeSpan cacheAge,
        long minimumReusableBytes,
        Func<string, bool> validator,
        string invalidDownloadMessage,
        CancellationToken cancellationToken)
    {
        if (IsReusableDownload(destination, cacheAge, minimumReusableBytes, validator))
        {
            progress?.Report(100);
            return;
        }

        var temp = destination + ".download";
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(temp, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 1024 * 512,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            }))
            {
                var buffer = new byte[1024 * 512];
                long received = 0;
                var lastPercent = -1d;
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (total is > 0)
                    {
                        var percent = Math.Round(Math.Min(100, received * 100d / total.Value), 1);
                        if (percent >= lastPercent + 0.1 || percent >= 100)
                        {
                            progress?.Report(percent);
                            lastPercent = percent;
                        }
                    }
                }

                await target.FlushAsync(cancellationToken);
            }

            if (!validator(temp))
                throw new InvalidDataException(invalidDownloadMessage);

            File.Move(temp, destination, true);
            progress?.Report(100);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static bool IsReusableDownload(
        string path,
        TimeSpan maximumAge,
        long minimumBytes,
        Func<string, bool> validator)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                   && file.Length >= minimumBytes
                   && DateTime.UtcNow - file.LastWriteTimeUtc <= maximumAge
                   && validator(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidCleanZoomArchive(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Any(entry =>
                entry.Length > 0 &&
                Path.GetFileName(entry.FullName).Equals("CleanZoom.exe", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidZoomMsi(string path)
    {
        try
        {
            var result = MsiOpenDatabase(path, IntPtr.Zero, out var database);
            if (result != 0)
                return false;
            MsiCloseHandle(database);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VIP1132/3.0");
        return client;
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabase(string databasePath, IntPtr persist, out uint database);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(uint handle);
}
