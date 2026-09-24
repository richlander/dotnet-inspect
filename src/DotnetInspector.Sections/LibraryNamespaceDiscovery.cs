using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Shared composition for namesake-Library discovery of one exact namespace.
/// </summary>
public static class LibraryNamespaceDiscovery
{
    private const int MaximumNamesakeCandidates = 64;

    /// <summary>
    /// Returns proper dotted prefixes in longest-first order.
    /// </summary>
    public static ImmutableArray<string> NamesakeLibraryCandidates(
        string namespaceName)
    {
        ArgumentNullException.ThrowIfNull(namespaceName);
        if (namespaceName.Length == 0
            || namespaceName[0] == '.'
            || namespaceName[^1] == '.'
            || namespaceName.Contains("..", StringComparison.Ordinal))
        {
            return [];
        }

        var candidates = ImmutableArray.CreateBuilder<string>();
        int searchEnd = namespaceName.Length;
        while (candidates.Count < MaximumNamesakeCandidates)
        {
            int separator = namespaceName.LastIndexOf(
                '.',
                searchEnd - 1);
            if (separator <= 0)
                break;

            string candidate = namespaceName[..separator];
            if (!candidate.Contains('.', StringComparison.Ordinal))
                break;

            candidates.Add(candidate);
            searchEnd = separator;
        }

        return candidates.ToImmutable();
    }

    /// <summary>
    /// Creates the minimal exact-namespace request needed to confirm a hit.
    /// </summary>
    public static LibraryInspectionPlan CreateProbePlan(
        string namespaceName,
        ApiSurfaceExtractionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(namespaceName);
        ArgumentNullException.ThrowIfNull(bounds);
        return new(
            new(
                LibraryTypeAccessibility.Public,
                count: null,
                rows: new(maximumRows: 1),
                LibraryTypeDeclarationSelection.DefinitionsAndForwarders,
                ApiTypeInventoryKinds.All,
                namespaceName,
                MetadataNamespaceMatch.Exact),
            bounds);
    }

    /// <summary>
    /// Returns the exact namespace probe result from a matching Library document.
    /// </summary>
    public static LibraryTypePopulationRowsOutcome ProbeRows(
        LibraryDocument document,
        string namespaceName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(namespaceName);
        LibraryTypePopulationBinding binding = document.Types.Binding;
        if (!string.Equals(
                binding.Namespace,
                namespaceName,
                StringComparison.Ordinal)
            || binding.NamespaceMatch is not MetadataNamespaceMatch.Exact)
        {
            throw new ArgumentException(
                "The Library document does not describe the requested exact namespace.",
                nameof(document));
        }

        return document.Types.Rows
            ?? throw new ArgumentException(
                "The Library document does not contain namespace probe rows.",
                nameof(document));
    }
}
