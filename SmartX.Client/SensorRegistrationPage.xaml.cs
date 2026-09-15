using SmartX.Client.Services;
using SmartX.Shared.Contracts;
using SmartX.Shared.Telemetry;

namespace SmartX.Client;

public partial class SensorRegistrationPage : ContentPage
{
    private readonly SmartXApiClient _apiClient;

    // Set by a successful registration and used as the upload target, so an attachment can only
    // ever be sent against a sensor the gateway has already accepted.
    private string? _attachmentSensorId;
    private FileResult? _pendingFile;

    /// <summary>
    /// A deployment record is a photo one moment and a hardware log the next, so the filter stays
    /// broad on purpose. Narrowing it to a single family would block exactly the configuration
    /// files an engineer is most likely to attach.
    /// </summary>
    private static readonly FilePickerFileType AttachmentFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".txt", ".log", ".json", ".xml", ".csv", ".yaml", ".yml", ".ini", ".cfg", ".conf"],
            [DevicePlatform.Android] = ["image/*", "text/*", "application/json", "application/xml"],
            [DevicePlatform.iOS] = ["public.image", "public.text", "public.json", "public.xml"],
            [DevicePlatform.MacCatalyst] = ["public.image", "public.text", "public.json", "public.xml"]
        });

    private static readonly CategoryOption[] CategoryOptions =
    [
        new(SensorCategory.Environmental, "Environmental — soil, temperature, humidity"),
        new(SensorCategory.PowerConsumption, "Power Consumption — smart meters"),
        new(SensorCategory.Actuator, "Actuator — valves, switches, relays")
    ];

    public SensorRegistrationPage(SmartXApiClient apiClient)
    {
        InitializeComponent();

        _apiClient = apiClient;

        CategoryPicker.ItemsSource = CategoryOptions;
        CategoryPicker.SelectedIndex = 0;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("..");

    private async void OnDashboardTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("dashboard");

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        var macAddress = MacAddressEntry.Text?.Trim() ?? string.Empty;
        var facility = FacilityEntry.Text?.Trim() ?? string.Empty;
        var zone = ZoneEntry.Text?.Trim() ?? string.Empty;
        var subZone = SubZoneEntry.Text?.Trim();

        // Catching obviously incomplete input here keeps a pointless round trip off the wire; the
        // gateway still re-checks everything, so this is a convenience, not the real guard.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(macAddress)) missing.Add("A MAC address or unique identifier is required.");
        if (string.IsNullOrWhiteSpace(facility)) missing.Add("A facility name is required.");
        if (string.IsNullOrWhiteSpace(zone)) missing.Add("A zone name is required.");

        if (missing.Count > 0)
        {
            ShowFeedback("Check the form", missing, isSuccess: false);
            return;
        }

        if (CategoryPicker.SelectedItem is not CategoryOption category)
        {
            ShowFeedback("Check the form", ["Select a sensor category."], isSuccess: false);
            return;
        }

        SetBusy(true);

        try
        {
            var outcome = await _apiClient.RegisterSensorAsync(new SensorRegistrationRequest
            {
                MacAddress = macAddress,
                FacilityName = facility,
                ZoneName = zone,
                SubZoneName = string.IsNullOrWhiteSpace(subZone) ? null : subZone,
                Category = category.Value
            });

            if (outcome.IsSuccess)
            {
                var sensor = outcome.Sensor!;
                ShowFeedback(
                    $"{sensor.MacAddress} registered",
                    [$"Deployed at {sensor.DeploymentPath}", $"Category: {sensor.Category}"],
                    isSuccess: true);

                ClearForm();
                OpenAttachmentsFor(sensor.MacAddress);
            }
            else
            {
                ShowFeedback("Registration rejected", outcome.Violations, isSuccess: false);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnChooseFileClicked(object? sender, EventArgs e)
    {
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a file to attach",
                FileTypes = AttachmentFileTypes
            });

            // A null result means the picker was dismissed, which is not an error worth reporting.
            if (picked is null)
            {
                return;
            }

            // A zero-byte file is a real thing to pick by accident — a screenshot that failed to
            // save looks identical in the picker. The gateway rejects it anyway, so this only
            // moves the answer forward to the moment of choosing.
            if (await TryGetFileSizeAsync(picked) == 0)
            {
                _pendingFile = null;
                PendingFileRow.IsVisible = false;
                ShowFeedback("Nothing to upload", [$"'{picked.FileName}' is empty."], isSuccess: false);
                return;
            }

            _pendingFile = picked;
            PickedFileLabel.Text = picked.FileName;
            PendingFileRow.IsVisible = true;
            UploadButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException)
        {
            ShowFeedback("Cannot open the file picker", [ex.Message], isSuccess: false);
        }
    }

    private async void OnUploadClicked(object? sender, EventArgs e)
    {
        if (_attachmentSensorId is null || _pendingFile is null)
        {
            return;
        }

        SetUploadBusy(true);

        try
        {
            var outcome = await _apiClient.UploadAttachmentAsync(_attachmentSensorId, _pendingFile);

            if (outcome.IsSuccess)
            {
                ShowFeedback(
                    "Attachment uploaded",
                    [$"'{_pendingFile.FileName}' was stored against {_attachmentSensorId} as {outcome.FileName}."],
                    isSuccess: true);

                // Clearing the pending file stops the same upload being fired twice while leaving
                // the section open for the next one.
                _pendingFile = null;
                PickedFileLabel.Text = "Choose another file to attach more.";
            }
            else
            {
                ShowFeedback("Upload failed", [outcome.ErrorMessage!], isSuccess: false);
            }
        }
        finally
        {
            SetUploadBusy(false);
        }
    }

    /// <summary>
    /// Returns null when the size cannot be established rather than guessing, so a stream that
    /// simply cannot report its length is still allowed through to the gateway's own check.
    /// </summary>
    private static async Task<long?> TryGetFileSizeAsync(FileResult file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            return stream.CanSeek ? stream.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private void OpenAttachmentsFor(string sensorId)
    {
        _attachmentSensorId = sensorId;
        _pendingFile = null;

        AttachmentTargetLabel.Text = $"Attaching to {sensorId}";
        PendingFileRow.IsVisible = false;
        PickedFileLabel.Text = string.Empty;
        AttachmentCard.IsVisible = true;
    }

    private void SetUploadBusy(bool isBusy)
    {
        ChooseFileButton.IsEnabled = !isBusy;
        UploadButton.IsEnabled = !isBusy && _pendingFile is not null;
        UploadButton.Text = isBusy ? "Uploading…" : "Upload";
        UploadSpinner.IsVisible = isBusy;
        UploadSpinner.IsRunning = isBusy;
    }

    private void SetBusy(bool isBusy)
    {
        SubmitButton.IsEnabled = !isBusy;
        SubmitButton.Text = isBusy ? "Registering…" : "Register Sensor";
        SubmitSpinner.IsVisible = isBusy;
        SubmitSpinner.IsRunning = isBusy;
    }

    private void ShowFeedback(string heading, IEnumerable<string> details, bool isSuccess)
    {
        var accent = isSuccess
            ? (Color)Application.Current!.Resources["SmartXAccent"]
            : (Color)Application.Current!.Resources["SmartXDanger"];

        FeedbackHeading.Text = heading;
        FeedbackHeading.TextColor = accent;
        FeedbackCard.Stroke = accent;

        FeedbackDetails.Clear();
        foreach (var detail in details)
        {
            FeedbackDetails.Add(new Label
            {
                Text = $"• {detail}",
                FontSize = 14,
                TextColor = (Color)Application.Current!.Resources["SmartXTextSecondary"]
            });
        }

        FeedbackCard.IsVisible = true;
    }

    private void ClearForm()
    {
        MacAddressEntry.Text = string.Empty;
        FacilityEntry.Text = string.Empty;
        ZoneEntry.Text = string.Empty;
        SubZoneEntry.Text = string.Empty;
        CategoryPicker.SelectedIndex = 0;
    }

    private sealed record CategoryOption(SensorCategory Value, string Display);
}
