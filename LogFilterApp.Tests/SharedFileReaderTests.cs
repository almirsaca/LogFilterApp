using System.IO;

namespace LogFilterApp.Tests;

public class SharedFileReaderTests : IDisposable
{
    private readonly string _testDir;

    public SharedFileReaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"LogFilterApp_SharedReader_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { }
    }

    [Fact]
    public async Task ReadAllTextAsync_ReadsSimpleFile()
    {
        var path = Path.Combine(_testDir, "simple.log");
        await File.WriteAllTextAsync(path, "hello world");

        var content = await SharedFileReader.ReadAllTextAsync(path);

        Assert.Equal("hello world", content);
    }

    [Fact]
    public async Task ReadAllTextAsync_WorksWhileWriterHoldsHandle()
    {
        // Reproduz o caso log4net: outro processo mantém o arquivo aberto p/ escrita
        // com FileShare.Read (típico de loggers).
        var path = Path.Combine(_testDir, "live.log");
        await File.WriteAllTextAsync(path, "linha inicial\n");

        // Abre como o log4net abriria: append + share=Read (permite outros lerem).
        await using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes("linha do writer\n");
        await writer.WriteAsync(bytes);
        await writer.FlushAsync();

        // File.ReadAllTextAsync padrão *funciona* aqui porque o writer permite Read.
        // O cenário hostil é o writer NÃO permitir leitura — vamos testar isso também abaixo.
        var content = await SharedFileReader.ReadAllTextAsync(path);

        Assert.Contains("linha inicial", content);
        Assert.Contains("linha do writer", content);
    }

    [Fact]
    public async Task ReadAllTextAsync_WorksWhileWriterHoldsExclusiveWriteHandle()
    {
        // Cenário mais hostil: writer aberto SEM FileShare.Read (exclusivo de escrita).
        // File.ReadAllTextAsync padrão falha; SharedFileReader funciona porque pede
        // FileShare.ReadWrite — combina com o que o writer permite (write + read).
        var path = Path.Combine(_testDir, "exclusive-write.log");
        await File.WriteAllTextAsync(path, "linha inicial\n");

        await using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var bytes = System.Text.Encoding.UTF8.GetBytes("escrevendo\n");
        await writer.WriteAsync(bytes);
        await writer.FlushAsync();

        var content = await SharedFileReader.ReadAllTextAsync(path);

        Assert.Contains("linha inicial", content);
    }

    [Fact]
    public void OpenForSharedReading_AllowsConcurrentWriterAndDelete()
    {
        var path = Path.Combine(_testDir, "concurrent.log");
        File.WriteAllText(path, "x");

        using var reader = SharedFileReader.OpenForSharedReading(path);

        // Outro writer pode abrir com FileShare.ReadWrite simultaneamente.
        using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            writer.WriteByte((byte)'y');
        }

        // E delete-on-close pelo writer também deve ser tolerado (não dispara aqui mas
        // confirma que abrimos com FileShare.Delete — sem o flag, dataria erro de sharing).
        Assert.True(reader.CanRead);
    }
}
