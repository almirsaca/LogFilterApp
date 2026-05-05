using System.IO;

namespace LogFilterApp.Tests;

public class PathHistoryManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _historyFile;

    public PathHistoryManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"LogFilterApp_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _historyFile = Path.Combine(_testDir, "path_history.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { }
    }

    private PathHistoryManager NewManager() => new(_historyFile);

    [Fact]
    public void AddOrUpdate_NewEntry_AddsToList()
    {
        var manager = NewManager();
        var testPath = Path.Combine(_testDir, "logs");
        Directory.CreateDirectory(testPath);

        manager.AddOrUpdate(testPath, "Test Logs");

        Assert.True(manager.Contains(testPath));
        var entry = manager.Entries.First(e =>
            string.Equals(e.Path, testPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Test Logs", entry.Alias);
    }

    [Fact]
    public void AddOrUpdate_ExistingEntry_UpdatesAlias()
    {
        var manager = NewManager();
        var testPath = Path.Combine(_testDir, "logs2");
        Directory.CreateDirectory(testPath);

        manager.AddOrUpdate(testPath, "Old Alias");
        manager.AddOrUpdate(testPath, "New Alias");

        var entry = manager.Entries.First(e =>
            string.Equals(e.Path, testPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("New Alias", entry.Alias);
    }

    [Fact]
    public void Contains_CaseInsensitive()
    {
        var manager = NewManager();
        var testPath = Path.Combine(_testDir, "CasePath");
        Directory.CreateDirectory(testPath);

        manager.AddOrUpdate(testPath, "Test");

        Assert.True(manager.Contains(testPath.ToUpper()));
        Assert.True(manager.Contains(testPath.ToLower()));
    }

    [Fact]
    public void Remove_RemovesFromList()
    {
        var manager = NewManager();
        var testPath = Path.Combine(_testDir, "toremove");
        Directory.CreateDirectory(testPath);

        manager.AddOrUpdate(testPath, "To Remove");
        var entry = manager.Entries.First(e =>
            string.Equals(e.Path, testPath, StringComparison.OrdinalIgnoreCase));

        manager.Remove(entry);

        Assert.False(manager.Contains(testPath));
    }

    [Fact]
    public void UpdateLastUsed_UpdatesTimestamp()
    {
        var manager = NewManager();
        var testPath = Path.Combine(_testDir, "updatetime");
        Directory.CreateDirectory(testPath);

        manager.AddOrUpdate(testPath, "Time Test");
        var entry = manager.Entries.First(e =>
            string.Equals(e.Path, testPath, StringComparison.OrdinalIgnoreCase));
        var originalTime = entry.LastUsed;

        Thread.Sleep(50);
        manager.UpdateLastUsed(testPath);

        Assert.True(entry.LastUsed >= originalTime);
    }

    [Fact]
    public void Persistence_AcrossInstances_RestoresEntries()
    {
        var path = Path.Combine(_testDir, "persisted");
        Directory.CreateDirectory(path);

        {
            var manager = NewManager();
            manager.AddOrUpdate(path, "Persisted");
        }

        var reopened = NewManager();
        Assert.True(reopened.Contains(path));
        Assert.Equal("Persisted", reopened.Entries.Single().Alias);
    }

    [Fact]
    public void PathHistoryEntry_DisplayText_WithAlias()
    {
        var entry = new PathHistoryEntry
        {
            Path = @"C:\Logs",
            Alias = "Production"
        };

        Assert.Equal(@"Production  (C:\Logs)", entry.DisplayText);
    }

    [Fact]
    public void PathHistoryEntry_DisplayText_WithoutAlias()
    {
        var entry = new PathHistoryEntry
        {
            Path = @"C:\Logs",
            Alias = ""
        };

        Assert.Equal(@"C:\Logs", entry.DisplayText);
    }

    [Fact]
    public void Save_ReturnsTrue_OnHappyPath()
    {
        var manager = NewManager();
        var testPath = Path.Combine(_testDir, "happy");
        Directory.CreateDirectory(testPath);

        Assert.True(manager.AddOrUpdate(testPath, "ok"));
        Assert.Null(manager.LastSaveError);
    }

    [Fact]
    public void Load_CorruptedJson_QuarantinesAndStartsFresh()
    {
        File.WriteAllText(_historyFile, "{not valid json at all");

        var manager = NewManager();

        // Não deve crashar. _entries deve estar vazio.
        Assert.Empty(manager.Entries);

        // Deve existir um arquivo .bak ao lado.
        var dir = Path.GetDirectoryName(_historyFile)!;
        var backups = Directory.GetFiles(dir, "*.bak");
        Assert.NotEmpty(backups);
        Assert.Contains(backups, b => b.Contains("corrupted-"));
    }

    [Fact]
    public void Save_AfterCorrupted_Recovers()
    {
        File.WriteAllText(_historyFile, "{not valid json at all");

        var manager = NewManager();
        var testPath = Path.Combine(_testDir, "after-corrupted");
        Directory.CreateDirectory(testPath);

        Assert.True(manager.AddOrUpdate(testPath, "recovered"));

        var reopened = NewManager();
        Assert.True(reopened.Contains(testPath));
        Assert.Equal("recovered", reopened.Entries.Single().Alias);
    }
}
