using SmartX.Shared.Contracts;
using SmartX.Shared.Deployment;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services;

/// <summary>
/// In-memory backing store for the gateway. Part 1 has no persistence requirement, so the
/// deployment tree, the sensor register and the accumulated telemetry all live for the lifetime
/// of the process. Every caller goes through these methods rather than touching the collections
/// directly, so swapping in a real database later is an implementation change behind this class
/// rather than a rewrite of the controllers.
/// </summary>
/// <remarks>
/// The tree is rooted at a single synthetic "Smart-X" node whose children are the facilities.
/// A shared root means the validator can be run once over the entire estate instead of once per
/// facility, so a fault anywhere is reported in a single pass.
/// </remarks>
public class SmartXStore
{
    public static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private const string RootName = "Smart-X";

    private readonly object _gate = new();
    private readonly Dictionary<string, SensorRecord> _sensors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastSeenUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly CategoryChannel<float> _environmental = new();
    private readonly CategoryChannel<int> _power = new();
    private readonly CategoryChannel<bool> _actuator = new();

    private DeploymentNode _root = new() { Name = RootName, NodeType = DeploymentNodeType.Facility };

    public DeploymentNode DeploymentTree => _root;

    public IReadOnlyCollection<SensorRecord> Sensors
    {
        get
        {
            lock (_gate)
            {
                return _sensors.Values.ToList();
            }
        }
    }

    public bool TryGetSensor(string sensorId, out SensorRecord sensor)
    {
        lock (_gate)
        {
            return _sensors.TryGetValue(sensorId, out sensor!);
        }
    }

    /// <summary>
    /// Grafts a new sensor onto the deployment tree, but only if the resulting tree validates.
    /// The branch is grown on a clone first, so a rejected registration leaves the live tree
    /// exactly as it was rather than stranding half-created zones behind a failed request.
    /// </summary>
    public RegistrationResult RegisterSensor(SensorRegistrationRequest request)
    {
        var macAddress = request.MacAddress?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(macAddress))
        {
            return RegistrationResult.Rejected(["A sensor must be registered with a MAC address or unique identifier."]);
        }

