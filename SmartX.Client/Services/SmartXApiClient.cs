using System.Net;
using System.Net.Http.Headers;
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
    /// Snapshot of the currently open alert groups. An empty list is a successful read — it means
    /// the mesh is quiet, not that the gateway failed — so the dashboard can show an empty state
    /// without treating silence as an error.
    /// </summary>
    public async Task<AlertDashboardOutcome> GetActiveAlertsAsync()
    {
        try
        {
            using var response = await httpClient.GetAsync("api/alerts");

            if (!response.IsSuccessStatusCode)
            {
                return AlertDashboardOutcome.Failed(
                    $"The gateway responded with {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var groups = await response.Content.ReadFromJsonAsync<List<AlertGroup>>(JsonOptions);

            return AlertDashboardOutcome.Succeeded(groups ?? []);
        }
        catch (HttpRequestException)
        {
            return AlertDashboardOutcome.Failed(
                $"Could not reach the Smart-X gateway at {httpClient.BaseAddress}. Check the API is running.");
        }
        catch (TaskCanceledException)
        {
            return AlertDashboardOutcome.Failed("The gateway did not respond in time.");
        }
        catch (JsonException)
        {
            return AlertDashboardOutcome.Failed("The gateway returned a response the client could not read.");
        }
    }

    /// <summary>
    /// Uploads a configuration file, deployment photo or hardware log against a registered sensor.
    /// The file is streamed rather than read into a byte array, so attaching a large hardware log
    /// does not cost the device a matching allocation.
    /// </summary>
    public async Task<AttachmentUploadOutcome> UploadAttachmentAsync(string sensorId, FileResult file)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            await using var stream = await file.OpenReadAsync();
            using var filePart = new StreamContent(stream);

            filePart.Headers.ContentType = ResolveContentType(file);

            // The part name has to be "file" because that is what the IFormFile parameter on
            // SensorsController.UploadAttachment is called. Any other name still uploads cleanly
            // and the server simply binds null, which is a genuinely confusing way to fail.
            content.Add(filePart, "file", file.FileName);

            using var response = await httpClient.PostAsync($"api/sensors/{Uri.EscapeDataString(sensorId)}/attachments", content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return AttachmentUploadOutcome.Failed(ExtractError(body, response.StatusCode));
            }

            var saved = JsonSerializer.Deserialize<AttachmentPayload>(body, JsonOptions);

            return string.IsNullOrWhiteSpace(saved?.SavedName)
                ? AttachmentUploadOutcome.Failed("The gateway stored the file but did not say under what name.")
                : AttachmentUploadOutcome.Succeeded(saved.SavedName);
        }
        catch (HttpRequestException)
        {
            return AttachmentUploadOutcome.Failed(
                $"Could not reach the Smart-X gateway at {httpClient.BaseAddress}. Check the API is running.");
        }
        catch (TaskCanceledException)
        {
            return AttachmentUploadOutcome.Failed("The upload did not complete in time.");
        }
        catch (JsonException)
        {
            return AttachmentUploadOutcome.Failed("The gateway returned a response the client could not read.");
        }
        catch (IOException)
        {
            return AttachmentUploadOutcome.Failed($"'{file.FileName}' could not be read from this device.");
        }
        catch (UnauthorizedAccessException)
        {
            return AttachmentUploadOutcome.Failed($"This app is not allowed to read '{file.FileName}'.");
        }
    }

    /// <summary>
    /// A picked file does not always carry a usable content type, and a malformed one would throw
    /// while building the request rather than being reported as a failed upload.
    /// </summary>
    private static MediaTypeHeaderValue ResolveContentType(FileResult file) =>
        MediaTypeHeaderValue.TryParse(file.ContentType, out var contentType)
            ? contentType
            : new MediaTypeHeaderValue("application/octet-stream");

    private static string ExtractError(string body, HttpStatusCode statusCode)
    {
        var messages = ExtractMessages(body);

        return messages.Count > 0
            ? string.Join(" ", messages)
            : $"The gateway rejected the upload with {(int)statusCode} {statusCode}.";
    }

    private static IReadOnlyList<string> ExtractViolations(string body)
    {
        var messages = ExtractMessages(body);

        return messages.Count > 0
            ? messages
            : ["The gateway rejected the registration without explaining why."];
    }

    /// <summary>
    /// A refusal arrives in one of several shapes. The validator answers with
    /// <c>{ violations: [...] }</c> and the attachment endpoint with <c>{ error: "..." }</c>, but a
    /// request that fails model binding never reaches either and is answered with ASP.NET's own
    /// ProblemDetails. Reporting nothing in that last case would hide a real bug — a mismatched
    /// multipart part name shows up there and nowhere else — so every shape is unpacked.
    /// </summary>
    private static IReadOnlyList<string> ExtractMessages(string body)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<RejectionPayload>(body, JsonOptions);

            if (payload?.Violations is { Count: > 0 } violations)
            {
                return violations;
            }

            if (!string.IsNullOrWhiteSpace(payload?.Error))
            {
                return [payload.Error];
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
            // Falls through to the caller's own fallback message.
        }

        return [];
    }

    private sealed class RejectionPayload
    {
        public List<string>? Violations { get; set; }

        public string? Title { get; set; }

        public Dictionary<string, List<string>>? Errors { get; set; }

        public string? Error { get; set; }
    }

    private sealed class AttachmentPayload
    {
        public string? SavedName { get; set; }
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

/// <summary>
/// Result of an attachment upload. A single message is enough here: unlike a registration, an
/// upload fails for one reason at a time rather than accumulating structural faults.
/// </summary>
public class AttachmentUploadOutcome
{
    private AttachmentUploadOutcome(string? fileName, string? errorMessage)
    {
        FileName = fileName;
        ErrorMessage = errorMessage;
    }

    public string? FileName { get; }

    public string? ErrorMessage { get; }

    public bool IsSuccess => FileName is not null;

    public static AttachmentUploadOutcome Succeeded(string fileName) => new(fileName, null);

    public static AttachmentUploadOutcome Failed(string errorMessage) => new(null, errorMessage);
}

/// <summary>
/// Result of a dashboard poll. Groups are kept as a list even when empty so the UI can tell a
/// quiet mesh apart from a failed fetch without a second null check.
/// </summary>
public class AlertDashboardOutcome
{
    private AlertDashboardOutcome(IReadOnlyList<AlertGroup>? groups, string? errorMessage)
    {
        Groups = groups ?? [];
        ErrorMessage = errorMessage;
    }

    public IReadOnlyList<AlertGroup> Groups { get; }

    public string? ErrorMessage { get; }

    public bool IsSuccess => ErrorMessage is null;

    public static AlertDashboardOutcome Succeeded(IReadOnlyList<AlertGroup> groups) => new(groups, null);

    public static AlertDashboardOutcome Failed(string errorMessage) => new(null, errorMessage);
}
