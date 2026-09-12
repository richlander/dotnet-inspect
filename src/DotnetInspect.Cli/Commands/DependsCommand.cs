using ILInspector.CSharp;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Walks dependency graphs upward: type hierarchies, library references, or package dependencies.
/// </summary>
public partial class DependsCommand
{
    /// <summary>
    /// Returned when the target type was not found so the command surface can report it.
    /// </summary>
    internal const int TypeNotFoundExitCode = 2;

    /// <summary>
    /// Returned when the scan produced usable output but at least one candidate
    /// assembly was excluded, so completeness is unknown. The result is not a
    /// certified success and must not be consumed as one.
    /// </summary>
    internal const int UncertifiedScanExitCode = 3;

    /// <summary>
    /// The outcome of a type dependency scan. <see cref="ExitCode"/> reports
    /// what the scan found, so the caller can still fall back to library mode
    /// or diagnose an absence. <see cref="Uncertified"/> reports separately
    /// that a candidate was excluded. The two must stay separate: folding
    /// uncertainty into the exit code hides the outcome the caller dispatches
    /// on, which silently withholds an answer the caller would otherwise emit.
    /// </summary>
    internal readonly record struct TypeDependsOutcome(
        int ExitCode,
        bool Uncertified);

    internal static async Task<TypeDependsOutcome> ExecuteTypeDependsAsync(
        DependsOptions options,
        CancellationToken cancellationToken = default)
    {
        SectionCatalog<DependsAssetProjection> catalog =
            DependsAssetSections.GraphCatalog;
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            catalog.SelectableSectionNames,
            catalog.InfoSectionNames,
            catalog.SelectionCategoryMap,
            options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return new TypeDependsOutcome(1, false);

        if (options.Discover is { } discover)
        {
            return new TypeDependsOutcome(
                DiscoverOutput.Execute(
                    discover,
                    DependsAssetSections.CreateGraphSchema(),
                    tree: options.Tree,
                    json: options.JsonOutput,
                    tsv: options.Tsv,
                    jsonl: options.Jsonl,
                    sectionCostAnnotations:
                        catalog.Pipeline.GetCostAnnotations(),
                    sectionCategories: catalog.SelectionCategoryMap,
                    projection: options),
                false);
        }

        if (options.Schema)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return new TypeDependsOutcome(1, false);
        }
        if (IsColumnProjectionRequested(options))
        {
            CommandError.Write(
                "--columns and --fields are not supported in positional type mode.");
            return new TypeDependsOutcome(1, false);
        }

