using System.IO;
using System.Text.RegularExpressions;

namespace LogFilterApp.Tests;

public class LogParserTests
{
    private static readonly TimestampPreset DefaultPreset = LogParser.KnownPresets[0];
    private static readonly TimestampPreset BrPreset = LogParser.KnownPresets[1];
    private static readonly TimestampPreset JsPreset = LogParser.KnownPresets[2];

    // ───── ParseLogEntries ─────

    [Fact]
    public void ParseLogEntries_SingleEntry_ReturnsSingleEntry()
    {
        var input = "2024-01-15 10:30:00,123 INFO Starting application";

        var entries = LogParser.ParseLogEntries(input, DefaultPreset);

        Assert.Single(entries);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0, 123), entries[0].Timestamp);
        Assert.Contains("Starting application", entries[0].FullText);
    }

    [Fact]
    public void ParseLogEntries_MultipleEntries_ReturnsAll()
    {
        var input = """
            2024-01-15 10:30:00,123 INFO First entry
            2024-01-15 10:30:01,456 ERROR Second entry
            2024-01-15 10:30:02,789 WARN Third entry
            """;

        var entries = LogParser.ParseLogEntries(input, DefaultPreset);

        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void ParseLogEntries_MultiLineEntry_GroupsLines()
    {
        var input = """
            2024-01-15 10:30:00,123 ERROR Something failed
            at MyApp.Service.DoWork()
            at MyApp.Program.Main()
            2024-01-15 10:30:01,456 INFO Next entry
            """;

        var entries = LogParser.ParseLogEntries(input, DefaultPreset);

        Assert.Equal(2, entries.Count);
        Assert.Equal(3, entries[0].Lines.Count);
        Assert.Contains("at MyApp.Service.DoWork()", entries[0].FullText);
    }

    [Fact]
    public void ParseLogEntries_OrphanedLines_GetMinTimestamp()
    {
        var input = "This line has no timestamp";

        var entries = LogParser.ParseLogEntries(input, DefaultPreset);

        Assert.Single(entries);
        Assert.Equal(DateTime.MinValue, entries[0].Timestamp);
    }

    [Fact]
    public void ParseLogEntries_BlankLinesSkipped()
    {
        var input = """
            2024-01-15 10:30:00,123 INFO First

            2024-01-15 10:30:01,456 INFO Second
            """;

        var entries = LogParser.ParseLogEntries(input, DefaultPreset);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ParseLogEntries_EmptyInput_ReturnsEmpty()
    {
        var entries = LogParser.ParseLogEntries("", DefaultPreset);

        Assert.Empty(entries);
    }

    // ───── JS Timestamp Format ─────

    [Fact]
    public void ParseLogEntries_JsTimestamp_ParsesCorrectly()
    {
        var input = "communication.service.ts:140 [Fri Apr 10 2026 14:57:30 GMT-0300 (GMT-03:00)] sendMessageToParent";

        var entries = LogParser.ParseLogEntries(input, JsPreset);

        Assert.Single(entries);
        Assert.Equal(new DateTime(2026, 4, 10, 14, 57, 30), entries[0].Timestamp);
    }

    [Fact]
    public void ParseLogEntries_JsTimestamp_MultipleEntries()
    {
        var input = """
            logger-factory.ts:109 Fri Apr 10 2026 14:57:25 some log message
            communication.service.ts:140 [Fri Apr 10 2026 14:57:30 GMT-0300 (GMT-03:00)] sendMessageToParent
            logger-factory.ts:109 Sat Apr 11 2026 08:00:01 another message
            """;

        var entries = LogParser.ParseLogEntries(input, JsPreset);

        Assert.Equal(3, entries.Count);
        Assert.Equal(new DateTime(2026, 4, 10, 14, 57, 25), entries[0].Timestamp);
        Assert.Equal(new DateTime(2026, 4, 10, 14, 57, 30), entries[1].Timestamp);
        Assert.Equal(new DateTime(2026, 4, 11, 8, 0, 1), entries[2].Timestamp);
    }

    // ───── BR Timestamp Format ─────

    [Fact]
    public void ParseLogEntries_BrTimestamp_ParsesCorrectly()
    {
        var input = "19/01/2026 16:44:27.428 INFO Some message";

        var entries = LogParser.ParseLogEntries(input, BrPreset);

        Assert.Single(entries);
        Assert.Equal(new DateTime(2026, 1, 19, 16, 44, 27, 428), entries[0].Timestamp);
    }

    // ───── Filter ─────

    [Fact]
    public void Filter_WithPattern_ReturnsOnlyMatches()
    {
        var input = """
            2024-01-15 10:30:00,123 ERROR Something failed
            2024-01-15 10:30:01,456 INFO All good
            2024-01-15 10:30:02,789 ERROR Another failure
            """;

        var result = LogParser.Filter(input, "ERROR", DefaultPreset);

        Assert.Contains("Something failed", result.Text);
        Assert.Contains("Another failure", result.Text);
        Assert.DoesNotContain("All good", result.Text);
        Assert.Equal(2, result.TotalFiltered);
        Assert.Equal(3, result.TotalParsed);
    }

    [Fact]
    public void Filter_CaseInsensitive()
    {
        var input = "2024-01-15 10:30:00,123 ERROR Something failed";

        var result = LogParser.Filter(input, "error", DefaultPreset);

        Assert.Contains("Something failed", result.Text);
    }

    [Fact]
    public void Filter_EmptyPattern_ReturnsAll()
    {
        var input = """
            2024-01-15 10:30:00,123 INFO First
            2024-01-15 10:30:01,456 INFO Second
            """;

        var result = LogParser.Filter(input, "", DefaultPreset);

        Assert.Contains("First", result.Text);
        Assert.Contains("Second", result.Text);
    }

    [Fact]
    public void Filter_SortsByTimestamp()
    {
        var input = """
            2024-01-15 10:30:02,000 INFO Third
            2024-01-15 10:30:00,000 INFO First
            2024-01-15 10:30:01,000 INFO Second
            """;

        var result = LogParser.Filter(input, "", DefaultPreset);

        var firstIndex = result.Text.IndexOf("First");
        var secondIndex = result.Text.IndexOf("Second");
        var thirdIndex = result.Text.IndexOf("Third");

        Assert.True(firstIndex < secondIndex);
        Assert.True(secondIndex < thirdIndex);
    }

    // ───── Filter com intervalo de data ─────

    [Fact]
    public void Filter_WithStartDate_FiltersOlderEntries()
    {
        var input = """
            2024-01-15 08:00:00,000 INFO Too early
            2024-01-15 12:00:00,000 INFO In range
            2024-01-15 18:00:00,000 INFO Also in range
            """;

        var result = LogParser.Filter(input, "", DefaultPreset,
            startDate: new DateTime(2024, 1, 15, 10, 0, 0));

        Assert.DoesNotContain("Too early", result.Text);
        Assert.Contains("In range", result.Text);
        Assert.Contains("Also in range", result.Text);
        Assert.Equal(2, result.TotalFiltered);
    }

    [Fact]
    public void Filter_WithEndDate_FiltersNewerEntries()
    {
        var input = """
            2024-01-15 08:00:00,000 INFO In range
            2024-01-15 12:00:00,000 INFO Also in range
            2024-01-15 18:00:00,000 INFO Too late
            """;

        var result = LogParser.Filter(input, "", DefaultPreset,
            endDate: new DateTime(2024, 1, 15, 15, 0, 0));

        Assert.Contains("In range", result.Text);
        Assert.Contains("Also in range", result.Text);
        Assert.DoesNotContain("Too late", result.Text);
        Assert.Equal(2, result.TotalFiltered);
    }

    [Fact]
    public void Filter_WithDateRange_FiltersOutside()
    {
        var input = """
            2024-01-15 08:00:00,000 INFO Before
            2024-01-15 12:00:00,000 INFO Inside
            2024-01-15 18:00:00,000 INFO After
            """;

        var result = LogParser.Filter(input, "", DefaultPreset,
            startDate: new DateTime(2024, 1, 15, 10, 0, 0),
            endDate: new DateTime(2024, 1, 15, 15, 0, 0));

        Assert.DoesNotContain("Before", result.Text);
        Assert.Contains("Inside", result.Text);
        Assert.DoesNotContain("After", result.Text);
        Assert.Equal(1, result.TotalFiltered);
    }

    // ───── DetectPreset ─────

    [Fact]
    public void DetectPreset_DefaultFormat_Detected()
    {
        var input = """
            2024-01-15 10:30:00,123 INFO First
            2024-01-15 10:30:01,456 INFO Second
            """;

        var preset = LogParser.DetectPreset(input);

        Assert.NotNull(preset);
        Assert.Same(DefaultPreset, preset);
    }

    [Fact]
    public void DetectPreset_JsFormat_Detected()
    {
        var input = """
            logger-factory.ts:109 Fri Apr 10 2026 14:57:25 some log
            communication.service.ts:140 [Fri Apr 10 2026 14:57:30 GMT-0300] sendMessage
            """;

        var preset = LogParser.DetectPreset(input);

        Assert.NotNull(preset);
        Assert.Same(JsPreset, preset);
    }

    [Fact]
    public void DetectPreset_BrFormat_Detected()
    {
        var input = """
            19/01/2026 16:44:27.428 INFO msg1
            19/01/2026 16:44:28.100 INFO msg2
            """;

        var preset = LogParser.DetectPreset(input);

        Assert.NotNull(preset);
        Assert.Same(BrPreset, preset);
    }

    [Fact]
    public void DetectPreset_NoTimestamp_ReturnsNull()
    {
        var input = """
            just some random text
            without any timestamps
            """;

        var preset = LogParser.DetectPreset(input);

        Assert.Null(preset);
    }

    [Fact]
    public void DetectPreset_EmptyInput_ReturnsNull()
    {
        Assert.Null(LogParser.DetectPreset(""));
    }

    // ───── ValidatePresetAgainstInput ─────

    [Fact]
    public void ValidatePreset_CorrectPreset_ReturnsTrue()
    {
        var input = "2024-01-15 10:30:00,123 INFO Test";

        Assert.True(LogParser.ValidatePresetAgainstInput(input, DefaultPreset));
    }

    [Fact]
    public void ValidatePreset_WrongPreset_ReturnsFalse()
    {
        var input = "2024-01-15 10:30:00,123 INFO Test";

        Assert.False(LogParser.ValidatePresetAgainstInput(input, JsPreset));
    }

    // ───── Cancellation ─────

    [Fact]
    public void ParseLogEntries_AlreadyCancelledToken_Throws()
    {
        var input = string.Join("\n", Enumerable.Range(0, 10_000)
            .Select(i => $"2024-01-15 10:30:00,{(i % 1000):D3} INFO line {i}"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            LogParser.ParseLogEntries(input, DefaultPreset, cts.Token));
    }

    [Fact]
    public void Filter_AlreadyCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            LogParser.Filter("2024-01-15 10:30:00,000 INFO hi", "", DefaultPreset, ct: cts.Token));
    }

    // ───── TextReader overloads ─────

    [Fact]
    public void ParseLogEntries_FromTextReader_ParsesSameAsString()
    {
        var input = """
            2024-01-15 10:30:00,000 INFO one
            2024-01-15 10:30:01,000 INFO two
            """;

        using var reader = new StringReader(input);
        var entries = LogParser.ParseLogEntries(reader, DefaultPreset);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void DetectPreset_FromTextReader_DetectsCorrectly()
    {
        var input = """
            2024-01-15 10:30:00,000 INFO one
            2024-01-15 10:30:01,000 INFO two
            """;

        using var reader = new StringReader(input);
        var preset = LogParser.DetectPreset(reader);

        Assert.Same(DefaultPreset, preset);
    }

    // ───── FilterResult.Entries populado ─────

    [Fact]
    public void Filter_FilterResultExposesEntriesList()
    {
        var input = """
            2024-01-15 10:30:00,123 ERROR boom
            2024-01-15 10:30:01,000 INFO ok
            """;

        var result = LogParser.Filter(input, "ERROR", DefaultPreset);

        Assert.Single(result.Entries);
        Assert.Contains("boom", result.Entries[0].FullText);
    }
}
