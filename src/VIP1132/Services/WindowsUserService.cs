using System.Text.RegularExpressions;

namespace VIP1132.Services;

public sealed class WindowsUserService
{
    private static readonly Regex NumericUser = new("^\\d+$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<string>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync("net.exe", ["user"], TimeSpan.FromSeconds(15), cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException("Could not list local users: " + result.BestMessage);

        var users = new List<string>();
        var capture = false;
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("---", StringComparison.Ordinal))
            {
                capture = true;
                continue;
            }
            if (!capture || line.Contains("command completed", StringComparison.OrdinalIgnoreCase))
                continue;
            users.AddRange(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return users;
    }

    public async Task<int?> HighestNumericUserAsync(CancellationToken cancellationToken = default)
    {
        var users = await ListUsersAsync(cancellationToken);
        return users.Where(x => NumericUser.IsMatch(x)).Select(int.Parse).DefaultIfEmpty().Max() is var max && max > 0 ? max : null;
    }

    public async Task<ProcessResult> CreateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        Validate(username, password);
        var create = await ProcessRunner.RunAsync(
            "net.exe", ["user", username, password, "/add", "/active:yes", "/passwordchg:no"],
            TimeSpan.FromSeconds(30), cancellationToken);
        if (!create.Success)
            return create;

        var addAdmin = await ProcessRunner.RunAsync(
            "net.exe", ["localgroup", "Administrators", username, "/add"],
            TimeSpan.FromSeconds(30), cancellationToken);
        if (!addAdmin.Success)
            return addAdmin;

        // A room utility must not silently stop working when Windows' normal password age elapses.
        await ProcessRunner.RunAsync(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command",
                $"Set-LocalUser -Name '{username}' -PasswordNeverExpires $true -UserMayChangePassword $false"],
            TimeSpan.FromSeconds(30), cancellationToken);
        return create;
    }

    public Task<ProcessResult> DeleteAsync(string username, CancellationToken cancellationToken = default)
    {
        if (!NumericUser.IsMatch(username))
            throw new ArgumentException("VIP-managed usernames must be numeric.", nameof(username));
        return ProcessRunner.RunAsync("net.exe", ["user", username, "/delete"], TimeSpan.FromSeconds(30), cancellationToken);
    }

    public async Task<bool> ExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync("net.exe", ["user", username], TimeSpan.FromSeconds(15), cancellationToken);
        return result.Success;
    }

    private static void Validate(string username, string password)
    {
        if (!NumericUser.IsMatch(username))
            throw new ArgumentException("VIP-managed usernames must be numeric.", nameof(username));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("A password is required.", nameof(password));
    }
}
