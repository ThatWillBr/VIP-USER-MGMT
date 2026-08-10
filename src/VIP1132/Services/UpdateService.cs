using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using VIP1132.Models;

namespace VIP1132.Services;

public sealed class UpdateService
{
    public const string DefaultManifestUrl = "https://pnpatvip.com/vip1132-update.json";

    private static readonly HttpClient Http = CreateHttpClient();

    public bool IsInstalledCopy
    {
        get
        {
            var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            return File.Exists(Path.Combine(baseDirectory, "unins000.exe")) ||
                   baseDirectory.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<UpdateManifest?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var manifestUrl = Environment.GetEnvironmentVariable("VIP1132_UPDATE_MANIFEST_URL") ?? DefaultManifestUrl;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        using var response = await Http.GetAsync(manifestUrl, HttpCompletionOption.ResponseContentRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest == null ||
            !Version.TryParse(manifest.Version, out var available) ||
            !Version.TryParse(currentVersion, out var current))
        {
            return null;
        }

        return available > current ? manifest : null;
    }

    public void OpenPortableDownload(UpdateManifest manifest)
    {
        var url = string.IsNullOrWhiteSpace(manifest.PortableUrl) ? manifest.InstallerUrl : manifest.PortableUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateManifest manifest,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(manifest.InstallerUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("The update manifest contains an invalid installer address.");
        }

        var updateRoot = Path.Combine(Path.GetTempPath(), "VIP1132", "Updates", manifest.Version);
        Directory.CreateDirectory(updateRoot);
        var extension = uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".exe";
        var packagePath = Path.Combine(updateRoot, "VIP1132-Update-" + manifest.Version + extension);
        await DownloadFileAsync(uri, packagePath, progress, cancellationToken);

        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            string actualHash;
            await using (var packageStream = File.OpenRead(packagePath))
            {
                actualHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken));
            }

            if (!actualHash.Equals(manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(packagePath);
                throw new InvalidDataException("The update package failed its security check. Please download it from pnpatvip.com instead.");
            }
        }

        if (extension == ".exe")
            return packagePath;

        var extractPath = Path.Combine(updateRoot, "installer");
        if (Directory.Exists(extractPath))
            Directory.Delete(extractPath, recursive: true);

        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(packagePath, extractPath, overwriteFiles: true);
        return Directory.EnumerateFiles(extractPath, "VIP1132-Setup-*.exe", SearchOption.AllDirectories).FirstOrDefault()
               ?? throw new InvalidDataException("The downloaded update did not contain the VIP 1132 installer.");
    }

    private static async Task DownloadFileAsync(Uri uri, string destination, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var temp = destination + ".download";
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true))
            {
                var buffer = new byte[131072];
                long received = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (total is > 0)
                        progress?.Report((int)Math.Min(100, received * 100 / total.Value));
                }

                await target.FlushAsync(cancellationToken);
            }

            File.Move(temp, destination, overwrite: true);
            progress?.Report(100);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VIP1132-Updater/3.0");
        return client;
    }
}
