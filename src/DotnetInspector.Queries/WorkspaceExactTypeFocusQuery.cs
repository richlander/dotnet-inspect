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
        WorkspaceDeclarationOccurrence Occurrence,
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
/// Locates one exact Type definition inside a captured Workspace population
/// without requiring a portable source coordinate or projecting an API surface.
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
        var matches = new List<(
            WorkspaceDeclarationOccurrence Occurrence,
            MetadataTypeDefinitionName Type,
            string FullName)>();
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
                in read.Inventory.GetDeclarations(includeAll))
            {
                if (declaration.Kind
                        != AssemblyTypeDeclarationKind.Definition)
                    continue;
                matches.Add((
                    member.Occurrence,
                    declaration.Name,
                    declaration.Name.ToEscapedFullName()));
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

        List<(
            WorkspaceDeclarationOccurrence Occurrence,
            MetadataTypeDefinitionName Type,
            string FullName)> selected;
        if (selectionKind == ExactTypeSelectionKind.DefinitionIdentity)
        {
            selected =
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
            selected =
            [
                .. matches.Where(match =>
                    selectedNames.Contains(
                        match.FullName,
                        StringComparer.OrdinalIgnoreCase)),
            ];
        }

        return selected switch
        {
            [var match] => new WorkspaceExactTypeFocusOutcome.Found(
                match.Occurrence,
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
}
