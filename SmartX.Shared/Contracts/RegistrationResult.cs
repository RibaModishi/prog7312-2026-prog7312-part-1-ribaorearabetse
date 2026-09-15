namespace SmartX.Shared.Contracts;

/// <summary>
/// Outcome of a registration attempt. Violations are returned rather than thrown so the caller
/// can surface every problem with a deployment at once, matching how the recursive validator
/// accumulates faults instead of stopping at the first.
/// </summary>
public class RegistrationResult
{
    private RegistrationResult(SensorRecord? sensor, List<string> violations)
    {
        Sensor = sensor;
        Violations = violations;
    }

    public SensorRecord? Sensor { get; }

    public List<string> Violations { get; }

    public bool IsSuccess => Sensor is not null;

    public static RegistrationResult Succeeded(SensorRecord sensor) => new(sensor, []);

    public static RegistrationResult Rejected(List<string> violations) => new(null, violations);
}
