using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Presentation.Tests;

public sealed class CloneCandidatePresentationTests
{
    [Fact]
    public async Task Create_ProjectsGlobalRowsAndExactParticipants()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        WorkspaceStructuralCloneSearchResult query =
            fixture.Execute(
                fixture.Snapshot(includeDuplicate: true),
                new StructuralCloneSearchSeed.Library(),
                limits: new WorkspaceStructuralCloneSearchLimits(
                    MaximumResults: 12));

        CloneCandidateDocument first = Available(
            CloneCandidatePresentation.Create(query));
        CloneCandidateDocument second = Available(
            CloneCandidatePresentation.Create(query));

        Assert.Equal(CloneCandidateDocument.SectionName, "Clone Candidates");
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(CloneCandidateSeedKind.Library, first.Seed.Kind);
        Assert.Equal(
            StructuralCloneCandidateBreadth.Everything,
            first.Breadth);
        Assert.Equal(
            StructuralCloneCandidateDiscovery.SimilarNames,
            first.Discovery);
        Assert.NotEmpty(first.Rows);
        Assert.Equal(
            Enumerable.Range(1, first.Rows.Length),
            first.Rows.Select(row => row.Rank));
        Assert.Equal(first.Receipt.ReturnedPairs, first.Rows.Length);
        Assert.Contains(
            first.Rows,
            row =>
                row.Left.Participant.Ordinal
                != row.Right.Participant.Ordinal);

