using System.Globalization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Views;
using ILInspector.Analysis;
using InertText;
using Markout;

namespace DotnetInspector.Sections;

/// <summary>
/// Projects the shared assembly Package Query event stream into the CLI's
/// rendered document.
/// </summary>
/// <remarks>
/// The projection is total over the owner's closed outcome set: a match, a
/// semantically confirmed miss, an inapplicable candidate, an evaluation
/// failure, and an acquisition failure each produce their own candidate row,
/// so none of them can be rendered as success-shaped empty output. Only
/// evaluated candidates carry an owner-issued Root reopening token, because an
/// acquisition failure never produced one.
/// </remarks>
public static class PackageAssemblyQuerySections
{
    public const string Matches = "Matches";
    public const string Candidates = "Candidates";

    /// <summary>
    /// The scope this query covers, stated wherever its results are rendered.
    /// </summary>
    public const string Scope =
        "Selected primary implementation assemblies only; not all package assemblies.";

    public static DocumentSchema CreateSchema() =>
        SearchViewContext.Default
            .GetSchemaInfo<PackageAssemblyQueryView>()!
            .ToDocumentSchema();

    public static PackageAssemblyQueryView CreateDocument(
        PackageAssemblyQueryPlan plan,
        IReadOnlyList<PackageAssemblyQueryEvent> events)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(events);

        var matches = new List<PackageAssemblyLiteralUseRow>();
        var candidates = new List<PackageAssemblyCandidateRow>();
        PackageAssemblyQuerySummary? summary = null;
        foreach (PackageAssemblyQueryEvent queryEvent in events)
        {
            switch (queryEvent)
            {
                case PackageAssemblyQueryEvent.Progress:
                    break;
                case PackageAssemblyQueryEvent.Evaluated evaluated:
                    AddOutcome(matches, candidates, evaluated.Value);
                    break;
                case PackageAssemblyQueryEvent.AcquisitionFailed failed:
                    candidates.Add(AcquisitionFailureRow(failed.Value));
                    break;
                case PackageAssemblyQueryEvent.Completed completed:
                    summary = completed.Value;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown package assembly query event.");
            }
        }

        if (summary is null)
        {
            throw new InvalidOperationException(
                "The package assembly query stream ended without a completion summary.");
        }

