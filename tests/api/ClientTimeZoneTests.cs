using VendemeFacil.Api.Infrastructure;

namespace VendemeFacil.Api.Tests;

public sealed class ClientTimeZoneTests
{
    [Fact]
    public void Today_UsesClientLocalDate()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-minus-six", TimeSpan.FromHours(-6), "Test", "Test");
        var utc = new DateTimeOffset(2026, 8, 21, 3, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 8, 20), ClientTimeZone.Today(zone, utc));
    }

    [Fact]
    public void StartOfDayUtc_ConvertsLocalMidnight()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-minus-six", TimeSpan.FromHours(-6), "Test", "Test");

        Assert.Equal(
            new DateTimeOffset(2026, 8, 21, 6, 0, 0, TimeSpan.Zero),
            ClientTimeZone.StartOfDayUtc(new DateOnly(2026, 8, 21), zone));
    }
}
