namespace SmartX.Shared.Deployment;

/// <summary>
/// One level of a Smart-X deployment tree. Nodes nest to mirror the physical layout of an
/// installation, so a facility holds zones, a zone holds sub-zones, and a sub-zone holds the
/// sensors that publish telemetry from that location.
/// </summary>
public class DeploymentNode
{
    public string Name { get; init; } = string.Empty;

    public DeploymentNodeType NodeType { get; init; }

    public List<DeploymentNode> Children { get; init; } = [];
}
