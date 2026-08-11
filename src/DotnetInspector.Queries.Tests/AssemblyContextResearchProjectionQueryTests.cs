using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Gates the group-scoped Research projection queries: they project from workspace-owned content
/// with no filesystem path, address an exact <c>MethodDef</c>, carry the whole-assembly fact
/// context path-keyed resolution cannot reach, resolve references through the participant's own
/// binding policy, and report participant failure as a typed entry.
/// </summary>
public sealed class AssemblyContextResearchProjectionQueryTests
{
    [Fact]
    public void TypeProjection_ProjectsFromContentWithoutAFilesystemPath()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        Assert.Null(Assert.Single(group.Participants).Assembly.Path);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!));

        ResearchViews.TypeProjectionResult projection = Available(result);
        Assert.Equal(typeof(ResearchProjectionProbe).FullName, projection.Identity.FullName);
        Assert.Equal("class", projection.Identity.Kind);
        Assert.NotNull(projection.Composition);
        Assert.True(projection.Composition!.Methods > 0);
    }

    [Fact]
    public void MemberProjection_ProducesAnAnnotatedSourceDocumentFromContent()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        Assert.Null(projection.Projection.SourceDocumentFailure);
        Assert.Null(projection.ContextLimitation);
        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.NotEmpty(document.Text);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.CSharp);
        Assert.Contains(document.Nodes, node => node.Medium == SourceLineKind.Il);
    }

    [Fact]
    public void MemberProjection_CarriesTheWholeAssemblyFactContext()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        // Boxing is an assembly-scoped Analysis fact. Path-keyed resolution cannot reach a
        // snapshot, so its presence proves the query supplied the context rather than letting
        // the projection observe a consistent absence.
        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.Contains(document.Facts, fact => fact.Descriptor == "alloc.box");
    }

    [Fact]
    public void MemberProjection_MethodTokenAddressesTheExactOverload()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        int token = typeof(ResearchProjectionProbe)
            .GetMethod(
                nameof(ResearchProjectionProbe.Overloaded),
                BindingFlags.Public | BindingFlags.Static,
                [typeof(string)])!
            .MetadataToken;

        AssemblyMemberProjection projection = Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.Overloaded)) with
                {
                    MethodToken = token,
                }));

        AnnotatedSourceDocument document =
            Assert.IsType<AnnotatedSourceDocument>(projection.Projection.SourceDocument);
        Assert.Contains("string", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Overloaded(int", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_ResolvesReferencesThroughTheParticipantBindingPolicy()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);

        Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        // Reference resolution is a policy question, not a name match: every request the
        // projection made names the participant it came from, so a sibling is selected by the
        // group's binding snapshot rather than by a matching simple name.
        Assert.NotEmpty(policy.Requests);
        Assert.All(
            policy.Requests,
            request => Assert.Same(
                participant.Assembly.Registration,
                Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(request.Origin)
                    .Registration));
    }

    [Fact]
    public void Projection_DoesNotAcquireAPolicySelectionOutsideTheGroup()
    {
        ImmutableArray<byte> image = SelfImage();
        int outsiderOpens = 0;
        ResolvedAssemblyReference outsider = ResolvedAssemblyReference.Create(
            ContentIdentity(image),
            path: null,
            () =>
            {
                outsiderOpens++;
                return new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false);
            },
            AssemblyResolutionProvenance.Package(
                "outsider",
                "1.0.0",
                "net11.0",
                rid: null));
        var policy = new SelectingBindingPolicy(outsider);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        Available(
            AssemblyContextMemberProjectionQuery.Execute(
                group,
                Request(nameof(ResearchProjectionProbe.BoxInt))));

        Assert.NotEmpty(policy.Requests);
        Assert.Equal(0, outsiderOpens);
    }

    [Fact]
    public void Execute_CarriesRejectedParticipantBesideLaterResultsInGroupOrder()
    {
        ImmutableArray<byte> image = SelfImage();
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                Participant(image, ContentIdentity(image) with { Name = "WrongIdentity" }, policy),
                Participant(image, ContentIdentity(image), policy),
            ]);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!));

        Assert.False(result.IsComplete);
        var rejected = Assert.IsType<
            AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Rejected>(
            result.Assemblies[0]);
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
        Assert.IsType<AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Available>(
            result.Assemblies[1]);
    }

    [Fact]
    public void TypeProjection_ReportsAMissingTypeAsATypedParticipantFailure()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyContextResult<ResearchViews.TypeProjectionResult> result =
            AssemblyContextTypeProjectionQuery.Execute(
                group,
                new AssemblyContextTypeProjectionRequest("No.Such.Type"));

        var failed = Assert.IsType<
            AssemblyContextEntry<ResearchViews.TypeProjectionResult>.Failed>(
            Assert.Single(result.Assemblies));
        Assert.Contains("No.Such.Type", failed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteParticipant_RefusesAParticipantOutsideTheGroup()
    {
        ImmutableArray<byte> image = SelfImage();
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);
        AssemblyContextParticipant outsider =
            Participant(image, ContentIdentity(image), policy);

        Assert.Throws<ArgumentException>(
            () => AssemblyContextTypeProjectionQuery.ExecuteParticipant(
                group,
                outsider,
                new AssemblyContextTypeProjectionRequest(
                    typeof(ResearchProjectionProbe).FullName!)));
    }

    [Fact]
    public void ExecuteParticipant_ProjectsOnlyTheRequestedParticipant()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = ContentGroup(workspace, policy);

        AssemblyContextEntry<AssemblyMemberProjection> entry =
            AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                group,
                Assert.Single(group.Participants),
                Request(nameof(ResearchProjectionProbe.BoxInt)));

        var available =
            Assert.IsType<AssemblyContextEntry<AssemblyMemberProjection>.Available>(entry);
        Assert.IsType<AnnotatedSourceDocument>(
            available.Value.Projection.SourceDocument);
    }

    static AssemblyContextMemberProjectionRequest Request(string member) =>
        new(
            typeof(ResearchProjectionProbe).FullName!,
            member,
            SourceDocument: true);

    static AssemblyContextGroup ContentGroup(
        InspectionWorkspace workspace,
        IAssemblyBindingPolicy policy)
    {
        ImmutableArray<byte> image = SelfImage();
        return workspace.CreateAssemblyContextGroup(
            [Participant(image, ContentIdentity(image), policy)]);
    }

    static AssemblyContextParticipant Participant(
        ImmutableArray<byte> image,
        AssemblyReferenceIdentity identity,
        IAssemblyBindingPolicy policy)
        => new(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Package("probe", "1.0.0", "net11.0", rid: null)),
            policy);

    static ImmutableArray<byte> SelfImage() =>
        ImmutableCollectionsMarshal.AsImmutableArray(
            File.ReadAllBytes(
                typeof(AssemblyContextResearchProjectionQueryTests).Assembly.Location));

    static AssemblyReferenceIdentity ContentIdentity(ImmutableArray<byte> image)
    {
        using var reader = new PEReader(image);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
    }

    static TValue Available<TValue>(AssemblyContextResult<TValue> result)
        => Assert.IsType<AssemblyContextEntry<TValue>.Available>(
                Assert.Single(result.Assemblies))
            .Value;

    sealed class RecordingBindingPolicy : IAssemblyBindingPolicy
    {
        readonly List<AssemblyBindingRequest> _requests = [];

        public AssemblyBindingPolicyVersion Version { get; } = new();

        internal IReadOnlyList<AssemblyBindingRequest> Requests => _requests;

        public AssemblyBindingSelection Select(AssemblyBindingRequest request)
        {
            _requests.Add(request);
            return AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }

    sealed class SelectingBindingPolicy(ResolvedAssemblyReference selection)
        : IAssemblyBindingPolicy
    {
        readonly List<AssemblyBindingRequest> _requests = [];

        public AssemblyBindingPolicyVersion Version { get; } = new();

        internal IReadOnlyList<AssemblyBindingRequest> Requests => _requests;

        public AssemblyBindingSelection Select(AssemblyBindingRequest request)
        {
            _requests.Add(request);
            return AssemblyBindingSelection.Found(selection);
        }
    }
}

/// <summary>Probe members the group-scoped Research projections address.</summary>
public static class ResearchProjectionProbe
{
    public static object BoxInt(int value) => value;

    public static int Overloaded(int value) => value + 1;

    public static int Overloaded(string value) => value.Length;
}
