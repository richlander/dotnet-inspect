using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Outcome-level gates for the host-neutral Workspace structural-clone search.
/// </summary>
/// <remarks>
/// The fixtures are synthetic assemblies so seed populations, decoded names,
/// and body shapes are exact. Every method carries the same
/// <c>static void()</c> signature, so Analysis ranks the whole population and
/// the assertions observe search behavior rather than scoring calibration.
/// </remarks>
public sealed class WorkspaceStructuralCloneSearchQueryTests
{
    const string SelfAssembly = "CloneSearchSelf";
    const string EcosystemAssembly = "CloneSearchEcosystem";
    const string AvailableAssembly = "CloneSearchAvailable";
    const string CrossSeedAssembly = "CloneSearchCrossSeed";
    const string CrossSeedCandidateAssembly = "CloneSearchCrossSeedPeer";
    const string RepeatedNamesAssembly = "CloneSearchRepeatedNames";
    const int RepeatedNameTypes = 4;
    const int RepeatedNameMethods = 8;

    [Fact]
    public async Task Request_DefaultsToEverythingPlusSimilarNames()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        var input =
            new WorkspaceStructuralCloneSearchInput(
                fixture.Snapshot(),
                new StructuralCloneSearchSeed.Library());

        Assert.Equal(
            StructuralCloneCandidateBreadth.Everything,
            input.Breadth);
        Assert.Equal(
            StructuralCloneCandidateDiscovery.SimilarNames,
            input.Discovery);
        Assert.Equal(
            0.6,
            WorkspaceStructuralCloneSearchQuery.NameSimilarityThreshold);
        Assert.Equal(
            InspectionCost.Unbounded,
            WorkspaceStructuralCloneSearchQuery.Definition.Cost);
    }

    [Fact]
    public async Task Snapshot_RejectsInconsistentOrDuplicateMembership()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        WorkspaceScopeRevision revision = fixture.Revision;

        Assert.Throws<ArgumentException>(
            () => new StructuralCloneParticipantSnapshot(
                revision,
                revision,
                [fixture.SelfEntry, fixture.SelfEntry]));
        Assert.Throws<ArgumentException>(
            () => new StructuralCloneParticipantSnapshot(
                revision,
                revision,
                [
                    fixture.SelfEntry,
                    new StructuralCloneParticipantEntry(
                        fixture.SelfGroup,
                        fixture.SelfParticipant,
                        StructuralCloneParticipantMembership.Available),
                ]));
        Assert.Throws<ArgumentException>(
            () => new StructuralCloneParticipantSnapshot(
                revision,
                revision,
                [
                    fixture.SelfEntry,
                    new StructuralCloneParticipantEntry(
                        fixture.EcosystemGroup,
                        fixture.EcosystemParticipant,
                        StructuralCloneParticipantMembership
                            .ContainingLibrary),
                ]));
        Assert.Throws<ArgumentException>(
            () => new StructuralCloneParticipantSnapshot(
                revision,
                revision,
                [fixture.EcosystemEntry]));
        Assert.Throws<ArgumentException>(
            () => new StructuralCloneParticipantEntry(
                fixture.SelfGroup,
                fixture.EcosystemParticipant,
                StructuralCloneParticipantMembership.Available));

        await using var foreign = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeRevision foreignRevision =
            (await ScopeSnapshot(foreign)).Revision;
        Assert.Throws<ArgumentException>(
            () => new StructuralCloneParticipantSnapshot(
                revision,
                foreignRevision,
                [fixture.SelfEntry]));
    }

    [Fact]
    public async Task Execute_BindsRevisionsAndParticipantSnapshotIdentity()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        WorkspaceScopeRevision starting = fixture.Revision;
        WorkspaceScopeRevision effective =
            await fixture.CommitFreshRevisionAsync();
        Assert.NotSame(starting.Identity, effective.Identity);
        StructuralCloneParticipantSnapshot snapshot =
            new(
                starting,
                effective,
                [fixture.SelfEntry, fixture.EcosystemEntry]);

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        new StructuralCloneSearchSeed.Library())));

        Assert.Same(starting, result.StartingRevision);
        Assert.Same(effective, result.EffectiveRevision);
        Assert.Same(snapshot.Identity, result.ParticipantSnapshot);
        Assert.Equal(100, result.Limits.MaximumResults);
        Assert.Equal(
            WorkspaceStructuralCloneSearchQuery.NameSimilarityThreshold,
            result.NameSimilarityThreshold);
        Assert.Same(
            fixture.SelfParticipant.Assembly.Registration,
            result.SeedSubject.Registration);
        Assert.True(result.CoverageIsComplete);
    }

    [Fact]
    public async Task Execute_ExpandsLibraryTypeAndMemberSeedPopulations()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        Assert.Equal(
            6,
            SeedCount(fixture, new StructuralCloneSearchSeed.Library()));
        Assert.Equal(
            5,
            SeedCount(
                fixture,
                new StructuralCloneSearchSeed.Type(
                    TypeName("N.Alpha"))));
        Assert.Equal(
            1,
            SeedCount(fixture, fixture.MemberSeed("Compute0")));
    }

    [Theory]
    [InlineData(StructuralCloneCandidateBreadth.Self, 1, 6)]
    [InlineData(
        StructuralCloneCandidateBreadth.SelfAndRegisteredEcosystems,
        2,
        7)]
    [InlineData(StructuralCloneCandidateBreadth.Everything, 3, 8)]
    public async Task Execute_BreadthSelectsOnlySuppliedMembership(
        StructuralCloneCandidateBreadth breadth,
        int expectedLibraries,
        int expectedCandidateMethods)
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        breadth,
                        StructuralCloneCandidateDiscovery.All)));

        Assert.Equal(breadth, result.Breadth);
        Assert.Equal(
            expectedLibraries,
            result.Receipt.AdmittedLibraries);
        Assert.Equal(
            expectedCandidateMethods,
            result.Receipt.CandidateMethods);
        Assert.Equal(3, result.Libraries.Length);
        Assert.Equal(
            expectedLibraries,
            result.Libraries.Count(library => library.Admitted));
        Assert.All(
            result.Libraries,
            library => Assert.True(library.CoverageIsComplete));

        // Every candidate method except the physical seed is ranked, so the
        // middle breadth is observably not degraded to Self.
        Assert.Equal(
            expectedCandidateMethods - 1,
            result.Pairs.Length);
        Assert.True(result.CoverageIsComplete);
    }

    [Theory]
    [InlineData(StructuralCloneCandidateBreadth.Self, 4, 6)]
    [InlineData(
        StructuralCloneCandidateBreadth.SelfAndRegisteredEcosystems,
        5,
        7)]
    [InlineData(StructuralCloneCandidateBreadth.Everything, 6, 8)]
    public async Task Execute_DiscoveryFiltersMethodsWithoutChangingBreadth(
        StructuralCloneCandidateBreadth breadth,
        int similarNames,
        int all)
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available filtered =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        breadth,
                        StructuralCloneCandidateDiscovery.SimilarNames)));
        WorkspaceStructuralCloneSearchResult.Available exhaustive =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        breadth,
                        StructuralCloneCandidateDiscovery.All)));

        Assert.Equal(
            filtered.Receipt.AdmittedLibraries,
            exhaustive.Receipt.AdmittedLibraries);
        Assert.Equal(
            filtered.Receipt.CandidateMethods,
            exhaustive.Receipt.CandidateMethods);
        Assert.Equal(
            similarNames,
            filtered.Receipt.DiscoveredCandidateMethods);
        Assert.Equal(all, exhaustive.Receipt.DiscoveredCandidateMethods);
        Assert.True(filtered.CoverageIsComplete);
        Assert.True(exhaustive.CoverageIsComplete);
    }

    [Fact]
    public async Task Execute_SimilarNamesRequiresTypeAndMemberForOneSeed()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames)));

        // Zulu.Compute4 shares the member name and Alpha.Unrelated shares the
        // declaring type, and neither qualifies on its own.
        Assert.DoesNotContain(
            "Compute4",
            result.Pairs.Select(pair => fixture.SelfName(pair.Right)));
        Assert.DoesNotContain(
            "Unrelated",
            result.Pairs.Select(pair => fixture.SelfName(pair.Right)));
        Assert.Equal(
            ["Compute1", "Compute2", "Compute7"],
            result.Pairs
                .Select(pair => fixture.SelfName(pair.Right))
                .Order(StringComparer.Ordinal));
        Assert.All(
            result.Pairs,
            pair =>
            {
                StructuralCloneNameQualification qualification =
                    Assert.IsType<StructuralCloneNameQualification>(
                        pair.NameQualification);
                Assert.Equal(1.0, qualification.DeclaringTypeSimilarity);
                Assert.True(
                    qualification.MemberSimilarity
                        >= WorkspaceStructuralCloneSearchQuery
                            .NameSimilarityThreshold);
            });

        WorkspaceStructuralCloneSearchResult.Available exhaustive =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All)));

        Assert.Contains(
            "Compute4",
            exhaustive.Pairs.Select(pair => fixture.SelfName(pair.Right)));
        Assert.Contains(
            "Unrelated",
            exhaustive.Pairs.Select(pair => fixture.SelfName(pair.Right)));
        Assert.All(
            exhaustive.Pairs,
            pair => Assert.Null(pair.NameQualification));
    }

    [Fact]
    public async Task Execute_SuppressesSelfPairsAndOppositeOrientations()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        new StructuralCloneSearchSeed.Type(
                            TypeName("N.Alpha")),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumResults: 1000))));

        // Five seeds over six same-library candidates: the ten unordered
        // seed pairs are returned once each, plus one row per seed for the
        // one candidate outside the seed population.
        Assert.Equal(15, result.Pairs.Length);
        Assert.Equal(10, result.Receipt.SuppressedPairs);
        Assert.All(
            result.Pairs,
            pair => Assert.NotEqual(pair.Left, pair.Right));
        Assert.All(
            result.Pairs,
            pair => Assert.True(
                Row(pair.Left.Method) < Row(pair.Right.Method),
                "Same-population pairs keep one deterministic orientation."));

        Assert.Equal(
            result.Pairs.Length,
            result.Pairs
                .Select(pair => (pair.Left.Method, pair.Right.Method))
                .Distinct()
                .Count());
        Assert.True(result.CoverageIsComplete);
        Assert.False(result.Receipt.ResultLimitReached);
    }

    [Fact]
    public async Task Execute_MemberSeedKeepsSelectedSeedOnTheLeft()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute2"),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All)));

        StructuralCloneSearchSeedCoverage seed =
            Assert.Single(result.Seeds);
        Assert.All(
            result.Pairs,
            pair => Assert.Equal(seed.Seed.Method, pair.Left.Method));
        Assert.Contains(
            result.Pairs,
            pair => Row(pair.Right.Method) < Row(pair.Left.Method));
        Assert.Equal(0, result.Receipt.SuppressedPairs);
        Assert.Equal(result.Pairs.Length, seed.RankedPairs);
    }

    [Fact]
    public async Task Execute_KeepsExactEndpointIdentityForEqualContent()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        StructuralCloneParticipantSnapshot snapshot =
            fixture.SnapshotWithDuplicateContent();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All)));

        StructuralCloneSearchPair[] duplicates =
        [
            .. result.Pairs.Where(
                pair => pair.Right.Participant.Ordinal != 0),
        ];
        Assert.Equal(2, duplicates.Length);

        // Equal content, equal MVID, and equal MethodDef token under two
        // distinct acquisition registrations.
        Assert.Equal(
            duplicates[0].Right.Method,
            duplicates[1].Right.Method);
        Assert.NotSame(
            duplicates[0].Right.Subject,
            duplicates[1].Right.Subject);
        Assert.NotSame(
            duplicates[0].Right.Subject.Registration,
            duplicates[1].Right.Subject.Registration);

        // The snapshot's opaque per-entry identity keeps them apart, so pair
        // identity never collapses onto MVID plus token.
        Assert.NotSame(
            duplicates[0].Right.Participant,
            duplicates[1].Right.Participant);
        Assert.NotEqual(
            duplicates[0].Right.Participant.Ordinal,
            duplicates[1].Right.Participant.Ordinal);
        Assert.All(
            duplicates,
            pair => Assert.Same(
                snapshot.Identity,
                pair.Right.Participant.Snapshot));
        Assert.NotEqual(duplicates[0].Right, duplicates[1].Right);
        Assert.NotEqual(duplicates[0], duplicates[1]);
    }

    [Fact]
    public async Task
        Snapshot_OwnsDeterministicSnapshotLocalParticipantIdentities()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        StructuralCloneParticipantSnapshot ordered =
            fixture.SnapshotOf(
                fixture.SelfEntry,
                fixture.EcosystemEntry,
                fixture.AvailableEntry);
        StructuralCloneParticipantSnapshot reordered =
            fixture.SnapshotOf(
                fixture.AvailableEntry,
                fixture.EcosystemEntry,
                fixture.SelfEntry);

        // The snapshot captures its owner-issued entry order once and issues
        // one opaque identity per position; the order is part of the
        // snapshot's exact identity, not a shared global fact.
        Assert.Equal(
            [0, 1, 2],
            ordered.ParticipantIdentities.Select(
                identity => identity.Ordinal));
        Assert.All(
            ordered.ParticipantIdentities,
            identity => Assert.Same(ordered.Identity, identity.Snapshot));
        Assert.Same(
            ordered.ParticipantIdentities[0],
            ordered.ContainingLibraryIdentity);
        Assert.Same(
            reordered.ParticipantIdentities[2],
            reordered.ContainingLibraryIdentity);
        Assert.All(
            reordered.ParticipantIdentities,
            identity => Assert.DoesNotContain(
                identity,
                ordered.ParticipantIdentities));

        // The same entry receives a different identity in each snapshot, so an
        // ordinal never leaks across snapshots.
        Assert.NotSame(
            ordered.ContainingLibraryIdentity,
            reordered.ContainingLibraryIdentity);

        // Endpoints and tie-breaking use those identities, so the reordered
        // snapshot ranks the same evidence in its own declared order.
        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        reordered,
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All)));

        Assert.All(
            result.Pairs,
            pair =>
            {
                Assert.Same(
                    reordered.Identity,
                    pair.Left.Participant.Snapshot);
                Assert.Same(
                    reordered.Identity,
                    pair.Right.Participant.Snapshot);
                Assert.Same(
                    reordered.ContainingLibraryIdentity,
                    pair.Left.Participant);
            });
        Assert.Equal(
            [0, 1, 2],
            result.Libraries.Select(
                library => library.Participant.Ordinal));
        Assert.All(
            result.Libraries.Select(
                (library, index) => (library, index)),
            row => Assert.Same(
                reordered.ParticipantIdentities[row.index],
                row.library.Participant));

        // Distinct registrations always receive distinct identities, so pair
        // identity stays exact even when two entries are otherwise equal.
        Assert.Equal(
            reordered.Entries.Length,
            reordered.ParticipantIdentities.Distinct().Count());
    }

    [Fact]
    public async Task Execute_RanksGloballyAndLimitsAfterTheMerge()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        // Both runs bind the same snapshot: endpoint identity is
        // snapshot-local, so rows are only comparable within one snapshot.
        StructuralCloneParticipantSnapshot snapshot = fixture.Snapshot();
        var complete =
            new WorkspaceStructuralCloneSearchInput(
                snapshot,
                new StructuralCloneSearchSeed.Type(TypeName("N.Alpha")),
                StructuralCloneCandidateBreadth.Everything,
                StructuralCloneCandidateDiscovery.All,
                new WorkspaceStructuralCloneSearchLimits(
                    MaximumResults: 1000));

        WorkspaceStructuralCloneSearchResult.Available all =
            Available(Execute(complete));

        Assert.False(all.Receipt.ResultLimitReached);
        Assert.Equal(all.Pairs.Length, all.Receipt.RankedPairs);
        for (int index = 1; index < all.Pairs.Length; index++)
        {
            StructuralCloneSearchPair previous = all.Pairs[index - 1];
            StructuralCloneSearchPair current = all.Pairs[index];
            Assert.Equal(index + 1, current.Rank);
            Assert.True(
                previous.Similarity.Score >= current.Similarity.Score,
                "Pairs are one global ranking by Analysis score.");
            if (previous.Similarity == current.Similarity)
            {
                Assert.True(
                    Endpoints(previous).CompareTo(Endpoints(current)) < 0,
                    "Ties resolve by exact endpoint identity.");
            }
        }

        // The lower-scoring self method sorts below other libraries' rows, so
        // the ranking cannot be a per-seed or per-library concatenation.
        int worstSelf =
            all.Pairs
                .Select((pair, index) => (pair, index))
                .Where(row => row.pair.Right.Participant.Ordinal == 0
                    && fixture.SelfName(row.pair.Right) == "Compute7")
                .Min(row => row.index);
        Assert.Contains(
            all.Pairs.Take(worstSelf),
            pair => pair.Right.Participant.Ordinal != 0);

        WorkspaceStructuralCloneSearchResult.Available limited =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        new StructuralCloneSearchSeed.Type(
                            TypeName("N.Alpha")),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumResults: 3))));

        Assert.Equal(3, limited.Pairs.Length);
        Assert.Equal(
            all.Pairs.Take(3).Select(pair => (pair.Left, pair.Right)),
            limited.Pairs.Select(pair => (pair.Left, pair.Right)));
        Assert.Equal(all.Receipt.RankedPairs, limited.Receipt.RankedPairs);

        // Intentional row suppression is reported separately from coverage.
        Assert.True(limited.Receipt.ResultLimitReached);
        Assert.True(limited.CoverageIsComplete);
    }

    [Fact]
    public async Task Execute_ReportsUnavailableCandidateLibrary()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.SnapshotWithUnreadableParticipant(),
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All)));

        StructuralCloneSearchLibraryCoverage unreadable =
            Assert.Single(
                result.Libraries,
                library => !library.CoverageIsComplete);
        Assert.Equal(
            StructuralCloneSearchFailureKind.CandidateLibraryUnavailable,
            Assert.Single(unreadable.Failures).Kind);
        Assert.False(result.CoverageIsComplete);
        Assert.False(result.Receipt.ResultLimitReached);

        // Existing ranked evidence stays usable beside the visible gap.
        Assert.NotEmpty(result.Pairs);
        Assert.All(
            result.Pairs,
            pair => Assert.NotSame(
                unreadable.Participant,
                pair.Right.Participant));
    }

    [Fact]
    public async Task Execute_ReportsSeedSelectionFailure()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult missingType =
            Execute(
                new WorkspaceStructuralCloneSearchInput(
                    fixture.Snapshot(),
                    new StructuralCloneSearchSeed.Type(
                        TypeName("N.Missing"))));
        WorkspaceStructuralCloneSearchResult missingMember =
            Execute(
                new WorkspaceStructuralCloneSearchInput(
                    fixture.Snapshot(),
                    new StructuralCloneSearchSeed.Member(
                        TypeName("N.Alpha"),
                        fixture.ForeignAnchor())));

        Assert.Equal(
            StructuralCloneSearchFailureKind.SeedTypeNotFound,
            Assert.IsType<WorkspaceStructuralCloneSearchResult.Failed>(
                    missingType)
                .Failure.Kind);
        Assert.Equal(
            StructuralCloneSearchFailureKind.SeedMemberNotFound,
            Assert.IsType<WorkspaceStructuralCloneSearchResult.Failed>(
                    missingMember)
                .Failure.Kind);
    }

    [Fact]
    public async Task Execute_ReportsExhaustedNameWorkAsIncompleteCoverage()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumNameComparisonWork: 32))));

        Assert.Contains(
            result.Libraries.SelectMany(library => library.Failures),
            failure => failure.Kind
                == StructuralCloneSearchFailureKind.NameWorkLimitReached);
        Assert.False(result.CoverageIsComplete);
    }

    [Fact]
    public async Task Execute_ReportsUndecodableNamesAsIncompleteCoverage()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumNameCharacters: 4))));

        StructuralCloneSearchSeedCoverage seed =
            Assert.Single(result.Seeds);
        Assert.Equal(
            StructuralCloneSearchFailureKind.NameDecodeFailed,
            Assert.Single(seed.Failures).Kind);
        Assert.False(seed.CoverageIsComplete);
        Assert.False(result.CoverageIsComplete);
        Assert.Empty(result.Pairs);
    }

    /// <summary>
    /// Similar-name admission is a property of one seed-candidate pair, not of
    /// the candidate against the best unrelated seed.
    /// </summary>
    [Fact]
    public async Task Execute_RanksEachCandidateOnlyAgainstQualifyingSeeds()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        StructuralCloneParticipantSnapshot snapshot =
            fixture.CrossSeedSnapshot();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.SimilarNames)));

        // Two seeds, and each library declares one exact counterpart of each:
        // Parser.Parse qualifies only for the Parser.Parse seed, and
        // Zulu.Xylophone only for the Zulu.Xylophone seed. The peer library
        // adds Parsers.Parses, which is near the first seed and far from the
        // second.
        Assert.Equal(2, result.Receipt.SeedMethods);
        Assert.Equal(5, result.Receipt.DiscoveredCandidateMethods);

        (string Left, string Right)[] pairs =
        [
            .. result.Pairs.Select(
                pair => (
                    fixture.CrossSeedName(pair.Left),
                    fixture.CrossSeedName(pair.Right))),
        ];

        // Only the qualifying pairs survive: the same-library counterparts
        // are the seeds themselves and are suppressed, and neither seed is
        // ever ranked against the other seed's candidates.
        Assert.Equal(
            [
                ("Parse", "Parse"),
                ("Parse", "Parses"),
                ("Xylophone", "Xylophone"),
            ],
            pairs.Order());
        Assert.DoesNotContain(
            pairs,
            pair => pair.Left == "Xylophone" && pair.Right != "Xylophone");
        Assert.DoesNotContain(
            pairs,
            pair => pair.Left == "Parse" && pair.Right == "Xylophone");

        // The unqualified pairs are not merely dropped from the rows: they
        // are never retrieved, so they charge no aggregate retrieval work.
        // The containing library charges two pairs whose rows are suppressed
        // self-pairs; the peer charges two for the first seed and one for the
        // second.
        Assert.Equal(5L, result.Receipt.RetrievalPairs);
        Assert.Equal(4, result.Receipt.RetrievalCalls);

        // Each row carries its own pair's evidence, not the best unrelated
        // seed's scores.
        StructuralCloneNameQualification exact =
            Qualification(
                Assert.Single(
                    result.Pairs,
                    pair => fixture.CrossSeedName(pair.Right) == "Parse"));
        StructuralCloneNameQualification near =
            Qualification(
                Assert.Single(
                    result.Pairs,
                    pair => fixture.CrossSeedName(pair.Right) == "Parses"));
        Assert.Equal(1.0, exact.DeclaringTypeSimilarity);
        Assert.Equal(1.0, exact.MemberSimilarity);
        Assert.Equal(1.0 - (1.0 / 7.0), near.DeclaringTypeSimilarity, 9);
        Assert.Equal(1.0 - (1.0 / 6.0), near.MemberSimilarity, 9);
        Assert.All(
            result.Pairs,
            pair => Assert.True(
                Qualification(pair).DeclaringTypeSimilarity
                    >= WorkspaceStructuralCloneSearchQuery
                        .NameSimilarityThreshold
                    && Qualification(pair).MemberSimilarity
                        >= WorkspaceStructuralCloneSearchQuery
                            .NameSimilarityThreshold,
                "Every returned pair cleared both thresholds itself."));
        Assert.True(result.CoverageIsComplete);

        // The same population under All discovery ranks every seed against
        // every candidate, so the difference above is admission, not breadth.
        WorkspaceStructuralCloneSearchResult.Available exhaustive =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All)));

        Assert.Equal(
            exhaustive.Receipt.DiscoveredCandidateMethods,
            result.Receipt.DiscoveredCandidateMethods);
        Assert.Equal(10L, exhaustive.Receipt.RetrievalPairs);
        Assert.Contains(
            exhaustive.Pairs,
            pair => fixture.CrossSeedName(pair.Left) == "Xylophone"
                && fixture.CrossSeedName(pair.Right) == "Parse");
    }

    /// <summary>
    /// Per-library bounds alone permit unbounded aggregate work, so the search
    /// bounds the breadth-admitted participant count as a whole-unit
    /// preflight.
    /// </summary>
    [Fact]
    public async Task Execute_ExcludesParticipantsBeyondTheParticipantBound()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available bounded =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumParticipants: 2))));

        Assert.Equal(2, bounded.Receipt.AdmittedLibraries);
        Assert.Equal(1, bounded.Receipt.ExcludedLibraries);
        StructuralCloneSearchLibraryCoverage excluded =
            Assert.Single(
                bounded.Libraries,
                library => !library.CoverageIsComplete);
        Assert.Equal(2, excluded.Participant.Ordinal);
        Assert.False(excluded.Admitted);
        Assert.Equal(
            StructuralCloneSearchFailureKind
                .ParticipantPopulationLimitReached,
            Assert.Single(excluded.Failures).Kind);

        // The omitted pair population is visible, and is not confused with
        // the intentional returned-row bound.
        Assert.False(bounded.CoverageIsComplete);
        Assert.False(bounded.Receipt.ResultLimitReached);
        Assert.All(
            bounded.Pairs,
            pair => Assert.NotSame(
                excluded.Participant,
                pair.Right.Participant));

        // The containing library is always the first admitted participant,
        // even when the snapshot lists it last.
        WorkspaceStructuralCloneSearchResult.Available selfOnly =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.SnapshotOf(
                            fixture.AvailableEntry,
                            fixture.EcosystemEntry,
                            fixture.SelfEntry),
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumParticipants: 1))));

        Assert.Equal(1, selfOnly.Receipt.AdmittedLibraries);
        Assert.Equal(
            StructuralCloneParticipantMembership.ContainingLibrary,
            Assert.Single(
                    selfOnly.Libraries,
                    library => library.Admitted)
                .Membership);
        Assert.Equal(2, selfOnly.Receipt.ExcludedLibraries);
        Assert.False(selfOnly.CoverageIsComplete);

        WorkspaceStructuralCloneSearchResult.Available complete =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumParticipants: 3))));

        Assert.Equal(3, complete.Receipt.AdmittedLibraries);
        Assert.True(complete.CoverageIsComplete);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkspaceStructuralCloneSearchInput(
                fixture.Snapshot(),
                new StructuralCloneSearchSeed.Library(),
                limits: new WorkspaceStructuralCloneSearchLimits(
                    MaximumParticipants: 0)));
    }

    /// <summary>
    /// The aggregate seed-by-candidate retrieval population is bounded, and a
    /// participant that does not fit is excluded whole rather than truncated
    /// into a result that could pass for a complete global top N.
    /// </summary>
    [Fact]
    public async Task Execute_ExcludesParticipantsBeyondTheRetrievalBound()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        // Six library seeds over the six same-library candidates.
        WorkspaceStructuralCloneSearchResult.Available unbounded =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All)));

        Assert.Equal(36L, unbounded.Receipt.RetrievalPairs);
        Assert.True(unbounded.CoverageIsComplete);

        WorkspaceStructuralCloneSearchResult.Available bounded =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumRetrievalPairs: 35))));

        StructuralCloneSearchLibraryCoverage excluded =
            Assert.Single(
                bounded.Libraries,
                library => library.Membership
                    == StructuralCloneParticipantMembership
                        .ContainingLibrary);
        Assert.False(excluded.Admitted);
        Assert.Equal(0L, excluded.RetrievalPairs);
        Assert.Equal(
            StructuralCloneSearchFailureKind.RetrievalWorkLimitReached,
            Assert.Single(excluded.Failures).Kind);
        Assert.Empty(bounded.Pairs);
        Assert.Equal(0L, bounded.Receipt.RetrievalPairs);
        Assert.Equal(0, bounded.Receipt.RetrievalCalls);
        Assert.False(bounded.CoverageIsComplete);
        Assert.False(bounded.Receipt.ResultLimitReached);

        // The budget latches: once one participant does not fit, no later
        // participant is opened or partially evaluated.
        WorkspaceStructuralCloneSearchResult.Available latched =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumRetrievalPairs: 40))));

        Assert.Equal(36L, latched.Receipt.RetrievalPairs);
        Assert.Equal(1, latched.Receipt.AdmittedLibraries);
        Assert.Equal(2, latched.Receipt.ExcludedLibraries);
        Assert.Equal(
            2,
            latched.Libraries.Count(
                library => library.Failures.Any(
                    failure => failure.Kind
                        == StructuralCloneSearchFailureKind
                            .RetrievalWorkLimitReached)));
        Assert.False(latched.CoverageIsComplete);
        Assert.All(
            latched.Pairs,
            pair => Assert.Equal(
                StructuralCloneParticipantMembership.ContainingLibrary,
                pair.Right.Membership));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkspaceStructuralCloneSearchInput(
                fixture.Snapshot(),
                new StructuralCloneSearchSeed.Library(),
                limits: new WorkspaceStructuralCloneSearchLimits(
                    MaximumRetrievalPairs: 0)));

        // Under SimilarNames the pair population is built one admitted
        // seed-candidate at a time, so the bound stops admission itself
        // rather than measuring a materialized quadratic structure.
        WorkspaceStructuralCloneSearchResult.Available named =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumRetrievalPairs: 2))));

        StructuralCloneSearchLibraryCoverage abandoned =
            Assert.Single(
                named.Libraries,
                library => library.Membership
                    == StructuralCloneParticipantMembership
                        .ContainingLibrary);
        Assert.False(abandoned.Admitted);
        Assert.Equal(
            StructuralCloneSearchFailureKind.RetrievalWorkLimitReached,
            Assert.Single(abandoned.Failures).Kind);
        Assert.Empty(named.Pairs);
        Assert.Equal(0L, named.Receipt.RetrievalPairs);
        Assert.Equal(0, named.Receipt.RetrievalCalls);
        Assert.False(named.CoverageIsComplete);
        Assert.False(named.Receipt.ResultLimitReached);
    }

    [Fact]
    public async Task Execute_ReportsCandidateAndSeedPopulationLimits()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        WorkspaceStructuralCloneSearchResult.Available candidateLimit =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        fixture.MemberSeed("Compute0"),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumCandidateMethods: 2))));

        Assert.Empty(candidateLimit.Pairs);
        Assert.Contains(
            candidateLimit.Libraries.SelectMany(
                library => library.Failures),
            failure => failure.Kind
                == StructuralCloneSearchFailureKind
                    .CandidatePopulationLimitReached);
        Assert.False(candidateLimit.CoverageIsComplete);

        WorkspaceStructuralCloneSearchResult seedLimit =
            Execute(
                new WorkspaceStructuralCloneSearchInput(
                    fixture.Snapshot(),
                    new StructuralCloneSearchSeed.Library(),
                    StructuralCloneCandidateBreadth.Self,
                    StructuralCloneCandidateDiscovery.All,
                    new WorkspaceStructuralCloneSearchLimits(
                        MaximumSeedMethods: 2)));

        Assert.Equal(
            StructuralCloneSearchFailureKind.SeedPopulationLimitReached,
            Assert.IsType<WorkspaceStructuralCloneSearchResult.Failed>(
                    seedLimit)
                .Failure.Kind);
    }

    /// <summary>
    /// Group and participant lifetime belongs to the snapshot's issuer, and
    /// nothing enforces it yet. A release before execution must therefore be
    /// visible rather than an exception escaping <c>Execute</c>.
    /// </summary>
    [Fact]
    public async Task Execute_ReportsReleasedSeedLibraryAsTypedFailure()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        StructuralCloneParticipantSnapshot snapshot = fixture.Snapshot();
        fixture.SelfGroup.Dispose();

        WorkspaceStructuralCloneSearchResult result =
            Execute(
                new WorkspaceStructuralCloneSearchInput(
                    snapshot,
                    new StructuralCloneSearchSeed.Library()));

        StructuralCloneSearchFailure failure =
            Assert.IsType<WorkspaceStructuralCloneSearchResult.Failed>(
                    result)
                .Failure;
        Assert.Equal(
            StructuralCloneSearchFailureKind.SeedLibraryReleased,
            failure.Kind);
        Assert.Same(fixture.SelfEntry.Subject, failure.Subject);
    }

    [Fact]
    public async Task
        Execute_ReportsReleasedCandidateLibraryAsIncompleteCoverage()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        StructuralCloneParticipantSnapshot snapshot = fixture.Snapshot();
        StructuralCloneSearchSeed seed = fixture.MemberSeed("Compute0");
        fixture.EcosystemGroup.Dispose();

        WorkspaceStructuralCloneSearchResult.Available result =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        seed,
                        StructuralCloneCandidateBreadth.Everything,
                        StructuralCloneCandidateDiscovery.All)));

        StructuralCloneSearchLibraryCoverage released =
            Assert.Single(
                result.Libraries,
                library => library.Membership
                    == StructuralCloneParticipantMembership
                        .RegisteredEcosystem);
        Assert.True(released.Admitted);
        Assert.Equal(
            StructuralCloneSearchFailureKind.CandidateLibraryReleased,
            Assert.Single(released.Failures).Kind);
        Assert.Equal(0L, released.RetrievalPairs);
        Assert.False(released.CoverageIsComplete);
        Assert.False(result.CoverageIsComplete);

        // The released participant reserved no retrieval budget, and the
        // evidence ranked from the other libraries survives beside the gap.
        Assert.NotEmpty(result.Pairs);
        Assert.All(
            result.Pairs,
            pair => Assert.NotSame(
                released.Participant,
                pair.Right.Participant));
        Assert.Contains(
            result.Pairs,
            pair => pair.Right.Membership
                == StructuralCloneParticipantMembership.Available);
    }

    /// <summary>
    /// Memoized name scores are bounded by retained cells, not only by entry
    /// count, because one entry costs one cell per distinct seed name.
    /// </summary>
    [Fact]
    public async Task
        Execute_BoundedNameScoreCacheDoesNotChangeCompleteAdmission()
    {
        await using Fixture fixture = await Fixture.CreateAsync();

        // Four declaring types share one simple name and each repeats the
        // same eight member names, so the caches take real hits and one
        // score vector is eight cells wide.
        StructuralCloneParticipantSnapshot snapshot =
            fixture.RepeatedNamesSnapshot();

        WorkspaceStructuralCloneSearchResult.Available cached =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumResults: 500))));

        // One cell cannot hold an eight-cell vector, so nothing that matters
        // is retained and every name is rescored.
        WorkspaceStructuralCloneSearchResult.Available uncached =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumResults: 500,
                            MaximumNameCacheCells: 1))));

        Assert.True(cached.CoverageIsComplete);
        Assert.True(uncached.CoverageIsComplete);
        Assert.NotEmpty(cached.Pairs);

        // Admission and ranking are identical; only the charged name work
        // differs, which is what proves the cache is a pure optimization.
        Assert.Equal(cached.Pairs, uncached.Pairs);
        Assert.Equal(cached.Receipt, uncached.Receipt);
        Assert.Equal(
            cached.Libraries.Select(
                library => (
                    library.Admitted,
                    library.CandidateMethods,
                    library.DiscoveredMethods,
                    library.RetrievalPairs)),
            uncached.Libraries.Select(
                library => (
                    library.Admitted,
                    library.CandidateMethods,
                    library.DiscoveredMethods,
                    library.RetrievalPairs)));
        Assert.True(
            NameWork(uncached) > NameWork(cached),
            "The bounded cache must actually retain scores when it fits.");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkspaceStructuralCloneSearchInput(
                snapshot,
                new StructuralCloneSearchSeed.Library(),
                limits: new WorkspaceStructuralCloneSearchLimits(
                    MaximumNameCacheCells: 0)));
    }

    /// <summary>
    /// The shared name-work budget, not the cache, decides admission: once it
    /// is exhausted no later candidate is admitted, including one whose
    /// declaring-type and member scores are already memoized.
    /// </summary>
    [Fact]
    public async Task Execute_StopsNameAdmissionAtTheExhaustedWorkBudget()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        StructuralCloneParticipantSnapshot snapshot =
            fixture.RepeatedNamesSnapshot();
        StructuralCloneSearchSeed seed = fixture.RepeatedNamesSeed();

        WorkspaceStructuralCloneSearchResult.Available complete =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        seed,
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumResults: 500))));

        Assert.True(complete.CoverageIsComplete);
        int discovered = complete.Receipt.DiscoveredCandidateMethods;
        Assert.True(discovered > 1);

        WorkspaceStructuralCloneSearchResult.Available exhausted =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        seed,
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumResults: 500,
                            MaximumNameComparisonWork:
                                NameWork(complete) / 2))));

        Assert.Contains(
            exhausted.Libraries.SelectMany(library => library.Failures),
            failure => failure.Kind
                == StructuralCloneSearchFailureKind.NameWorkLimitReached);
        Assert.False(exhausted.CoverageIsComplete);
        Assert.True(
            exhausted.Receipt.DiscoveredCandidateMethods < discovered);

        // Every candidate name in this fixture qualifies, so a complete run
        // admits every method in MethodDef order. Admission after exhaustion
        // would appear as a gap in that prefix.
        int[] rows =
        [
            .. exhausted.Pairs
                .Select(pair => Row(pair.Right.Method))
                .Order(),
        ];
        Assert.NotEmpty(rows);
        Assert.Equal(
            Enumerable.Range(rows[0], rows.Length),
            rows);
    }

    /// <summary>
    /// One seed's candidate group is retrieved in bounded chunks so
    /// cancellation is observed between units of Analysis work. Chunking is a
    /// scheduling decision: it cannot change the global ranking or the
    /// aggregate retrieval charge.
    /// </summary>
    [Fact]
    public async Task
        Execute_ChunkedRetrievalPreservesGlobalRankingAndPairCharge()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        StructuralCloneParticipantSnapshot snapshot =
            fixture.RepeatedNamesSnapshot();

        WorkspaceStructuralCloneSearchResult.Available whole =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumResults: 500))));

        // Three does not divide the candidate population, so the last chunk
        // of every seed group is short.
        WorkspaceStructuralCloneSearchResult.Available chunked =
            Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        snapshot,
                        new StructuralCloneSearchSeed.Library(),
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.SimilarNames,
                        new WorkspaceStructuralCloneSearchLimits(
                            MaximumResults: 500,
                            MaximumRetrievalChunkMethods: 3))));

        Assert.NotEmpty(whole.Pairs);
        Assert.Equal(whole.Pairs, chunked.Pairs);
        Assert.Equal(
            whole.Receipt.RetrievalPairs,
            chunked.Receipt.RetrievalPairs);
        Assert.Equal(
            whole.Receipt.RankedPairs,
            chunked.Receipt.RankedPairs);
        Assert.Equal(
            whole.Receipt.SuppressedPairs,
            chunked.Receipt.SuppressedPairs);
        Assert.Equal(
            whole.Receipt.ReturnedPairs,
            chunked.Receipt.ReturnedPairs);
        Assert.True(whole.CoverageIsComplete);
        Assert.True(chunked.CoverageIsComplete);
        Assert.Equal(
            whole.Seeds.Select(
                seed => (seed.Seed, seed.Disposition, seed.RankedPairs,
                    seed.SuppressedPairs)),
            chunked.Seeds.Select(
                seed => (seed.Seed, seed.Disposition, seed.RankedPairs,
                    seed.SuppressedPairs)));

        // The retrieval-call count is the one receipt field chunking moves,
        // and every pair still carries its own name evidence.
        Assert.True(
            chunked.Receipt.RetrievalCalls
                > whole.Receipt.RetrievalCalls);
        Assert.All(
            chunked.Pairs,
            pair => Assert.NotNull(pair.NameQualification));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorkspaceStructuralCloneSearchInput(
                snapshot,
                new StructuralCloneSearchSeed.Library(),
                limits: new WorkspaceStructuralCloneSearchLimits(
                    MaximumRetrievalChunkMethods: 0)));
    }

    [Fact]
    public async Task Execute_ObservesCancellation()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.Throws<OperationCanceledException>(
            () => WorkspaceStructuralCloneSearchQuery.Execute(
                new WorkspaceStructuralCloneSearchInput(
                    fixture.RepeatedNamesSnapshot(),
                    new StructuralCloneSearchSeed.Library()),
                cancellation.Token));
    }

    [Fact]
    public async Task Execute_RunsThroughTheInspectionQueryRegistry()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        var input =
            new WorkspaceStructuralCloneSearchInput(
                fixture.Snapshot(),
                fixture.MemberSeed("Compute0"));
        var registry =
            new InspectionQueryRegistry<
                WorkspaceStructuralCloneSearchInput>()
                .Add(
                    WorkspaceStructuralCloneSearchQuery.Definition,
                    WorkspaceStructuralCloneSearchQuery.Execute);

        WorkspaceStructuralCloneSearchResult result =
            registry
                .Run(
                    [WorkspaceStructuralCloneSearchQuery.Definition],
                    input)
                .Get(WorkspaceStructuralCloneSearchQuery.Definition);

        Assert.IsType<WorkspaceStructuralCloneSearchResult.Available>(
            result);
    }

    static WorkspaceStructuralCloneSearchResult Execute(
        WorkspaceStructuralCloneSearchInput input)
        => WorkspaceStructuralCloneSearchQuery.Execute(
            input,
            TestContext.Current.CancellationToken);

    static WorkspaceStructuralCloneSearchResult.Available Available(
        WorkspaceStructuralCloneSearchResult result)
        => Assert.IsType<
            WorkspaceStructuralCloneSearchResult.Available>(result);

    static StructuralCloneNameQualification Qualification(
        StructuralCloneSearchPair pair)
        => Assert.IsType<StructuralCloneNameQualification>(
            pair.NameQualification);

    static int SeedCount(Fixture fixture, StructuralCloneSearchSeed seed)
        => Available(
                Execute(
                    new WorkspaceStructuralCloneSearchInput(
                        fixture.Snapshot(),
                        seed,
                        StructuralCloneCandidateBreadth.Self,
                        StructuralCloneCandidateDiscovery.All)))
            .Receipt.SeedMethods;

    static int Row(MetadataMethodAddress address)
        => MetadataTokens.GetRowNumber(address.Handle);

    static long NameWork(
        WorkspaceStructuralCloneSearchResult.Available result)
        => result.Libraries.Sum(
            library => library.NameComparisonWork);

    static (int Left, int LeftRow, int Right, int RightRow) Endpoints(
        StructuralCloneSearchPair pair)
        => (
            pair.Left.Participant.Ordinal,
            Row(pair.Left.Method),
            pair.Right.Participant.Ordinal,
            Row(pair.Right.Method));

    static MetadataTypeDefinitionName TypeName(string serializedName)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.ParseSerialized(
                    serializedName))
            .Name;

    static async ValueTask<WorkspaceScopeSnapshot> ScopeSnapshot(
        InspectionWorkspace workspace)
        => Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync())
            .Snapshot;

    /// <summary>
    /// One Workspace, three synthetic libraries, and the owner-issued
    /// participant snapshot the search consumes.
    /// </summary>
    sealed class Fixture : IAsyncDisposable
    {
        Fixture(
            InspectionWorkspace workspace,
            WorkspaceScopeRevision revision)
        {
            Workspace = workspace;
            Revision = revision;
            SelfImage =
                Assembly(
                    SelfAssembly,
                    new Guid("1B0A0C5E-0001-4000-8000-000000000001"),
                    SelfTypes());
            ImmutableArray<byte> ecosystem =
                Assembly(
                    EcosystemAssembly,
                    new Guid("1B0A0C5E-0002-4000-8000-000000000002"),
                    [("N", "Alpha", [("Compute9", 1)])]);
            ImmutableArray<byte> available =
                Assembly(
                    AvailableAssembly,
                    new Guid("1B0A0C5E-0003-4000-8000-000000000003"),
                    [("N", "Alpha", [("Compute8", 1)])]);
            SelfGroup = Group(workspace, SelfImage);
            EcosystemGroup = Group(workspace, ecosystem);
            AvailableGroup = Group(workspace, available);
            DuplicateGroup = Group(workspace, ecosystem);
            UnreadableGroup =
                Group(
                    workspace,
                    ImmutableCollectionsMarshal.AsImmutableArray(
                        new byte[] { 0x4D, 0x5A, 0x00, 0x00 }));
            SelfEntry =
                new StructuralCloneParticipantEntry(
                    SelfGroup,
                    SelfParticipant,
                    StructuralCloneParticipantMembership
                        .ContainingLibrary);
            EcosystemEntry =
                new StructuralCloneParticipantEntry(
                    EcosystemGroup,
                    EcosystemParticipant,
                    StructuralCloneParticipantMembership
                        .RegisteredEcosystem);
            AvailableEntry =
                new StructuralCloneParticipantEntry(
                    AvailableGroup,
                    AvailableGroup.Participants[0],
                    StructuralCloneParticipantMembership.Available);

            // Two seeds whose declaring-type and member names are mutually
            // dissimilar, plus a peer library carrying one exact counterpart
            // for each. Every candidate qualifies for exactly one seed.
            CrossSeedImage =
                Assembly(
                    CrossSeedAssembly,
                    new Guid("1B0A0C5E-0004-4000-8000-000000000004"),
                    CrossSeedTypes());
            CrossSeedPeerImage =
                Assembly(
                    CrossSeedCandidateAssembly,
                    new Guid("1B0A0C5E-0005-4000-8000-000000000005"),
                    [
                        .. CrossSeedTypes(),

                        // Near, but not equal, to the Parser.Parse seed and
                        // far from the Zulu.Xylophone seed.
                        ("N", "Parsers", [("Parses", 1)]),
                    ]);
            CrossSeedGroup = Group(workspace, CrossSeedImage);
            CrossSeedPeerGroup = Group(workspace, CrossSeedPeerImage);
            CrossSeedEntry =
                new StructuralCloneParticipantEntry(
                    CrossSeedGroup,
                    CrossSeedGroup.Participants[0],
                    StructuralCloneParticipantMembership
                        .ContainingLibrary);
            CrossSeedPeerEntry =
                new StructuralCloneParticipantEntry(
                    CrossSeedPeerGroup,
                    CrossSeedPeerGroup.Participants[0],
                    StructuralCloneParticipantMembership.Available);

            // Every declaring type carries the same simple name and the same
            // member names, so decoded-name memoization takes real hits and
            // one score vector spans every distinct seed member name.
            RepeatedNamesImage =
                Assembly(
                    RepeatedNamesAssembly,
                    new Guid("1B0A0C5E-0006-4000-8000-000000000006"),
                    RepeatedNameTypeShapes());
            RepeatedNamesGroup = Group(workspace, RepeatedNamesImage);
            RepeatedNamesEntry =
                new StructuralCloneParticipantEntry(
                    RepeatedNamesGroup,
                    RepeatedNamesGroup.Participants[0],
                    StructuralCloneParticipantMembership
                        .ContainingLibrary);
        }

        internal InspectionWorkspace Workspace { get; }
        internal WorkspaceScopeRevision Revision { get; private set; }
        internal ImmutableArray<byte> SelfImage { get; }
        internal AssemblyContextGroup SelfGroup { get; }
        internal AssemblyContextGroup EcosystemGroup { get; }
        internal AssemblyContextGroup AvailableGroup { get; }
        internal AssemblyContextGroup DuplicateGroup { get; }
        internal AssemblyContextGroup UnreadableGroup { get; }
        internal StructuralCloneParticipantEntry SelfEntry { get; }
        internal StructuralCloneParticipantEntry EcosystemEntry { get; }
        internal StructuralCloneParticipantEntry AvailableEntry { get; }
        internal ImmutableArray<byte> CrossSeedImage { get; }
        internal ImmutableArray<byte> CrossSeedPeerImage { get; }
        internal AssemblyContextGroup CrossSeedGroup { get; }
        internal AssemblyContextGroup CrossSeedPeerGroup { get; }
        internal StructuralCloneParticipantEntry CrossSeedEntry { get; }
        internal StructuralCloneParticipantEntry CrossSeedPeerEntry { get; }
        internal ImmutableArray<byte> RepeatedNamesImage { get; }
        internal AssemblyContextGroup RepeatedNamesGroup { get; }
        internal StructuralCloneParticipantEntry RepeatedNamesEntry { get; }

        internal AssemblyContextParticipant SelfParticipant =>
            SelfGroup.Participants[0];

        internal AssemblyContextParticipant EcosystemParticipant =>
            EcosystemGroup.Participants[0];

        internal static async ValueTask<Fixture> CreateAsync()
        {
            InspectionWorkspace workspace =
                InspectionWorkspace.CreateAsynchronous();
            return new Fixture(
                workspace,
                (await ScopeSnapshot(workspace)).Revision);
        }

        internal StructuralCloneParticipantSnapshot Snapshot()
            => new(
                Revision,
                Revision,
                [SelfEntry, EcosystemEntry, AvailableEntry]);

        /// <summary>One snapshot in the caller's exact entry order.</summary>
        internal StructuralCloneParticipantSnapshot SnapshotOf(
            params StructuralCloneParticipantEntry[] entries)
            => new(Revision, Revision, entries);

        /// <summary>
        /// The containing library and one peer, each declaring
        /// <c>N.Parser.Parse</c> and <c>N.Zulu.Xylophone</c>.
        /// </summary>
        internal StructuralCloneParticipantSnapshot CrossSeedSnapshot()
            => new(
                Revision,
                Revision,
                [CrossSeedEntry, CrossSeedPeerEntry]);

        /// <summary>Decodes one cross-seed endpoint's method name.</summary>
        internal string CrossSeedName(
            StructuralCloneSearchEndpoint endpoint)
        {
            using var image =
                new PEReader(
                    endpoint.Participant.Ordinal == 0
                        ? CrossSeedImage
                        : CrossSeedPeerImage);
            MetadataReader reader = image.GetMetadataReader();
            return reader.GetString(
                reader.GetMethodDefinition(endpoint.Method.Handle).Name);
        }

        /// <summary>
        /// One library whose four declaring types share one simple name and
        /// repeat the same eight member names.
        /// </summary>
        internal StructuralCloneParticipantSnapshot RepeatedNamesSnapshot()
            => new(Revision, Revision, [RepeatedNamesEntry]);

        /// <summary>The first repeated-names method as an exact seed.</summary>
        internal StructuralCloneSearchSeed.Member RepeatedNamesSeed()
        {
            using var image = new PEReader(RepeatedNamesImage);
            MetadataReader reader = image.GetMetadataReader();
            TypeDefinitionHandle type = Type(reader, "N0.Group");
            return new StructuralCloneSearchSeed.Member(
                TypeName("N0.Group"),
                ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    type,
                    reader.GetMethodDefinition(
                        Method(reader, type, "Compute00"))));
        }

        internal StructuralCloneParticipantSnapshot
            SnapshotWithDuplicateContent()
            => new(
                Revision,
                Revision,
                [
                    SelfEntry,
                    EcosystemEntry,
                    new StructuralCloneParticipantEntry(
                        DuplicateGroup,
                        DuplicateGroup.Participants[0],
                        StructuralCloneParticipantMembership.Available),
                ]);

        internal StructuralCloneParticipantSnapshot
            SnapshotWithUnreadableParticipant()
            => new(
                Revision,
                Revision,
                [
                    SelfEntry,
                    new StructuralCloneParticipantEntry(
                        UnreadableGroup,
                        UnreadableGroup.Participants[0],
                        StructuralCloneParticipantMembership.Available),
                ]);

        internal async ValueTask<WorkspaceScopeRevision>
            CommitFreshRevisionAsync()
        {
            var committed =
                Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                    await Workspace.ClearScopeAsync(
                        Revision,
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        TestContext.Current.CancellationToken));
            Revision = committed.Snapshot.Revision;
            return Revision;
        }

        internal StructuralCloneSearchSeed.Member MemberSeed(
            string methodName)
        {
            using var image = new PEReader(SelfImage);
            MetadataReader reader = image.GetMetadataReader();
            TypeDefinitionHandle type = Type(reader, "N.Alpha");
            return new StructuralCloneSearchSeed.Member(
                TypeName("N.Alpha"),
                ApiMemberIdentity.CreateMethodAnchor(
                    reader,
                    type,
                    reader.GetMethodDefinition(
                        Method(reader, type, methodName))));
        }

        /// <summary>An anchor for a method that N.Alpha does not declare.</summary>
        internal MemberAnchor ForeignAnchor()
        {
            using var image = new PEReader(SelfImage);
            MetadataReader reader = image.GetMetadataReader();
            TypeDefinitionHandle type = Type(reader, "N.Zulu");
            return ApiMemberIdentity.CreateMethodAnchor(
                reader,
                type,
                reader.GetMethodDefinition(
                    Method(reader, type, "Compute4")));
        }

        internal string SelfName(StructuralCloneSearchEndpoint endpoint)
        {
            Assert.Equal(0, endpoint.Participant.Ordinal);
            using var image = new PEReader(SelfImage);
            MetadataReader reader = image.GetMetadataReader();
            return reader.GetString(
                reader.GetMethodDefinition(endpoint.Method.Handle).Name);
        }

        public async ValueTask DisposeAsync()
        {
            SelfGroup.Dispose();
            EcosystemGroup.Dispose();
            AvailableGroup.Dispose();
            DuplicateGroup.Dispose();
            UnreadableGroup.Dispose();
            CrossSeedGroup.Dispose();
            CrossSeedPeerGroup.Dispose();
            RepeatedNamesGroup.Dispose();
            await Workspace.DisposeAsync();
        }

        /// <summary>
        /// Types that share one simple name across distinct namespaces, each
        /// repeating the same member names with differing body lengths.
        /// </summary>
        static (string Namespace, string Name,
            (string Method, int Body)[] Methods)[] RepeatedNameTypeShapes()
        {
            var types =
                new (string, string, (string, int)[])[RepeatedNameTypes];
            for (int type = 0; type < RepeatedNameTypes; type++)
            {
                var methods = new (string, int)[RepeatedNameMethods];
                for (int method = 0;
                    method < RepeatedNameMethods;
                    method++)
                {
                    methods[method] =
                        ($"Compute0{method}", 1 + ((type + method) % 4));
                }

                types[type] = ($"N{type}", "Group", methods);
            }

            return types;
        }

        static (string Namespace, string Name,
            (string Method, int Body)[] Methods)[] CrossSeedTypes()
            =>
            [
                ("N", "Parser", [("Parse", 1)]),
                ("N", "Zulu", [("Xylophone", 1)]),
            ];

        static (string Namespace, string Name,
            (string Method, int Body)[] Methods)[] SelfTypes()
            =>
            [
                (
                    "N",
                    "Alpha",
                    [
                        ("Compute0", 1),
                        ("Compute1", 1),
                        ("Compute2", 1),
                        ("Compute7", 4),
                        ("Unrelated", 1),
                    ]),
                ("N", "Zulu", [("Compute4", 1)]),
            ];

        static AssemblyContextGroup Group(
            InspectionWorkspace workspace,
            ImmutableArray<byte> image)
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
                            AssemblyResolutionProvenance.Local(
                                "clone search tests")),
                        new TestBindingPolicy()),
                ]);

        static AssemblyReferenceIdentity Identity(
            ImmutableArray<byte> image)
        {
            try
            {
                using var reader = new PEReader(image);
                return AssemblyReferenceIdentity
                    .FromAssemblyDefinition(
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

        static MethodDefinitionHandle Method(
            MetadataReader reader,
            TypeDefinitionHandle type,
            string name)
            => Assert.Single(
                reader.GetTypeDefinition(type).GetMethods(),
                method => reader.GetString(
                    reader.GetMethodDefinition(method).Name)
                    == name);

        static TypeDefinitionHandle Type(
            MetadataReader reader,
            string serializedName)
        {
            var index = MetadataTypeDefinitionIndex.Create(reader);
            Assert.True(
                index.TryGetUniqueDefinition(
                    TypeName(serializedName),
                    out TypeDefinitionHandle handle));
            return handle;
        }

        /// <summary>
        /// Builds one synthetic library whose TypeDef method ranges partition
        /// the MethodDef table and whose bodies differ only in length.
        /// </summary>
        static ImmutableArray<byte> Assembly(
            string assemblyName,
            Guid moduleVersionId,
            (string Namespace, string Name,
                (string Method, int Body)[] Methods)[] types)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString($"{assemblyName}.dll"),
                metadata.GetOrAddGuid(moduleVersionId),
                encId: default,
                encBaseId: default);
            metadata.AddAssembly(
                metadata.GetOrAddString(assemblyName),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKey: default,
                flags: default,
                hashAlgorithm: default);
            metadata.AddTypeDefinition(
                default,
                default,
                metadata.GetOrAddString("<Module>"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            int nextMethod = 1;
            foreach ((string ns, string name,
                (string Method, int Body)[] methods) in types)
            {
                metadata.AddTypeDefinition(
                    TypeAttributes.Class | TypeAttributes.Public,
                    metadata.GetOrAddString(ns),
                    metadata.GetOrAddString(name),
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList:
                        MetadataTokens.MethodDefinitionHandle(nextMethod));
                nextMethod += methods.Length;
            }

            var bodies = new BlobBuilder();
            var encoder = new MethodBodyStreamEncoder(bodies);
            foreach ((_, _, (string Method, int Body)[] methods) in types)
            {
                foreach ((string method, int body) in methods)
                {
                    AddMethod(metadata, encoder, method, body);
                }
            }

            var pe = new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                bodies,
                flags: CorFlags.ILOnly);
            var image = new BlobBuilder();
            pe.Serialize(image);
            return ImmutableCollectionsMarshal.AsImmutableArray(
                image.ToArray());
        }

        static void AddMethod(
            MetadataBuilder metadata,
            MethodBodyStreamEncoder bodies,
            string name,
            int instructions)
        {
            var code = new BlobBuilder();
            for (int index = 1; index < instructions; index++)
            {
                code.WriteByte(0x00);
            }
            code.WriteByte(0x2A);
            int body = bodies.AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 0);
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: false)
                .Parameters(
                    parameterCount: 0,
                    returnType => returnType.Void(),
                    parameters => { });
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature),
                body,
                MetadataTokens.ParameterHandle(1));
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
                        AssemblyBindingFailureKind
                            .CandidateUnavailable)));
    }
}
