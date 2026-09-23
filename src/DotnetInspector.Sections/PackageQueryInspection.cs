using System.Collections.Immutable;
using System.Globalization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Analysis;
using InertText;
using NuGetFetch;
using QuerySpace;

namespace DotnetInspector.Sections;

public sealed class PackageQueryAssemblySemanticExecution
{
    public PackageQueryAssemblySemanticExecution(
        IPackageSourceAuthorization sourceAuthorization,
        Func<CancellationToken, PackageSourceOperationLease>
            createSourceOperation,
        PackagePayloadAcquisitionPlan payloadAcquisition,
        PackageAssemblySemanticFindBudget budget,
        IPackageQueryLibraryLiteralAssessmentSink? assessmentSink = null)
    {
        SourceAuthorization = sourceAuthorization
            ?? throw new ArgumentNullException(nameof(sourceAuthorization));
        CreateSourceOperation = createSourceOperation
            ?? throw new ArgumentNullException(nameof(createSourceOperation));
        PayloadAcquisition = payloadAcquisition
            ?? throw new ArgumentNullException(nameof(payloadAcquisition));
        Budget = budget ?? throw new ArgumentNullException(nameof(budget));
        AssessmentSink = assessmentSink;
    }

    public IPackageSourceAuthorization SourceAuthorization { get; }

    public Func<CancellationToken, PackageSourceOperationLease>
        CreateSourceOperation { get; }

    public PackagePayloadAcquisitionPlan PayloadAcquisition { get; }

    public PackageAssemblySemanticFindBudget Budget { get; }

    public IPackageQueryLibraryLiteralAssessmentSink? AssessmentSink
        { get; }
}

