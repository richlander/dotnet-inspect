using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextStructuralCloneSeedQueryTests
{
    static ApiSurfaceProjectionLimits Limits { get; } =
        new(
            maxParticipants: 1,
            maxTypes: 100_000,
            maxMembers: 1_000_000,
            maxInspectionFailures: 1_024,
            maxTypeForwarders: 100_000,
            maxMetadataRows: 250_000,
            maxRetainedTextCharacters: 32_000_000);

    [Fact]
    public async Task ExecuteMember_MapsReferenceBodyToImplementationMethod()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup sourceGroup = Group(
            workspace,
            FixtureCatalog.AnalysisMethodCorrespondenceSurface.AssemblyPath());
        using AssemblyContextGroup implementationGroup = Group(
            workspace,
            FixtureCatalog.AnalysisMethodCorrespondenceRuntime.AssemblyPath());
        AssemblyContextParticipant sourceParticipant =
            Assert.Single(sourceGroup.Participants);
        AssemblyContextParticipant implementationParticipant =
            Assert.Single(implementationGroup.Participants);
        LogicalMember source = Member(
            sourceGroup,
            sourceParticipant,
            "MethodCorrespondenceFixture.Widget",
            "Transform");
        LogicalMember implementation = Member(
            implementationGroup,
            implementationParticipant,
            "MethodCorrespondenceFixture.Widget",
            "Transform");
        Assert.NotEqual(
            source.Member.MetadataToken,
            implementation.Member.MetadataToken);
        string selector =
            CallGraphMemberResolver.CreateSelector(
                source.Type,
                source.Member).Key;

        StructuralCloneSearchSeed.Member result = Available(
            AssemblyContextStructuralCloneSeedQuery.ExecuteMember(
                implementationGroup,
                implementationParticipant,
                sourceGroup,
                sourceParticipant,
                new(
                    source.Type.DefinitionName!,
                    ApiMemberIdentity.GetMemberAnchor(
                        source.Type,
                        source.Member),
                    new(
                        source.Type.DefinitionName!.ToEscapedFullName(),
                        source.Member.Name,
                        selector,
                        source.Member.MetadataToken)),
                Limits));
        MemberAnchor expected = Available(
            AssemblyContextMethodAnchorQuery.ExecuteParticipant(
                implementationGroup,
                implementationParticipant,
                implementation.Type.DefinitionName!,
                implementation.Member.MetadataToken!.Value,
                implementation.Member.IsExtension));

        Assert.Equal(expected, result.MemberIdentity);
    }

    [Fact]
    public async Task ExecuteMember_AcceptsImplementationIssuedBody()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup sourceGroup = Group(
            workspace,
            FixtureCatalog.AnalysisMethodCorrespondenceSurface.AssemblyPath());
        using AssemblyContextGroup implementationGroup = Group(
            workspace,
            FixtureCatalog.AnalysisMethodCorrespondenceRuntime.AssemblyPath());
        AssemblyContextParticipant sourceParticipant =
            Assert.Single(sourceGroup.Participants);
        AssemblyContextParticipant implementationParticipant =
            Assert.Single(implementationGroup.Participants);
        LogicalMember implementation = Member(
            implementationGroup,
            implementationParticipant,
            "MethodCorrespondenceFixture.Widget",
            "Transform");
        string selector =
            CallGraphMemberResolver.CreateSelector(
                implementation.Type,
                implementation.Member).Key;
        MemberAnchor anchor = ApiMemberIdentity.GetMemberAnchor(
            implementation.Type,
            implementation.Member);

        StructuralCloneSearchSeed.Member result = Available(
            AssemblyContextStructuralCloneSeedQuery.ExecuteMember(
                implementationGroup,
                implementationParticipant,
                sourceGroup,
                sourceParticipant,
                new(
                    implementation.Type.DefinitionName!,
                    anchor,
                    new(
                        implementation.Type.DefinitionName!
                            .ToEscapedFullName(),
                        implementation.Member.Name,
                        selector,
                        implementation.Member.MetadataToken)),
                Limits));
        MemberAnchor expected = Available(
            AssemblyContextMethodAnchorQuery.ExecuteParticipant(
                implementationGroup,
                implementationParticipant,
                implementation.Type.DefinitionName!,
                implementation.Member.MetadataToken!.Value,
                implementation.Member.IsExtension));

        Assert.Equal(expected, result.MemberIdentity);
    }

    [Fact]
    public async Task ExecuteMember_RejectsBodyOutsideLogicalMember()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            FixtureCatalog.AnalysisMethodCorrespondenceRuntime.AssemblyPath());
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        LogicalMember requested = Member(
            group,
            participant,
            "MethodCorrespondenceFixture.Widget",
            "Transform");
        LogicalMember body = Member(
            group,
            participant,
            "MethodCorrespondenceFixture.Widget",
            "Other");
        string bodySelector =
            CallGraphMemberResolver.CreateSelector(
                body.Type,
                body.Member).Key;

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            AssemblyContextStructuralCloneSeedQuery.ExecuteMember(
                group,
                participant,
                group,
                participant,
                new(
                    requested.Type.DefinitionName!,
                    ApiMemberIdentity.GetMemberAnchor(
                        requested.Type,
                        requested.Member),
                    new(
                        body.Type.DefinitionName!.ToEscapedFullName(),
                        body.Member.Name,
                        bodySelector,
                        body.Member.MetadataToken)),
                Limits));

        Assert.Contains(
            "does not belong",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteMember_ReusesSharedSourceParticipantProjection()
    {
        await using var workspace = new InspectionWorkspace();
        string path =
            FixtureCatalog.AnalysisMethodCorrespondenceRuntime.AssemblyPath();
        using AssemblyContextGroup probeGroup = Group(workspace, path);
        AssemblyContextParticipant probeParticipant =
            Assert.Single(probeGroup.Participants);
        LogicalMember property = Member(
            probeGroup,
            probeParticipant,
            "MethodCorrespondenceFixture.Helper",
            "Value");
        MemberAnchor anchor = ApiMemberIdentity.GetMemberAnchor(
            property.Type,
            property.Member);
        CountedAssembly counted = CountedAssembly.Create(path);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([counted.Participant]);

        StructuralCloneSearchSeed.Member result = Available(
            AssemblyContextStructuralCloneSeedQuery.ExecuteMember(
                group,
                counted.Participant,
                group,
                counted.Participant,
                new(property.Type.DefinitionName!, anchor),
                Limits));

        Assert.Equal(anchor, result.MemberIdentity);
        Assert.Equal(1, counted.OpenCount);
    }

    static LogicalMember Member(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeName,
        string memberName)
    {
        ApiSurface surface = Available(
                AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    participant,
                    ApiSurfaceScope.IncludeAll))
            .Surface;
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeName);
        return new(
            type,
            Assert.Single(
                type.Members,
                candidate => candidate.Name == memberName));
    }

    static TValue Available<TValue>(AssemblyContextEntry<TValue> entry) =>
        Assert.IsType<AssemblyContextEntry<TValue>.Available>(entry).Value;

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string path) =>
        workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "structural clone seed tests")),
                    new TestBindingPolicy()),
            ]);

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }

    sealed class CountedAssembly
    {
        int _openCount;

        CountedAssembly(
            byte[] image,
            AssemblyReferenceIdentity identity,
            string label)
        {
            Image = image;
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () =>
                    {
                        Interlocked.Increment(ref _openCount);
                        return new MemoryStream(Image, writable: false);
                    },
                    AssemblyResolutionProvenance.Local(label));
            Participant = new(
                assembly,
                new TestBindingPolicy());
        }

        byte[] Image { get; }

        public AssemblyContextParticipant Participant { get; }

        public int OpenCount => Volatile.Read(ref _openCount);

        public static CountedAssembly Create(string path)
        {
            ResolvedAssemblyReference source =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local(
                        "structural clone seed identity"));
            return new(
                File.ReadAllBytes(path),
                source.Identity,
                "structural clone seed counted participant");
        }
    }

    sealed record LogicalMember(ApiType Type, ApiMember Member);
}
