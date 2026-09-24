using System.Collections.Immutable;

using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspect.Cli.Commands;

internal sealed record LibraryTypeListingResult(
    LibraryDocument Document,
    ImmutableArray<LibraryTypeShape> Rows);

internal static class LibraryTypeListingCommand
{
    private const int SegmentSize = 256;

    private static readonly HashSet<string> s_supportedSections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            SectionNames.Classes,
            SectionNames.Structs,
            SectionNames.Interfaces,
            SectionNames.Enums,
            SectionNames.Delegates,
            SectionNames.TypeForwarders,
        };

    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    public static async Task<int?> TryExecuteAsync(
        ApiSourceResult source,
        TypeOptions options,
        CancellationToken cancellationToken)
    {
        if (!CanExecute(options))
            return null;

        string? assemblyPath =
            ApiServices.FindApiDll(
                source.SearchPath,
                source.Context.Logger);
        if (assemblyPath is null)
            return 1;

        LibraryTypeDeclarationSelection declarationSelection =
            GetDeclarationSelection(options);
        LibraryTypeListingResult? result =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                assemblyPath,
                "Library Type listing",
                session =>
                    options.Count
                        ? ReadCount(
                            session,
                            declarationSelection,
                            cancellationToken)
                        : ReadRows(
                            session,
                            declarationSelection,
                            cancellationToken),
                cancellationToken);
        if (result is null)
            return 1;

        return ApiCommand.WriteLibraryTypeListingOutput(
            result,
            options);
    }

    private static bool CanExecute(TypeOptions options)
    {
        if (options.TypeName is not null
            || options.TypeFilter is not null
            || options.IncludeAll
            || options.ShowDocs
            || options.ShowSamples
            || options.UnsafeOnly
            || options.MemberFilter.Count > 0
            || options.KindFilter.Count > 0
            || options.TypeListingRowSelection is not null
            || options.Rows is not null
            || options.Limit is not null
            || options.ShapeOutput
            || options.EnvelopeOutput
            || options.EffectiveDiscovery
            || options.Schema
            || options.Tree
            || options.MermaidOutput
            || options.EmbeddedMermaid
            || options.Print
            || options.Value
            || options.Urls
            || options.Paths
            || options.Columns is { Length: > 0 }
            || options.Fields is { Length: > 0 }
            || options.PerformanceTriage.HasFilters
            || options.BodyKindQuery.HasFilter
            || options.CloneCandidateQuery.HasPredicates)
        {
            return false;
        }

        if (options.IncludeSections is { } sections
            && !sections.IsSubsetOf(s_supportedSections))
        {
            return false;
        }

        return options.UserVerbosity
                is Verbosity.Minimal or Verbosity.Normal
            && !options.JsonOutput
            && !options.Tabular
            && !options.Tsv
            && !options.Jsonl
            && !options.PlainText
            && !options.NoHeader
            && options.Format == OutputFormat.Markdown;
    }

    private static LibraryTypeDeclarationSelection
        GetDeclarationSelection(TypeOptions options)
    {
        if (options.CountDefaultPopulation
            || options.IncludeSections is not { Count: > 0 } sections)
        {
            return LibraryTypeDeclarationSelection
                .DefinitionsAndForwarders;
        }

        bool includeForwarders =
            sections.Contains(SectionNames.TypeForwarders);
        bool includeDefinitions =
            sections.Any(
                static section =>
                    section is SectionNames.Classes
                        or SectionNames.Structs
                        or SectionNames.Interfaces
                        or SectionNames.Enums
                        or SectionNames.Delegates);

        return (includeDefinitions, includeForwarders) switch
        {
            (true, true) =>
                LibraryTypeDeclarationSelection
                    .DefinitionsAndForwarders,
            (true, false) =>
                LibraryTypeDeclarationSelection.Definitions,
            (false, true) =>
                LibraryTypeDeclarationSelection.Forwarders,
            _ =>
                LibraryTypeDeclarationSelection
                    .DefinitionsAndForwarders,
        };
    }

    private static LibraryTypeListingResult? ReadCount(
        ExactLibraryInspectionSession session,
        LibraryTypeDeclarationSelection declarationSelection,
        CancellationToken cancellationToken)
    {
        var plan =
            new LibraryInspectionPlan(
                new LibraryTypePopulationRequest(
                    LibraryTypeAccessibility.Public,
                    new LibraryTypePopulationCountRequest(),
                    declarationSelection: declarationSelection),
                s_bounds);
        InspectionEnvelope<LibraryInspectionOutcome>? envelope =
            session.Execute(plan, cancellationToken);
        if (!TryGetDocument(envelope, out LibraryDocument document))
            return null;
        if (document.Types.Count
            is not LibraryTypePopulationCountOutcome.Counted)
        {
            WriteCountFailure(document.Types.Count);
            return null;
        }

        return new LibraryTypeListingResult(document, []);
    }

    private static LibraryTypeListingResult? ReadRows(
        ExactLibraryInspectionSession session,
        LibraryTypeDeclarationSelection declarationSelection,
        CancellationToken cancellationToken)
    {
        var rows = ImmutableArray.CreateBuilder<LibraryTypeShape>();
        LibraryDocument? firstDocument = null;
        LibraryTypePopulationBinding? binding = null;
        LibraryTypePopulationContinuation? continuation = null;
        var continuations = new HashSet<string>(StringComparer.Ordinal);

        do
        {
            var plan =
                new LibraryInspectionPlan(
                    new LibraryTypePopulationRequest(
                        LibraryTypeAccessibility.Public,
                        count: null,
                        new LibraryTypePopulationRowsRequest(
                            SegmentSize,
                            memberCount:
                                new LibraryTypeMemberCountRequest(),
                            continuation: continuation),
                        declarationSelection),
                    s_bounds);
            InspectionEnvelope<LibraryInspectionOutcome>? envelope =
                session.Execute(plan, cancellationToken);
            if (!TryGetDocument(
                    envelope,
                    out LibraryDocument document))
            {
                return null;
            }

            firstDocument ??= document;
            binding ??= document.Types.Binding;
            if (document.Types.Binding != binding)
            {
                CommandError.Write(
                    "The Library Type population changed while rows "
                        + "were being read.");
                return null;
            }
            if (document.Types.Rows
                is not LibraryTypePopulationRowsOutcome.Read read)
            {
                WriteRowsFailure(document.Types.Rows);
                return null;
            }

            rows.AddRange(read.Items);
            continuation = read.Continuation;
            if (continuation is not null)
            {
                string current = continuation.Value.ToString();
                if (!continuations.Add(current))
                {
                    CommandError.Write(
                        "The Library Type population repeated a "
                            + "continuation.");
                    return null;
                }
            }
        }
        while (continuation is not null);

        return new LibraryTypeListingResult(
            firstDocument
            ?? throw new InvalidOperationException(
                "Library Type row inspection produced no document."),
            rows.ToImmutable());
    }

    private static bool TryGetDocument(
        InspectionEnvelope<LibraryInspectionOutcome>? envelope,
        out LibraryDocument document)
    {
        document = null!;
        if (envelope is null)
            return false;

        bool hasErrors = WriteDiagnostics(envelope.Diagnostics);
        switch (envelope.Content)
        {
            case LibraryInspectionOutcome.Available available:
                document = available.Document;
                return !hasErrors;
            case LibraryInspectionOutcome.Rejected rejected:
                CommandError.Write(
                    "The Library inspection request was rejected.",
                    $"Reason: {rejected.Reason}");
                return false;
            case LibraryInspectionOutcome.Failed failed:
                CommandError.Write(
                    "The Library inspection failed.",
                    $"Reason: {failed.Reason}");
                return false;
            default:
                throw new InvalidOperationException(
                    "Unknown Library inspection outcome.");
        }
    }

    private static bool WriteDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics)
    {
        bool hasErrors = false;
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            string message = diagnostic.Summary.ToString();
            switch (diagnostic.Severity)
            {
                case InspectionDiagnosticSeverity.Information:
                    CommandError.WriteNote(message);
                    break;
                case InspectionDiagnosticSeverity.Warning:
                    CommandError.WriteWarning(message);
                    break;
                case InspectionDiagnosticSeverity.Error:
                    CommandError.Write(message);
                    hasErrors = true;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown Library inspection diagnostic severity.");
            }
        }

        return hasErrors;
    }

    private static void WriteCountFailure(
        LibraryTypePopulationCountOutcome? outcome)
    {
        switch (outcome)
        {
            case null:
                CommandError.Write(
                    "The Library Type population did not return Count.");
                break;
            case LibraryTypePopulationCountOutcome.Unavailable unavailable:
                CommandError.Write(
                    "Library Type Count is unavailable.",
                    $"Reason: {unavailable.Reason}");
                break;
            case LibraryTypePopulationCountOutcome.Incomplete incomplete:
                CommandError.Write(
                    "Library Type Count exceeded an inspection bound.",
                    [
                        $"Bound: {incomplete.Bound}",
                        $"Limit: {incomplete.Limit}",
                        $"Measured: {incomplete.Measured}",
                    ]);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Library Type Count outcome.");
        }
    }

    private static void WriteRowsFailure(
        LibraryTypePopulationRowsOutcome? outcome)
    {
        switch (outcome)
        {
            case null:
                CommandError.Write(
                    "The Library Type population did not return Rows.");
                break;
            case LibraryTypePopulationRowsOutcome.Unavailable unavailable:
                CommandError.Write(
                    "Library Type Rows are unavailable.",
                    $"Reason: {unavailable.Reason}");
                break;
            case LibraryTypePopulationRowsOutcome.Rejected rejected:
                CommandError.Write(
                    "Library Type Rows were rejected.",
                    $"Reason: {rejected.Reason}");
                break;
            case LibraryTypePopulationRowsOutcome.Incomplete incomplete:
                CommandError.Write(
                    "Library Type Rows exceeded an inspection bound.",
                    [
                        $"Bound: {incomplete.Bound}",
                        $"Limit: {incomplete.Limit}",
                        $"Measured: {incomplete.Measured}",
                    ]);
                break;
            case LibraryTypePopulationRowsOutcome.Failed failed:
                CommandError.Write(
                    "Library Type Rows failed.",
                    $"Reason: {failed.Reason}");
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Library Type Rows outcome.");
        }
    }
}
