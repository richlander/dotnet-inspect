using System.Text.Json;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Commands;

public static class ResourceExplanationCommand
{
    public const string Name = "explain";
    private const int ResourceLimit = 256;
    private const int RelationshipLimit = 2048;

    public static int Execute(
        string operand,
        int depth,
        bool depthExplicitlySet,
        int? maximumResults,
        OutputFormat format,
        bool envelopeOutput,
        bool noHeaders,
        string? outputPath)
    {
        StructuralSchemaProjection projection =
            StructuralViewRegistry.Project(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.DirectLibrary,
                    InspectionCatalogIdentity.Library));
        DiscoveryDocumentFactory.Projection? structural =
            DiscoveryDocumentFactory.CreateProjection(
                "library",
                discover: null,
                projection.Schema,
                projection.SectionCategories,
                projection.CatalogHiddenSections,
                projection.ListedCategoryDoors,
                projection.SectionCostAnnotations,
                projection.ExactOnlySections,
                projection.OutputCapabilities
                ?? throw new InvalidOperationException(
                    "Library output capabilities are required."),
                sectionCardinalities:
                    projection.SectionCardinalities);
        if (structural is null)
        {
            CommandError.Write(
                "The Library structural resource catalog could not be built.");
            return 1;
        }

        ResourceExplanationCatalog structuralCatalog =
            ResourceExplanationCatalog.CreateStructural(
                structural.Document,
                structural.ResourcePaths);
        InspectionCapabilityCatalog capabilityCatalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    PackageQueryCommandCapability.Module,
                ]);
        ResourceExplanationCatalog capabilityExplanation =
            ResourceExplanationCatalog.CreateCapabilities(
                capabilityCatalog,
                PackageQueryCapabilityResourcePaths.Create(
                    capabilityCatalog));
        InspectionCapabilityCatalog packageFilesCapabilityCatalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageFileInventoryCapability.ProductModule,
                    PackageFileInventoryCommandCapability.Module,
                ]);
        ResourceExplanationCatalog packageFilesCapabilityExplanation =
            ResourceExplanationCatalog.CreateCapabilities(
                packageFilesCapabilityCatalog,
                PackageFileInventoryCapabilityResourcePaths.Create(
                    packageFilesCapabilityCatalog));
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.Combine(
                structuralCatalog,
                capabilityExplanation,
                packageFilesCapabilityExplanation);
        string normalizedOperand = operand.Trim();
        ResourcePath.TryCreate(
            normalizedOperand,
            out ResourcePath? canonicalPath,
            out _);
        if (canonicalPath is not null
            && catalog.TryResolveExact(
                canonicalPath,
                out ResourcePathResolution.Resolved? resolved))
        {
            if (maximumResults is not null)
            {
                CommandError.Write(
                    "-n applies only when explain performs capability "
                    + "search.");
                return 1;
            }
            return ExplainExact(
                catalog,
                resolved,
                depth,
                format,
                envelopeOutput,
                outputPath);
        }

        if (canonicalPath is not null
            && canonicalPath.Value.Contains('/'))
        {
            return WriteResolutionFailure(
                catalog.Resolve(normalizedOperand));
        }

        if (depthExplicitlySet)
        {
            CommandError.Write(
                "--depth applies only to exact Resource Explanation paths.");
            return 1;
        }

        CapabilityCatalogSearchRequest searchRequest;
        try
        {
            searchRequest = new(
                normalizedOperand,
                maximumResults
                    ?? CapabilityCatalogSearchRequest.DefaultMaximumResults);
        }
        catch (ArgumentException exception)
        {
            CommandError.Write(exception.Message);
            return 1;
        }

        InspectionEnvelope<CapabilityCatalogSearchDocument> search =
            CapabilityCatalogSearch.Search(
                capabilityCatalog,
                capabilityExplanation,
                searchRequest);
        WriteSearch(
            search,
            format,
            envelopeOutput,
            noHeaders,
            outputPath);
        return 0;
    }

    private static int ExplainExact(
        ResourceExplanationCatalog catalog,
        ResourcePathResolution.Resolved resolution,
        int depth,
        OutputFormat format,
        bool envelopeOutput,
        string? outputPath)
    {
        if (envelopeOutput
            || format is not (
                OutputFormat.Markdown
                or OutputFormat.PlainText
                or OutputFormat.Json))
        {
            CommandError.Write(
                "Exact explain supports Markdown, plain text, and JSON "
                + "output.");
            return 1;
        }

        InspectionEnvelope<ResourceExplanationDocument> explanation =
            catalog.Explain(
                resolution,
                new(
                    depth,
                    ResourceLimit,
                    RelationshipLimit));
        ResourceExplanationDocument document = explanation.Content;
        OutputDestination.Write(
            outputPath,
            rowWindow: null,
            output =>
            {
                if (format == OutputFormat.Json)
                {
                    output.WriteLine(
                        JsonSerializer.Serialize(
                            document,
                            ResourceExplanationJsonContext
                                .Default
                                .ResourceExplanationDocument));
                    return;
                }

                MarkoutSerializer.Serialize(
                    ResourceExplanationView.Create(document),
                    output,
                    format == OutputFormat.PlainText
                        ? new PlainTextFormatter()
                        : new MarkdownFormatter(),
                    ResourceExplanationViewContext.Default);
            });
        return 0;
    }

    private static int WriteResolutionFailure(
        ResourcePathResolution resolution)
    {
        if (resolution is ResourcePathResolution.Invalid invalid)
        {
            CommandError.Write(
                $"Resource path '{invalid.RequestedPath}' is invalid.",
                invalid.Reason);
            return 1;
        }

        var unknown = (ResourcePathResolution.Unknown)resolution;
        CommandError.Write(
            $"Resource path '{unknown.RequestedPath}' was not found.");
        if (!unknown.Suggestions.IsEmpty)
        {
            CommandError.WriteBlankLine();
            CommandError.WriteLine("Did you mean:");
            foreach (ResourcePath suggestion in unknown.Suggestions)
                CommandError.WriteDetail(suggestion.Value);
        }
        return 1;
    }

    private static void WriteSearch(
        InspectionEnvelope<CapabilityCatalogSearchDocument> envelope,
        OutputFormat format,
        bool envelopeOutput,
        bool noHeaders,
        string? outputPath)
    {
        OutputDestination.Write(
            outputPath,
            rowWindow: null,
            output =>
            {
                if (envelopeOutput)
                {
                    output.WriteLine(
                        JsonSerializer.Serialize(
                            envelope,
                            CapabilityCatalogSearchJsonContext
                                .Default
                                .InspectionEnvelopeCapabilityCatalogSearchDocument));
                    return;
                }
                if (format == OutputFormat.Json)
                {
                    output.WriteLine(
                        JsonSerializer.Serialize(
                            envelope.Content,
                            CapabilityCatalogSearchJsonContext
                                .Default
                                .CapabilityCatalogSearchDocument));
                    return;
                }

                if (format is OutputFormat.Table
                    or OutputFormat.Tsv
                    or OutputFormat.Jsonl)
                {
                    MarkoutSerializer.Serialize(
                        CapabilityCatalogSearchTableView.Create(
                            envelope.Content),
                        output,
                        new TableFormatter(!noHeaders),
                        CapabilityCatalogSearchViewContext.Default,
                        OutputFormatter.CreateTableWriterOptions(
                            tsv: format == OutputFormat.Tsv,
                            jsonl: format == OutputFormat.Jsonl));
                    return;
                }

                MarkoutSerializer.Serialize(
                    CapabilityCatalogSearchView.Create(envelope.Content),
                    output,
                    format == OutputFormat.PlainText
                        ? new PlainTextFormatter()
                        : new MarkdownFormatter(),
                    CapabilityCatalogSearchViewContext.Default);
            });
    }
}
