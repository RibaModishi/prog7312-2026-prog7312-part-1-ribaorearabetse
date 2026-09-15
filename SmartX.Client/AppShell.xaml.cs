namespace SmartX.Client;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Pages reached by GoToAsync rather than by a Shell tab or flyout item have to be routed
		// explicitly, otherwise the navigation throws at runtime instead of failing at build.
		Routing.RegisterRoute("registration", typeof(SensorRegistrationPage));
		Routing.RegisterRoute("dashboard", typeof(AlertDashboardPage));
	}
}
