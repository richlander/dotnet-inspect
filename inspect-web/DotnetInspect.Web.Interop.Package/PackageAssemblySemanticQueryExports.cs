using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
internal static partial class BrowserPackageQueryOperations
{
    internal static void ValidateAssemblySemanticLiteral(string literal) =>
        ArgumentException.ThrowIfNullOrEmpty(literal);

    internal static async Task<BrowserPackageQueryInspection>
        ExecuteAssemblySemanticAsync(
        string packageInput,
        string literal,
        string targetFramework,
        int maximumCandidates,
        bool includePrerelease,
        Action<BrowserPackageQueryEvent> emit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageInput);
        ValidateAssemblySemanticLiteral(literal);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentNullException.ThrowIfNull(emit);

        (PackageQueryPlan plan, int requestedCandidates) =
            PlanAssemblySemantic(
                packageInput,
                maximumCandidates,
                includePrerelease);

        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        ConfiguredPackageAuthority galleryAuthority =
            authorization.Authorities[0];
        using IPackageSourceClient gallery =
            PackageSourceClientFactory.CreateGallery(
                galleryAuthority.Association,
                new NuGetFetchOptions
                {
                    RequestTimeout =
                        BrowserPackageWorkspace.GalleryOperationTimeout,
                    OperationTimeout =
                        BrowserPackageWorkspace.GalleryOperationTimeout,
                });
        await using PackageSourceSettlementLease settlement =
            PackageSourceSettlementService.IssueLease(
                authority =>
                    ReferenceEquals(
                        authority.Association,
                        galleryAuthority.Association)
                        ? gallery
                        : throw new InvalidOperationException(
                            "Browser library-literal Package Query requested an unauthorized package source."));
        PackageAssemblySemanticFindBudget budget =
            BrowserAssemblySemanticBudget();
        PackageSourceOperationLease? operation =
            settlement.IssueOperationLease(
                cancellationToken,
                BrowserPackageWorkspace.GalleryOperationTimeout,
                BrowserPackageWorkspace.GalleryOperationTimeout);

