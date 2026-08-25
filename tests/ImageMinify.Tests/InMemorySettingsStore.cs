using ImageMinify.Services;

namespace ImageMinify.Tests;

/// <summary>
/// A settings store backed by a dictionary, so the settings tests exercise the service's own logic
/// rather than the JSON file store's.
/// </summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> _values = [];

    /// <summary>How many times the store has been written to. The debounce tests read this.</summary>
    public int WriteCount { get; private set; }

    public object? GetValue(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    public void SetValue(string key, object value)
    {
        _values[key] = value;
        WriteCount++;
    }

    public void SetValues(IReadOnlyDictionary<string, object> values)
    {
        foreach (var (key, value) in values)
        {
            _values[key] = value;
        }

        // One write, whatever the batch's size — which is the behaviour the real store has and the
        // thing a test asserting "a slider drag is one save" needs to be able to see.
        WriteCount++;
    }

    public void Remove(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            _values.Remove(key);
        }
    }

    /// <summary>True when the key is present. Lets a test assert that a purge actually removed it.</summary>
    public bool Contains(string key) => _values.ContainsKey(key);
}
