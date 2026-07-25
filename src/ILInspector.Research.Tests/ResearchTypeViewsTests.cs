using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Research.Tests.TypeFixtures;

namespace ILInspector.Research.Tests;

public class ResearchTypeViewsTests
{
    static MetadataSource OpenSelf()
        => MetadataSource.Open(typeof(ResearchTypeViewsTests).Assembly.Location);

    static ApiType FindType(ApiSurface surface, Type clrType)
    {
        var type = surface.Types.FirstOrDefault(t => t.FullName == clrType.FullName)
            ?? surface.Types.FirstOrDefault(t => t.MetadataName == clrType.Name);
        Assert.NotNull(type);
        return type!;
    }

    [Fact]
    public void ProjectType_AbstractClass_ExposesInterfacesAndDerivedTypes()
    {
        using var source = OpenSelf();
        var surface = source.ExtractApiSurface(includeAll: true);
        var animal = FindType(surface, typeof(ResearchAnimal));

        var result = ResearchViews.ProjectType(animal, surface);

        Assert.Equal("class", result.Identity.Kind);
        Assert.Contains("abstract", result.Identity.Modifiers);
        // System.Object base is trivial and filtered out.
        Assert.Null(result.BaseType);

        Assert.Equal(
            [typeof(IResearchAged).FullName!, typeof(IResearchNamed).FullName!],
            result.Interfaces);

        Assert.Equal(
            [typeof(ResearchCat).FullName!, typeof(ResearchDog).FullName!],
            result.DerivedTypes);
    }

    [Fact]
    public void ProjectType_RelationshipGraph_UsesNeutralNodeRoleEdgeModel()
    {
        using var source = OpenSelf();
        var surface = source.ExtractApiSurface(includeAll: true);
        var animal = FindType(surface, typeof(ResearchAnimal));

        var graph = ResearchViews.ProjectType(animal, surface).Graph;
        Assert.NotNull(graph);

        var self = Assert.Single(graph!.Nodes, n => n.Role == ResearchViews.TypeRelationshipRole.Self);
        Assert.Equal(typeof(ResearchAnimal).FullName, self.Id);

        Assert.Equal(2, graph.Nodes.Count(n => n.Role == ResearchViews.TypeRelationshipRole.Interface));
        Assert.Equal(2, graph.Nodes.Count(n => n.Role == ResearchViews.TypeRelationshipRole.Derived));
        Assert.DoesNotContain(graph.Nodes, n => n.Role == ResearchViews.TypeRelationshipRole.Base);

        Assert.Equal(2, graph.Edges.Count(e => e.Kind == ResearchViews.TypeRelationshipKind.Implements));
        Assert.All(
            graph.Edges.Where(e => e.Kind == ResearchViews.TypeRelationshipKind.Implements),
            e => Assert.Equal(self.Id, e.FromId));

        Assert.Equal(2, graph.Edges.Count(e => e.Kind == ResearchViews.TypeRelationshipKind.DerivedFrom));
        Assert.All(
            graph.Edges.Where(e => e.Kind == ResearchViews.TypeRelationshipKind.DerivedFrom),
            e => Assert.Equal(self.Id, e.ToId));

        Assert.DoesNotContain(graph.Edges, e => e.Kind == ResearchViews.TypeRelationshipKind.Inherits);
    }

    [Fact]
    public void ProjectType_SealedDerivedClass_ReportsBaseAndInheritsEdge()
    {
        using var source = OpenSelf();
        var surface = source.ExtractApiSurface(includeAll: true);
        var dog = FindType(surface, typeof(ResearchDog));

        var result = ResearchViews.ProjectType(dog, surface);

        Assert.Contains("sealed", result.Identity.Modifiers);
        Assert.Equal(typeof(ResearchAnimal).FullName, result.BaseType);

        var inherits = Assert.Single(result.Graph!.Edges, e => e.Kind == ResearchViews.TypeRelationshipKind.Inherits);
        Assert.Equal(typeof(ResearchDog).FullName, inherits.FromId);
        Assert.Equal(typeof(ResearchAnimal).FullName, inherits.ToId);
    }

