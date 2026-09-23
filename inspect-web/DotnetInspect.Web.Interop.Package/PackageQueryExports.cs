using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Ecosystems;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using QuerySpace;
using QuerySpace.Rows;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Web;
using DotnetInspect.Web.Interop.Package;
using NuGetFetch;

namespace DotnetInspect.Web.Interop.Package
{
    [SupportedOSPlatform("browser")]
    internal static partial class BrowserPackageQueryOperations
    {
        internal static BrowserPackageQueryCatalog Catalog() =>
            new(
                [
                    .. PackageQuery.RegisteredTerms
                        .Where(term =>
                            term.Descriptor.Role
                                == PackageQueryTermRole.Inspection)
                        .SelectMany(term =>
                            term.Descriptor.Options.Select(option =>
                        new BrowserPackageQueryPresetDescriptor(
                            term.Descriptor.Key,
                            PortableQueryModel.TextOf(
                                SingleControlOperator(term)),
                            option.Value,
                            option.Label,
                            option.Summary,
                            term.Descriptor.Weight,
                            term.Descriptor.Tier switch
                            {
                                PackageQueryAcquisitionTier.Nuspec =>
                                    BrowserPackageQueryAcquisitionTier.Nuspec,
                                PackageQueryAcquisitionTier.PackageContent =>
                                    BrowserPackageQueryAcquisitionTier.PackageContent,
                                PackageQueryAcquisitionTier.SearchMetadata =>
                                    BrowserPackageQueryAcquisitionTier.SearchMetadata,
                                _ => throw new InvalidOperationException(
                                    "Unknown package-query term tier."),
                            },
                            BrowserExecutionClass(
                                term.Descriptor.ExecutionClass),
                            term.Descriptor.SelectionGroupId,
                            term.Descriptor.CombinesWithinSelectionGroup,
                            term.Descriptor.ReplacementGroupId,
                            term.Descriptor.DisplayGroupId,
                            term.Descriptor.DisplayGroupLabel))),
                ],
                [
                    .. PackageQuery.RegisteredTerms
                        .Where(term =>
                            term.Descriptor.Role
                                == PackageQueryTermRole.Inspection
                            && term.Descriptor.ControlKind
                                is PackageQueryTermControlKind.Input
                                    or PackageQueryTermControlKind.MultilineInput)
                        .Select(term =>
                        new BrowserPackageQueryTermDescriptor(
                            term.Descriptor.Key,
                            term.Descriptor.Label,
                            term.Descriptor.Summary,
                            term.Descriptor.Weight,
                            term.Descriptor.Tier switch
                            {
                                PackageQueryAcquisitionTier.Nuspec =>
                                    BrowserPackageQueryAcquisitionTier.Nuspec,
                                PackageQueryAcquisitionTier.PackageContent =>
                                    BrowserPackageQueryAcquisitionTier.PackageContent,
                                PackageQueryAcquisitionTier.SearchMetadata =>
                                    BrowserPackageQueryAcquisitionTier.SearchMetadata,
                                _ => throw new InvalidOperationException(
                                    "Unknown package-query term tier."),
                            },
                            BrowserExecutionClass(
                                term.Descriptor.ExecutionClass),
                            [
                                .. term.Operators.Select(
                                    PortableQueryModel.TextOf),
                            ],
                            term.Descriptor.ValueKind,
                            term.Descriptor.ExampleValue,
                            term.Descriptor.ControlKind
                                == PackageQueryTermControlKind.MultilineInput)),
                ]);

        private static PortableQueryOperator SingleControlOperator(
            PackageQueryRegisteredTerm term) =>
            term.Operators.Length == 1
                ? term.Operators[0]
                : throw new InvalidOperationException(
                    $"Package Query control '{term.Descriptor.Key}' requires "
                    + "exactly one registered operator.");

        private static BrowserPackageQueryExecutionClass
            BrowserExecutionClass(
                PackageQueryExecutionClass executionClass) =>
            executionClass switch
            {
                PackageQueryExecutionClass.SearchMetadata =>
                    BrowserPackageQueryExecutionClass.SearchMetadata,
                PackageQueryExecutionClass.Nuspec =>
                    BrowserPackageQueryExecutionClass.Nuspec,
                PackageQueryExecutionClass.NuspecExpensive =>
                    BrowserPackageQueryExecutionClass.NuspecExpensive,
                PackageQueryExecutionClass.PackageContent =>
                    BrowserPackageQueryExecutionClass.PackageContent,
                PackageQueryExecutionClass.Metadata =>
                    BrowserPackageQueryExecutionClass.Metadata,
                PackageQueryExecutionClass.MetadataExpensive =>
                    BrowserPackageQueryExecutionClass.MetadataExpensive,
                _ => throw new InvalidOperationException(
                    "Unknown package-query execution class."),
            };

