using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.StructuralCloneFixtures;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneRetrievalTests
{
    [Fact]
    public void RetrieveSimilar_RanksExactAndNearPeersAboveHardNegative()
    {
        using PEReader image = OpenFixture();
        ImmutableArray<MethodDefinitionHandle> population = Population();

        StructuralCloneRetrievalResult exact =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.ExactPositiveA)),
                population);
        StructuralCloneRetrievalResult near =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearConstantA)),
                population);

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            exact.Disposition);
        StructuralCloneRetrievalCandidate exactPeer = Candidate(
            exact,
            nameof(StructuralCloneFixture.ExactPositiveB));
        Assert.Equal(1, exactPeer.Rank);
        Assert.Equal(10_000, exactPeer.Similarity.Score);

        StructuralCloneRetrievalCandidate nearPeer = Candidate(
            near,
            nameof(StructuralCloneFixture.NearConstantB));
        StructuralCloneRetrievalCandidate hardNegative = Candidate(
            near,
            nameof(StructuralCloneFixture.NearHardNegativeA));
        Assert.InRange(nearPeer.Rank, 1, 5);
        Assert.True(
            nearPeer.Similarity.Score
                > hardNegative.Similarity.Score);
        Assert.Equal(
            near.Receipt.ReturnedCandidates,
            near.Candidates.Length);
        Assert.True(near.Receipt.UnsupportedMethods > 0);
    }

    [Fact]
    public void RetrieveSimilar_InputOrderDoesNotChangeRanking()
    {
        using PEReader image = OpenFixture();
        ImmutableArray<MethodDefinitionHandle> population = Population();
        StructuralCloneRetrievalResult forward =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearCallTargetA)),
                population);
        StructuralCloneRetrievalResult reverse =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearCallTargetA)),
                [.. population.Reverse()]);

        Assert.Equal(
            forward.Candidates.Select(CandidateKey),
            reverse.Candidates.Select(CandidateKey));
    }

    [Fact]
    public void RetrieveSimilar_ResultLimitIsDeterministicAndVisible()
    {
        using PEReader image = OpenFixture();

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearConstantA)),
                Population(),
                new StructuralCloneRetrievalLimits(
                    MaximumResults: 2));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            result.Disposition);
        Assert.Equal(2, result.Candidates.Length);
        Assert.Equal([1, 2], result.Candidates.Select(static item =>
            item.Rank));
        Assert.Equal(
            result.Receipt.RankedCandidates
                - result.Receipt.ReturnedCandidates,
            result.Receipt.SuppressedCandidates);
    }

    [Fact]
    public void RetrieveSimilar_MethodLimitIsAtomic()
    {
        using PEReader image = OpenFixture();

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearConstantA)),
                Population(),
                new StructuralCloneRetrievalLimits(
                    MaximumMethods: 1));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.LimitReached,
            result.Disposition);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.Receipt.BodyProductions);
        Assert.Contains(
            result.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneRetrievalBlockerKind.MethodLimit);
    }

    [Fact]
    public void RetrieveSimilar_UnsupportedSeedIsExplicit()
    {
        using PEReader image = OpenFixture();

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.ExceptionHandlingA)),
                Population());

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Unsupported,
            result.Disposition);
        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            result.Seed.Disposition);
        Assert.Empty(result.Candidates);
        Assert.Contains(
            result.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneRetrievalBlockerKind.SeedUnsupported);
    }

    [Fact]
    public void RetrieveSimilar_PartialRankingRetainsVisibleLimit()
    {
        using PEReader image = OpenFixture();

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearConstantA)),
                Population(),
                new StructuralCloneRetrievalLimits(
                    ComparisonLimits:
                        new StructuralCloneComparisonLimits(
                            MaximumBlocks: 1)));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.LimitReached,
            result.Disposition);
        Assert.NotEmpty(result.Candidates);
        Assert.True(result.Receipt.LimitReachedMethods > 0);
        Assert.True(
            result.Receipt.SuppressedCandidates
                >= result.Receipt.LimitReachedMethods);
        Assert.Contains(
            result.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneRetrievalBlockerKind
                        .CandidateProductionLimit);
    }

    [Fact]
    public void RetrieveSimilar_SeedNeedNotBeInPopulation()
    {
        using PEReader image = OpenFixture();
        MethodDefinitionHandle seed =
            Method(nameof(StructuralCloneFixture.ExactPositiveA));
        ImmutableArray<MethodDefinitionHandle> population =
        [
            Method(nameof(StructuralCloneFixture.ExactPositiveB)),
        ];

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                seed,
                population);

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            result.Disposition);
        Assert.Equal(1, result.Receipt.InputMethods);
        Assert.Equal(1, result.Receipt.ProcessedMethods);
        Assert.Equal(2, result.Receipt.BodyProductions);
        Assert.Equal(
            Method(nameof(StructuralCloneFixture.ExactPositiveB)),
            Assert.Single(result.Candidates).Method.Handle);
    }

    [Fact]
    public void RetrieveSimilar_RejectsDuplicatePopulationHandles()
    {
        using PEReader image = OpenFixture();
        MethodDefinitionHandle method =
            Method(nameof(StructuralCloneFixture.ExactPositiveB));

        Assert.Throws<ArgumentException>(() =>
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.ExactPositiveA)),
                [method, method]));
    }

    [Fact]
    public void RetrieveSimilar_ScoreDoesNotEstablishRelationship()
    {
        using PEReader image = OpenFixture();
        MethodDefinitionHandle seed =
            Method(nameof(StructuralCloneFixture.NearConstantA));
        MethodDefinitionHandle hardNegative =
            Method(nameof(StructuralCloneFixture.NearHardNegativeA));

        StructuralCloneRetrievalCandidate candidate = Candidate(
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                seed,
                Population()),
            nameof(StructuralCloneFixture.NearHardNegativeA));
        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(image, seed, hardNegative);

        Assert.True(candidate.Similarity.Score > 0);
        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
    }

    static PEReader OpenFixture()
        => new(File.OpenRead(
            typeof(StructuralCloneFixture).Assembly.Location));

    static ImmutableArray<MethodDefinitionHandle> Population()
        =>
        [
            .. typeof(StructuralCloneFixture)
                .GetMethods(
                    BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                .Select(static method =>
                    MetadataTokens.MethodDefinitionHandle(
                        method.MetadataToken & 0x00FFFFFF)),
        ];

    static MethodDefinitionHandle Method(string name)
        => MetadataTokens.MethodDefinitionHandle(
            typeof(StructuralCloneFixture)
                .GetMethod(
                    name,
                    BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)!
                .MetadataToken
                & 0x00FFFFFF);

    static StructuralCloneRetrievalCandidate Candidate(
        StructuralCloneRetrievalResult result,
        string name)
    {
        MethodDefinitionHandle handle = Method(name);
        return Assert.Single(
            result.Candidates,
            item => item.Method.Handle == handle);
    }

    static string CandidateKey(
        StructuralCloneRetrievalCandidate candidate)
        => $"{candidate.Rank}:"
            + $"{MetadataTokens.GetToken(candidate.Method.Handle):X8}:"
            + $"{candidate.Similarity.Score}";
}
