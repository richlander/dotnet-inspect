using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Sections;

public enum StructuralMatchDiscoveryParticipantRole
{
    Seed,
    Candidate,
}

public abstract record StructuralMatchDiscoveryInspectionResult
{
    private protected StructuralMatchDiscoveryInspectionResult()
    {
    }

    public sealed record Available(
        AssemblyContextSubject SeedSubject,
        AssemblyContextSubject CandidateSubject,
        StructuralCloneRetrievalResult Retrieval,
        ImmutableArray<StructuralCloneRetrievalCandidate> Candidates,
        StructuralCloneRetrievalReceipt Receipt)
        : StructuralMatchDiscoveryInspectionResult;

    public sealed record LimitReached(
        AssemblyContextSubject SeedSubject,
        AssemblyContextSubject CandidateSubject,
        MetadataMethodAddress Seed,
        int InputMethods,
        int SuppressedCandidates,
        StructuralCloneSearchFailure Failure)
        : StructuralMatchDiscoveryInspectionResult;

    public sealed record Rejected(
        StructuralMatchDiscoveryParticipantRole Role,
        AssemblyContextSubject SeedSubject,
        AssemblyContextSubject CandidateSubject,
        CandidateOpenFailure Failure)
        : StructuralMatchDiscoveryInspectionResult;

    public sealed record Failed(
        StructuralMatchDiscoveryParticipantRole Role,
        AssemblyContextSubject SeedSubject,
        AssemblyContextSubject CandidateSubject,
        StructuralCloneSearchFailure Failure)
        : StructuralMatchDiscoveryInspectionResult;
}

/// <summary>
/// Executes one exact-seed Match discovery through Workspace Structural Clone
/// Search and returns the completed host-neutral envelope.
/// </summary>
public static class StructuralMatchDiscoveryInspection
{
    public static InspectionEnvelope<StructuralMatchDiscoveryInspectionResult>
        Execute(
            StructuralCloneParticipantSnapshot participants,
            int seedMethodDefinitionToken,
            StructuralCloneCandidatePopulation candidatePopulation,
            StructuralCloneRetrievalLimits limits,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(candidatePopulation);
        ArgumentNullException.ThrowIfNull(limits);

        var searchLimits =
            new WorkspaceStructuralCloneSearchLimits(
                MaximumResults: limits.MaximumResults,
                MaximumSeedMethods: 1,
                MaximumCandidateMethods: limits.MaximumMethods,
                MaximumParticipants: 1,
                MaximumRetrievalPairs: int.MaxValue,
                MaximumRetrievalChunkMethods: limits.MaximumMethods,
                ComparisonLimits: limits.ComparisonLimits);
        WorkspaceStructuralCloneSearchResult search =
            WorkspaceStructuralCloneSearchQuery.Execute(
                new WorkspaceStructuralCloneSearchInput(
                    participants,
                    new StructuralCloneSearchSeed
                        .MethodDefinitionToken(
                            seedMethodDefinitionToken),
                    StructuralCloneCandidateBreadth.Self,
                    StructuralCloneCandidateDiscovery.All,
                    searchLimits,
                    candidatePopulation,
                    StructuralCloneSearchEvidence.DetailedRetrievals),
                cancellationToken);
        StructuralMatchDiscoveryInspectionResult result =
            Project(search, participants.ContainingLibrary.Subject);

        return new(
            result,
            new InspectionShare.NonProjectable(
                "structural-match/discovery",
                "Match discovery does not yet have a canonical Workspace "
                    + "Share projection."),
            Diagnostics(result));
    }

    static StructuralMatchDiscoveryInspectionResult Project(
        WorkspaceStructuralCloneSearchResult result,
        AssemblyContextSubject candidateSubject)
        => result switch
        {
            WorkspaceStructuralCloneSearchResult.Rejected rejected =>
                new StructuralMatchDiscoveryInspectionResult.Rejected(
                    StructuralMatchDiscoveryParticipantRole.Seed,
                    rejected.SeedSubject,
                    candidateSubject,
                    rejected.Failure),
            WorkspaceStructuralCloneSearchResult.Failed failed =>
                new StructuralMatchDiscoveryInspectionResult.Failed(
                    StructuralMatchDiscoveryParticipantRole.Seed,
                    failed.SeedSubject,
                    candidateSubject,
                    failed.Failure),
            WorkspaceStructuralCloneSearchResult.Available available =>
                Project(available, candidateSubject),
            _ => throw new InvalidOperationException(
                "Unknown structural clone search result."),
        };

