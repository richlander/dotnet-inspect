using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextMemberSelectionQueryTests
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
    public async Task ExecuteType_ResolvesExactStructuredIdentity()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);
        AssemblyContextParticipant participant = Assert.Single(
            group.Participants);
        ApiType expected = ProbeType(group, participant);

        ApiType selected = Available(
            AssemblyContextMemberSelectionQuery.ExecuteType(
                group,
                participant,
                expected.DefinitionName!.ToEscapedFullName(),
                Limits));

        Assert.Equal(expected.DefinitionName, selected.DefinitionName);
        Assert.Equal(expected.FullName, selected.FullName);
    }

    [Fact]
    public async Task ExecuteDeclaration_FallsBackFromImageLocalTokenToSelector()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);
        AssemblyContextParticipant participant = Assert.Single(
            group.Participants);
        ApiSurface surface = Available(
                AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    participant,
                    ApiSurfaceScope.IncludeAll))
            .Surface;
        ApiType type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.FullName == typeof(MemberSelectionProbe).FullName);
        ApiMember selected = Assert.Single(
            type.Members,
            member => member.Name == nameof(MemberSelectionProbe.Target));
        ApiMember other = Assert.Single(
            type.Members,
            member => member.Name == nameof(MemberSelectionProbe.Other));
        string selector =
            CallGraphMemberResolver.CreateSelector(type, selected).Key;

        AssemblyContextMemberDeclaration result = Available(
            AssemblyContextMemberSelectionQuery.ExecuteDeclaration(
                group,
                participant,
                new(
                    type.DefinitionName!.ToEscapedFullName(),
                    selected.Name,
                    selector,
                    other.MetadataToken),
                Limits));

        Assert.Equal(selected.MetadataToken, result.Member.MetadataToken);
    }

    [Fact]
    public async Task ExecuteDeclaration_FallsBackFromSameNameTokenCollisionToSelector()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);
        AssemblyContextParticipant participant = Assert.Single(
            group.Participants);
        ApiSurface surface = Available(
                AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    participant,
                    ApiSurfaceScope.IncludeAll))
            .Surface;
        ApiType type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.FullName == typeof(MemberSelectionProbe).FullName);
        ApiMember selected = Assert.Single(
            type.Members,
            member =>
                member.Name == nameof(MemberSelectionProbe.Overloaded)
                && member.Signature == "int Overloaded(int value)");
        ApiMember collision = Assert.Single(
            type.Members,
            member =>
                member.Name == nameof(MemberSelectionProbe.Overloaded)
                && member.Signature == "string Overloaded(string value)");
        string selector =
            CallGraphMemberResolver.CreateSelector(type, selected).Key;

        AssemblyContextMemberDeclaration result = Available(
            AssemblyContextMemberSelectionQuery.ExecuteDeclaration(
                group,
                participant,
                new(
                    type.DefinitionName!.ToEscapedFullName(),
                    selected.Name,
                    selector,
                    collision.MetadataToken),
                Limits));

        Assert.Equal(selected.MetadataToken, result.Member.MetadataToken);
    }

    [Fact]
    public async Task ExecuteBody_FallsBackFromImageLocalTokenToSelector()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);
        AssemblyContextParticipant participant = Assert.Single(
            group.Participants);
        ApiType type = ProbeType(group, participant);
        ApiMember selected = Assert.Single(
            type.Members,
            member => member.Name == nameof(MemberSelectionProbe.Target));
        ApiMember other = Assert.Single(
            type.Members,
            member => member.Name == nameof(MemberSelectionProbe.Other));
        string selector =
            CallGraphMemberResolver.CreateSelector(type, selected).Key;

        AssemblyContextMemberBody result = Available(
            AssemblyContextMemberSelectionQuery.ExecuteBody(
                group,
                participant,
                new(
                    type.DefinitionName!.ToEscapedFullName(),
                    selected.Name,
                    selector,
                    other.MetadataToken),
                Limits));

        Assert.Equal(selected.MetadataToken, result.BodyToken);
        Assert.Equal(selected.MetadataToken, result.Member.MetadataToken);
    }

    [Fact]
    public async Task SelectBody_ReusesCallerProjectedSurface()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);
        AssemblyContextParticipant participant = Assert.Single(
            group.Participants);
        ApiSurface surface = Available(
                AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                    group,
                    participant,
                    ApiSurfaceScope.IncludeAll))
            .Surface;
        ApiType type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.FullName == typeof(MemberSelectionProbe).FullName);
        ApiMember selected = Assert.Single(
            type.Members,
            member => member.Name == nameof(MemberSelectionProbe.Target));
        string selector =
            CallGraphMemberResolver.CreateSelector(type, selected).Key;

        AssemblyContextMemberBody result =
            ApiSurfaceMemberSelection.SelectBody(
                surface,
                new(
                    type.DefinitionName!.ToEscapedFullName(),
                    selected.Name,
                    selector,
                    selected.MetadataToken));

        Assert.Same(type, result.Type);
        Assert.Same(selected, result.Member);
    }

    [Fact]
    public async Task ExecuteBody_FailsVisiblyWhenBoundsCannotRetainParticipant()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);
        AssemblyContextParticipant participant = Assert.Single(
            group.Participants);

        var failed = Assert.IsType<
            AssemblyContextEntry<AssemblyContextMemberBody>.Failed>(
                AssemblyContextMemberSelectionQuery.ExecuteBody(
                    group,
                    participant,
                    new(
                        typeof(MemberSelectionProbe).FullName!,
                        nameof(MemberSelectionProbe.Target),
                        "opaque"),
                    new(
                        maxParticipants: 1,
                        maxTypes: 1,
                        maxMembers: 1,
                        maxInspectionFailures: 0,
                        maxTypeForwarders: 0,
                        maxMetadataRows: 1,
                        maxRetainedTextCharacters: 1)));

        Assert.Contains("bound", failed.Error.Message, StringComparison.Ordinal);
    }

    static TValue Available<TValue>(AssemblyContextEntry<TValue> entry) =>
        Assert.IsType<AssemblyContextEntry<TValue>.Available>(entry).Value;

    static ApiType ProbeType(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        AssemblyApiSurface surface = Available(
            AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                group,
                participant,
                ApiSurfaceScope.IncludeAll));
        return Assert.Single(
            surface.Surface.Types,
            type => type.FullName == typeof(MemberSelectionProbe).FullName);
    }

    static AssemblyContextGroup SelfGroup(InspectionWorkspace workspace) =>
        workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        typeof(AssemblyContextMemberSelectionQueryTests)
                            .Assembly.Location,
                        AssemblyResolutionProvenance.Local(
                            "member selection tests")),
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
}

public sealed class MemberSelectionProbe
{
    public static int Target(int value) => value;

    public static int Other(int value) => value;

    public static int Overloaded(int value) => value;

    public static string Overloaded(string value) => value;
}
