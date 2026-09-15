using SmartX.Shared.Contracts;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services;

/// <summary>
/// Seeds the gateway with mock telemetry because Part 1 has no physical ESP32 mesh to poll.
/// Each tick walks the live register and publishes through the same
/// <see cref="SmartXStore.RecordEnvironmental"/> / <see cref="SmartXStore.RecordPower"/> /
/// <see cref="SmartXStore.RecordActuator"/> path the HTTP ingest endpoints use, so simulated
/// traffic updates last-seen timestamps, fills the generic packet lists, and hits the
/// threshold checker identically to a real reading. Occasional skipped cycles are what feed
/// the disconnect watcher; occasional out-of-band values are what give the dashboard something
/// to group.
/// </summary>
public class SensorTelemetrySimulator(
    SmartXStore store,
    AlertAggregationService alerts,
    ILogger<SensorTelemetrySimulator> logger) : BackgroundService
{
    // Same 5-second nominal poll the disconnect watcher treats as one missed cycle.
    private static readonly TimeSpan PollingInterval = SmartXStore.PollingInterval;

    private const double DroppedPollChance = 0.08;
    private const double ThresholdBreachChance = 0.10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Tick();
        }
    }

    private void Tick()
    {
        var published = 0;
        var skipped = 0;

        foreach (var sensor in store.Sensors)
        {
            try
            {
                if (Random.Shared.NextDouble() < DroppedPollChance)
                {
                    skipped++;
                    continue;
                }

                Publish(sensor);
                published++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Simulator failed to publish for {SensorId}; continuing with the rest of the cycle.", sensor.MacAddress);
            }
        }

        if (published + skipped > 0)
        {
            logger.LogInformation("Simulator cycle published {Published} reading(s) and skipped {Skipped}.", published, skipped);
        }
    }

    private void Publish(SensorRecord sensor)
    {
        switch (sensor.Category)
        {
            case SensorCategory.Environmental:
                var moisture = NextEnvironmental();
                store.RecordEnvironmental(sensor.MacAddress, moisture);
                alerts.CheckThreshold(sensor.MacAddress, sensor.DeploymentPath, sensor.Category, moisture);
                break;

            case SensorCategory.PowerConsumption:
                var watts = NextPower();
                store.RecordPower(sensor.MacAddress, watts);
                alerts.CheckThreshold(sensor.MacAddress, sensor.DeploymentPath, sensor.Category, watts);
                break;

            case SensorCategory.Actuator:
                store.RecordActuator(sensor.MacAddress, Random.Shared.Next(2) == 1);
                break;
        }
    }

    private static float NextEnvironmental()
    {
        if (Random.Shared.NextDouble() < ThresholdBreachChance)
        {
            return Random.Shared.Next(2) == 0
                ? Random.Shared.NextSingle() * 9f
                : 90.5f + Random.Shared.NextSingle() * 9.5f;
        }

        return 30f + Random.Shared.NextSingle() * 40f;
    }

    private static int NextPower()
    {
        if (Random.Shared.NextDouble() < ThresholdBreachChance)
        {
            return Random.Shared.Next(1_501, 2_501);
        }

        return Random.Shared.Next(200, 1_201);
    }
}
