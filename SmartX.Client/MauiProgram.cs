using Microsoft.Extensions.Logging;
using SmartX.Client.Services;

namespace SmartX.Client;

public static class MauiProgram
{
	// Match this to SmartX.Api's launchSettings.json HTTP port. Plain HTTP is used for local
	// development on purpose: the ASP.NET dev certificate is not in the Android emulator's trust
	// store, so HTTPS there fails the handshake before any Smart-X code runs.
	private const int ApiHttpPort = 5022;

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddHttpClient<SmartXApiClient>(client =>
		{
			client.BaseAddress = new Uri(ResolveApiBaseAddress());
		});

		// Registering the pages lets Shell resolve them through the container, which is what gives
		// SensorRegistrationPage its SmartXApiClient without a service locator.
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddTransient<SensorRegistrationPage>();
		builder.Services.AddTransient<AlertDashboardPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	private static string ResolveApiBaseAddress()
	{
#if ANDROID
		// An Android emulator is a separate virtual machine, so localhost resolves to the emulator
		// itself. 10.0.2.2 is the alias the emulator maps back to the host development machine.
		return $"http://10.0.2.2:{ApiHttpPort}/";
#else
		return $"http://localhost:{ApiHttpPort}/";
#endif
	}
}