        try
        {
            PackageAcquisitionPopulation population =
                plan.PackageInput switch
                {
                    SourceSelector.Package exact =>
                        await PackageAcquisitionPopulationResolver
                            .ResolveGalleryExactAsync(
                                operation,
                                exact.Coordinate.PackageId,
                                authorization,
                                includePrerelease).ConfigureAwait(false),
                    SourceSelector.PackagePrefix prefix =>
                        await PackageAcquisitionPopulationResolver
                            .ResolveGalleryPrefixAsync(
                                operation,
                                new PackagePrefixDeclaration(
                                    prefix.Request.Prefix),
                                requestedCandidates,
                                authorization,
                                includePrerelease).ConfigureAwait(false),
                    _ => throw new InvalidOperationException(
                        "Unknown library-literal Package Query population plan."),
                };
            var request = new PackageAssemblySemanticFindRequest(
                population,
                PackageHouseTargetContext.Exact(targetFramework),
                PackageAssemblyPatterns.CreateRequest(
                    PackageAssemblyPatterns.StringLiteralContains,
                    literal),
                budget);
            PackageSourceOperationLease transferredOperation = operation;
            operation = null;
            InspectionEnvelope<PackageAssemblySemanticQueryDocument> envelope =
                await PackageAssemblySemanticQueryInspection.ExecuteAsync(
                    request,
                    transferredOperation,
                    new PackagePayloadAcquisitionPlan(
                        static (_, _) =>
                            BrowserPackageWorkspace.SessionPackageStore,
                        transferPolicy:
                            BrowserPackageWorkspace.PackageTransferPolicy),
                    new AssemblySemanticProgressSink(
                        population.Candidates.Length,
                        emit),
                    cancellationToken).ConfigureAwait(false);
            return Complete(envelope, packageInput);
        }
        finally
        {
            operation?.Dispose();
        }
    }

    internal static (PackageQueryPlan Plan, int RequestedCandidates)
        PlanAssemblySemantic(
        string packageInput,
        int maximumCandidates,
        bool includePrerelease)
    {
        PackageQueryPlanResult planResult = PackageQuery.PlanInput(
            packageInput,
            maximumCandidates: maximumCandidates,
            maximumMatches: null,
            includePrerelease: includePrerelease);
        if (planResult is PackageQueryPlanResult.Rejected rejected)
            throw new ArgumentException(rejected.Failure.Message, nameof(packageInput));

        PackageQueryPlan plan =
            ((PackageQueryPlanResult.Accepted)planResult).Plan;
        int requestedCandidates = plan.PackageInput switch
        {
            SourceSelector.Package when maximumCandidates == 1 => 1,
            SourceSelector.Package =>
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCandidates),
                    "An exact package assembly-semantic query admits one candidate."),
            SourceSelector.PackagePrefix
                when maximumCandidates is >= 1
                    and <= PackageAcquisitionPopulation.MaximumCandidates =>
                maximumCandidates,
            SourceSelector.PackagePrefix =>
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCandidates),
                    $"A package-prefix assembly-semantic query admits between 1 and {PackageAcquisitionPopulation.MaximumCandidates} candidates."),
            _ => throw new InvalidOperationException(
                "Unknown Package Query input selection."),
        };
        return (plan, requestedCandidates);
    }

    static PackageAssemblySemanticFindBudget BrowserAssemblySemanticBudget()
    {
        PackageAssemblySemanticFindBudget defaults =
            PackageAssemblySemanticFindBudget.Default;
        PackageAssemblyEvaluationBudget evaluation = defaults.Evaluation;
        return new(
            defaults.Payload,
            new(
                evaluation.MaximumEntryBytes,
                evaluation.MaximumRetainedImageBytes,
                evaluation.SemanticBudget,
                BrowserPackageWorkspace.GalleryOperationTimeout));
    }

    internal static BrowserPackageQueryInspection Complete(
        InspectionEnvelope<PackageAssemblySemanticQueryDocument> envelope,
        string packageInput)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageInput);

        BrowserPackageAssemblySemanticDocument semantic =
            Project(envelope.Content);
        BrowserPackageQueryFailure[] failures =
        [
            .. semantic.Population.Failures.Select(failure =>
                new BrowserPackageQueryFailure(
                    failure.PackageId,
                    failure.Version,
                    failure.Authority,
                    failure.CandidateOrdinal is null
                        ? BrowserPackageQueryFailureKind.Search
                        : BrowserPackageQueryFailureKind.AssemblyAcquisition,
                    failure.Message)),
            .. semantic.CandidateOutcomes
                .Where(outcome =>
                    outcome.Kind
                        == BrowserPackageAssemblySemanticCandidateOutcomeKind
                            .Failure)
                .Select(outcome =>
                    new BrowserPackageQueryFailure(
                        outcome.PackageId,
                        outcome.Version,
                        outcome.Producer,
                        outcome.FailureKind
                            == BrowserPackageAssemblySemanticFailureKind
                                .Acquisition
                            ? BrowserPackageQueryFailureKind.AssemblyAcquisition
                            : BrowserPackageQueryFailureKind.AssemblyEvaluation,
                        outcome.Message
                            ?? "Assembly-semantic evaluation failed.")),
        ];
        BrowserPackageQueryCompletion completion = new(
            packageInput,
            BrowserPackageWorkspace.Gallery.Source.Producer.Display.ToString(),
            semantic.Population.RequestedCandidates,
            semantic.Population.RequestedCandidates,
            semantic.CandidateCount,
            semantic.MatchedPackageCount,
            failures.Length,
            semantic.Population.Completion switch
            {
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .ExactPackageComplete =>
                    BrowserPackageQueryCompletionKind.ExactPackageComplete,
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .PrefixExhausted =>
                    BrowserPackageQueryCompletionKind.Exhausted,
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .CandidateLimitReached =>
                    BrowserPackageQueryCompletionKind.CandidateLimitReached,
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .SourcePageLimitReached =>
                    BrowserPackageQueryCompletionKind.SourcePageLimitReached,
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .ClientPageLimitReached =>
                    BrowserPackageQueryCompletionKind.ClientPageLimitReached,
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .SourceFailed =>
                    BrowserPackageQueryCompletionKind.Failed,
                _ => throw new InvalidOperationException(
                    "Unknown assembly-semantic population completion."),
            },
            SourceCandidates: semantic.Population.Candidates,
            SemanticMisses: semantic.SemanticMissCount,
            NotApplicable: semantic.NotApplicableCount,
            Scope:
                "Selected primary implementation libraries only; not every package assembly.")
        {
            Occurrences = semantic.OccurrenceCount,
            NotEvaluated = semantic.NotEvaluatedCount,
        };
        BrowserPackageQueryRow[] results =
        [
            .. semantic.Results.Select(ProjectResultRow),
        ];
        BrowserPackageQueryDocument content = new(
            results,
            results.Length > 0,
            failures,
            completion)
        {
            AssemblySemantic = semantic,
        };
        return new(
            BrowserInspectionWireProjection.Project(envelope.ContentKind),
            content,
            Project(envelope.PortableProjection),
            [
                .. envelope.Diagnostics.Select(diagnostic =>
                    new BrowserInspectionDiagnostic(
                        diagnostic.Code,
                        diagnostic.Severity.ToString(),
                        diagnostic.Summary.ToString(),
                        diagnostic.Correspondence?.ToString())),
            ]);
    }

    internal static BrowserPackageAssemblySemanticDocument Project(
        PackageAssemblySemanticQueryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            new(
                document.Population.RequestedCandidates,
                document.Population.Candidates.Length,
                Project(document.Population.Completion),
                document.Population.IsRequestedPopulationComplete,
                [
                    .. document.Population.Failures.Select(Project),
                ]),
            [
                .. document.Results.Select(Project),
            ],
            [
                .. document.CandidateOutcomes.Select(Project),
            ],
            document.CandidateCount,
            document.EvaluatedCandidateCount,
            document.NotEvaluatedCount,
            document.MatchedPackageCount,
            document.OccurrenceCount,
            document.SemanticMissCount,
            document.NotApplicableCount,
            document.FailureCount,
            new(
                Project(document.Completion.Population),
                document.Completion.IsRequestedPopulationComplete,
                document.Completion.AllCandidatesHaveTerminalOutcomes,
                document.Completion.HasFailures,
                document.Completion.IsSemanticEvaluationComplete,
                document.Completion.IsOperationDeadlineExpired));
    }

    static BrowserPackageAssemblySemanticResult Project(
        PackageAssemblySemanticQueryResult result) =>
        new(
            result.CandidateOrdinal,
            result.Coordinate.PackageId,
            result.Coordinate.Version,
            GalleryProducer,
            Project(result.SelectedAsset),
            [
                .. result.Occurrences.Select(occurrence =>
                    new BrowserPackageAssemblySemanticOccurrence(
                        occurrence.Address.ModuleVersionId.ToString("D"),
                        occurrence.Address.MethodDefinitionToken,
                        occurrence.Address.ILOffset,
                        occurrence.UserStringToken,
                        occurrence.LiteralCharacterCount,
                        occurrence.LiteralText.ToString())),
            ]);

    static BrowserPackageAssemblySemanticSelectedAsset Project(
        PackageAssemblySelectedAssetContext selected) =>
        new(
            selected.Asset.Path.ToString(),
            selected.Asset.AssemblyName.ToString(),
            selected.Asset.TargetFramework.ToString(),
            selected.Occurrence.Sequence.ToString(),
            selected.Occurrence.Ordinal,
            selected.UnevaluatedSiblings,
            selected.Subject.RootRequest.Encode());

    static BrowserPackageAssemblySemanticCandidateOutcome Project(
        PackageAssemblySemanticQueryCandidateOutcome outcome)
    {
        string packageId = outcome.Coordinate.PackageId;
        string version = outcome.Coordinate.Version;
        string producer = GalleryProducer;
        return outcome switch
        {
            PackageAssemblySemanticQueryCandidateOutcome.Matched matched =>
                new(
                    BrowserPackageAssemblySemanticCandidateOutcomeKind.Matched,
                    outcome.CandidateOrdinal,
                    packageId,
                    version,
                    producer,
                    Project(matched.Result),
                    Project(matched.Result.SelectedAsset),
                    matched.Result.RootRequest.Encode(),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
            PackageAssemblySemanticQueryCandidateOutcome.NoMatch noMatch =>
                new(
                    BrowserPackageAssemblySemanticCandidateOutcomeKind.NoMatch,
                    outcome.CandidateOrdinal,
                    packageId,
                    version,
                    producer,
                    null,
                    Project(noMatch.Evaluation.SelectedAsset!),
                    noMatch.Evaluation.Subject.RootRequest.Encode(),
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "The selected implementation library has no matching decoded ldstr use."),
            PackageAssemblySemanticQueryCandidateOutcome.NotApplicable
                notApplicable =>
                new(
                    BrowserPackageAssemblySemanticCandidateOutcomeKind
                        .NotApplicable,
                    outcome.CandidateOrdinal,
                    packageId,
                    version,
                    producer,
                    null,
                    notApplicable.Evaluation.SelectedAsset is { } selected
                        ? Project(selected)
                        : null,
                    notApplicable.Evaluation.Subject.RootRequest.Encode(),
                    Project(notApplicable.Evaluation.Reason),
                    null,
                    null,
                    null,
                    null,
                    null,
                    Describe(notApplicable.Evaluation.Reason)),
            PackageAssemblySemanticQueryCandidateOutcome.Failure failure =>
                ProjectFailure(failure),
            PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated
                notEvaluated =>
                ProjectNotEvaluated(notEvaluated),
            _ => throw new InvalidOperationException(
                "Unknown package assembly-semantic candidate outcome."),
        };
    }

    static BrowserPackageAssemblySemanticCandidateOutcome ProjectFailure(
        PackageAssemblySemanticQueryCandidateOutcome.Failure failure)
    {
        BrowserPackageAssemblySemanticFailureKind kind;
        string? stage;
        string message;
        string? rootRequest;
        BrowserPackageAssemblySemanticSelectedAsset? selectedAsset;
        switch (failure.Reason)
        {
            case PackageAssemblySemanticQueryFailureReason.Acquisition
                acquisition:
                kind = BrowserPackageAssemblySemanticFailureKind.Acquisition;
                stage = null;
                rootRequest = null;
                selectedAsset = null;
                message = acquisition.Evidence.Failures
                    .Select(item => item.Message)
                    .Concat(acquisition.Evidence.NotFoundAuthorities.Select(
                        authority =>
                            $"Package content was not found from {authority}."))
                    .DefaultIfEmpty(
                        "Package content acquisition failed.")
                    .Aggregate((left, right) => $"{left} {right}");
                break;
            case PackageAssemblySemanticQueryFailureReason.Evaluation
                evaluation:
                kind = BrowserPackageAssemblySemanticFailureKind.Evaluation;
                stage = evaluation.Evidence.Reason.Stage.ToString();
                rootRequest =
                    evaluation.Evidence.Subject.RootRequest.Encode();
                selectedAsset =
                    evaluation.Evidence.SelectedAsset is { } selected
                        ? Project(selected)
                        : null;
                message =
                    $"Assembly evaluation failed: {stage}."
                    + (evaluation.Evidence.Cleanup is null
                        ? ""
                        : " Candidate cleanup was incomplete.");
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown package assembly-semantic failure reason.");
        }

        return new(
            BrowserPackageAssemblySemanticCandidateOutcomeKind.Failure,
            failure.CandidateOrdinal,
            failure.Coordinate.PackageId,
            failure.Coordinate.Version,
            GalleryProducer,
            null,
            selectedAsset,
            rootRequest,
            null,
            kind,
            stage,
            null,
            null,
            null,
            message);
    }

    static BrowserPackageAssemblySemanticCandidateOutcome ProjectNotEvaluated(
        PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated outcome) =>
        outcome.Reason switch
        {
            PackageAssemblySemanticQueryNonEvaluationReason.OperationDeadline
                deadline =>
                new(
                    BrowserPackageAssemblySemanticCandidateOutcomeKind
                        .NotEvaluated,
                    outcome.CandidateOrdinal,
                    outcome.Coordinate.PackageId,
                    outcome.Coordinate.Version,
                    GalleryProducer,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    BrowserPackageAssemblySemanticNonEvaluationKind
                        .OperationDeadline,
                    deadline.Timeout.Kind.ToString(),
                    deadline.Timeout.Duration.TotalSeconds,
                    "The operation deadline expired before semantic evaluation."),
            _ => throw new InvalidOperationException(
                "Unknown package assembly-semantic non-evaluation reason."),
        };

    static BrowserPackageAssemblySemanticPopulationFailure Project(
        PackageAcquisitionPopulationFailure failure) =>
        new(
            failure.CandidateOrdinal,
            failure.PackageId,
            failure.Coordinate?.Version,
            failure.Failure.Authority.ToString(),
            failure.Failure.Kind.ToString(),
            failure.Failure.Message,
            failure.Failure.Timeout?.Kind.ToString(),
            failure.Failure.Timeout?.Duration.TotalSeconds);

    static BrowserPackageAssemblySemanticPopulationCompletionKind Project(
        PackageAcquisitionPopulationCompletionKind completion) =>
        completion switch
        {
            PackageAcquisitionPopulationCompletionKind.ExactPackageComplete =>
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .ExactPackageComplete,
            PackageAcquisitionPopulationCompletionKind.PrefixExhausted =>
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .PrefixExhausted,
            PackageAcquisitionPopulationCompletionKind.CandidateLimitReached =>
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .CandidateLimitReached,
            PackageAcquisitionPopulationCompletionKind.SourcePageLimitReached =>
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .SourcePageLimitReached,
            PackageAcquisitionPopulationCompletionKind.ClientPageLimitReached =>
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .ClientPageLimitReached,
            PackageAcquisitionPopulationCompletionKind.SourceFailed =>
                BrowserPackageAssemblySemanticPopulationCompletionKind
                    .SourceFailed,
            PackageAcquisitionPopulationCompletionKind.ExactCoordinates =>
                throw new InvalidOperationException(
                    "Browser assembly-semantic Package Query does not admit caller-pinned coordinates."),
            _ => throw new InvalidOperationException(
                "Unknown package population completion."),
        };

    static BrowserPackageAssemblyNotApplicableReason Project(
        PackageAssemblyNotApplicableReason reason) =>
        reason switch
        {
            PackageAssemblyNotApplicableReason.NoCompileAssets =>
                BrowserPackageAssemblyNotApplicableReason.NoCompileAssets,
            PackageAssemblyNotApplicableReason.NoMatchingTargetFramework =>
                BrowserPackageAssemblyNotApplicableReason
                    .NoMatchingTargetFramework,
            PackageAssemblyNotApplicableReason.EmptyCompileGroup =>
                BrowserPackageAssemblyNotApplicableReason.EmptyCompileGroup,
            PackageAssemblyNotApplicableReason.NoImplementationCounterpart =>
                BrowserPackageAssemblyNotApplicableReason
                    .NoImplementationCounterpart,
            _ => throw new InvalidOperationException(
                "Unknown package assembly applicability reason."),
        };

    static string Describe(PackageAssemblyNotApplicableReason reason) =>
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

    internal static BrowserPackageQueryRow ProjectResultRow(
        BrowserPackageAssemblySemanticResult result)
    {
        string[] preview =
        [
            .. result.Occurrences.Take(3).Select(occurrence =>
                $"Method 0x{occurrence.MethodDefinitionToken:X8}, "
                + $"IL_{occurrence.IlOffset:X4}: "
                + LiteralExcerpt(occurrence.LiteralText)),
        ];
        return new(
            result.PackageId,
            result.Version,
            BrowserPackageQueryAcquisitionTier.Assembly,
            [],
            [
                new(
                    "selected-assembly",
                    BrowserPackageQueryEvidenceScope.Package,
                    new(
                        result.Occurrences.Length,
                        preview),
                    [
                        new("path", result.SelectedAsset.Path),
                        new(
                            "literal-use-count",
                            result.Occurrences.Length.ToString(
                                CultureInfo.InvariantCulture)),
                        new(
                            "unevaluated-sibling-count",
                            result.SelectedAsset.UnevaluatedSiblings.ToString(
                                CultureInfo.InvariantCulture)),
                    ],
                    null),
            ],
            TotalDownloads: null,
            Verified: null,
            Producer: result.Producer,
            Description:
                "Matched the decoded library-literal query.",
            RootRequest: result.SelectedAsset.RootRequest);
    }

    static string LiteralExcerpt(string value) =>
        value.Length <= 160 ? value : value[..160] + "...";

    static string GalleryProducer =>
        BrowserPackageWorkspace.Gallery.Source.Producer.Display.ToString();

    internal static BrowserPackageQueryEvent ProjectAssemblySemanticProgress(
        int candidateOrdinal,
        int candidateCount) =>
        new(
            BrowserPackageQueryEventKind.Progress,
            Row: null,
            Failure: null,
            Completion: null,
            new(
                BrowserPackageQueryProgressPhase.Assembly,
                candidateOrdinal,
                candidateCount));

    sealed class AssemblySemanticProgressSink(
        int candidateCount,
        Action<BrowserPackageQueryEvent> emit)
        : IPackageAssemblySemanticQueryNonterminalSink
    {
        public ValueTask ReportAsync(
            PackageAssemblySemanticQueryCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            emit(ProjectAssemblySemanticProgress(
                outcome.CandidateOrdinal,
                candidateCount));
            return ValueTask.CompletedTask;
        }
    }
}

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    [JSExport]
    public static async Task<string> RunPackageAssemblySemanticQuery(
        string operationId,
        string packageInput,
        string literal,
        string targetFramework,
        int maximumCandidates,
        bool includePrerelease,
        int initialMatchCredit,
        JSObject eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        try
        {
            BrowserPackageQueryOperations.ValidateAssemblySemanticLiteral(
                literal);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        }
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(
                BrowserPackageQueryResult.ExpectedFailure(exception.Message),
                BrowserPackageJsonContext.Default.BrowserPackageQueryResult);
        }

        BrowserManagedOperationResult<
            BrowserPackageQueryInspection,
            string,
            string> result =
            await BrowserPackageQueryOperationCoordinator.RunAsync<
                BrowserPackageQueryInspection,
                BrowserPackageQueryEvent>(
                BrowserManagedOperationId.From(operationId),
                initialMatchCredit,
                queryEvent => eventSink.SetProperty(
                    "event",
                    BrowserPackageQueryOperations.Serialize(queryEvent)),
                async (_, events, token) =>
                {
                    await BrowserPackageQueryOperations
                        .WaitForSerializationPreparationAsync()
                        .WaitAsync(token)
                        .ConfigureAwait(false);
                    return await BrowserPackageWorkspace.RunPackageOperationAsync(
                        deadline =>
                            BrowserPackageQueryOperations
                                .ExecuteAssemblySemanticAsync(
                                    packageInput,
                                    literal,
                                    targetFramework,
                                    maximumCandidates,
                                    includePrerelease,
                                    events.Report,
                                    deadline.Token),
                        BrowserPackageWorkspace.PackageOperationTimeout,
                        token).ConfigureAwait(false);
                });
        return JsonSerializer.Serialize(
            BrowserPackageQueryResult.From(result),
            BrowserPackageJsonContext.Default.BrowserPackageQueryResult);
    }
}
