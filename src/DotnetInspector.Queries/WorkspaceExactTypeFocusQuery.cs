using System.Collections.Immutable;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public abstract record WorkspaceExactTypeFocusOutcome
{
    private protected WorkspaceExactTypeFocusOutcome()
    {
    }

    public sealed record Found(
        AssemblyReferenceIdentity Assembly,
        WorkspaceDeclarationOccurrence? DefinitionOccurrence,
        MetadataTypeDefinitionName Type)
        : WorkspaceExactTypeFocusOutcome;

    public sealed record Unavailable(
        string Detail,
        ImmutableArray<WorkspaceExactTypeFocusMemberOutcome> Members)
        : WorkspaceExactTypeFocusOutcome;

    public sealed record PlatformAssemblyRequired(
        AssemblyReferenceIdentity Assembly,
        ImmutableArray<WorkspaceExactTypeFocusMemberOutcome> Members)
        : WorkspaceExactTypeFocusOutcome;
}

public sealed record WorkspaceExactTypeFocusMemberOutcome(
    WorkspaceDeclarationMember Member,
    bool IsComplete);

internal abstract record WorkspaceAcquiredTypeResolutionOutcome
{
    private protected WorkspaceAcquiredTypeResolutionOutcome()
    {
    }

    internal sealed record Resolved(
        ResolvedTypeDefinition Definition)
        : WorkspaceAcquiredTypeResolutionOutcome;

    internal sealed record PlatformAssemblyRequired(
        AssemblyReferenceIdentity Assembly)
        : WorkspaceAcquiredTypeResolutionOutcome;

    internal sealed record Unavailable
        : WorkspaceAcquiredTypeResolutionOutcome;
}

