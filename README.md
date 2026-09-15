# prog7312-2026-prog7312-part-1-ribaorearabetse

# Smart-X — IoT Mesh Data Ingestion Gateway

Smart-X is a simulated hybrid IoT mesh ecosystem for monitoring distributed environments (hydroponic farms, utility trackers, smart grid installations). This repository contains **Part 1** of the Portfolio of Evidence: the sensor data ingestion and validation gateway, covering the API backend, the MAUI client dashboard, and the shared domain logic between them.

## Architecture

The solution is split into three projects:

| Project | Purpose |
|---|---|
| `SmartX.Api` | ASP.NET Core Web API (.NET 10) — the ingestion gateway |
| `SmartX.Client` | .NET MAUI application — the configuration and monitoring dashboard |
| `SmartX.Shared` | Class library — domain models, telemetry structures, and API contracts shared by both the API and the client |
| `SmartX.Shared.Tests` | xUnit test suite for the shared domain logic |

`SmartX.Client` was built as a cross-platform desktop/mobile application communicating with a self-hosted local Web API, rather than a browser-based frontend — chosen to stay within a single language (C#) across the whole stack.

## Key technical features

- **`TelemetryPacket<T>`** — a generic wrapper (`where T : struct`) that uniformly handles float, int, and bool sensor readings without boxing.
- **`PowerReading`** — a struct with overloaded `+`, `-`, `>`, and `<` operators, supporting direct aggregation and delta comparisons of sensor values (e.g. `meter3 = meter1 + meter2`).
- **`DeploymentValidator`** — a recursive algorithm validating nested device deployment trees (Facility → Zone → Sub-Zone → Sensor), checking for blank names, duplicate sibling names, and structurally invalid sensor nodes, and reporting every violation found rather than stopping at the first.
- **`TelemetryBatchProcessor<T>`** — accumulates historical telemetry as a jagged array (`T[][]`), reflecting that real sensor polling cycles can vary in length, and flattens that raw data into optimised `List<TelemetryPacket<T>>` collections.
- **Alert aggregation with contextual anomaly visualisation** — the dashboard's dynamic engagement feature. Rather than showing every threshold breach or sensor disconnect as a separate, disconnected notification, `AlertAggregationService` groups alerts by sensor and time-window proximity into single, contextualised incidents — directly implementing the strategy researched and justified in the Part 1 research report.
- **Mock telemetry simulation** — since no physical ESP32 hardware is used, `SensorTelemetrySimulator` (a background service) publishes realistic mock readings through the same ingestion path real hardware would use, including occasional threshold breaches and dropped polling cycles, to exercise the system under realistic conditions.

## Getting started

### Prerequisites
- .NET 10 SDK
- Visual Studio 2022 (17.x or later) with the .NET MAUI workload installed, **or** the MAUI workload via `dotnet workload install maui`

### Clone and restore
```bash
git clone TODO-your-repo-url
cd prog7312-2026-prog7312-part-1-ribaorearabetse
dotnet restore
```

### Run the API
```bash
dotnet run --project SmartX.Api --urls http://localhost:5022
```
The API starts on `http://localhost:5022`. Swagger UI is available in development mode at `http://localhost:5022/swagger` for testing endpoints directly.

> The API runs on plain HTTP for local development on purpose — the ASP.NET Core HTTPS development certificate is not trusted by the Android emulator, so HTTPS is skipped locally to avoid that certificate-trust failure.

### Run the client
With the API already running, launch `SmartX.Client` from Visual Studio (select your target: Windows Machine, or an Android emulator) and press Run — or from the command line:
```bash
dotnet build SmartX.Client -t:Run -f net10.0-windows10.0.19041.0
```
(Substitute the target framework moniker for Android/iOS/Mac Catalyst as needed.)

The client's API base address is configured in `MauiProgram.cs` (`ApiHttpPort` constant, currently `5022`). If you change the API's port, update this constant to match.

**Note for Android emulator testing:** the emulator cannot reach `localhost` directly (it resolves to the emulator itself, not your development machine) — the client automatically switches to `10.0.2.2` when running on Android, so no manual configuration is needed there.

### Run the tests
```bash
dotnet test SmartX.Shared.Tests
```

## Using the application

1. Launch the app — you'll see the Smart-X Gateway landing page with three pillars. Only **Sensor Data Ingestion and Telemetry** is active for Part 1.
2. Tap through to register a sensor: provide a MAC address/unique identifier, its deployment location (Facility, Zone, and optionally Sub-Zone), and its category (Environmental, Power Consumption, or Actuator).
3. Once registered, optionally attach a configuration file, deployment photo, or hardware log.
4. Navigate to the **Live Dashboard** to see grouped, contextualised alerts as the mock telemetry simulator generates readings — including threshold breaches and simulated sensor disconnects.

## Project status

Part 1 (Research + Implementation) is complete. Real-Time Command Stream and History, and Network Topology and Mesh Routing, are disabled pending Part 2 and the final Portfolio of Evidence respectively.
