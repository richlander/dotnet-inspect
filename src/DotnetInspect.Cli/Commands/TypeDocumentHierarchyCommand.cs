using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal static class TypeDocumentHierarchyCommand
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 250_000,
            maxMembers: 2_000_000,
            maxInspectionFailures: 4_096,
            maxTypeForwarders: 250_000,
            maxMetadataRows: 5_000_000,
            maxRetainedTextCharacters: 8_000_000);

    internal static async Task<int?> TryExecuteAsync(
        ApiSourceResult source,
        TypeOptions options,
        CancellationToken cancellationToken)
    {
        if (!CanExecute(options))
            return null;

        string? apiDllPath =
            ApiServices.FindApiDll(
                source.SearchPath,
                source.Context.Logger);
        if (apiDllPath is null)
        {
            CommandError.Write("Could not find API DLL.");
            return 1;
        }

        TypeDocumentHierarchyPresentationFormat format =
            options.Format is OutputFormat.Mermaid
                ? TypeDocumentHierarchyPresentationFormat.Mermaid
                : TypeDocumentHierarchyPresentationFormat.Tree;
        TypeDocumentHierarchyPresentationPlan presentation =
            TypeDocumentHierarchyPresentation.CreateCompactPlan(
                format,
                options.IncludeAll);
        TypeHierarchyInspectionResult? result =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                    apiDllPath,
                    "Type document hierarchy",
                    session => Inspect(
                        session,
                        options.TypeName!,
                        presentation.Declarations,
                        presentation.Hierarchy,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        if (result is null
            || result is TypeHierarchyInspectionResult.Failed)
        {
            return 1;
        }
        if (result is TypeHierarchyInspectionResult.NotApplicable)
            return null;
        InspectionEnvelope<TypeDocumentInspectionOutcome>? envelope =
            ((TypeHierarchyInspectionResult.Completed)result).Envelope;
        if (envelope?.Content
            is not TypeDocumentInspectionOutcome.Available inspected)
        {
            WriteFailure(envelope?.Content);
            return 1;
        }

        try
        {
            TypeDocumentHierarchyPresentation.Write(
                inspected.Document,
                presentation,
                Console.Out);
            return 0;
        }
        catch (InvalidOperationException failure)
        {
            CommandError.Write(failure.Message);
            return 1;
        }
    }

    static bool CanExecute(TypeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TypeName)
            || options.TypeName.Contains('*', StringComparison.Ordinal)
            || options.TypeName.Contains('?', StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(options.TypeFilter))
        {
            return false;
        }

        if (!options.ShapeOutput
            && !options.Tree
            && options.Format is not OutputFormat.Mermaid)
            return false;
        if (options.AssemblyPath is { } assemblyPath
            && File.Exists(
                Path.ChangeExtension(
                    assemblyPath,
                    ".xml")))
        {
            return false;
        }

        return !options.HasSectionQuery
            && !options.Count
            && options.MemberFilter.Count == 0
            && options.KindFilter.Count == 0
            && !options.ShowDocs
            && !options.DocsExplicitlySet
            && !options.ShowSamples
            && options.Discover is null
            && !options.JsonOutput
            && !options.EnvelopeOutput
            && !options.Tabular
            && !options.Tsv
            && !options.Jsonl
            && options.Columns is null
            && options.Fields is null
            && options.Select is null
            && !options.SelectDefault
            && !options.Print
            && options.PrintRow is null
            && !options.Value
            && !options.Urls
            && !options.Paths
            && options.Limit is null
            && options.MemberLimit is null
            && options.Rows is null
            && !options.Schema
            && !options.BodyKindQuery.HasFilter
            && !options.PerformanceTriage.HasFilters
            && !options.CloneCandidateQuery.HasPredicates
            && !options.UnsafeOnly
            && !options.RequestAllTaste
            && !options.RequestReadableLocalNames;
    }

    static TypeHierarchyInspectionResult Inspect(
        ExactLibraryInspectionSession session,
        string typeQuery,
        TypeMemberGroupPopulationRequest declarations,
        InspectionHierarchyRequest<TypeDocumentHierarchyTopology>
            hierarchy,
        CancellationToken cancellationToken)
    {
        LibraryTypeListingResult? listing =
            LibraryTypeListingCommand.ReadRows(
                session,
                new(
                    LibraryTypeDeclarationSelection.Definitions,
                    ApiTypeInventoryKinds.All),
                cancellationToken,
                writeFailures: false);
        if (listing is null)
            return new TypeHierarchyInspectionResult.NotApplicable();

        if (!TryResolveType(
                listing.Rows,
                typeQuery,
                out MetadataTypeDefinitionName? type))
        {
            return new TypeHierarchyInspectionResult.NotApplicable();
        }

        return new TypeHierarchyInspectionResult.Completed(
            session.ExecuteTypeDocument(
                new(
                    type!,
                    s_bounds,
                    declarations,
                    hierarchy),
                cancellationToken));
    }

    static bool TryResolveType(
        IReadOnlyList<LibraryTypeShape> rows,
        string typeQuery,
        out MetadataTypeDefinitionName? type)
    {
        var candidates =
            rows.ToDictionary(
                static row =>
                    row.Identity.ToEscapedFullName(),
                static row => row.Identity,
                StringComparer.Ordinal);
        LookupResult lookup =
            TypeMatcher.Lookup(
                candidates.Keys,
                typeQuery);
        if (lookup.Match is { } match)
        {
            type = candidates[match];
            return true;
        }

        type = null;
        return false;
    }

    static void WriteFailure(
        TypeDocumentInspectionOutcome? outcome)
    {
        switch (outcome)
        {
            case TypeDocumentInspectionOutcome.Rejected rejected:
                CommandError.Write(
                    $"The Type document inspection was rejected: "
                        + $"{rejected.Reason}.");
                break;
            case TypeDocumentInspectionOutcome.Incomplete incomplete:
                CommandError.Write(
                    $"The Type document inspection reached the "
                        + $"{incomplete.Bound} bound "
                        + $"({incomplete.Measured}/{incomplete.Limit}).");
                break;
            case TypeDocumentInspectionOutcome.Failed failed:
                CommandError.Write(
                    $"The Type document inspection failed: "
                        + $"{failed.Reason}.");
                break;
            default:
                CommandError.Write(
                    "The Type document inspection is unavailable.");
                break;
        }
    }

    abstract record TypeHierarchyInspectionResult
    {
        public sealed record Failed : TypeHierarchyInspectionResult;

        public sealed record NotApplicable :
            TypeHierarchyInspectionResult;

        public sealed record Completed(
            InspectionEnvelope<TypeDocumentInspectionOutcome>? Envelope)
            : TypeHierarchyInspectionResult;
    }
}