        lock (_gate)
        {
            if (_sensors.ContainsKey(macAddress))
            {
                return RegistrationResult.Rejected([$"Sensor '{macAddress}' is already registered."]);
            }

            var candidateRoot = Clone(_root);
            var parent = FindOrCreateBranch(candidateRoot, request);
            parent.Children.Add(new DeploymentNode { Name = macAddress, NodeType = DeploymentNodeType.Sensor });

            var violations = DeploymentValidator.Validate(candidateRoot);
            if (violations.Count > 0)
            {
                return RegistrationResult.Rejected(violations);
            }

            _root = candidateRoot;

            var record = new SensorRecord
            {
                MacAddress = macAddress,
                Category = request.Category,
                DeploymentPath = BuildDeploymentPath(request)
            };

            _sensors[macAddress] = record;
            // Seeded at registration so a sensor that has not yet published is given a full
            // overdue window before the disconnect watcher treats the silence as a fault.
            _lastSeenUtc[macAddress] = DateTime.UtcNow;
            return RegistrationResult.Succeeded(record);
        }
    }

    public TelemetryPacket<float> RecordEnvironmental(string sensorId, float value) =>
        Record(_environmental, sensorId, SensorCategory.Environmental, value);

    public TelemetryPacket<int> RecordPower(string sensorId, int value) =>
        Record(_power, sensorId, SensorCategory.PowerConsumption, value);

    public TelemetryPacket<bool> RecordActuator(string sensorId, bool value) =>
        Record(_actuator, sensorId, SensorCategory.Actuator, value);

    public IReadOnlyList<TelemetryPacket<float>> RecentEnvironmental(string sensorId) =>
        Recent(_environmental, sensorId);

    public IReadOnlyList<TelemetryPacket<int>> RecentPower(string sensorId) =>
        Recent(_power, sensorId);

    public IReadOnlyList<TelemetryPacket<bool>> RecentActuator(string sensorId) =>
        Recent(_actuator, sensorId);

    public void AddEnvironmentalBatch(string sensorId, float[] readings) =>
        AddBatch(_environmental, sensorId, SensorCategory.Environmental, readings);

    public void AddPowerBatch(string sensorId, int[] readings) =>
        AddBatch(_power, sensorId, SensorCategory.PowerConsumption, readings);

    public void AddActuatorBatch(string sensorId, bool[] readings) =>
        AddBatch(_actuator, sensorId, SensorCategory.Actuator, readings);

    public List<TelemetryPacket<float>> EnvironmentalHistory(string sensorId) =>
        History(_environmental, sensorId);

    public List<TelemetryPacket<int>> PowerHistory(string sensorId) =>
        History(_power, sensorId);

    public List<TelemetryPacket<bool>> ActuatorHistory(string sensorId) =>
        History(_actuator, sensorId);

    private TelemetryPacket<T> Record<T>(CategoryChannel<T> channel, string sensorId, SensorCategory category, T value)
        where T : struct
    {
        var packet = new TelemetryPacket<T>(sensorId, category, value);

        lock (_gate)
        {
            if (!channel.Recent.TryGetValue(sensorId, out var readings))
            {
                readings = [];
                channel.Recent[sensorId] = readings;
            }

            readings.Add(packet);
            _lastSeenUtc[sensorId] = packet.Timestamp;
        }

        return packet;
    }

    /// <summary>
    /// Snapshot of every registered sensor together with when it last published. Taken under one
    /// lock so the disconnect watcher cannot observe a sensor whose last-seen entry has not been
    /// written yet, which would look like a node that never came online.
    /// </summary>
    public IReadOnlyList<(SensorRecord Sensor, DateTime LastSeenUtc)> SnapshotLastSeen()
    {
        lock (_gate)
        {
            return _sensors.Values
                .Select(sensor => (
                    sensor,
                    _lastSeenUtc.TryGetValue(sensor.MacAddress, out var seen) ? seen : sensor.RegisteredAtUtc))
                .ToList();
        }
    }

    private IReadOnlyList<TelemetryPacket<T>> Recent<T>(CategoryChannel<T> channel, string sensorId)
        where T : struct
    {
        lock (_gate)
        {
            return channel.Recent.TryGetValue(sensorId, out var readings) ? readings.ToList() : [];
        }
    }

    private void AddBatch<T>(CategoryChannel<T> channel, string sensorId, SensorCategory category, T[] readings)
        where T : struct
    {
        lock (_gate)
        {
            if (!channel.Batches.TryGetValue(sensorId, out var processor))
            {
                processor = new TelemetryBatchProcessor<T>(sensorId, category, PollingInterval);
                channel.Batches[sensorId] = processor;
            }

            processor.AddBatch(readings);
        }
    }

    private List<TelemetryPacket<T>> History<T>(CategoryChannel<T> channel, string sensorId)
        where T : struct
    {
        lock (_gate)
        {
            if (!channel.Batches.TryGetValue(sensorId, out var processor))
            {
                return [];
            }

            // Batches were captured one polling interval apart, so winding the clock back by the
            // number of cycles held puts the oldest reading at its real position on the timeline
            // and the newest at roughly now.
            var batchCount = processor.ToJaggedArray().Length;
            return processor.Flatten(DateTime.UtcNow - (PollingInterval * batchCount));
        }
    }

    private static DeploymentNode FindOrCreateBranch(DeploymentNode root, SensorRegistrationRequest request)
    {
        var facility = FindOrCreateChild(root, request.FacilityName, DeploymentNodeType.Facility);
        var zone = FindOrCreateChild(facility, request.ZoneName, DeploymentNodeType.Zone);

        return string.IsNullOrWhiteSpace(request.SubZoneName)
            ? zone
            : FindOrCreateChild(zone, request.SubZoneName, DeploymentNodeType.SubZone);
    }

    private static DeploymentNode FindOrCreateChild(DeploymentNode parent, string name, DeploymentNodeType nodeType)
    {
        var existing = parent.Children.FirstOrDefault(child =>
            child.NodeType == nodeType && string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return existing;
        }

        // A blank name is deliberately not rejected here. It is grown onto the candidate tree and
        // left for the validator to report, so every fault in a registration comes back through
        // the same channel with the same wording.
        var created = new DeploymentNode { Name = name?.Trim() ?? string.Empty, NodeType = nodeType };
        parent.Children.Add(created);
        return created;
    }

    private static DeploymentNode Clone(DeploymentNode node) => new()
    {
        Name = node.Name,
        NodeType = node.NodeType,
        Children = node.Children.Select(Clone).ToList()
    };

    private static string BuildDeploymentPath(SensorRegistrationRequest request)
    {
        var segments = new List<string> { RootName, request.FacilityName, request.ZoneName };

        if (!string.IsNullOrWhiteSpace(request.SubZoneName))
        {
            segments.Add(request.SubZoneName);
        }

        return string.Join(" > ", segments);
    }

    private sealed class CategoryChannel<T> where T : struct
    {
        public Dictionary<string, TelemetryBatchProcessor<T>> Batches { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<TelemetryPacket<T>>> Recent { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
