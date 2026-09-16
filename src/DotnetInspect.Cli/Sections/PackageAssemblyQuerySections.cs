using System.Globalization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Views;
using ILInspector.Analysis;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Projects the shared assembly-semantic Find Document into the CLI's rendered
/// document.
/// </summary>
/// <remarks>
/// The projection is total over the owner's closed outcome set: a match, a
/// semantically confirmed miss, an inapplicable candidate, an evaluation
/// failure, and an acquisition failure each produce their own candidate row,
/// so none of them can be rendered as success-shaped empty output. Only
/// evaluated candidates carry an owner-issued Root reopening token, because an
/// acquisition failure never produced one. Population formation failures stay
/// separate from admitted-candidate outcomes.
/// </remarks>
public static class PackageAssemblyQuerySections
{
    public const string Matches = "Matches";
    public const string Candidates = "Candidates";
    public const string PopulationFailures = "Population Failures";

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
        PackageAssemblySemanticFindRequest request,
        PackageAssemblySemanticFindDocument document,
        IReadOnlyList<PackageAssemblySemanticFindResult>? selectedResults = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(document);
        selectedResults ??= document.Results;

        List<PackageAssemblyLiteralUseRow> matches =
            [.. selectedResults.Select(ResultRow)];
        var candidates = new List<PackageAssemblyCandidateRow>();
        foreach (PackageAssemblySemanticFindCandidateOutcome outcome
            in document.CandidateOutcomes)
        {
            candidates.Add(CandidateRow(outcome));
        }

        List<PackageAssemblyPopulationFailureRow> populationFailures =
        [
            .. document.Population.Failures.Select(PopulationFailureRow),
        ];

