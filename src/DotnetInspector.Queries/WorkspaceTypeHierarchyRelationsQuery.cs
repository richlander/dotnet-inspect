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

public sealed record WorkspaceTypeHierarchyRelationsResult(
    StructuralSubjectIdentity Focus,
    SubjectRelationPopulationAuthority Population,
    SubjectRelationPopulationEvidence Evidence,
    ImmutableArray<SubjectRelationRow> Rows,
    ImmutableArray<WorkspaceTypeHierarchyRelationSource> Sources);

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

        var scans = new List<ParticipantScan>();
        foreach (IGrouping<
            AssemblyContextGroup,
            (
                WorkspaceDeclarationMember Member,
                AssemblyContextGroup Group,
                ResolvedAssemblyReference Assembly)> context
            in population.ReadAccesses().GroupBy(
                static item => item.Group))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scans.AddRange(
                ScanContext(
                    context.Key,
                    context,
                    focusSelection,
                    includeNonPublic,
                    cancellationToken));
        }

        SubjectRelationProducerOutcome metadata =
            AggregateMetadata(scans);
        SubjectRelationProducerOutcome correspondence =
            AggregateCorrespondence(scans);
        var evidence = new SubjectRelationPopulationEvidence(
            populationAuthority,
            [metadata, correspondence]);
        ImmutableArray<SubjectRelationRow> rows =
        [
            .. scans
                .SelectMany(scan => scan.Matches)
                .SelectMany(match =>
                {
                    SubjectRelationFocusCorrespondence focusCorrespondence =
                        SubjectRelationFocusCorrespondence.Create(
                            focus,
                            populationAuthority,
                            match.Occurrence.TargetSubject,
                            InspectionGraphEndpointRole.Target,
                            match.Correspondence);
                    return MetadataRelationGraphAdapter.BindRows(
                        match.Projection,
                        focusCorrespondence);
                })
                .GroupBy(static row => new
                {
                    row.Relationship,
                    row.Source,
                    row.Target,
                })
                .Select(static group => group.First()),
        ];
        return new(
            focus,
            populationAuthority,
            evidence,
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
        var retained = new List<(
            AssemblyContextParticipant Participant,
            ResolvedAssemblyReference Assembly)>();
        CandidateOpenFailure? retentionFailure = null;
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

        IAcquisitionFreeAssemblyBindingPolicy? policy =
            retentionFailure is null
                ? SourceRelativeAssemblyGroupBindingPolicy
                    .CreateClosedWorld(
                        retained.Select(item => (
                            item.Assembly,
                            (IAcquisitionFreeAssemblyBindingPolicy)
                                item.Participant.BindingPolicy)))
                : null;
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
                            includeNonPublic: false)
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
                                projection,
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
            if (policy is null)
            {
                yield return ParticipantScan.Unresolved(
                    member,
                    projection,
                    occurrences.Length,
                    retentionFailure);
                continue;
            }

            var candidates = new List<ResolutionCandidate>();
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

            var matches = ImmutableArray.CreateBuilder<ResolvedMatch>();
            using (TypeResolutionContext resolution =
                TypeResolutionContext.Create(
                    policy,
                    retained.Select(static item => item.Assembly),
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
                                projection,
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
        IEnumerable<ParticipantScan> scans)
    {
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
                            new SubjectRelationCoverage(1, 0, 0, 1, 0))));
        return new(
            MetadataRelationGraphAdapter.HierarchyQuery,
            AggregateDisposition(
                outcomes.Select(static outcome => outcome.Disposition)
                    .Concat(
                        scans.Where(static scan => scan.Projection is null)
                            .Select(static _ =>
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
                                scan.EntryFailure!))));
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
        MetadataRelationGraphProjection Projection,
        InspectionGraphOccurrence Occurrence,
        TypeHierarchyRelationCorrespondenceEvidence Correspondence);

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

        internal static ParticipantScan Unresolved(
            WorkspaceDeclarationMember member,
            MetadataRelationGraphProjection projection,
            int unavailable,
            CandidateOpenFailure? retentionFailure) =>
            new(
                member,
                projection,
                [],
                unavailable,
                Examined: 0,
                unavailable,
                retentionFailure,
                EntryFailure: null);
    }
}
