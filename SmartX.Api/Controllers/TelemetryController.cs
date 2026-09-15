using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Models;
using SmartX.Api.Services;
using SmartX.Shared.Contracts;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Controllers;

/// <summary>
/// Telemetry ingestion, split into one endpoint set per sensor category. JSON cannot express an
/// open type parameter, so a single endpoint could never accept "any T" from the wire. Resolving
/// the type by category instead means each endpoint closes <see cref="TelemetryPacket{T}"/> over a
/// concrete value type at compile time, keeping the whole path boxing-free while still exercising
/// the one generic wrapper across float, int and bool.
/// </summary>
[ApiController]
[Route("api/telemetry")]
public class TelemetryController(SmartXStore store, AlertAggregationService alerts) : ControllerBase
{
    [HttpPost("environmental")]
    public ActionResult<TelemetryPacket<float>> PostEnvironmental([FromBody] ReadingRequest<float> request)
    {
        var rejection = Reject(request.SensorId, SensorCategory.Environmental, out var sensor);
        if (rejection is not null)
        {
            return rejection;
        }

        var packet = store.RecordEnvironmental(request.SensorId, request.Value);
        alerts.CheckThreshold(sensor.MacAddress, sensor.DeploymentPath, sensor.Category, request.Value);
        return Ok(packet);
    }

    [HttpPost("power")]
    public ActionResult<TelemetryPacket<int>> PostPower([FromBody] ReadingRequest<int> request)
    {
        var rejection = Reject(request.SensorId, SensorCategory.PowerConsumption, out var sensor);
        if (rejection is not null)
        {
            return rejection;
        }

        var packet = store.RecordPower(request.SensorId, request.Value);
        alerts.CheckThreshold(sensor.MacAddress, sensor.DeploymentPath, sensor.Category, request.Value);
        return Ok(packet);
    }

    [HttpPost("actuator")]
    public ActionResult<TelemetryPacket<bool>> PostActuator([FromBody] ReadingRequest<bool> request)
    {
        var rejection = Reject(request.SensorId, SensorCategory.Actuator);
        return rejection ?? Ok(store.RecordActuator(request.SensorId, request.Value));
    }

    [HttpPost("environmental/{sensorId}/batches")]
    public ActionResult<object> PostEnvironmentalBatch(string sensorId, [FromBody] float[] readings)
    {
        var rejection = Reject(sensorId, SensorCategory.Environmental, readings);
        if (rejection is not null)
        {
            return rejection;
        }

        store.AddEnvironmentalBatch(sensorId, readings);
        return Ok(new { sensorId, readingsAccepted = readings.Length });
    }

    [HttpPost("power/{sensorId}/batches")]
    public ActionResult<object> PostPowerBatch(string sensorId, [FromBody] int[] readings)
    {
        var rejection = Reject(sensorId, SensorCategory.PowerConsumption, readings);
        if (rejection is not null)
        {
            return rejection;
        }

        store.AddPowerBatch(sensorId, readings);
        return Ok(new { sensorId, readingsAccepted = readings.Length });
    }

    [HttpPost("actuator/{sensorId}/batches")]
    public ActionResult<object> PostActuatorBatch(string sensorId, [FromBody] bool[] readings)
    {
        var rejection = Reject(sensorId, SensorCategory.Actuator, readings);
        if (rejection is not null)
        {
            return rejection;
        }

        store.AddActuatorBatch(sensorId, readings);
        return Ok(new { sensorId, readingsAccepted = readings.Length });
    }

    [HttpGet("environmental/{sensorId}/history")]
    public ActionResult<List<TelemetryPacket<float>>> GetEnvironmentalHistory(string sensorId)
    {
        var rejection = Reject(sensorId, SensorCategory.Environmental);
        return rejection ?? Ok(store.EnvironmentalHistory(sensorId));
    }

    [HttpGet("power/{sensorId}/history")]
    public ActionResult<List<TelemetryPacket<int>>> GetPowerHistory(string sensorId)
    {
        var rejection = Reject(sensorId, SensorCategory.PowerConsumption);
        return rejection ?? Ok(store.PowerHistory(sensorId));
    }

    [HttpGet("actuator/{sensorId}/history")]
    public ActionResult<List<TelemetryPacket<bool>>> GetActuatorHistory(string sensorId)
    {
        var rejection = Reject(sensorId, SensorCategory.Actuator);
        return rejection ?? Ok(store.ActuatorHistory(sensorId));
    }

    /// <summary>
    /// Confirms the reading belongs to a sensor that is both registered and registered under the
    /// category it is publishing to. A mismatch is a 400 rather than a 404 because the sensor
    /// exists; it is the routing of its data that is wrong.
    /// </summary>
    private ActionResult? Reject(string sensorId, SensorCategory expectedCategory) =>
        Reject(sensorId, expectedCategory, out _);

    private ActionResult? Reject(string sensorId, SensorCategory expectedCategory, out SensorRecord sensor)
    {
        sensor = null!;

        if (string.IsNullOrWhiteSpace(sensorId))
        {
            return BadRequest(new { error = "A telemetry reading must reference a registered sensor." });
        }

        if (!store.TryGetSensor(sensorId, out sensor))
        {
            return NotFound(new { error = $"Sensor '{sensorId}' is not registered." });
        }

        if (sensor.Category != expectedCategory)
        {
            return BadRequest(new
            {
                error = $"Sensor '{sensorId}' is registered as {sensor.Category} and cannot publish to the {expectedCategory} endpoint."
            });
        }

        return null;
    }

    private ActionResult? Reject<T>(string sensorId, SensorCategory expectedCategory, T[] readings) where T : struct
    {
        if (readings is null)
        {
            return BadRequest(new { error = "A batch must contain a readings array, even if it is empty." });
        }

        return Reject(sensorId, expectedCategory);
    }
}