    static StructuralMatchDiscoveryInspectionResult Project(
        WorkspaceStructuralCloneSearchResult.Available result,
        AssemblyContextSubject candidateSubject)
    {
        StructuralCloneSearchSeedCoverage seed =
            AssertSingle(result.Seeds, "seed");
        if (!seed.Failures.IsEmpty)
        {
            return new StructuralMatchDiscoveryInspectionResult.Failed(
                StructuralMatchDiscoveryParticipantRole.Seed,
                result.SeedSubject,
                candidateSubject,
                seed.Failures[0]);
        }

        StructuralCloneSearchLibraryCoverage candidate =
            AssertSingle(
                result.Libraries.Where(
                    static library =>
                        library.Membership
                        == StructuralCloneParticipantMembership
                            .ContainingLibrary),
                "candidate library");
        if (!candidate.Failures.IsEmpty)
        {
            StructuralCloneSearchFailure failure = candidate.Failures[0];
            if (failure.Kind
                == StructuralCloneSearchFailureKind
                    .CandidatePopulationLimitReached)
            {
                return new StructuralMatchDiscoveryInspectionResult
                    .LimitReached(
                        result.SeedSubject,
                        candidateSubject,
                        seed.Seed.Method,
                        candidate.CandidateMethods,
                        Math.Max(0, candidate.CandidateMethods - 1),
                        failure);
            }

            return new StructuralMatchDiscoveryInspectionResult.Failed(
                StructuralMatchDiscoveryParticipantRole.Candidate,
                result.SeedSubject,
                candidateSubject,
                failure);
        }

        StructuralCloneSearchRetrievalEvidence retrieval =
            AssertSingle(seed.Retrievals, "detailed retrieval");
        ImmutableArray<StructuralCloneRetrievalCandidate> candidates =
        [
            .. result.Pairs.Select(
                static pair =>
                    new StructuralCloneRetrievalCandidate(
                        pair.Rank,
                        pair.Right.Method,
                        pair.Similarity)),
        ];
        StructuralCloneRetrievalReceipt rawReceipt =
            retrieval.Retrieval.Receipt;
        var receipt =
            new StructuralCloneRetrievalReceipt(
                rawReceipt.InputMethods,
                rawReceipt.ProcessedMethods,
                SuppressedCandidates:
                    result.Receipt.RankedPairs
                    - candidates.Length
                    + rawReceipt.LimitReachedMethods
                    + rawReceipt.FailedMethods,
                rawReceipt.EligibleMethods,
                rawReceipt.UnsupportedMethods,
                rawReceipt.LimitReachedMethods,
                rawReceipt.FailedMethods,
                RankedCandidates: result.Receipt.RankedPairs,
                ReturnedCandidates: candidates.Length,
                rawReceipt.BodyProductions);
        return new StructuralMatchDiscoveryInspectionResult.Available(
            result.SeedSubject,
            candidateSubject,
            retrieval.Retrieval,
            candidates,
            receipt);
    }

    static T AssertSingle<T>(
        IEnumerable<T> values,
        string description)
    {
        using IEnumerator<T> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException(
                $"Match discovery produced no {description}.");
        }

        T value = enumerator.Current;
        if (enumerator.MoveNext())
        {
            throw new InvalidOperationException(
                $"Match discovery produced more than one {description}.");
        }

        return value;
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        StructuralMatchDiscoveryInspectionResult result)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        switch (result)
        {
            case StructuralMatchDiscoveryInspectionResult.Rejected rejected:
                diagnostics.Add(
                    new(
                        "structural-match-discovery.seed-rejected",
                        InspectionDiagnosticSeverity.Error,
                        rejected.Failure.ToString() ?? "Seed image rejected."));
                break;
            case StructuralMatchDiscoveryInspectionResult.Failed failed:
                AddFailure(diagnostics, failed.Failure);
                break;
            case StructuralMatchDiscoveryInspectionResult.LimitReached limited:
                diagnostics.Add(
                    new(
                        "structural-match-discovery.analysis-incomplete",
                        InspectionDiagnosticSeverity.Warning,
                        limited.Failure.Detail,
                        StructuralCloneRetrievalBlockerKind
                            .MethodLimit.ToString()));
                break;
            case StructuralMatchDiscoveryInspectionResult.Available available:
                foreach (StructuralCloneRetrievalBlocker blocker
                    in available.Retrieval.Blockers)
                {
                    diagnostics.Add(
                        new(
                            "structural-match-discovery.analysis-incomplete",
                            InspectionDiagnosticSeverity.Warning,
                            blocker.Detail,
                            blocker.Kind.ToString()));
                }
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown structural Match discovery result.");
        }

        return diagnostics.ToImmutable();
    }

    static void AddFailure(
        ImmutableArray<InspectionDiagnostic>.Builder diagnostics,
        StructuralCloneSearchFailure failure)
        => diagnostics.Add(
            new(
                "structural-match-discovery.incomplete",
                InspectionDiagnosticSeverity.Error,
                failure.Detail,
                failure.Kind.ToString()));
}
