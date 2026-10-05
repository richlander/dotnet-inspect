using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.Tests;

public class SectionShapeOutputPolicyTests
{
    private static readonly Dictionary<string, SectionShape> Shapes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Rows"] = SectionShape.Table,
            ["Tree"] = SectionShape.Hierarchy,
            ["Body"] = SectionShape.Text,
        };

    private static readonly Dictionary<
        string,
        SectionCardinalityDeclaration> Cardinalities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Scalar"] = SectionCardinalityDeclaration.Scalar,
            ["Inventory"] = SectionCardinalityDeclaration.Inventory,
        };

    [Fact]
    public void ResolveNativeOutput_ReturnsShapeTerminal()
    {
        Assert.Equal(
            SectionNativeOutput.TabularRows,
            SectionShapeOutputPolicy.ResolveNativeOutput(
                selectionExplicit: true,
                ["Rows"],
                Shapes,
                hasExplicitOutputIntent: false));
        Assert.Equal(
            SectionNativeOutput.HierarchyTree,
            SectionShapeOutputPolicy.ResolveNativeOutput(
                selectionExplicit: true,
                ["Tree"],
                Shapes,
                hasExplicitOutputIntent: false));
        Assert.Equal(
            SectionNativeOutput.TextPayload,
            SectionShapeOutputPolicy.ResolveNativeOutput(
                selectionExplicit: true,
                ["Body"],
                Shapes,
                hasExplicitOutputIntent: false));
    }

    [Fact]
    public void ResolveNativeOutput_RequiresOneImplicitFormatSelection()
    {
        Assert.Null(SectionShapeOutputPolicy.ResolveNativeOutput(
            selectionExplicit: false,
            ["Rows"],
            Shapes,
            hasExplicitOutputIntent: false));
        Assert.Null(SectionShapeOutputPolicy.ResolveNativeOutput(
            selectionExplicit: true,
            ["Rows", "Tree"],
            Shapes,
            hasExplicitOutputIntent: false));
        Assert.Null(SectionShapeOutputPolicy.ResolveNativeOutput(
            selectionExplicit: true,
            ["Rows"],
            Shapes,
            hasExplicitOutputIntent: true));
        Assert.Null(SectionShapeOutputPolicy.ResolveNativeOutput(
            selectionExplicit: true,
            ["Unknown"],
            Shapes,
            hasExplicitOutputIntent: false));
    }

    [Theory]
    [InlineData(SectionTerminalCapability.Count, "--count")]
    [InlineData(SectionTerminalCapability.Rows, "--rows")]
    public void ValidateScalarTerminal_RejectsScalar(
        SectionTerminalCapability terminal,
        string option)
    {
        string error = Assert.IsType<string>(
            SectionShapeOutputPolicy.ValidateScalarTerminal(
                ["Scalar"],
                Cardinalities,
                terminal,
                discovery: false));

        Assert.Contains($"does not support {option}", error);
        Assert.Contains("Select an inventory section", error);
    }

    [Fact]
    public void ValidateScalarTerminal_AllowsNonScalarRequests()
    {
        Assert.Null(SectionShapeOutputPolicy.ValidateScalarTerminal(
            ["Inventory"],
            Cardinalities,
            SectionTerminalCapability.Count,
            discovery: false));
        Assert.Null(SectionShapeOutputPolicy.ValidateScalarTerminal(
            ["Scalar", "Inventory"],
            Cardinalities,
            SectionTerminalCapability.Count,
            discovery: false));
        Assert.Null(SectionShapeOutputPolicy.ValidateScalarTerminal(
            ["Scalar"],
            Cardinalities,
            SectionTerminalCapability.Rows,
            discovery: true));
        Assert.Null(SectionShapeOutputPolicy.ValidateScalarTerminal(
            ["Scalar"],
            Cardinalities,
            terminal: null,
            discovery: false));
    }

    [Fact]
    public void DescriptionsUseDeclaredShapeAndFormats()
    {
        OutputCapabilityCatalog capabilities =
            OutputCapabilityCatalog.FromShapes(Shapes);

        Assert.Equal(
            "'Tree' (Hierarchy)",
            SectionShapeOutputPolicy.DescribeSection("Tree", Shapes));
        Assert.Equal(
            "'Unknown'",
            SectionShapeOutputPolicy.DescribeSection("Unknown", Shapes));
        Assert.Equal(
            "--markdown, --plaintext, --json, --table, --tsv, --jsonl, --tree",
            SectionShapeOutputPolicy.DescribeFormats("Tree", capabilities));
        Assert.Equal(
            "--markdown, --json",
            SectionShapeOutputPolicy.DescribeFormats("Unknown", capabilities));
    }
}
