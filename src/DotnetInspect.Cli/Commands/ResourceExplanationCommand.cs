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
        string resourcePath,
        int depth,
        OutputFormat format,
        string? outputPath)
    {
        if (format is not (
                OutputFormat.Markdown
                or OutputFormat.PlainText
                or OutputFormat.Json))
        {
            CommandError.Write(
                "explain supports Markdown, plain text, and JSON output.");
            return 1;
        }

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

        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateStructural(
                structural.Document,
                structural.ResourcePaths);
        ResourcePathResolution resolution = catalog.Resolve(resourcePath);
        if (resolution is ResourcePathResolution.Invalid invalid)
        {
            CommandError.Write(
                $"Resource path '{invalid.RequestedPath}' is invalid.",
                invalid.Reason);
            return 1;
        }
        if (resolution is ResourcePathResolution.Unknown unknown)
        {
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

        var request =
            new ResourceExplanationRequest(
                depth,
                ResourceLimit,
                RelationshipLimit);
        InspectionEnvelope<ResourceExplanationDocument> envelope =
            catalog.Explain(
                (ResourcePathResolution.Resolved)resolution,
                request);
        ResourceExplanationDocument document = envelope.Content;
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
}
