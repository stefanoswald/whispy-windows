using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Whispy;

public class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Now;
    public string RawText { get; set; } = "";
    public string CleanedText { get; set; } = "";
    public WritingMode Mode { get; set; }
    public string? AppName { get; set; }
}

/// <summary>
/// Local dictation history, encrypted at rest with Windows DPAPI (per-user
/// key managed by the OS — silent, no password prompts, survives rebuilds).
/// </summary>
public sealed class HistoryStore
{
    public List<HistoryEntry> Entries { get; private set; } = new();

    private static string FilePath => Path.Combine(AppSettings.AppDataDir, "history.bin");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public HistoryStore() => Load();

    public void Add(HistoryEntry entry, int limit)
    {
        Entries.Insert(0, entry);
        if (Entries.Count > limit)
        {
            Entries.RemoveRange(limit, Entries.Count - limit);
        }
        Persist();
    }

    public void Delete(Guid id)
    {
        Entries.RemoveAll(e => e.Id == id);
        Persist();
    }

    public void ClearAll()
    {
        Entries.Clear();
        try { File.Delete(FilePath); } catch { }
    }

    public List<HistoryEntry> Search(string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return Entries;
        return Entries.Where(e =>
            e.CleanedText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.RawText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (e.AppName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var encrypted = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var decoded = JsonSerializer.Deserialize<List<HistoryEntry>>(
                Encoding.UTF8.GetString(plain), JsonOptions);
            if (decoded != null) Entries = decoded;
        }
        catch
        {
            // Unreadable history starts fresh rather than crashing.
        }
    }

    private void Persist()
    {
        try
        {
            var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Entries, JsonOptions));
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, encrypted);
        }
        catch
        {
            // Non-fatal.
        }
    }
}
