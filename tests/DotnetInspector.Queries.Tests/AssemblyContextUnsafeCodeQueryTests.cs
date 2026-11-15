using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextUnsafeCodeQueryTests
{
    // A held participant's Library answer is the path's Finding Unsafe Any,
    // each Type resolves through the all-accessibility API surface, and that
    // Type's census reports its members with their navigation identities.
    [Fact]
    public async Task ExecuteParticipant_ListsTypesThenTheTypeCensusWithNavigation()
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

        var library = Assert.IsType<AssemblyContextEntry<AssemblyUnsafeCodeTypesResult>.Available>(
            AssemblyContextFindingUnsafeAnyQuery.ExecuteParticipant(group, participant));
        var answer = Assert.IsType<ILInspector.Analysis.Planning.FindingUnsafeAnyResult.Available>(
            Assert.IsType<FindingUnsafeAnyQueryResult.Inspected>(library.Value.Answer).Answer);

        using PdbContext context = PdbContext.OpenMetadataOnly(path);
        var expected = Assert.IsType<ILInspector.Analysis.Planning.FindingUnsafeAnyResult.Available>(
            Assert.IsType<FindingUnsafeAnyQueryResult.Inspected>(
                FindingUnsafeAnyQuery.Execute(path, context)).Answer);
        Assert.Equal(expected.Types, answer.Types);
        Assert.NotEmpty(answer.Types.Types);
        Assert.All(answer.Types.Types, type =>
            Assert.True(library.Value.Types.ContainsKey(Assert.NotNull(type.MetadataToken))));

        UnsafeCodeType declaring = Assert.Single(
            answer.Types.Types,
            type => library.Value.Types[type.MetadataToken!.Value].DeclaringType.Members
                .Any(static member => member.Name == "CaptureLocal"));
        string typeIdentity = library.Value.Types[declaring.MetadataToken!.Value].Type;
        var entry = Assert.IsType<AssemblyContextEntry<AssemblyUnsafeMemberCensusResult>.Available>(
            AssemblyContextUnsafeMemberCensusQuery.ExecuteParticipant(
                group, participant, new UnsafeMemberCensusScope.TypeScope(typeIdentity)));
        var census = Assert.IsType<UnsafeMemberCensusQueryResult.Inspected>(entry.Value.Census);
        var available = Assert.IsType<ILInspector.Analysis.Planning.UnsafeMemberCensusResult.Available>(census.Census);

        UnsafeMemberFinding capture = Assert.Single(
            available.Census.Members,
            static member => member.Member.Name == "CaptureLocal");
        UnsafeMemberNavigation navigation = entry.Value.Members[capture.Member.MetadataToken];
        Assert.Equal(typeIdentity, navigation.Type);
        Assert.Equal("CaptureLocal", navigation.Member);
        Assert.NotEqual("public", navigation.Accessibility);
        Assert.NotEmpty(navigation.StableSelector);
    }
}
