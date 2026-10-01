using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.LibraryMetadata;
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
}

public sealed record WorkspaceExactTypeFocusMemberOutcome(
    WorkspaceDeclarationMember Member,
    bool IsComplete);

/// <summary>
/// Locates one exact Type definition or hierarchy target for a captured
/// Workspace population without projecting an API surface.
/// </summary>
public static class WorkspaceExactTypeFocusQuery
{
    public static WorkspaceExactTypeFocusOutcome ExecuteFromDefinitionNames(
        AssemblyReferenceIdentity assembly,
        IEnumerable<MetadataTypeDefinitionName> definitions,
        string type,
        ExactTypeSelectionKind selectionKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(definitions);
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

        List<DefinitionFocusCandidate> candidates =
        [
            .. definitions.Select(static definition =>
                new DefinitionFocusCandidate(
                    Occurrence: null,
                    definition,
                    definition.ToEscapedFullName(),
                    AssemblyTypeDeclarationKind.Definition)),
        ];
        List<DefinitionFocusCandidate> selected =
            Select(candidates, type, selectionKind);
        return selected switch
        {
            [var match] => new WorkspaceExactTypeFocusOutcome.Found(
                assembly,
                DefinitionOccurrence: null,
                match.Type),
            [] => new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The requested Library does not declare the selected exact "
                    + "Type.",
                []),
            _ => new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus is ambiguous in the requested "
                    + "Library.",
                []),
        };
    }

    public static WorkspaceExactTypeFocusOutcome ExecuteFromDefinitionSurface(
        AssemblyReferenceIdentity assembly,
        AssemblyTypeDeclarationInventoryOutcome declarations,
        string type,
        ExactTypeSelectionKind selectionKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(declarations);
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
        if (declarations
            is not AssemblyTypeDeclarationInventoryOutcome.Read read)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The requested Library declaration surface is unavailable.",
                []);
        }

        return ExecuteFromDefinitionNames(
            assembly,
            read.Inventory
                .GetDeclarations(includeAll: true)
                .Where(static declaration =>
                    declaration.Kind
                        == AssemblyTypeDeclarationKind.Definition)
                .Select(static declaration => declaration.Name),
            type,
            selectionKind,
            cancellationToken);
    }

    public static WorkspaceExactTypeFocusOutcome ExecuteFromAssemblySurface(
        WorkspaceDeclarationPopulation population,
        string type,
        ExactTypeSelectionKind selectionKind,
        string assemblyName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
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

        WorkspaceDeclarationMember[] members =
        [
            .. population.Receipt.Members.Where(member =>
                member.AssemblyIdentity.Name.Equals(
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (members.Length != 1)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                members.Length == 0
                    ? "The requested Library is not present in the "
                        + "candidate context."
                    : "The requested Library is ambiguous in the "
                        + "candidate context.",
                []);
        }

        WorkspaceDeclarationMember member = members[0];
        WorkspaceDeclarationInventoryOutcome outcome =
            population.ReadDeclarations(
                member.Occurrence,
                cancellationToken);
        if (!TryGetInventory(
                outcome,
                out AssemblyTypeDeclarationInventory? inventory))
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The requested Library declaration surface is unavailable.",
                [new(member, IsComplete: false)]);
        }

        var candidates = new List<DefinitionFocusCandidate>();
        foreach (AssemblyTypeDeclaration declaration
            in inventory.GetDeclarations(includeAll: true))
        {
            if (declaration.Kind
                is not (AssemblyTypeDeclarationKind.Definition
                    or AssemblyTypeDeclarationKind.Forwarder))
            {
                continue;
            }
            candidates.Add(new(
                member.Occurrence,
                declaration.Name,
                declaration.Name.ToEscapedFullName(),
                declaration.Kind));
        }

        List<DefinitionFocusCandidate> selected =
            Select(candidates, type, selectionKind);
        if (selected.Count == 0)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The requested Library does not declare or forward the "
                    + "selected exact Type.",
                [new(member, IsComplete: true)]);
        }
        if (selected.Count > 1)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus is ambiguous in the requested "
                    + "Library.",
                [new(member, IsComplete: true)]);
        }

        DefinitionFocusCandidate match = selected[0];
        if (match.Kind == AssemblyTypeDeclarationKind.Definition)
        {
            return new WorkspaceExactTypeFocusOutcome.Found(
                member.AssemblyIdentity,
                member.Occurrence,
                match.Type);
        }
        if (population.TryGetAccess(
                member.Occurrence,
                out _,
                out AssemblyContextGroup? group,
                out ResolvedAssemblyReference? assembly)
            && group is not null
            && assembly is not null)
        {
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
                    AssemblyResolutionScope.Any);
            if (resolution
                is not AssemblyContextTypeResolutionResult.Available
                {
                    Outcome: TypeResolutionOutcome.Resolved resolved,
                })
            {
                return new WorkspaceExactTypeFocusOutcome.Unavailable(
                    "The requested Library forwarding chain could not "
                        + "establish the selected exact Type.",
                    [new(member, IsComplete: false)]);
            }

            ResolvedAssemblyReference definitionAssembly =
                resolved.Definition.Assembly.Assembly;
            return new WorkspaceExactTypeFocusOutcome.Found(
                definitionAssembly.Identity,
                population.FindOccurrence(definitionAssembly.Registration),
                resolved.Definition.Type);
        }

        WorkspaceDeclarationMember[] borrowedMembers =
        [
            .. population.ReadLibraryAccesses().Select(
                static access => access.Member),
        ];
        var resolver = new WorkspaceBorrowedLibraryTypeResolver(
            population,
            borrowedMembers);
        WorkspaceBorrowedLibraryTypeResolution borrowedResolution =
            resolver.Resolve(
                member.AssemblyIdentity,
                match.Type,
                cancellationToken);
        if (borrowedResolution
            is WorkspaceBorrowedLibraryTypeResolution.Resolved
            {
                Assembly: var terminal,
                DefinitionOccurrence: var definitionOccurrence,
            })
        {
            return new WorkspaceExactTypeFocusOutcome.Found(
                terminal,
                definitionOccurrence,
                match.Type);
        }

        if (borrowedMembers.Any(candidate =>
                ReferenceEquals(candidate.Occurrence, member.Occurrence)))
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The requested Library forwarding chain could not "
                    + "establish the selected exact Type.",
                [new(member, IsComplete: false)]);
        }

        return new WorkspaceExactTypeFocusOutcome.Unavailable(
            "The requested Library forwarding chain is unavailable.",
            [new(member, IsComplete: false)]);
    }

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
            if (!TryGetInventory(
                    outcome,
                    out AssemblyTypeDeclarationInventory? inventory))
            {
                outcomes.Add(new(member, IsComplete: false));
                continue;
            }

            outcomes.Add(new(member, IsComplete: true));
            foreach (AssemblyTypeDeclaration declaration
                in inventory.GetDeclarations(includeAll))
            {
                if (declaration.Kind
                        != AssemblyTypeDeclarationKind.Definition)
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
        if (selected.Count == 1)
        {
            var match = selected[0];
            WorkspaceDeclarationMember member =
                population.Receipt.Members.Single(candidate =>
                    ReferenceEquals(
                        candidate.Occurrence,
                        match.Occurrence));
            return new WorkspaceExactTypeFocusOutcome.Found(
                member.AssemblyIdentity,
                match.Occurrence,
                match.Type);
        }
        if (selected.Count > 1)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus is ambiguous in the candidate "
                    + "context.",
                outcomes.ToImmutable());
        }
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

    private static bool TryGetInventory(
        WorkspaceDeclarationInventoryOutcome outcome,
        [NotNullWhen(true)]
        out AssemblyTypeDeclarationInventory? inventory)
    {
        inventory = outcome switch
        {
            WorkspaceDeclarationInventoryOutcome.Inspected
            {
                Outcome:
                    AssemblyTypeDeclarationInventoryOutcome.Read read,
            } => read.Inventory,
            WorkspaceDeclarationInventoryOutcome.LibraryInspected
            {
                Outcome:
                    LibraryTypeDeclarationInventoryInspectionOutcome.Completed
                        completed,
            } => completed.Correspondence.Inventory,
            _ => null,
        };
        return inventory is not null;
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
            if (!population.TryGetAccess(
                    member.Occurrence,
                    out _,
                    out AssemblyContextGroup? group,
                    out ResolvedAssemblyReference? assembly)
                || group is null
                || assembly is null)
            {
                return null;
            }

            AssemblyContextParticipant participant =
                group.Participants.Single(candidate =>
                    ReferenceEquals(
                        candidate.Assembly.Registration,
                        assembly.Registration));
            AssemblyContextEntry<MetadataTypeDefinitionNameSearchResult>
                entry =
                    AssemblyContextQueryExecutor.ExecuteParticipant(
                        group,
                        participant,
                        session =>
                            session.FindTypeDefinitionsBySimpleName(type));
            if (entry is not AssemblyContextEntry<
                    MetadataTypeDefinitionNameSearchResult>.Available
                {
                    Value:
                        MetadataTypeDefinitionNameSearchResult.Found found,
                })
            {
                outcomes.Add(new(member, IsComplete: false));
                continue;
            }

            outcomes.Add(new(member, IsComplete: true));
            matches.AddRange(
                found.Names.Select(name =>
                    new DefinitionFocusCandidate(
                        member.Occurrence,
                        name,
                        name.ToEscapedFullName(),
                        AssemblyTypeDeclarationKind.Definition)));
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
        if (selected.Count == 1)
        {
            DefinitionFocusCandidate match = selected[0];
            WorkspaceDeclarationMember member =
                population.Receipt.Members.Single(candidate =>
                    ReferenceEquals(
                        candidate.Occurrence,
                        match.Occurrence));
            return new WorkspaceExactTypeFocusOutcome.Found(
                member.AssemblyIdentity,
                match.Occurrence,
                match.Type);
        }
        if (selected.Count > 1)
        {
            return new WorkspaceExactTypeFocusOutcome.Unavailable(
                "The exact Type focus is ambiguous in the candidate "
                    + "context.",
                outcomes.ToImmutable());
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
                            includeNonPublic: false)
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
        out MetadataTypeDefinitionName? type) =>
        TryGetExactTarget(
            source.Identity,
            target,
            out assembly,
            out type);

    internal static bool TryGetExactTarget(
        AssemblyReferenceIdentity source,
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
            MetadataTypeScopeKind.CurrentModule => source,
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
        WorkspaceDeclarationOccurrence? Occurrence,
        MetadataTypeDefinitionName Type,
        string FullName,
        AssemblyTypeDeclarationKind Kind)
        : IFocusCandidate;

    private sealed record ReferencedFocusCandidate(
        AssemblyReferenceIdentity Assembly,
        MetadataTypeDefinitionName Type,
        string FullName)
        : IFocusCandidate;

    private sealed record ReferencedFocusScan(
        bool IsComplete,
        ImmutableArray<ReferencedFocusCandidate> Candidates);
}
