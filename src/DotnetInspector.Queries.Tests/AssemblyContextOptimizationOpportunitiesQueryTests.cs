using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextOptimizationOpportunitiesQueryTests
{
    [Fact]
    public void Execute_RanksCompiledOpportunitiesAndAttributesPublicBodies()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy);

        AssemblyContextOptimizationOpportunitiesResult result =
            Execute(group);

        Assert.True(result.IsComplete);
        AssemblyContextOptimizationOpportunityMember member =
            Assert.Single(
                result.RankedMembers,
                candidate =>
                    candidate.Member.Ranking.Method.Name
                    == nameof(ResearchProjectionProbe.BoxInt));
        Assert.Contains(
            member.Member.Ranking.Opportunities,
            opportunity =>
                opportunity.Shape == "box-value-type");
        OptimizationOpportunityPublicMember publicMember =
            Assert.IsType<OptimizationOpportunityPublicMember>(
                member.Member.PublicMember);
        Assert.Equal(
            typeof(ResearchProjectionProbe).FullName,
            publicMember.Type);
        Assert.Equal(
            nameof(ResearchProjectionProbe.BoxInt),
            publicMember.Member);
        Assert.StartsWith(
            $"{nameof(ResearchProjectionProbe.BoxInt)}~",
            publicMember.StableSelector);
        Assert.Equal(
            typeof(ResearchProjectionProbe)
                .GetMethod(nameof(ResearchProjectionProbe.BoxInt))!
                .MetadataToken,
            publicMember.BodyToken);
        Assert.Equal(
            [
                .. OptimizationOpportunityRanking.OrderMembers(
                    result.RankedMembers.Select(
                        candidate =>
                            candidate.Member.Ranking)),
            ],
            result.RankedMembers.Select(
                candidate => candidate.Member.Ranking));
    }

    [Fact]
    public void Execute_AttributesLiftedAccessorAndNestedBodiesToPublicOwners()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy);

        AssemblyContextOptimizationOpportunitiesResult result =
            Execute(group);

        AssemblyContextOptimizationOpportunityMember lifted =
            Assert.Single(
                result.RankedMembers,
                candidate =>
                    candidate.Member.Ranking.Method.Name
                    == nameof(
                        ResearchProjectionProbe
                            .GenericObjectEqualsInLocal));
        Assert.Contains(
            lifted.Member.Ranking.Opportunities,
            opportunity =>
                opportunity.Method.MetadataToken
                != lifted.Member.Ranking.Method.MetadataToken);
        Assert.Equal(
            nameof(
                ResearchProjectionProbe
                    .GenericObjectEqualsInLocal),
            Assert.IsType<OptimizationOpportunityPublicMember>(
                    lifted.Member.PublicMember)
                .Member);

        AssemblyContextOptimizationOpportunityMember accessor =
            Assert.Single(
                result.RankedMembers,
                candidate =>
                    candidate.Member.PublicMember?.Member
                    == nameof(ResearchProjectionProbe.BoxedValue));
        OptimizationOpportunityPublicMember property =
            accessor.Member.PublicMember!;
        Assert.Equal(
            typeof(ResearchProjectionProbe)
                .GetProperty(
                    nameof(ResearchProjectionProbe.BoxedValue))!
                .GetMethod!
                .MetadataToken,
            property.BodyToken);
        Assert.StartsWith(
            $"{nameof(ResearchProjectionProbe.BoxedValue)}~",
            property.StableSelector);

        OptimizationOpportunityPublicMember nested =
            Assert.Single(
                result.RankedMembers,
                candidate =>
                    candidate.Member.PublicMember?.Member
                    == nameof(
                        ResearchProjectionProbe.Nested.BoxNested))
                .Member
                .PublicMember!;
        Assert.Equal(
            $"{typeof(ResearchProjectionProbe).FullName}+Nested",
            nested.Type);
    }

    [Fact]
    public void Execute_ResolvesThroughTheParticipantBindingPolicy()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        Execute(group);

        Assert.NotEmpty(policy.Requests);
        Assert.All(
            policy.Requests,
            request => Assert.Same(
                participant.Assembly.Registration,
                Assert.IsType<
                    AssemblyBindingOrigin.RequestingAssembly>(
                        request.Origin)
                    .Registration));
    }

    [Fact]
    public void Execute_CarriesRejectedParticipantBesideAvailableRanking()
    {
        ImmutableArray<byte> image = SelfImage();
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
            [
                Participant(
                    image,
                    ContentIdentity(image) with
                    {
                        Name = "WrongIdentity",
                    },
                    policy),
                Participant(
                    image,
                    ContentIdentity(image),
                    policy),
            ]);

        AssemblyContextOptimizationOpportunitiesResult result =
            Execute(group);

        Assert.False(result.IsComplete);
        var rejected = Assert.IsType<
            AssemblyContextEntry<
                AssemblyOptimizationOpportunityRanking>.Rejected>(
                    result.Assemblies.Assemblies[0]);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.IsType<
            AssemblyContextEntry<
                AssemblyOptimizationOpportunityRanking>.Available>(
                    result.Assemblies.Assemblies[1]);
        Assert.All(
            result.RankedMembers,
            member => Assert.Same(
                group.Participants[1].Assembly.Registration,
                member.Subject.Registration));
    }

    [Fact]
    public void Definition_IsUnboundedAndRunsWithoutAHostOwnedIndex()
    {
        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextOptimizationOpportunitiesQuery.Definition,
                    AssemblyContextOptimizationOpportunitiesQuery.Execute);

        Assert.Equal(
            InspectionCost.Unbounded,
            AssemblyContextOptimizationOpportunitiesQuery
                .Definition.Cost);
        Assert.Equal(
            [],
            registry.RequirementsOf(
                AssemblyContextOptimizationOpportunitiesQuery
                    .Definition));
    }

    static AssemblyContextOptimizationOpportunitiesResult Execute(
        AssemblyContextGroup group)
    {
        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextOptimizationOpportunitiesQuery.Definition,
                    AssemblyContextOptimizationOpportunitiesQuery.Execute);
        return registry.Run(
                [
                    AssemblyContextOptimizationOpportunitiesQuery
                        .Definition,
                ],
                group)
            .Get(
                AssemblyContextOptimizationOpportunitiesQuery
                    .Definition);
    }

    static AssemblyContextGroup ContentGroup(
        InspectionWorkspace workspace,
        IAssemblyBindingPolicy policy)
    {
        ImmutableArray<byte> image = SelfImage();
        return workspace.CreateAssemblyContextGroup(
            [
                Participant(
                    image,
                    ContentIdentity(image),
                    policy),
            ]);
    }

    static AssemblyContextParticipant Participant(
        ImmutableArray<byte> image,
        AssemblyReferenceIdentity identity,
        IAssemblyBindingPolicy policy) =>
        new(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "ranking-probe",
                    "1.0.0",
                    "net11.0",
                    rid: null)),
            policy);

    static ImmutableArray<byte> SelfImage() =>
        ImmutableCollectionsMarshal.AsImmutableArray(
            File.ReadAllBytes(
                typeof(
                    AssemblyContextOptimizationOpportunitiesQueryTests)
                    .Assembly.Location));

    static AssemblyReferenceIdentity ContentIdentity(
        ImmutableArray<byte> image)
    {
        using var reader = new PEReader(image);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    sealed class RecordingBindingPolicy : IAssemblyBindingPolicy
    {
        readonly List<AssemblyBindingRequest> _requests = [];

        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        internal IReadOnlyList<AssemblyBindingRequest> Requests =>
            _requests;

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            _requests.Add(request);
            return AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind
                        .CandidateUnavailable));
        }
    }
}