        CloneCandidateRow crossParticipant =
            Assert.Single(
                first.Rows.Where(
                    row => row.Left.Participant.Ordinal
                        != row.Right.Participant.Ordinal).Take(1),
                row => row.Left.Participant.Ordinal
                    != row.Right.Participant.Ordinal);
        Assert.Equal(
            crossParticipant.Left.Participant.Assembly,
            crossParticipant.Right.Participant.Assembly);
        Assert.Equal(
            crossParticipant.Left.ModuleVersionId,
            crossParticipant.Right.ModuleVersionId);
        Assert.NotEqual(
            crossParticipant.Left.Participant,
            crossParticipant.Right.Participant);
        Assert.Contains(
            ":0x06",
            crossParticipant.Left.AddressDisplay.ToString(),
            StringComparison.Ordinal);
        Assert.NotNull(crossParticipant.NameQualification);
        Assert.InRange(crossParticipant.Similarity.Score, 0, 10_000);
    }

    [Fact]
    public async Task Create_PreservesLogicalMemberSeedCoverage()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        MemberAnchor property = fixture.LogicalProperty("Value");

        CloneCandidateDocument document =
            Available(
                CloneCandidatePresentation.Create(
                    fixture.Execute(
                        fixture.Snapshot(),
                        new StructuralCloneSearchSeed.Member(
                            TypeName("Cases.Widget"),
                            property),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All)));

        Assert.Equal(CloneCandidateSeedKind.Member, document.Seed.Kind);
        Assert.Equal(property, document.Seed.Member);
        Assert.Equal(2, document.Seeds.Length);
        Assert.All(
            document.Seeds,
            seed => Assert.True(seed.IsComplete));
        Assert.Contains(
            document.Rows,
            row => row.Left.MethodDefinitionToken
                != row.Right.MethodDefinitionToken);

        CloneCandidateDocument limited =
            Available(
                CloneCandidatePresentation.Create(
                    fixture.Execute(
                        fixture.Snapshot(),
                        new StructuralCloneSearchSeed.Member(
                            TypeName("Cases.Widget"),
                            property),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            ComparisonLimits:
                                new StructuralCloneComparisonLimits(
                                    MaximumInstructions: 1)))));
        Assert.False(limited.CoverageIsComplete);
        Assert.Contains(
            limited.Seeds,
            seed => !seed.IsComplete && !seed.Blockers.IsEmpty);
    }

    [Fact]
    public async Task Create_SeparatesSuppressionFromIncompleteCoverage()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        var limits =
            new WorkspaceStructuralCloneSearchLimits(MaximumResults: 1);

        CloneCandidateDocument complete =
            Available(
                CloneCandidatePresentation.Create(
                    fixture.Execute(
                        fixture.Snapshot(includeDuplicate: true),
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All,
                        limits)));
        CloneCandidateDocument incomplete =
            Available(
                CloneCandidatePresentation.Create(
                    fixture.Execute(
                        fixture.Snapshot(includeUnreadable: true),
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All,
                        limits)));

        Assert.True(complete.CoverageIsComplete);
        Assert.True(complete.ResultLimitReached);
        Assert.True(complete.ResultLimitOmittedPairs > 0);
        CloneCandidateLibraryCoverage breadthExcluded =
            Assert.Single(
                complete.Libraries,
                library => !library.Admitted);
        Assert.True(breadthExcluded.IsComplete);
        Assert.False(incomplete.CoverageIsComplete);
        Assert.True(incomplete.ResultLimitReached);
        Assert.True(incomplete.ResultLimitOmittedPairs > 0);
        CloneCandidateLibraryCoverage failedLibrary =
            Assert.Single(
                incomplete.Libraries,
                library => !library.Failures.IsEmpty);
        Assert.False(failedLibrary.IsComplete);
        Assert.All(
            failedLibrary.Failures,
            failure => Assert.NotEqual(default, failure.Detail));
    }

    [Fact]
    public async Task Create_PreservesFailedAndRejectedOutcomes()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        CloneCandidatePresentationResult failed =
            CloneCandidatePresentation.Create(
                fixture.Execute(
                    fixture.Snapshot(),
                    new StructuralCloneSearchSeed.Type(
                        TypeName("Cases.Missing"))));
        CloneCandidatePresentationResult rejected =
            CloneCandidatePresentation.Create(
                fixture.Execute(
                    fixture.InvalidSnapshot(),
                    new StructuralCloneSearchSeed.Library()));

        var failedResult =
            Assert.IsType<CloneCandidatePresentationResult.Failed>(failed);
        Assert.Equal(
            StructuralCloneSearchFailureKind.SeedTypeNotFound,
            failedResult.Failure.Kind);
        Assert.NotEqual(default, failedResult.Failure.Detail);

        var rejectedResult =
            Assert.IsType<
                CloneCandidatePresentationResult.Rejected>(rejected);
        Assert.NotEqual(default, rejectedResult.Detail);
    }

    static CloneCandidateDocument Available(
        CloneCandidatePresentationResult result)
        => Assert.IsType<
            CloneCandidatePresentationResult.Available>(result).Document;

    static MetadataTypeDefinitionName TypeName(string serializedName)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.ParseSerialized(
                    serializedName))
            .Name;

    sealed class Fixture : IAsyncDisposable
    {
        readonly ImmutableArray<byte> _image;
        readonly AssemblyContextGroup _self;
        readonly AssemblyContextGroup _duplicate;
        readonly AssemblyContextGroup _unreadable;
        readonly AssemblyContextGroup _invalidSelf;
        readonly StructuralCloneParticipantEntry _selfEntry;
        readonly StructuralCloneParticipantEntry _duplicateEntry;
        readonly StructuralCloneParticipantEntry _unreadableEntry;
        readonly StructuralCloneParticipantEntry _invalidSelfEntry;

        Fixture(
            InspectionWorkspace workspace,
            WorkspaceScopeRevision revision,
            ImmutableArray<byte> image)
        {
            Workspace = workspace;
            Revision = revision;
            _image = image;
            _self = Group(workspace, image, "clone candidates self");
            _duplicate =
                Group(workspace, image, "clone candidates duplicate");
            ImmutableArray<byte> invalid =
                ImmutableCollectionsMarshal.AsImmutableArray(
                    new byte[] { 0x4D, 0x5A, 0x00, 0x00 });
            _unreadable =
                Group(workspace, invalid, "clone candidates unreadable");
            _invalidSelf =
                Group(workspace, invalid, "clone candidates invalid self");
            _selfEntry =
                Entry(
                    _self,
                    StructuralCloneParticipantMembership
                        .ContainingLibrary);
            _duplicateEntry =
                Entry(
                    _duplicate,
                    StructuralCloneParticipantMembership.Available);
            _unreadableEntry =
                Entry(
                    _unreadable,
                    StructuralCloneParticipantMembership.Available);
            _invalidSelfEntry =
                Entry(
                    _invalidSelf,
                    StructuralCloneParticipantMembership
                        .ContainingLibrary);
        }

        internal InspectionWorkspace Workspace { get; }
        internal WorkspaceScopeRevision Revision { get; }

        internal static async ValueTask<Fixture> CreateAsync()
        {
            InspectionWorkspace workspace =
                InspectionWorkspace.CreateAsynchronous();
            WorkspaceScopeSnapshot snapshot =
                Assert.IsType<WorkspaceScopeReadResult.Available>(
                    await workspace.GetScopeSnapshotAsync())
                .Snapshot;
            ImmutableArray<byte> image =
                ImmutableCollectionsMarshal.AsImmutableArray(
                    File.ReadAllBytes(
                        FixtureCatalog.CloneSearchMembers.AssemblyPath()));
            return new Fixture(workspace, snapshot.Revision, image);
        }

        internal StructuralCloneParticipantSnapshot Snapshot(
            bool includeDuplicate = false,
            bool includeUnreadable = false)
        {
            var entries =
                ImmutableArray.CreateBuilder<
                    StructuralCloneParticipantEntry>();
            entries.Add(_selfEntry);
            if (includeDuplicate)
                entries.Add(_duplicateEntry);
            if (includeUnreadable)
                entries.Add(_unreadableEntry);
            return new(
                Revision,
                Revision,
                entries.ToImmutable());
        }

        internal StructuralCloneParticipantSnapshot InvalidSnapshot()
            => new(Revision, Revision, [_invalidSelfEntry]);

        internal WorkspaceStructuralCloneSearchResult Execute(
            StructuralCloneParticipantSnapshot participants,
            StructuralCloneSearchSeed seed,
            StructuralCloneCandidateBreadth breadth =
                StructuralCloneCandidateBreadth.Everything,
            StructuralCloneCandidateDiscovery discovery =
                StructuralCloneCandidateDiscovery.SimilarNames,
            WorkspaceStructuralCloneSearchLimits? limits = null)
            => WorkspaceStructuralCloneSearchQuery.Execute(
                new WorkspaceStructuralCloneSearchInput(
                    participants,
                    seed,
                    breadth,
                    discovery,
                    limits),
                TestContext.Current.CancellationToken);

        internal MemberAnchor LogicalProperty(string name)
        {
            using var image = new PEReader(_image);
            ApiSurface surface =
                ApiSurfaceExtractor.Extract(image, includeAll: true);
            ApiType type =
                Assert.Single(
                    surface.Types,
                    candidate => candidate.Namespace == "Cases"
                        && candidate.Name == "Widget");
            ApiMember property =
                Assert.Single(
                    type.Members,
                    candidate => candidate.Kind == "property"
                        && candidate.Name == name);
            return ApiMemberIdentity.GetMemberAnchor(type, property);
        }

        public async ValueTask DisposeAsync()
        {
            _self.Dispose();
            _duplicate.Dispose();
            _unreadable.Dispose();
            _invalidSelf.Dispose();
            await Workspace.DisposeAsync();
        }

        static StructuralCloneParticipantEntry Entry(
            AssemblyContextGroup group,
            StructuralCloneParticipantMembership membership)
            => new(group, group.Participants[0], membership);

        static AssemblyContextGroup Group(
            InspectionWorkspace workspace,
            ImmutableArray<byte> image,
            string source)
            => workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        ResolvedAssemblyReference.Create(
                            Identity(image),
                            path: null,
                            () => new MemoryStream(
                                ImmutableCollectionsMarshal
                                    .AsArray(image)!,
                                writable: false),
                            AssemblyResolutionProvenance.Local(source)),
                        new TestBindingPolicy()),
                ]);

        static AssemblyReferenceIdentity Identity(
            ImmutableArray<byte> image)
        {
            try
            {
                using var reader = new PEReader(image);
                return AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader.GetMetadataReader());
            }
            catch (BadImageFormatException)
            {
                return new AssemblyReferenceIdentity(
                    "Unreadable",
                    new Version(1, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null);
            }
        }
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
            => new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
