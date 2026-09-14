using SmartX.Shared.Telemetry;

namespace SmartX.Shared.Tests;

public class PowerReadingTests
{
    [Fact]
    public void Addition_AggregatesTheLoadOfTwoMeters()
    {
        var meter1 = new PowerReading(240.5);
        var meter2 = new PowerReading(112.25);

        var meter3 = meter1 + meter2;

        Assert.Equal(352.75, meter3.Watts, 3);
    }

    [Fact]
    public void Subtraction_ProducesTheDeltaBetweenTwoReadings()
    {
        var current = new PowerReading(400);
        var previous = new PowerReading(325);

        var delta = current - previous;

        Assert.Equal(75, delta.Watts, 3);
    }

    [Fact]
    public void Subtraction_ReportsNegativeDeltaWhenConsumptionDrops()
    {
        var current = new PowerReading(150);
        var previous = new PowerReading(210);

        var delta = current - previous;

        Assert.Equal(-60, delta.Watts, 3);
    }

    [Fact]
    public void GreaterThan_DetectsAReadingAboveAnAlertThreshold()
    {
        var reading = new PowerReading(1_450);
        var threshold = new PowerReading(1_000);

        Assert.True(reading > threshold);
        Assert.False(reading < threshold);
    }

    [Fact]
    public void LessThan_DetectsAReadingBelowAnAlertThreshold()
    {
        var reading = new PowerReading(320);
        var threshold = new PowerReading(1_000);

        Assert.True(reading < threshold);
        Assert.False(reading > threshold);
    }

    [Fact]
    public void Comparisons_AreFalseInBothDirectionsForEqualReadings()
    {
        var left = new PowerReading(500);
        var right = new PowerReading(500);

        Assert.False(left > right);
        Assert.False(left < right);
    }

    [Fact]
    public void GreaterThanOrEqual_TriggersOnAReadingSittingExactlyOnTheThreshold()
    {
        var reading = new PowerReading(1_000);
        var threshold = new PowerReading(1_000);

        Assert.True(reading >= threshold);
        Assert.True(reading <= threshold);
    }

    [Fact]
    public void InclusiveComparisons_StillRespectReadingsEitherSideOfTheThreshold()
    {
        var threshold = new PowerReading(1_000);

        Assert.True(new PowerReading(1_001) >= threshold);
        Assert.False(new PowerReading(999) >= threshold);
        Assert.True(new PowerReading(999) <= threshold);
        Assert.False(new PowerReading(1_001) <= threshold);
    }
}