        return new PackageAssemblyQueryView(
            InertString.Format(
                TextPolicy.Prose,
                $"Find literal: {request.Pattern.Operand.DisplayText}"),
            Describe(
                request,
                document,
                matches.Count))
        {
            CandidateCount = document.CandidateCount,
            MatchedCandidateCount = document.MatchedCandidateCount,
            SemanticMissCount = document.SemanticMissCount,
            NotApplicableCount = document.NotApplicableCount,
            FailureCount = document.FailureCount,
            PopulationFailureCount = populationFailures.Count,
            IsComplete =
                document.Completion.IsRequestedPopulationComplete
                && document.Completion.IsSemanticEvaluationComplete,
            Matches = matches.Count == 0 ? null : matches,
            Candidates = candidates.Count == 0 ? null : candidates,
            PopulationFailures =
                populationFailures.Count == 0
                    ? null
                    : populationFailures,
        };
    }

    public static int CountMatchRows(PackageAssemblyQueryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Matches?.Count ?? 0;
    }

    static InertString Describe(
        PackageAssemblySemanticFindRequest request,
        PackageAssemblySemanticFindDocument document,
        int matchRows)
    {
        string lead = document.OccurrenceCount == 0
            ? "No matching decoded string literal uses were reported."
            : matchRows == document.OccurrenceCount
                ? $"{Count(matchRows)} decoded string literal uses; "
                    + $"{Count(document.MatchedCandidateCount)} of "
                    + $"{Count(document.CandidateCount)} package candidates matched."
                : $"Showing {Count(matchRows)} of "
                    + $"{Count(document.OccurrenceCount)} decoded string literal uses; "
                    + $"{Count(document.MatchedCandidateCount)} of "
                    + $"{Count(document.CandidateCount)} package candidates matched.";
        string population = document.Population.Completion switch
        {
            PackageAcquisitionPopulationCompletionKind.CandidateLimitReached =>
                "The requested bounded population is complete; the wider prefix was not exhausted.",
            PackageAcquisitionPopulationCompletionKind.PrefixExhausted =>
                "The package prefix was exhausted.",
            PackageAcquisitionPopulationCompletionKind.ExactCoordinates =>
                "The exact package population was formed.",
            _ =>
                $"Package population completion was {document.Population.Completion}.",
        };
        return InertString.Format(
            TextPolicy.Prose,
            $"{lead} {Scope} Target framework "
            + $"{request.Target.RequestedFramework}; misses "
            + $"{Count(document.SemanticMissCount)}, not applicable "
            + $"{Count(document.NotApplicableCount)}, candidate failures "
            + $"{Count(document.FailureCount)}, population failures "
            + $"{Count(document.Population.Failures.Length)}. {population}");
    }

    static PackageAssemblyLiteralUseRow ResultRow(
        PackageAssemblySemanticFindResult result) =>
        new(
            Cell(result.Coordinate.PackageId),
            Cell(result.Coordinate.Version),
            result.SelectedAsset.Asset.AssemblyName,
            Cell(FormatMethod(
                result.Evidence.Address.MethodDefinitionToken)),
            Cell(FormatOffset(result.Evidence.Address.ILOffset)),
            result.Evidence.LiteralText);

    static PackageAssemblyCandidateRow CandidateRow(
        PackageAssemblySemanticFindCandidateOutcome outcome)
    {
        return outcome switch
        {
            PackageAssemblySemanticFindCandidateOutcome.Matched matched =>
                EvaluationRow(matched.Evaluation),
            PackageAssemblySemanticFindCandidateOutcome.NoMatch noMatch =>
                EvaluationRow(noMatch.Evaluation),
            PackageAssemblySemanticFindCandidateOutcome.NotApplicable
                notApplicable =>
                EvaluationRow(notApplicable.Evaluation),
            PackageAssemblySemanticFindCandidateOutcome.Failure
                {
                    Reason:
                        PackageAssemblySemanticFindFailureReason.Evaluation
                        evaluation,
                } =>
                EvaluationRow(evaluation.Evidence),
            PackageAssemblySemanticFindCandidateOutcome.Failure failure =>
                AcquisitionFailureRow(failure),
            _ => throw new InvalidOperationException(
                "Unknown package assembly-semantic Find candidate outcome."),
        };
    }

    static PackageAssemblyCandidateRow EvaluationRow(
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
                return new PackageAssemblyCandidateRow(
                    package,
                    version,
                    MatchedOutcome,
                    asset,
                    targetFramework,
                    Cell(
                        $"{Count(match.Evidence.Occurrences.Length)} literal uses; "
                        + $"{Count(matched.UnevaluatedSiblings)} sibling assemblies not evaluated."),
                    root);
            case PackageAssemblyEvaluationOutcome.NoMatch noMatch:
                return new PackageAssemblyCandidateRow(
                    package,
                    version,
                    NoMatchOutcome,
                    asset,
                    targetFramework,
                    Cell(
                        "The selected implementation assembly has no matching decoded ldstr use "
                        + $"after {Count(noMatch.Receipt.MethodBodiesVisited)} method bodies."),
                    root);
            case PackageAssemblyEvaluationOutcome.NotApplicable notApplicable:
                return new PackageAssemblyCandidateRow(
                    package,
                    version,
                    NotApplicableOutcome,
                    asset,
                    targetFramework,
                    Cell(Describe(notApplicable.Reason)),
                    root);
            case PackageAssemblyEvaluationOutcome.Failure failure:
                return new PackageAssemblyCandidateRow(
                    package,
                    version,
                    FailedOutcome,
                    asset,
                    targetFramework,
                    Cell(Describe(failure)),
                    root);
            default:
                throw new InvalidOperationException(
                    "Unknown package assembly evaluation outcome.");
        }
    }

    static PackageAssemblyCandidateRow AcquisitionFailureRow(
        PackageAssemblySemanticFindCandidateOutcome.Failure failure)
    {
        var acquisition =
            (PackageAssemblySemanticFindFailureReason.Acquisition)
                failure.Reason;
        List<string> details =
        [
            .. acquisition.Evidence.Failures.Select(
                static item =>
                    $"{item.Authority} ({item.Kind}): {item.Message}"),
            .. acquisition.Evidence.NotFoundAuthorities.Select(
                static authority =>
                    $"{authority}: package payload was not found"),
        ];
        return new(
            Cell(failure.Coordinate.PackageId),
            Cell(failure.Coordinate.Version),
            FailedOutcome,
            EmptyCell,
            EmptyCell,
            Cell(
                details.Count == 0
                    ? "Package payload acquisition failed."
                    : string.Join("; ", details)),
            EmptyCell);
    }

    static PackageAssemblyPopulationFailureRow PopulationFailureRow(
        PackageAcquisitionPopulationFailure failure) =>
        new(
            failure.CandidateOrdinal?.ToString(
                CultureInfo.InvariantCulture) ?? "",
            failure.Coordinate?.PackageId ?? failure.PackageId ?? "",
            failure.Coordinate?.Version ?? "",
            failure.Failure.Authority.ToString(),
            failure.Failure.Kind.ToString(),
            failure.Failure.Message);

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
