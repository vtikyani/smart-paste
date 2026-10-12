using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmartPaste.App.Shell;

// Facts about other processes that need only limited query rights, so they work for most processes including
// elevated ones.
internal static class ProcessInfo
{
    // Mandatory integrity levels (the RID of the token's integrity SID).
    public const int UntrustedIntegrity = 0x0000;
    public const int LowIntegrity = 0x1000;
    public const int MediumIntegrity = 0x2000;
    public const int HighIntegrity = 0x3000;
    public const int SystemIntegrity = 0x4000;

    // Executable file name, for example "chrome.exe". Null when the process cannot be opened.
    public static string? TryGetName(uint processId)
    {
        if (processId == 0) return null;
        using var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid) return null;
        var buffer = new char[1024];
        var size = (uint)buffer.Length;
        if (!NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size)) return null;
        return Path.GetFileName(new string(buffer, 0, (int)size));
    }

    // The process's mandatory integrity level. Null when it cannot be read.
    public static int? TryGetIntegrityLevel(uint processId)
    {
        if (processId == 0) return null;
        using var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        return process.IsInvalid ? null : ReadIntegrityLevel(process);
    }

    public static int CurrentIntegrityLevel() =>
        TryGetIntegrityLevel((uint)Environment.ProcessId) ?? MediumIntegrity;

    private static int? ReadIntegrityLevel(SafeProcessHandle process)
    {
        if (!NativeMethods.OpenProcessToken(process, NativeMethods.TokenQuery, out var token)) return null;
        using (token)
        {
            NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return null;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, buffer, needed, out _)) return null;
                // TOKEN_MANDATORY_LABEL starts with the SID pointer; the level is the SID's last sub-authority.
                var sid = Marshal.ReadIntPtr(buffer);
                if (sid == IntPtr.Zero) return null;
                var count = Marshal.ReadByte(NativeMethods.GetSidSubAuthorityCount(sid));
                if (count == 0) return null;
                return Marshal.ReadInt32(NativeMethods.GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
