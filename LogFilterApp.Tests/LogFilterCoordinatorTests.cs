namespace LogFilterApp.Tests;

public class LogFilterCoordinatorTests
{
    private static readonly TimestampPreset DefaultPreset = LogParser.KnownPresets[0];

    [Fact]
    public async Task FilterAsync_ReturnsExpectedEntries()
    {
        var coordinator = new LogFilterCoordinator();
        var input = """
            2024-01-15 10:30:00,123 ERROR boom
            2024-01-15 10:30:01,000 INFO ok
            """;

        var result = await coordinator.FilterAsync(input, DefaultPreset, "ERROR", null, null, CancellationToken.None);

        Assert.Equal(2, result.TotalParsed);
        Assert.Equal(1, result.TotalFiltered);
        Assert.Single(result.Entries);
        Assert.Contains("boom", result.Entries[0].FullText);
    }

    [Fact]
    public async Task FilterAsync_ReusesParseCacheForSameInput()
    {
        var coordinator = new LogFilterCoordinator();
        var input = """
            2024-01-15 10:30:00,000 INFO one
            2024-01-15 10:30:01,000 INFO two
            """;

        var first = await coordinator.FilterAsync(input, DefaultPreset, "one", null, null, CancellationToken.None);
        var second = await coordinator.FilterAsync(input, DefaultPreset, "two", null, null, CancellationToken.None);

        // Ambos devem refletir o mesmo total de entries parsed.
        Assert.Equal(first.TotalParsed, second.TotalParsed);
        Assert.Equal(1, first.TotalFiltered);
        Assert.Equal(1, second.TotalFiltered);
    }

    [Fact]
    public async Task FilterAsync_InvalidatedCache_ReparsesNewInput()
    {
        var coordinator = new LogFilterCoordinator();

        var firstInput = "2024-01-15 10:30:00,000 INFO first";
        var firstResult = await coordinator.FilterAsync(firstInput, DefaultPreset, "", null, null, CancellationToken.None);
        Assert.Equal(1, firstResult.TotalParsed);

        coordinator.Invalidate();

        var secondInput = """
            2024-01-15 10:30:00,000 INFO a
            2024-01-15 10:30:01,000 INFO b
            """;
        var secondResult = await coordinator.FilterAsync(secondInput, DefaultPreset, "", null, null, CancellationToken.None);

        Assert.Equal(2, secondResult.TotalParsed);
    }

    [Fact]
    public async Task FilterAsync_AlreadyCancelled_Throws()
    {
        var coordinator = new LogFilterCoordinator();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.FilterAsync("2024-01-15 10:30:00,000 INFO hi", DefaultPreset, "", null, null, cts.Token));
    }

    [Fact]
    public async Task FilterAsync_CancelledMidFlight_DoesNotPoisonCache()
    {
        var coordinator = new LogFilterCoordinator();
        var input = string.Join("\n", Enumerable.Range(0, 50_000)
            .Select(i => $"2024-01-15 10:30:{(i % 60):D2},{(i % 1000):D3} INFO line {i}"));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(1));

        try
        {
            await coordinator.FilterAsync(input, DefaultPreset, "", null, null, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // esperado em algumas execuções; em outras a operação termina antes do cancel.
        }

        // Após a tentativa cancelada, uma nova chamada com input pequeno deve dar resultado correto,
        // ou seja, o cache não foi envenenado pela versão cancelada.
        var smallInput = "2024-01-15 10:30:00,000 INFO solo";
        var result = await coordinator.FilterAsync(smallInput, DefaultPreset, "", null, null, CancellationToken.None);

        Assert.Equal(1, result.TotalParsed);
        Assert.Equal(1, result.TotalFiltered);
    }
}