        return new PackageAssemblyQueryView(
            InertString.Format(
                TextPolicy.Prose,
                $"Find literal: {plan.Pattern.Operand.DisplayText}"),
            Describe(plan, summary, matches.Count))
        {
            CandidateCount = summary.Candidates,
            MatchedCandidateCount = summary.Matches,
            SemanticMissCount = summary.SemanticMisses,
            NotApplicableCount = summary.NotApplicable,
            FailureCount = summary.Failures,
            Matches = matches.Count == 0 ? null : matches,
            Candidates = candidates.Count == 0 ? null : candidates,
        };
    }

    public static int CountMatchRows(PackageAssemblyQueryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Matches?.Count ?? 0;
    }

    static InertString Describe(
        PackageAssemblyQueryPlan plan,
        PackageAssemblyQuerySummary summary,
        int matchRows)
    {
        string lead = matchRows == 0
            ? "No matching decoded string literal uses were reported."
            : $"{Count(matchRows)} decoded string literal uses in {Count(summary.Matches)} of {Count(summary.Candidates)} package candidates.";
        return InertString.Format(
            TextPolicy.Prose,
            $"{lead} {Scope} Target framework {plan.TargetFramework}; "
            + $"misses {Count(summary.SemanticMisses)}, not applicable {Count(summary.NotApplicable)}, failures {Count(summary.Failures)}.");
    }

    static void AddOutcome(
        List<PackageAssemblyLiteralUseRow> matches,
        List<PackageAssemblyCandidateRow> candidates,
        PackageAssemblyEvaluationOutcome outcome)
    {
        PackageAssemblyEvaluationSubject subject = outcome.Subject;
        InertString package = Cell(subject.Coordinate.PackageId);
        InertString version = Cell(subject.Coordinate.Version);
        InertString root = Cell(subject.RootRequest.Encode());
        InertString asset = outcome.SelectedAsset is { } selected
            ? selected.Asset.Path
            : EmptyCell;
        InertString targetFramework = outcome.SelectedAsset is { } selectedAsset
            ? selectedAsset.Asset.TargetFramework
            : EmptyCell;
        switch (outcome)
        {
            case PackageAssemblyEvaluationOutcome.Matched
                { SelectedAsset: { } matched } match:
                foreach (StringLiteralUseOccurrence occurrence
                    in match.Evidence.Occurrences)
                {
                    matches.Add(new PackageAssemblyLiteralUseRow(
                        package,
                        version,
                        matched.Asset.AssemblyName,
                        Cell(FormatMethod(occurrence.Address.MethodDefinitionToken)),
                        Cell(FormatOffset(occurrence.Address.ILOffset)),
                        occurrence.LiteralText));
                }

                candidates.Add(new PackageAssemblyCandidateRow(
                    package,
                    version,
                    MatchedOutcome,
                    asset,
                    targetFramework,
                    Cell(
                        $"{Count(match.Evidence.Occurrences.Length)} literal uses; "
                        + $"{Count(matched.UnevaluatedSiblings)} sibling assemblies not evaluated."),
                    root));
                break;
            case PackageAssemblyEvaluationOutcome.NoMatch noMatch:
                candidates.Add(new PackageAssemblyCandidateRow(
                    package,
                    version,
                    NoMatchOutcome,
                    asset,
                    targetFramework,
                    Cell(
                        "The selected implementation assembly has no matching decoded ldstr use "
                        + $"after {Count(noMatch.Receipt.MethodBodiesVisited)} method bodies."),
                    root));
                break;
            case PackageAssemblyEvaluationOutcome.NotApplicable notApplicable:
                candidates.Add(new PackageAssemblyCandidateRow(
                    package,
                    version,
                    NotApplicableOutcome,
                    asset,
                    targetFramework,
                    Cell(Describe(notApplicable.Reason)),
                    root));
                break;
            case PackageAssemblyEvaluationOutcome.Failure failure:
                candidates.Add(new PackageAssemblyCandidateRow(
                    package,
                    version,
                    FailedOutcome,
                    asset,
                    targetFramework,
                    Cell(Describe(failure)),
                    root));
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown package assembly evaluation outcome.");
        }
    }

    static PackageAssemblyCandidateRow AcquisitionFailureRow(
        PackageAssemblyQueryAcquisitionFailure failure)
    {
        InertString detail = failure.SourceFailureKind is { } kind
            ? InertString.Format(
                TextPolicy.Field,
                $"{failure.Producer} ({kind}): {failure.Message}")
            : InertString.Format(
                TextPolicy.Field,
                $"{failure.Producer}: {failure.Message}");
        return new(
            Cell(failure.Coordinate.PackageId),
            Cell(failure.Coordinate.Version),
            FailedOutcome,
            EmptyCell,
            EmptyCell,
            detail,
            EmptyCell);
    }

    static string Describe(PackageAssemblyNotApplicableReason reason) =>
        reason switch
        {
            PackageAssemblyNotApplicableReason.NoCompileAssets =>
                "No compile assembly is available.",
            PackageAssemblyNotApplicableReason.NoMatchingTargetFramework =>
                "No compile group matches the requested framework.",
            PackageAssemblyNotApplicableReason.EmptyCompileGroup =>
                "The selected compile group is explicitly empty.",
            PackageAssemblyNotApplicableReason.NoImplementationCounterpart =>
                "The primary compile assembly has no implementation counterpart.",
            _ => throw new InvalidOperationException(
                "Unknown package assembly applicability outcome."),
        };

    /// <summary>
    /// Describes a failure by the owner's stage, keeping the reason subtype's
    /// own facts rather than flattening every failure to one message.
    /// </summary>
    static string Describe(PackageAssemblyEvaluationOutcome.Failure failure)
    {
        string detail = failure.Reason switch
        {
            PackageAssemblyFailureReason.InvalidSelection selection =>
                $"Asset selection reported {selection.Status}.",
            PackageAssemblyFailureReason.EntryByteLimit limit =>
                $"The selected entry exceeds the {Count(limit.MaximumEntryBytes)}-byte entry limit "
                + $"or the {Count(limit.MaximumRetainedImageBytes)}-byte retained-image limit.",
            PackageAssemblyFailureReason.ArtifactPublication publication =>
                "Artifact publication was refused: "
                + string.Join(
                    ", ",
                    publication.Failures.Select(
                        static value => $"{value.Kind}/{value.DiagnosticCode}")),
            PackageAssemblyFailureReason.NotAssembly notAssembly =>
                $"The selected entry is not an assembly ({notAssembly.AdmissionStage}: {notAssembly.Kind}).",
            PackageAssemblyFailureReason.ProjectionRejected projection =>
                $"Assembly projection was refused: {projection.Failure}.",
            PackageAssemblyFailureReason.QueryRejected query =>
                $"The assembly query was refused: {query.Failure}.",
            PackageAssemblyFailureReason.SemanticRejection rejection =>
                $"The literal producer rejected the assembly: {rejection.Rejection.Kind} "
                + $"at method 0x{rejection.Rejection.Site.MethodDefinitionToken:X8}.",
            PackageAssemblyFailureReason.SemanticWorkLimit limit =>
                $"The literal producer reached its {limit.Limit} work limit.",
            PackageAssemblyFailureReason.CandidateCleanup cleanup =>
                $"Candidate cleanup was incomplete: {DescribeCleanup(cleanup.Evidence)}.",
            _ => "",
        };
        string stage = $"Assembly evaluation failed at {failure.Reason.Stage}.";
        string incomplete = failure.Cleanup is { } evidence
            ? $" Candidate cleanup was incomplete: {DescribeCleanup(evidence)}."
            : "";
        return detail.Length == 0
            ? stage + incomplete
            : $"{stage} {detail}{incomplete}";
    }

    internal static string DescribeCleanup(
        PackageAssemblyEvaluationCleanupEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var parts = new List<string>(evidence.CandidateFailures.Length + 1);
        if (evidence.ProjectionCleanup is { } projection)
            parts.Add($"projection cleanup {projection}");
        foreach (PackageAssemblyCandidateCleanupFailure failure
            in evidence.CandidateFailures)
        {
            parts.Add($"{failure.Stage} x{Count(failure.Count)}");
        }

        return parts.Count == 0
            ? "no recorded stage"
            : string.Join(", ", parts);
    }

    static string FormatMethod(int methodDefinitionToken) =>
        $"0x{methodDefinitionToken:X8}";

    static string FormatOffset(int ilOffset) =>
        $"IL_{ilOffset:X4}";

    static string Count(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    static string Count(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    static InertString Cell(string value) =>
        new(TextPolicy.Field, value);

    static readonly InertString EmptyCell = Cell("");
    static readonly InertString MatchedOutcome = Cell("matched");
    static readonly InertString NoMatchOutcome = Cell("no-match");
    static readonly InertString NotApplicableOutcome = Cell("not-applicable");
    static readonly InertString FailedOutcome = Cell("failed");
}
