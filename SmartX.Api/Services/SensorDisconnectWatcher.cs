using SmartX.Shared.Contracts;

namespace SmartX.Api.Services;

/// <summary>
/// Watches for sensors that have gone quiet. A delayed ESP32 poll is expected; silence that lasts
/// several polling intervals is not, and is raised as a disconnect rather than guessed at from a
/// stale reading. Each overdue sensor is recorded once per episode — the next sweep must not mint
/// a fresh alert just because the node is still silent — and the episode ends the moment a
/// reading arrives again.
/// </summary>
public class SensorDisconnectWatcher(SmartXStore store, AlertAggregationService alerts) : BackgroundService
{
    /// <summary>
    /// Tunable. Frequent enough to notice a drop within a couple of poll cycles, sparse enough
    /// that the sweep itself is not a load on the gateway.
    /// </summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Tunable. Four times the nominal 5-second poll, so one or two dropped packets are tolerated
    /// and a node that has actually left the mesh is not.
    /// </summary>
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromSeconds(20);

    private readonly HashSet<string> _outstandingDisconnects = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Sweep(DateTime.UtcNow);
        }
    }

    internal void Sweep(DateTime utcNow)
    {
        foreach (var (sensor, lastSeenUtc) in store.SnapshotLastSeen())
        {
            var overdue = utcNow - lastSeenUtc > OverdueAfter;

            if (!overdue)
            {
                // A fresh reading ends the episode. The next silence is a new disconnect, not a
                // continuation of this one, which is why the sensor is removed from the set
                // rather than left marked.
                _outstandingDisconnects.Remove(sensor.MacAddress);
                continue;
            }

            if (!_outstandingDisconnects.Add(sensor.MacAddress))
            {
                continue;
            }

            var silentFor = utcNow - lastSeenUtc;
            alerts.RecordAlert(new Alert
            {
                SensorId = sensor.MacAddress,
                DeploymentPath = sensor.DeploymentPath,
                Category = sensor.Category,
                Type = AlertType.Disconnect,
                Message = $"Sensor '{sensor.MacAddress}' has not published for {silentFor.TotalSeconds:0}s (last seen {lastSeenUtc:HH:mm:ss} UTC).",
                Timestamp = utcNow
            });
        }
    }
}
