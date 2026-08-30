using System.Collections.Immutable;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.CSharp;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// The request shape <c>match --similar</c> presents back to the caller, so the rendered output
/// states exactly what was searched and under which limits (issue #4740).
/// </summary>
internal sealed record MatchDiscoveryRequest(
    string Seed,
    string Scope,
    string? CandidateAssembly,
    StructuralCloneRetrievalLimits Limits,
    int? Top);

/// <summary>
/// Token-to-display names for one candidate assembly, projected from the already-extracted
/// <see cref="ApiSurface"/>. Retrieval addresses candidates by MethodDef token; this only supplies
/// a readable label and never participates in selection or ranking.
/// </summary>
internal sealed class MatchDiscoveryNames
{
    readonly Dictionary<int, string> names;

    MatchDiscoveryNames(Dictionary<int, string> names) => this.names = names;

    internal static MatchDiscoveryNames Build(ApiSurface api)
    {
        var names = new Dictionary<int, string>();
        foreach (ApiType type in api.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                foreach (int token in Commands.MatchDiscovery.MemberTokens(member))
                    names.TryAdd(token, $"{type.FullName}.{member.Name}");
            }
        }

        return new MatchDiscoveryNames(names);
    }

    /// <summary>
    /// The member's display name, or its token when the extracted surface does not name it (a
    /// non-public method without <c>--all</c>, or a compiler-generated body). The token is always
    /// shown so every row stays addressable by pairwise <c>match</c>.
    /// </summary>
    internal string Display(int token)
        => names.TryGetValue(token, out string? name)
            ? CSharpIdentifier.ContainRenderedText(name)
            : $"MethodDef 0x{token:X8}";
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class MatchDiscoveryView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }

    public string Seed { get; set; } = "";

    public string Scope { get; set; } = "";

    [MarkoutSkipNull] public string? CandidateAssembly { get; set; }

    public string Disposition { get; set; } = "";

    [MarkoutSkipNull] public string? SeedBody { get; set; }

    public string Limits { get; set; } = "";

    [MarkoutSkipNull] public string? Receipt { get; set; }

    [MarkoutSkipNull] public string? Showing { get; set; }

    [MarkoutSection(Name = "Blockers")]
    [MarkoutSkipNull]
    public List<MatchDiscoveryBlockerRow>? Blockers { get; set; }

    [MarkoutSection(Name = "Ranked Candidates")]
    [MarkoutSkipNull]
    public List<MatchDiscoveryCandidateRow>? Candidates { get; set; }
}

[MarkoutSerializable]
public record MatchDiscoveryBlockerRow(string Kind, string Detail)
{
    /// <summary>Untrusted producer detail is contained here. See <see cref="Detail"/>.</summary>
    public string Detail { get; init; } = CSharpIdentifier.ContainRenderedText(Detail);
}

/// <summary>
/// One ranked row. <c>Score</c> and its components are reproduced exactly as Analysis issued them;
/// this projection performs no arithmetic on them.
/// </summary>
[MarkoutSerializable]
public record MatchDiscoveryCandidateRow(
    int Rank,
    string Member,
    string Token,
    int Score,
    int Operations,
    int Position,
    int Blocks,
    int Edges,
    int Locals);

/// <summary>
/// Complete structured evidence for one seeded retrieval. Every query-returned candidate, outcome,
/// blocker, limit, and receipt is retained here regardless of <c>--top</c>, which bounds only the
/// rendered text rows.
/// </summary>
public sealed record MatchDiscoveryDocument(
    string Seed,
    string Scope,
    string? CandidateAssembly,
    string Disposition,
    string Disclosure,
    MatchDiscoveryLimitsDocument Limits,
    MatchDiscoverySeedDocument? SeedOutcome,
    MatchDiscoveryReceiptDocument? Receipt,
    ImmutableArray<MatchDiscoveryBlockerDocument> Blockers,
    ImmutableArray<MatchDiscoveryCandidateDocument> Candidates,
    MatchDiscoveryFailureDocument? Failure);

public sealed record MatchDiscoveryLimitsDocument(
    int MaximumMethods,
    int MaximumResults,
    int? TextRows);

public sealed record MatchDiscoverySeedDocument(
    string Member,
    string Token,
    string Disposition,
    ImmutableArray<MatchDiscoveryBlockerDocument> Blockers);

