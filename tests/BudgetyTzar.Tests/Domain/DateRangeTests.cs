using BudgetyTzar.Api;

namespace BudgetyTzar.Tests;

public sealed class DateRangeTests
{
    [Fact]
    public void DateRangeDetectsOverlap()
    {
        var june = new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var lateJune = new DateRange(new DateOnly(2026, 6, 20), new DateOnly(2026, 7, 19));
        var july = new DateRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        Assert.True(june.Overlaps(lateJune));
        Assert.False(june.Overlaps(july));
    }
}
