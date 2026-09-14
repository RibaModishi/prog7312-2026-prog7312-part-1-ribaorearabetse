using SmartX.Shared.Deployment;

namespace SmartX.Shared.Tests;

public class DeploymentValidatorTests
{
    [Fact]
    public void Validate_ReturnsNoViolationsForAWellFormedTree()
    {
        var facility = new DeploymentNode
        {
            Name = "Facility A",
            NodeType = DeploymentNodeType.Facility,
            Children =
            [
                new DeploymentNode
                {
                    Name = "Zone 1",
                    NodeType = DeploymentNodeType.Zone,
                    Children =
                    [
                        new DeploymentNode { Name = "ESP32-SOIL-01", NodeType = DeploymentNodeType.Sensor }
                    ]
                },
                new DeploymentNode { Name = "Zone 2", NodeType = DeploymentNodeType.Zone }
            ]
        };

        var violations = DeploymentValidator.Validate(facility);

        Assert.Empty(violations);
    }

    [Fact]
    public void Validate_WalksAFacilityZoneSubZoneSensorChainWithoutComplaint()
    {
        var facility = new DeploymentNode
        {
            Name = "Facility A",
            NodeType = DeploymentNodeType.Facility,
            Children =
            [
                new DeploymentNode
                {
                    Name = "Zone 1",
                    NodeType = DeploymentNodeType.Zone,
                    Children =
                    [
                        new DeploymentNode
                        {
                            Name = "Sub-Zone B",
                            NodeType = DeploymentNodeType.SubZone,
                            Children =
                            [
                                new DeploymentNode
                                {
                                    Name = "ESP32-MTR-07",
                                    NodeType = DeploymentNodeType.Sensor
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var violations = DeploymentValidator.Validate(facility);

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReportsABlankNamedNodeAgainstItsParentPath(string blankName)
    {
        var facility = new DeploymentNode
        {
            Name = "Facility A",
            NodeType = DeploymentNodeType.Facility,
            Children =
            [
                new DeploymentNode
                {
                    Name = "Zone 1",
                    NodeType = DeploymentNodeType.Zone,
                    Children =
                    [
                        new DeploymentNode { Name = blankName, NodeType = DeploymentNodeType.SubZone }
                    ]
                }
            ]
        };

        var violations = DeploymentValidator.Validate(facility);

        var violation = Assert.Single(violations);
        Assert.Contains("Facility A > Zone 1", violation);
        Assert.Contains("blank name", violation);
    }

    [Fact]
    public void Validate_ReportsTwoDirectSiblingsSharingAName()
    {
        var zone = new DeploymentNode
        {
            Name = "Zone 1",
            NodeType = DeploymentNodeType.Zone,
            Children =
            [
                new DeploymentNode { Name = "Sub-Zone B", NodeType = DeploymentNodeType.SubZone },
                new DeploymentNode { Name = "Sub-Zone B", NodeType = DeploymentNodeType.SubZone }
            ]
        };

        var violations = DeploymentValidator.Validate(zone);

        var violation = Assert.Single(violations);
        Assert.Contains("Zone 1", violation);
        Assert.Contains("Sub-Zone B", violation);
    }

    [Fact]
    public void Validate_ReportsSiblingsWhoseNamesDifferOnlyByCase()
    {
        var zone = new DeploymentNode
        {
            Name = "Zone 1",
            NodeType = DeploymentNodeType.Zone,
            Children =
            [
                new DeploymentNode { Name = "Sub-Zone B", NodeType = DeploymentNodeType.SubZone },
                new DeploymentNode { Name = "sub-zone b", NodeType = DeploymentNodeType.SubZone }
            ]
        };

        var violations = DeploymentValidator.Validate(zone);

        var violation = Assert.Single(violations);
        Assert.Contains("sibling nodes named", violation);
    }

    [Fact]
    public void Validate_AllowsTheSameNameUnderDifferentParents()
    {
        var facility = new DeploymentNode
        {
            Name = "Facility A",
            NodeType = DeploymentNodeType.Facility,
            Children =
            [
                new DeploymentNode
                {
                    Name = "Zone 1",
                    NodeType = DeploymentNodeType.Zone,
                    Children =
                    [
                        new DeploymentNode { Name = "Sub-Zone B", NodeType = DeploymentNodeType.SubZone }
                    ]
                },
                new DeploymentNode
                {
                    Name = "Zone 2",
                    NodeType = DeploymentNodeType.Zone,
                    Children =
                    [
                        new DeploymentNode { Name = "Sub-Zone B", NodeType = DeploymentNodeType.SubZone }
                    ]
                }
            ]
        };

        var violations = DeploymentValidator.Validate(facility);

        Assert.Empty(violations);
    }

    [Fact]
    public void Validate_ReportsASensorThatContainsChildNodes()
    {
        var zone = new DeploymentNode
        {
            Name = "Zone 1",
            NodeType = DeploymentNodeType.Zone,
            Children =
            [
                new DeploymentNode
                {
                    Name = "ESP32-VLV-03",
                    NodeType = DeploymentNodeType.Sensor,
                    Children =
                    [
                        new DeploymentNode { Name = "Sub-Zone C", NodeType = DeploymentNodeType.SubZone }
                    ]
                }
            ]
        };

        var violations = DeploymentValidator.Validate(zone);

        var violation = Assert.Single(violations);
        Assert.Contains("Zone 1 > ESP32-VLV-03", violation);
        Assert.Contains("must not contain child nodes", violation);
    }

    [Fact]
    public void Validate_ReturnsEveryViolationInTheTreeRatherThanStoppingAtTheFirst()
    {
        var facility = new DeploymentNode
        {
            Name = "Facility A",
            NodeType = DeploymentNodeType.Facility,
            Children =
            [
                new DeploymentNode
                {
                    Name = "Zone 1",
                    NodeType = DeploymentNodeType.Zone,
                    Children =
                    [
                        new DeploymentNode
                        {
                            Name = "ESP32-SOIL-01",
                            NodeType = DeploymentNodeType.Sensor,
                            Children =
                            [
                                new DeploymentNode { Name = "Sub-Zone X", NodeType = DeploymentNodeType.SubZone }
                            ]
                        },
                        new DeploymentNode { Name = "  ", NodeType = DeploymentNodeType.SubZone }
                    ]
                },
                new DeploymentNode
                {
                    Name = "Zone 2",
                    NodeType = DeploymentNodeType.Zone,
                    Children =
                    [
                        new DeploymentNode { Name = "Sub-Zone A", NodeType = DeploymentNodeType.SubZone },
                        new DeploymentNode { Name = "Sub-Zone A", NodeType = DeploymentNodeType.SubZone }
                    ]
                }
            ]
        };

        var violations = DeploymentValidator.Validate(facility);

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.Contains("must not contain child nodes"));
        Assert.Contains(violations, v => v.Contains("blank name"));
        Assert.Contains(violations, v => v.Contains("sibling nodes named 'Sub-Zone A'"));
    }
}