        internal static PackageQueryPlanResult Plan(
            string text,
            IReadOnlyCollection<PortableQueryTerm>? terms,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            string? targetFramework) =>
            PackageQuery.PlanInput(
                text,
                EcosystemPackCatalog.PackageQueryMemberships,
                terms,
                maximumCandidates,
                maximumMatches,
                includePrerelease,
                RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(maximumMatches),
                ]),
                targetFramework);

        internal static bool TryCreateTerms(
            BrowserPackageQueryTerm[] wireTerms,
            out PortableQueryTerm[] terms,
            out string error)
        {
            ArgumentNullException.ThrowIfNull(wireTerms);
            terms = new PortableQueryTerm[wireTerms.Length];
            for (int index = 0; index < wireTerms.Length; index++)
            {
                BrowserPackageQueryTerm term = wireTerms[index];
                if (!PortableQueryModel.TryParseOperator(
                        term.Operator,
                        out PortableQueryOperator @operator))
                {
                    terms = [];
                    error =
                        $"Unknown package-query operator '{term.Operator}'.";
                    return false;
                }
                terms[index] =
                    new PortableQueryTerm(term.Key, @operator, term.Value);
            }
            error = "";
            return true;
        }

        internal static async Task<BrowserPackageQueryInspection> ExecuteAsync(
            string prefix,
            PortableQueryTerm[] terms,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null,
            string? targetFramework = null)
            => await ExecuteAsync(
                prefix,
                terms,
                maximumCandidates,
                maximumMatches,
                includePrerelease,
                contentProvider: null,
                matchCredit,
                emit,
                cancellationToken,
                deadline,
                targetFramework).ConfigureAwait(false);

        internal static async Task<BrowserPackageQueryInspection> ExecuteAsync(
            string prefix,
            PortableQueryTerm[] terms,
            int maximumCandidates,
            int maximumMatches,
            bool includePrerelease,
            IPackageQueryContentProvider? contentProvider,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null,
            string? targetFramework = null)
        {
            ArgumentNullException.ThrowIfNull(terms);
            ArgumentNullException.ThrowIfNull(emit);

            PackageQueryPlanResult planResult = Plan(
                prefix,
                terms,
                maximumCandidates,
                maximumMatches,
                includePrerelease,
                targetFramework);
            if (planResult is PackageQueryPlanResult.Rejected rejected)
                throw new InvalidOperationException(rejected.Failure.Message);

            return await ExecuteAsync(
                ((PackageQueryPlanResult.Accepted)planResult).Plan,
                contentProvider,
                matchCredit,
                emit,
                cancellationToken,
                deadline).ConfigureAwait(false);
        }

        internal static async Task<BrowserPackageQueryInspection> ExecuteAsync(
            PackageQueryPlan plan,
            IPackageQueryContentProvider? contentProvider,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(emit);

            var observer = new EventObserver(
                matchCredit,
                emit,
                deadline);
            await using PackageSourceSettlementLease? sourceLease =
                plan.RequiresDependencyTraversal
                    ? PackageSourceSettlementService.IssueLease(
                        authority =>
                            ReferenceEquals(
                                authority.Association,
                                BrowserPackageWorkspace.Gallery.Source.Association)
                                ? BrowserPackageWorkspace.Gallery
                                : throw new InvalidOperationException(
                                    "Browser Package Query requested an unauthorized package source."))
                    : null;
            PackageQueryDependencyTraversalServices? traversalServices = null;
            if (sourceLease is not null)
            {
                var candidateSource =
                    new AuthorizedPackageDependencyCandidateSource(
                        BrowserPackageWorkspace.PackageSourceAuthorization,
                        sourceLease);
                traversalServices = new(
                    new PackageDependencyTraversalCandidateAdapter(
                        candidateSource),
                    new AuthorizedPackageDependencyManifestSource(
                        candidateSource));
            }
            await using PackageSourceSettlementLease? semanticSettlement =
                plan.RequiresLibraryLiteralEvaluation
                    ? PackageSourceSettlementService.IssueLease(
                        authority =>
                            ReferenceEquals(
                                authority.Association,
                                BrowserPackageWorkspace.Gallery.Source.Association)
                                ? BrowserPackageWorkspace.Gallery
                                : throw new InvalidOperationException(
                                    "Browser library-literal Package Query requested an unauthorized package source."))
                    : null;
            PackageAssemblySemanticFindBudget? semanticBudget =
                plan.RequiresLibraryLiteralEvaluation
                    ? BrowserAssemblySemanticBudget()
                    : null;
            PackageQueryAssemblySemanticExecution? semanticExecution =
                semanticSettlement is null
                    ? null
                    : new(
                        BrowserPackageWorkspace.PackageSourceAuthorization,
                        token => semanticSettlement.IssueOperationLease(
                            token,
                            BrowserPackageWorkspace.GalleryOperationTimeout,
                            semanticBudget!.MaximumDuration),
                        new PackagePayloadAcquisitionPlan(
                            static (_, _) =>
                                BrowserPackageWorkspace.SessionPackageStore,
                            transferPolicy:
                                BrowserPackageWorkspace.PackageTransferPolicy),
                        semanticBudget!,
                        new LibraryLiteralAssessmentSink(emit));
            var envelope = await PackageQueryInspection.ExecuteAsync(
                    BrowserPackageWorkspace.Gallery,
                    plan,
                    contentProvider,
                    traversalServices,
                    semanticExecution,
                    observer,
                    cancellationToken)
                .ConfigureAwait(false);
            return Complete(envelope);
        }

        private static PackageAssemblySemanticFindBudget
            BrowserAssemblySemanticBudget()
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

        internal static Task<BrowserPackageQueryEvent> PumpAsync(
            IAsyncEnumerable<PackageQueryEvent> events,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null) =>
            PumpAsync(events, Project, matchCredit, emit, cancellationToken, deadline);

        internal static Task<BrowserPackageQueryEvent> ExecuteAssemblyAsync(
            PackageAssemblyQueryPlan plan,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null) =>
            PumpAsync(
                PackageAssemblyQuery.ExecuteAsync(
                    AssemblyQueryPayloadProvider.Instance,
                    plan,
                    cancellationToken),
                queryEvent => ProjectAssembly(plan, queryEvent),
                matchCredit,
                emit,
                cancellationToken,
                deadline);

        sealed class AssemblyQueryPayloadProvider
            : IPackageRootPayloadProvider
        {
            internal static AssemblyQueryPayloadProvider Instance { get; } =
                new();

            public ValueTask<PackageRootPayloadResult> GetPayloadAsync(
                PackageSourceCoordinate coordinate,
                string? requiredProducerKey,
                PackagePayloadLimits limits,
                CancellationToken cancellationToken) =>
                BrowserPackageWorkspace.AcquirePackageAssemblyQueryPayloadAsync(
                    coordinate,
                    requiredProducerKey,
                    limits,
                    cancellationToken);
        }

        internal static BrowserPackageQueryEvent ProjectAssembly(
            PackageAssemblyQueryPlan plan,
            PackageAssemblyQueryEvent queryEvent) =>
            queryEvent switch
            {
                PackageAssemblyQueryEvent.Progress progress =>
                    new(BrowserPackageQueryEventKind.Progress, null, null, null,
                        new(BrowserPackageQueryProgressPhase.Assembly,
                            progress.CompletedCandidates, progress.Limit)),
                PackageAssemblyQueryEvent.AcquisitionFailed failed =>
                    new(BrowserPackageQueryEventKind.Failure, null,
                        new(failed.Value.Coordinate.PackageId, failed.Value.Coordinate.Version,
                            failed.Value.Producer.ToString(),
                            BrowserPackageQueryFailureKind.AssemblyAcquisition,
                            failed.Value.Message.ToString()), null, null),
                PackageAssemblyQueryEvent.Evaluated evaluated =>
                    ProjectAssemblyOutcome(evaluated.Value),
                PackageAssemblyQueryEvent.Completed completed =>
                    new(BrowserPackageQueryEventKind.Completed, null, null,
                        new(
                            plan.Pattern.Operand.DisplayText.ToString(),
                            BrowserPackageWorkspace.Gallery.Source.Producer.Display.ToString(),
                            plan.Coordinates.Length, plan.Coordinates.Length,
                            completed.Value.Candidates, completed.Value.Matches,
                            completed.Value.Failures,
                            BrowserPackageQueryCompletionKind.ExplicitCandidatesComplete,
                            SemanticMisses: completed.Value.SemanticMisses,
                            NotApplicable: completed.Value.NotApplicable,
                            Scope: "Selected primary implementation assemblies only; not all package assemblies."),
                        null),
                _ => throw new InvalidOperationException("Unknown assembly-query event."),
            };

        static BrowserPackageQueryEvent ProjectAssemblyOutcome(PackageAssemblyEvaluationOutcome outcome)
        {
            PackageAssemblyEvaluationSubject subject = outcome.Subject;
            string id = subject.Coordinate.PackageId;
            string version = subject.Coordinate.Version;
            string rootRequest = subject.RootRequest.Encode();
            return outcome switch
            {
                PackageAssemblyEvaluationOutcome.Matched { SelectedAsset: { } selected } matched =>
                    new(BrowserPackageQueryEventKind.Match,
                        new(id, version, BrowserPackageQueryAcquisitionTier.Assembly,
                            [],
                            [
                                new("selected-assembly",
                                    BrowserPackageQueryEvidenceScope.Package,
                                    null,
                                    [
                                        new("path", selected.Asset.Path.ToString()),
                                        new(
                                            "literal-use-count",
                                            matched.Evidence.Occurrences.Length.ToString(
                                                CultureInfo.InvariantCulture)),
                                        new(
                                            "unevaluated-sibling-count",
                                            selected.UnevaluatedSiblings.ToString(
                                                CultureInfo.InvariantCulture)),
                                    ],
                                    null),
                                .. matched.Evidence.Occurrences.Take(3).Select(occurrence =>
                                    new BrowserPackageQueryEvidence("literal-use",
                                        BrowserPackageQueryEvidenceScope.Package,
                                        null,
                                        [
                                            new(
                                                "method-token",
                                                $"0x{occurrence.Address.MethodDefinitionToken:X8}"),
                                            new(
                                                "il-offset",
                                                $"IL_{occurrence.Address.ILOffset:X4}"),
                                            new(
                                                "excerpt",
                                                Excerpt(occurrence.LiteralText.ToString())),
                                        ],
                                        null)),
                            ],
                            null, null, subject.Coordinate.Producer,
                            RootRequest: rootRequest),
                        null, null, null),
                PackageAssemblyEvaluationOutcome.NoMatch { SelectedAsset: { } selected } =>
                    new(BrowserPackageQueryEventKind.Assessment, null, null, null, null,
                        new(id, version, BrowserPackageAssemblyAssessmentKind.NoMatch,
                            "The selected implementation assembly has no matching decoded ldstr use.",
                            selected.Asset.Path.ToString(), rootRequest, [])),
                PackageAssemblyEvaluationOutcome.NotApplicable notApplicable =>
                    new(BrowserPackageQueryEventKind.Assessment, null, null, null, null,
                        new(id, version, BrowserPackageAssemblyAssessmentKind.NotApplicable,
                            notApplicable.Reason switch
                            {
                                PackageAssemblyNotApplicableReason.NoCompileAssets => "No compile assembly is available.",
                                PackageAssemblyNotApplicableReason.NoMatchingTargetFramework => "No compile group matches the requested framework.",
                                PackageAssemblyNotApplicableReason.EmptyCompileGroup => "The selected compile group is explicitly empty.",
                                PackageAssemblyNotApplicableReason.NoImplementationCounterpart => "The primary compile assembly has no implementation counterpart.",
                                _ => throw new InvalidOperationException("Unknown assembly-query applicability outcome."),
                            },
                            notApplicable.SelectedAsset?.Asset.Path.ToString(),
                            rootRequest,
                            [])),
                PackageAssemblyEvaluationOutcome.Failure failure =>
                    new(BrowserPackageQueryEventKind.Failure, null,
                        new(id, version, subject.Coordinate.Producer,
                            BrowserPackageQueryFailureKind.AssemblyEvaluation,
                            $"Assembly evaluation failed: {failure.Reason.Stage}."
                            + (failure.Cleanup is null ? "" : " Candidate cleanup was incomplete.")),
                        null, null),
                _ => throw new InvalidOperationException("Unknown assembly-query evaluation outcome."),
            };
        }

        static string Excerpt(string value) =>
            value.Length <= 160 ? value : value[..160] + "...";

        internal static async Task<BrowserPackageQueryEvent> PumpAsync<TEvent>(
            IAsyncEnumerable<TEvent> events,
            Func<TEvent, BrowserPackageQueryEvent> project,
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            CancellationToken cancellationToken,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline = null)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(emit);
            BrowserPackageQueryEvent? completedEvent = null;
            await foreach (TEvent queryEvent in events
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                BrowserPackageQueryEvent projected = project(queryEvent);
                if (completedEvent is not null)
                {
                    throw new InvalidOperationException(
                        "The package-query stream produced an event after completion.");
                }

                if (projected.Kind == BrowserPackageQueryEventKind.Completed)
                {
                    completedEvent = projected;
                    continue;
                }

                if (projected.Kind == BrowserPackageQueryEventKind.Match
                    && matchCredit is not null)
                {
                    try
                    {
                        if (deadline is null)
                        {
                            await matchCredit.WaitAsync(cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await deadline.WaitForConsumerAsync(matchCredit.WaitAsync)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested
                            && (deadline is null
                                || deadline.CallerCancellation.IsCancellationRequested))
                    {
                        // Only caller cancellation revokes Browser publication;
                        // a timeout must not hand off this uncredited match.
                        emit(projected);
                        throw;
                    }
                }

                emit(projected);
            }

            return completedEvent
                ?? throw new InvalidOperationException(
                    "The package-query stream ended without a completion event.");
        }

        internal sealed class EventObserver(
            BrowserPackageQueryMatchCredit? matchCredit,
            Action<BrowserPackageQueryEvent> emit,
            BrowserPackageWorkspace.BrowserPackageOperationDeadline? deadline)
            : IPackageQueryNonterminalSink
        {
            public async ValueTask ReportAsync(
                PackageQueryEvent.Nonterminal queryEvent,
                CancellationToken cancellationToken)
            {
                BrowserPackageQueryEvent projected = Project(queryEvent);

                if (projected.Kind == BrowserPackageQueryEventKind.Match
                    && matchCredit is not null)
                {
                    try
                    {
                        if (deadline is null)
                        {
                            await matchCredit.WaitAsync(cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await deadline.WaitForConsumerAsync(
                                    matchCredit.WaitAsync)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested
                            && (deadline is null
                                || deadline.CallerCancellation
                                    .IsCancellationRequested))
                    {
                        emit(projected);
                        throw;
                    }
                }

                emit(projected);
            }
        }

        private sealed class LibraryLiteralAssessmentSink(
            Action<BrowserPackageQueryEvent> emit)
            : IPackageQueryLibraryLiteralAssessmentSink
        {
            public ValueTask ReportAsync(
                PackageQueryLibraryLiteralAssessment assessment,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BrowserPackageAssemblySemanticCandidateOutcome projected =
                    Project(assessment);
                var browserAssessment = new BrowserPackageAssemblyAssessment(
                    projected.PackageId,
                    projected.Version,
                    projected.Kind switch
                    {
                        BrowserPackageAssemblySemanticCandidateOutcomeKind
                            .Matched =>
                            BrowserPackageAssemblyAssessmentKind.Matched,
                        BrowserPackageAssemblySemanticCandidateOutcomeKind
                            .NoMatch =>
                            BrowserPackageAssemblyAssessmentKind.NoMatch,
                        BrowserPackageAssemblySemanticCandidateOutcomeKind
                            .NotApplicable =>
                            BrowserPackageAssemblyAssessmentKind.NotApplicable,
                        BrowserPackageAssemblySemanticCandidateOutcomeKind
                            .Failure =>
                            BrowserPackageAssemblyAssessmentKind.Failure,
                        BrowserPackageAssemblySemanticCandidateOutcomeKind
                            .NotEvaluated =>
                            BrowserPackageAssemblyAssessmentKind.NotEvaluated,
                        _ => throw new InvalidOperationException(
                            "Unknown library-literal assessment kind."),
                    },
                    projected.Message
                        ?? "The selected implementation libraries contain matching decoded ldstr uses.",
                    projected.SelectedAsset?.Path,
                    projected.RootRequest,
                    projected.Libraries);
                emit(new(
                    BrowserPackageQueryEventKind.Assessment,
                    Row: null,
                    Failure: null,
                    Completion: null,
                    Progress: null,
                    browserAssessment));
                return ValueTask.CompletedTask;
            }
        }

        internal static BrowserPackageQueryInspection Complete(
            InspectionEnvelope<PackageQueryDocument> envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            return new BrowserPackageQueryInspection(
                Project(envelope.Content),
                Project(envelope.Share),
                [.. envelope.Diagnostics.Select(diagnostic =>
                    new BrowserInspectionDiagnostic(
                        diagnostic.Code,
                        diagnostic.Severity.ToString(),
                        diagnostic.Summary.ToString(),
                        diagnostic.Correspondence?.ToString()))]);
        }

        internal static BrowserInspectionShare Project(InspectionShare share) =>
            share switch
            {
                InspectionShare.Available available =>
                    new(
                        BrowserInspectionShareKind.Available,
                        available.FullUrl,
                        available.Packet,
                        Path: null,
                        Reason: null),
                InspectionShare.NonProjectable nonProjectable =>
                    new(
                        BrowserInspectionShareKind.NonProjectable,
                        FullUrl: null,
                        Packet: null,
                        nonProjectable.Path,
                        nonProjectable.Reason.ToString()),
                _ => throw new InvalidOperationException(
                    "Unknown inspection Share outcome."),
            };

        internal static BrowserPackageQueryEvent Project(
            PackageQueryEvent queryEvent) =>
            queryEvent switch
            {
            PackageQueryEvent.Progress progress =>
                new BrowserPackageQueryEvent(
                    BrowserPackageQueryEventKind.Progress,
                    Row: null,
                    Failure: null,
                    Completion: null,
                    Progress: new BrowserPackageQueryProgress(
                        progress.Value.Phase switch
                        {
                            PackageQueryProgressPhase.Search =>
                                BrowserPackageQueryProgressPhase.Search,
                            PackageQueryProgressPhase.Manifest =>
                                BrowserPackageQueryProgressPhase.Manifest,
                            PackageQueryProgressPhase.PackageContent =>
                                BrowserPackageQueryProgressPhase.PackageContent,
                            PackageQueryProgressPhase.DependencyTraversal =>
                                BrowserPackageQueryProgressPhase.DependencyTraversal,
                            PackageQueryProgressPhase.Assembly =>
                                BrowserPackageQueryProgressPhase.Assembly,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query progress phase."),
                        },
                        progress.Value.Completed,
                        progress.Value.Limit)),
            PackageQueryEvent.Match match =>
                new BrowserPackageQueryEvent(
                    BrowserPackageQueryEventKind.Match,
                    Row: new BrowserPackageQueryRow(
                        match.Value.Package.PackageId,
                        match.Value.Package.Version,
                        match.Value.Tier switch
                        {
                            PackageQueryAcquisitionTier.SearchMetadata =>
                                BrowserPackageQueryAcquisitionTier.SearchMetadata,
                            PackageQueryAcquisitionTier.Nuspec =>
                                BrowserPackageQueryAcquisitionTier.Nuspec,
                            PackageQueryAcquisitionTier.PackageContent =>
                                BrowserPackageQueryAcquisitionTier.PackageContent,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query match tier."),
                        },
                        [
                            .. match.Value.Answers.Select(answer =>
                                new BrowserPackageQueryAnswer(
                                    answer.Id,
                                    answer.Value,
                                    answer.Term is { } answerTerm
                                        ? new BrowserPackageQueryTerm(
                                            answerTerm.Key,
                                            PortableQueryModel.TextOf(
                                                answerTerm.Operator),
                                            answerTerm.Value)
                                        : null)),
                        ],
                        [
                            .. match.Value.Evidence.Select(evidence =>
                                new BrowserPackageQueryEvidence(
                                    evidence.Id,
                                    evidence.Scope switch
                                    {
                                        PackageQueryEvidenceScope.Package =>
                                            BrowserPackageQueryEvidenceScope.Package,
                                        PackageQueryEvidenceScope.Query =>
                                            BrowserPackageQueryEvidenceScope.Query,
                                        _ => throw new InvalidOperationException(
                                            "Unknown package-query evidence scope."),
                                    },
                                    evidence.Summary is { } summary
                                        ? new BrowserPackageQueryEvidenceSummary(
                                            summary.Count,
                                            [
                                                .. summary.Preview.Select(
                                                    value => value.ToString()),
                                            ])
                                        : null,
                                    [
                                        .. evidence.Properties.Select(
                                            property =>
                                                new BrowserPackageQueryEvidenceProperty(
                                                    property.Name,
                                                    property.Value)),
                                    ],
                                    evidence.Number,
                                    evidence.Term is { } term
                                        ? new BrowserPackageQueryTerm(
                                            term.Key,
                                            PortableQueryModel.TextOf(
                                                term.Operator),
                                            term.Value)
                                        : null)),
                        ],
                        match.Value.Package.TotalDownloads,
                        match.Value.Package.Verified,
                        match.Value.Package.Source.Producer.Display.ToString(),
                        match.Value.Package.Description,
                        match.Value.LibraryLiteral?.RootRequest.Encode())
                    {
                        Owners = [.. match.Value.Package.Owners],
                        Manifest = match.Value.Package.Manifest is { } manifest
                            ? Project(manifest)
                            : null,
                    },
                    Failure: null,
                    Completion: null),
            PackageQueryEvent.Failure failure =>
                new BrowserPackageQueryEvent(
                    BrowserPackageQueryEventKind.Failure,
                    Row: null,
                    Failure: new BrowserPackageQueryFailure(
                        failure.Value.PackageId,
                        failure.Value.Version,
                        failure.Value.Source.Producer.Display.ToString(),
                        failure.Value.Kind switch
                        {
                            PackageQueryFailureKind.Search =>
                                BrowserPackageQueryFailureKind.Search,
                            PackageQueryFailureKind.SearchContract =>
                                BrowserPackageQueryFailureKind.SearchContract,
                            PackageQueryFailureKind.ManifestAcquisition =>
                                BrowserPackageQueryFailureKind.ManifestAcquisition,
                            PackageQueryFailureKind.ManifestContract =>
                                BrowserPackageQueryFailureKind.ManifestContract,
                            PackageQueryFailureKind.InvalidManifest =>
                                BrowserPackageQueryFailureKind.InvalidManifest,
                            PackageQueryFailureKind.PackageContentAcquisition =>
                                BrowserPackageQueryFailureKind.PackageContentAcquisition,
                            PackageQueryFailureKind.PackageContentEvaluation =>
                                BrowserPackageQueryFailureKind.PackageContentEvaluation,
                            PackageQueryFailureKind.DependencyTraversal =>
                                BrowserPackageQueryFailureKind.DependencyTraversal,
                            PackageQueryFailureKind.AssemblyAcquisition =>
                                BrowserPackageQueryFailureKind.AssemblyAcquisition,
                            PackageQueryFailureKind.AssemblyEvaluation =>
                                BrowserPackageQueryFailureKind.AssemblyEvaluation,
                            PackageQueryFailureKind.AssemblyNotEvaluated =>
                                BrowserPackageQueryFailureKind.AssemblyNotEvaluated,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query failure kind."),
                        },
                        failure.Value.Message)
                    {
                        ManifestFailureReason =
                            failure.Value.ManifestFailureReason switch
                            {
                                PackageManifestFailureReason.MalformedXml =>
                                    BrowserPackageQueryManifestFailureReason.MalformedXml,
                                PackageManifestFailureReason.UnsupportedDocumentShape =>
                                    BrowserPackageQueryManifestFailureReason.UnsupportedDocumentShape,
                                PackageManifestFailureReason.IdentityMismatch =>
                                    BrowserPackageQueryManifestFailureReason.IdentityMismatch,
                                PackageManifestFailureReason.InvalidDependencyContract =>
                                    BrowserPackageQueryManifestFailureReason.InvalidDependencyContract,
                                PackageManifestFailureReason.ConfiguredLimitExceeded =>
                                    BrowserPackageQueryManifestFailureReason.ConfiguredLimitExceeded,
                                PackageManifestFailureReason.InvalidIdentityContract =>
                                    BrowserPackageQueryManifestFailureReason.InvalidIdentityContract,
                                null => null,
                                _ => throw new InvalidOperationException(
                                    "Unknown package-manifest failure reason."),
                            },
                    },
                    Completion: null),
            PackageQueryEvent.Completed completed =>
                new BrowserPackageQueryEvent(
                    BrowserPackageQueryEventKind.Completed,
                    Row: null,
                    Failure: null,
                    Completion: new BrowserPackageQueryCompletion(
                        completed.Value.Prefix.ToString(),
                        completed.Value.Source.Producer.Display.ToString(),
                        completed.Value.CandidateLimit,
                        completed.Value.MatchLimit
                            ?? throw new InvalidOperationException(
                                "Browser Package Query requires a match budget."),
                        completed.Value.Candidates,
                        completed.Value.Matches,
                        completed.Value.Failures,
                        completed.Value.Completion switch
                        {
                            PackageQueryCompletionKind.Exhausted =>
                                BrowserPackageQueryCompletionKind.Exhausted,
                            PackageQueryCompletionKind.MatchLimitReached =>
                                BrowserPackageQueryCompletionKind.MatchLimitReached,
                            PackageQueryCompletionKind.CandidateLimitReached =>
                                BrowserPackageQueryCompletionKind.CandidateLimitReached,
                            PackageQueryCompletionKind.SourcePageLimitReached =>
                                BrowserPackageQueryCompletionKind.SourcePageLimitReached,
                            PackageQueryCompletionKind.ClientPageLimitReached =>
                                BrowserPackageQueryCompletionKind.ClientPageLimitReached,
                            PackageQueryCompletionKind.Failed =>
                                BrowserPackageQueryCompletionKind.Failed,
                            PackageQueryCompletionKind.ExactPackageComplete =>
                                BrowserPackageQueryCompletionKind.ExactPackageComplete,
                            _ => throw new InvalidOperationException(
                                "Unknown package-query completion kind."),
                        },
                        completed.Value.SourceCandidates,
                        completed.Value.SemanticMisses,
                        completed.Value.NotApplicable,
                        completed.Value.Scope)
                    {
                        Occurrences = completed.Value.Occurrences,
                        NotEvaluated =
                            completed.Value.NotEvaluatedCandidates,
                        EvaluatedCandidates =
                            completed.Value.EvaluatedCandidates,
                        SemanticMatches =
                            completed.Value.SemanticMatches,
                    }),
                _ => throw new InvalidOperationException(
                    "Unknown package-query event."),
            };

        internal static BrowserPackageQueryDocument Project(
            PackageQueryDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            return new(
                [
                    .. document.Results.Select(result =>
                        Project(new PackageQueryEvent.Match(result)).Row!),
                ],
                document.HasPackages,
                [
                    .. document.Failures.Select(failure =>
                        Project(new PackageQueryEvent.Failure(failure)).Failure!),
                ],
                Project(new PackageQueryEvent.Completed(document.Summary))
                    .Completion!)
            {
                LibraryLiteralAssessments =
                [
                    .. document.LibraryLiteralAssessments.Select(assessment =>
                        Project(
                            assessment,
                            FindLibraryLiteralResult(document, assessment))),
                ],
            };
        }

        private static PackageQueryLibraryLiteralResult?
            FindLibraryLiteralResult(
                PackageQueryDocument document,
                PackageQueryLibraryLiteralAssessment assessment)
        {
            if (assessment.Kind
                is not PackageQueryLibraryLiteralAssessmentKind.Matched)
            {
                return null;
            }

            return document.Results
                .Where(result =>
                    string.Equals(
                        result.Package.PackageId,
                        assessment.PackageId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        result.Package.Version,
                        assessment.Version,
                        StringComparison.OrdinalIgnoreCase))
                .Select(result => result.LibraryLiteral)
                .SingleOrDefault(result => result is not null)
                ?? throw new InvalidOperationException(
                    "A matched library-literal assessment has no matching Package Query Result.");
        }

        private static BrowserPackageAssemblySemanticCandidateOutcome Project(
            PackageQueryLibraryLiteralAssessment assessment,
            PackageQueryLibraryLiteralResult? result = null) =>
            new(
                assessment.Kind switch
                {
                    PackageQueryLibraryLiteralAssessmentKind.Matched =>
                        BrowserPackageAssemblySemanticCandidateOutcomeKind.Matched,
                    PackageQueryLibraryLiteralAssessmentKind.NoMatch =>
                        BrowserPackageAssemblySemanticCandidateOutcomeKind.NoMatch,
                    PackageQueryLibraryLiteralAssessmentKind.NotApplicable =>
                        BrowserPackageAssemblySemanticCandidateOutcomeKind
                            .NotApplicable,
                    PackageQueryLibraryLiteralAssessmentKind.Failure =>
                        BrowserPackageAssemblySemanticCandidateOutcomeKind.Failure,
                    PackageQueryLibraryLiteralAssessmentKind.NotEvaluated =>
                        BrowserPackageAssemblySemanticCandidateOutcomeKind
                            .NotEvaluated,
                    _ => throw new InvalidOperationException(
                        "Unknown library-literal assessment kind."),
                },
                assessment.CandidateOrdinal,
                assessment.PackageId,
                assessment.Version,
                assessment.Source.Producer.Display.ToString(),
                result is null
                    ? null
                    : new BrowserPackageAssemblySemanticResult(
                        assessment.CandidateOrdinal,
                        assessment.PackageId,
                        assessment.Version,
                        assessment.Source.Producer.Display.ToString(),
                        new BrowserPackageAssemblySemanticSelectedAsset(
                            result.SelectedAsset.Path,
                            result.SelectedAsset.AssemblyName,
                            result.SelectedAsset.TargetFramework,
                            Sequence: "Implementation",
                            result.SelectedAsset.Ordinal,
                            result.SelectedAsset.UnevaluatedSiblings,
                            result.RootRequest.Encode()),
                        [
                           .. result.GetLibraryOccurrences().Select(value =>
                                new BrowserPackageAssemblySemanticOccurrence(
                                   value.SelectedAsset.Path,
                                   value.Evidence.Address.ModuleVersionId
                                       .ToString("D"),
                                   value.Evidence.Address
                                       .MethodDefinitionToken,
                                   value.Evidence.Address.ILOffset,
                                   value.Evidence.UserStringToken,
                                   value.Evidence.LiteralCharacterCount,
                                   value.Evidence.LiteralText.ToString())),
                        ]),
                assessment.SelectedAsset is { } selected
                    ? new BrowserPackageAssemblySemanticSelectedAsset(
                        selected.Path,
                        selected.AssemblyName,
                        selected.TargetFramework,
                        Sequence: "Implementation",
                        selected.Ordinal,
                        selected.UnevaluatedSiblings,
                        assessment.RootRequest?.Encode() ?? "")
                    : null,
                assessment.RootRequest?.Encode(),
                assessment.NotApplicableReason switch
                {
                    PackageQueryLibraryLiteralNotApplicableReason
                        .NoCompileAssets =>
                        BrowserPackageAssemblyNotApplicableReason.NoCompileAssets,
                    PackageQueryLibraryLiteralNotApplicableReason
                        .NoMatchingTargetFramework =>
                        BrowserPackageAssemblyNotApplicableReason
                            .NoMatchingTargetFramework,
                    PackageQueryLibraryLiteralNotApplicableReason
                        .EmptyCompileGroup =>
                        BrowserPackageAssemblyNotApplicableReason.EmptyCompileGroup,
                    PackageQueryLibraryLiteralNotApplicableReason
                        .NoImplementationCounterpart =>
                        BrowserPackageAssemblyNotApplicableReason
                            .NoImplementationCounterpart,
                    null => null,
                    _ => throw new InvalidOperationException(
                        "Unknown library-literal applicability reason."),
                },
                assessment.FailureKind switch
                {
                    PackageQueryLibraryLiteralFailureKind.Acquisition =>
                        BrowserPackageAssemblySemanticFailureKind.Acquisition,
                    PackageQueryLibraryLiteralFailureKind.Evaluation =>
                        BrowserPackageAssemblySemanticFailureKind.Evaluation,
                    null => null,
                    _ => throw new InvalidOperationException(
                        "Unknown library-literal failure kind."),
                },
                assessment.FailureStage,
                assessment.NonEvaluationKind switch
                {
                    PackageQueryLibraryLiteralNonEvaluationKind
                        .OperationDeadline =>
                        BrowserPackageAssemblySemanticNonEvaluationKind
                            .OperationDeadline,
                    null => null,
                    _ => throw new InvalidOperationException(
                        "Unknown library-literal non-evaluation reason."),
                },
                TimeoutKind:
                    assessment.NonEvaluationKind is null
                        ? null
                        : "Operation",
                TimeoutSeconds: null,
                assessment.Message,
                ProjectLibraries(assessment));

        private static BrowserPackageAssemblySemanticLibraryAssessment[]
            ProjectLibraries(
                PackageQueryLibraryLiteralAssessment assessment)
        {
            if (assessment.Libraries.IsEmpty)
                return [];

            string rootRequest = assessment.RootRequest?.Encode()
                ?? throw new InvalidOperationException(
                    "A per-Library assessment requires a Root request.");
            return
            [
                .. assessment.Libraries.Select(library =>
                    new BrowserPackageAssemblySemanticLibraryAssessment(
                        new BrowserPackageAssemblySemanticSelectedAsset(
                            library.SelectedAsset.Path,
                            library.SelectedAsset.AssemblyName,
                            library.SelectedAsset.TargetFramework,
                            Sequence: "Implementation",
                            library.SelectedAsset.Ordinal,
                            library.SelectedAsset.UnevaluatedSiblings,
                            rootRequest),
                        library.Kind switch
                        {
                            PackageQueryLibraryLiteralLibraryAssessmentKind
                                .Matched =>
                                BrowserPackageAssemblySemanticLibraryAssessmentKind
                                    .Matched,
                            PackageQueryLibraryLiteralLibraryAssessmentKind
                                .NoMatch =>
                                BrowserPackageAssemblySemanticLibraryAssessmentKind
                                    .NoMatch,
                            PackageQueryLibraryLiteralLibraryAssessmentKind
                                .Failure =>
                                BrowserPackageAssemblySemanticLibraryAssessmentKind
                                    .Failure,
                            _ => throw new InvalidOperationException(
                                "Unknown per-Library assessment kind."),
                        },
                        library.Occurrences,
                        library.FailureStage,
                        library.Message)),
            ];
        }

        private static BrowserPackageQueryManifest Project(
            PackageManifestFacts manifest) =>
            new(
                manifest.Coordinate.PackageId,
                manifest.Coordinate.Version,
                manifest.ManifestVersion,
                manifest.Description?.ToString(),
                manifest.Authors,
                manifest.Repository,
                manifest.RepositoryType,
                manifest.RepositoryCommit,
                manifest.License,
                manifest.LicenseUrl,
                [.. manifest.PackageTypes],
                manifest.IsToolPackage,
                manifest.ReadmeFile,
                [
                    .. manifest.DependencyGroups.Select(group =>
                        new BrowserPackageQueryDeclaredDependencyGroup(
                            group.TargetFramework,
                            [
                                .. group.Dependencies.Select(dependency =>
                                    new BrowserPackageQueryDeclaredDependency(
                                        dependency.Id,
                                        dependency.VersionRange)),
                            ],
                            group.IsImplicitManifestGroup)),
                ],
                manifest.IconFile,
                manifest.IconUrl,
                manifest.IdentityProvenance switch
                {
                    PackageManifestIdentityProvenance.ExpectedCoordinate =>
                        BrowserPackageQueryManifestIdentityProvenance.ExpectedCoordinate,
                    PackageManifestIdentityProvenance.SelfAttested =>
                        BrowserPackageQueryManifestIdentityProvenance.SelfAttested,
                    _ => throw new InvalidOperationException(
                        "Unknown package-manifest identity provenance."),
                });

        internal static string Serialize(BrowserPackageQueryEvent queryEvent) =>
            JsonSerializer.Serialize(
                queryEvent,
                BrowserPackageJsonContext.Default.BrowserPackageQueryEvent);

        static readonly Lazy<Task> SerializationPreparation =
            new(PrepareSerializationAsync);

        internal static void StartSerializationPreparation() =>
            _ = SerializationPreparation.Value;

        internal static Task WaitForSerializationPreparationAsync() =>
            SerializationPreparation.Value;

        static async Task PrepareSerializationAsync()
        {
            await Task.Yield();
            // These payloads compile the event writers but never cross the Browser boundary.
            _ = Serialize(new BrowserPackageQueryEvent(
                BrowserPackageQueryEventKind.Progress,
                Row: null,
                Failure: null,
                Completion: null,
                Progress: new BrowserPackageQueryProgress(
                    BrowserPackageQueryProgressPhase.Search,
                    Completed: 0,
                    Limit: 1)));
            _ = Serialize(new BrowserPackageQueryEvent(
                BrowserPackageQueryEventKind.Match,
                Row: new BrowserPackageQueryRow(
                    "",
                    "",
                    BrowserPackageQueryAcquisitionTier.SearchMetadata,
                    [],
                    [
                        new BrowserPackageQueryEvidence(
                            "",
                            BrowserPackageQueryEvidenceScope.Query,
                            new BrowserPackageQueryEvidenceSummary(0, [""]),
                            [],
                            null),
                    ],
                    TotalDownloads: 0,
                    Verified: false,
                    Producer: "",
                    Description: ""),
                Failure: null,
                Completion: null));
        }
    }
}

namespace DotnetInspect.Web.Interop.Package
{
[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    [JSExport]
    public static string ListPackageQueryCatalog()
    {
        string result = JsonSerializer.Serialize(
            BrowserPackageQueryOperations.Catalog(),
            BrowserPackageJsonContext.Default.BrowserPackageQueryCatalog);
        BrowserPackageQueryOperations.StartSerializationPreparation();
        return result;
    }

    [JSExport]
    public static string CancelPackageQuery(
        string operationId,
        string reason)
    {
        BrowserPackageQueryCancellation result =
            BrowserPackageQueryCancellation.From(
                BrowserPackageQueryOperationCoordinator.RequestCancellation(
                    BrowserManagedOperationId.From(operationId),
                    BrowserManagedOperationCancelReasons.Parse(reason)));
        return JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default.BrowserPackageQueryCancellation);
    }

    [JSExport]
    public static string RequestPackageQueryMatches(
        string operationId,
        int additionalMatchCredit)
    {
        BrowserPackageQueryMatchCreditResponse result =
            BrowserPackageQueryMatchCreditResponse.From(
                BrowserPackageQueryOperationCoordinator.RequestMatches(
                    BrowserManagedOperationId.From(operationId),
                    additionalMatchCredit));
        return JsonSerializer.Serialize(
            result,
            BrowserPackageJsonContext.Default
                .BrowserPackageQueryMatchCreditResponse);
    }

    [JSExport]
    public static async Task<string> RunPackageQuery(
        string operationId,
        string prefix,
        string termsJson,
        string? targetFramework,
        int maximumCandidates,
        int maximumMatches,
        bool includePrerelease,
        int initialMatchCredit,
        JSObject eventSink)
    {
        ArgumentNullException.ThrowIfNull(eventSink);
        BrowserPackageQueryTerm[] wireTerms = JsonSerializer.Deserialize(
            termsJson,
            BrowserPackageJsonContext.Default.BrowserPackageQueryTermArray) ?? [];
        if (!BrowserPackageQueryOperations.TryCreateTerms(
                wireTerms,
                out PortableQueryTerm[] terms,
                out string termError))
        {
            return JsonSerializer.Serialize(
                BrowserPackageQueryResult.ExpectedFailure(termError),
                BrowserPackageJsonContext.Default.BrowserPackageQueryResult);
        }

        PackageQueryPlanResult planResult =
            BrowserPackageQueryOperations.Plan(
                prefix,
                terms,
                maximumCandidates,
                maximumMatches,
                includePrerelease,
                targetFramework);
        if (planResult is PackageQueryPlanResult.Rejected rejected)
        {
            return JsonSerializer.Serialize(
                BrowserPackageQueryResult.ExpectedFailure(
                    rejected.Failure.Message),
                BrowserPackageJsonContext.Default.BrowserPackageQueryResult);
        }
        PackageQueryPlan plan =
            ((PackageQueryPlanResult.Accepted)planResult).Plan;

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
                async (matchCredit, events, token) =>
                {
                    await BrowserPackageQueryOperations
                        .WaitForSerializationPreparationAsync()
                        .WaitAsync(token)
                        .ConfigureAwait(false);
                    return await BrowserPackageWorkspace.RunPackageOperationAsync(
                        async deadline =>
                        {
                            var contentProvider =
                                new BrowserPackageQueryContentProvider(deadline);
                            return await BrowserPackageQueryOperations.ExecuteAsync(
                                plan,
                                contentProvider,
                                matchCredit,
                                events.Report,
                                deadline.Token,
                                deadline);
                        },
                        BrowserPackageWorkspace.PackageOperationTimeout,
                        token).ConfigureAwait(false);
                });
        return JsonSerializer.Serialize(
            BrowserPackageQueryResult.From(result),
            BrowserPackageJsonContext.Default.BrowserPackageQueryResult);
    }
}
}

namespace DotnetInspect.Web.Interop.Package
{
    [SupportedOSPlatform("browser")]
    internal sealed class BrowserPackageQueryContentProvider(
        BrowserPackageWorkspace.BrowserPackageOperationDeadline deadline)
        : IPackageQueryContentProvider
    {
        public ValueTask<PackageQueryContentResult> GetContentAsync(
            PackageQueryPackage package,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BrowserPackageWorkspace.AcquirePackageQueryContentAsync(
                package,
                BrowserPackageWorkspace.Gallery,
                deadline);
        }
    }
}
