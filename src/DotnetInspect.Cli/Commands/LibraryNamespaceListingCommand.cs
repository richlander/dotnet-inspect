using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Commands;

internal static class LibraryNamespaceListingCommand
{
    internal static bool ValidateOptions(LibraryOptions options)
    {
        if (options.TypeNamespace is null)
        {
            if (options.IncludeNamespaceChildren)
            {
                CommandError.Write(
                    "library --children requires --namespace.");
                return false;
            }
            return true;
        }
        if (!TryGetNamespaceMatch(options, out _))
            return false;

        if (options.JsonOutput
            || options.PlainText
            || options.Tabular
            || options.Tsv
            || options.Jsonl
            || options.NoHeader
            || options.Tree
            || options.Format is not OutputFormat.Markdown
            || options.Count
            || options.Rows is not null
            || options.Print
            || options.Value
            || options.Urls
            || options.Paths
            || options.JsonArray
            || options.OutputPath is not null
            || options.Select is { Length: > 0 }
            || options.SelectDefault
            || options.SelectExplicitlySet
            || options.IncludeSections is { Count: > 0 }
            || options.Discover is not null
            || options.DiscoverDetails
            || options.Effective
            || options.Schema
            || options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 }
            || options.TypeFilter is not null
            || options.IncludeReferences
            || options.IncludeDependencies
            || options.ReferenceHierarchyDepth is not null
            || options.CoordinateRequest is not null
            || options.ExtractResources is not null
            || options.IntegrationQuery.HasFilter
            || options.PerformanceTriage.HasFilters
            || options.PerformanceTriage.HasRanking
            || options.BodyKindQuery.HasFilter
            || options.CloneCandidateQuery.HasPredicates)
        {
            CommandError.Write(
                "library --namespace currently supports its complete "
                    + "Markdown Type listing without section, row, "
                    + "projection, or alternate-format controls.");
            return false;
        }

        return true;
    }

    internal static async Task<int> ExecuteAsync(
        string assemblyPath,
        LibraryOptions options,
        CancellationToken cancellationToken)
    {
        string @namespace =
            options.TypeNamespace
            ?? throw new InvalidOperationException(
                "Namespace Type listing requires a namespace.");
        if (!TryGetNamespaceMatch(
                options,
                out MetadataNamespaceMatch namespaceMatch))
        {
            return 1;
        }
        var selection =
            new LibraryTypeListingCommand
                .LibraryTypePopulationSelection(
                    LibraryTypeDeclarationSelection
                        .DefinitionsAndForwarders,
                    ApiTypeInventoryKinds.All);

        LibraryTypeListingResult? result;
        try
        {
            result =
                await ExactLibraryInspectionExecutor.ExecuteAsync(
                    assemblyPath,
                    "Library namespace Type listing",
                    session =>
                        LibraryTypeListingCommand.ReadRows(
                            session,
                            selection,
                            cancellationToken,
                            @namespace,
                            namespaceMatch),
                    cancellationToken);
        }
        catch (ArgumentException failure)
        {
            CommandError.Write(failure.Message);
            return 1;
        }
        if (result is null)
            return 1;

        return ApiCommand.WriteLibraryNamespaceListingOutput(result);
    }

    internal static bool TryGetNamespaceMatch(
        LibraryOptions options,
        out MetadataNamespaceMatch namespaceMatch)
    {
        ArgumentNullException.ThrowIfNull(options);
        namespaceMatch = MetadataNamespaceMatch.Exact;
        string? @namespace = options.TypeNamespace;
        if (@namespace is null)
        {
            if (!options.IncludeNamespaceChildren)
                return true;

            CommandError.Write(
                "library --children requires --namespace.");
            return false;
        }

        bool suffix =
            @namespace.StartsWith(".", StringComparison.Ordinal);
        if (suffix && options.IncludeNamespaceChildren)
        {
            CommandError.Write(
                "library --children cannot be combined with a leading-dot "
                    + "namespace suffix.");
            return false;
        }

        namespaceMatch = suffix
            ? MetadataNamespaceMatch.Suffix
            : options.IncludeNamespaceChildren
                ? MetadataNamespaceMatch.ExactOrDescendant
                : MetadataNamespaceMatch.Exact;
        return true;
    }
}
