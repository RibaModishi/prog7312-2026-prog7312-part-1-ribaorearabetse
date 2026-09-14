namespace SmartX.Shared.Telemetry;

/// <summary>
/// Accumulates raw polling cycles for a single sensor before they are promoted into optimised
/// collections. Each cycle is held as its own array because a real ESP32 poll can drop or delay
/// readings, so cycle lengths genuinely differ. A rectangular array would force every cycle to
/// the same width, either padding short cycles with readings that never happened or truncating
/// long ones, which is why the accumulated history is exposed as a jagged <c>T[][]</c>.
/// </summary>
public class TelemetryBatchProcessor<T> where T : struct
{
    private readonly List<T[]> _batches = [];
    private readonly string _sensorId;
    private readonly SensorCategory _category;
    private readonly TimeSpan _pollingInterval;

    public TelemetryBatchProcessor(string sensorId, SensorCategory category, TimeSpan pollingInterval)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
        {
            throw new ArgumentException("A telemetry batch must reference a registered sensor.", nameof(sensorId));
        }

        _sensorId = sensorId;
        _category = category;
        _pollingInterval = pollingInterval;
    }

    public void AddBatch(T[] readings)
    {
        ArgumentNullException.ThrowIfNull(readings);

        _batches.Add(readings);
    }

    public T[][] ToJaggedArray() => _batches.ToArray();

    public List<TelemetryPacket<T>> Flatten(DateTime batchStartTime)
    {
        var packets = new List<TelemetryPacket<T>>();

        for (var batchIndex = 0; batchIndex < _batches.Count; batchIndex++)
        {
            // The index drives the timestamp, so a cycle that captured nothing still advances the
            // clock and later readings stay on the correct point of the time axis.
            var batchTimestamp = batchStartTime + (_pollingInterval * batchIndex);

            foreach (var reading in _batches[batchIndex])
            {
                packets.Add(new TelemetryPacket<T>(_sensorId, _category, reading, batchTimestamp));
            }
        }

        return packets;
    }
}