        HashSet<string> requestedSections =
            catalog.Pipeline.GetCandidateSections(
                options.Verbosity,
                selection.Sections,
                fixedOverview: options.SelectDefault);
        bool emptyQuietSelection =
            requestedSections.Count == 0
            && options.Verbosity == Verbosity.Quiet;
        if (options.Depth is not null
            && !requestedSections.Contains(
                DependsAssetSections.DependencyGraph))
        {
            CommandError.Write(
                "--depth requires the Dependency Graph section.");
            return new TypeDependsOutcome(1, false);
        }
        if (!emptyQuietSelection
            && !requestedSections.Contains(
                DependsAssetSections.DependencyGraph))
        {
            CommandError.Write(
                $"Type relationship mode currently produces only the '{DependsAssetSections.DependencyGraph}' section.");
            return new TypeDependsOutcome(1, false);
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            // Safety fallback — default to all platform frameworks
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            var result = await DependencyGraphService.BuildTypeDependencyTreeAsync(
                context.HttpClient,
                options,
                logger,
                cancellationToken);

            // A rejected participant scopes to itself and leaves the rest of
            // the scan intact, but the resulting graph is uncertified: it may
            // omit edges the rejected assembly would have contributed. Name
            // each rejection before any result or absence claim, so neither a
            // partial graph nor a "not found" is reported as certified.
            WriteRejectionWarnings(result.Diagnostics);
            bool uncertified = result.Diagnostics.Count > 0;
            if (!result.IsAvailable)
            {
                if (uncertified)
                {
                    CommandError.Write(
                        "Dependency scan unavailable because every selected assembly was rejected.");
                }
                return new TypeDependsOutcome(1, false);
            }

            if (!result.Dependency.Found)
            {
                // Report the absence as an absence so the caller can still fall
                // back or diagnose it, and carry the uncertainty alongside.
                return new TypeDependsOutcome(TypeNotFoundExitCode, uncertified);
            }

            if (result.RowSelectionFailure is { } rowFailure)
            {
                CommandError.Write(
                    $"Type dependency row selection stage "
                    + $"{rowFailure.Failure.StageNumber} requires row "
                    + $"{rowFailure.Failure.RequiredPosition}, but "
                    + $"{rowFailure.Identity} has "
                    + $"{rowFailure.Failure.AvailableCount} rows.");
                return new TypeDependsOutcome(1, uncertified);
            }
            if (emptyQuietSelection)
                return Certified(0, uncertified);

            DependencyGraphDocument document =
                DependencyGraphProjection.Type(result.Dependency);
            HashSet<int> selectedRelationshipOrdinals =
                [
                    .. result.Relationships.Select(
                        static relationship => relationship.Ordinal),
                ];
            TypeDependencyRelationship[] orderedRelationships =
            [
                .. result.Dependency.Relationships.OrderBy(
                    static relationship => relationship.Ordinal),
            ];
            List<DependencyGraphEdgeRow> allRows =
                DependencyGraphOutputAdapter.EdgeRows(document);
            if (allRows.Count != orderedRelationships.Length)
            {
                throw new InvalidOperationException(
                    "The type dependency graph projection did not preserve "
                        + "the query relationship count.");
            }
            IReadOnlyList<DependencyGraphEdgeRow> rows =
            [
                .. allRows.Where(
                    (_, index) =>
                        selectedRelationshipOrdinals.Contains(
                            orderedRelationships[index].Ordinal)),
            ];
            if (options.Count)
            {
                CountOutput.WriteCount(rows.Count);
            }
            else
            {
                DependencyGraphOutputAdapter.Write(
                    document,
                    rows,
                    EffectiveFormat(options),
                    options.Tree,
                    options.EmbeddedMermaid,
                    options.NoHeader,
                    options.CompactJson);
            }

            return Certified(0, uncertified);
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return new TypeDependsOutcome(1, false);
        }
    }

    internal static bool ValidateTypeDepthSelectionBeforeAcquisition(
        DependsOptions options)
    {
        if (options.Depth is null
            || options.Discover is not null
            || options.Schema
            || IsColumnProjectionRequested(options))
        {
            return true;
        }

        SectionCatalog<DependsAssetProjection> catalog =
            DependsAssetSections.GraphCatalog;
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            catalog.SelectableSectionNames,
            catalog.InfoSectionNames,
            catalog.SelectionCategoryMap,
            options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return false;

        HashSet<string> requestedSections =
            catalog.Pipeline.GetCandidateSections(
                options.Verbosity,
                selection.Sections,
                fixedOverview: options.SelectDefault);
        if (requestedSections.Contains(
                DependsAssetSections.DependencyGraph))
        {
            return true;
        }

        CommandError.Write(
            "--depth requires the Dependency Graph section.");
        return false;
    }

    /// <summary>
    /// A tree label is written straight into the terminal beside a box-drawing
    /// gutter, so an ESC or a bidi override in a metadata name rewrites the
    /// shape of the tree itself (issue #3319). Containment goes here, at
    /// construction, so every renderer of the node inherits it.
    /// </summary>
    private static void WriteRejectionWarnings(
        IReadOnlyList<TypeDependencyScanDiagnostic> diagnostics)
    {
        foreach (TypeDependencyScanDiagnostic diagnostic in diagnostics)
        {
            string mechanism = diagnostic.Failure.Kind switch
            {
                CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                    "unsupported metadata format (Windows Metadata)",
                CandidateOpenFailureKind.ResourceBudget =>
                    $"resource budget ({diagnostic.Failure.Detail})",
                CandidateOpenFailureKind.Unreadable =>
                    $"unreadable image ({diagnostic.Failure.Detail})",
                _ when diagnostic.Failure.MetadataRootReason is { } reason =>
                    $"malformed metadata root ({reason})",
                _ => "invalid image",
            };
            CommandError.WriteWarning(
                $"Excluded '{ContainLabel(diagnostic.Subject)}' from the dependency scan: {mechanism}.",
                "Results may be incomplete.");
        }
    }

    private static TypeDependsOutcome Certified(int exitCode, bool uncertified)
        => new(
            uncertified && exitCode == 0 ? UncertifiedScanExitCode : exitCode,
            uncertified);

    private static string ContainLabel(string label)
        => CSharpIdentifier.ContainRenderedText(label);

    private static OutputFormat EffectiveFormat(DependsOptions options) =>
        options.JsonOutput
            ? OutputFormat.Json
            : options.MermaidOutput
                ? OutputFormat.Mermaid
                : options.Format;

}
