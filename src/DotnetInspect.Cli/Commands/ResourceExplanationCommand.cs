using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Commands;

public static class ResourceExplanationCommand
{
    public const string Name = "explain";

    internal static int ExplainPackageQuery(string[]? facetKeys = null) => ExplainFromCatalog(
        CreatePackageQueryExplanation(),
        PackageQueryCapabilityResourcePaths.QuerySpace,
        depth: 0,
        maximumResults: null,
        format: OutputFormat.Json,
        envelopeOutput: false,
        outputPath: null,
        contract: false,
        selection: ".hal",
        facetKeys: facetKeys is { Length: > 0 } ? facetKeys : null);

    public static int Execute(
        string? operand,
        int depth,
        bool depthExplicitlySet,
        int? maximumResults,
        OutputFormat format,
        bool envelopeOutput,
        bool noHeaders,
        string? outputPath,
        bool contract = false,
        string? selection = null,
        string[]? valueIds = null)
    {
        if ((contract || selection is not null) && format != OutputFormat.Json)
        {
            CommandError.Write(contract ? ".contract requires --json." : "Explanation data selections require --json.");
            return 1;
        }
        if (selection is not null && depthExplicitlySet)
        {
            CommandError.Write("Selected data closes over its reading dataset; --depth does not apply.");
            return 1;
        }
        const string resourcePrefix = "inspect-resource:/";
        // Bare explain reads the root, expanded through its entries unless
        // the caller chose a depth.
        if (operand is null && !depthExplicitlySet)
            depth = BareRootDepth;
        string normalizedOperand = (operand ?? RootSegmentName).Trim();
        bool resourceAddress = normalizedOperand.StartsWith(resourcePrefix, StringComparison.Ordinal);
        if (resourceAddress)
        {
            normalizedOperand = normalizedOperand[resourcePrefix.Length..];
            string? linkedProjection = normalizedOperand.EndsWith("?projection=hal", StringComparison.Ordinal)
                ? ".hal" : normalizedOperand.EndsWith("?projection=contract", StringComparison.Ordinal) ? ".contract" : null;
            if (linkedProjection is not null)
            {
                if (contract || selection is not null && selection != linkedProjection || depthExplicitlySet
                    || format != OutputFormat.Json)
                {
                    CommandError.Write("A projected resource address requires JSON and cannot override its projection or depth.");
                    return 1;
                }
                contract = linkedProjection == ".contract";
                selection = contract ? null : linkedProjection;
                normalizedOperand = normalizedOperand[..normalizedOperand.LastIndexOf('?')];
            }
        }
        if (valueIds is not null && selection is not (".data" or ".hal"))
        {
            CommandError.Write("-Q requires a vocabulary .data or .hal reading projection and JSON output.");
            return 1;
        }
        ResourcePath.TryCreate(
            normalizedOperand,
            out ResourcePath? canonicalPath,
            out _);
        if (valueIds is not null && (canonicalPath is null
            || RootSegment(canonicalPath) != ResourceExplanationCatalog.VocabulariesCollectionSegment
            || canonicalPath.Value.Split('/').Length != 2))
        {
            CommandError.Write("-Q value selection requires an exact vocabulary root.");
            return 1;
        }
        if (canonicalPath is not null)
        {
            string root = RootSegment(canonicalPath);
            foreach ((
                         InspectionCatalogIdentity identity,
                         StructuralViewIdentity[] views)
                     in ExplainableStructuralCatalogs)
            {
                if (StructuralViewRegistry.CatalogPathName(identity) != root)
                    continue;

                ResourceExplanationCatalog? structuralCatalog =
                    CreateStructuralCatalog(identity, views);
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
                    outputPath,
                    contract, selection, valueIds: valueIds);
            }

            if (IsRootPath(root))
            {
                if (CreatePublishedRootCatalog() is not { } rootCatalog)
                    return 1;
                if (format is OutputFormat.Table
                    or OutputFormat.Tsv
                    or OutputFormat.Jsonl)
                {
                    return WriteRoutingTable(
                        rootCatalog,
                        canonicalPath,
                        depthExplicitlySet,
                        maximumResults,
                        format,
                        envelopeOutput,
                        noHeaders,
                        outputPath);
                }
                return ExplainFromCatalog(
                    rootCatalog,
                    canonicalPath,
                    depth,
                    maximumResults,
                    format,
                    envelopeOutput,
                    outputPath,
                    contract, selection, valueIds: valueIds);
            }

            ResourceExplanationCatalog? exactCatalog = root switch
            {
                ResourceExplanationCatalog.ComponentContractsCollectionSegment =>
                    ResourceExplanationCatalog.CreateWorkspaceComponents(),
                "workspace" when canonicalPath.Value == WorkspacePackageComponentExplanation.Contract =>
                    ResourceExplanationCatalog.CreateWorkspaceComponents(),
                "package-query" => CreatePackageQueryExplanation(),
                "package-files" => CreatePackageFilesExplanation(),
                ResourceExplanationCatalog.AnalysesCollectionSegment =>
                    CreateAnalysisExplanation(),
                ResourceExplanationCatalog.VocabulariesCollectionSegment =>
                    ResourceExplanationCatalog.CreateVocabularies(
                        CliVocabularyComposition.Snapshot),
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
                    outputPath,
                    contract, selection, valueIds: valueIds);
            }