/// <summary>
/// Materializes a completed Package Query operation into the shared
/// host-neutral inspection envelope.
/// </summary>
public static class PackageQueryInspection
{
    public static async ValueTask<
        InspectionEnvelope<PackageQueryDocument>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            source,
            plan,
            contentProvider: null,
            dependencyTraversalServices: null,
            assemblySemanticExecution: null,
            nonterminalSink: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<PackageQueryDocument>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            source,
            plan,
            contentProvider,
            dependencyTraversalServices: null,
            assemblySemanticExecution: null,
            nonterminalSink: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<PackageQueryDocument>>
        ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            IPackageQueryNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken = default)
            => await ExecuteAsync(
                source,
                plan,
                contentProvider,
                dependencyTraversalServices: null,
                assemblySemanticExecution: null,
                nonterminalSink,
                cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
            InspectionEnvelope<PackageQueryDocument>>
            ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            PackageQueryDependencyTraversalServices?
                dependencyTraversalServices,
            IPackageQueryNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            source,
            plan,
            contentProvider,
            dependencyTraversalServices,
            assemblySemanticExecution: null,
            nonterminalSink,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
            InspectionEnvelope<PackageQueryDocument>>
            ExecuteAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            PackageQueryDependencyTraversalServices?
                dependencyTraversalServices,
            PackageQueryAssemblySemanticExecution? assemblySemanticExecution,
            IPackageQueryNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.RequiresLibraryLiteralEvaluation
            && assemblySemanticExecution is null)
        {
            throw new InvalidOperationException(
                "A library-literal Package Query requires assembly-semantic execution services.");
        }

        PackageQueryPlan executionPlan =
            plan.RequiresLibraryLiteralEvaluation
                ? plan.CreatePrequalificationPlan()
                : plan;
        var results = ImmutableArray.CreateBuilder<PackageQueryMatch>();
        var failures = ImmutableArray.CreateBuilder<PackageQueryFailure>();
        PackageQuerySummary? summary = null;
        await foreach (PackageQueryEvent queryEvent in PackageQuery.ExecuteAsync(
            source,
            executionPlan,
            contentProvider,
            dependencyTraversalServices,
            cancellationToken).ConfigureAwait(false))
        {
            if (summary is not null)
            {
                throw new InvalidOperationException(
                    "Package Query produced an event after completion.");
            }

            switch (queryEvent)
            {
                case PackageQueryEvent.Match match:
                    results.Add(match.Value);
                    break;
                case PackageQueryEvent.Failure failure:
                    failures.Add(failure.Value);
                    break;
                case PackageQueryEvent.Completed completed:
                    summary = completed.Value;
                    continue;
            }

            if (queryEvent is PackageQueryEvent.Nonterminal nonterminal
                && nonterminalSink is not null
                && (!plan.RequiresLibraryLiteralEvaluation
                    || queryEvent is not PackageQueryEvent.Match))
            {
                await nonterminalSink.ReportAsync(
                    nonterminal,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (summary is null)
        {
            throw new InvalidOperationException(
                "Package Query ended without completion.");
        }

        PackageQueryDocument content = plan.RequiresLibraryLiteralEvaluation
            ? await CompleteLibraryLiteralAsync(
                source,
                plan,
                results.ToImmutable(),
                failures.ToImmutable(),
                summary,
                assemblySemanticExecution!,
                nonterminalSink,
                cancellationToken).ConfigureAwait(false)
            : new(
                results.ToImmutable(),
                failures.ToImmutable(),
                summary);
        ValidateContent(plan, content);

        return new(
            content,
            new InspectionShare.NonProjectable(
                "package-query/share",
                "Package Query plans do not yet have a canonical Workspace Share projection."));
    }

    private static async ValueTask<PackageQueryDocument>
        CompleteLibraryLiteralAsync(
            IPackageSourceClient source,
            PackageQueryPlan plan,
            ImmutableArray<PackageQueryMatch> prequalified,
            ImmutableArray<PackageQueryFailure> preliminaryFailures,
            PackageQuerySummary preliminarySummary,
            PackageQueryAssemblySemanticExecution execution,
            IPackageQueryNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken)
    {
        string literal = plan.LibraryLiteral
            ?? throw new InvalidOperationException(
                "A semantic Package Query plan requires a library literal.");
        string targetFramework = plan.LibraryTargetFramework
            ?? throw new InvalidOperationException(
                "A semantic Package Query plan requires a target framework.");
        if (prequalified.IsEmpty)
        {
            return new(
                [],
                preliminaryFailures,
                SemanticSummary(
                    plan,
                    preliminarySummary,
                    matches: 0,
                    failures: preliminaryFailures.Length,
                    evaluated: 0,
                    notEvaluated: 0,
                    semanticMatches: 0,
                    occurrences: 0,
                    semanticMisses: 0,
                    notApplicable: 0,
                    matchLimitReached: false));
        }

        PackageSourceOperationLease? sourceOperation =
            execution.CreateSourceOperation(cancellationToken);
        var semanticSink = new SemanticSink(
            plan,
            prequalified,
            source.Source,
            execution.AssessmentSink,
            nonterminalSink);
        PackageAssemblySemanticQueryDocument semantic;
        try
        {
            PackageAcquisitionPopulation population =
                await sourceOperation.ResolvePinnedPopulationAsync(
                    execution.SourceAuthorization,
                    [
                        .. prequalified.Select(match =>
                            PackageSourceCoordinate.Create(
                                match.Package.PackageId,
                                match.Package.Version)),
                    ]).ConfigureAwait(false);
            var request = new PackageAssemblySemanticFindRequest(
                population,
                PackageHouseTargetContext.Exact(targetFramework),
                PackageAssemblyPatterns.CreateRequest(
                    PackageAssemblyPatterns.StringLiteralContains,
                    literal),
                execution.Budget,
                maximumMatches: plan.MaximumMatches);
            PackageSourceOperationLease transferredOperation = sourceOperation;
            sourceOperation = null;
            InspectionEnvelope<PackageAssemblySemanticQueryDocument> envelope =
                await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                    request,
                    transferredOperation,
                    execution.PayloadAcquisition,
                    semanticSink,
                    cancellationToken).ConfigureAwait(false);
            semantic = envelope.Content;
        }
        finally
        {
            sourceOperation?.Dispose();
        }

        var failures = preliminaryFailures.ToBuilder();
        ImmutableArray<PackageQueryFailure> populationFailures =
        [
            .. semantic.Population.Failures.Select(failure =>
                new PackageQueryFailure(
                    failure.PackageId,
                    failure.Coordinate?.Version,
                    source.Source,
                    PackageQueryFailureKind.AssemblyAcquisition,
                    failure.Failure.Message)),
        ];
        foreach (PackageQueryFailure failure in populationFailures)
        {
            failures.Add(failure);
        }
        foreach (PackageAssemblySemanticQueryCandidateOutcome.Failure failure
            in semantic.CandidateOutcomes.OfType<
                PackageAssemblySemanticQueryCandidateOutcome.Failure>())
        {
            failures.Add(ProjectFailure(source.Source, failure));
        }
        foreach (PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated
            notEvaluated in semantic.CandidateOutcomes.OfType<
                PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated>())
        {
            failures.Add(ProjectNotEvaluated(source.Source, notEvaluated));
        }

        ImmutableArray<PackageQueryMatch> matches =
        [
            .. semantic.Results
                .Select(result => ProjectMatch(plan, prequalified, result)),
        ];
        var content = new PackageQueryDocument(
            matches,
            failures.ToImmutable(),
            SemanticSummary(
                plan,
                preliminarySummary,
                matches.Length,
                failures.Count,
                semantic.EvaluatedCandidateCount,
                semantic.NotEvaluatedCount,
                semantic.MatchedPackageCount,
                semantic.OccurrenceCount,
                semantic.SemanticMissCount,
                semantic.NotApplicableCount,
                semantic.Completion.IsMatchLimitReached))
        {
            LibraryLiteralAssessments = [.. semanticSink.Assessments],
        };

        foreach (PackageQueryFailure failure in populationFailures)
        {
            if (nonterminalSink is not null)
            {
                await nonterminalSink.ReportAsync(
                    new PackageQueryEvent.Failure(failure),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        return content;
    }

    private static PackageQuerySummary SemanticSummary(
        PackageQueryPlan plan,
        PackageQuerySummary preliminary,
        int matches,
        int failures,
        int evaluated,
        int notEvaluated,
        int semanticMatches,
        int occurrences,
        int semanticMisses,
        int notApplicable,
        bool matchLimitReached) =>
        preliminary with
        {
            MatchLimit = plan.MaximumMatches,
            Matches = matches,
            Failures = failures,
            Completion =
                plan.PackageInput is SourceSelector.Package
                    ? PackageQueryCompletionKind.ExactPackageComplete
                    : plan.MaximumMatches is not null
                        && matchLimitReached
                        ? PackageQueryCompletionKind.MatchLimitReached
                        : preliminary.Completion,
            EvaluatedCandidates = evaluated,
            NotEvaluatedCandidates = notEvaluated,
            SemanticMatches = semanticMatches,
            Occurrences = occurrences,
            SemanticMisses = semanticMisses,
            NotApplicable = notApplicable,
            Scope =
                "All selected implementation libraries for one compatible target framework.",
        };

    private static PackageQueryMatch ProjectMatch(
        PackageQueryPlan plan,
        ImmutableArray<PackageQueryMatch> prequalified,
        PackageAssemblySemanticQueryResult result)
    {
        PackageQueryMatch original = FindMatch(prequalified, result.Coordinate);
        ImmutableArray<PackageQueryLibraryLiteralOccurrence>
            libraryOccurrences =
        [
            .. result.LibraryOccurrences.Select(value =>
                new PackageQueryLibraryLiteralOccurrence(
                    ProjectSelectedAsset(value.SelectedAsset),
                    value.Evidence)),
        ];
        PackageQueryLibraryLiteralSelectedAsset selected =
            libraryOccurrences[0].SelectedAsset;
        PortableQueryTerm term = plan.Terms.Single(value =>
            value.Key == PackageQuery.LibraryLiteralTermKey);
        ImmutableArray<InertString> preview =
        [
            .. libraryOccurrences
                .Take(PackageQuery.MaximumEvidencePreviewItems)
                .Select(value => new InertString(
                    TextPolicy.Field,
                    $"{value.SelectedAsset.Path}: "
                    + $"Method 0x{value.Evidence.Address.MethodDefinitionToken:X8}, "
                    + $"IL_{value.Evidence.Address.ILOffset:X4}: "
                    + Excerpt(value.Evidence.LiteralText.ToString()))),
        ];
        var answer = new PackageQueryAnswer(
            PackageQuery.LibraryLiteralTermKey,
            InertString.Format(
                TextPolicy.Field,
                $"{result.Occurrences.Length.ToString(CultureInfo.InvariantCulture)} decoded literal uses across {result.MatchedLibraryCount.ToString(CultureInfo.InvariantCulture)} implementation libraries"))
        {
            Term = term,
        };
        var evidence = new PackageQueryEvidence("implementation-libraries")
        {
            Scope = PackageQueryEvidenceScope.Package,
            Summary = new(result.Occurrences.Length, preview),
            Properties =
            [
                Property(
                    "evaluated-library-count",
                    result.EvaluatedLibraryCount.ToString(
                        CultureInfo.InvariantCulture)),
                Property(
                    "matched-library-count",
                    result.MatchedLibraryCount.ToString(
                        CultureInfo.InvariantCulture)),
            ],
            Term = term,
        };
        return original with
        {
            Tier = PackageQueryAcquisitionTier.PackageContent,
            Answers = original.Answers.Add(answer),
            Evidence = original.Evidence.Add(evidence),
            LibraryLiteral = new(
                result.RootRequest,
                selected,
                result.Occurrences)
            {
                LibraryOccurrences = libraryOccurrences,
            },
        };
    }

    private static PackageQueryLibraryLiteralAssessment ProjectAssessment(
        PackageSourceResultIdentity source,
        PackageAssemblySemanticQueryCandidateOutcome outcome)
    {
        var assessment = new PackageQueryLibraryLiteralAssessment(
            outcome.CandidateOrdinal,
            outcome.Coordinate.PackageId,
            outcome.Coordinate.Version,
            source,
            outcome switch
            {
                PackageAssemblySemanticQueryCandidateOutcome.Matched =>
                    PackageQueryLibraryLiteralAssessmentKind.Matched,
                PackageAssemblySemanticQueryCandidateOutcome.NoMatch =>
                    PackageQueryLibraryLiteralAssessmentKind.NoMatch,
                PackageAssemblySemanticQueryCandidateOutcome.NotApplicable =>
                    PackageQueryLibraryLiteralAssessmentKind.NotApplicable,
                PackageAssemblySemanticQueryCandidateOutcome.Failure =>
                    PackageQueryLibraryLiteralAssessmentKind.Failure,
                PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated =>
                    PackageQueryLibraryLiteralAssessmentKind.NotEvaluated,
                _ => throw new InvalidOperationException(
                    "Unknown assembly-semantic candidate outcome."),
            })
        {
            Libraries = ProjectLibraryAssessments(
                outcome.LibraryEvaluations),
        };
        return outcome switch
        {
            PackageAssemblySemanticQueryCandidateOutcome.Matched matched =>
                assessment with
                {
                    RootRequest = matched.Result.RootRequest,
                    SelectedAsset =
                        ProjectSelectedAsset(matched.Result.SelectedAsset),
                },
            PackageAssemblySemanticQueryCandidateOutcome.NoMatch noMatch =>
                assessment with
                {
                    RootRequest = noMatch.Evaluation.Subject.RootRequest,
                    SelectedAsset =
                        ProjectSelectedAsset(
                            noMatch.Evaluation.SelectedAsset!),
                    Message =
                        "The selected implementation libraries have no matching decoded ldstr use.",
                },
            PackageAssemblySemanticQueryCandidateOutcome.NotApplicable
                notApplicable =>
                assessment with
                {
                    RootRequest =
                        notApplicable.Evaluation.Subject.RootRequest,
                    SelectedAsset =
                        notApplicable.Evaluation.SelectedAsset is { } selected
                            ? ProjectSelectedAsset(selected)
                            : null,
                    NotApplicableReason =
                        Project(notApplicable.Evaluation.Reason),
                    Message = Describe(notApplicable.Evaluation.Reason),
                },
            PackageAssemblySemanticQueryCandidateOutcome.Failure failure =>
                ProjectFailureAssessment(assessment, failure),
            PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated
                notEvaluated =>
                ProjectNotEvaluatedAssessment(assessment, notEvaluated),
            _ => throw new InvalidOperationException(
                "Unknown assembly-semantic candidate outcome."),
        };
    }

    private static PackageQueryLibraryLiteralAssessment
        ProjectFailureAssessment(
            PackageQueryLibraryLiteralAssessment assessment,
            PackageAssemblySemanticQueryCandidateOutcome.Failure failure) =>
        failure.Reason switch
        {
            PackageAssemblySemanticQueryFailureReason.Acquisition acquisition =>
                assessment with
                {
                    FailureKind =
                        PackageQueryLibraryLiteralFailureKind.Acquisition,
                    Message = Describe(acquisition.Evidence),
                },
            PackageAssemblySemanticQueryFailureReason.Evaluation evaluation =>
                assessment with
                {
                    RootRequest = evaluation.Evidence.Subject.RootRequest,
                    SelectedAsset =
                        evaluation.Evidence.SelectedAsset is { } selected
                            ? ProjectSelectedAsset(selected)
                            : null,
                    FailureKind =
                        PackageQueryLibraryLiteralFailureKind.Evaluation,
                    FailureStage =
                        evaluation.Evidence.Reason.Stage.ToString(),
                    Message = Describe(
                        failure.LibraryEvaluations
                            .OfType<
                                PackageAssemblyEvaluationOutcome.Failure>()),
                },
            _ => throw new InvalidOperationException(
                "Unknown assembly-semantic failure reason."),
        };

    private static PackageQueryLibraryLiteralAssessment
        ProjectNotEvaluatedAssessment(
            PackageQueryLibraryLiteralAssessment assessment,
            PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated
                outcome) =>
        outcome.Reason switch
        {
            PackageAssemblySemanticQueryNonEvaluationReason.OperationDeadline =>
                assessment with
                {
                    NonEvaluationKind =
                        PackageQueryLibraryLiteralNonEvaluationKind
                            .OperationDeadline,
                    Message =
                        "The operation deadline expired before semantic evaluation.",
                },
            _ => throw new InvalidOperationException(
                "Unknown assembly-semantic non-evaluation reason."),
        };

    private static PackageQueryFailure ProjectFailure(
        PackageSourceResultIdentity source,
        PackageAssemblySemanticQueryCandidateOutcome.Failure failure) =>
        failure.Reason switch
        {
            PackageAssemblySemanticQueryFailureReason.Acquisition acquisition =>
                new(
                    failure.Coordinate.PackageId,
                    failure.Coordinate.Version,
                    source,
                    PackageQueryFailureKind.AssemblyAcquisition,
                    Describe(acquisition.Evidence)),
            PackageAssemblySemanticQueryFailureReason.Evaluation evaluation =>
                new(
                    failure.Coordinate.PackageId,
                    failure.Coordinate.Version,
                    source,
                    PackageQueryFailureKind.AssemblyEvaluation,
                    Describe(
                        failure.LibraryEvaluations
                            .OfType<
                                PackageAssemblyEvaluationOutcome.Failure>())),
            _ => throw new InvalidOperationException(
                "Unknown assembly-semantic failure reason."),
        };

    private static PackageQueryFailure ProjectNotEvaluated(
        PackageSourceResultIdentity source,
        PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated outcome) =>
        new(
            outcome.Coordinate.PackageId,
            outcome.Coordinate.Version,
            source,
            PackageQueryFailureKind.AssemblyNotEvaluated,
            "The operation deadline expired before semantic evaluation.");

    private static PackageQueryLibraryLiteralSelectedAsset
        ProjectSelectedAsset(PackageAssemblySelectedAssetContext selected) =>
        new(
            selected.Asset.Path,
            selected.Asset.AssemblyName,
            selected.Asset.TargetFramework,
            selected.UnevaluatedSiblings,
            selected.Occurrence.Ordinal);

    private static ImmutableArray<
        PackageQueryLibraryLiteralLibraryAssessment>
        ProjectLibraryAssessments(
            ImmutableArray<PackageAssemblyEvaluationOutcome> evaluations) =>
    [
        .. evaluations
            .Where(evaluation =>
                (evaluation
                    is PackageAssemblyEvaluationOutcome.Matched
                    or PackageAssemblyEvaluationOutcome.NoMatch
                    or PackageAssemblyEvaluationOutcome.Failure)
                && evaluation.SelectedAsset is not null)
            .Select(evaluation =>
            {
                PackageAssemblySelectedAssetContext selected =
                    evaluation.SelectedAsset!;
                return evaluation switch
                {
                    PackageAssemblyEvaluationOutcome.Matched matched =>
                        new PackageQueryLibraryLiteralLibraryAssessment(
                            ProjectSelectedAsset(selected),
                            PackageQueryLibraryLiteralLibraryAssessmentKind
                                .Matched,
                            matched.Evidence.Occurrences.Length),
                    PackageAssemblyEvaluationOutcome.NoMatch =>
                        new PackageQueryLibraryLiteralLibraryAssessment(
                            ProjectSelectedAsset(selected),
                            PackageQueryLibraryLiteralLibraryAssessmentKind
                                .NoMatch,
                            0),
                    PackageAssemblyEvaluationOutcome.Failure failure =>
                        new PackageQueryLibraryLiteralLibraryAssessment(
                            ProjectSelectedAsset(selected),
                            PackageQueryLibraryLiteralLibraryAssessmentKind
                                .Failure,
                            0)
                        {
                            FailureStage = failure.Reason.Stage.ToString(),
                            Message = Describe(failure),
                        },
                    _ => throw new InvalidOperationException(
                        "Unknown selected-library evaluation outcome."),
                };
            }),
    ];

    private static PackageQueryMatch FindMatch(
        ImmutableArray<PackageQueryMatch> matches,
        PackageSourceCoordinate coordinate) =>
        matches.Single(match =>
            match.Package.PackageId.Equals(
                coordinate.PackageId,
                StringComparison.OrdinalIgnoreCase)
            && match.Package.Version.Equals(
                coordinate.Version,
                StringComparison.OrdinalIgnoreCase));

    private static PackageQueryLibraryLiteralNotApplicableReason Project(
        PackageAssemblyNotApplicableReason reason) =>
        reason switch
        {
            PackageAssemblyNotApplicableReason.NoCompileAssets =>
                PackageQueryLibraryLiteralNotApplicableReason.NoCompileAssets,
            PackageAssemblyNotApplicableReason.NoMatchingTargetFramework =>
                PackageQueryLibraryLiteralNotApplicableReason
                    .NoMatchingTargetFramework,
            PackageAssemblyNotApplicableReason.EmptyCompileGroup =>
                PackageQueryLibraryLiteralNotApplicableReason.EmptyCompileGroup,
            PackageAssemblyNotApplicableReason.NoImplementationCounterpart =>
                PackageQueryLibraryLiteralNotApplicableReason
                    .NoImplementationCounterpart,
            _ => throw new InvalidOperationException(
                "Unknown package assembly applicability reason."),
        };

    private static string Describe(
        PackageAssemblyNotApplicableReason reason) =>
        reason switch
        {
            PackageAssemblyNotApplicableReason.NoCompileAssets =>
                "No compile library is available.",
            PackageAssemblyNotApplicableReason.NoMatchingTargetFramework =>
                "No compile group matches the requested framework.",
            PackageAssemblyNotApplicableReason.EmptyCompileGroup =>
                "The selected compile group is explicitly empty.",
            PackageAssemblyNotApplicableReason.NoImplementationCounterpart =>
                "The primary compile library has no implementation counterpart.",
            _ => throw new InvalidOperationException(
                "Unknown package assembly applicability reason."),
        };

    private static string Describe(
        PackageAssemblySemanticQueryAcquisitionFailure failure) =>
        failure.Failures
            .Select(item => item.Message)
            .Concat(failure.NotFoundAuthorities.Select(authority =>
                $"Package content was not found from {authority}."))
            .DefaultIfEmpty("Package content acquisition failed.")
            .Aggregate((left, right) => $"{left} {right}");

    private static string Describe(
        IEnumerable<PackageAssemblyEvaluationOutcome.Failure> failures)
    {
        string[] descriptions =
        [
            .. failures.Select(failure =>
            {
                string path =
                    failure.SelectedAsset?.Asset.Path.ToString()
                    ?? "package selection";
                return $"{path}: {Describe(failure)}";
            }),
        ];
        return descriptions.Length == 0
            ? "Package assembly evaluation failed."
            : string.Join(" ", descriptions);
    }

    private static string Describe(
        PackageAssemblyEvaluationOutcome.Failure failure)
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
                    publication.Failures.Select(value =>
                        $"{value.Kind}/{value.DiagnosticCode}")),
            PackageAssemblyFailureReason.NotAssembly notAssembly =>
                $"The selected entry is not an assembly ({notAssembly.AdmissionStage}: {notAssembly.Kind}).",
            PackageAssemblyFailureReason.ProjectionRejected projection =>
                $"Assembly projection was refused: {projection.Failure}.",
            PackageAssemblyFailureReason.QueryRejected query =>
                $"The assembly query was refused: {query.Failure}.",
            PackageAssemblyFailureReason.SemanticRejection rejection =>
                $"The literal producer rejected the assembly: "
                + $"{rejection.Rejection.Kind} at method "
                + $"0x{rejection.Rejection.Site.MethodDefinitionToken:X8}.",
            PackageAssemblyFailureReason.SemanticWorkLimit limit =>
                $"The literal producer reached its {limit.Limit} work limit.",
            PackageAssemblyFailureReason.CandidateCleanup cleanup =>
                $"Candidate cleanup was incomplete: "
                + $"{DescribeCleanup(cleanup.Evidence)}.",
            _ => "",
        };
        string stage =
            $"Assembly evaluation failed at {failure.Reason.Stage}.";
        string incomplete = failure.Cleanup is { } evidence
            ? $" Candidate cleanup was incomplete: "
                + $"{DescribeCleanup(evidence)}."
            : "";
        return detail.Length == 0
            ? stage + incomplete
            : $"{stage} {detail}{incomplete}";
    }

    private static string DescribeCleanup(
        PackageAssemblyEvaluationCleanupEvidence evidence)
    {
        var parts = new List<string>(
            evidence.CandidateFailures.Length + 1);
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

    private static PackageQueryEvidenceProperty Property(
        string name,
        string value) =>
        new(name, new InertString(TextPolicy.Field, value));

    private static string Excerpt(string value) =>
        value.Length <= PackageQuery.MaximumEvidencePreviewCharacters
            ? value
            : value[..PackageQuery.MaximumEvidencePreviewCharacters] + "...";

    private static string Count(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static void ValidateContent(
        PackageQueryPlan plan,
        PackageQueryDocument content)
    {
        if (content.Results.IsDefault
            || content.Failures.IsDefault
            || content.LibraryLiteralAssessments.IsDefault)
        {
            throw new ArgumentException(
                "Package Query content must be initialized.",
                nameof(content));
        }
        PackageQuerySummary summary = content.Summary;
        if (summary.Matches != content.Results.Length
            || summary.Failures != content.Failures.Length)
        {
            throw new ArgumentException(
                "Package Query content does not match its terminal accounting.",
                nameof(content));
        }

        if (!summary.Prefix.ToString().Equals(
                plan.Prefix.ToString(),
                StringComparison.Ordinal)
            || summary.CandidateLimit != plan.MaximumCandidates
            || summary.MatchLimit != plan.MaximumMatches)
        {
            throw new ArgumentException(
                "Package Query content belongs to another query plan.",
                nameof(content));
        }
    }

    private sealed class SemanticSink(
        PackageQueryPlan plan,
        ImmutableArray<PackageQueryMatch> prequalified,
        PackageSourceResultIdentity source,
        IPackageQueryLibraryLiteralAssessmentSink? assessmentSink,
        IPackageQueryNonterminalSink? querySink)
        : IPackageAssemblySemanticQueryNonterminalSink
    {
        private int _publishedMatches;

        internal List<PackageQueryLibraryLiteralAssessment> Assessments
            { get; } = [];

        public async ValueTask ReportAsync(
            PackageAssemblySemanticQueryCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            PackageQueryLibraryLiteralAssessment assessment =
                ProjectAssessment(source, outcome);
            if (assessmentSink is not null)
            {
                await assessmentSink.ReportAsync(
                    assessment,
                    cancellationToken).ConfigureAwait(false);
            }
            Assessments.Add(assessment);
            if (querySink is null)
                return;

            await querySink.ReportAsync(
                new PackageQueryEvent.Progress(new(
                    PackageQueryProgressPhase.Assembly,
                    outcome.CandidateOrdinal,
                    prequalified.Length)),
                cancellationToken).ConfigureAwait(false);
            switch (outcome)
            {
                case PackageAssemblySemanticQueryCandidateOutcome.Matched matched
                    when plan.MaximumMatches is not int maximumMatches
                        || _publishedMatches < maximumMatches:
                    _publishedMatches++;
                    await querySink.ReportAsync(
                        new PackageQueryEvent.Match(
                            ProjectMatch(
                                plan,
                                prequalified,
                                matched.Result)),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case PackageAssemblySemanticQueryCandidateOutcome.Failure failure:
                    await querySink.ReportAsync(
                        new PackageQueryEvent.Failure(
                            ProjectFailure(source, failure)),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated
                    notEvaluated:
                    await querySink.ReportAsync(
                        new PackageQueryEvent.Failure(
                            ProjectNotEvaluated(source, notEvaluated)),
                        cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }
}

/// <summary>
/// Receives Package Query events established before terminal settlement.
/// Completion remains authoritative in the returned Document.
/// </summary>
public interface IPackageQueryNonterminalSink
{
    ValueTask ReportAsync(
        PackageQueryEvent.Nonterminal queryEvent,
        CancellationToken cancellationToken);
}

/// <summary>
/// Receives Library literal assessments established before terminal settlement.
/// Completion remains authoritative in the returned Document.
/// </summary>
public interface IPackageQueryLibraryLiteralAssessmentSink
{
    ValueTask ReportAsync(
        PackageQueryLibraryLiteralAssessment assessment,
        CancellationToken cancellationToken);
}
