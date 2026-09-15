using SmartX.Client.Services;
using SmartX.Shared.Contracts;
using SmartX.Shared.Telemetry;

namespace SmartX.Client;

public partial class SensorRegistrationPage : ContentPage
{
    private readonly SmartXApiClient _apiClient;

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
