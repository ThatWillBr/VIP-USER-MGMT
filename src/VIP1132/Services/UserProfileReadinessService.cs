using Microsoft.Win32;
using System.IO;
using System.Security.Principal;
using VIP1132.Models;

namespace VIP1132.Services;

/// <summary>
/// Protects a completed account/setup from a failed first profile logon. Windows can leave a
/// user folder behind without a ProfileList entry; trying again then creates name-suffixed and
/// temporary profiles. The incomplete folders are moved to a recoverable location before retry.
/// </summary>
public sealed class UserProfileReadinessService
{
    private const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private static readonly string UserProfilesRoot = Path.GetDirectoryName(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        ?? throw new InvalidOperationException("Windows did not provide the user-profile root directory.");
    private readonly string _recoveryRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VIP1132", "ProfileRecovery");

    public OperationResult RepairIncompleteProfileIfNeeded(string username)
    {
        var sid = GetUserSid(username);
        var expectedPath = Path.Combine(UserProfilesRoot, username);
        using var profileKey = Registry.LocalMachine.OpenSubKey($"{ProfileListPath}\\{sid}");

        if (profileKey is not null)
        {
            var path = profileKey.GetValue("ProfileImagePath") as string;
            var state = profileKey.GetValue("State") as int? ?? 0;
            if (!string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase))
                return new OperationResult(false,
                    $"Windows registered {Environment.MachineName}\\{username} with profile path '{path ?? "(missing)"}', not '{expectedPath}'. The existing account was preserved.");
            if (state != 0)
                return new OperationResult(false,
                    $"Windows reports the profile for {Environment.MachineName}\\{username} is still in state 0x{state:X}. Close every process for that account and retry.");
            return new OperationResult(true, "The Windows profile is already registered.");
        }

        var candidates = GetIncompleteProfileDirectories(username).ToArray();
        if (candidates.Length == 0)
            return new OperationResult(true, "Windows will initialize the new profile during launch.");

        try
        {
            Directory.CreateDirectory(_recoveryRoot);
            foreach (var candidate in candidates)
            {
                var backup = Path.Combine(_recoveryRoot,
                    $"{candidate.Name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
                Directory.Move(candidate.FullName, backup);
            }
            return new OperationResult(true,
                "An incomplete Windows profile was preserved under ProgramData before retrying the profile logon.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new OperationResult(false,
                $"Windows has no registered profile for {Environment.MachineName}\\{username}, but its incomplete profile folder could not be preserved: {ex.Message}");
        }
    }

    public OperationResult VerifyRegisteredProfile(string username)
    {
        var sid = GetUserSid(username);
        var expectedPath = Path.Combine(UserProfilesRoot, username);
        using var profileKey = Registry.LocalMachine.OpenSubKey($"{ProfileListPath}\\{sid}");
        var path = profileKey?.GetValue("ProfileImagePath") as string;
        var state = profileKey?.GetValue("State") as int? ?? 0;
        return profileKey is not null && string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase) && state == 0
            ? new OperationResult(true, "Windows profile initialization completed.")
            : new OperationResult(false,
                $"Windows did not finish initializing the profile for {Environment.MachineName}\\{username}. Expected '{expectedPath}' (registered path: '{path ?? "none"}', state: 0x{state:X}).");
    }

    private static string GetUserSid(string username) =>
        ((SecurityIdentifier)new NTAccount(Environment.MachineName, username)
            .Translate(typeof(SecurityIdentifier))).Value;

    private static IEnumerable<DirectoryInfo> GetIncompleteProfileDirectories(string username)
    {
        var root = new DirectoryInfo(UserProfilesRoot);
        var prefix = username + "." + Environment.MachineName;
        return root.EnumerateDirectories()
            .Where(directory => string.Equals(directory.Name, username, StringComparison.OrdinalIgnoreCase)
                || directory.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
