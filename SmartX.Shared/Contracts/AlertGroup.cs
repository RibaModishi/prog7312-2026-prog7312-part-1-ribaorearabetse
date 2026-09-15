namespace SmartX.Shared.Contracts;

/// <summary>
/// Alerts that share a sensor and fall inside one time window, folded together so a burst of
/// related faults is one dashboard row instead of a stack of identical cards. Severity is derived
/// from the contained types rather than stored, so adding a disconnect to an existing warning
/// group promotes it without a second write.
/// </summary>
public class AlertGroup
{
    public required string SensorId { get; init; }

    public required string DeploymentPath { get; init; }

    public DateTime WindowStart { get; init; }

    public DateTime WindowEnd { get; set; }

    public List<Alert> Alerts { get; init; } = [];

    public string Severity =>
        Alerts.Exists(alert => alert.Type == AlertType.Disconnect) ? "Critical" : "Warning";
}
