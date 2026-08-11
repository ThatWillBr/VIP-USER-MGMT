using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace VIP1132.Services;

public static class NativeSessionLauncher
{
    private const int DaclSecurityInformation = 0x00000004;
    private const int WinstaAllAccess = 0x000F037F;
    private const int DesktopAllAccess = 0x000F01FF;

    public static int LaunchAsUser(string username, string password, string executable, IReadOnlyList<string> arguments)
    {
        GrantInteractiveDesktopAccess(username);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            Domain = Environment.MachineName,
            UserName = username,
            PasswordInClearText = password,
            LoadUserProfile = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            var process = Process.Start(startInfo)
                ?? throw new Win32Exception("Windows did not return a Zoom process.");
            var pid = process.Id;
            process.Dispose();
            return pid;
        }
        catch (Win32Exception ex)
        {
            throw new Win32Exception(ex.NativeErrorCode,
                $"Windows could not start Zoom as {Environment.MachineName}\\{username}: {ex.Message}");
        }
    }

    public static string? TryGetProcessOwner(Process process)
    {
        const uint tokenQuery = 0x0008;
        try
        {
            if (!OpenProcessToken(process.Handle, tokenQuery, out var token))
                return null;
            try
            {
                using var identity = new System.Security.Principal.WindowsIdentity(token);
                return identity.Name;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void GrantInteractiveDesktopAccess(string username)
    {
        var account = new NTAccount(Environment.MachineName, username);
        var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));

        GrantUserObjectAccess(GetProcessWindowStation(), sid, WinstaAllAccess);
        GrantUserObjectAccess(GetThreadDesktop(GetCurrentThreadId()), sid, DesktopAllAccess);
    }

    private static void GrantUserObjectAccess(IntPtr handle, SecurityIdentifier sid, int accessMask)
    {
        var securityInformation = DaclSecurityInformation;
        if (!GetUserObjectSecurity(handle, ref securityInformation, IntPtr.Zero, 0, out var needed) && needed <= 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the interactive desktop security descriptor.");

        var descriptorBuffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!GetUserObjectSecurity(handle, ref securityInformation, descriptorBuffer, needed, out needed))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the interactive desktop security descriptor.");

            var descriptorBytes = new byte[needed];
            Marshal.Copy(descriptorBuffer, descriptorBytes, 0, needed);
            var descriptor = new RawSecurityDescriptor(descriptorBytes, 0);
            var dacl = descriptor.DiscretionaryAcl ?? new RawAcl(2, 1);

            foreach (GenericAce genericAce in dacl)
            {
                if (genericAce is CommonAce ace &&
                    ace.AceQualifier == AceQualifier.AccessAllowed &&
                    ace.SecurityIdentifier.Equals(sid) &&
                    (ace.AccessMask & accessMask) == accessMask)
                {
                    return;
                }
            }

            dacl.InsertAce(dacl.Count, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, accessMask, sid, false, null));
            descriptor.DiscretionaryAcl = dacl;

            var updatedBytes = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(updatedBytes, 0);
            var updatedBuffer = Marshal.AllocHGlobal(updatedBytes.Length);
            try
            {
                Marshal.Copy(updatedBytes, 0, updatedBuffer, updatedBytes.Length);
                if (!SetUserObjectSecurity(handle, ref securityInformation, updatedBuffer))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not grant access to the interactive desktop.");
            }
            finally
            {
                Marshal.FreeHGlobal(updatedBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(descriptorBuffer);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectSecurity(
        IntPtr hObj,
        ref int pSIRequested,
        IntPtr pSID,
        int nLength,
        out int lpnLengthNeeded);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetUserObjectSecurity(
        IntPtr hObj,
        ref int pSIRequested,
        IntPtr pSID);
}
