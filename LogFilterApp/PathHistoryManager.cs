using System.IO;
using System.Text.Json;

namespace LogFilterApp;

public sealed class PathHistoryEntry
{
    public string Path { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public DateTime LastUsed { get; set; } = DateTime.Now;

    public string DisplayText => string.IsNullOrWhiteSpace(Alias) ? Path : $"{Alias}  ({Path})";

    public override string ToString() => DisplayText;
}

public sealed class PathHistoryManager
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly string _filePath;
    private List<PathHistoryEntry> _entries = [];

    public IReadOnlyList<PathHistoryEntry> Entries => _entries;

    public string FilePath => _filePath;

    public string? LastSaveError { get; private set; }

    public PathHistoryManager() : this(filePath: null) { }

    public PathHistoryManager(string? filePath)
    {
        if (filePath != null)
        {
            _filePath = filePath;
            var dir = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            Load();
            return;
        }

        var appDataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogFilterApp");

        Directory.CreateDirectory(appDataDir);

        _filePath = System.IO.Path.Combine(appDataDir, "path_history.json");

        MigrateFromLegacyLocation();
        Load();
    }

    private void MigrateFromLegacyLocation()
    {
        var legacyPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "path_history.json");

        if (File.Exists(legacyPath) && !File.Exists(_filePath))
        {
            try
            {
                File.Move(legacyPath, _filePath);
            }
            catch
            {
                try { File.Copy(legacyPath, _filePath); } catch { }
            }
        }
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            _entries = [];
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch
        {
            _entries = [];
            return;
        }

        try
        {
            _entries = JsonSerializer.Deserialize<List<PathHistoryEntry>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // JSON corrompido: preserva o arquivo num .bak antes de zerar para não perder dados.
            QuarantineCorruptedFile();
            _entries = [];
        }
    }

    private void QuarantineCorruptedFile()
    {
        try
        {
            var backupPath = _filePath + ".corrupted-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Move(_filePath, backupPath);
        }
        catch
        {
            // Se nem renomear deu, deixa quieto. O Save subsequente vai sobrescrever — mas pelo menos tentamos.
        }
    }

    public bool Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            // Write atomico: grava em tmp e move (reduz risco de arquivo truncado em queda).
            var tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            if (File.Exists(_filePath))
                File.Replace(tmpPath, _filePath, destinationBackupFileName: null);
            else
                File.Move(tmpPath, _filePath);
            LastSaveError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastSaveError = ex.Message;
            return false;
        }
    }

    public bool Contains(string directoryPath)
    {
        return _entries.Any(e =>
            string.Equals(e.Path, directoryPath, StringComparison.OrdinalIgnoreCase));
    }

    public bool AddOrUpdate(string directoryPath, string alias)
    {
        var existing = _entries.FirstOrDefault(e =>
            string.Equals(e.Path, directoryPath, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Alias = alias;
            existing.LastUsed = DateTime.Now;
        }
        else
        {
            _entries.Add(new PathHistoryEntry
            {
                Path = directoryPath,
                Alias = alias,
                LastUsed = DateTime.Now
            });
        }

        return Save();
    }

    public bool UpdateLastUsed(string directoryPath)
    {
        var entry = _entries.FirstOrDefault(e =>
            string.Equals(e.Path, directoryPath, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
            return true;

        entry.LastUsed = DateTime.Now;
        return Save();
    }

    public bool Remove(PathHistoryEntry entry)
    {
        _entries.Remove(entry);
        return Save();
    }
}
