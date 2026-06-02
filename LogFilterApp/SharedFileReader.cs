using System.IO;

namespace LogFilterApp;

/// <summary>
/// Leitura de arquivos que podem estar sendo escritos por outro processo (ex: log4net, Serilog, NLog).
/// `FileShare.ReadWrite | FileShare.Delete` permite que o writer continue escrevendo/rotacionando
/// enquanto a gente lê uma snapshot do conteúdo atual.
/// </summary>
public static class SharedFileReader
{
    public static FileStream OpenForSharedReading(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public static async Task<string> ReadAllTextAsync(string path, CancellationToken ct = default)
    {
        using var stream = OpenForSharedReading(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
