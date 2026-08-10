namespace VIP1132.Models;

public sealed class AppState
{
    public int? CurrentUserNumber { get; set; }
    public int? LastAttemptUserNumber { get; set; }
    public string LastSetupStatus { get; set; } = "Never run";
    public DateTimeOffset? LastUpdatedUtc { get; set; }

    public string? CurrentUsername => CurrentUserNumber?.ToString();
}

public enum LogLevel
{
    Info,
    Detail,
    Success,
    Warning,
    Error
}

public sealed record ProgressUpdate(double Percent, string Step, string Message, LogLevel Level = LogLevel.Detail);

public sealed record OperationResult(bool Success, string Message, bool HasWarnings = false);
