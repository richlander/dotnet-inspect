using System.Collections.Immutable;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed record TypeHierarchyRelationCorrespondenceEvidence(
    AssemblyReferenceIdentity Assembly,
    MetadataTypeDefinitionName Type,
    ResolvedTypeDefinition? Definition);

public sealed record WorkspaceTypeHierarchyRelationSource(
    AssemblyAcquisitionRegistration Registration,
    AssemblyReferenceIdentity Assembly,
    AssemblyResolutionProvenance Provenance,
    ExactLibrarySourceCoordinate? Coordinate);

public sealed record WorkspaceTypeHierarchyRelationExecutionPlan
{
    private WorkspaceTypeHierarchyRelationExecutionPlan(
        bool materializeRows,
        int startOrdinal,
        int maximumRows)
    {
        MaterializeRows = materializeRows;
        StartOrdinal = startOrdinal;
        MaximumRows = maximumRows;
    }

    public bool MaterializeRows { get; }

    public int StartOrdinal { get; }

    public int MaximumRows { get; }

    public static WorkspaceTypeHierarchyRelationExecutionPlan Exhaustive(
        bool materializeRows = true,
        int startOrdinal = 0,
        int maximumRows = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);
        return new(
            materializeRows,
            startOrdinal,
            maximumRows);
    }

    public static WorkspaceTypeHierarchyRelationExecutionPlan RowsSegment(
        int startOrdinal,
        int maximumRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);
        return new(
            materializeRows: true,
            startOrdinal,
            maximumRows);
    }
}

public sealed record WorkspaceTypeHierarchyRelationsResult(
    StructuralSubjectIdentity Focus,
    SubjectRelationPopulationAuthority Population,
    SubjectRelationPopulationEvidence Evidence,
    WorkspaceTypeHierarchyRelationExecutionPlan ExecutionPlan,
    int CandidateCount,
    bool CandidateCountIsComplete,
    ImmutableArray<SubjectRelationRow> Rows,
    ImmutableArray<WorkspaceTypeHierarchyRelationSource> Sources);

/// <summary>Canonical ordering for Workspace Type hierarchy candidates.</summary>
public static class WorkspaceTypeHierarchyRelationOrdering
{
    public static int FormRank(SubjectRelationForm form) =>
        form switch
        {
            SubjectRelationForm.Interface => 0,
            SubjectRelationForm.BaseType => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

    public static string CandidateName(InspectionGraphSubject candidate)
    {
        var typeSubject =
            candidate as InspectionGraphSubject.TypeSubject
            ?? throw new ArgumentException(
                "A hierarchy candidate must be a Type subject.",
                nameof(candidate));
        var identity =
            typeSubject.Identity
                as InspectionGraphTypeIdentity.AcquiredDefinition
            ?? throw new ArgumentException(
                "A hierarchy candidate must identify an acquired Type "
                    + "definition.",
                nameof(candidate));
        return MetadataTypeNameFormatter.FormatFullName(identity.Type);
    }
}

/// <summary>
/// Resolves incoming hierarchy relations to one exact Type across a captured
/// Workspace declaration population.
/// </summary>
public static class WorkspaceTypeHierarchyRelationsQuery
{
    public static InspectionQuery<WorkspaceTypeHierarchyRelationsResult>
        Definition { get; } =
        new(
            "Workspace Type hierarchy relations",
            InspectionCost.Unbounded);