/// <summary>
/// Locates one exact Type definition or hierarchy target for a captured
/// Workspace population without projecting an API surface.
/// </summary>
public static class WorkspaceExactTypeFocusQuery
{
    public static WorkspaceExactTypeFocusOutcome Execute(
        WorkspaceDeclarationPopulation population,
        string type,
        ExactTypeSelectionKind selectionKind =
            ExactTypeSelectionKind.Query,
        string? assemblyName = null,
        ExactLibrarySourceCoordinate? library = null,
        bool includeAll = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (!Enum.IsDefined(selectionKind))
            throw new ArgumentOutOfRangeException(nameof(selectionKind));
        if (selectionKind == ExactTypeSelectionKind.Query
            && TypeMatcher.IsTypeGlobPattern(type))
        {
            throw new ArgumentException(
                "Exact Type focus does not accept a Type glob.",
                nameof(type));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (IsSimpleAsciiMetadataName(type)
            && TrySelectSimpleName(
                population,
                type,
                selectionKind,
                assemblyName,
                library,
                cancellationToken)
                is { } simpleSelection)
        {
            return simpleSelection;
        }

        var matches = new List<DefinitionFocusCandidate>();
        var outcomes =
            ImmutableArray.CreateBuilder<
                WorkspaceExactTypeFocusMemberOutcome>(
                    population.Receipt.Members.Length);
        foreach (WorkspaceDeclarationMember member
            in population.Receipt.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assemblyName is not null
                && !member.AssemblyIdentity.Name.Equals(
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (library is not null
                && member.Coordinate != library)
            {
                continue;
            }
            WorkspaceDeclarationInventoryOutcome outcome =
                population.ReadDeclarations(
                    member.Occurrence,
                    cancellationToken);
            if (outcome
                is not WorkspaceDeclarationInventoryOutcome.Inspected
                {
                    Outcome:
                        AssemblyTypeDeclarationInventoryOutcome.Read read,
                })
            {
                outcomes.Add(new(member, IsComplete: false));
                continue;
            }

            outcomes.Add(new(member, IsComplete: true));
            foreach (AssemblyTypeDeclaration declaration
                in read.Inventory.GetDeclarations(includeAll: true))
            {
                if (declaration.Kind is not (
                    AssemblyTypeDeclarationKind.Definition
                    or AssemblyTypeDeclarationKind.Forwarder))
                    continue;
                matches.Add(new(
                    member.Occurrence,
                    declaration.Name,
                    declaration.Name.ToEscapedFullName(),
                    declaration.Kind));
            }
        }

        if (library is not null && outcomes.Count == 0)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact focused Library is not present in the "
                    + "candidate context.",
                []);
        }

        if ((library is null
                && !population.Receipt.IsRealizationComplete)
            || outcomes.Any(static outcome => !outcome.IsComplete))
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus could not be established because "
                    + "the candidate context is incomplete.",
                outcomes.ToImmutable());
        }

        List<DefinitionFocusCandidate> selected = Select(
            matches,
            type,
            selectionKind);
        if (selected is
            [
            {
                Kind: AssemblyTypeDeclarationKind.Definition,
            } directMatch,
            ])
        {
            WorkspaceDeclarationMember member =
                population.Receipt.Members.Single(candidate =>
                    ReferenceEquals(
                        candidate.Occurrence,
                        directMatch.Occurrence));
            return new WorkspaceExactTypeFocusOutcome.Found(
                member.AssemblyIdentity,
                directMatch.Occurrence,
                directMatch.Type);
        }
        if (selected.Count > 0)
            return ResolveSelected(population, selected, outcomes);
        if (library is not null)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus could not be resolved in the "
                    + "candidate context.",
                outcomes.ToImmutable());
        }

        ReferencedFocusScan referenced = ScanReferencedHierarchyTargets(
            population,
            assemblyName,
            includeAll,
            cancellationToken);
        if (!referenced.IsComplete)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus could not be established because "
                    + "the candidate hierarchy evidence is incomplete.",
                outcomes.ToImmutable());
        }
        List<ReferencedFocusCandidate> referencedSelection = Select(
            referenced.Candidates,
            type,
            selectionKind);
        return referencedSelection switch
        {
            [var match] => new WorkspaceExactTypeFocusOutcome.Found(
                match.Assembly,
                DefinitionOccurrence: null,
                match.Type),
            [] => new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus could not be resolved in the "
                    + "candidate context.",
                outcomes.ToImmutable()),
            _ => new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus is ambiguous in the candidate "
                    + "context.",
                outcomes.ToImmutable()),
        };
    }

    private static WorkspaceExactTypeFocusOutcome?
        TrySelectSimpleName(
            WorkspaceDeclarationPopulation population,
            string type,
            ExactTypeSelectionKind selectionKind,
            string? assemblyName,
            ExactLibrarySourceCoordinate? library,
            CancellationToken cancellationToken)
    {
        var matches = new List<DefinitionFocusCandidate>();
        var outcomes =
            ImmutableArray.CreateBuilder<
                WorkspaceExactTypeFocusMemberOutcome>();
        foreach (WorkspaceDeclarationMember member
            in population.Receipt.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assemblyName is not null
                && !member.AssemblyIdentity.Name.Equals(
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (library is not null && member.Coordinate != library)
                continue;
            WorkspaceDeclarationNameSearchOutcome outcome =
                population.FindDeclarationsBySimpleName(
                    member.Occurrence,
                    type,
                    cancellationToken);
            if (outcome
                is not WorkspaceDeclarationNameSearchOutcome.Inspected
                {
                    Outcome:
                        MetadataTypeDeclarationNameSearchResult.Found found,
                })
            {
                outcomes.Add(new(member, IsComplete: false));
                continue;
            }

            outcomes.Add(new(member, IsComplete: true));
            foreach (MetadataTypeDeclarationNameMatch declaration
                in found.Matches)
            {
                matches.Add(new(
                    member.Occurrence,
                    declaration.Name,
                    declaration.Name.ToEscapedFullName(),
                    declaration.Kind));
            }
        }

        if ((library is null
                && !population.Receipt.IsRealizationComplete)
            || outcomes.Any(static outcome => !outcome.IsComplete))
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus could not be established because "
                    + "the candidate context is incomplete.",
                outcomes.ToImmutable());
        }
        if (matches.Count == 0)
            return null;

        List<DefinitionFocusCandidate> selected =
            Select(matches, type, selectionKind);
        if (selected is
            [
            {
                Kind: AssemblyTypeDeclarationKind.Definition,
            } directMatch,
            ])
        {
            WorkspaceDeclarationMember member =
                population.Receipt.Members.Single(candidate =>
                    ReferenceEquals(
                        candidate.Occurrence,
                        directMatch.Occurrence));
            return new WorkspaceExactTypeFocusOutcome.Found(
                member.AssemblyIdentity,
                directMatch.Occurrence,
                directMatch.Type);
        }
        if (selected.Count > 0)
            return ResolveSelected(population, selected, outcomes);

        return null;
    }

    private static WorkspaceExactTypeFocusOutcome ResolveSelected(
        WorkspaceDeclarationPopulation population,
        IEnumerable<DefinitionFocusCandidate> selected,
        ImmutableArray<WorkspaceExactTypeFocusMemberOutcome>.Builder
            outcomes)
    {
        var resolved = new List<ResolvedFocusCandidate>();
        foreach (DefinitionFocusCandidate match in selected)
        {
            if (!population.TryGetAccess(
                    match.Occurrence,
                    out WorkspaceDeclarationMember? member,
                    out AssemblyContextGroup? group,
                    out ResolvedAssemblyReference? assembly)
                || member is null
                || group is null
                || assembly is null)
            {
                return Unavailable();
            }
            AssemblyContextParticipant participant =
                group.Participants.Single(candidate =>
                    ReferenceEquals(
                        candidate.Assembly.Registration,
                        assembly.Registration));
            AssemblyContextTypeResolutionResult resolution =
                AssemblyContextTypeResolutionQuery.Execute(
                    group,
                    participant,
                    match.Type,
                    member.Coordinate
                        is ExactLibrarySourceCoordinate.Platform
                            ? AssemblyResolutionScope.Platform
                            : AssemblyResolutionScope.Any);
            if (resolution
                is AssemblyContextTypeResolutionResult.Available
                {
                    Outcome: TypeResolutionOutcome.UnboundBinding unbound,
                }
                && member.Coordinate
                    is ExactLibrarySourceCoordinate.Platform
                && unbound.TerminalAssemblyIdentity is { } required)
            {
                WorkspaceAcquiredTypeResolutionOutcome acquired =
                    ResolveAcquiredPlatformType(
                        population,
                        required,
                        match.Type,
                        unbound.Hops.Length);
                if (acquired
                    is WorkspaceAcquiredTypeResolutionOutcome.Resolved
                        acquiredResolved)
                {
                    ResolvedTypeDefinition acquiredDefinition =
                        acquiredResolved.Definition;
                    AddResolved(
                        new(
                            acquiredDefinition.Assembly.Assembly.Identity,
                            acquiredDefinition.Assembly.Assembly.Registration,
                            DefinitionOccurrence(
                                population,
                                acquiredDefinition.Assembly.Assembly
                                    .Registration),
                            acquiredDefinition.Type));
                    continue;
                }
                if (acquired
                    is WorkspaceAcquiredTypeResolutionOutcome
                        .PlatformAssemblyRequired assemblyRequired)
                {
                    return new WorkspaceExactTypeFocusOutcome
                        .PlatformAssemblyRequired(
                            assemblyRequired.Assembly,
                            outcomes.ToImmutable());
                }
                else
                {
                    return Unavailable();
                }
            }
            if (resolution
                is not AssemblyContextTypeResolutionResult.Available
                {
                    Outcome: TypeResolutionOutcome.Resolved available,
                })
            {
                return Unavailable();
            }

            ResolvedTypeDefinition definition = available.Definition;
            WorkspaceDeclarationOccurrence? occurrence =
                DefinitionOccurrence(
                    population,
                    definition.Assembly.Assembly.Registration);
            AddResolved(
                new(
                    definition.Assembly.Assembly.Identity,
                    definition.Assembly.Assembly.Registration,
                    occurrence,
                    definition.Type));
        }

        return resolved switch
        {
            [var match] => new WorkspaceExactTypeFocusOutcome.Found(
                match.Assembly,
                match.Occurrence,
                match.Type),
            [] => Unavailable(),
            _ => new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus is ambiguous in the candidate "
                    + "context.",
                outcomes.ToImmutable()),
        };

        WorkspaceExactTypeFocusOutcome.Unavailable Unavailable() =>
            new(
                "The exact Type focus could not be resolved in the "
                    + "candidate context.",
                outcomes.ToImmutable());

        void AddResolved(ResolvedFocusCandidate candidate)
        {
            int existing = resolved.FindIndex(current =>
                current.Type == candidate.Type
                && ReferenceEquals(
                    current.Registration,
                    candidate.Registration));
            if (existing < 0)
                resolved.Add(candidate);
            else if (resolved[existing].Occurrence is null
                && candidate.Occurrence is not null)
                resolved[existing] = candidate;
        }
    }

    internal static WorkspaceAcquiredTypeResolutionOutcome
        ResolveAcquiredPlatformType(
            WorkspaceDeclarationPopulation population,
            AssemblyReferenceIdentity required,
            MetadataTypeDefinitionName type,
            int forwarderHops = 0,
            CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(forwarderHops);
        var visited = new HashSet<AssemblyReferenceIdentity>(
            AssemblyReferenceIdentity.EquivalentComparer);
        while (forwarderHops
            <= TypeResolutionContextOptions.DefaultMaxForwarderHops)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(required))
            {
                return new WorkspaceAcquiredTypeResolutionOutcome
                    .Unavailable();
            }

            var matches = population.ReadAccesses()
                .Where(access =>
                    access.Member.AssemblyIdentity.IsEquivalentTo(required))
                .GroupBy(
                    static access => access.Assembly.Registration,
                    ReferenceEqualityComparer.Instance)
                .Select(static group => group.First())
                .ToArray();
            if (matches.Length == 0)
            {
                return new WorkspaceAcquiredTypeResolutionOutcome
                    .PlatformAssemblyRequired(required);
            }
            if (matches.Length != 1)
            {
                return new WorkspaceAcquiredTypeResolutionOutcome
                    .Unavailable();
            }

            var match = matches[0];
            AssemblyContextParticipant participant =
                match.Group.Participants.Single(candidate =>
                    ReferenceEquals(
                        candidate.Assembly.Registration,
                        match.Assembly.Registration));
            AssemblyContextTypeResolutionResult resolution =
                AssemblyContextTypeResolutionQuery.Execute(
                    match.Group,
                    participant,
                    type,
                    match.Member.Coordinate
                        is ExactLibrarySourceCoordinate.Platform
                            ? AssemblyResolutionScope.Platform
                            : AssemblyResolutionScope.Any);
            if (resolution
                is not AssemblyContextTypeResolutionResult.Available
                    available)
            {
                return new WorkspaceAcquiredTypeResolutionOutcome
                    .Unavailable();
            }
            if (available.Outcome.Hops.Length
                > TypeResolutionContextOptions.DefaultMaxForwarderHops
                    - forwarderHops)
            {
                return new WorkspaceAcquiredTypeResolutionOutcome
                    .Unavailable();
            }
            forwarderHops = checked(
                forwarderHops + available.Outcome.Hops.Length);
            if (available.Outcome
                is TypeResolutionOutcome.Resolved resolved)
            {
                return new WorkspaceAcquiredTypeResolutionOutcome.Resolved(
                    resolved.Definition);
            }
            if (available.Outcome
                is TypeResolutionOutcome.UnboundBinding unbound
                && match.Member.Coordinate
                    is ExactLibrarySourceCoordinate.Platform
                && unbound.TerminalAssemblyIdentity is { } next)
            {
                required = next;
                continue;
            }

            return new WorkspaceAcquiredTypeResolutionOutcome.Unavailable();
        }

        return new WorkspaceAcquiredTypeResolutionOutcome.Unavailable();
    }

    private static WorkspaceDeclarationOccurrence? DefinitionOccurrence(
        WorkspaceDeclarationPopulation population,
        AssemblyAcquisitionRegistration registration)
    {
        foreach (WorkspaceDeclarationMember member
            in population.Receipt.Members)
        {
            if (population.TryGetAccess(
                    member.Occurrence,
                    out _,
                    out _,
                    out ResolvedAssemblyReference? assembly)
                && assembly is not null
                && ReferenceEquals(
                    assembly.Registration,
                    registration))
            {
                return member.Occurrence;
            }
        }
        return null;
    }

    private static bool IsSimpleAsciiMetadataName(string type) =>
        type.Length is > 0
        && type.All(static character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '_' or '`');

    private static List<TCandidate> Select<TCandidate>(
        IEnumerable<TCandidate> candidates,
        string type,
        ExactTypeSelectionKind selectionKind)
        where TCandidate : IFocusCandidate
    {
        TCandidate[] matches = [.. candidates];
        if (selectionKind == ExactTypeSelectionKind.DefinitionIdentity)
        {
            return
            [
                .. matches.Where(match =>
                    match.FullName.Equals(
                        type,
                        StringComparison.Ordinal)),
            ];
        }
        else
        {
            string[] names =
            [
                .. matches
                    .Select(static match => match.FullName)
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];
            string? exact = names.FirstOrDefault(name =>
                name.Equals(type, StringComparison.OrdinalIgnoreCase));
            string[] selectedNames =
                exact is not null
                    ? [exact]
                    :
                [
                    .. names.Where(name =>
                        TypeMatcher.MatchesExactTypeName(name, type)),
                ];
            if (selectedNames.Length == 0)
            {
                selectedNames =
                [
                    .. names.Where(name =>
                        TypeMatcher.MatchesTypeFilter(name, type)),
                ];
            }
            return
            [
                .. matches.Where(match =>
                    selectedNames.Contains(
                        match.FullName,
                        StringComparer.OrdinalIgnoreCase)),
            ];
        }
    }

    private static ReferencedFocusScan ScanReferencedHierarchyTargets(
        WorkspaceDeclarationPopulation population,
        string? assemblyName,
        bool includeAll,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ReferencedFocusCandidate>();
        foreach (var access in population.ReadAccesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextParticipant participant =
                access.Group.Participants.Single(candidate =>
                    ReferenceEquals(
                        candidate.Assembly.Registration,
                        access.Assembly.Registration));
            AssemblyContextEntry<MetadataRelationInspectionOutcome> entry =
                AssemblyContextQueryExecutor.ExecuteParticipant(
                    access.Group,
                    participant,
                    session => session.Relations(
                        new(
                            [MetadataRelationFamily.Hierarchy],
                            MetadataOperationPolicy.Unbounded,
                            includeNonPublic: includeAll)
                        {
                            IncludeHidden = includeAll,
                        },
                        cancellationToken));
            if (entry is not AssemblyContextEntry<
                    MetadataRelationInspectionOutcome>.Available
                {
                    Value:
                        MetadataRelationInspectionOutcome.Available available,
                }
                || available.Result.Hierarchy.Disposition
                    != MetadataRelationFamilyDisposition.Complete)
            {
                return new(IsComplete: false, []);
            }

            MetadataRelationGraphProjection projection =
                MetadataRelationGraphAdapter.Project(
                    access.Assembly,
                    available.Result);
            foreach (InspectionGraphOccurrence occurrence
                in projection.Occurrences)
            {
                var hierarchy =
                    (MetadataHierarchyGraphEvidence)occurrence.Evidence;
                if (!TryGetExactTarget(
                        access.Assembly,
                        hierarchy.Evidence.Target,
                        out AssemblyReferenceIdentity? assembly,
                        out MetadataTypeDefinitionName? target)
                    || assembly is null
                    || target is null
                    || assemblyName is not null
                        && !assembly.Name.Equals(
                            assemblyName,
                            StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = new ReferencedFocusCandidate(
                    assembly,
                    target,
                    target.ToEscapedFullName());
                if (!candidates.Any(existing =>
                        existing.Type == candidate.Type
                        && existing.Assembly.IsEquivalentTo(
                            candidate.Assembly)))
                {
                    candidates.Add(candidate);
                }
            }
        }
        return new(IsComplete: true, [.. candidates]);
    }

    internal static bool TryGetExactTarget(
        ResolvedAssemblyReference source,
        MetadataTypeIdentity target,
        out AssemblyReferenceIdentity? assembly,
        out MetadataTypeDefinitionName? type)
    {
        MetadataNamedTypeIdentity? named = target switch
        {
            MetadataTypeIdentity.Named value => value.Definition,
            MetadataTypeIdentity.GenericInstance value => value.Definition,
            _ => null,
        };
        if (named is null
            || MetadataTypeDefinitionName.Create(
                    named.Namespace.ToString(),
                    [.. named.Segments.Select(static segment =>
                        segment.ToString())])
                is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            assembly = null;
            type = null;
            return false;
        }

        assembly = named.Scope.Kind switch
        {
            MetadataTypeScopeKind.CurrentModule => source.Identity,
            MetadataTypeScopeKind.AssemblyReference
                when named.Scope.Assembly is { } reference =>
                new AssemblyReferenceIdentity(
                    reference.Name.ToString(),
                    reference.Version,
                    EmptyToNull(reference.Culture),
                    EmptyToNull(reference.PublicKeyToken)),
            MetadataTypeScopeKind.ModuleReference => null,
            _ => null,
        };
        type = assembly is null ? null : valid.Name;
        return assembly is not null;
    }

    private static string? EmptyToNull(InertText.InertString? value) =>
        value is null || value.Value.IsEmpty
            ? null
            : value.Value.ToString();

    private interface IFocusCandidate
    {
        string FullName { get; }
    }

    private sealed record DefinitionFocusCandidate(
        WorkspaceDeclarationOccurrence Occurrence,
        MetadataTypeDefinitionName Type,
        string FullName,
        AssemblyTypeDeclarationKind Kind)
        : IFocusCandidate;

    private sealed record ResolvedFocusCandidate(
        AssemblyReferenceIdentity Assembly,
        AssemblyAcquisitionRegistration Registration,
        WorkspaceDeclarationOccurrence? Occurrence,
        MetadataTypeDefinitionName Type);

    private sealed record ReferencedFocusCandidate(
        AssemblyReferenceIdentity Assembly,
        MetadataTypeDefinitionName Type,
        string FullName)
        : IFocusCandidate;

    private sealed record ReferencedFocusScan(
        bool IsComplete,
        ImmutableArray<ReferencedFocusCandidate> Candidates);
}
