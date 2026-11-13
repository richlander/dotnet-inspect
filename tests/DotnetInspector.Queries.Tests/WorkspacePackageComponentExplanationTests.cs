using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Queries.Definitions;
using QuerySpace.Explanation;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspacePackageComponentExplanationTests
{
    private const string Component = """
        {"contract":"workspace/components/package","schema_version":1,"package_id":"System.Text.Json","version":"10.0.0","framework":"net10.0"}
        """;

    [Fact]
    public void Standalone_PreservesLocalValuesAndDefaultsWithoutParentFacts()
    {
        var input = WorkspacePackageComponentExplanation.Parse(Component);
        Assert.Equal("System.Text.Json", input.Declaration.Id);
        Assert.Equal("10.0.0", input.Declaration.Version);
        Assert.Equal("net10.0", input.Declaration.Framework);
        Assert.Null(input.Declaration.RuntimeIdentifier);
        Assert.Equal(["runtime_identifier"], input.DefaultedFields);
        Assert.Null(input.Occurrence);
    }

    [Fact]
    public void ExplicitNull_IsSuppliedRatherThanDefaulted()
    {
        var input = WorkspacePackageComponentExplanation.Parse(
            Component.Replace("\"framework\":\"net10.0\"", "\"framework\":null,\"runtime_identifier\":null"));
        Assert.Empty(input.DefaultedFields);
        Assert.Null(input.Declaration.Framework);
        Assert.Null(input.Declaration.RuntimeIdentifier);
        Assert.Null(input.Occurrence);
    }

    [Fact]
    public void ExtractedInventory_QualifiesOnlyAgainstTheSuppliedParent()
    {
        WorkspaceSharePacket parent = Parent();
        WorkspacePackageComponentDescriptor component =
            Assert.Single(WorkspaceComponentCatalog.Describe(parent).Packages);
        string json = JsonSerializer.Serialize(component,
            ComponentExplanationTestJsonContext.Default.WorkspacePackageComponentDescriptor);

        var local = WorkspacePackageComponentExplanation.Parse(json);
        Assert.Null(local.Occurrence);
        Assert.Empty(local.DefaultedFields);

        var qualified = WorkspacePackageComponentExplanation.Parse(json, parent);
        Assert.Same(parent, qualified.Occurrence!.Packet);
        Assert.Equal(component.Path, qualified.Occurrence.Component.Path);
        Assert.Equal(["contexts/g0"],
            qualified.Occurrence.Component.Contexts.Select(p => p.Value));
        Assert.True(qualified.Occurrence.Component.Focused);

        WorkspaceSharePacket differentParent = Parent(focused: false);
        Assert.Equal(WorkspaceComponentExplanationFailureKind.ParentMismatch,
            Assert.Throws<WorkspaceComponentExplanationException>(() =>
                WorkspacePackageComponentExplanation.Parse(json, differentParent)).Kind);
        var otherOccurrence = WorkspacePackageComponentExplanation.Extract(
            differentParent, component.Path);
        Assert.Same(differentParent, otherOccurrence.Occurrence!.Packet);
        Assert.False(otherOccurrence.Occurrence.Component.Focused);
        Assert.NotSame(qualified.Occurrence.Packet, otherOccurrence.Occurrence.Packet);
    }

    [Theory]
    [InlineData("\"contract\":\"workspace/components/package\"", "\"contract\":\"unknown\"", WorkspaceComponentExplanationFailureKind.UnsupportedContract)]
    [InlineData("\"schema_version\":1", "\"schema_version\":2", WorkspaceComponentExplanationFailureKind.UnsupportedVersion)]
    [InlineData("\"version\":\"10.0.0\"", "\"version\":\"10.*\"", WorkspaceComponentExplanationFailureKind.InvalidInput)]
    [InlineData("\"framework\":\"net10.0\"", "\"framework\":\"NET10.0\"", WorkspaceComponentExplanationFailureKind.InvalidInput)]
    [InlineData("\"package_id\":\"System.Text.Json\"", "\"package_id\":\"Bad/Id\"", WorkspaceComponentExplanationFailureKind.InvalidInput)]
    [InlineData("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1", WorkspaceComponentExplanationFailureKind.InvalidInput)]
    [InlineData("\"schema_version\":1", "\"schema_version\":1,\"unknown\":true", WorkspaceComponentExplanationFailureKind.InvalidInput)]
    public void InvalidDeclarations_AreTypedRefusals(string oldValue, string newValue,
        WorkspaceComponentExplanationFailureKind expected)
    {
        Assert.Equal(expected,
            Assert.Throws<WorkspaceComponentExplanationException>(() =>
                WorkspacePackageComponentExplanation.Parse(Component.Replace(oldValue, newValue))).Kind);
    }

    [Fact]
    public void ClaimedPath_MustMatchLocalCoordinateEvenWithoutParent()
    {
        string mismatch = Component.Replace("\"schema_version\":1",
            "\"schema_version\":1,\"path\":\"packages/system.text.json@9.0.0/net10.0/~\"");
        Assert.Equal(WorkspaceComponentExplanationFailureKind.InvalidInput,
            Assert.Throws<WorkspaceComponentExplanationException>(() =>
                WorkspacePackageComponentExplanation.Parse(mismatch)).Kind);
    }

    [Fact]
    public void ParentCannotSupplyMissingCoordinateOrTurnUnknownIntoSuccess()
    {
        Assert.Equal(WorkspaceComponentExplanationFailureKind.UnknownComponent,
            Assert.Throws<WorkspaceComponentExplanationException>(() =>
                WorkspacePackageComponentExplanation.Parse(
                    Component.Replace("10.0.0", "9.0.0"), Parent())).Kind);
        string omittedTarget = Component.Replace(
            ",\"framework\":\"net10.0\"", "");
        Assert.Equal(WorkspaceComponentExplanationFailureKind.UnknownComponent,
            Assert.Throws<WorkspaceComponentExplanationException>(() =>
                WorkspacePackageComponentExplanation.Parse(omittedTarget, Parent())).Kind);
    }

    [Fact]
    public void SamePath_InDifferentParentDoesNotCarryContextMembership()
    {
        var first = Parent();
        var descriptor = Assert.Single(WorkspaceComponentCatalog.Describe(first).Packages);
        string json = JsonSerializer.Serialize(descriptor,
            ComponentExplanationTestJsonContext.Default.WorkspacePackageComponentDescriptor);
        WorkspaceSharePacket second = WorkspaceSharePacketCodec.ParseJson("""
            {"f":4,"t":[["System.Text.Json","10.0.0","net10.0",null],["Other","1.0.0","net9.0",null]],"g":[[1],[0]],"r":[],"a":0,"x":1,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}},{"t":1,"u":{"k":"package"},"r":{"k":"package"}}]}
            """, TestContext.Current.CancellationToken);
        Assert.Equal(WorkspaceComponentExplanationFailureKind.ParentMismatch,
            Assert.Throws<WorkspaceComponentExplanationException>(() =>
                WorkspacePackageComponentExplanation.Parse(json, second)).Kind);
        var extracted = WorkspacePackageComponentExplanation.Extract(second, descriptor.Path);
        Assert.Equal(["contexts/g1"], extracted.Occurrence!.Component.Contexts.Select(c => c.Value));
        Assert.Same(second, extracted.Occurrence.Packet);
    }

    [Fact]
    public void InputBudgetsAndUnicode_AreEnforced()
    {
        Assert.Throws<WorkspaceComponentExplanationException>(() =>
            WorkspacePackageComponentExplanation.Parse(
                new string(' ', WorkspacePackageComponentExplanation.MaxUtf8ByteLength + 1)));
        Assert.Throws<WorkspaceComponentExplanationException>(() =>
            WorkspacePackageComponentExplanation.Parse(Component + "\ud800"));
    }

    [Fact]
    public void Schema_DeclaresNativeValuesAndUnavailableParentFacts()
    {
        ExplanationSchema schema = WorkspacePackageComponentExplanation.Schema;
        ExplanationConformance.ValidateSnapshots([schema], []);
        Assert.Equal("workspace", schema.Identity.Owner.Value);
        Assert.Equal(1, schema.Version.Value);
        ExplanationResourceTypeDeclaration package = Assert.Single(schema.ResourceTypes);
        Assert.Equal("package", package.Identity.Value);
        Assert.Equal(8, package.Facts.Length);
        foreach (string field in new[] { "component_path", "contexts", "focused" })
            Assert.True(Assert.Single(package.Facts, f => f.Identity.Value == field)
                .AdmittedStates.HasFlag(ExplanationObservationStates.Unavailable));
    }

    private static WorkspaceSharePacket Parent(bool focused = true) =>
        WorkspaceSharePacketCodec.ParseJson(
            """
            {"f":4,"t":[["System.Text.Json","10.0.0","net10.0",null]],"g":[[0]],"r":[],"a":FOCUS,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """.Replace("FOCUS", focused ? "0" : "null"),
            TestContext.Current.CancellationToken);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WorkspacePackageComponentDescriptor))]
internal partial class ComponentExplanationTestJsonContext : JsonSerializerContext;
