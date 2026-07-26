namespace EasyUnpack.Core.Passwords;

public sealed class PasswordVault
{
    private readonly List<PasswordEntry> _entries;

    public PasswordVault(IEnumerable<PasswordEntry>? entries = null)
    {
        _entries = [];
        if (entries is null) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Value) && seen.Add(entry.Value)) _entries.Add(entry);
        }
    }

    public IReadOnlyList<PasswordEntry> Entries => _entries;

    public IReadOnlyList<string> GetCandidates() => _entries.Select(static entry => entry.Value).ToArray();

    public bool Add(string password, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (_entries.Any(entry => string.Equals(entry.Value, password, StringComparison.Ordinal))) return false;

        _entries.Insert(0, new PasswordEntry(Guid.NewGuid(), password, NormalizeLabel(label), null, 0));
        return true;
    }

    public bool Update(Guid id, string password, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var index = _entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || _entries.Any(entry => entry.Id != id && string.Equals(entry.Value, password, StringComparison.Ordinal))) return false;

        var entry = _entries[index];
        _entries[index] = entry with { Value = password, Label = NormalizeLabel(label) };
        return true;
    }

    public bool Move(Guid id, int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= _entries.Count) return false;
        var sourceIndex = _entries.FindIndex(entry => entry.Id == id);
        if (sourceIndex < 0 || sourceIndex == targetIndex) return sourceIndex >= 0;

        var entry = _entries[sourceIndex];
        _entries.RemoveAt(sourceIndex);
        _entries.Insert(targetIndex, entry);
        return true;
    }

    public void RecordSuccessfulPassword(string password, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var index = _entries.FindIndex(entry => string.Equals(entry.Value, password, StringComparison.Ordinal));
        if (index >= 0)
        {
            var entry = _entries[index];
            var updated = entry with
            {
                Label = string.IsNullOrWhiteSpace(label) ? entry.Label : NormalizeLabel(label),
                LastSuccessfulAt = DateTimeOffset.UtcNow,
                SuccessCount = checked(entry.SuccessCount + 1),
            };
            _entries.RemoveAt(index);
            _entries.Insert(0, updated);
            return;
        }

        _entries.Insert(0, new PasswordEntry(Guid.NewGuid(), password, NormalizeLabel(label), DateTimeOffset.UtcNow, 1));
    }

    public bool Remove(Guid id) => _entries.RemoveAll(entry => entry.Id == id) > 0;

    private static string? NormalizeLabel(string? label) => string.IsNullOrWhiteSpace(label) ? null : label.Trim();
}
