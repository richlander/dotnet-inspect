using System.Collections.Immutable;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
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
    int? Top,
    string? CandidatePackage = null,
    string? CandidateTfm = null,
    string? ReplayLibrary = null,
    MatchDiscoveryReplaySources? ReplaySources = null);

internal sealed record MatchDiscoveryReplaySources(
    ImmutableArray<string> Sources,
    ImmutableArray<string> AdditionalSources,
    string? ConfigFile);

/// <summary>
/// Token-to-display names for one candidate assembly, projected from the already-extracted
/// <see cref="ApiSurface"/>. Retrieval addresses candidates by MethodDef token; this only supplies
/// a readable label and never participates in selection or ranking.
/// </summary>
internal sealed class MatchDiscoveryNames
{
    readonly Dictionary<int, string> names;

    MatchDiscoveryNames(Dictionary<int, string> names) => this.names = names;

    /// <summary>
    /// Builds the token-to-name projection for one image, keeping only the types that image
    /// actually defines. An <see cref="ApiSurface"/> also describes the types the image forwards,
    /// whose tokens index the defining assembly; admitting them lets a forwarded type shadow a
    /// local row and label it with a name from another image.
    /// </summary>
    internal static MatchDiscoveryNames Build(ApiSurface api, string image)
    {
        var names = new Dictionary<int, string>();
        foreach (ApiType type in api.Types)
        {
            if (!Commands.MatchCommand.DefinesOwnRows(type, image))
                continue;

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
    /// shown so a same-image row stays addressable by pairwise <c>match</c>. Across two images
    /// the token still identifies the row within its own image, but it is not addressable by
    /// pairwise <c>match</c>, which compares two methods inside one retained assembly.
    /// </summary>
    internal string Display(MetadataMethodAddress address)
        => names.TryGetValue(address.Token, out string? name)
            ? CSharpIdentifier.ContainRenderedText(name)
            : $"MethodDef 0x{address.Token:X8}";
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class MatchDiscoveryView
{
    // Every string column is contained on the way in. Seed, Scope, Title, and
    // CandidateAssembly carry inspected-assembly names and paths; the rest are
    // tool-composed, and containing them too keeps the rule uniform rather than
    // asking each new column to justify an exemption.
    [MarkoutIgnore]
    public string Title { get => field; set => field = Contain(value); } = "";

    [MarkoutIgnore]
    [MarkoutSkipNull]
    public string? Description { get => field; set => field = ContainOptional(value); }

    public string Seed { get => field; set => field = Contain(value); } = "";

    public string Scope { get => field; set => field = Contain(value); } = "";

    [MarkoutSkipNull]
    public string? CandidateAssembly { get => field; set => field = ContainOptional(value); }

    public string Disposition { get => field; set => field = Contain(value); } = "";

    [MarkoutSkipNull]
    public string? SeedBody { get => field; set => field = ContainOptional(value); }

    public string Limits { get => field; set => field = Contain(value); } = "";

    [MarkoutSkipNull]
    public string? Receipt { get => field; set => field = ContainOptional(value); }

    [MarkoutSkipNull]
    public string? Showing { get => field; set => field = ContainOptional(value); }

    [MarkoutSection(Name = "Blockers")]
    [MarkoutSkipNull]
    public List<MatchDiscoveryBlockerRow>? Blockers { get; set; }

    [MarkoutSection(Name = "Ranked Candidates")]
    [MarkoutSkipNull]
    public List<MatchDiscoveryCandidateRow>? Candidates { get; set; }

    private static string Contain(string value) => CSharpIdentifier.ContainRenderedText(value);

    private static string? ContainOptional(string? value) =>
        value is null ? null : CSharpIdentifier.ContainRenderedText(value);
}

/// <summary>
/// The tabular projection of a discovery result. <c>--table</c>, <c>--tsv</c>, and <c>--jsonl</c>
/// require exactly one table shape (see <c>docs/design/output-shapes.md</c>), and <c>match</c>
/// carries no section-selection options, so the ranked candidates are that one shape. The seed,
/// scope, receipt, blockers, and disclosure travel on stderr instead of adding a second row
/// schema to the parsed stream.
/// </summary>
[MarkoutSerializable]
public class MatchDiscoveryCandidateTableView
{
    [MarkoutSection(Name = "Ranked Candidates")]
    public List<MatchDiscoveryCandidateRow> Candidates { get; set; } = [];
}

[MarkoutSerializable]
public record MatchDiscoveryBlockerRow(string Kind, string Detail)
{
    public string Kind { get; init; } = CSharpIdentifier.ContainRenderedText(Kind);

    /// <summary>Untrusted producer detail is contained here. See <see cref="Detail"/>.</summary>
    public string Detail { get; init; } = CSharpIdentifier.ContainRenderedText(Detail);
}

/// <summary>
/// One ranked row. <c>Score</c> and its components are reproduced exactly as Analysis issued them;
/// this projection performs no arithmetic on them.
/// </summary>
[MarkoutSerializable]
// Declared as explicit properties rather than positional parameters because
// containing a positional parameter requires redeclaring it in the body, which
// moves that column to the end of the rendered table.
public record MatchDiscoveryCandidateRow
{
    public int Rank { get; init; }

    /// <summary>Projected from inspected metadata, so it is contained here.</summary>
    public string Member { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public string Token { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public int Score { get; init; }

    public int Operations { get; init; }

    public int Position { get; init; }

    public int Blocks { get; init; }

    public int Edges { get; init; }

    public int Locals { get; init; }
}

/// <summary>
/// Complete structured evidence for one seeded retrieval. Every query-returned candidate, outcome,
/// blocker, limit, and receipt is retained here regardless of <c>--top</c>, which bounds only the
/// rendered text rows.
/// </summary>
public sealed record MatchDiscoveryDocument
{
    /// <summary>
    /// The seed, scope, and candidate-assembly spellings are metadata- or path-derived, so the
    /// document contains them itself. Containing at the construction site instead would make the
    /// guarantee a per-caller discipline, and <c>MarkoutRowContainmentTests</c> cannot enforce it
    /// here: that gate covers Markout views, and a JSON document is not one. JSON escaping is not
    /// containment — a parser restores the original control character.
    /// </summary>
    public required string Seed { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Scope { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public string? CandidateAssembly
    {
        get => field;
        init => field = value is null ? null : CSharpIdentifier.ContainRenderedText(value);
    }

    public required string Disposition { get; init; }

    /// <summary>
    /// Contained like <see cref="CandidateAssembly"/>: this sentence embeds the same
    /// metadata-derived path, so leaving it raw would reinstate through the prose exactly what
    /// containing the field removes.
    /// </summary>
    public required string Disclosure
    {
        get => field;
        init => field = CSharpIdentifier.ContainRenderedText(value);
    }

    public required MatchDiscoveryLimitsDocument Limits { get; init; }

    public MatchDiscoverySeedDocument? SeedOutcome { get; init; }

    public MatchDiscoveryReceiptDocument? Receipt { get; init; }

    public ImmutableArray<MatchDiscoveryBlockerDocument> Blockers { get; init; } = [];

    public ImmutableArray<MatchDiscoveryCandidateDocument> Candidates { get; init; } = [];

    public ImmutableArray<MatchDiscoveryMethodOutcomeDocument> MethodOutcomes { get; init; } = [];

    public MatchDiscoveryFailureDocument? Failure { get; init; }
}

/// <summary>
/// One candidate method's retrieval outcome, including the methods that produced no candidate.
/// The receipt counts them in aggregate; this is what says which method each count refers to.
/// </summary>
public sealed record MatchDiscoveryMethodOutcomeDocument
{
    /// <summary>Projected from inspected metadata, so it is contained here.</summary>
    public required string Member { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Token { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Disposition { get; init; }

    public ImmutableArray<MatchDiscoveryBlockerDocument> Blockers { get; init; } = [];

    public int BodyBytes { get; init; }

    public int Instructions { get; init; }

    public int Blocks { get; init; }

    public int Edges { get; init; }

    public int Locals { get; init; }
}

public sealed record MatchDiscoveryLimitsDocument(
    int MaximumMethods,
    int MaximumResults,
    int? TextRows);

public sealed record MatchDiscoverySeedDocument
{
    /// <summary>Projected from inspected metadata, so it is contained here.</summary>
    public required string Member { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Token { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Disposition { get; init; }

    public ImmutableArray<MatchDiscoveryBlockerDocument> Blockers { get; init; } = [];
}

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

public sealed record MatchDiscoveryBlockerDocument
{
    /// <summary>Projected from inspected metadata, so it is contained here.</summary>
    public required string Kind { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Detail { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";
}

public sealed record MatchDiscoveryCandidateDocument
{
    public int Rank { get; init; }

    /// <summary>Projected from inspected metadata, so it is contained here.</summary>
    public required string Member { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Token { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required MatchDiscoverySimilarityDocument Similarity { get; init; }
}

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

/// <summary>
/// Why a retrieval produced no result. <see cref="Detail"/> is metadata-derived — the query layer
/// spells a missing or ambiguous target as <c>Type '…' does not exist.</c> and reports a metadata
/// exception's own message — so this record contains it for the same reason every other document
/// here does, rather than leaving containment to the one construction site that happens to build
/// it today.
/// </summary>
public sealed record MatchDiscoveryFailureDocument
{
    public required string Kind { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Role { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";

    public required string Detail { get => field; init => field = CSharpIdentifier.ContainRenderedText(value); } = "";
}

internal static class MatchDiscoveryFormatter
{
    internal const string RejectedDisposition = "Rejected";
    internal const string UnresolvedDisposition = "Unresolved";

    /// <summary>
    /// The one table shape that <c>--table</c>, <c>--tsv</c>, and <c>--jsonl</c> may emit. A run
    /// that ranked nothing yields an empty table; its blockers and disposition reach the reader
    /// through <see cref="TabularContext"/> and the exit code.
    /// </summary>
    internal static MatchDiscoveryCandidateTableView CandidateTable(MatchDiscoveryView view)
        => new() { Candidates = view.Candidates ?? [] };

    /// <summary>
    /// Everything the full view carries that the single tabular table cannot. Emitted as stderr
    /// notes so a failed or blocked retrieval stays visible without adding a second row schema to
    /// the parsed stream.
    /// </summary>
    internal static IEnumerable<string> TabularContext(MatchDiscoveryView view)
    {
        yield return $"Seed: {view.Seed}";
        yield return $"Scope: {view.Scope}";
        if (view.CandidateAssembly is string assembly)
            yield return $"Candidate assembly: {assembly}";

        yield return $"Disposition: {view.Disposition}";
        yield return $"Limits: {view.Limits}";
        if (view.Receipt is string receipt)
            yield return $"Receipt: {receipt}";

        // Truncation provenance travels with the rows it truncates. A persisted table that omits
        // it reads as the complete ranking rather than the first --top rows of it.
        if (view.Showing is string showing)
            yield return $"Showing: {showing}";

        foreach (MatchDiscoveryBlockerRow blocker in view.Blockers ?? [])
            yield return $"Blocker {blocker.Kind}: {blocker.Detail}";
    }

    /// <summary>
    /// Retrieval ranks structural candidates. It is a selection step, not a verdict. A ranked row
    /// is addressable by pairwise <c>match</c>, which is what the printed token promises — but a
    /// MethodDef token is a row index in one image, so the promise is only good against the image
    /// that owns the row. When type forwarding puts the population in the assembly that defines
    /// it rather than the facade the caller opened, the disclosure names that assembly, because
    /// telling the reader to run pairwise <c>match</c> without saying which <c>--library</c> to
    /// pass would name a transition their next command cannot perform.
    /// </summary>
    internal const string DisclosurePrefix =
        "Ranks structural candidates only. A rank does not establish Exact, Near, or Different, "
            + "nor semantic equivalence, authorship, copying intent, or vulnerability. ";

    internal const string Disclosure =
        DisclosurePrefix + "Run pairwise `match` on a candidate to obtain a checked relation.";

    /// <summary>
    /// A run that carries a candidate assembly is exactly the run whose ranked tokens index an
    /// image other than the one the caller named, because discovery leaves the candidate assembly
    /// null when the population lives in the caller's own image.
    /// <para>
    /// The address must be one the caller can still use after this process exits. A package is
    /// extracted to a temporary directory that the command deletes, so a package-sourced run
    /// discloses the package and the library inside it rather than the extraction path, including
    /// when that package image is also the image the caller named.
    /// </para>
    /// </summary>
    internal static string DisclosureFor(MatchDiscoveryRequest request)
        => request.CandidateAssembly is string candidateAssembly
            ? DisclosurePrefix
                + "Ranked tokens index "
                + Path.GetFileName(candidateAssembly)
                + ", which defines them rather than the assembly named on the command line; run "
                + "pairwise `match` in "
                + ShellCommandText.CurrentDialectName
                + " on a candidate with `"
                + ReplayOptions(request, candidateAssembly)
                + "` to obtain a checked relation."
            : request.CandidatePackage is string
                && request.ReplayLibrary is string replayLibrary
                    ? DisclosurePrefix
                        + "Ranked tokens index the package image selected for this run; run "
                        + "pairwise `match` in "
                        + ShellCommandText.CurrentDialectName
                        + " on a candidate with `"
                        + ReplayOptions(request, replayLibrary)
                        + "` to obtain a checked relation against that same image."
                    : Disclosure;

    static string ReplayOptions(MatchDiscoveryRequest request, string library)
    {
        var options = new List<string>();
        if (request.CandidatePackage is string candidatePackage)
        {
            options.Add("--package " + ShellCommandText.Quote(candidatePackage));
            options.Add("--library " + ShellCommandText.Quote(library));
            if (request.CandidateTfm is string candidateTfm)
                options.Add("--tfm " + ShellCommandText.Quote(candidateTfm));

            if (request.ReplaySources is { } sources)
            {
                options.AddRange(sources.Sources.Select(
                    source => "--source " + ShellCommandText.Quote(source)));
                options.AddRange(sources.AdditionalSources.Select(
                    source => "--add-source " + ShellCommandText.Quote(source)));
                if (sources.ConfigFile is string configFile)
                    options.Add("--nugetconfig " + ShellCommandText.Quote(configFile));
            }
        }
        else
        {
            options.Add("--library " + ShellCommandText.Quote(library));
        }

        return string.Join(' ', options);
    }

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
                    new MatchDiscoveryFailureDocument
                    {
                        Kind = rejected.Failure.GetType().Name,
                        Role = rejected.Role.ToString(),
                        Detail = rejected.Failure.ToString() ?? "",
                    }),
            AssemblyContextStructuralCloneRetrievalResult.Failed failed =>
                BuildTerminal(
                    request,
                    UnresolvedDisposition,
                    new MatchDiscoveryFailureDocument
                    {
                        Kind = failed.Failure.Kind.ToString(),
                        Role = failed.Failure.Role.ToString(),
                        Detail = failed.Failure.Detail,
                    }),
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

        var document = new MatchDiscoveryDocument
        {
            Seed = request.Seed,
            Scope = request.Scope,
            CandidateAssembly = request.CandidateAssembly,
            Disposition = disposition,
            Disclosure = DisclosureFor(request),
            Limits = LimitsOf(request),
            Failure = failure,
        };
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

        // Input and processed are different numbers and the difference is the honest part: a
        // limit rejects the population atomically, so nothing is scanned even though the input
        // was large. Reporting the input count as "scanned" claims work the tool never did.
        view.Receipt =
            $"{receipt.EligibleMethods} eligible of {receipt.ProcessedMethods} processed "
                + $"({receipt.InputMethods} input); "
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
                .Select(candidate => new MatchDiscoveryCandidateRow
                {
                    Rank = candidate.Rank,
                    Member = names.Display(candidate.Method),
                    Token = $"0x{candidate.Method.Token:X8}",
                    Score = candidate.Similarity.Score,
                    Operations = candidate.Similarity.OperationScore,
                    Position = candidate.Similarity.PositionScore,
                    Blocks = candidate.Similarity.BlockScore,
                    Edges = candidate.Similarity.EdgeScore,
                    Locals = candidate.Similarity.LocalScore,
                })
                .ToList();

            if (request.Top is int limit && candidates.Length > limit)
                view.Showing = $"{limit} of {candidates.Length} returned candidates";
        }

        var document = new MatchDiscoveryDocument
        {
            Seed = request.Seed,
            Scope = request.Scope,
            CandidateAssembly = request.CandidateAssembly,
            Disposition = retrieval.Disposition.ToString(),
            Disclosure = DisclosureFor(request),
            Limits = LimitsOf(request),
            SeedOutcome = new MatchDiscoverySeedDocument
            {
                // The seed's own resolved display, never a lookup in the candidate name map: the
                // seed can live in a different image, where its token names another member.
                Member = request.Seed,
                Token = $"0x{retrieval.Seed.Method.Token:X8}",
                Disposition = retrieval.Seed.Disposition.ToString(),
                Blockers = [.. retrieval.Seed.Blockers.Select(Blocker)],
            },
            Receipt = new MatchDiscoveryReceiptDocument(
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
            Blockers = [.. retrieval.Blockers.Select(Blocker)],
            Candidates = [.. candidates.Select(candidate => new MatchDiscoveryCandidateDocument
            {
                Rank = candidate.Rank,
                Member = names.Display(candidate.Method),
                Token = $"0x{candidate.Method.Token:X8}",
                Similarity = Similarity(candidate.Similarity),
            })],
            // Never bounded by --top: this is the per-method evidence behind the receipt counts.
            MethodOutcomes =
            [
                .. retrieval.Methods.Select(outcome => new MatchDiscoveryMethodOutcomeDocument
                {
                    Member = names.Display(outcome.Method),
                    Token = $"0x{outcome.Method.Token:X8}",
                    Disposition = outcome.Disposition.ToString(),
                    Blockers = [.. outcome.Blockers.Select(Blocker)],
                    BodyBytes = outcome.Receipt.BodyBytes,
                    Instructions = outcome.Receipt.Instructions,
                    Blocks = outcome.Receipt.Blocks,
                    Edges = outcome.Receipt.Edges,
                    Locals = outcome.Receipt.Locals,
                }),
            ],
        };

        return (view, document);
    }

    static MatchDiscoveryView NewView(MatchDiscoveryRequest request, string disposition)
        => new()
        {
            Title = $"Similar to: {CSharpIdentifier.ContainRenderedText(request.Seed)}",
            Description = DisclosureFor(request),
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

    static MatchDiscoveryBlockerDocument Blocker(StructuralCloneBlocker blocker)
        => new() { Kind = blocker.Kind.ToString(), Detail = blocker.Detail };

    static MatchDiscoveryBlockerDocument Blocker(StructuralCloneRetrievalBlocker blocker)
        => new() { Kind = blocker.Kind.ToString(), Detail = blocker.Detail };

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
