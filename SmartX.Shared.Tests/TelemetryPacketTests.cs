using SmartX.Shared.Telemetry;

namespace SmartX.Shared.Tests;

public class TelemetryPacketTests
{
    [Fact]
    public void Wrapper_CarriesAFloatSoilMoistureReading()
    {
        var packet = new TelemetryPacket<float>("ESP32-SOIL-01", SensorCategory.Environmental, 42.8f);

        Assert.Equal("ESP32-SOIL-01", packet.SensorId);
        Assert.Equal(SensorCategory.Environmental, packet.Category);
        Assert.Equal(42.8f, packet.Value);
    }

    [Fact]
    public void Wrapper_CarriesAnIntegerWattageReading()
    {
        var packet = new TelemetryPacket<int>("ESP32-MTR-07", SensorCategory.PowerConsumption, 1_450);

        Assert.Equal(1_450, packet.Value);
    }

    [Fact]
    public void Wrapper_CarriesABooleanValveState()
    {
        var packet = new TelemetryPacket<bool>("ESP32-VLV-03", SensorCategory.Actuator, true);

        Assert.True(packet.Value);
    }

    [Fact]
    public void Wrapper_AcceptsACustomStructAsItsPayload()
    {
        var meter1 = new TelemetryPacket<PowerReading>(
            "ESP32-MTR-01", SensorCategory.PowerConsumption, new PowerReading(240.5));
        var meter2 = new TelemetryPacket<PowerReading>(
            "ESP32-MTR-02", SensorCategory.PowerConsumption, new PowerReading(112.25));

        var aggregate = meter1.Value + meter2.Value;

        Assert.Equal(352.75, aggregate.Watts, 3);
    }

    [Fact]
    public void Timestamp_DefaultsToCaptureTimeWhenNotSupplied()
    {
        var before = DateTime.UtcNow;

        var packet = new TelemetryPacket<int>("ESP32-MTR-07", SensorCategory.PowerConsumption, 900);

        Assert.InRange(packet.Timestamp, before, DateTime.UtcNow);
    }

    [Fact]
    public void Timestamp_IsPreservedWhenReplayingHistoricalTelemetry()
    {
        var captured = new DateTime(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc);

        var packet = new TelemetryPacket<float>(
            "ESP32-SOIL-01", SensorCategory.Environmental, 31.2f, captured);

        Assert.Equal(captured, packet.Timestamp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsAPacketWithNoRegisteredSensor(string? sensorId)
    {
        Assert.Throws<ArgumentException>(() =>
            new TelemetryPacket<float>(sensorId!, SensorCategory.Environmental, 12.5f));
    }
}
