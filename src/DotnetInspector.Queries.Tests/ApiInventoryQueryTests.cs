using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public class ApiInventoryQueryTests
{
    [Fact]
    public void Types_DescriptorsDriveOrderedFiltering()
    {
        var surface = new ApiSurface
        {
            Types =
            [
                new ApiType { Name = "C", Kind = "class" },
                new ApiType { Name = "S", Kind = "struct" },
                new ApiType { Name = "I", Kind = "interface" },
                new ApiType { Name = "E", Kind = "enum" },
                new ApiType { Name = "D", Kind = "delegate" },
                new ApiType { Name = "C2", Kind = "class" },
            ]
        };

        var result = ApiInventoryQuery.Types(surface);

        Assert.Equal(
            ["class", "struct", "interface", "enum", "delegate"],
            result.KindFacets.Select(facet => facet.SingularLabel));
        Assert.Equal([2, 1, 1, 1, 1], result.KindFacets.Select(facet => facet.Count));
        Assert.True(result.KindFacets.Select(facet => facet.Weight).SequenceEqual(
            result.KindFacets.Select(facet => facet.Weight).Order()));
        Assert.All(result.KindFacets, facet => Assert.True(facet.IsDefault));
        Assert.Equal(surface.Types, result.Types);

        foreach (var facet in result.KindFacets)
        {
            var filtered = ApiInventoryQuery.Types(
                surface,
                new ApiTypeInventoryRequest([facet.Id]));
            Assert.Equal(facet.Count, filtered.Types.Count);
        }

        var firstTwo = result.KindFacets.Take(2).Select(facet => facet.Id).ToList();
        var combined = ApiInventoryQuery.Types(
            surface,
            new ApiTypeInventoryRequest(firstTwo));
        Assert.Equal(3, combined.Types.Count);

        var defaults = ApiInventoryQuery.Types(
            surface,
            new ApiTypeInventoryRequest([]));
        Assert.Equal(surface.Types, defaults.Types);
    }

    [Fact]
    public void Members_RealMetadataShapesHaveProductOwnedFacets()
    {
        using var inspection = AssemblyInspectionSession.Open(
            typeof(ApiInventoryQueryTests).Assembly.Location);
        var surface = inspection.ApiSurface(includeAll: true);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(InventoryFixture).FullName);

        var result = ApiInventoryQuery.Members(type);

        Assert.Equal(
            [
                "constructor",
                "finalizer",
                "constant",
                "field",
                "property",
                "method",
                "operator",
                "extension method",
                "explicit implementation",
                "event",
            ],
            result.KindFacets.Select(facet => facet.SingularLabel));

        foreach (var facet in result.KindFacets)
        {
            var filtered = ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest([facet.Id]));
            Assert.Equal(facet.Count, filtered.Members.Count);
        }

        var constant = Assert.Single(result.KindFacets, facet => facet.SingularLabel == "constant");
        var constants = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest([constant.Id]));
        Assert.Contains(constants.Members, member => member.Name == nameof(InventoryFixture.Constant));
        Assert.All(constants.Members, member => Assert.True(member.IsConst));

        var extension = Assert.Single(result.KindFacets, facet => facet.SingularLabel == "extension method");
        var extensions = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest([extension.Id]));
        Assert.Contains(extensions.Members, member => member.Name == nameof(InventoryExtensions.Extend));
    }

    [Fact]
    public void Members_CompilerProducedExtensionOperatorHasOneKindFacet()
    {
        using var inspection = AssemblyInspectionSession.Open(
            typeof(ApiInventoryQueryTests).Assembly.Location);
        var surface = inspection.ApiSurface(includeAll: true);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(InventoryExtensions).FullName);
        var extensionOperator = Assert.Single(
            type.Members,
            member => member.Name == "op_Addition");

        Assert.Equal("operator", extensionOperator.Kind);
        Assert.True(extensionOperator.IsExtension);

        var result = ApiInventoryQuery.Members(type);

        var operatorFacet = Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "operator" && facet.Count == 1);
        Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "extension method" && facet.Count == 1);
        Assert.Single(
            ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest([operatorFacet.Id]))
            .Members,
            member => member.Name == "op_Addition");

        var targetType = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(InventoryFixture).FullName);
        var projected = Assert.Single(
            targetType.Members,
            member => member.Name == "op_Addition"
                && member.DeclaringType == typeof(InventoryExtensions).FullName);
        Assert.Equal("extension-method", projected.Kind);

        var targetResult = ApiInventoryQuery.Members(targetType);
        var extensionFacet = Assert.Single(
            targetResult.KindFacets,
            facet => facet.SingularLabel == "extension method");
        Assert.Contains(
            ApiInventoryQuery.Members(
                targetType,
                new ApiMemberInventoryRequest([extensionFacet.Id]))
            .Members,
            member => ReferenceEquals(member, projected));
    }

    [Fact]
    public void Members_StaticConstructorUsesConstructorFacet()
    {
        using var inspection = AssemblyInspectionSession.Open(
            typeof(ApiInventoryQueryTests).Assembly.Location);
        var surface = inspection.ApiSurface(includeAll: true);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(InventoryFixture).FullName);
        var staticConstructor = Assert.Single(
            type.Members,
            member => member.Name == ".cctor");

        Assert.Equal("method", staticConstructor.Kind);

        var result = ApiInventoryQuery.Members(type);
        var constructorFacet = Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "constructor");
        var methodFacet = Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "method");

        Assert.Contains(
            ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest([constructorFacet.Id]))
            .Members,
            member => ReferenceEquals(member, staticConstructor));
        Assert.DoesNotContain(
            ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest([methodFacet.Id]))
            .Members,
            member => ReferenceEquals(member, staticConstructor));
    }

    [Fact]
    public void Types_PreservesPartialInspectionFailures()
    {
        var failure = new ApiSurfaceInspectionFailure(
            "relationship",
            0x02000001,
            MetadataTypeNameFailureMechanism.Relationship,
            "base-type",
            "cycle");
        var surface = new ApiSurface
        {
            Types = [new ApiType { Name = "C", Kind = "class" }],
            InspectionFailures = [failure]
        };

        var result = ApiInventoryQuery.Types(surface);

        Assert.Equal([failure], result.InspectionFailures);
        Assert.NotSame(surface.InspectionFailures, result.InspectionFailures);
    }

    [Fact]
    public void Selection_RejectsUnknownFacetIds()
    {
        var surface = new ApiSurface
        {
            Types = [new ApiType { Name = "C", Kind = "class" }]
        };

        var error = Assert.Throws<ArgumentException>(() =>
            ApiInventoryQuery.Types(
                surface,
                new ApiTypeInventoryRequest(["consumer-invented-kind"])));

        Assert.Contains("consumer-invented-kind", error.Message);
    }

    [Fact]
    public void Classification_RejectsUnknownProducerKinds()
    {
        var surface = new ApiSurface
        {
            Types = [new ApiType { Name = "Future", Kind = "future-kind" }]
        };
        var type = new ApiType
        {
            Name = "Future",
            Kind = "class",
            Members = [new ApiMember { Name = "Future", Kind = "future-kind" }]
        };

        Assert.Throws<InvalidOperationException>(() => ApiInventoryQuery.Types(surface));
        Assert.Throws<InvalidOperationException>(() => ApiInventoryQuery.Members(type));
    }
}

public interface IInventoryFixture
{
    void Explicit();
}

public sealed class InventoryFixture : IInventoryFixture
{
    public const int Constant = 1;
    public int Field;
    public int Property { get; set; }
    public event EventHandler? Changed;

    public InventoryFixture() { }

    static InventoryFixture() { }

    ~InventoryFixture() { }

    public void Method() => Changed?.Invoke(this, EventArgs.Empty);

    void IInventoryFixture.Explicit() { }

    public static InventoryFixture operator +(InventoryFixture left, InventoryFixture right)
        => left;
}

public static class InventoryExtensions
{
    public static void Extend(this InventoryFixture fixture) => fixture.Method();

    public static void op_Addition(this InventoryFixture fixture) => fixture.Method();
}

public struct InventoryStruct;
public interface IInventoryType;
public enum InventoryEnum;
public delegate void InventoryDelegate();
