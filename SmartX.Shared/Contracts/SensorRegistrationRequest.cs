using SmartX.Shared.Telemetry;

namespace SmartX.Shared.Contracts;

/// <summary>
/// Registration payload for a new sensor. The sub-zone is optional because a sensor can sit
/// directly in a zone, which is why the deployment chain is built one level at a time rather
/// than assuming a fixed depth.
/// </summary>
public class SensorRegistrationRequest
{
    public string MacAddress { get; set; } = string.Empty;

    public string FacilityName { get; set; } = string.Empty;

    public string ZoneName { get; set; } = string.Empty;

    public string? SubZoneName { get; set; }

    public SensorCategory Category { get; set; }
}
