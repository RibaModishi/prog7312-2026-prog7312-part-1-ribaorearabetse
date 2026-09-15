using SmartX.Client.Services;

namespace SmartX.Client;

public partial class AlertDashboardPage : ContentPage
{
    private readonly SmartXApiClient _apiClient;

    // Dispatcher timer, not System.Threading.Timer: Tick already runs on the UI thread, so the
    // CollectionView update does not need a second marshal. Stopped and unsubscribed in
    // OnDisappearing so navigating away cannot leave a background poll alive.
    private IDispatcherTimer? _pollTimer;
    private CancellationTokenSource? _lifetime;
    private bool _isRefreshing;

    public AlertDashboardPage(SmartXApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _lifetime = new CancellationTokenSource();
        StartPolling();
        _ = RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        // Stop first so a Tick already queued cannot start another refresh after cancellation.
        StopPolling();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;

        base.OnDisappearing();
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("..");

    private async void OnRefreshClicked(object? sender, EventArgs e) =>
        await RefreshAsync();

    private async void OnPollTick(object? sender, EventArgs e) =>
        await RefreshAsync();

    private void StartPolling()
    {
        // Idempotent: if OnAppearing fires twice without a matching disappear (platform quirks),
        // the previous timer is torn down before a new one is created.
        StopPolling();

        _pollTimer = Dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(5);
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        if (_pollTimer is null)
        {
            return;
        }

        _pollTimer.Stop();
        _pollTimer.Tick -= OnPollTick;
        _pollTimer = null;
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        var token = _lifetime?.Token ?? CancellationToken.None;
        _isRefreshing = true;
        SetBusy(true);

        try
        {
            var outcome = await _apiClient.GetActiveAlertsAsync();

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!outcome.IsSuccess)
            {
                StatusLabel.TextColor = (Color)Application.Current!.Resources["SmartXDanger"];
                StatusLabel.Text = outcome.ErrorMessage;
                return;
            }

            AlertList.ItemsSource = outcome.Groups.ToList();
            StatusLabel.TextColor = (Color)Application.Current!.Resources["SmartXTextSecondary"];
            StatusLabel.Text = outcome.Groups.Count == 0
                ? $"Updated {DateTime.Now:HH:mm:ss} — no open groups."
                : $"Updated {DateTime.Now:HH:mm:ss} — {outcome.Groups.Count} open group(s).";
        }
        finally
        {
            _isRefreshing = false;
            if (!token.IsCancellationRequested)
            {
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool isBusy)
    {
        RefreshButton.IsEnabled = !isBusy;
        RefreshSpinner.IsVisible = isBusy;
        RefreshSpinner.IsRunning = isBusy;
    }
}
