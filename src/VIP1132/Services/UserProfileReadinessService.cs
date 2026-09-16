using Microsoft.Win32;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text.RegularExpressions;
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
    private static readonly Regex ManagedProfileName = new(@"^(?<username>\d+)(?:\..+)?$", RegexOptions.CultureInvariant);
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
            if ((state & 0x81) != 0)
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

    /// <summary>
    /// A deleted local account leaves its ProfileList entry behind. Reusing that numeric name then
    /// makes Windows select an old SID or create a suffixed temporary profile. Preserve any folder
    /// and remove only entries whose numeric account no longer exists and whose hive is unloaded.
    /// </summary>
    public OperationResult RepairOrphanedProfiles(IEnumerable<string> existingUsernames)
    {
        var existing = existingUsernames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var profileList = Registry.LocalMachine.OpenSubKey(ProfileListPath, writable: true);
        if (profileList is null)
            return new OperationResult(false, "Windows ProfileList could not be opened for profile recovery.");

        var candidates = profileList.GetSubKeyNames()
            .Select(sid => ReadOrphanedProfile(profileList, sid, existing))
            .Where(candidate => candidate is not null)
            .Cast<OrphanedProfile>()
            .ToArray();
        if (candidates.Length == 0)
            return new OperationResult(true, "No orphaned VIP numeric profiles were found.");

        try
        {
            Directory.CreateDirectory(_recoveryRoot);
            foreach (var candidate in candidates)
            {
                using var loadedHive = Registry.Users.OpenSubKey(candidate.Sid);
                if (loadedHive is not null)
                    return new OperationResult(false,
                        $"Windows still has the old profile hive for SID {candidate.Sid} loaded. Sign out that user before retrying; no profile data was removed.");

                var preservedPath = PreserveProfileDirectory(candidate.Path, candidate.Username, candidate.Sid);
                profileList.DeleteSubKeyTree(candidate.Sid, throwOnMissingSubKey: false);
                WriteRecoveryRecord(candidate, preservedPath);
            }

            return new OperationResult(true,
                $"Preserved and cleared {candidates.Length} orphaned VIP Windows profile registration(s) before reusing numeric account names.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new OperationResult(false,
                $"Could not safely preserve an orphaned VIP Windows profile: {ex.Message}");
        }
    }

    public OperationResult VerifyRegisteredProfile(string username)
    {
        var sid = GetUserSid(username);
        var expectedPath = Path.Combine(UserProfilesRoot, username);
        using var profileKey = Registry.LocalMachine.OpenSubKey($"{ProfileListPath}\\{sid}");
        var path = profileKey?.GetValue("ProfileImagePath") as string;
        var state = profileKey?.GetValue("State") as int? ?? 0;
        return profileKey is not null && string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase) && (state & 0x81) == 0
            ? new OperationResult(true, "Windows profile initialization completed.")
            : new OperationResult(false,
                $"Windows did not finish initializing the profile for {Environment.MachineName}\\{username}. Expected '{expectedPath}' (registered path: '{path ?? "none"}', state: 0x{state:X}).");
    }

    private static string GetUserSid(string username) =>
        ((SecurityIdentifier)new NTAccount(Environment.MachineName, username)
            .Translate(typeof(SecurityIdentifier))).Value;

    private OrphanedProfile? ReadOrphanedProfile(RegistryKey profileList, string sid, IReadOnlySet<string> existingUsernames)
    {
        using var profileKey = profileList.OpenSubKey(sid);
        var path = profileKey?.GetValue("ProfileImagePath") as string;
        if (!TryGetManagedUsername(path, out var username) || existingUsernames.Contains(username))
            return null;
        return new OrphanedProfile(sid, username, path!);
    }

    private static bool TryGetManagedUsername(string? profilePath, out string username)
    {
        username = string.Empty;
        if (string.IsNullOrWhiteSpace(profilePath)) return false;
        var normalized = Path.GetFullPath(profilePath);
        if (!normalized.StartsWith(UserProfilesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;
        var match = ManagedProfileName.Match(Path.GetFileName(normalized));
        if (!match.Success) return false;
        username = match.Groups["username"].Value;
        return true;
    }

    private string? PreserveProfileDirectory(string profilePath, string username, string sid)
    {
        if (!Directory.Exists(profilePath)) return null;
        var target = Path.Combine(_recoveryRoot,
            $"{username}-{sid}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.Move(profilePath, target);
        return target;
    }

    private void WriteRecoveryRecord(OrphanedProfile profile, string? preservedPath)
    {
        var record = Path.Combine(_recoveryRoot,
            $"{profile.Username}-{profile.Sid}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
        File.WriteAllText(record,
            $"Removed orphaned Windows profile registration.{Environment.NewLine}" +
            $"SID: {profile.Sid}{Environment.NewLine}" +
            $"Profile path: {profile.Path}{Environment.NewLine}" +
            $"Preserved folder: {preservedPath ?? "(none existed)"}{Environment.NewLine}");
    }

    private static IEnumerable<DirectoryInfo> GetIncompleteProfileDirectories(string username)
    {
        var root = new DirectoryInfo(UserProfilesRoot);
        var prefix = username + "." + Environment.MachineName;
        return root.EnumerateDirectories()
            .Where(directory => string.Equals(directory.Name, username, StringComparison.OrdinalIgnoreCase)
                || directory.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record OrphanedProfile(string Sid, string Username, string Path);
}
