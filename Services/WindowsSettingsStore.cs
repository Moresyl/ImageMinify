using System.Text.Json;

namespace ImageMinify.Services;

public sealed class WindowsSettingsStore : ISettingsStore
{
    private readonly string _settingsPath;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, JsonElement> _values;

    public WindowsSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsDirectory = Path.Combine(appData, "ImageMinify");
        Directory.CreateDirectory(settingsDirectory);

        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
        _values = LoadValues(_settingsPath);
    }

    public object? GetValue(string key)
    {
        lock (_gate)
        {
            return !_values.TryGetValue(key, out var value)
                ? null
                : value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
                    JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
                    JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => value.GetRawText(),
                };
        }
    }

    public void SetValue(string key, object value)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    public void SetValues(IReadOnlyDictionary<string, object> values)
    {
        lock (_gate)
        {
            foreach (var (key, value) in values)
            {
                _values[key] = JsonSerializer.SerializeToElement(value);
            }

            Save();
        }
    }

    public void Remove(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            var removed = false;
            foreach (var key in keys)
            {
                removed |= _values.Remove(key);
            }

            if (removed)
            {
                Save();
            }
        }
    }

    /// <summary>
    /// Serialises to a temporary file and moves it into place.
    /// </summary>
    /// <remarks>
    /// Settings are saved on almost every interaction, so a write is in flight much of the time a
    /// user has the window open — including when they close it. Writing straight to the real file
    /// means a save interrupted by the process exiting leaves half a JSON document, which does not
    /// parse, and every preference is silently back to its default on next launch.
    /// </remarks>
    private void Save()
    {
        var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        var tempPath = _settingsPath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
        catch (IOException)
        {
            // A locked or full disk. Preferences are a convenience; losing one is not worth taking
            // down whatever the user was actually doing.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: read-only profile directory, roaming policy, antivirus holding the file.
        }
    }

    private static Dictionary<string, JsonElement> LoadValues(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        }
        catch
        {
            // A corrupt settings file starts over rather than blocking startup.
            return [];
        }
    }
}
