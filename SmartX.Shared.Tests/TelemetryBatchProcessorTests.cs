using SmartX.Shared.Telemetry;

namespace SmartX.Shared.Tests;

public class TelemetryBatchProcessorTests
{
    private static readonly DateTime BatchStart = new(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private static TelemetryBatchProcessor<int> CreateProcessor() =>
        new("ESP32-MTR-07", SensorCategory.PowerConsumption, PollingInterval);

    [Fact]
    public void ToJaggedArray_PreservesCyclesOfDifferentLengths()
    {
        var processor = CreateProcessor();
        processor.AddBatch([120, 135, 128]);
        processor.AddBatch([140, 152, 149, 151, 147]);

        var jagged = processor.ToJaggedArray();

        Assert.Equal(2, jagged.Length);
        Assert.Equal(3, jagged[0].Length);
        Assert.Equal(5, jagged[1].Length);
        Assert.Equal([120, 135, 128], jagged[0]);
        Assert.Equal([140, 152, 149, 151, 147], jagged[1]);
    }

    [Fact]
    public void Flatten_ReturnsOnePacketPerReadingRatherThanPerBatch()
    {
        var processor = CreateProcessor();
        processor.AddBatch([120, 135, 128]);
        processor.AddBatch([140, 152, 149, 151, 147]);

        var packets = processor.Flatten(BatchStart);

        Assert.Equal(8, packets.Count);
        Assert.Equal([120, 135, 128, 140, 152, 149, 151, 147], packets.Select(packet => packet.Value));
    }

    [Fact]
    public void Flatten_StampsEveryPacketWithTheOwningSensorAndCategory()
    {
        var processor = CreateProcessor();
        processor.AddBatch([120, 135]);
        processor.AddBatch([140]);

        var packets = processor.Flatten(BatchStart);

        Assert.All(packets, packet =>
        {
            Assert.Equal("ESP32-MTR-07", packet.SensorId);
            Assert.Equal(SensorCategory.PowerConsumption, packet.Category);
        });
    }

    [Fact]
    public void Flatten_SharesOneTimestampWithinABatchAndOffsetsConsecutiveBatches()
    {
        var processor = CreateProcessor();
        processor.AddBatch([120, 135, 128]);
        processor.AddBatch([140, 152]);

        var packets = processor.Flatten(BatchStart);

        Assert.All(packets.Take(3), packet => Assert.Equal(BatchStart, packet.Timestamp));
        Assert.All(packets.Skip(3), packet => Assert.Equal(BatchStart + PollingInterval, packet.Timestamp));
    }

    [Fact]
    public void Flatten_ReturnsAnEmptyListWhenNoBatchesHaveBeenAdded()
    {
        var processor = CreateProcessor();

        var packets = processor.Flatten(BatchStart);

        Assert.Empty(packets);
    }

    [Fact]
    public void Flatten_AcceptsADroppedCycleAndKeepsLaterReadingsOnTheTimeAxis()
    {
        var processor = CreateProcessor();
        processor.AddBatch([120]);
        processor.AddBatch([]);
        processor.AddBatch([140]);

        var packets = processor.Flatten(BatchStart);

        Assert.Equal(2, packets.Count);
        Assert.Equal(BatchStart, packets[0].Timestamp);
        Assert.Equal(BatchStart + (PollingInterval * 2), packets[1].Timestamp);
    }

    [Fact]
    public void AddBatch_AcceptsAnEmptyPollingCycleAsValidData()
    {
        var processor = CreateProcessor();

        processor.AddBatch([]);

        var jagged = processor.ToJaggedArray();
        Assert.Single(jagged);
        Assert.Empty(jagged[0]);
    }

    [Fact]
    public void AddBatch_ThrowsWhenTheCycleIsNull()
    {
        var processor = CreateProcessor();

        Assert.Throws<ArgumentNullException>(() => processor.AddBatch(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsABatchWithNoRegisteredSensor(string? sensorId)
    {
        Assert.Throws<ArgumentException>(() =>
            new TelemetryBatchProcessor<int>(sensorId!, SensorCategory.PowerConsumption, PollingInterval));
    }
}
