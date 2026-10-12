using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SmartPaste.App.Infrastructure;

// Loads and saves settings.json. Saves are atomic (temp file, then rename), so a crash mid-save never leaves a
// half-written file. Reads hand out immutable snapshots; Update serializes writers.
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _gate = new();
    private readonly string _path;
    private AppSettings _current;

    public SettingsStore(string path)
    {
        _path = path;
        (_current, IsFirstRun) = Load(path);
    }

    // True when no settings file existed at startup (a fresh install).
    public bool IsFirstRun { get; }

    public AppSettings Current
    {
        get { lock (_gate) return _current; }
    }

    // Raised after every saved change, on the thread that called Update. Arguments: (previous, current).
    public event Action<AppSettings, AppSettings>? Changed;

    // Applies a change and saves it. Returns the new settings. A failed save keeps the change in memory, logs
    // it, and rethrows so the caller can tell the user.
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings previous, next;
        lock (_gate)
        {
            previous = _current;
            next = change(previous).Normalized();
            // Compare the saved form: record equality would compare the ExcludedApps list by reference.
            var json = JsonSerializer.Serialize(next, Json);
            if (json == JsonSerializer.Serialize(previous, Json)) return previous;
            _current = next;
            Save(json);
        }
        Changed?.Invoke(previous, next);
        return next;
    }

    // Writes the current settings to disk. Also used at first run so the file exists from then on.
    public void SaveNow()
    {
        lock (_gate) Save(JsonSerializer.Serialize(_current, Json));
    }

    private void Save(string json)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        var temp = Path.Combine(directory, $".settings.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("settings_save_error", ex);
            throw;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static (AppSettings Settings, bool FirstRun) Load(string path)
    {
        if (!File.Exists(path)) return (new AppSettings(), true);
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (node is null) return (new AppSettings(), false);
            var settings = IsVersion1(node) ? FromVersion1(node) : node.Deserialize<AppSettings>(Json) ?? new AppSettings();
            return (settings.Normalized(), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings must not stop the app. Defaults apply; the broken file is overwritten on the
            // next change.
            Log.Error("settings_load_error", ex);
            return (new AppSettings(), false);
        }
    }

    // Version 1 (the first prototype) wrote PascalCase names and had no version field.
    private static bool IsVersion1(JsonObject node) =>
        !node.ContainsKey("version") && !node.ContainsKey("Version") &&
        (node.ContainsKey("ParseScreenshots") || node.ContainsKey("Enabled") || node.ContainsKey("StartAtLogin"));

    private static AppSettings FromVersion1(JsonObject node)
    {
        static bool Read(JsonObject n, string name, bool fallback) =>
            n[name] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : fallback;
        return new AppSettings
        {
            Enabled = Read(node, "Enabled", true),
            StartAtLogin = Read(node, "StartAtLogin", false),
            ReadScreenshots = Read(node, "ParseScreenshots", false),
            // Version 1 users have already been using the app; skip the welcome tour.
            OnboardingCompleted = true
        };
    }
}
