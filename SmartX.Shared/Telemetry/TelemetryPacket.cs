namespace SmartX.Shared.Telemetry;

/// <summary>
/// Uniform wrapper for a single sensor reading. <typeparamref name="T"/> is constrained to
/// <see langword="struct"/> so the reading is stored in a value-typed field rather than an
/// <see cref="object"/>, which keeps float, int and bool payloads free of boxing allocations.
/// </summary>
public class TelemetryPacket<T> where T : struct
{
    public TelemetryPacket(string sensorId, SensorCategory category, T value, DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
        {
            throw new ArgumentException("A telemetry packet must reference a registered sensor.", nameof(sensorId));
        }

        SensorId = sensorId;
        Category = category;
        Value = value;
        Timestamp = timestamp ?? DateTime.UtcNow;
    }

    public string SensorId { get; }

    public SensorCategory Category { get; }

    public T Value { get; }

    public DateTime Timestamp { get; }

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss.fff}] {SensorId} ({Category}) = {Value}";
}
