namespace VIP1132.Models;

public sealed class ZoomProfileReport
{
    public bool ZoomStarted { get; set; }
    public DateTimeOffset CompletedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ZoomSettingResult> Settings { get; set; } = [];
    public string? Error { get; set; }

    public int AppliedCount => Settings.Count(x => x.Status == ZoomSettingStatus.Applied || x.Status == ZoomSettingStatus.AlreadySet);
    public int WarningCount => Settings.Count(x => x.Status == ZoomSettingStatus.Unavailable || x.Status == ZoomSettingStatus.Failed);
}

public sealed class ZoomSettingResult
{
    public string Category { get; set; } = "";
    public string Setting { get; set; } = "";
    public string DesiredValue { get; set; } = "";
    public ZoomSettingStatus Status { get; set; }
    public string? Detail { get; set; }
}

public enum ZoomSettingStatus
{
    Applied,
    AlreadySet,
    Unavailable,
    Failed
}
