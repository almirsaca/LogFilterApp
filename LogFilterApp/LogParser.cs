using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LogFilterApp;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; }
    public List<string> Lines { get; } = new();

    private string? _cachedText;
    public string FullText => _cachedText ??= string.Join(Environment.NewLine, Lines);
}

public sealed class TimestampPreset
{
    private Regex? _regex;

    public required string Name { get; init; }
    public required string RegexPattern { get; init; }
    public required string[] DateFormats { get; init; }
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
    public bool NormalizeComma { get; init; }

    public Regex Regex => _regex ??= new Regex(RegexPattern, RegexOptions.Compiled);

    public override string ToString() => Name;
}

public static class LogParser
{
    public static readonly TimestampPreset[] KnownPresets =
    [
        new TimestampPreset
        {
            Name = "yyyy-MM-dd HH:mm:ss,fff  (2026-01-27 08:56:09,121)",
            RegexPattern = @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}[,\.]\d{3})",
            DateFormats = ["yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss,fff"],
            NormalizeComma = true
        },
        new TimestampPreset
        {
            Name = "dd/MM/yyyy HH:mm:ss.fff  (19/01/2026 16:44:27.428)",
            RegexPattern = @"(\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}:\d{2}\.\d{3})",
            DateFormats = ["dd/MM/yyyy HH:mm:ss.fff"]
        },
        new TimestampPreset
        {
            Name = "ddd MMM dd yyyy HH:mm:ss  (Fri Apr 10 2026 14:57:25)",
            RegexPattern = @"([A-Za-z]{3}\s+[A-Za-z]{3}\s+\d{1,2}\s+\d{4}\s+\d{2}:\d{2}:\d{2})",
            DateFormats = ["ddd MMM dd yyyy HH:mm:ss", "ddd MMM d yyyy HH:mm:ss"],
            Culture = CultureInfo.GetCultureInfo("en-US")
        }
    ];

    public static TimestampPreset? DetectPreset(string input)
    {
        using var reader = new StringReader(input);
        return DetectPreset(reader);
    }

    public static TimestampPreset? DetectPreset(TextReader reader, int sampleLineLimit = 30)
    {
        var sampleLines = new List<string>();

        string? line;
        while ((line = reader.ReadLine()) != null && sampleLines.Count < sampleLineLimit)
        {
            if (!string.IsNullOrWhiteSpace(line))
                sampleLines.Add(line);
        }

        if (sampleLines.Count == 0)
            return null;

        foreach (var preset in KnownPresets)
        {
            int matchCount = 0;

            foreach (var sample in sampleLines)
            {
                var match = preset.Regex.Match(sample);
                if (!match.Success)
                    continue;

                var raw = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                if (TryParseWithPreset(raw, preset, out _))
                {
                    matchCount++;
                    if (matchCount >= 2)
                        return preset;
                }
            }

            if (matchCount > 0 && sampleLines.Count <= 2)
                return preset;
        }

        return null;
    }

    public static bool ValidatePresetAgainstInput(string input, TimestampPreset preset)
    {
        using var reader = new StringReader(input);

        string? line;
        int linesChecked = 0;

        while ((line = reader.ReadLine()) != null && linesChecked < 20)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            linesChecked++;
            var match = preset.Regex.Match(line);
            if (match.Success)
            {
                var raw = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                if (TryParseWithPreset(raw, preset, out _))
                    return true;
            }
        }

        return false;
    }

    public static List<LogEntry> ParseLogEntries(string input, TimestampPreset preset, CancellationToken ct = default)
    {
        using var reader = new StringReader(input);
        return ParseLogEntries(reader, preset, ct);
    }

    public static List<LogEntry> ParseLogEntries(TextReader reader, TimestampPreset preset, CancellationToken ct = default)
    {
        var entries = new List<LogEntry>();
        LogEntry? currentEntry = null;

        string? line;
        int lineCount = 0;

        while ((line = reader.ReadLine()) != null)
        {
            if ((++lineCount & 0xFFF) == 0)
                ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var match = preset.Regex.Match(line);
            if (match.Success)
            {
                var rawTimestamp = match.Groups.Count > 1
                    ? match.Groups[1].Value
                    : match.Value;

                if (string.IsNullOrWhiteSpace(rawTimestamp))
                    continue;

                if (TryParseWithPreset(rawTimestamp, preset, out var timestamp))
                {
                    currentEntry = new LogEntry { Timestamp = timestamp };
                    currentEntry.Lines.Add(line);
                    entries.Add(currentEntry);
                }
                else if (currentEntry != null)
                {
                    currentEntry.Lines.Add(line);
                }
                else
                {
                    currentEntry = new LogEntry { Timestamp = DateTime.MinValue };
                    currentEntry.Lines.Add(line);
                    entries.Add(currentEntry);
                }
            }
            else if (currentEntry != null)
            {
                currentEntry.Lines.Add(line);
            }
            else
            {
                currentEntry = new LogEntry { Timestamp = DateTime.MinValue };
                currentEntry.Lines.Add(line);
                entries.Add(currentEntry);
            }
        }

        return entries;
    }

    public static FilterResult Filter(string input, string filterPattern, TimestampPreset preset,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var parsedEntries = ParseLogEntries(input, preset, ct);
        return Filter(parsedEntries, filterPattern, startDate, endDate, ct);
    }

    public static FilterResult Filter(IReadOnlyList<LogEntry> parsedEntries, string filterPattern,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        IEnumerable<LogEntry> query = parsedEntries;

        if (!string.IsNullOrWhiteSpace(filterPattern))
        {
            query = query.Where(entry =>
                entry.FullText.Contains(filterPattern, StringComparison.OrdinalIgnoreCase));
        }

        if (startDate.HasValue)
        {
            query = query.Where(entry =>
                entry.Timestamp != DateTime.MinValue && entry.Timestamp >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(entry =>
                entry.Timestamp != DateTime.MinValue && entry.Timestamp <= endDate.Value);
        }

        var sortedResults = query
            .OrderBy(entry => entry.Timestamp)
            .ToList();

        ct.ThrowIfCancellationRequested();

        var result = new StringBuilder();
        for (int i = 0; i < sortedResults.Count; i++)
        {
            if ((i & 0xFFF) == 0)
                ct.ThrowIfCancellationRequested();
            result.AppendLine(sortedResults[i].FullText);
        }

        return new FilterResult
        {
            Text = result.ToString(),
            Entries = sortedResults,
            TotalParsed = parsedEntries.Count,
            TotalFiltered = sortedResults.Count
        };
    }

    private static bool TryParseWithPreset(string raw, TimestampPreset preset, out DateTime result)
    {
        string normalized = preset.NormalizeComma ? raw.Replace(',', '.').Trim() : raw.Trim();

        return DateTime.TryParseExact(
            normalized,
            preset.DateFormats,
            preset.Culture,
            DateTimeStyles.None,
            out result);
    }
}

public sealed class FilterResult
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<LogEntry> Entries { get; init; } = Array.Empty<LogEntry>();
    public int TotalParsed { get; init; }
    public int TotalFiltered { get; init; }
}
