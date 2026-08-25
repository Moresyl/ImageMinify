namespace ImageMinify.Services;

public interface ISettingsStore
{
    object? GetValue(string key);

    void SetValue(string key, object value);

    /// <summary>
    /// Writes several keys as one operation.
    /// </summary>
    /// <remarks>
    /// The whole point is that the backing file is written once. Saving the settings touches around
    /// fifteen keys, and a save happens every time a slider moves — one file write per key would
    /// mean hundreds of writes to disk during a single drag.
    /// </remarks>
    void SetValues(IReadOnlyDictionary<string, object> values);

    /// <summary>Deletes keys, ignoring any that are not there.</summary>
    void Remove(IEnumerable<string> keys);
}
