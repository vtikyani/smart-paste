using SmartPaste.App.Infrastructure;

namespace SmartPaste.App.UI.Navigation;

// Saved API keys, as the settings window and the welcome tour see them. Targets are CredentialStore.OpenRouter
// and CredentialStore.TypeSafe.
internal interface IApiKeyStore
{
    string? Get(string target);

    bool Exists(string target);

    // Throws Win32Exception when Windows refuses to save the key.
    void Set(string target, string key);

    void Delete(string target);

    // Raised after a key is saved or deleted, on the thread that made the change. Argument: the target.
    event Action<string>? Changed;
}

// Windows Credential Manager.
internal sealed class CredentialKeyStore : IApiKeyStore
{
    public static readonly CredentialKeyStore Instance = new();

    private CredentialKeyStore()
    {
    }

    public event Action<string>? Changed
    {
        add => CredentialStore.Changed += value;
        remove => CredentialStore.Changed -= value;
    }

    public string? Get(string target) => CredentialStore.Get(target);

    public bool Exists(string target) => CredentialStore.Exists(target);

    public void Set(string target, string key) => CredentialStore.Set(target, key);

    public void Delete(string target) => CredentialStore.Delete(target);
}