    public static WorkspaceTypeHierarchyRelationsResult Execute(
        InspectionWorkspace workspace,
        WorkspaceDeclarationPopulation population,
        WorkspaceExactTypeFocusOutcome.Found focusSelection,
        bool includeNonPublic = false,
        SubjectRelationForm? form = null,
        WorkspaceTypeHierarchyRelationExecutionPlan? executionPlan = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(focusSelection);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(
                workspace.Identity,
                population.Receipt.Workspace))
        {
            throw new ArgumentException(
                "The hierarchy population must belong to the exact Workspace.",
                nameof(population));
        }
        executionPlan ??=
            WorkspaceTypeHierarchyRelationExecutionPlan.Exhaustive();
        var workspaceSubject =
            StructuralSubjectIdentity.ForWorkspace(workspace.Identity);
        StructuralSubjectIdentity focus;
        if (focusSelection.DefinitionOccurrence is { } focusOccurrence)
        {
            if (!population.TryGetAccess(
                    focusOccurrence,
                    out WorkspaceDeclarationMember? focusMember,
                    out _,
                    out ResolvedAssemblyReference? focusAssembly)
                || focusMember is null
                || focusAssembly is null
                || !focusAssembly.Identity.IsEquivalentTo(
                    focusSelection.Assembly))
            {
                throw new ArgumentException(
                    "The acquired hierarchy focus must be an available "
                        + "member of the captured population.",
                    nameof(focusSelection));
            }
            var focusLibrary =
                StructuralSubjectIdentity.ForContextLibrary(
                    workspaceSubject,
                    new NavigationAssemblyIdentity(
                        focusAssembly.Registration,
                        focusAssembly.Identity,
                        focusAssembly.Provenance),
                    focusMember.Coordinate);
            focus = StructuralSubjectIdentity.ForContextType(
                focusLibrary,
                focusSelection.Type);
        }
        else
        {
            focus = StructuralSubjectIdentity.ForReferencedType(
                workspaceSubject,
                focusSelection.Assembly,
                focusSelection.Type);
        }
        SubjectRelationPopulationAuthority populationAuthority =
            population.RelationAuthority;

