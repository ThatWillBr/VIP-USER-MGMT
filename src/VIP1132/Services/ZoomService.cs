using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
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

    public Task StopZoomAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var names = new[] { "Zoom", "CptHost", "zCrashReport64", "ZoomOutlookMAPI", "ZoomAutoUpdate" };
            foreach (var name in names)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
        }, cancellationToken);
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

    public async Task<(OperationResult Result, ZoomProfileReport? Report)> ConfigureAndLaunchAsUserAsync(
        string username,
        string password,
        string reportPath,
        CancellationToken cancellationToken = default)
    {
        try { if (File.Exists(reportPath)) File.Delete(reportPath); } catch { }

        var executable = PrepareHelperExecutable();
        var helperPid = NativeSessionLauncher.LaunchAsUser(
            username, password, executable, ["--zoom-helper", "--report", reportPath]);

        using var helper = Process.GetProcessById(helperPid);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await helper.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (new OperationResult(false, "Zoom configuration timed out."), null);
        }
        var helperExitCode = helper.ExitCode;

        ZoomProfileReport? report = null;
        try
        {
            if (File.Exists(reportPath))
                report = JsonSerializer.Deserialize<ZoomProfileReport>(await File.ReadAllTextAsync(reportPath, cancellationToken));
        }
        catch (Exception ex)
        {
            return (new OperationResult(false, "Could not read the Zoom dark mode report: " + ex.Message), null);
        }

        var verifiedProcess = await WaitForZoomProcessAsUserAsync(
            username, TimeSpan.FromSeconds(90), cancellationToken);

        if (verifiedProcess is null)
        {
            var detail = report?.Error is { } error
                ? " Helper reported: " + error
                : helperExitCode != 0
                    ? $" Helper exited with code {helperExitCode} (0x{helperExitCode:X8})."
                    : "";
            return (new OperationResult(false,
                $"Zoom did not open visibly as {Environment.MachineName}\\{username}. The setup was not marked successful.{detail}"), report);
        }

        verifiedProcess.Dispose();
        if (report is null)
        {
            var detail = helperExitCode == 0
                ? "the settings helper did not produce a verification report"
                : $"the settings helper exited with code {helperExitCode} before producing a verification report";
            return (new OperationResult(true, $"Zoom opened as {username}, but {detail}.", true), null);
        }

        var warnings = report.WarningCount > 0 || report.Error is not null;
        var message = report.Error is not null
            ? $"Zoom opened as {username}; dark mode helper reported: {report.Error}"
            : warnings
            ? $"Zoom opened as {username}; dark mode was checked with {report.WarningCount} warning(s)."
            : $"Zoom opened as {username} with dark mode verified.";
        return (new OperationResult(true, message, warnings), report);
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
                if (IsZoomProcessForUser(process, sessionId, username))
                    return process;

                process.Dispose();
            }
            await Task.Delay(1000, cancellationToken);
        }
        return null;
    }

    private static bool IsZoomProcessForUser(Process process, int sessionId, string username)
    {
        try
        {
            if (process.HasExited || process.SessionId != sessionId)
                return false;

            var owner = NativeSessionLauncher.TryGetProcessOwner(process);
            return owner?.EndsWith("\\" + username, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    private static string PrepareHelperExecutable()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The VIP 1132 executable path is unavailable.");

        if (IsSharedApplicationPath(executable))
            return executable;

        var sourceDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var helperDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VIP1132",
            "Helper",
            Guid.NewGuid().ToString("N"));
        CopyDirectory(sourceDirectory, helperDirectory);

        var helperExecutable = Path.Combine(helperDirectory, Path.GetFileName(executable));
        if (!File.Exists(helperExecutable))
            throw new InvalidOperationException("The staged Zoom helper executable could not be prepared.");

        return helperExecutable;
    }

    private static bool IsSharedApplicationPath(string executable)
    {
        var path = Path.GetFullPath(executable);
        var sharedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Users", "Public")
        };

        return sharedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static async Task DownloadAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        TimeSpan cacheAge,
        long minimumReusableBytes,
        CancellationToken cancellationToken)
    {
        if (IsReusableDownload(destination, cacheAge, minimumReusableBytes))
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

            File.Move(temp, destination, true);
            progress?.Report(100);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static bool IsReusableDownload(string path, TimeSpan maximumAge, long minimumBytes)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                   && file.Length >= minimumBytes
                   && DateTime.UtcNow - file.LastWriteTimeUtc <= maximumAge;
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
}
