namespace LogFilterApp;

/// <summary>
/// Encapsula o ciclo parse-or-reuse-then-filter.
/// Cache válido enquanto (input, preset) forem as mesmas instâncias da última execução.
/// Não atualiza o cache se a operação foi cancelada — evita race em que uma execução
/// cancelada termina depois da nova e sobrescreve o cache com dados stale.
/// </summary>
public sealed class LogFilterCoordinator
{
    private List<LogEntry>? _cachedEntries;
    private string? _cachedInput;
    private TimestampPreset? _cachedPreset;

    public void Invalidate()
    {
        _cachedEntries = null;
        _cachedInput = null;
        _cachedPreset = null;
    }

    public async Task<FilterResult> FilterAsync(
        string input,
        TimestampPreset preset,
        string filterPattern,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken ct)
    {
        var entries = await GetOrParseEntriesAsync(input, preset, ct);
        ct.ThrowIfCancellationRequested();

        return await Task.Run(
            () => LogParser.Filter(entries, filterPattern, startDate, endDate, ct),
            ct);
    }

    private async Task<List<LogEntry>> GetOrParseEntriesAsync(string input, TimestampPreset preset, CancellationToken ct)
    {
        if (_cachedEntries != null &&
            ReferenceEquals(_cachedPreset, preset) &&
            ReferenceEquals(_cachedInput, input))
        {
            return _cachedEntries;
        }

        var entries = await Task.Run(() => LogParser.ParseLogEntries(input, preset, ct), ct);

        // Só grava no cache se a operação não foi cancelada — caso contrário, uma execução
        // descartada poderia sobrescrever o cache com dados stale.
        if (!ct.IsCancellationRequested)
        {
            _cachedEntries = entries;
            _cachedInput = input;
            _cachedPreset = preset;
        }

        return entries;
    }
}
