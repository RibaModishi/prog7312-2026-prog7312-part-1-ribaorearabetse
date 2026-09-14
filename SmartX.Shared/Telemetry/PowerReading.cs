namespace SmartX.Shared.Telemetry;

/// <summary>
/// Wattage sample from a smart meter. Declared as a <see langword="struct"/> so it satisfies the
/// <c>where T : struct</c> constraint on <see cref="TelemetryPacket{T}"/> and can be aggregated
/// without leaving the value-type pipeline.
/// </summary>
public readonly struct PowerReading
{
    public PowerReading(double watts) => Watts = watts;

    public double Watts { get; }

    public static PowerReading operator +(PowerReading left, PowerReading right) =>
        new(left.Watts + right.Watts);

    public static PowerReading operator -(PowerReading left, PowerReading right) =>
        new(left.Watts - right.Watts);

    public static bool operator >(PowerReading left, PowerReading right) =>
        left.Watts > right.Watts;

    public static bool operator <(PowerReading left, PowerReading right) =>
        left.Watts < right.Watts;

    public static bool operator >=(PowerReading left, PowerReading right) =>
        left.Watts >= right.Watts;

    public static bool operator <=(PowerReading left, PowerReading right) =>
        left.Watts <= right.Watts;

    public override string ToString() => $"{Watts:0.##} W";
}
