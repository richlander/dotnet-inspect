using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
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
            [
                typeof(ResearchProjectionProbe)
                    .GetMethod(nameof(ResearchProjectionProbe.BoxInt))!
                    .MetadataToken,
            ],
            publicMember.BodyTokens);
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
        Assert.Equal(
            lifted.Member.Ranking.Opportunities
                .Select(opportunity =>
                    opportunity.Method.MetadataToken)
                .Distinct()
                .Order(),
            lifted.Member.PublicMember!.BodyTokens);

        AssemblyContextOptimizationOpportunityMember accessor =
            Assert.Single(
                result.RankedMembers,
                candidate =>
                    candidate.Member.PublicMember?.Member
                    == nameof(ResearchProjectionProbe.BoxedValue));
        OptimizationOpportunityPublicMember property =
            accessor.Member.PublicMember!;
        Assert.Equal(
            [
                typeof(ResearchProjectionProbe)
                    .GetProperty(
                        nameof(ResearchProjectionProbe.BoxedValue))!
                    .GetMethod!
                    .MetadataToken,
            ],
            property.BodyTokens);
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
    public void Execute_AggregatesAllAccessorBodiesUnderOnePublicMember()
    {
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            ContentGroup(workspace, policy);

        AssemblyContextOptimizationOpportunitiesResult result =
            Execute(group);

        AssemblyContextOptimizationOpportunityMember accessor =
            Assert.Single(
                result.RankedMembers,
                candidate =>
                    candidate.Member.PublicMember?.Member
                    == nameof(
                        ResearchProjectionProbe
                            .AccessorBoxedValue));
        PropertyInfo property = typeof(ResearchProjectionProbe)
            .GetProperty(
                nameof(
                    ResearchProjectionProbe
                        .AccessorBoxedValue))!;
        Assert.Equal(
            [
                property.GetMethod!.MetadataToken,
                property.SetMethod!.MetadataToken,
            ],
            accessor.Member.PublicMember!.BodyTokens);
        Assert.Contains(
            accessor.Member.Ranking.Opportunities,
            opportunity =>
                opportunity.Method.MetadataToken
                == property.GetMethod.MetadataToken);
        Assert.Contains(
            accessor.Member.Ranking.Opportunities,
            opportunity =>
                opportunity.Method.MetadataToken
                == property.SetMethod.MetadataToken);
    }

    [Fact]
    public void Execute_ReportsPhysicalAsyncEvidenceBodyToken()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    typeof(ClassicAsyncSiblingFixture)
                        .Assembly.Location));
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
            [
                Participant(
                    image,
                    ContentIdentity(image),
                    policy),
            ]);

        AssemblyContextOptimizationOpportunitiesResult result =
            Execute(group);

        AssemblyContextOptimizationOpportunityMember member =
            Assert.Single(
                result.RankedMembers,
                candidate =>
                    candidate.Member.PublicMember?.Member
                    == nameof(
                        ClassicAsyncSiblingFixture
                            .CallsSyncSiblingFromAsync));
        OptimizationOpportunity opportunity =
            Assert.Single(
                member.Member.Ranking.Opportunities,
                candidate =>
                    candidate.Shape == "sync-call-in-async");
        int evidenceToken =
            Assert.IsType<int>(opportunity.EvidenceMethodToken);
        int kickoffToken = typeof(ClassicAsyncSiblingFixture)
            .GetMethod(
                nameof(
                    ClassicAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync))!
            .MetadataToken;

        Assert.NotEqual(kickoffToken, evidenceToken);
        Assert.DoesNotContain(
            kickoffToken,
            member.Member.PublicMember!.BodyTokens);
        Assert.Equal(
            [
                .. member.Member.Ranking.Opportunities
                    .Select(candidate =>
                        candidate.EvidenceMethodToken
                        ?? candidate.Method.MetadataToken)
                    .Distinct()
                    .Order(),
            ],
            member.Member.PublicMember.BodyTokens);
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
    public void Execute_RanksAcrossTwoAvailableParticipants()
    {
        ImmutableArray<byte> firstImage = SelfImage();
        ImmutableArray<byte> secondImage =
        [
            .. File.ReadAllBytes(
                typeof(OptimizationOpportunityRanking)
                    .Assembly
                    .Location),
        ];
        var policy = new RecordingBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
            [
                Participant(
                    firstImage,
                    ContentIdentity(firstImage),
                    policy),
                Participant(
                    secondImage,
                    ContentIdentity(secondImage),
                    policy),
            ]);

        AssemblyContextOptimizationOpportunitiesResult result =
            Execute(group);

        Assert.True(result.IsComplete);
        Assert.All(
            result.Assemblies.Assemblies,
            entry => Assert.IsType<
                AssemblyContextEntry<
                    AssemblyOptimizationOpportunityRanking>.Available>(
                        entry));
        Assert.Equal(
            group.Participants
                .Select(participant =>
                    participant.Assembly.Registration)
                .ToHashSet(),
            result.RankedMembers
                .Select(member => member.Subject.Registration)
                .ToHashSet());
        Assert.Equal(
            [
                .. OptimizationOpportunityRanking.OrderMembers(
                    result.RankedMembers.Select(
                        member => member.Member.Ranking)),
            ],
            result.RankedMembers.Select(
                member => member.Member.Ranking));
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

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
            {
                _requests.Add(request);
                return AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .CandidateUnavailable));

            }
        }
    }
}