public sealed record MatchDiscoveryReceiptDocument(
    int InputMethods,
    int ProcessedMethods,
    int SuppressedCandidates,
    int EligibleMethods,
    int UnsupportedMethods,
    int LimitReachedMethods,
    int FailedMethods,
    int RankedCandidates,
    int ReturnedCandidates,
    int BodyProductions);

public sealed record MatchDiscoveryBlockerDocument(string Kind, string Detail);

public sealed record MatchDiscoveryCandidateDocument(
    int Rank,
    string Member,
    string Token,
    MatchDiscoverySimilarityDocument Similarity);

/// <summary>
/// Analysis-issued similarity evidence, reproduced verbatim. Scores range from zero through
/// 10,000 and select candidates; they do not establish a clone relation.
/// </summary>
public sealed record MatchDiscoverySimilarityDocument(
    int Score,
    int OperationScore,
    int PositionScore,
    int BlockScore,
    int EdgeScore,
    int LocalScore,
    int SeedInstructions,
    int CandidateInstructions,
    int SeedBlocks,
    int CandidateBlocks,
    int SeedEdges,
    int CandidateEdges,
    int SeedLocals,
    int CandidateLocals);

public sealed record MatchDiscoveryFailureDocument(
    string Kind,
    string Role,
    string Detail);

internal static class MatchDiscoveryFormatter
{
    internal const string RejectedDisposition = "Rejected";
    internal const string UnresolvedDisposition = "Unresolved";

    /// <summary>
    /// Retrieval ranks structural candidates. It is a selection step, not a verdict: use pairwise
    /// <c>match</c> on a ranked row to obtain a checked relation.
    /// </summary>
    internal const string Disclosure =
        "Ranks structural candidates only. A rank does not establish Exact, Near, or Different, "
            + "nor semantic equivalence, authorship, copying intent, or vulnerability. "
            + "Run pairwise `match` on a candidate to obtain a checked relation.";

    internal static (MatchDiscoveryView View, MatchDiscoveryDocument Document) BuildView(
        MatchDiscoveryRequest request,
        AssemblyContextStructuralCloneRetrievalResult result,
        MatchDiscoveryNames names)
        => result switch
        {
            AssemblyContextStructuralCloneRetrievalResult.Available available =>
                BuildAvailable(request, available, names),
            AssemblyContextStructuralCloneRetrievalResult.Rejected rejected =>
                BuildTerminal(
                    request,
                    RejectedDisposition,
                    new MatchDiscoveryFailureDocument(
                        rejected.Failure.GetType().Name,
                        rejected.Role.ToString(),
                        rejected.Failure.ToString() ?? "")),
            AssemblyContextStructuralCloneRetrievalResult.Failed failed =>
                BuildTerminal(
                    request,
                    UnresolvedDisposition,
                    new MatchDiscoveryFailureDocument(
                        failed.Failure.Kind.ToString(),
                        failed.Failure.Role.ToString(),
                        failed.Failure.Detail)),
            _ => throw new InvalidOperationException(
                $"Unhandled retrieval result '{result.GetType().Name}'."),
        };

    static (MatchDiscoveryView, MatchDiscoveryDocument) BuildTerminal(
        MatchDiscoveryRequest request,
        string disposition,
        MatchDiscoveryFailureDocument failure)
    {
        var view = NewView(request, disposition);
        view.Blockers =
        [
            new MatchDiscoveryBlockerRow(
                $"{failure.Role}/{failure.Kind}",
                failure.Detail),
        ];

        var document = new MatchDiscoveryDocument(
            request.Seed,
            request.Scope,
            request.CandidateAssembly,
            disposition,
            Disclosure,
            LimitsOf(request),
            SeedOutcome: null,
            Receipt: null,
            Blockers: [],
            Candidates: [],
            Failure: failure);
        return (view, document);
    }

