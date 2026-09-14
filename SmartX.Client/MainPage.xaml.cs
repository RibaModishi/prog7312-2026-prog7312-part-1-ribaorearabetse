namespace SmartX.Client;

public partial class MainPage : ContentPage
{
    private bool _isNavigating;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnIngestionTapped(object? sender, TappedEventArgs e)
    {
        // A card is a large target and a double tap fires twice, which would push the registration
        // screen onto the stack twice over.
        if (_isNavigating)
        {
            return;
        }

        _isNavigating = true;

        try
        {
            await Shell.Current.GoToAsync("registration");
        }
        finally
        {
            _isNavigating = false;
        }
    }
}
