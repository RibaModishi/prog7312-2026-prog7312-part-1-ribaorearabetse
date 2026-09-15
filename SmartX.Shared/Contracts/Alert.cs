using SmartX.Shared.Telemetry;

namespace SmartX.Shared.Contracts;

/// <summary>
/// A single fault reported against a sensor. The deployment path is copied onto the alert at the
/// moment it is raised so the dashboard can still name the location after the live tree has
/// moved on, and so grouping can key off identity without walking the tree again.
/// </summary>
public class Alert
{
    public required string SensorId { get; init; }

    public required string DeploymentPath { get; init; }

    public required SensorCategory Category { get; init; }

    public required AlertType Type { get; init; }

    public required string Message { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
