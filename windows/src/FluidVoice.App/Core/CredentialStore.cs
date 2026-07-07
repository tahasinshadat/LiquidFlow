using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace FluidVoice.Core;

/// <summary>
/// API keys in Windows Credential Manager (mac parity: Keychain service
/// "com.fluidvoice.provider-api-keys", account "fluidApiKeys" holding one JSON map).
/// We store the same JSON map under a single generic credential.
/// </summary>
public static class CredentialStore
{
    private const string TargetName = "FluidVoice/ProviderAPIKeys";

    public static string? GetApiKey(string providerId)
    {
        var map = ReadMap();
        return map.TryGetValue(providerId, out var key) && !string.IsNullOrWhiteSpace(key) ? key : null;
    }

    public static void SetApiKey(string providerId, string? apiKey)
    {
        var map = ReadMap();
        if (string.IsNullOrWhiteSpace(apiKey)) map.Remove(providerId);
        else map[providerId] = apiKey;
        WriteMap(map);
    }

    public static Dictionary<string, string> ReadMap()
    {
        try
        {
            var blob = ReadCredential(TargetName);
            if (blob is null) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(blob) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("credstore", "Failed to read credential map", ex);
            return new();
        }
    }

    private static void WriteMap(Dictionary<string, string> map)
    {
        try
        {
            WriteCredential(TargetName, JsonSerializer.Serialize(map));
        }
        catch (Exception ex)
        {
            Log.Error("credstore", "Failed to write credential map", ex);
        }
    }

    // ---- Win32 Credential Manager interop ----

    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr cred);

    private static string? ReadCredential(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize <= 0 || cred.CredentialBlob == IntPtr.Zero) return null;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    private static void WriteCredential(string target, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        var targetPtr = Marshal.StringToHGlobalUni(target);
        var userPtr = Marshal.StringToHGlobalUni("fluidApiKeys");
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = targetPtr,
                CredentialBlob = blob,
                CredentialBlobSize = bytes.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = userPtr,
            };
            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException($"CredWrite failed: {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(targetPtr);
            Marshal.FreeHGlobal(userPtr);
        }
    }
}