    static (MatchDiscoveryView, MatchDiscoveryDocument) BuildAvailable(
        MatchDiscoveryRequest request,
        AssemblyContextStructuralCloneRetrievalResult.Available available,
        MatchDiscoveryNames names)
    {
        StructuralCloneRetrievalResult retrieval = available.Retrieval;
        var view = NewView(request, retrieval.Disposition.ToString());

        view.SeedBody = retrieval.Seed.Disposition.ToString();
        StructuralCloneRetrievalReceipt receipt = retrieval.Receipt;
        view.Receipt =
            $"{receipt.EligibleMethods} eligible of {receipt.InputMethods} scanned; "
                + $"{receipt.RankedCandidates} ranked, {receipt.ReturnedCandidates} returned "
                + $"({receipt.UnsupportedMethods} unsupported, {receipt.LimitReachedMethods} limit-reached, "
                + $"{receipt.FailedMethods} failed)";

        if (!retrieval.Blockers.IsEmpty)
        {
            view.Blockers = retrieval.Blockers
                .Select(blocker => new MatchDiscoveryBlockerRow(
                    blocker.Kind.ToString(), blocker.Detail))
                .ToList();
        }

        ImmutableArray<StructuralCloneRetrievalCandidate> candidates = retrieval.Candidates;
        if (!candidates.IsEmpty)
        {
            // --top bounds the rendered rows only. The document below keeps every candidate.
            IEnumerable<StructuralCloneRetrievalCandidate> shown = request.Top is int top
                ? candidates.Take(top)
                : candidates;
            view.Candidates = shown
                .Select(candidate => new MatchDiscoveryCandidateRow(
                    candidate.Rank,
                    names.Display(candidate.Method.Token),
                    $"0x{candidate.Method.Token:X8}",
                    candidate.Similarity.Score,
                    candidate.Similarity.OperationScore,
                    candidate.Similarity.PositionScore,
                    candidate.Similarity.BlockScore,
                    candidate.Similarity.EdgeScore,
                    candidate.Similarity.LocalScore))
                .ToList();

            if (request.Top is int limit && candidates.Length > limit)
                view.Showing = $"{limit} of {candidates.Length} ranked candidates";
        }

        var document = new MatchDiscoveryDocument(
            request.Seed,
            request.Scope,
            request.CandidateAssembly,
            retrieval.Disposition.ToString(),
            Disclosure,
            LimitsOf(request),
            new MatchDiscoverySeedDocument(
                names.Display(retrieval.Seed.Method.Token),
                $"0x{retrieval.Seed.Method.Token:X8}",
                retrieval.Seed.Disposition.ToString(),
                [.. retrieval.Seed.Blockers.Select(
                    blocker => new MatchDiscoveryBlockerDocument(
                        blocker.Kind.ToString(), blocker.Detail))]),
            new MatchDiscoveryReceiptDocument(
                receipt.InputMethods,
                receipt.ProcessedMethods,
                receipt.SuppressedCandidates,
                receipt.EligibleMethods,
                receipt.UnsupportedMethods,
                receipt.LimitReachedMethods,
                receipt.FailedMethods,
                receipt.RankedCandidates,
                receipt.ReturnedCandidates,
                receipt.BodyProductions),
            [.. retrieval.Blockers.Select(
                blocker => new MatchDiscoveryBlockerDocument(
                    blocker.Kind.ToString(), blocker.Detail))],
            [.. candidates.Select(candidate => new MatchDiscoveryCandidateDocument(
                candidate.Rank,
                names.Display(candidate.Method.Token),
                $"0x{candidate.Method.Token:X8}",
                Similarity(candidate.Similarity)))],
            Failure: null);

        return (view, document);
    }

    static MatchDiscoveryView NewView(MatchDiscoveryRequest request, string disposition)
        => new()
        {
            Title = $"Similar to: {CSharpIdentifier.ContainRenderedText(request.Seed)}",
            Description = Disclosure,
            Seed = CSharpIdentifier.ContainRenderedText(request.Seed),
            Scope = CSharpIdentifier.ContainRenderedText(request.Scope),
            CandidateAssembly = request.CandidateAssembly is null
                ? null
                : CSharpIdentifier.ContainRenderedText(request.CandidateAssembly),
            Disposition = disposition,
            Limits =
                $"max-methods {request.Limits.MaximumMethods}, "
                    + $"max-results {request.Limits.MaximumResults}",
        };

    static MatchDiscoveryLimitsDocument LimitsOf(MatchDiscoveryRequest request)
        => new(
            request.Limits.MaximumMethods,
            request.Limits.MaximumResults,
            request.Top);

    static MatchDiscoverySimilarityDocument Similarity(StructuralCloneSimilarityEvidence evidence)
        => new(
            evidence.Score,
            evidence.OperationScore,
            evidence.PositionScore,
            evidence.BlockScore,
            evidence.EdgeScore,
            evidence.LocalScore,
            evidence.SeedInstructions,
            evidence.CandidateInstructions,
            evidence.SeedBlocks,
            evidence.CandidateBlocks,
            evidence.SeedEdges,
            evidence.CandidateEdges,
            evidence.SeedLocals,
            evidence.CandidateLocals);
}
