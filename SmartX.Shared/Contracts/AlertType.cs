namespace SmartX.Shared.Contracts;

/// <summary>
/// Why a sensor was raised to the dashboard. Threshold breaches come from a reading that crossed
/// a numeric band; disconnects come from silence that lasted longer than the mesh can explain as
/// a delayed poll. Splitting them this way lets a group of mixed alerts still be ranked by the
/// more serious of the two rather than by count.
/// </summary>
public enum AlertType
{
    ThresholdBreach,
    Disconnect
}
