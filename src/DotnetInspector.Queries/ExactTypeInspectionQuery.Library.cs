using System.Collections.Immutable;

using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>A narrow Type selection from one bounded, owner-attested Library.</summary>
public sealed record ExactTypeDefinitionSelection(
    ExactTypeInspectionOutcome Outcome,
    string RequestedType,
    MetadataTypeDefinitionName? Definition,
    ImmutableArray<string> Suggestions,
    string? Failure)
{
    /// <summary>
    /// Availability of prefix candidates in the same admitted inventory.
    /// Null unless requested after a conclusive query miss.
    /// </summary>
    public bool? HasPrefixCandidates { get; init; }

    /// <summary>The selected TypeDef coordinate in the supplying inventory image.</summary>
    public TypeDefinitionToken? DefinitionToken { get; init; }
}

internal static partial class ExactTypeInspectionQuery
{
    internal static ExactTypeDefinitionSelection SelectLibrary(
        LibraryReference library,
        LibraryOperationLease lease,
        string type,
        ApiSurfaceScope scope,
        ApiSurfaceExtractionBounds bounds,
        ExactTypeSelectionKind selectionKind,
        bool includePrefixDiscovery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(bounds);
        if (scope is not (ApiSurfaceScope.Public
            or ApiSurfaceScope.IncludeAll
            or ApiSurfaceScope.PublicWithNonPublicTypes))
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (!Enum.IsDefined(selectionKind))
            throw new ArgumentOutOfRangeException(nameof(selectionKind));
        if (selectionKind == ExactTypeSelectionKind.Query
            && TypeMatcher.IsTypeGlobPattern(type))
            throw new ArgumentException("Exact Type lookup does not accept a Type glob.", nameof(type));

        LibraryTypeDeclarationInventoryInspectionOutcome source =
            LibraryTypeDeclarationInventoryInspection.Execute(
                new(library, new(
                    maximumAssemblyBytes: int.MaxValue,
                    maximumRetainedDeclarations: (int)Math.Min(
                        (long)bounds.MaxTypes + bounds.MaxTypeForwarders, int.MaxValue),
                    maximumMetadataRows: bounds.MaxMetadataRows,
                    maximumRetainedTextCharacters: bounds.MaxRetainedTextCharacters)),
                lease,
                cancellationToken);
        if (source is not LibraryTypeDeclarationInventoryInspectionOutcome.Completed completed)
        {
            return Unavailable(source switch
            {
                LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete incomplete =>
                    $"The exact Type inventory reached its {incomplete.Bound} bound.",
                LibraryTypeDeclarationInventoryInspectionOutcome.Rejected rejected =>
                    $"The exact Type inventory was rejected: {rejected.Kind}.",
                LibraryTypeDeclarationInventoryInspectionOutcome.Failed failed =>
                    $"The exact Type inventory failed: {failed.Kind}.",
                _ => throw new InvalidOperationException("Unknown Type inventory outcome."),
            });
        }

        return SelectDeclarations(completed.Correspondence.Inventory, type, scope,
            selectionKind, includePrefixDiscovery, cancellationToken, bounds);

        ExactTypeDefinitionSelection Unavailable(string detail) =>
            new(ExactTypeInspectionOutcome.Unavailable, type, null, [], detail);
    }

    internal static ExactTypeDefinitionSelection SelectDeclarations(
        AssemblyTypeDeclarationInventory inventory,
        string type,
        ApiSurfaceScope scope,
        ExactTypeSelectionKind selectionKind,
        bool includePrefixDiscovery = false,
        CancellationToken cancellationToken = default,
        ApiSurfaceExtractionBounds? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (scope is not (ApiSurfaceScope.Public or ApiSurfaceScope.IncludeAll
            or ApiSurfaceScope.PublicWithNonPublicTypes))
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (!Enum.IsDefined(selectionKind))
            throw new ArgumentOutOfRangeException(nameof(selectionKind));
        var declarations = inventory.Declarations;
        if (declarations.Any(item => item.Kind == AssemblyTypeDeclarationKind.ModuleExport))
            return Unavailable("The Library has exported Types whose defining module is unavailable.");
        AssemblyTypeDeclaration[] admitted =
            [.. declarations.Where(item => AdmitsDeclaration(item, scope))];
        if (bounds is not null
            && (admitted.Count(item => item.Kind == AssemblyTypeDeclarationKind.Definition) > bounds.MaxTypes
                || admitted.Count(item => item.Kind == AssemblyTypeDeclarationKind.Forwarder) > bounds.MaxTypeForwarders))
            return Unavailable("The exact Type inventory exceeded its declaration bounds.");
        cancellationToken.ThrowIfCancellationRequested();
        MetadataTypeLookupResult<AssemblyTypeDeclaration> selection =
            selectionKind == ExactTypeSelectionKind.DefinitionIdentity
                ? MetadataTypeLookup.Definition(admitted, static item => item.Name, type)
                : MetadataTypeLookup.Query(admitted, static item => item.Name, type);
        if (selection.Matches.Any(item => item.Kind != AssemblyTypeDeclarationKind.Definition))
            return Unavailable("A matching forwarded Type requires an authorized defining Library context.");
        if (selection.Matches.Length > 1)
            return new(ExactTypeInspectionOutcome.Ambiguous, type, null,
                [.. selection.Matches.Select(item => item.Name.ToEscapedFullName())],
                $"Type '{type}' resolved to more than one exact Metadata definition.");
        if (selection.Matches.Length == 1)
            return new(ExactTypeInspectionOutcome.Available, type, selection.Matches[0].Name, [], null)
            {
                DefinitionToken = selection.Matches[0].DefinitionToken,
            };
        bool? hasPrefixCandidates = null;
        if (includePrefixDiscovery && selectionKind == ExactTypeSelectionKind.Query)
        {
            var prefix = new TypePrefixPattern(type);
            hasPrefixCandidates = admitted.Any(item => prefix.Matches(item.Name));
        }
        return new(ExactTypeInspectionOutcome.NotFound, type, null,
            selectionKind == ExactTypeSelectionKind.Query
                ? [.. TypeMatcher.Lookup(admitted.Select(item => item.Name.ToEscapedFullName()), type).Suggestions]
                : [],
            $"Type '{type}' was not found.")
        {
            HasPrefixCandidates = hasPrefixCandidates,
        };

        ExactTypeDefinitionSelection Unavailable(string detail) =>
            new(ExactTypeInspectionOutcome.Unavailable, type, null, [], detail);
    }
}
