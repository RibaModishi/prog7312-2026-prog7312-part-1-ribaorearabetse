using SmartX.Shared.Telemetry;

namespace SmartX.Shared.Contracts;

/// <summary>
/// A sensor that has passed registration. The deployment path is stored as a flattened string
/// rather than a node reference so the record stays a simple serialisable snapshot, while the
/// live tree remains the single source of truth for structure.
/// </summary>
public class SensorRecord
{
    public required string MacAddress { get; init; }

    public required SensorCategory Category { get; init; }

    public required string DeploymentPath { get; init; }

    public DateTime RegisteredAtUtc { get; init; } = DateTime.UtcNow;
}