    [Fact]
    public void ProjectType_GenericType_ProjectsTypeParameterConstraints()
    {
        using var source = OpenSelf();
        var surface = source.ExtractApiSurface(includeAll: true);
        var repository = FindType(surface, typeof(ResearchRepository<>));

        var result = ResearchViews.ProjectType(repository, surface);

        var tp = Assert.Single(result.TypeParameters);
        Assert.Equal("T", tp.Name);
        Assert.Null(tp.Variance);
        Assert.Contains("class", tp.Constraints);
        Assert.Contains("new()", tp.Constraints);
        Assert.Contains(tp.Constraints, c => c.Contains("IResearchNamed"));
    }

    [Fact]
    public void ProjectType_Enum_ReportsUnderlyingType()
    {
        using var source = OpenSelf();
        var surface = source.ExtractApiSurface(includeAll: true);
        var color = FindType(surface, typeof(ResearchColor));

        var result = ResearchViews.ProjectType(color, surface);

        Assert.Equal("enum", result.Identity.Kind);
        Assert.NotNull(result.EnumUnderlyingType);
        Assert.Contains("byte", result.EnumUnderlyingType!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectType_RefStruct_And_ReadonlyStruct_CarryModifiers()
    {
        using var source = OpenSelf();
        var surface = source.ExtractApiSurface(includeAll: true);

        var refSpan = ResearchViews.ProjectType(FindType(surface, typeof(ResearchRefSpan)));
        Assert.Equal("struct", refSpan.Identity.Kind);
        Assert.Contains("ref", refSpan.Identity.Modifiers);

        var readonlyPoint = ResearchViews.ProjectType(FindType(surface, typeof(ResearchReadonlyPoint)));
        Assert.Equal("struct", readonlyPoint.Identity.Kind);
        Assert.Contains("readonly", readonlyPoint.Identity.Modifiers);
    }

    [Fact]
    public void ProjectType_Request_ComposesCountsAndRecoversAsync()
    {
        using var source = OpenSelf();

        var result = ResearchViews.ProjectType(new ResearchViews.TypeProjectionRequest(
            source,
            typeof(ResearchComposite).FullName!));

        Assert.Equal(source.AssemblyName, result.Identity.Assembly);

        var composition = result.Composition;
        Assert.NotNull(composition);
        // Two async methods (DoWorkAsync, ComputeAsync) recovered from metadata classification;
        // ApiSurface extraction alone would report zero (async is a body-gated fact).
        Assert.Equal(2, composition!.Async);
        // One unsafe method (Poke) — populated directly by surface extraction.
        Assert.Equal(1, composition.Unsafe);
        Assert.True(composition.Static >= 2, $"expected >= 2 static members, got {composition.Static}");
        Assert.True(composition.Methods >= 5, $"expected >= 5 methods, got {composition.Methods}");
        Assert.True(composition.Constructors >= 1);
        Assert.True(composition.Properties >= 1);
    }

    [Fact]
    public void ProjectType_Core_DoesNotInventAsync_ButKeepsUnsafe()
    {
        using var source = OpenSelf();
        var surface = source.ExtractApiSurface(includeAll: true);
        var composite = FindType(surface, typeof(ResearchComposite));

        // Core overload has no MetadataReader, so it composes only the surface-carried flags:
        // unsafe is populated, async is deliberately not (docs/design/member-body-substrate.md).
        var result = ResearchViews.ProjectType(composite, surface);

        Assert.Equal(0, result.Composition!.Async);
        Assert.Equal(1, result.Composition.Unsafe);
    }

    [Fact]
    public void ProjectType_Request_ThrowsForUnknownType()
    {
        using var source = OpenSelf();

        Assert.Throws<InvalidOperationException>(() =>
            ResearchViews.ProjectType(new ResearchViews.TypeProjectionRequest(source, "No.Such.Type")));
    }
}
