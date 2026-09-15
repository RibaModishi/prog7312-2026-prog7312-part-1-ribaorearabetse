using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartX.Shared.Contracts;

namespace SmartX.Client.Services;

/// <summary>
/// Talks to the Smart-X gateway. Every failure the user can plausibly hit — an unreachable API, a
/// rejected registration, a malformed response — comes back as an outcome rather than an
/// exception, so the UI has one code path to render instead of a try/catch around every call.
/// </summary>
public class SmartXApiClient(HttpClient httpClient)
{
    // The gateway serialises enums by name, so the client has to read and write them the same way
    // or SensorCategory round-trips as a number the API cannot match.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<SensorRegistrationOutcome> RegisterSensorAsync(SensorRegistrationRequest request)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/sensors", request, JsonOptions);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var sensor = await response.Content.ReadFromJsonAsync<SensorRecord>(JsonOptions);

                return sensor is null
                    ? SensorRegistrationOutcome.Failed(["The gateway accepted the sensor but returned no record."])
                    : SensorRegistrationOutcome.Succeeded(sensor);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var body = await response.Content.ReadAsStringAsync();
                return SensorRegistrationOutcome.Failed(ExtractViolations(body));
            }

            return SensorRegistrationOutcome.Failed(
                [$"The gateway responded with {(int)response.StatusCode} {response.ReasonPhrase}."]);
        }
        catch (HttpRequestException)
        {
            return SensorRegistrationOutcome.Failed(
                [$"Could not reach the Smart-X gateway at {httpClient.BaseAddress}. Check the API is running."]);
        }
        catch (TaskCanceledException)
        {
            return SensorRegistrationOutcome.Failed(["The gateway did not respond in time."]);
        }
        catch (JsonException)
        {
            return SensorRegistrationOutcome.Failed(["The gateway returned a response the client could not read."]);
        }
    }

    /// <summary>
    /// A rejected registration can come back in two shapes. The validator's own
    /// <c>{ violations: [...] }</c> is the normal one, but a request that fails model binding never
    /// reaches the validator and is answered with ASP.NET's ProblemDetails instead. Reporting
    /// nothing in that case would hide a real bug, so the problem's own wording is surfaced.
    /// </summary>
    private static IReadOnlyList<string> ExtractViolations(string body)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<RejectionPayload>(body, JsonOptions);

            if (payload?.Violations is { Count: > 0 } violations)
            {
                return violations;
            }

            if (payload?.Errors is { Count: > 0 } errors)
            {
                return errors.SelectMany(field => field.Value).ToList();
            }

            if (!string.IsNullOrWhiteSpace(payload?.Title))
            {
                return [payload.Title];
            }
        }
        catch (JsonException)
        {
            // Falls through to the generic message below.
        }

        return ["The gateway rejected the registration without explaining why."];
    }

    private sealed class RejectionPayload
    {
        public List<string>? Violations { get; set; }

        public string? Title { get; set; }

        public Dictionary<string, List<string>>? Errors { get; set; }
    }
}

/// <summary>
/// Result of a registration attempt as the UI sees it. Violations are a list because the recursive
/// validator reports every fault in a deployment at once, and collapsing them to a single message
/// would throw away most of what it found.
/// </summary>
public class SensorRegistrationOutcome
{
    private SensorRegistrationOutcome(SensorRecord? sensor, IReadOnlyList<string> violations)
    {
        Sensor = sensor;
        Violations = violations;
    }

    public SensorRecord? Sensor { get; }

    public IReadOnlyList<string> Violations { get; }

    public bool IsSuccess => Sensor is not null;

    public static SensorRegistrationOutcome Succeeded(SensorRecord sensor) => new(sensor, []);

    public static SensorRegistrationOutcome Failed(IReadOnlyList<string> violations) => new(null, violations);
}
