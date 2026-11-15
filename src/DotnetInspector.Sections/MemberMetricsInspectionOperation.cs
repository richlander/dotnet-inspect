using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Libraries;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public static class MemberMetricsInspectionOperation
{
    private const string SharePath = "member-metrics-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<MemberMetricsInspectionContent> Execute(
        MemberMetricsInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(request.Library, lease.Reference))
            {
                return Rejected(
                    MemberMetricsInspectionRejection
                        .LeaseReferenceMismatch,
                    "The operation lease belongs to another Library.");
            }
            if (!ReferenceEquals(
                    request.Population.Generation,
                    lease.Reference.ApiAssembly.Generation))
            {
                return Rejected(
                    MemberMetricsInspectionRejection
                        .PopulationGenerationMismatch,
                    "The population receipt belongs to another Library "
                        + "generation.");
            }

            MemberMetricsQueryRequestResult query =
                MemberMetricsQuery.ResolveRequest(
                    request.Query,
                    cancellationToken);
            if (query
                is MemberMetricsQueryRequestResult.Rejected rejected)
            {
                return Rejected(
                    MemberMetricsInspectionRejection.QueryRejected,
                    rejected.Detail);
            }
            var accepted =
                (MemberMetricsQueryRequestResult.Accepted)query;
            if (accepted.RequestedEvidence
                is MemberMetricEvidenceKind.None)
            {
                return Rejected(
                    MemberMetricsInspectionRejection.NoEffectiveEvidence,
                    "The resolved request has no effective metric evidence.");
            }
            MemberMetricEvidenceKind unauthorized =
                accepted.RequestedEvidence
                & ~request.AuthorizedEvidence;
            if (unauthorized is not MemberMetricEvidenceKind.None)
            {
                return Rejected(
                    MemberMetricsInspectionRejection.UnauthorizedEvidence,
                    $"The request did not authorize '{unauthorized}'.");
            }
            if (!TryValidatePopulation(
                    request.Population,
                    out MemberOverloadRowsOutcome.Read? populationRows,
                    out MemberMetricsInspectionRejection populationFailure,
                    out string populationDetail))
            {
                return Rejected(
                    populationFailure,
                    populationDetail);
            }

            ExecutionResult result =
                lease.Snapshot(
                    request.Library.ApiAssembly,
                    (request, accepted, populationRows),
                    static (view, state, token) =>
                        Execute(
                            view,
                            state.request,
                            state.accepted,
                            state.populationRows,
                            token),
                    cancellationToken);
            return Envelope(result.Content, result.Diagnostics);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static ExecutionResult Execute(
        scoped LibraryContentView view,
        MemberMetricsInspectionRequest request,
        MemberMetricsQueryRequestResult.Accepted query,
        MemberOverloadRowsOutcome.Read populationRows,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
        {
            return FailedResult(
                MemberMetricsInspectionFailure.MalformedMetadata,
                "The Library API assembly is empty.");
        }
        if (view.Content.Length != request.Population.AssemblyBytes)
        {
            return RejectedResult(
                MemberMetricsInspectionRejection.AssemblyLengthMismatch,
                "The current assembly length differs from the population "
                    + "receipt.");
        }

        return view.UseReadStream(
            content => Execute(
                content,
                request,
                query,
                populationRows,
                cancellationToken));
    }

    private static ExecutionResult Execute(
        Stream content,
        MemberMetricsInspectionRequest request,
        MemberMetricsQueryRequestResult.Accepted query,
        MemberOverloadRowsOutcome.Read populationRows,
        CancellationToken cancellationToken)
    {
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.OpenPrefetched(content);
            if (!session.HasMetadata)
            {
                return FailedResult(
                    MemberMetricsInspectionFailure.NotManagedAssembly,
                    "The selected Library content has no managed metadata.");
            }
            if (!session.IsAssembly)
            {
                return FailedResult(
                    MemberMetricsInspectionFailure.ManagedModule,
                    "The selected Library content is a managed module, not "
                        + "an assembly.");
            }

            MemberOverloadPopulationBinding binding =
                request.Population.Overloads.Binding;
            if (!session.AssemblyIdentity().IsEquivalentTo(
                    AssemblyIdentity(binding)))
            {
                return RejectedResult(
                    MemberMetricsInspectionRejection
                        .AssemblyIdentityMismatch,
                    "The current assembly identity differs from the "
                        + "population receipt.");
            }
            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
            {
                return FailedResult(
                    MemberMetricsInspectionFailure.EmptyModuleVersionId,
                    "The current assembly has an empty module version "
                        + "identifier.");
            }
            if (moduleVersionId != binding.ModuleVersionId)
            {
                return RejectedResult(
                    MemberMetricsInspectionRejection
                        .ModuleVersionMismatch,
                    "The current module version differs from the population "
                        + "receipt.");
            }

            ExactMethodSemanticProducts products =
                Products(query.RequestedEvidence);
            ImmutableArray<MemberOverloadShape> analysisMembers =
                AnalysisMembers(
                    populationRows.Items,
                    query);
            ExactMethodSemanticAnalysisResult analysis =
                analysisMembers.IsEmpty
                    ? EmptyAnalysisResult()
                    : ExactMethodSemanticAnalysis.Execute(
                        session,
                        $"Member Metrics: "
                            + $"{binding.DeclaringType}.{binding.Name}",
                        analysisMembers.Select(
                            static row => row.MetadataToken),
                        products,
                        new(
                            includeGeneratedExecutionBodies: true,
                            request.GeneratedExpansionLimits,
                            request.TerminalWorkLimits),
                        cancellationToken);
            return Complete(
                request,
                query,
                analysisMembers,
                analysis);
        }
        catch (UnsupportedMetadataFormatException ex)
        {
            return FailedResult(
                MemberMetricsInspectionFailure.MalformedMetadata,
                ex.Message);
        }
        catch (Exception ex)
            when (ex is BadImageFormatException
                or MalformedMetadataRootException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return FailedResult(
                MemberMetricsInspectionFailure.MalformedMetadata,
                ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return FailedResult(
                MemberMetricsInspectionFailure.AnalysisFailed,
                ex.Message);
        }
    }

    private static ExecutionResult Complete(
        MemberMetricsInspectionRequest request,
        MemberMetricsQueryRequestResult.Accepted query,
        ImmutableArray<MemberOverloadShape> members,
        ExactMethodSemanticAnalysisResult analysis)
    {
        var memberByToken =
            members.ToDictionary(
                static member => member.MetadataToken);
        var originsByPhysical =
            analysis.SourceGroupReceipt
                .PhysicalCoverage
                .GeneratedExpansion
                .Origins
                .GroupBy(origin =>
                    MetadataTokens.GetToken(origin.Method))
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First());
        var generatedByOwner =
            originsByPhysical.Values
                .GroupBy(origin =>
                    MetadataTokens.GetToken(origin.DeclaredOwner))
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .Select(origin =>
                            MetadataTokens.GetToken(origin.Method))
                        .Distinct()
                        .Order()
                        .ToImmutableArray());

        ExactMethodSemanticLaneFailure? bodySizeFailure =
            analysis.Failures.FirstOrDefault(
                static failure =>
                    failure.Product
                        is ExactMethodSemanticProducts.BodySize);
        ExactMethodSemanticLaneFailure? siblingCallsFailure =
            analysis.Failures.FirstOrDefault(
                static failure =>
                    failure.Product
                        is ExactMethodSemanticProducts.SiblingCalls);
        IReadOnlyDictionary<int, ExactMethodBodySize> bodySizes =
            analysis.BodySize is { } bodySizeLane
                ? bodySizeLane.Result.Bodies.ToDictionary(
                    static body => body.MethodToken)
                : ImmutableDictionary<int, ExactMethodBodySize>.Empty;
        IReadOnlyDictionary<int, ExactMethodSiblingCallBody> siblingCalls =
            analysis.SiblingCalls is { } siblingCallsLane
                ? siblingCallsLane.Result.Bodies.ToDictionary(
                    static body => body.MethodToken)
                : ImmutableDictionary<
                    int,
                    ExactMethodSiblingCallBody>.Empty;

        bool sourceIncomplete =
            analysis.SourceGroupReceipt.PhysicalCoverage
                .GeneratedExpansion.IsBounded
            || analysis.SourceGroupReceipt.PhysicalCoverage
                .TerminalWork.ReachedLimit is not null;
        bool siblingIncomingIncomplete =
            sourceIncomplete
            || siblingCalls.Values.Any(
                static body => !body.Diagnostics.IsEmpty);
        ImmutableArray<MemberSiblingCallRelationship> relationships =
            BuildRelationships(
                siblingCalls.Values,
                originsByPhysical,
                memberByToken);
        var incomingByToken =
            relationships
                .GroupBy(static relationship =>
                    relationship.Callee.MetadataToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToImmutableArray());
        var outgoingByToken =
            relationships
                .GroupBy(static relationship =>
                    relationship.Caller.MetadataToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToImmutableArray());

        bool projectsBody =
            query.ProjectedFields.Contains(
                MemberMetricField.LargestPhysicalIlBytes);
        bool projectsIncoming =
            query.ProjectedFields.Contains(
                MemberMetricField.IncomingSiblingCallers);
        bool projectsOutgoing =
            query.ProjectedFields.Contains(
                MemberMetricField.OutgoingSiblingTargets);
        bool projectsRelationships =
            query.ProjectedFields.Contains(
                MemberMetricField.SiblingRelationships);
        var rows =
            ImmutableArray.CreateBuilder<
                MemberMetricsEvaluationRow>(members.Length);
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();

        foreach (ExactMethodSemanticLaneFailure failure
            in analysis.Failures)
        {
            diagnostics.Add(
                new(
                    "member-metrics.analysis-lane-failed",
                    InspectionDiagnosticSeverity.Error,
                    failure.Message,
                    failure.Product.ToString()));
        }
        AddSourceDiagnostics(analysis, diagnostics);

        foreach (MemberOverloadShape member in members)
        {
            ImmutableArray<int> physicalTokens =
                PhysicalTokens(
                    member.MetadataToken,
                    generatedByOwner);
            BodySizeEvaluation bodySize =
                EvaluateBodySize(
                    physicalTokens,
                    member.MetadataToken,
                    originsByPhysical,
                    bodySizes,
                    bodySizeFailure,
                    sourceIncomplete);
            ImmutableArray<MemberSiblingCallRelationship> incoming =
                incomingByToken.GetValueOrDefault(
                    member.MetadataToken,
                    []);
            ImmutableArray<MemberSiblingCallRelationship> outgoing =
                outgoingByToken.GetValueOrDefault(
                    member.MetadataToken,
                    []);
            SiblingCallEvaluation siblings =
                EvaluateSiblingCalls(
                    physicalTokens,
                    siblingCalls,
                    siblingCallsFailure,
                    siblingIncomingIncomplete,
                    incoming,
                    outgoing);

            rows.Add(
                new(
                    member,
                    query.RequestedEvidence.HasFlag(
                        MemberMetricEvidenceKind.BodySize)
                            ? bodySize.State
                            : MemberMetricEvidenceState.NotRequested,
                    query.RequestedEvidence.HasFlag(
                        MemberMetricEvidenceKind.SiblingCalls)
                            ? siblings.State
                            : MemberMetricEvidenceState.NotRequested,
                    bodySize.LargestPhysicalIlBytes,
                    siblings.IncomingCount,
                    siblings.OutgoingCount,
                    projectsBody
                        ? new(
                            bodySize.LargestPhysicalIlBytes,
                            bodySize.PhysicalBodies)
                        : null,
                    projectsIncoming || projectsOutgoing
                        ? new(
                            projectsIncoming
                                ? siblings.IncomingCount
                                : null,
                            projectsOutgoing
                                ? siblings.OutgoingCount
                                : null)
                        : null,
                    projectsRelationships
                        && siblings.State
                            is MemberMetricEvidenceState.Available
                                or MemberMetricEvidenceState.Incomplete
                            ? new(incoming, outgoing)
                        : null));
        }

        RowSelectionResult<MemberMetricsEvaluationRow> selection =
            RowQueryExecutor.Apply(rows, query.Rows);
        if (!selection.IsSuccess)
        {
            return RejectedResult(
                MemberMetricsInspectionRejection.QueryRejected,
                $"The metric row selection failed: "
                    + $"stage {selection.Failure!.StageNumber} required "
                    + $"position {selection.Failure.RequiredPosition}, but "
                    + $"only {selection.Failure.AvailableCount} rows were "
                    + "available.");
        }

        MemberMetricsPopulationResult result =
            query.Terminal is QuerySpaceTerminalRequirement.Rows
                ? new MemberMetricsPopulationResult.Rows(
                    [
                        .. selection.Values.Select(Project),
                    ])
                : new MemberMetricsPopulationResult.Count(
                    selection.Values.Count);
        bool incomplete =
            sourceIncomplete
            || !analysis.Failures.IsEmpty
            || rows.Any(row =>
                IsIncomplete(row.BodySizeState)
                || IsIncomplete(row.SiblingCallsState));
        var completed =
            new MemberMetricsInspectionContent.Completed(
                new(
                    request.Population.Subject,
                    request.Population.Overloads.Binding,
                    new(
                        request.AuthorizedEvidence,
                        query.RequestedEvidence,
                        query.RequestedEvidence,
                        query.ProjectedFields),
                    new(
                        analysis.SourceGroupReceipt,
                        analysis.BodySize?.WorkReceipt,
                        analysis.SiblingCalls?.WorkReceipt,
                        analysis.Failures),
                    result,
                    incomplete
                        ? MemberMetricsCompletion.Incomplete
                        : MemberMetricsCompletion.Complete));
        return new(completed, diagnostics.ToImmutable());
    }

    private static ImmutableArray<MemberOverloadShape> AnalysisMembers(
        ImmutableArray<MemberOverloadShape> members,
        MemberMetricsQueryRequestResult.Accepted query)
    {
        if (query.RequestedEvidence.HasFlag(
                MemberMetricEvidenceKind.SiblingCalls)
            || query.SourcePredicates.PredicateKeyIdentities.Count == 0)
        {
            return members;
        }

        ImmutableArray<MemberOverloadShape> narrowed =
        [
            .. members.Where(
                member =>
                    RowQueryExecutor.Matches(
                        SourcePredicateRow(member),
                        query.SourcePredicates)),
        ];
        return narrowed;
    }

    private static ExactMethodSemanticAnalysisResult EmptyAnalysisResult() =>
        new(
            Allocation: null,
            Safety: null,
            Calls: null,
            MethodDefinitionSourceGroupReceipt.CreateEmpty());

    private static MemberMetricsEvaluationRow SourcePredicateRow(
        MemberOverloadShape member) =>
        new(
            member,
            MemberMetricEvidenceState.NotRequested,
            MemberMetricEvidenceState.NotRequested,
            null,
            null,
            null,
            null,
            null,
            null);

    private static MemberMetricsRow Project(
        MemberMetricsEvaluationRow row) =>
        new(
            row.Member,
            row.BodySizeState,
            row.SiblingCallsState,
            row.BodySize,
            row.SiblingCalls,
            row.Relationships);

    private static BodySizeEvaluation EvaluateBodySize(
        ImmutableArray<int> physicalTokens,
        int declaredToken,
        IReadOnlyDictionary<
            int,
            MethodDefinitionGeneratedExpansionOrigin> origins,
        IReadOnlyDictionary<int, ExactMethodBodySize> facts,
        ExactMethodSemanticLaneFailure? laneFailure,
        bool sourceIncomplete)
    {
        if (laneFailure is not null)
        {
            return new(
                MemberMetricEvidenceState.Failed,
                null,
                []);
        }

        var physical =
            ImmutableArray.CreateBuilder<
                MemberPhysicalBodySizeEvidence>(
                    physicalTokens.Length);
        int? largest = null;
        bool hasUnavailable = false;
        bool hasManaged = false;
        foreach (int token in physicalTokens)
        {
            MemberPhysicalBodyRole role =
                PhysicalRole(token, origins);
            bool isAsync =
                origins.TryGetValue(token, out var origin)
                && origin.IsAsyncStateMachine;
            if (!facts.TryGetValue(
                    token,
                    out ExactMethodBodySize? fact))
            {
                hasUnavailable = true;
                physical.Add(
                    new(
                        token,
                        declaredToken,
                        role,
                        isAsync,
                        HasManagedBody: true,
                        EncodedIlBytes: null,
                        MemberMetricEvidenceState.Unavailable,
                        "The physical MethodDef produced no body-size "
                            + "result."));
                continue;
            }

            hasManaged |= fact.HasManagedBody;
            MemberMetricEvidenceState state;
            string? detail = fact.Diagnostic?.Message;
            if (!fact.HasManagedBody)
            {
                state = MemberMetricEvidenceState.Bodyless;
            }
            else if (fact.EncodedIlBytes is { } bytes
                && fact.Diagnostic is null)
            {
                state = MemberMetricEvidenceState.Available;
                largest = largest is null
                    ? bytes
                    : Math.Max(largest.Value, bytes);
            }
            else
            {
                state = MemberMetricEvidenceState.Unavailable;
                hasUnavailable = true;
            }
            physical.Add(
                new(
                    token,
                    declaredToken,
                    role,
                    isAsync,
                    fact.HasManagedBody,
                    fact.EncodedIlBytes,
                    state,
                    detail));
        }

        MemberMetricEvidenceState rowState =
            sourceIncomplete
                ? MemberMetricEvidenceState.Incomplete
                : hasUnavailable
                    ? largest is null
                        ? MemberMetricEvidenceState.Unavailable
                        : MemberMetricEvidenceState.Incomplete
                    : hasManaged
                        ? MemberMetricEvidenceState.Available
                        : MemberMetricEvidenceState.Bodyless;
        return new(rowState, largest, physical.ToImmutable());
    }

    private static SiblingCallEvaluation EvaluateSiblingCalls(
        ImmutableArray<int> physicalTokens,
        IReadOnlyDictionary<int, ExactMethodSiblingCallBody> facts,
        ExactMethodSemanticLaneFailure? laneFailure,
        bool incomingIncomplete,
        ImmutableArray<MemberSiblingCallRelationship> incoming,
        ImmutableArray<MemberSiblingCallRelationship> outgoing)
    {
        if (laneFailure is not null)
        {
            return new(
                MemberMetricEvidenceState.Failed,
                null,
                null);
        }

        bool hasEvidence = false;
        bool hasUnavailable = false;
        bool hasPartial = false;
        foreach (int token in physicalTokens)
        {
            if (!facts.TryGetValue(
                    token,
                    out ExactMethodSiblingCallBody? fact))
            {
                hasUnavailable = true;
                continue;
            }
            hasEvidence = true;
            hasPartial |= !fact.Diagnostics.IsEmpty;
        }

        MemberMetricEvidenceState state =
            incomingIncomplete || hasPartial
                ? MemberMetricEvidenceState.Incomplete
                : hasUnavailable
                    ? hasEvidence
                        ? MemberMetricEvidenceState.Incomplete
                        : MemberMetricEvidenceState.Unavailable
                    : MemberMetricEvidenceState.Available;
        return new(
            state,
            state is MemberMetricEvidenceState.Unavailable
                ? null
                : incoming.Length,
            state is MemberMetricEvidenceState.Unavailable
                ? null
                : outgoing.Length);
    }

    private static ImmutableArray<MemberSiblingCallRelationship>
        BuildRelationships(
            IEnumerable<ExactMethodSiblingCallBody> bodies,
            IReadOnlyDictionary<
                int,
                MethodDefinitionGeneratedExpansionOrigin> origins,
            IReadOnlyDictionary<int, MemberOverloadShape> members)
    {
        var calls =
            new Dictionary<
                RelationshipKey,
                RelationshipAccumulator>();
        foreach (ExactMethodSiblingCallBody body in bodies)
        {
            int callerToken =
                origins.TryGetValue(
                    body.MethodToken,
                    out MethodDefinitionGeneratedExpansionOrigin? origin)
                    ? MetadataTokens.GetToken(origin.DeclaredOwner)
                    : body.MethodToken;
            if (!members.ContainsKey(callerToken))
                continue;

            foreach (ExactMethodSiblingCallSite call in body.Calls)
            {
                if (!TryCurrentModuleTargetToken(
                        call,
                        out int calleeToken)
                    || !members.ContainsKey(calleeToken))
                {
                    continue;
                }

                var key = new RelationshipKey(
                    callerToken,
                    calleeToken);
                if (!calls.TryGetValue(
                        key,
                        out RelationshipAccumulator? accumulator))
                {
                    accumulator = new();
                    calls.Add(key, accumulator);
                }
                accumulator.InvocationCount++;
                accumulator.PhysicalCallerTokens.Add(
                    body.MethodToken);
            }
        }

        return
        [
            .. calls
                .OrderBy(pair =>
                    members[pair.Key.CallerToken]
                        .BaselineOrdinal)
                .ThenBy(pair =>
                    members[pair.Key.CalleeToken]
                        .BaselineOrdinal)
                .Select(pair =>
                    new MemberSiblingCallRelationship(
                        Identity(
                            members[pair.Key.CallerToken]),
                        Identity(
                            members[pair.Key.CalleeToken]),
                        pair.Value.InvocationCount,
                        [
                            .. pair.Value
                                .PhysicalCallerTokens
                                .Order(),
                        ])),
        ];
    }

    private static bool TryCurrentModuleTargetToken(
        ExactMethodSiblingCallSite call,
        out int targetToken)
    {
        if (call.Target
            is MethodCallTarget.CurrentModule currentModule)
        {
            targetToken =
                MetadataTokens.GetToken(currentModule.Method);
            return true;
        }

        EntityHandle operand =
            MetadataTokens.EntityHandle(call.OperandToken);
        EntityHandle definition =
            MetadataTokens.EntityHandle(call.DefinitionToken);
        if (operand.Kind == HandleKind.MethodSpecification
            && definition.Kind == HandleKind.MethodDefinition)
        {
            targetToken = call.DefinitionToken;
            return true;
        }

        targetToken = 0;
        return false;
    }

    private static MemberMetricRowIdentity Identity(
        MemberOverloadShape member) =>
        new(
            member.MetadataToken,
            member.Anchor,
            member.BaselineOrdinal);

    private static ImmutableArray<int> PhysicalTokens(
        int declaredToken,
        IReadOnlyDictionary<int, ImmutableArray<int>> generatedByOwner) =>
        generatedByOwner.TryGetValue(
            declaredToken,
            out ImmutableArray<int> generated)
            ? [declaredToken, .. generated]
            : [declaredToken];

    private static MemberPhysicalBodyRole PhysicalRole(
        int token,
        IReadOnlyDictionary<
            int,
            MethodDefinitionGeneratedExpansionOrigin> origins) =>
        origins.TryGetValue(
            token,
            out MethodDefinitionGeneratedExpansionOrigin? origin)
            ? origin.Kind switch
            {
                MethodDefinitionGeneratedExpansionOriginKind
                        .StateMachineExecutionBody =>
                    MemberPhysicalBodyRole.StateMachineExecutionBody,
                MethodDefinitionGeneratedExpansionOriginKind
                        .LiftedExecutionBody =>
                    MemberPhysicalBodyRole.LiftedExecutionBody,
                _ => throw new InvalidOperationException(
                    "Unknown generated Method body role."),
            }
            : MemberPhysicalBodyRole.Declared;

    private static void AddSourceDiagnostics(
        ExactMethodSemanticAnalysisResult analysis,
        ImmutableArray<InspectionDiagnostic>.Builder diagnostics)
    {
        MethodDefinitionSourceCoverage coverage =
            analysis.SourceGroupReceipt.PhysicalCoverage;
        if (coverage.GeneratedExpansion.ReachedLimit is { } expansion)
        {
            diagnostics.Add(
                new(
                    "member-metrics.generated-expansion-incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    $"Generated-body expansion reached '{expansion}' "
                        + $"at 0x{coverage.GeneratedExpansion.ReachedAtToken
                            ?? 0:x8}."));
        }
        if (coverage.TerminalWork.ReachedLimit is { } terminal)
        {
            diagnostics.Add(
                new(
                    "member-metrics.terminal-work-incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    $"Terminal Method work reached '{terminal}' "
                        + $"at 0x{coverage.TerminalWork
                            .ReachedAtMethodToken ?? 0:x8}."));
        }
    }

    private static bool TryValidatePopulation(
        MemberOverloadPopulationContent population,
        [NotNullWhen(true)] out MemberOverloadRowsOutcome.Read? rows,
        out MemberMetricsInspectionRejection rejection,
        out string detail)
    {
        rows = population.Overloads.Rows
            as MemberOverloadRowsOutcome.Read;
        if (rows is null)
        {
            rejection =
                MemberMetricsInspectionRejection.PopulationRowsRequired;
            detail =
                "Member Metrics requires an owner-issued Overloads Rows "
                + "population.";
            return false;
        }
        if (!rows.IsComplete
            || rows.Items.Length == 0
            || rows.Items[0].BaselineOrdinal != 1)
        {
            rejection =
                MemberMetricsInspectionRejection.PopulationIncomplete;
            detail =
                "Member Metrics requires the complete baseline Overloads "
                + "population.";
            return false;
        }

        MemberOverloadPopulationBinding binding =
            population.Overloads.Binding;
        bool subjectMatches =
            population.Subject.DeclaringType == binding.DeclaringType
            && string.Equals(
                population.Subject.Name,
                binding.Name,
                StringComparison.Ordinal)
            && population.Subject.Category == binding.Category
            && population.Subject.Role == binding.Role
            && population.Subject.Spelling == binding.Spelling;
        if (!subjectMatches
            || population.Assembly != binding.Assembly)
        {
            rejection =
                MemberMetricsInspectionRejection
                    .PopulationSubjectMismatch;
            detail =
                "The population subject and binding do not correspond.";
            return false;
        }

        var tokens = new HashSet<int>();
        for (int index = 0; index < rows.Items.Length; index++)
        {
            MemberOverloadShape row = rows.Items[index];
            EntityHandle handle =
                MetadataTokens.EntityHandle(row.MetadataToken);
            if (row.BaselineOrdinal != index + 1
                || row.Binding != binding
                || handle.Kind is not HandleKind.MethodDefinition
                || !tokens.Add(row.MetadataToken))
            {
                rejection =
                    MemberMetricsInspectionRejection.InvalidPopulation;
                detail =
                    "The Overloads Rows receipt contains an invalid exact "
                    + "Member identity or baseline order.";
                return false;
            }
        }
        if (population.Overloads.Count
                is MemberOverloadCountOutcome.Counted counted
            && counted.Value != rows.Items.Length)
        {
            rejection =
                MemberMetricsInspectionRejection.InvalidPopulation;
            detail =
                "The Overloads Count and complete Rows population disagree.";
            return false;
        }

        rejection = default;
        detail = "";
        return true;
    }

    private static AssemblyReferenceIdentity AssemblyIdentity(
        MemberOverloadPopulationBinding binding) =>
        new(
            binding.Assembly.Name.ToString(),
            binding.Assembly.Version,
            binding.Assembly.Culture?.ToString(),
            binding.Assembly.PublicKeyToken?.ToString());

    private static ExactMethodSemanticProducts Products(
        MemberMetricEvidenceKind evidence)
    {
        ExactMethodSemanticProducts products =
            ExactMethodSemanticProducts.None;
        if (evidence.HasFlag(MemberMetricEvidenceKind.BodySize))
            products |= ExactMethodSemanticProducts.BodySize;
        if (evidence.HasFlag(MemberMetricEvidenceKind.SiblingCalls))
            products |= ExactMethodSemanticProducts.SiblingCalls;
        return products;
    }

    private static bool IsIncomplete(
        MemberMetricEvidenceState state) =>
        state is MemberMetricEvidenceState.Unavailable
            or MemberMetricEvidenceState.Incomplete
            or MemberMetricEvidenceState.Failed;

    private static InspectionEnvelope<MemberMetricsInspectionContent>
        Rejected(
            MemberMetricsInspectionRejection reason,
            string detail) =>
        Envelope(
            new MemberMetricsInspectionContent.Rejected(
                reason,
                detail),
            [
                new(
                    "member-metrics.rejected",
                    InspectionDiagnosticSeverity.Error,
                    detail,
                    reason.ToString()),
            ]);

    private static ExecutionResult RejectedResult(
        MemberMetricsInspectionRejection reason,
        string detail) =>
        new(
            new MemberMetricsInspectionContent.Rejected(
                reason,
                detail),
            [
                new(
                    "member-metrics.rejected",
                    InspectionDiagnosticSeverity.Error,
                    detail,
                    reason.ToString()),
            ]);

    private static ExecutionResult FailedResult(
        MemberMetricsInspectionFailure reason,
        string detail) =>
        new(
            new MemberMetricsInspectionContent.Failed(
                reason,
                detail),
            [
                new(
                    "member-metrics.failed",
                    InspectionDiagnosticSeverity.Error,
                    detail,
                    reason.ToString()),
            ]);

    private static InspectionEnvelope<MemberMetricsInspectionContent>
        Envelope(
            MemberMetricsInspectionContent content,
            IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            content,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            diagnostics);

    private sealed record ExecutionResult(
        MemberMetricsInspectionContent Content,
        ImmutableArray<InspectionDiagnostic> Diagnostics);

    private sealed record BodySizeEvaluation(
        MemberMetricEvidenceState State,
        int? LargestPhysicalIlBytes,
        ImmutableArray<MemberPhysicalBodySizeEvidence> PhysicalBodies);

    private sealed record SiblingCallEvaluation(
        MemberMetricEvidenceState State,
        int? IncomingCount,
        int? OutgoingCount);

    private readonly record struct RelationshipKey(
        int CallerToken,
        int CalleeToken);

    private sealed class RelationshipAccumulator
    {
        public int InvocationCount { get; set; }

        public HashSet<int> PhysicalCallerTokens { get; } = [];
    }
}
