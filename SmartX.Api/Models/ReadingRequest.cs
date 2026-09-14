namespace SmartX.Api.Models;

/// <summary>
/// Single live reading pushed by a device. The payload is generic but is only ever closed over a
/// concrete type at each endpoint, because JSON has no way to express "any T" on the wire.
/// </summary>
public class ReadingRequest<T> where T : struct
{
    public string SensorId { get; set; } = string.Empty;

    public T Value { get; set; }
}
