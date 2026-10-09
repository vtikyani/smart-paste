using System.Text.Json;

namespace SmartPaste.App;
internal sealed class AppSettings
{
    internal static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SmartPaste");
    private static string PathName => Path.Combine(DirectoryPath,"settings.json");
    public bool Enabled { get; set; } = true;
    public bool ParseScreenshots { get; set; } = true;
    public bool StartAtLogin { get; set; }
    internal static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathName)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(PathName,JsonSerializer.Serialize(this));
    }
}
