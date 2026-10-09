using System.Runtime.InteropServices;
using System.Text;
using SmartPaste.App.Native;

namespace SmartPaste.App;
internal static class CredentialStore
{
    internal const string OpenRouter="SmartPaste/OpenRouter", TypeSafe="SmartPaste/TypeSafe";
    internal static string? Get(string target)
    {
        if(!Win32.CredRead(target,Win32.CredTypeGeneric,0,out var pointer)) return null;
        try { var credential=Marshal.PtrToStructure<Win32.Credential>(pointer);
            return credential.CredentialBlobSize>0?Marshal.PtrToStringUni(credential.CredentialBlob,(int)credential.CredentialBlobSize/2):null; }
        finally { Win32.CredFree(pointer); }
    }
    internal static void Set(string target,string value)
    {
        byte[] blob=Encoding.Unicode.GetBytes(value);
        IntPtr pointer=Marshal.AllocHGlobal(blob.Length);
        try { Marshal.Copy(blob,0,pointer,blob.Length);
            var credential=new Win32.Credential { Type=Win32.CredTypeGeneric,TargetName=target,CredentialBlob=pointer,
                CredentialBlobSize=(uint)blob.Length,Persist=Win32.CredPersistLocalMachine,UserName=Environment.UserName };
            if(!Win32.CredWrite(ref credential,0)) throw new InvalidOperationException("Credential save failed."); }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    internal static void Delete(string target) => Win32.CredDelete(target,Win32.CredTypeGeneric,0);
}