        (
            WorkspaceDeclarationMember Member,
            AssemblyContextGroup Group,
            ResolvedAssemblyReference Assembly)[] accesses =
            [.. population.ReadAccesses()];
        WorkspaceDeclarationContextReceipt[] failedContexts =
        [
            .. population.Receipt.Contexts.Where(
                static context => !context.IsRealized),
        ];
        var scans = new List<ParticipantScan>();
        foreach (IGrouping<
            AssemblyContextGroup,
            (
                WorkspaceDeclarationMember Member,
                AssemblyContextGroup Group,
                ResolvedAssemblyReference Assembly)> context
            in accesses.GroupBy(
                static item => item.Group))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ParticipantScan scan in ScanContext(
                    context.Key,
                    context,
                    focusSelection,
                    includeNonPublic,
                    form,
                    cancellationToken))
            {
                scans.Add(scan);
            }
        }

        SubjectRelationProducerOutcome metadata =
            AggregateMetadata(scans, failedContexts);
        SubjectRelationProducerOutcome correspondence =
            AggregateCorrespondence(scans);
        var evidence = new SubjectRelationPopulationEvidence(
            populationAuthority,
            [metadata, correspondence]);
        IGrouping<
            CandidateIdentity,
            ResolvedMatch>[] candidateGroups =
            !executionPlan.MaterializeRows
                ? []
                :
                [
                    .. scans
                        .SelectMany(scan => scan.Matches)
                        .GroupBy(static match =>
                            new CandidateIdentity(
                                MetadataRelationGraphCatalog.Form(
                                    match.Occurrence.Relationship),
                                match.Occurrence.SourceSubject))
                        .OrderBy(
                            static group =>
                                WorkspaceTypeHierarchyRelationOrdering
                                    .FormRank(group.Key.Form))
                        .ThenBy(
                            static group =>
                                WorkspaceTypeHierarchyRelationOrdering
                                    .CandidateName(group.Key.Source),
                            StringComparer.Ordinal),
                ];
        int candidateCount =
            executionPlan.MaterializeRows
                ? candidateGroups.Length
                : scans
                    .SelectMany(scan => scan.Matches)
                    .Select(static match =>
                        new CandidateIdentity(
                            MetadataRelationGraphCatalog.Form(
                                match.Occurrence.Relationship),
                            match.Occurrence.SourceSubject))
                    .Distinct()
                    .Count();
        IGrouping<
            CandidateIdentity,
            ResolvedMatch>[] selectedCandidateGroups =
            !executionPlan.MaterializeRows
                ? []
                :
                [
                    .. candidateGroups
                        .Skip(executionPlan.StartOrdinal)
                        .Take(executionPlan.MaximumRows),
                ];
        ImmutableArray<SubjectRelationRow> rows =
            !executionPlan.MaterializeRows
                ? []
                :
        [
            .. selectedCandidateGroups
                .SelectMany(candidateGroup =>
                    candidateGroup
                        .GroupBy(static match =>
                            new RelationCandidateIdentity(
                                match.Occurrence.Relationship,
                                match.Occurrence.SourceSubject,
                                match.Occurrence.TargetSubject))
                        .Select(group =>
                        {
                            ResolvedMatch match = group.First();
                            RelationCandidateIdentity identity = group.Key;
                            SubjectRelationFocusCorrespondence
                                focusCorrespondence =
                                    SubjectRelationFocusCorrespondence.Create(
                                        focus,
                                        populationAuthority,
                                        match.Occurrence.TargetSubject,
                                        InspectionGraphEndpointRole.Target,
                                        match.Correspondence);
                            return new SubjectRelationRow(
                                MetadataRelationGraphCatalog.Form(
                                    identity.Relationship),
                                SubjectRelationEvidenceKind.Declaration,
                                identity.Relationship,
                                identity.Source,
                                identity.Target,
                                focusCorrespondence,
                                group.Select(static candidate =>
                                    candidate.Occurrence));
                        })),
        ];
        return new(
            focus,
            populationAuthority,
            evidence,
            executionPlan,
            candidateCount,
            CandidateCountIsComplete: true,
            rows,
            [
                .. population.ReadAccesses()
                    .Select(static item =>
                        new WorkspaceTypeHierarchyRelationSource(
                            item.Assembly.Registration,
                            item.Assembly.Identity,
                            item.Assembly.Provenance,
                            item.Member.Coordinate))
                    .GroupBy(static source => source.Registration)
                    .Select(static group => group.First()),
            ]);
    }

    private static IEnumerable<ParticipantScan> ScanContext(
        AssemblyContextGroup group,
        IEnumerable<(
            WorkspaceDeclarationMember Member,
            AssemblyContextGroup Group,
            ResolvedAssemblyReference Assembly)> members,
        WorkspaceExactTypeFocusOutcome.Found focus,
        bool includeNonPublic,
        SubjectRelationForm? form,
        CancellationToken cancellationToken)
    {
        var selected = members
            .Select(item => (
                item.Member,
                Participant: group.Participants.Single(participant =>
                    ReferenceEquals(
                        participant.Assembly.Registration,
                        item.Assembly.Registration))))
            .ToArray();
        List<(
            AssemblyContextParticipant Participant,
            ResolvedAssemblyReference Assembly)>? retained = null;
        CandidateOpenFailure? retentionFailure = null;
        IAcquisitionFreeAssemblyBindingPolicy? policy =
            null;
        bool bindingAttempted = false;
        foreach ((WorkspaceDeclarationMember member,
            AssemblyContextParticipant participant) in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextEntry<MetadataRelationInspectionOutcome> entry =
                AssemblyContextQueryExecutor.ExecuteParticipant(
                    group,
                    participant,
                    session => session.Relations(
                        new(
                            [MetadataRelationFamily.Hierarchy],
                            MetadataOperationPolicy.Unbounded,
                            includeNonPublic: false,
                            hierarchyTarget:
                                new(
                                    focus.Type,
                                    form switch
                                    {
                                        SubjectRelationForm.Interface =>
                                            MetadataHierarchyRelationKind
                                                .Interface,
                                        SubjectRelationForm.BaseType =>
                                            MetadataHierarchyRelationKind
                                                .BaseType,
                                        _ => null,
                                    }))
                        {
                            IncludeHidden = includeNonPublic,
                        },
                        cancellationToken));
            if (entry is not AssemblyContextEntry<
                    MetadataRelationInspectionOutcome>.Available
                {
                    Value:
                        MetadataRelationInspectionOutcome.Available available,
                })
            {
                yield return ParticipantScan.CreateUnavailable(
                    member,
                    entry,
                    retentionFailure);
                continue;
            }

            MetadataRelationGraphProjection projection =
                MetadataRelationGraphAdapter.Project(
                    participant.Assembly,
                    available.Result);
            InspectionGraphOccurrence[] occurrences =
            [
                .. projection.Occurrences,
            ];
            if (focus.DefinitionOccurrence is null)
            {
                var referencedMatches =
                    ImmutableArray.CreateBuilder<ResolvedMatch>();
                int referencedExamined = 0;
                int referencedUnavailable = 0;
                foreach (InspectionGraphOccurrence occurrence
                    in occurrences)
                {
                    var hierarchy =
                        (MetadataHierarchyGraphEvidence)occurrence.Evidence;
                    if (!WorkspaceExactTypeFocusQuery.TryGetExactTarget(
                            participant.Assembly,
                            hierarchy.Evidence.Target,
                            out AssemblyReferenceIdentity? targetAssembly,
                            out MetadataTypeDefinitionName? targetType)
                        || targetAssembly is null
                        || targetType is null)
                    {
                        referencedUnavailable++;
                        continue;
                    }

                    referencedExamined++;
                    if (targetType == focus.Type
                        && targetAssembly.IsEquivalentTo(focus.Assembly))
                    {
                        referencedMatches.Add(
                            new(
                                occurrence,
                                new(
                                    targetAssembly,
                                    targetType,
                                    Definition: null)));
                    }
                }
                yield return new(
                    member,
                    projection,
                    referencedMatches.ToImmutable(),
                    occurrences.Length,
                    referencedExamined,
                    referencedUnavailable,
                    RetentionFailure: null,
                    EntryFailure: null);
                continue;
            }
            var candidates = new List<ResolutionCandidate>();
            var matches = ImmutableArray.CreateBuilder<ResolvedMatch>();
            int examined = 0;
            int unavailable = 0;
            foreach (InspectionGraphOccurrence occurrence in occurrences)
            {
                MetadataHierarchyGraphEvidence hierarchy =
                    (MetadataHierarchyGraphEvidence)occurrence.Evidence;
                MetadataNamedTypeIdentity? named = NamedDefinition(
                    hierarchy.Evidence.Target);
                if (named is null)
                {
                    unavailable++;
                }
                else if (!TryGetDefinitionName(
                    named,
                    out MetadataTypeDefinitionName? targetType)
                    || targetType is null)
                {
                    unavailable++;
                }
                else if (targetType != focus.Type)
                {
                    examined++;
                }
                else if (named.Scope.Kind
                    == MetadataTypeScopeKind.CurrentModule)
                {
                    examined++;
                    if (participant.Assembly.Identity
                        .IsEquivalentTo(focus.Assembly))
                    {
                        matches.Add(
                            new(
                                occurrence,
                                new(
                                    focus.Assembly,
                                    focus.Type,
                                    Definition: null)));
                    }
                }
                else if (TryCreateResolutionRequest(
                    participant.Assembly,
                    named,
                    targetType,
                    out TypeResolutionRequest? request)
                    && request is not null)
                {
                    candidates.Add(new(
                        occurrence,
                        request));
                }
                else
                {
                    unavailable++;
                }
            }

            if (candidates.Count == 0)
            {
                yield return new(
                    member,
                    projection,
                    matches.ToImmutable(),
                    occurrences.Length,
                    examined,
                    unavailable,
                    RetentionFailure: null,
                    EntryFailure: null);
                continue;
            }

            EnsureBinding();
            if (policy is null)
            {
                yield return new(
                    member,
                    projection,
                    matches.ToImmutable(),
                    occurrences.Length,
                    examined,
                    checked(unavailable + candidates.Count),
                    retentionFailure,
                    EntryFailure: null);
                continue;
            }

            using (TypeResolutionContext resolution =
                TypeResolutionContext.Create(
                    policy,
                    retained!.Select(static item => item.Assembly),
                    candidates.Select(static candidate =>
                        candidate.Request)))
            {
                foreach (ResolutionCandidate candidate in candidates)
                {
                    TypeResolutionOutcome outcome =
                        resolution.Resolve(candidate.Request);
                    if (outcome is not TypeResolutionOutcome.Resolved resolved)
                    {
                        unavailable++;
                        continue;
                    }
                    examined++;
                    if (resolved.Definition.Type.Equals(focus.Type)
                        && resolved.Definition.Assembly.Assembly.Identity
                            .IsEquivalentTo(focus.Assembly))
                    {
                        matches.Add(
                            new(
                                candidate.Occurrence,
                                new(
                                    focus.Assembly,
                                    focus.Type,
                                    resolved.Definition)));
                    }
                }
            }

            yield return new(
                member,
                projection,
                matches.ToImmutable(),
                occurrences.Length,
                examined,
                unavailable,
                RetentionFailure: null,
                EntryFailure: null);
        }

        void EnsureBinding()
        {
            if (bindingAttempted)
                return;
            bindingAttempted = true;
            retained = [];
            foreach (AssemblyContextParticipant participant
                in group.Participants)
            {
                AssemblyImageAccessResult<ResolvedAssemblyReference> access =
                    group.RetainAssemblyReference(participant.Assembly);
                if (access is AssemblyImageAccessResult<
                        ResolvedAssemblyReference>.Available available)
                {
                    retained.Add((participant, available.Value));
                }
                else if (access is AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Rejected rejected)
                {
                    retentionFailure ??= rejected.Failure;
                }
            }

            if (retentionFailure is null)
            {
                policy =
                    SourceRelativeAssemblyGroupBindingPolicy
                        .CreateClosedWorld(
                            retained.Select(item => (
                                item.Assembly,
                                (IAcquisitionFreeAssemblyBindingPolicy)
                                    item.Participant.BindingPolicy)));
            }
        }
    }

    private static bool TryCreateResolutionRequest(
        ResolvedAssemblyReference source,
        MetadataNamedTypeIdentity named,
        MetadataTypeDefinitionName type,
        out TypeResolutionRequest? request)
    {
        request = named.Scope.Kind switch
        {
            MetadataTypeScopeKind.CurrentModule =>
                TypeResolutionRequest.FromAssembly(
                    source,
                    AssemblyResolutionScope.Any,
                    type),
            MetadataTypeScopeKind.AssemblyReference
                when named.Scope.Assembly is { } assembly =>
                TypeResolutionRequest.FromReference(
                    new AssemblyReferenceIdentity(
                        assembly.Name.ToString(),
                        assembly.Version,
                        EmptyToNull(assembly.Culture),
                        EmptyToNull(assembly.PublicKeyToken)),
                    AssemblyBindingOrigin.FromAssembly(source),
                    AssemblyResolutionScope.Any,
                    type),
            MetadataTypeScopeKind.ModuleReference
                when named.Scope.ModuleName is { } module =>
                TypeResolutionRequest.FromModule(
                    source,
                    module.ToString(),
                    type),
            _ => null,
        };
        return request is not null;
    }

    private static bool TryGetDefinitionName(
        MetadataNamedTypeIdentity named,
        out MetadataTypeDefinitionName? type)
    {
        if (MetadataTypeDefinitionName.Create(
                named.Namespace.ToString(),
                [.. named.Segments.Select(static segment =>
                    segment.ToString())])
            is MetadataTypeDefinitionNameResult.Valid valid)
        {
            type = valid.Name;
            return true;
        }

        type = null;
        return false;
    }

    private static MetadataNamedTypeIdentity? NamedDefinition(
        MetadataTypeIdentity target) =>
        target switch
        {
            MetadataTypeIdentity.Named value => value.Definition,
            MetadataTypeIdentity.GenericInstance value => value.Definition,
            _ => null,
        };

    private static string? EmptyToNull(InertText.InertString? value) =>
        value is null || value.Value.IsEmpty
            ? null
            : value.Value.ToString();

    private static SubjectRelationProducerOutcome AggregateMetadata(
        IEnumerable<ParticipantScan> scans,
        IEnumerable<WorkspaceDeclarationContextReceipt> failedContexts)
    {
        WorkspaceDeclarationContextReceipt[] contextFailures =
            [.. failedContexts];
        SubjectRelationProducerOutcome[] outcomes =
        [
            .. scans
                .Where(static scan => scan.Projection is not null)
                .SelectMany(static scan => scan.Projection!.Producers),
        ];
        SubjectRelationCoverage coverage = SumCoverage(
            outcomes.Select(static outcome => outcome.Coverage)
                .Concat(
                    scans.Where(static scan => scan.Projection is null)
                        .Select(static _ =>
                            new SubjectRelationCoverage(1, 0, 0, 1, 0)))
                .Concat(
                    contextFailures.Select(static _ =>
                        new SubjectRelationCoverage(1, 0, 0, 1, 0))));
        return new(
            MetadataRelationGraphAdapter.HierarchyQuery,
            AggregateDisposition(
                outcomes.Select(static outcome => outcome.Disposition)
                    .Concat(
                        scans.Where(static scan => scan.Projection is null)
                            .Select(static _ =>
                                SubjectRelationProducerDisposition
                                    .Unavailable))
                    .Concat(
                        contextFailures.Select(static _ =>
                            SubjectRelationProducerDisposition
                                .Unavailable))),
            coverage,
            [
                MetadataRelationGraphCatalog.BaseType,
                MetadataRelationGraphCatalog.Interface,
            ],
            outcomes.SelectMany(static outcome => outcome.Diagnostics)
                .Concat(
                    scans.Where(static scan =>
                            scan.EntryFailure is not null)
                        .Select(scan =>
                            SubjectRelationProducerDiagnostic.Create(
                                SubjectRelationProducerDiagnosticKind
                                    .Failure,
                                scan.EntryFailure!)))
                .Concat(
                    contextFailures.Select(context =>
                        SubjectRelationProducerDiagnostic.Create(
                            SubjectRelationProducerDiagnosticKind.Failure,
                            context))));
    }

    private static SubjectRelationProducerOutcome AggregateCorrespondence(
        IEnumerable<ParticipantScan> scans)
    {
        int considered = scans.Sum(static scan => scan.Considered);
        int examined = scans.Sum(static scan => scan.Examined);
        int unavailable = scans.Sum(static scan => scan.Unavailable);
        return new(
            Definition,
            unavailable == 0
                ? SubjectRelationProducerDisposition.Complete
                : examined > 0
                    ? SubjectRelationProducerDisposition.Partial
                    : SubjectRelationProducerDisposition.Unavailable,
            new(
                considered,
                examined,
                excluded: 0,
                unavailable,
                limited: 0),
            [
                MetadataRelationGraphCatalog.BaseType,
                MetadataRelationGraphCatalog.Interface,
            ],
            scans.Where(static scan =>
                    scan.RetentionFailure is not null)
                .Select(scan =>
                    SubjectRelationProducerDiagnostic.Create(
                        SubjectRelationProducerDiagnosticKind.Failure,
                        scan.RetentionFailure!)));
    }

    private static SubjectRelationCoverage SumCoverage(
        IEnumerable<SubjectRelationCoverage> coverages)
    {
        int considered = 0;
        int examined = 0;
        int excluded = 0;
        int unavailable = 0;
        int limited = 0;
        foreach (SubjectRelationCoverage coverage in coverages)
        {
            considered = checked(considered + coverage.Considered);
            examined = checked(examined + coverage.Examined);
            excluded = checked(excluded + coverage.Excluded);
            unavailable = checked(unavailable + coverage.Unavailable);
            limited = checked(limited + coverage.Limited);
        }
        return new(
            considered,
            examined,
            excluded,
            unavailable,
            limited);
    }

    private static SubjectRelationProducerDisposition AggregateDisposition(
        IEnumerable<SubjectRelationProducerDisposition> dispositions)
    {
        SubjectRelationProducerDisposition[] values = [.. dispositions];
        if (values.Contains(SubjectRelationProducerDisposition.Failed))
            return SubjectRelationProducerDisposition.Failed;
        if (values.Contains(SubjectRelationProducerDisposition.Unavailable))
            return values.Any(value =>
                    value is SubjectRelationProducerDisposition.Complete
                        or SubjectRelationProducerDisposition.Stopped
                        or SubjectRelationProducerDisposition.Partial)
                ? SubjectRelationProducerDisposition.Partial
                : SubjectRelationProducerDisposition.Unavailable;
        if (values.Contains(SubjectRelationProducerDisposition.Partial))
            return SubjectRelationProducerDisposition.Partial;
        return SubjectRelationProducerDisposition.Complete;
    }

    private sealed record ResolutionCandidate(
        InspectionGraphOccurrence Occurrence,
        TypeResolutionRequest Request);

    private sealed record ResolvedMatch(
        InspectionGraphOccurrence Occurrence,
        TypeHierarchyRelationCorrespondenceEvidence Correspondence);

    private sealed record RelationCandidateIdentity(
        InspectionGraphRelationshipDescriptor Relationship,
        InspectionGraphSubject Source,
        InspectionGraphSubject Target);

    private sealed record CandidateIdentity(
        SubjectRelationForm Form,
        InspectionGraphSubject Source);

    private sealed record ParticipantScan(
        WorkspaceDeclarationMember Member,
        MetadataRelationGraphProjection? Projection,
        ImmutableArray<ResolvedMatch> Matches,
        int Considered,
        int Examined,
        int Unavailable,
        CandidateOpenFailure? RetentionFailure,
        object? EntryFailure)
    {
        internal static ParticipantScan CreateUnavailable(
            WorkspaceDeclarationMember member,
            object entryFailure,
            CandidateOpenFailure? retentionFailure) =>
            new(
                member,
                Projection: null,
                [],
                Considered: 1,
                Examined: 0,
                Unavailable: 1,
                retentionFailure,
                entryFailure);

    }
}
