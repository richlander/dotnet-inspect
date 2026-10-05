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
        string normalizedOperand = operand.Trim();
        ResourcePath.TryCreate(
            normalizedOperand,
            out ResourcePath? canonicalPath,
            out _);
        if (canonicalPath is not null)
        {
            string root = RootSegment(canonicalPath);
            foreach ((
                         StructuralViewIdentity view,
                         InspectionCatalogIdentity identity)
                     in ExplainableStructuralRoutes)
            {
                if (StructuralViewRegistry.CatalogPathName(identity) != root)
                    continue;

                ResourceExplanationCatalog? structuralCatalog =
                    CreateStructuralCatalog(view, identity);
                if (structuralCatalog is null)
                {
                    CommandError.Write(
                        $"The {root} structural resource catalog could not "
                        + "be built.");
                    return 1;
                }
                return ExplainFromCatalog(
                    structuralCatalog,
                    canonicalPath,
                    depth,
                    maximumResults,
                    format,
                    envelopeOutput,
                    outputPath);
            }

            ResourceExplanationCatalog? exactCatalog = root switch
            {
                "package-query" => CreatePackageQueryExplanation(),
                "package-files" => CreatePackageFilesExplanation(),
                ResourceExplanationCatalog.AnalysesCollectionSegment =>
                    CreateAnalysisExplanation(),
                _ => null,
            };
            if (exactCatalog is not null)
            {
                return ExplainFromCatalog(
                    exactCatalog,
                    canonicalPath,
                    depth,
                    maximumResults,
                    format,
                    envelopeOutput,
                    outputPath);
            }

            if (canonicalPath.Value.Contains('/'))
            {
                return WriteCompleteResolutionFailure(normalizedOperand);
            }
        }

        if (depthExplicitlySet)
        {
            CommandError.Write(
                "--depth applies only to exact Resource Explanation paths.");
            return 1;
        }

        (
            InspectionCapabilityCatalog capabilityCatalog,
            ResourceExplanationCatalog capabilityExplanation) =
                CreateCapabilityExplanation();
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

    private static ResourceExplanationCatalog? CreateStructuralCatalog(
        StructuralViewIdentity view,
        InspectionCatalogIdentity catalogIdentity)
    {
        StructuralSchemaProjection projection =
            StructuralViewRegistry.Project(
                StructuralViewRegistry.Route(view, catalogIdentity));
        string catalog =
            StructuralViewRegistry.CatalogPathName(catalogIdentity);
        DiscoveryDocumentFactory.Projection? structural =
            DiscoveryDocumentFactory.CreateProjection(
                catalog,
                discover: null,
                projection.Schema,
                projection.SectionCategories,
                projection.CatalogHiddenSections,
                projection.ListedCategoryDoors,
                projection.SectionCostAnnotations,
                projection.ExactOnlySections,
                projection.OutputCapabilities
                ?? throw new InvalidOperationException(
                    $"{catalog} output capabilities are required."),
                sectionCardinalities:
                    projection.SectionCardinalities,
                sectionShapes: projection.SectionShapes);
        return structural is null
            ? null
            : ResourceExplanationCatalog.CreateStructural(
                structural.Document,
                structural.ResourcePaths);
    }

    private static ResourceExplanationCatalog CreatePackageQueryExplanation()
    {
        InspectionCapabilityCatalog catalog =
            CreatePackageQueryCapabilityCatalog();
        return ResourceExplanationCatalog.CreateCapabilities(
            catalog,
            PackageQueryCapabilityResourcePaths.Create(catalog));
    }

    private static ResourceExplanationCatalog CreatePackageFilesExplanation()
    {
        InspectionCapabilityCatalog catalog =
            CreatePackageFilesCapabilityCatalog();
        return ResourceExplanationCatalog.CreateCapabilities(
            catalog,
            PackageFileInventoryCapabilityResourcePaths.Create(catalog));
    }

    private static (
        InspectionCapabilityCatalog Catalog,
        ResourceExplanationCatalog Explanation)
        CreateCapabilityExplanation()
    {
        InspectionCapabilityCatalog packageQuery =
            CreatePackageQueryCapabilityCatalog();
        InspectionCapabilityCatalog packageFiles =
            CreatePackageFilesCapabilityCatalog();
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create(
                [
                    PackageQueryCapability.ProductModule,
                    PackageQueryCommandCapability.Module,
                    PackageFileInventoryCapability.ProductModule,
                    PackageFileInventoryCommandCapability.Module,
                ]);
        ResourceExplanationCatalog explanation =
            ResourceExplanationCatalog.CreateCapabilities(
                catalog,
                [
                    .. PackageQueryCapabilityResourcePaths.Create(
                        packageQuery),
                    .. PackageFileInventoryCapabilityResourcePaths.Create(
                        packageFiles),
                ]);
        return (catalog, explanation);
    }

    private static InspectionCapabilityCatalog
        CreatePackageQueryCapabilityCatalog() =>
        InspectionCapabilityCatalog.Create(
            [
                PackageQueryCapability.ProductModule,
                PackageQueryCommandCapability.Module,
            ]);

    private static InspectionCapabilityCatalog
        CreatePackageFilesCapabilityCatalog() =>
        InspectionCapabilityCatalog.Create(
            [
                PackageFileInventoryCapability.ProductModule,
                PackageFileInventoryCommandCapability.Module,
            ]);

    private static ResourceExplanationCatalog? CreateCompleteCatalog()
    {
        var structuralCatalogs = new List<ResourceExplanationCatalog>();
        foreach ((
                     StructuralViewIdentity view,
                     InspectionCatalogIdentity identity)
                 in ExplainableStructuralRoutes)
        {
            ResourceExplanationCatalog? catalog =
                CreateStructuralCatalog(view, identity);
            if (catalog is null)
                return null;
            structuralCatalogs.Add(catalog);
        }
        (
            _,
            ResourceExplanationCatalog capabilities) =
                CreateCapabilityExplanation();
        ResourceExplanationCatalog analyses =
            ResourceExplanationCatalog.CreateAnalyses(
                DiffAnalysisCommandCapability.Catalog);
        return ResourceExplanationCatalog.Combine(
            [
                .. structuralCatalogs,
                capabilities,
                analyses,
            ]);
    }

    private static ResourceExplanationCatalog CreateAnalysisExplanation()
    {
        (
            _,
            ResourceExplanationCatalog capabilities) =
                CreateCapabilityExplanation();
        ResourceExplanationCatalog analyses =
            ResourceExplanationCatalog.CreateAnalyses(
                DiffAnalysisCommandCapability.Catalog);
        return ResourceExplanationCatalog.Combine(
            capabilities,
            analyses);
    }

    private static int ExplainFromCatalog(
        ResourceExplanationCatalog catalog,
        ResourcePath path,
        int depth,
        int? maximumResults,
        OutputFormat format,
        bool envelopeOutput,
        string? outputPath)
    {
        if (!catalog.TryResolveExact(
                path,
                out ResourcePathResolution.Resolved? resolved))
        {
            return WriteCompleteResolutionFailure(path.Value);
        }

        if (maximumResults is not null)
        {
            CommandError.Write(
                "-n applies only when explain performs capability search.");
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

    private static int WriteCompleteResolutionFailure(string path)
    {
        ResourceExplanationCatalog completeCatalog =
            CreateCompleteCatalog()
            ?? throw new InvalidOperationException(
                "The complete resource explanation catalog could not "
                + "be built.");
        return WriteResolutionFailure(completeCatalog.Resolve(path));
    }

    private static string RootSegment(ResourcePath path)
    {
        int separator = path.Value.IndexOf('/');
        return separator < 0
            ? path.Value
            : path.Value[..separator];
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
                    if (format == OutputFormat.Table
                        && envelope.Content.MatchCount == 0)
                    {
                        output.WriteLine(
                            CapabilityCatalogSearchView.NoMatchesStatus);
                        return;
                    }

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

    /// <summary>
    /// The structural routes whose section catalogs <c>explain</c> publishes:
    /// library, package, the type listing, and the three member catalogs under
    /// the names <see cref="StructuralViewRegistry.CatalogPathName"/> issues.
    /// </summary>
    private static readonly
        (StructuralViewIdentity View, InspectionCatalogIdentity Catalog)[]
        ExplainableStructuralRoutes =
    [
        (StructuralViewIdentity.DirectLibrary, InspectionCatalogIdentity.Library),
        (StructuralViewIdentity.Package, InspectionCatalogIdentity.Package),
        (StructuralViewIdentity.Type, InspectionCatalogIdentity.ApiType),
        (StructuralViewIdentity.MemberType, InspectionCatalogIdentity.ApiMember),
        (StructuralViewIdentity.MemberTarget, InspectionCatalogIdentity.ApiMemberOverload),
        (StructuralViewIdentity.MemberTarget, InspectionCatalogIdentity.ApiMemberDetail),
    ];
}
