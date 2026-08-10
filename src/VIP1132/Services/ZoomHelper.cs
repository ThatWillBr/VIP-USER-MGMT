using System.IO;
using System.Text.Json;
using VIP1132.Models;

namespace VIP1132.Services;

public static class ZoomHelper
{
    public static async Task<int> RunAsync(string? reportPath)
    {
        ZoomProfileReport report;
        try
        {
            report = await Task.Run(() => new ZoomUiConfigurator().Apply());
        }
        catch (Exception ex)
        {
            report = new ZoomProfileReport
            {
                Error = "Zoom helper failed before automation could complete: " + ex.Message
            };
        }

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch
            {
                return 3;
            }
        }
        return report.ZoomStarted && report.Error is null ? 0 : 2;
    }
}
