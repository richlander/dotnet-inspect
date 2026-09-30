using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Libraries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one focused Member metrics request over an owner-issued complete
/// exact-Member population.
/// </summary>
public static class MemberMetricsInspectionOperation
{
    private const string SharePath = "member-metrics-inspection/share";
    private const string ShareReason =
        "Member metrics do not yet have a canonical Workspace Share projection.";

    public static InspectionEnvelope<MemberMetricsInspectionOutcome>
        Execute(
            MemberMetricsInspectionRequest request,
            LibraryOperationLease lease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            MemberMetricsQueryRequestResult query =
                MemberMetricsQuery.ResolveRequest(
                    request.Query,
                    request.AuthorizedMetrics,
                    request.ProjectedMetrics,
                    cancellationToken);
            if (query
                is not MemberMetricsQueryRequestResult.Accepted accepted)
            {
                return Rejected(query);
            }

            if (!ReferenceEquals(request.Library, lease.Reference))
            {
                return Rejected(
                    MemberMetricsInspectionRejection
                        .LeaseReferenceMismatch);
            }

            PopulationValidation population =
                ValidatePopulation(request.Document);
            if (population.Rejection is { } populationRejection)
                return Rejected(populationRejection);

            LibraryContentReference? implementation =
                request.Library.ImplementationAssembly;
            if (implementation is null)
            {
                return Rejected(
                    MemberMetricsInspectionRejection
                        .MissingImplementationAssembly);
            }
            if (!ReferenceEquals(
                    request.Library.ApiAssembly,
                    implementation))
            {
                return Rejected(
                    MemberMetricsInspectionRejection
                        .SeparateApiAndImplementationAssemblies);
            }

            MemberOverloadPopulationBinding binding =
                request.Document.Overloads.Binding;
            if (!Matches(
                    request.Library.ApiAssembly.AssemblyIdentity,
                    binding.Assembly)
                || !Matches(
                    implementation.AssemblyIdentity,
                    binding.Assembly))
            {
                return Rejected(
                    MemberMetricsInspectionRejection
                        .AssemblyIdentityMismatch);
            }

            SnapshotOutcome snapshot;
            try
            {
                snapshot = lease.Snapshot(
                    implementation,
                    new SnapshotRequest(
                        request.Limits.MaximumAssemblyBytes,
                        binding),
                    static (view, state, token) =>
                        Snapshot(view, state, token),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnsupportedMetadataFormatException exception)
            {
                return Failed(
                    MemberMetricsInspectionFailure
                        .UnsupportedWindowsMetadata,
                    exception);
            }
            catch (MalformedMetadataRootException exception)
            {
                return Failed(
                    MemberMetricsInspectionFailure.MalformedMetadata,
                    exception);
            }
            catch (BadImageFormatException exception)
            {
                return Failed(
                    MemberMetricsInspectionFailure.MalformedMetadata,
                    exception);
            }
            catch (Exception exception) when (
                exception is ObjectDisposedException
                    or InvalidOperationException
                    or IOException)
            {
                return Failed(
                    MemberMetricsInspectionFailure.ContentAccess,
                    exception);
            }

            if (snapshot is SnapshotOutcome.Rejected rejected)
                return Rejected(rejected.Reason);
            if (snapshot is SnapshotOutcome.Failed failed)
            {
                return Failed(
                    failed.Reason,
                    failed.Message);
            }
            if (snapshot is SnapshotOutcome.Incomplete incomplete)
            {
                return Incomplete(
                    MemberMetricsInspectionBound.AssemblyBytes,
                    incomplete.Limit,
                    incomplete.Measured);
            }

            LibraryBodyAnalysisExecution analysis;
            try
            {
                var ready = (SnapshotOutcome.Ready)snapshot;
                analysis = LibraryBodyAnalysisService.ExecuteImage(
                    "MemberMetricsInspect",
                    ready.Content,
                    LibraryBodyAnalysisRequest
                        .CreateImplementationMetrics(
                            MemberMetricsQuery.AnalysisMetrics(
                                accepted.Plan.RequestedMetrics),
                            request.Limits.Analysis,
                            population.MethodTokens),
                    resolver: null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnsupportedMetadataFormatException exception)
            {
                return Failed(
                    MemberMetricsInspectionFailure
                        .UnsupportedWindowsMetadata,
                    exception);
            }
            catch (MalformedMetadataRootException exception)
            {
                return Failed(
                    MemberMetricsInspectionFailure.MalformedMetadata,
                    exception);
            }
            catch (BadImageFormatException exception)
            {
                return Failed(
                    MemberMetricsInspectionFailure.MalformedMetadata,
                    exception);
            }
            catch (Exception exception) when (
                exception is IOException
                    or InvalidOperationException
                    or ArgumentException
                    or OverflowException)
            {
                return Failed(
                    MemberMetricsInspectionFailure.Analysis,
                    exception);
            }

            LibraryImplementationMetricAnalysisResult metrics =
                analysis.ImplementationMetrics;
            if (!metrics.WasRequested
                || metrics.Participation is not { } participation)
            {
                return Failed(
                    MemberMetricsInspectionFailure.Analysis,
                    "Focused implementation metrics did not participate.");
            }
            if (participation.RequestedMetrics
                != MemberMetricsQuery.AnalysisMetrics(
                    accepted.Plan.RequestedMetrics))
            {
                return Failed(
                    MemberMetricsInspectionFailure.Analysis,
                    "Focused Analysis published a different metric request.");
            }
            if (!Matches(
                    metrics.Receipt.ModuleIdentity.AssemblyIdentity,
                    binding.Assembly)
                || metrics.Receipt.ModuleIdentity.ModuleVersionId
                    != binding.ModuleVersionId)
            {
                return Rejected(
                    MemberMetricsInspectionRejection
                        .StalePopulationBinding);
            }

            return Project(
                request,
                accepted.Plan,
                population.Rows,
                metrics,
                participation);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<MemberMetricsInspectionOutcome>
        Project(
            MemberMetricsInspectionRequest request,
            MemberMetricsQueryPlan plan,
            ImmutableArray<MemberOverloadShape> overloads,
            LibraryImplementationMetricAnalysisResult metrics,
            ImplementationMetricParticipationReceipt participation)
    {
        HashSet<int> expectedTokens =
            overloads
                .Select(static row => row.MetadataToken)
                .ToHashSet();
        if (metrics.Bodies.Any(body =>
                !expectedTokens.Contains(
                    body.Method.MetadataToken)))
        {
            return Failed(
                MemberMetricsInspectionFailure.Analysis,
                "Focused Analysis returned evidence outside the exact Member population.");
        }
        ImmutableArray<MethodIdentity> incompleteAttributionSources =
            metrics.AttributionClosure?.IncompleteSourceMethods
            ?? [];
        if (incompleteAttributionSources.Any(source =>
                !expectedTokens.Contains(source.MetadataToken)))
        {
            return Failed(
                MemberMetricsInspectionFailure.Analysis,
                "Focused Analysis returned attribution state outside the exact Member population.");
        }
        HashSet<int> incompleteAttributionTokens =
            incompleteAttributionSources
                .Select(static source => source.MetadataToken)
                .ToHashSet();

        IReadOnlyDictionary<int,
            ImmutableArray<MethodImplementationMetricEvidence>>
            bodiesByMethod =
                metrics.Bodies
                    .GroupBy(static body =>
                        body.Method.MetadataToken)
                    .ToDictionary(
                        static group => group.Key,
                        static group => group.ToImmutableArray());
        HashSet<int> managedTokens =
            metrics.ManagedMethodBodies
                .Select(static method => method.MetadataToken)
                .ToHashSet();
        ImmutableArray<OverloadCallRelationship> relationships =
        [
            .. (metrics.SiblingRelationships?.Relationships ?? [])
                .Where(relationship =>
                    expectedTokens.Contains(
                        relationship.Caller.MetadataToken)
                    && expectedTokens.Contains(
                        relationship.Callee.MetadataToken)),
        ];
        bool relationshipsComplete =
            metrics.SiblingRelationships?.IsComplete
            ?? !plan.RequestedMetrics.HasFlag(
                MemberMetricKind.SiblingRelationships);

        var rows =
            ImmutableArray.CreateBuilder<MemberMetricsRow>(
                overloads.Length);
        foreach (MemberOverloadShape overload in overloads)
        {
            bodiesByMethod.TryGetValue(
                overload.MetadataToken,
                out ImmutableArray<
                    MethodImplementationMetricEvidence> bodies);
            if (bodies.IsDefault)
                bodies = [];
            ImmutableArray<AnalysisDiagnostic> diagnostics =
            [
                .. metrics.Diagnostics.Where(diagnostic =>
                    diagnostic.MethodToken
                        == overload.MetadataToken
                    || diagnostic.SourceMethodToken
                        == overload.MetadataToken),
            ];

            MemberBodySizeMetric? bodySize =
                !plan.RequestedMetrics.HasFlag(
                    MemberMetricKind.BodySize)
                    ? null
                    : BodySize(
                        overload,
                        bodies,
                        diagnostics,
                        managedTokens,
                        participation.Work,
                        incompleteAttributionTokens.Contains(
                            overload.MetadataToken));
            MemberSiblingRelationshipMetric? siblingRelationships =
                !plan.RequestedMetrics.HasFlag(
                    MemberMetricKind.SiblingRelationships)
                    ? null
                    : new(
                        [
                            .. relationships.Where(
                                relationship =>
                                    relationship.Callee.MetadataToken
                                        == overload.MetadataToken),
                        ],
                        [
                            .. relationships.Where(
                                relationship =>
                                    relationship.Caller.MetadataToken
                                        == overload.MetadataToken),
                        ],
                        relationshipsComplete);
            rows.Add(
                new(
                    overload,
                    bodySize,
                    siblingRelationships));
        }

        ImmutableArray<MemberMetricsRow> completeRows =
            rows.MoveToImmutable();
        MemberMetricKind incompleteRequiredMetrics =
            IncompleteRequiredMetrics(
                completeRows,
                plan.QueryMetrics);
        MemberMetricsPopulationOutcome population;
        if (incompleteRequiredMetrics != MemberMetricKind.None)
        {
            population =
                new MemberMetricsPopulationOutcome.Incomplete(
                    MemberMetricsPopulationIncompleteReason
                        .RequiredEvidence,
                    incompleteRequiredMetrics);
        }
        else
        {
            RowSelectionResult<MemberMetricsRow> selected =
                RowQueryExecutor.Apply(
                    completeRows,
                    plan.Rows);
            population =
                !selected.IsSuccess
                    ? new MemberMetricsPopulationOutcome.Incomplete(
                        MemberMetricsPopulationIncompleteReason
                            .RowSelection,
                        MemberMetricKind.None,
                        selected.Failure!)
                    : plan.Terminal
                        is QuerySpaceTerminalRequirement.Count
                            ? new MemberMetricsPopulationOutcome.Count(
                                selected.Values.Count)
                            : new MemberMetricsPopulationOutcome.Rows(
                            [
                                .. selected.Values.Select(row =>
                                    Project(
                                        row,
                                        plan.ProjectedMetrics)),
                            ]);
        }
        MemberMetricsCoverageReceipt coverage =
            Coverage(
                completeRows,
                metrics.Bodies.Length,
                relationshipsComplete);
        var content = new MemberMetricsInspectionContent(
            request.Document.Subject,
            request.Document.Overloads.Binding,
            request.AuthorizedMetrics,
            plan.ProjectedMetrics,
            plan.RequestedMetrics,
            population,
            coverage,
            participation,
            metrics.Diagnostics);
        return new(
            new MemberMetricsInspectionOutcome.Available(content),
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            Diagnostics(metrics.Diagnostics));
    }

    private static MemberMetricKind IncompleteRequiredMetrics(
        ImmutableArray<MemberMetricsRow> rows,
        MemberMetricKind requiredMetrics)
    {
        MemberMetricKind incomplete =
            MemberMetricKind.None;
        if (requiredMetrics.HasFlag(MemberMetricKind.BodySize)
            && rows.Any(static row =>
                row.BodySize is not { IsComplete: true }))
        {
            incomplete |= MemberMetricKind.BodySize;
        }
        if (requiredMetrics.HasFlag(
                MemberMetricKind.SiblingRelationships)
            && rows.Any(static row =>
                row.SiblingRelationships
                    is not { IsComplete: true }))
        {
            incomplete |=
                MemberMetricKind.SiblingRelationships;
        }
        return incomplete;
    }

    private static MemberBodySizeMetric BodySize(
        MemberOverloadShape overload,
        ImmutableArray<MethodImplementationMetricEvidence> bodies,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        IReadOnlySet<int> managedTokens,
        ImplementationMetricWorkBudgetSnapshot? work,
        bool attributionIncomplete)
    {
        ImmutableArray<MemberPhysicalBodySize> physical =
        [
            .. bodies
                .Where(static body => body.ILBytes is not null)
                .Select(static body =>
                    new MemberPhysicalBodySize(
                        body.EvidenceMethod,
                        body.ILBytes!.Value)),
        ];
        bool exhausted =
            work?.MetricExhaustedLimit is not null
            || work?.AttributionExhaustedLimit is not null;
        bool rowExhausted =
            work?.AttributionExhaustedLimit is not null
            || work?.MetricExhaustedMethodToken
                == overload.MetadataToken
            || bodies.Any(body =>
                body.EvidenceMethod.MetadataToken
                    == work?.MetricExhaustedMethodToken)
            || work?.MetricExhaustedMethodToken
                is { } exhaustedMethodToken
                && diagnostics.Any(diagnostic =>
                    diagnostic.MethodToken
                        == exhaustedMethodToken
                    && diagnostic.SourceMethodToken
                        == overload.MetadataToken);

        MemberMetricCellState state;
        if (attributionIncomplete
            || rowExhausted
            || exhausted && physical.IsEmpty)
        {
            state = MemberMetricCellState.Incomplete;
        }
        else if (!physical.IsEmpty)
        {
            state = MemberMetricCellState.Available;
        }
        else if (!diagnostics.IsEmpty)
        {
            state = MemberMetricCellState.Failed;
        }
        else if (!managedTokens.Contains(overload.MetadataToken)
            && bodies.IsEmpty
            && !exhausted)
        {
            state = MemberMetricCellState.Bodyless;
        }
        else
        {
            state = MemberMetricCellState.Unavailable;
        }

        return new(
            state,
            physical.IsEmpty
                ? null
                : physical.Max(static body =>
                    body.EncodedIlBytes),
            physical,
            diagnostics);
    }

    private static MemberMetricsRow Project(
        MemberMetricsRow row,
        MemberMetricKind projected) =>
        row with
        {
            BodySize =
                projected.HasFlag(MemberMetricKind.BodySize)
                    ? row.BodySize
                    : null,
            SiblingRelationships =
                projected.HasFlag(
                    MemberMetricKind.SiblingRelationships)
                    ? row.SiblingRelationships
                    : null,
        };

    private static MemberMetricsCoverageReceipt Coverage(
        ImmutableArray<MemberMetricsRow> rows,
        int physicalBodyCount,
        bool relationshipsComplete)
    {
        MemberMetricCellState[] states =
        [
            .. rows
                .Select(static row => row.BodySize?.State)
                .OfType<MemberMetricCellState>(),
        ];
        return new(
            rows.Length,
            physicalBodyCount,
            states.Count(static state =>
                state is MemberMetricCellState.Available),
            states.Count(static state =>
                state is MemberMetricCellState.Bodyless),
            states.Count(static state =>
                state is MemberMetricCellState.Incomplete),
            states.Count(static state =>
                state is MemberMetricCellState.Unavailable),
            states.Count(static state =>
                state is MemberMetricCellState.Failed),
            relationshipsComplete);
    }

    private static PopulationValidation ValidatePopulation(
        MemberGroupDocument document)
    {
        MemberOverloadPopulationBinding binding =
            document.Overloads.Binding;
        if (binding.DeclaringType != document.Subject.DeclaringType
            || !binding.Name.Equals(
                document.Subject.Name,
                StringComparison.Ordinal)
            || binding.Category != document.Subject.Category
            || binding.Role != document.Subject.Role)
        {
            return PopulationValidation.Rejected(
                MemberMetricsInspectionRejection
                    .PopulationMismatch);
        }
        if (document.Overloads.Rows
            is not MemberOverloadRowsOutcome.Read rows)
        {
            return PopulationValidation.Rejected(
                MemberMetricsInspectionRejection
                    .PopulationRowsRequired);
        }
        if (!rows.IsComplete)
        {
            return PopulationValidation.Rejected(
                MemberMetricsInspectionRejection
                    .PopulationRowsIncomplete);
        }
        if (document.Overloads.Count
                is MemberOverloadCountOutcome.Counted counted
            && counted.Value != rows.Items.Length)
        {
            return PopulationValidation.Rejected(
                MemberMetricsInspectionRejection
                    .PopulationMismatch);
        }

        var tokens = new HashSet<int>();
        for (int index = 0; index < rows.Items.Length; index++)
        {
            MemberOverloadShape row = rows.Items[index];
            if (row.Binding != binding
                || row.BaselineOrdinal != index + 1
                || MetadataTokens.Handle(row.MetadataToken).Kind
                    != HandleKind.MethodDefinition)
            {
                return PopulationValidation.Rejected(
                    MemberMetricsInspectionRejection
                        .PopulationMismatch);
            }
            if (!tokens.Add(row.MetadataToken))
            {
                return PopulationValidation.Rejected(
                    MemberMetricsInspectionRejection
                        .DuplicateMemberIdentity);
            }
        }
        if (tokens.Count == 0)
        {
            return PopulationValidation.Rejected(
                MemberMetricsInspectionRejection
                    .PopulationMismatch);
        }

        return PopulationValidation.Accepted(
            rows.Items,
            tokens);
    }

    private static SnapshotOutcome Snapshot(
        scoped LibraryContentView view,
        SnapshotRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SnapshotOutcome identity =
            view.UseReadStream(
                stream => ValidateSnapshotIdentity(
                    stream,
                    request.Binding));
        if (identity is not SnapshotOutcome.IdentityReady)
            return identity;
        if (view.Content.Length > request.MaximumAssemblyBytes)
        {
            return new SnapshotOutcome.Incomplete(
                request.MaximumAssemblyBytes,
                view.Content.Length);
        }
        return new SnapshotOutcome.Ready(
            ImmutableArray.CreateRange(
                view.Content.ToArray()));
    }

    private static SnapshotOutcome ValidateSnapshotIdentity(
        Stream stream,
        MemberOverloadPopulationBinding binding)
    {
        using var peReader = new PEReader(
            stream,
            PEStreamOptions.LeaveOpen);
        if (!MetadataFormatAdmission.AdmitImage(peReader))
        {
            return new SnapshotOutcome.Failed(
                MemberMetricsInspectionFailure.NotManagedAssembly,
                "The Library implementation image has no managed metadata.");
        }
        MetadataReader reader = peReader.GetMetadataReader();
        if (!reader.IsAssembly)
        {
            return new SnapshotOutcome.Failed(
                MemberMetricsInspectionFailure.ManagedModule,
                "Member metrics require a managed assembly manifest.");
        }
        AssemblyReferenceIdentity actual =
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
        if (!Matches(actual, binding.Assembly))
        {
            return new SnapshotOutcome.Rejected(
                MemberMetricsInspectionRejection
                    .AssemblyIdentityMismatch);
        }
        Guid moduleVersionId =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        if (moduleVersionId == Guid.Empty)
        {
            return new SnapshotOutcome.Failed(
                MemberMetricsInspectionFailure.MalformedMetadata,
                "The Library implementation image has an empty MVID.");
        }
        return moduleVersionId == binding.ModuleVersionId
            ? new SnapshotOutcome.IdentityReady()
            : new SnapshotOutcome.Rejected(
                MemberMetricsInspectionRejection
                    .StalePopulationBinding);
    }

    private static bool Matches(
        ManagedMetadataIdentity.Assembly? actual,
        LibraryAssemblyIdentity expected) =>
        actual is not null
        && actual.Identity.IsEquivalentTo(Identity(expected));

    private static bool Matches(
        AssemblyReferenceIdentity? actual,
        LibraryAssemblyIdentity expected) =>
        actual is not null
        && actual.IsEquivalentTo(Identity(expected));

    private static AssemblyReferenceIdentity Identity(
        LibraryAssemblyIdentity assembly) =>
        new(
            assembly.Name.ToString(),
            assembly.Version,
            assembly.Culture?.ToString(),
            assembly.PublicKeyToken?.ToString());

    private static IEnumerable<InspectionDiagnostic> Diagnostics(
        ImmutableArray<AnalysisDiagnostic> diagnostics) =>
        diagnostics.Select(diagnostic =>
            new InspectionDiagnostic(
                "member-metrics.analysis-incomplete",
                InspectionDiagnosticSeverity.Warning,
                diagnostic.Message,
                $"method 0x{diagnostic.MethodToken:x8}"));

    private static InspectionEnvelope<MemberMetricsInspectionOutcome>
        Rejected(MemberMetricsQueryRequestResult result) =>
        result switch
        {
            MemberMetricsQueryRequestResult.Rejected rejected =>
                Rejected(Map(rejected.Reason)),
            MemberMetricsQueryRequestResult.IntentRejected =>
                Rejected(
                    MemberMetricsInspectionRejection
                        .QueryIntentRejected),
            MemberMetricsQueryRequestResult.RowIntentRejected rejected =>
                Rejected(
                    MemberMetricsInspectionRejection
                        .QueryIntentRejected,
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "An accepted Member metrics query cannot be rejected."),
        };

    private static MemberMetricsInspectionRejection Map(
        MemberMetricsQueryRequestRejection rejection) =>
        rejection switch
        {
            MemberMetricsQueryRequestRejection.QuerySpaceMismatch =>
                MemberMetricsInspectionRejection
                    .QuerySpaceMismatch,
            MemberMetricsQueryRequestRejection
                    .ParticipatingRowSetsMismatch =>
                MemberMetricsInspectionRejection
                    .ParticipatingRowSetsMismatch,
            MemberMetricsQueryRequestRejection.RowIntentMismatch =>
                MemberMetricsInspectionRejection
                    .RowIntentMismatch,
            MemberMetricsQueryRequestRejection.TerminalMismatch =>
                MemberMetricsInspectionRejection
                    .TerminalMismatch,
            MemberMetricsQueryRequestRejection
                    .ResultContractMismatch =>
                MemberMetricsInspectionRejection
                    .ResultContractMismatch,
            MemberMetricsQueryRequestRejection.UnauthorizedMetric =>
                MemberMetricsInspectionRejection
                    .UnauthorizedMetric,
            MemberMetricsQueryRequestRejection.NoEffectiveMetric =>
                MemberMetricsInspectionRejection
                    .NoEffectiveMetric,
            _ => throw new InvalidOperationException(
                "Unknown Member metrics query rejection."),
        };

    private static InspectionEnvelope<MemberMetricsInspectionOutcome>
        Rejected(
            MemberMetricsInspectionRejection reason,
            RowQueryFailure? rowQueryFailure = null) =>
        Envelope(
            new MemberMetricsInspectionOutcome.Rejected(
                reason,
                rowQueryFailure));

    private static InspectionEnvelope<MemberMetricsInspectionOutcome>
        Incomplete(
            MemberMetricsInspectionBound bound,
            long limit,
            long measured) =>
        Envelope(
            new MemberMetricsInspectionOutcome.Incomplete(
                bound,
                limit,
                measured));

    private static InspectionEnvelope<MemberMetricsInspectionOutcome>
        Failed(
            MemberMetricsInspectionFailure reason,
            Exception exception) =>
        Failed(reason, exception.Message);

    private static InspectionEnvelope<MemberMetricsInspectionOutcome>
        Failed(
            MemberMetricsInspectionFailure reason,
            string message) =>
        Envelope(
            new MemberMetricsInspectionOutcome.Failed(
                reason,
                message),
            [
                new(
                    "member-metrics.failed",
                    InspectionDiagnosticSeverity.Error,
                    message),
            ]);

    private static InspectionEnvelope<MemberMetricsInspectionOutcome>
        Envelope(
            MemberMetricsInspectionOutcome outcome,
            IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            diagnostics);

    private abstract record SnapshotOutcome
    {
        private SnapshotOutcome()
        {
        }

        internal sealed record Ready(
            ImmutableArray<byte> Content)
            : SnapshotOutcome;

        internal sealed record IdentityReady : SnapshotOutcome;

        internal sealed record Incomplete(
            long Limit,
            long Measured)
            : SnapshotOutcome;

        internal sealed record Rejected(
            MemberMetricsInspectionRejection Reason)
            : SnapshotOutcome;

        internal sealed record Failed(
            MemberMetricsInspectionFailure Reason,
            string Message)
            : SnapshotOutcome;
    }

    private sealed record SnapshotRequest(
        long MaximumAssemblyBytes,
        MemberOverloadPopulationBinding Binding);

    private sealed record PopulationValidation(
        ImmutableArray<MemberOverloadShape> Rows,
        IReadOnlySet<int> MethodTokens,
        MemberMetricsInspectionRejection? Rejection)
    {
        internal static PopulationValidation Accepted(
            ImmutableArray<MemberOverloadShape> rows,
            IReadOnlySet<int> methodTokens) =>
            new(rows, methodTokens, null);

        internal static PopulationValidation Rejected(
            MemberMetricsInspectionRejection rejection) =>
            new([], new HashSet<int>(), rejection);
    }
}
