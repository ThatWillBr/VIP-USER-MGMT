using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace VIP1132.Services;

public sealed class WindowsUserService
{
    private static readonly Regex NumericUser = new("\\A[0-9]+\\z", RegexOptions.Compiled);

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

    public Task<ProcessResult> CreateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        Validate(username, password);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Resolve the built-in SID so non-English Windows installations work too.
            var group = ((NTAccount)new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                .Translate(typeof(NTAccount))).Value.Split('\\')[^1];
            var info = new UserInfo1
            {
                Name = username, Password = password, Privilege = 1,
                // UF_SCRIPT | UF_NORMAL_ACCOUNT | UF_PASSWD_CANT_CHANGE | UF_DONT_EXPIRE_PASSWD
                Flags = 0x0001 | 0x0200 | 0x0040 | 0x10000
            };
            var status = NetUserAdd(null, 1, ref info, out _);
            if (status != 0) return NativeResult(status, "Creating the Windows user");

            var member = new LocalGroupMember { DomainAndName = Environment.MachineName + "\\" + username };
            status = NetLocalGroupAddMembers(null, group, 3, ref member, 1);
            if (status is not (0 or 1378))
            {
                // Undo only the account just created by this invocation, never an existing one.
                var rollback = NetUserDel(null, username);
                var suffix = rollback == 0 ? " The incomplete account was removed." :
                    " The incomplete account remains; remove it using Manage Users before retrying.";
                return new ProcessResult((int)status, "", "Adding the new user to Administrators failed: " +
                    new Win32Exception((int)status).Message + suffix);
            }
            return new ProcessResult(0, "Windows user created with a non-expiring password and administrator membership.", "");
        }, cancellationToken);
    }

    private static ProcessResult NativeResult(uint status, string operation) =>
        new((int)status, "", operation + " failed: " + new Win32Exception((int)status).Message);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo1
    {
        public string Name;
        public string Password;
        public uint PasswordAge;
        public uint Privilege;
        public string? HomeDirectory;
        public string? Comment;
        public uint Flags;
        public string? ScriptPath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LocalGroupMember { public string DomainAndName; }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetUserAdd(string? server, uint level, ref UserInfo1 info, out uint parameterError);

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetLocalGroupAddMembers(string? server, string group, uint level,
        ref LocalGroupMember member, uint entries);

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint NetUserDel(string? server, string username);

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
