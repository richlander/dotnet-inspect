using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextUnsafeMemberCensusQueryTests
{
    // A held participant runs the same census as the path, and every declared
    // member it reports resolves through the all-accessibility API surface.
    [Fact]
    public async Task ExecuteParticipant_RunsTheSharedCensusWithNavigation()
    {
        string path = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        byte[] bytes = File.ReadAllBytes(path);
        using var reader = new PEReader(ImmutableArray.Create(bytes));
        var identity = AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path));
        var participant = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(identity, path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local("unsafe-member-fixture")), resolver);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);

        var entry = Assert.IsType<AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Available>(
            AssemblyContextUnsafeMemberCensusQuery.ExecuteParticipant(group, participant));
        var census = Assert.IsType<UnsafeMemberCensusQueryResult.Inspected>(entry.Value.Census);
        var available = Assert.IsType<ILInspector.Analysis.Planning.UnsafeMemberCensusResult.Available>(census.Census);

        using PdbContext context = PdbContext.OpenMetadataOnly(path);
        var expected = Assert.IsType<ILInspector.Analysis.Planning.UnsafeMemberCensusResult.Available>(
            Assert.IsType<UnsafeMemberCensusQueryResult.Inspected>(
                UnsafeMemberCensusQuery.Execute(path, context)).Census);
        Assert.Equal(
            [.. expected.Census.Members.Select(static member => member.Member.MetadataToken).Order()],
            [.. available.Census.Members.Select(static member => member.Member.MetadataToken).Order()]);

        UnsafeMemberFinding capture = Assert.Single(
            available.Census.Members,
            static member => member.Member.Name == "CaptureLocal");
        UnsafeMemberNavigation navigation = entry.Value.Members[capture.Member.MetadataToken];
        Assert.Equal("CaptureLocal", navigation.Member);
        Assert.NotEqual("public", navigation.Accessibility);
        Assert.NotEmpty(navigation.StableSelector);
    }
}