            if (canonicalPath.Value.Contains('/'))
            {
                return WriteCompleteResolutionFailure(normalizedOperand);
            }
        }

        if (resourceAddress)
            return WriteCompleteResolutionFailure(normalizedOperand);

        if (contract || selection is not null)
        {
            CommandError.Write("Data and contract selections apply only to exact Resource Explanation paths.");
            return 1;
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

        (
            InspectionCapabilityCatalog capabilityCatalog,
            ResourceExplanationCatalog capabilityExplanation) =
                CreateCapabilityExplanation();
        // Search reads the structural Discovery projections directly; it does
        // not build or combine the structural explanation graphs.
        InspectionEnvelope<CapabilityCatalogSearchDocument> search =
            CapabilityCatalogSearch.Search(
                capabilityCatalog,
                capabilityExplanation,
                CreateStructuralSearchCatalogs()
                ?? throw new InvalidOperationException(
                    "The structural search catalogs could not be built."),
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
        InspectionCatalogIdentity catalogIdentity,
        StructuralViewIdentity[] views) =>
        CreateStructuralProjection(catalogIdentity, views) is { } structural
            ? ResourceExplanationCatalog.CreateStructural(
                structural.Document,
                structural.ResourcePaths)
            : null;

    /// <summary>
    /// One structural catalog across every view that selects its sections,
    /// so a section any of those commands can address is explained once
    /// under the catalog root.
    /// </summary>
    private static DiscoveryDocumentFactory.Projection?
        CreateStructuralProjection(
            InspectionCatalogIdentity catalogIdentity,
            StructuralViewIdentity[] views) =>
        CreateDiscoveryProjection(
            StructuralViewRegistry.ProjectCatalog(
                Routes(catalogIdentity, views)));

    private static DiscoveryDocumentFactory.Projection?
        CreateDiscoveryProjection(StructuralSchemaProjection projection)
    {
        string catalog =
            StructuralViewRegistry.CatalogPathName(projection.Route.Catalog);
        return DiscoveryDocumentFactory.CreateProjection(
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
            sectionShapes: projection.SectionShapes,
            sectionDescriptions: projection.SectionDescriptions);
    }

    private static StructuralRoute[] Routes(
        InspectionCatalogIdentity catalogIdentity,
        StructuralViewIdentity[] views) =>
        [
            .. views.Select(view =>
                StructuralViewRegistry.Route(view, catalogIdentity)),
        ];

    /// <summary>
    /// The structural catalogs search admits: every explainable structural
    /// catalog, from the same projection its explanation catalog is created
    /// from, with one section-selection binding per view.
    /// </summary>
    internal static ImmutableArray<CapabilityCatalogSearchStructuralCatalog>?
        CreateStructuralSearchCatalogs()
    {
        var catalogs =
            ImmutableArray.CreateBuilder<
                CapabilityCatalogSearchStructuralCatalog>();
        foreach ((
                     InspectionCatalogIdentity identity,
                     StructuralViewIdentity[] views)
                 in ExplainableStructuralCatalogs)
        {
            if (CreateStructuralProjection(identity, views)
                is not { } structural)
            {
                return null;
            }
            var bindings =
                new List<CapabilityCatalogSearchStructuralBinding>();
            foreach (StructuralRoute route in Routes(identity, views))
            {
                StructuralSchemaProjection routeProjection =
                    StructuralViewRegistry.Project(route);
                if (CreateDiscoveryProjection(routeProjection)
                    is not { } routeDiscovery)
                {
                    return null;
                }
                bindings.Add(
                    StructuralSearchBinding(
                        route,
                        isFirst: bindings.Count == 0,
                        routeProjection,
                        routeDiscovery.Document));
            }
            catalogs.Add(StructuralSearchCatalog(
                identity,
                structural,
                bindings));
        }
        return catalogs.ToImmutable();
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

    /// <summary>
    /// The CLI's explanation root over its complete catalog, from the
    /// routing declarations of the owners the CLI admits. Bare
    /// <c>explain</c> reads the root at <see cref="RootSegment"/>; its role
    /// collections and entries are top-level paths. The CLI withholds the
    /// root while any resource is unreachable from it.
    /// </summary>
    internal static ResourceExplanationRootComposition? CreateRootComposition() =>
        CreateCompleteCatalog() is { } complete
            ? ResourceExplanationCatalog.ComposeRoot(
                complete,
                new ResourcePath(RootSegmentName),
                [
                    .. StructuralRoutingDeclarations.Create(),
                    .. CapabilityRoutingDeclarations.Create(),
                    .. VocabularyRoutingDeclarations.Create(),
                    ResourceExplanationCatalog.ComponentContractsRoutingDeclaration,
                ])
            : null;

    /// <summary>The explanation root's path, which bare <c>explain</c> reads.</summary>
    internal const string RootSegmentName = "root";

    /// <summary>
    /// The default traversal depth of bare <c>explain</c>: the root, its role
    /// collections, and every routing entry, in one Document.
    /// </summary>
    internal const int BareRootDepth = 2;

    private static bool IsRootPath(string root) =>
        root is RootSegmentName
            or ResourceExplanationCatalog.RoutingSubjectKindsSegment
            or ResourceExplanationCatalog.RoutingOperationsSegment
            or ResourceExplanationCatalog.RoutingReferencesSegment;

    /// <summary>
    /// The complete catalog with its root, or a visible error while the root
    /// is withheld because a resource is unreachable from it.
    /// </summary>
    private static ResourceExplanationCatalog? CreatePublishedRootCatalog()
    {
        ResourceExplanationRootComposition composition =
            CreateRootComposition()
            ?? throw new InvalidOperationException(
                "The complete resource explanation catalog could not "
                + "be built.");
        if (composition.IsPublishable)
            return composition.Catalog;

        CommandError.Write(
            "The explanation root is withheld: "
            + $"{composition.Unreachable.Length} installed resources are not "
            + "reachable from it, starting with "
            + $"'{composition.Unreachable[0].Value}'.");
        return null;
    }

    internal static ResourceExplanationCatalog? CreateCompleteCatalog()
    {
        var structuralCatalogs = new List<ResourceExplanationCatalog>();
        foreach ((
                     InspectionCatalogIdentity identity,
                     StructuralViewIdentity[] views)
                 in ExplainableStructuralCatalogs)
        {
            ResourceExplanationCatalog? catalog =
                CreateStructuralCatalog(identity, views);
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
                ResourceExplanationCatalog.CreateVocabularies(
                    CliVocabularyComposition.Snapshot),
                ResourceExplanationCatalog.CreateWorkspaceComponents(),
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
        string? outputPath,
        bool contract,
        string? selection,
        IEnumerable<string>? facetKeys = null,
        string[]? valueIds = null)
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
            outputPath,
            contract, selection, facetKeys, valueIds);
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
        string? outputPath,
        bool contract,
        string? selection,
        IEnumerable<string>? facetKeys = null,
        string[]? valueIds = null)
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

        if (selection is not null)
        {
            try
            {
                ResourceExplanationDataset dataset = ResourceExplanationDataset.Create(catalog, resolution, facetKeys,
                    valueIds: valueIds is { Length: > 0 } ? valueIds : null);
                JsonElement data = dataset.ToJson(static path => "inspect-resource:/" + path.Value,
                    hal: selection == ".hal",
                    bindHalAddress: static path => "inspect-resource:/" + path.Value + "?projection=hal");
                OutputDestination.Write(outputPath, rowWindow: null,
                    output => output.WriteLine(data.GetRawText()));
                return 0;
            }
            catch (InvalidOperationException exception)
            {
                CommandError.Write(exception.Message);
                return 1;
            }
        }
        InspectionEnvelope<ResourceExplanationDocument> explanation =
            catalog.Explain(
                resolution,
                ResourceExplanationRequest.ForHost(depth));
        ResourceExplanationDocument document = explanation.Content;
        OutputDestination.Write(
            outputPath,
            rowWindow: null,
            output =>
            {
                if (format == OutputFormat.Json)
                {
                    output.WriteLine(
                        contract
                            ? JsonSerializer.Serialize(document,
                                ResourceExplanationJsonContext.Default.ResourceExplanationDocument)
                            : ResourceExplanationDataProjection.Create(document,
                                static path => "inspect-resource:/" + path.Value).GetRawText());
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

    /// <summary>
    /// The routing-table selection of a root path: one row per routing entry
    /// the path reaches. The root reaches every entry, a collection its own,
    /// and an entry itself, so traversal depth does not apply.
    /// </summary>
    private static int WriteRoutingTable(
        ResourceExplanationCatalog catalog,
        ResourcePath path,
        bool depthExplicitlySet,
        int? maximumResults,
        OutputFormat format,
        bool envelopeOutput,
        bool noHeaders,
        string? outputPath)
    {
        if (depthExplicitlySet || maximumResults is not null || envelopeOutput)
        {
            CommandError.Write(
                "The routing table selects every entry the path reaches; "
                + "--depth, -n, and --envelope do not apply. Add --markdown "
                + "or --json for the Document reading.");
            return 1;
        }
        if (!catalog.TryResolveExact(
                path,
                out ResourcePathResolution.Resolved? resolved))
        {
            return WriteResolutionFailure(catalog.Resolve(path.Value));
        }

        int depth = path.Value.Count(static character => character == '/')
            switch
            {
                0 when path.Value == RootSegmentName => BareRootDepth,
                0 => 1,
                _ => 0,
            };
        ResourceExplanationDocument document = catalog.Explain(
                resolved,
                ResourceExplanationRequest.ForHost(depth))
            .Content;
        OutputDestination.Write(
            outputPath,
            rowWindow: null,
            output => MarkoutSerializer.Serialize(
                new ResourceExplanationRoutingTableView
                {
                    Routing = ResourceExplanationRoutingRow.Create(document),
                },
                output,
                new TableFormatter(!noHeaders),
                ResourceExplanationViewContext.Default,
                OutputFormatter.CreateTableWriterOptions(
                    tsv: format == OutputFormat.Tsv,
                    jsonl: format == OutputFormat.Jsonl)));
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
    /// Admits one explained structural catalog to capability search: its
    /// catalog root as the owning route, and each view's <c>-S</c> section
    /// selection as a CLI binding over the sections that view addresses. The
    /// section or category name stays a separate result value; search
    /// composes no command line.
    /// </summary>
    private static CapabilityCatalogSearchStructuralCatalog
        StructuralSearchCatalog(
            InspectionCatalogIdentity identity,
            DiscoveryDocumentFactory.Projection structural,
            IEnumerable<CapabilityCatalogSearchStructuralBinding> bindings)
    {
        string root = StructuralViewRegistry.CatalogPathName(identity);
        return new(
            structural.Document,
            structural.ResourcePaths,
            new CapabilityCatalogSearchRoute(
                $"dotnet-inspect.cli/{root}-catalog",
                root,
                root),
            bindings);
    }

    /// <summary>
    /// One view's section-selection binding over the Sections it selects on
    /// its own and the Categories that contain one. A catalog's first view
    /// keeps the catalog's binding identity; a later view adds its view mode,
    /// as in <c>library-address-sections</c>.
    /// </summary>
    private static CapabilityCatalogSearchStructuralBinding
        StructuralSearchBinding(
            StructuralRoute route,
            bool isFirst,
            StructuralSchemaProjection projection,
            DiscoveryDocument addressed)
    {
        string root = StructuralViewRegistry.CatalogPathName(route.Catalog);
        string name = isFirst ? root : $"{root}-{route.View.ViewMode}";
        IReadOnlySet<string> standalone =
            StructuralViewRegistry.StandaloneSelectableSections(projection);
        return new(
            new CapabilityCatalogSearchBinding(
                $"dotnet-inspect.cli/{name}-sections",
                "dotnet-inspect CLI",
                InspectionConsumerKind.Cli,
                $"{route.View.DestinationCommand} -S",
                root),
            addressed.Resources
                .Select(static resource => resource.Identity)
                .Where(resource => resource.Kind switch
                {
                    DiscoveryResourceKind.Section =>
                        standalone.Contains(resource.Name),
                    DiscoveryResourceKind.Category =>
                        projection.SectionCategories.TryGetValue(
                            resource.Name,
                            out string[]? sections)
                        && sections.Any(standalone.Contains),
                    _ => false,
                }));
    }

    /// <summary>
    /// The structural catalogs <c>explain</c> publishes, each with the views
    /// that select its sections: library (direct and by address), package,
    /// the type listing, and the three member catalogs under the names
    /// <see cref="StructuralViewRegistry.CatalogPathName"/> issues. A
    /// catalog's first view supplies its projection's route.
    /// </summary>
    private static readonly
        (InspectionCatalogIdentity Catalog, StructuralViewIdentity[] Views)[]
        ExplainableStructuralCatalogs =
    [
        (
            InspectionCatalogIdentity.Library,
            [StructuralViewIdentity.DirectLibrary, StructuralViewIdentity.LibraryAddress]),
        (InspectionCatalogIdentity.Package, [StructuralViewIdentity.Package]),
        (InspectionCatalogIdentity.ApiType, [StructuralViewIdentity.Type]),
        (InspectionCatalogIdentity.ApiMember, [StructuralViewIdentity.MemberType]),
        (InspectionCatalogIdentity.ApiMemberOverload, [StructuralViewIdentity.MemberTarget]),
        (InspectionCatalogIdentity.ApiMemberDetail, [StructuralViewIdentity.MemberTarget]),
    ];
}
