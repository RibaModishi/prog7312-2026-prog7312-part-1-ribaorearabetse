namespace SmartX.Shared.Deployment;

/// <summary>
/// Recursively checks a deployment tree for structural faults. Every violation found anywhere in
/// the subtree is collected rather than stopping at the first one, so a single pass tells an
/// engineer everything that is wrong with a configuration profile.
/// </summary>
public static class DeploymentValidator
{
    public static List<string> Validate(DeploymentNode node, string parentPath = "")
    {
        var violations = new List<string>();
        var isNamed = !string.IsNullOrWhiteSpace(node.Name);
        var path = BuildPath(parentPath, isNamed ? node.Name : UnnamedLabel);

        if (!isNamed)
        {
            violations.Add(string.IsNullOrEmpty(parentPath)
                ? "The root node has a missing or blank name."
                : $"'{parentPath}' contains a node with a missing or blank name.");
        }

        if (node.NodeType == DeploymentNodeType.Sensor && node.Children.Count > 0)
        {
            violations.Add($"Sensor '{path}' must not contain child nodes.");
        }

        // Grouping only the immediate children keeps this check to one level, so nodes that share
        // a name under different parents elsewhere in the tree are left alone. Casing is ignored
        // because two sibling zones differing only in case would be indistinguishable on a
        // dashboard and almost certainly a configuration mistake.
        var duplicateSiblings = node.Children
            .Where(child => !string.IsNullOrWhiteSpace(child.Name))
            .GroupBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .Where(siblings => siblings.Count() > 1);

        foreach (var siblings in duplicateSiblings)
        {
            violations.Add($"'{path}' contains {siblings.Count()} sibling nodes named '{siblings.Key}'.");
        }

        foreach (var child in node.Children)
        {
            violations.AddRange(Validate(child, path));
        }

        return violations;
    }

    private const string UnnamedLabel = "(unnamed)";

    private static string BuildPath(string parentPath, string name) =>
        string.IsNullOrEmpty(parentPath) ? name : $"{parentPath} > {name}";
}
