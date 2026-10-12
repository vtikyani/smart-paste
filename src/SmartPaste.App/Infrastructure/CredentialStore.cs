using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartPaste.App.Infrastructure;

// API keys live in Windows Credential Manager (generic credentials), never in a file. Windows encrypts them
// for the signed-in user.
internal static class CredentialStore
{
    public const string OpenRouter = "SmartPaste/OpenRouter";
    public const string TypeSafe = "SmartPaste/TypeSafe";

    public static readonly IReadOnlyList<string> All = [OpenRouter, TypeSafe];

    // Raised after a key is saved or deleted. Argument: the credential target.
    public static event Action<string>? Changed;

    public static string? Get(string target)
    {
        if (!NativeMethods.CredRead(target, NativeMethods.CredTypeGeneric, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero) return null;
            var value = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        finally
        {
            NativeMethods.CredFree(pointer);
        }
    }

    public static bool Exists(string target) => Get(target) is not null;

    public static void Set(string target, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var blob = Encoding.Unicode.GetBytes(value.Trim());
        var pointer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, pointer, blob.Length);
            var credential = new NativeMethods.Credential
            {
                Type = NativeMethods.CredTypeGeneric,
                TargetName = target,
                CredentialBlob = pointer,
                CredentialBlobSize = (uint)blob.Length,
                Persist = NativeMethods.CredPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not save the key.");
        }
        finally
        {
            // Wipe the plaintext copy before releasing the buffer.
            Marshal.Copy(new byte[blob.Length], 0, pointer, blob.Length);
            Marshal.FreeHGlobal(pointer);
            Array.Clear(blob);
        }
        Changed?.Invoke(target);
    }

    public static void Delete(string target)
    {
        if (NativeMethods.CredDelete(target, NativeMethods.CredTypeGeneric, 0)) Changed?.Invoke(target);
    }

    public static void DeleteAll()
    {
        foreach (var target in All) Delete(target);
    }

    private static class NativeMethods
    {
        public const uint CredTypeGeneric = 1;
        public const uint CredPersistLocalMachine = 2;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredReadW", SetLastError = true)]
        public static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredWriteW", SetLastError = true)]
        public static extern bool CredWrite(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW", SetLastError = true)]
        public static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree")]
        public static extern void CredFree(IntPtr credential);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Credential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }
    }
}
