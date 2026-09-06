using DotnetInspector.Core;
using DotnetInspector.MetadataRendering;
using DotnetInspector.Models;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using ILInspector.Research;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Planning;
using DotnetInspector.Queries;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using SignatureVerificationResult = DotnetInspector.Services.SignatureVerificationResult;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InertText;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects a single .NET assembly.
/// </summary>
public class LibraryCommand
{
    internal static DocumentSchema CreateStructuralSchema()
        => MetadataSectionNames.AugmentSchema(
            InspectionContext.Default
                .GetSchemaInfo<LibraryInspectionView>()!
                .ToDocumentSchema());

    internal static StructuralSectionInput GetStructuralSectionInput(
        string section)
        => ILCoordinateSections.Contains(
                section,
                StringComparer.OrdinalIgnoreCase)
            ? StructuralSectionInput.IlCoordinate
            : section.Equals(
                MetadataSectionNames.Heap,
                StringComparison.OrdinalIgnoreCase)
                ? StructuralSectionInput.HeapCoordinate
                : section.Equals(
                    SectionNames.BodyShapes,
                    StringComparison.OrdinalIgnoreCase)
                    ? StructuralSectionInput.BodyKindFilter
                    : StructuralSectionInput.None;

    /// <summary>
    /// Discovery must know which metadata tables carry rows, or the whole <c>@Metadata</c> category
    /// filters out of the catalog: its sections are explicit-only, so no verbosity requests them,
    /// and their applicability is the scanned row count. The scan is deliberately the cheap half of
    /// the lens — table row counts, never rows — so listing the category accurately costs a header
    /// read rather than a projection.
    ///
    /// Passed into <see cref="SectionPipeline{TModel}.GetRequiredQueries"/> rather than added to its result,
    /// so the one method that computes the requested set is also the one that records it.
    /// </summary>
    internal static readonly HostQueryDemand[] DiscoveryQueries =
    [
        new("discovery catalog", MetadataImageQuery.Definition),
        new("References applicability", AssemblyReferencesQuery.Definition),
    ];

    internal static readonly HostQueryDemand[]
        BareDiscoveryQueries =
        [
            new("Unsafe Members applicability",
                UnsafeEvidencePresenceQuery.Definition),
        ];

    public static async Task<int> ExecuteAsync(LibraryOptions options)
    {
        if (!options.Trace)
            return await ExecuteCoreAsync(options, trace: null);

        // Rendered in a finally so a failed run still reports the work it did before failing —
        // which is exactly when "what did this actually scan?" is worth knowing.
        var trace = new InspectionTrace
        {
            Command = new InertString(TextPolicy.Field, "library"),
            Target = new InertString(
                TextPolicy.Field,
                Path.GetFileName(
                    options.AssemblyName
                        ?? options.PackagePath
                        ?? options.PlatformAssembly
                        ?? string.Empty)),
        };

        try
        {
            return await ExecuteCoreAsync(options, trace);
        }
        finally
        {
            // The trace interpolates untrusted text -- Target is argv, and resource details
            // name paths and package entries -- so it goes to the stream the way every other
            // stderr line does. Contained per line rather than per field: deciding which trace
            // fields are untrusted is the enumeration issue #3319 abandoned, and a field added
            // later would silently miss it.
            foreach (var line in trace.RenderLines())
                CommandError.WriteLine(line);
        }
    }

    /// <summary>
    /// Converts bare <c>-S</c> into the library pipeline's fixed, network-free overview while
    /// preserving explicit selectors and higher user-selected verbosity.
    /// </summary>
    internal static LibraryOptions NormalizeBareSelect(
        LibraryOptions options)
    {
        if (options.Discover != null || !options.SelectDefault)
            return options;

        options = options with { SelectDefault = false };
        return options.Select is null
            && options.Verbosity == Verbosity.Minimal
                ? options with
                {
                    Verbosity = Verbosity.Normal,
                    FixedOverview = true,
                }
                : options;
    }

    private static async Task<int> ExecuteCoreAsync(LibraryOptions options, InspectionTrace? trace)
    {
        if (options.IntegrationQuery.HasFilter
            && (options.BodyKindQuery.HasFilter || options.PerformanceTriage.HasFilters
                || options.PerformanceTriage.HasRanking))
        {
            CommandError.Write(
                "Integration ecosystem queries cannot be combined with Body Shapes or Performance Triage predicates/ranking.");
            return 1;
        }
        if (options.IntegrationQuery.HasFilter
            && (options.ILOffsetParameter is not null || options.ILOffsetsPath is not null
                || options.HeapParameter is not null || options.ExtractResources is not null
                || options.Print || options.Value || options.Urls || options.Paths))
        {
            CommandError.Write(
                "Integration ecosystem queries support section rows, columns, and counts, not coordinate or extraction operations.");
            return 1;
        }
        var assemblyPath = options.AssemblyName;
        var catalog = LibrarySections.CreateCatalog();
        var sections = catalog.Sections;
        var pipeline = catalog.Pipeline;
        var queryCatalog = catalog.QueryCatalog;
        var groupQueryCatalog = catalog.GroupQueryCatalog;

        var schemaMap = CreateStructuralSchema();
        bool hasInputSource = !string.IsNullOrEmpty(assemblyPath)
            || !string.IsNullOrEmpty(options.PackagePath)
            || !string.IsNullOrEmpty(options.PlatformAssembly);

        // Hex table aliases are resolved before anything reads a selector — including the static
        // discovery return below — so every consumer of Select/Discover sees canonical names. That
        // placement is the invariant the alias rests on, not an optimization: adversarial review of
        // #3510 found the normalizer sitting below this branch, where `-D "Metadata: 0x02"
        // --schema` returned "not found" while the effective-discovery path resolved it.
        var aliasNormalized = NormalizeMetadataTableAliases(options);
        if (aliasNormalized.Error is not null)
        {
            CommandError.Write(aliasNormalized.Error);
            return 1;
        }
        options = aliasNormalized.Options;
        options = NormalizeReferenceProjection(options);
        if (options.MetadataRoot is not null
            && !string.IsNullOrWhiteSpace(options.ILOffsetsPath))
        {
            CommandError.Write("--metadata-root cannot be combined with --il-offsets.");
            return 1;
        }

        if (options.MetadataRoot is not null
            && options.Discover is null
            && options.Select is not { Length: > 0 }
            && string.IsNullOrWhiteSpace(options.HeapParameter))
        {
            options = options with { Select = [MetadataSectionNames.Image] };
        }

        if (GetDiscoveryModeError(
                options.Effective,
                options.Discover is not null,
                options.Schema) is { } discoveryModeError)
        {
            CommandError.Write(discoveryModeError);
            return 1;
        }

        if (options.Discover is not null && options.Schema)
        {
            return StructuralViewRegistry.Execute(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.DirectLibrary,
                    InspectionCatalogIdentity.Library),
                StructuralDiscoveryRequest.From(options));
        }

        // Schema and named discovery are structural by default. They describe the catalog without
        // resolving the target or running producers. Bare -D with a target is the cheap,
        // target-aware orientation gesture; --effective opts named or bare discovery into producer
        // execution.
        if (options.Discover != null)
        {
            bool requiresInspection = hasInputSource
                && !options.Schema
                && (options.Effective
                    || options.Discover.Length == 0
                    || HasILOffsetCoordinate(options)
                    || HasHeapCoordinate(options));
            if (requiresInspection)
            {
                // Handled after data collection below.
            }
            else
            {
                return DiscoverOutput.Execute(options.Discover, schemaMap,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
                    verbosity: (int)options.Verbosity,
                    sectionCostAnnotations: pipeline.GetCostAnnotations(),
                    sectionCategories: sections.SelectionCategoryMap,
                    // --schema reveals every registered section. Structural category drill-down
                    // keeps the curated top-level scope when no target inspection is requested.
                    catalogHiddenSections: options.Schema ? null : pipeline.GetCatalogHiddenSections(),
                    listedCategoryDoors: pipeline.GetListedCategoryDoors(),
                    projection: options);
            }
        }

        // Bare -S selects the network-free "fixed" overview: only sections whose declared growth
        // class is Fixed and whose cost is NetworkFree, so the rendered set is structurally
        // identical for every package (absence means "not applicable", never "too long for this
        // package"). This still includes the symbol-dependent fact tables (Symbols, Signals)
        // because they read an embedded, adjacent, or already-cached PDB without touching the
        // network. Consume the marker so it never resolves as a section set; keep display verbosity
        // at Normal so the cache-only PDB read stays enabled (never downgrading a higher verbosity
        // the user asked for, in which case the normal curated ladder applies instead of the fixed
        // overview). Combined with an explicit selector the explicit selection wins and the marker
        // is dropped. See #3547.
        options = NormalizeBareSelect(options);

        bool discoveryInspection = options.Discover != null && !options.Schema && hasInputSource;
        bool fullEffectiveDiscovery = discoveryInspection && options.Effective;
        var userVerbosity = options.Verbosity; // preserve for display formatting
        options = options with { UserVerbosityOverride = userVerbosity };
        if (fullEffectiveDiscovery)
            options = options with { Verbosity = Verbosity.Detailed };

        var normalized = NormalizeILOffsetSelection(options);
        if (normalized.Error is not null)
        {
            CommandError.Write(normalized.Error);
            return 1;
        }
        options = normalized.Options;

        var heapNormalized = NormalizeHeapSelection(options);
        if (heapNormalized.Error is not null)
        {
            CommandError.Write(heapNormalized.Error);
            return 1;
        }
        options = heapNormalized.Options;

        // --effective with a named section/category scopes producer execution to that structural
        // selection. Bare --effective is scoped separately to the base-category union so it cannot
        // implicitly run unrelated domains.
        if (fullEffectiveDiscovery && options.Discover is { Length: > 0 })
        {
            var discoverResult = SelectResolver.ResolveSelectAsSections(
                options.Discover, sections.SelectableSectionNames, sections.InfoSectionNames,
                sections.SelectionCategoryMap, selectDefault: false);
            if (SelectOutput.WriteUnresolved(discoverResult))
                return 1;
            options = options with { IncludeSections = discoverResult.Sections };
        }

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select, sections.SelectableSectionNames, sections.InfoSectionNames,
            sections.SelectionCategoryMap,
            selectDefault: options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selectResult)) return 1;
        if (selectResult.Sections != null)
        {
            if (ApplyCoordinateSectionRequirements(
                    options,
                    selectResult) is { } coordinateError)
            {
                CommandError.Write(coordinateError);
                return 1;
            }

            options = options with
            {
                IncludeSections = selectResult.Sections,
                ExactIncludeSectionsOverride = selectResult.ExactSections,
            };
        }

        if (options.Discover is null || fullEffectiveDiscovery)
        {
            if (options.IntegrationQuery.HasFilter)
            {
                string[] integrationSections =
                    [.. LibraryIntegrationCatalog.CategorySections, IntegrationSectionNames.Opportunities];
                if (options.IncludeSections is not { Count: > 0 })
                {
                    options = options with
                    {
                        IncludeSections = [.. integrationSections],
                        FixedOverview = false,
                    };
                }
                else if (!options.IncludeSections.Overlaps(integrationSections))
                {
                    CommandError.Write(
                        "--where ecosystem=... targets Integrations. Omit -S or include an Integration section.");
                    return 1;
                }
            }
            bool bodyShapesSelected =
                options.IncludeSections?.Contains(SectionNames.BodyShapes) == true;
            if (options.BodyKindQuery.HasFilter
                && options.PerformanceTriage.HasRanking)
            {
                CommandError.Write(
                    "Body Shapes composition accepts Performance Triage filters, "
                    + "but not --top or --order-by. Use --rows to limit rendered matches.");
                return 1;
            }
            if (options.BodyKindQuery.HasFilter
                && options.IncludeSections is not { Count: > 0 })
            {
                options = options with
                {
                    IncludeSections =
                    [
                        SectionNames.BodyShapes,
                    ],
                    ExactIncludeSectionsOverride =
                    [
                        SectionNames.BodyShapes,
                    ],
                };
                bodyShapesSelected = true;
            }
            if (options.BodyKindQuery.HasFilter && !bodyShapesSelected)
            {
                CommandError.Write(
                    $"--where Kind=... targets section '{SectionNames.BodyShapes}'. "
                    + $"Omit -S or include -S \"{SectionNames.BodyShapes}\".");
                return 1;
            }
            if (bodyShapesSelected && !options.BodyKindQuery.HasFilter)
            {
                CommandError.Write(
                    $"Section '{SectionNames.BodyShapes}' requires "
                    + "--where \"Kind=<C# Body Kinds ID>\".");
                return 1;
            }
        }

        if (options.MetadataRoot is not null
            && options.Discover is null
            && options.IncludeSections?.Any(section =>
                MetadataSectionNames.IsMetadataSection(section)
                && section != MetadataSectionNames.ReadyToRun) != true)
        {
            CommandError.Write(
                "--metadata-root requires a metadata-root section. Omit -S or select a metadata image, table, or heap section.");
            return 1;
        }

        if (options.JsonOutput
            && !options.Count
            && options.Discover is null
            && (options.MetadataRoot is not null
                || options.IncludeSections?.Contains(MetadataSectionNames.ReadyToRun) == true))
        {
            CommandError.Write(
                "Metadata root and ReadyToRun rows do not support --json. Use --jsonl or --tsv for structured rows; --count and discovery support --json.");
            return 1;
        }

        options = options with
        {
            UserIncludeSectionsOverride = options.IncludeSections is { Count: > 0 }
                ? new HashSet<string>(options.IncludeSections, StringComparer.OrdinalIgnoreCase)
                : [],
        };

        if (options.ReferenceTreeDepth is < 1)
        {
            CommandError.Write("--depth must be at least 1.");
            return 1;
        }

        if (options.ReferenceTreeDepth is not null
            && (options.Discover != null || !options.Tree))
        {
            CommandError.Write("--depth requires -S References --tree.");
            return 1;
        }

        if (!ValidateMultiTfmOutput(options))
            return 1;

        if (!ValidateReferenceTreeCount(
                options.Tree, options.Count, options.IncludeSections))
            return 1;

        if (options.Tree && options.Discover == null && !options.Count)
        {
            if (options.IncludeSections is not { Count: 1 }
                || !options.IncludeSections.Contains(SectionNames.References))
            {
                CommandError.Write("--tree requires exactly one tree-shaped section (-S References).");
                return 1;
            }
        }

        if (options.Tree && options.Discover == null && !options.Count)
        {
            if (options.Print
                || options.Value
                || options.Urls
                || options.Paths
                || options.Columns is { Length: > 0 }
                || options.Fields is { Length: > 0 }
                || options.Rows is not null
                || options.JsonOutput
                || options.PlainText
                || options.TabularExplicitlySet)
            {
                CommandError.Write("--tree cannot be combined with row projections or non-Markdown formats.");
                return 1;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.HeapParameter)
            && options.IncludeSections is { Count: > 0 }
            && !options.IncludeSections.Contains(MetadataSectionNames.Heap))
        {
            CommandError.Write($"--heap requires the heap coordinate section. Omit -S or include -S \"{MetadataSectionNames.Heap}\".");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            && options.IncludeSections is { Count: > 0 }
            && !options.IncludeSections.Overlaps(ILCoordinateSections))
        {
            CommandError.Write($"--il-offset requires an IL coordinate section. Omit -S or include -S \"{SectionNames.ILOffset}\", -S \"{SectionNames.MemberContext}\", -S \"{SectionNames.InstructionContext}\", -S \"{SectionNames.ExceptionContext}\", -S \"{SectionNames.CallsiteContext}\", or -S \"{SectionNames.ReturnAddressContext}\".");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            && !string.IsNullOrWhiteSpace(options.ILOffsetsPath))
        {
            CommandError.Write("--il-offset cannot be combined with --il-offsets.");
            return 1;
        }

        // --il-offsets counts resolved coordinate rows, not section rows, so it does not need a
        // section filter to make --count meaningful.
        var ilOffsetsBatchMode = !string.IsNullOrWhiteSpace(options.ILOffsetsPath);
        if (ilOffsetsBatchMode && options.SelectExplicitlySet)
        {
            CommandError.Write(
                "-S/--select is not available with --il-offsets, which renders "
                + "its own payload rather than sections.");
            return 1;
        }

        // Discovery renders its own rows, so a section requirement describes a filter it does
        // not use. -S still narrows effective discovery, so it stays permitted.
        var rendersOwnPayload = ilOffsetsBatchMode || options.Discover != null;

        if (!rendersOwnPayload && options.Count)
        {
            if (!CountOutput.ValidateSectionsSelected(options.IncludeSections, options.FixedOverview))
                return 1;

            var ordered = OutputFormatter.ResolveCountMapSections(
                pipeline, options.IncludeSections, options.FixedOverview);
            if (!CountOutput.ValidateMapFormat(
                    options.Format, ordered, options.Tree))
                return 1;
        }

        if (options.Count && options.Print)
        {
            CommandError.Write("--count cannot be combined with --print.");
            return 1;
        }

        var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
        if (shapeCount > 1)
        {
            CommandError.Write("specify only one of --value, --urls, or --paths.");
            return 1;
        }

        if (shapeCount == 1)
        {
            var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
            // The batch path refuses shape projections with an accurate reason; a section
            // requirement reported first would not be the actual problem.
            if (!rendersOwnPayload && !ShapeProjectionOutput.ValidateSingleSection(options.IncludeSections, optionName))
                return 1;
            if (options.Count || options.Print)
            {
                CommandError.Write($"{optionName} cannot be combined with --count or --print.");
                return 1;
            }
            if (options.Rows is not null)
            {
                CommandError.Write($"--rows cannot be combined with {optionName}; use -n N to limit projected output lines or --row N|first|last to select a projected row.");
                return 1;
            }
        }

        if (options.JsonArray && shapeCount == 0 && !options.Print)
        {
            CommandError.Write("--json-array requires --value, --urls, --paths, or --print.");
            return 1;
        }

        if (options.JsonArray && (options.JsonOutput || options.Jsonl))
        {
            CommandError.Write("--json-array cannot be combined with --json or --jsonl.");
            return 1;
        }

        if (options.Print && !rendersOwnPayload && !ValidateLibraryPrintSelection(options.IncludeSections))
            return 1;

        if (options.Print && options.Rows is not null)
        {
            CommandError.Write("--rows cannot be combined with --print; use --row N|first|last to choose a printed row.");
            return 1;
        }

        if (options.ProjectionRow is not null && !options.Print && shapeCount == 0)
        {
            CommandError.Write("--row requires --print, --value, --urls, or --paths.");
            return 1;
        }

        // -S targeting specific sections: promote verbosity to ensure data collection
        var requiredVerbosity = pipeline.GetRequiredVerbosity(options.IncludeSections);
        if (requiredVerbosity > options.Verbosity)
            options = options with { Verbosity = requiredVerbosity };

        // Pre-render validation: check --fields/--columns names against the section schema.
        // Bare -S carries its selection through FixedOverview rather than IncludeSections.
        IReadOnlyCollection<string>? projectionSections =
            options.IncludeSections is { Count: > 0 } includeSections
                ? includeSections
                : options.FixedOverview
                    ? pipeline.BareSelectSectionNames
                    : null;
        if ((options.Fields is { Length: > 0 }
                || options.Columns is { Length: > 0 })
            && options.Discover == null
            && projectionSections is { Count: > 0 }
            && !ProjectionDiagnostics.ValidateProjection(
                schemaMap,
                projectionSections,
                options.Fields,
                options.Columns))
        {
            return 1;
        }

        if (options.Discover == null
            && !options.Count
            && !OutputFormatResolver.ValidateSingleSectionForTabular(
                options.TabularExplicitlySet, options.IncludeSections))
            return 1;

        // Warn if tabular output is combined with detailed verbosity without section selector
        if (!discoveryInspection && !options.Count)
            OutputFormatResolver.WarnIfTabularDetailMismatch(options.Tabular, options.Verbosity, options.IncludeSections);

        // Cheap discovery runs only the command-level presence probes. Full discovery executes the
        // requested sections; bare full discovery is bounded to the base-category union.
        HashSet<string>? discoveryExecutionScope = options.IncludeSections;
        if (fullEffectiveDiscovery && discoveryExecutionScope is not { Count: > 0 })
            discoveryExecutionScope = [.. sections.BaseSectionNames];
        bool useEffectiveDiscoveryCache = fullEffectiveDiscovery
            && options.Discover is { Length: 0 }
            && options.UserIncludeSections is not { Count: > 0 }
            && !HasILOffsetCoordinate(options)
            && !HasHeapCoordinate(options)
            && options.MetadataRoot is null;

        if (trace is not null)
            trace.Verbosity = new InertString(TextPolicy.Field, options.Verbosity.ToString());
        List<HostQueryDemand> commandQueryDemand = [];
        if (discoveryInspection)
        {
            commandQueryDemand.AddRange(DiscoveryQueries);
            if (options.Discover is { Length: 0 })
                commandQueryDemand.AddRange(BareDiscoveryQueries);
        }
        if (options.CollectReferenceTree)
        {
            commandQueryDemand.Add(
                new HostQueryDemand(
                    "reference tree",
                    AssemblyReferencesQuery.Definition));
        }
        SectionQueryPlan sectionPlan = sections.PlanQueries(
            discoveryInspection && !fullEffectiveDiscovery
                ? Verbosity.Quiet
                : options.Verbosity,
            discoveryInspection && !fullEffectiveDiscovery
                ? []
                : discoveryExecutionScope,
            discoveryInspection && !fullEffectiveDiscovery
                ? false
                : options.FixedOverview);
        if (sectionPlan.Queries.Contains(BodyShapesQuery.Definition)
            && options.BodyKindQuery.HasFilter
            && options.PerformanceTriage.HasCandidateFilters)
        {
            commandQueryDemand.Add(
                new HostQueryDemand(
                    "Body Shapes performance predicates",
                    OptimizationOpportunitiesQuery.Definition));
        }

        HashSet<InspectionQueryDefinition> queries =
            sectionPlan.Activate(trace, commandQueryDemand);
        var inspectionOptions = fullEffectiveDiscovery
            && options.IncludeSections is not { Count: > 0 }
            ? options with { IncludeSections = discoveryExecutionScope }
            : options;
        if (discoveryInspection)
        {
            inspectionOptions = inspectionOptions with
            {
                // References effectiveness is established from direct metadata. The explicit
                // identifier audit is different: full-effective discovery must run the same
                // closure that decides whether that section has rows.
                CollectReferenceTree = false,
                CollectIdentifierConfusionReferenceTree =
                    fullEffectiveDiscovery
                    && discoveryExecutionScope?.Contains(
                        SectionNames.IdentifierConfusion) == true,
            };
        }
        else
        {
            var candidates = pipeline.GetCandidateSections(
                options.Verbosity, options.IncludeSections, options.FixedOverview);
            inspectionOptions = inspectionOptions with
            {
                CollectReferenceTree =
                    options.Tree && candidates.Contains(SectionNames.References),
                CollectIdentifierConfusionReferenceTree =
                    candidates.Contains(SectionNames.IdentifierConfusion),
            };
        }

        // Check for valid input source
        if (string.IsNullOrEmpty(assemblyPath) &&
            string.IsNullOrEmpty(options.PackagePath) &&
            string.IsNullOrEmpty(options.PlatformAssembly))
        {
            CommandError.Write("Library path, package name, or --platform required.");
            CommandError.WriteLine("Run 'dotnet-inspect library --help' for usage.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            // Parse package name and version for symbol package download
            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = PackageExtractor.ParsePackageReference(options.PackagePath);
            }

            if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                var (resolvedPath, framework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                    options.PlatformAssembly,
                    context.HttpClient,
                    logger.Log,
                    options.PlatformFramework,
                    useRuntimeAssemblies: true,
                    platformVersion: options.PlatformVersion,
                    sourceOptions: options.SourceOptions);

                if (error != null)
                {
                    CommandError.Write($"{error}");
                    return 1;
                }

                logger.Log($"Using platform runtime library: {framework} {version}");

                AssemblyResolutionProvenance inspectionProvenance =
                    AssemblyResolutionProvenance.Platform(
                        framework!,
                        version,
                        "library --platform");
                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                {
                    LibraryInspectionSubject? coordinateSubject =
                        SelectInspectionSubjectOrReportFailure(
                            resolvedPath!,
                            inspectionProvenance);
                    if (coordinateSubject is null)
                        return 1;

                    return await WriteILCoordinateBatchAsync(
                        coordinateSubject,
                        null,
                        null,
                        isPlatformAssembly: true,
                        options,
                        context.HttpClient,
                        logger);
                }

                AssemblyContextIntegrationsBatch? integrations =
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        groupQueryCatalog,
                        [
                            new AssemblyContextIntegrationsInput(
                                resolvedPath!,
                                inspectionProvenance),
                        ],
                        trace);
                LibraryInspectionSubject? subject =
                    SelectInspectionSubjectOrReportFailure(
                        resolvedPath!,
                        inspectionProvenance,
                        integrations?.AssemblyForInspection(resolvedPath!));
                if (subject is null)
                    return 1;

                // Network-free SourceLink availability probe: drives the SourceLink section
                // family in -D and keys the effective cache so a warmed/cleared PDB busts a
                // stale catalog. Skipped (false) outside discovery.
                bool sourceLinkAvailable = fullEffectiveDiscovery && !HasILOffsetCoordinate(options)
                    && await ProbeLocalSourceLinkAsync(
                        subject,
                        context.HttpClient,
                        logger,
                        isPlatformAssembly: true,
                        sourceOptions: options.SourceOptions);

                // Identity of the bytes about to be inspected. Computed once and reused for the
                // lookup, the pre-inspection snapshot, and (via CacheEffective) the write, so a
                // discovery run hashes the assembly at most twice.
                string? inspectedContentHash = fullEffectiveDiscovery ? TryGetContentHash(resolvedPath!) : null;

                // Check effective sections cache before running full inspection
                if (useEffectiveDiscoveryCache && inspectedContentHash != null)
                {
                    var cached = TryGetCachedEffective(resolvedPath!, inspectedContentHash, sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(resolvedPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, pipeline, userVerbosity, rootLabel);
                    }
                }

                InspectionQueryPlan<InspectionQueryContext> queryPlan =
                    queryCatalog.Plan(queries);
                var inspection = await LibraryMetadataService.InspectAsync(
                    resolvedPath!, inspectionOptions, logger, null, null, context.HttpClient,
                    isPlatformAssembly: true,
                    queryPlan: queryPlan,
                    assemblyReference: subject.AssemblyReference,
                    integrationsEntry: integrations?.EntryFor(resolvedPath!),
                    integrationOpportunitiesEntry:
                        integrations?.OpportunitiesEntryFor(resolvedPath!),
                    discoveryOnly: discoveryInspection && !fullEffectiveDiscovery, trace: trace);
                if (inspection == null)
                {
                    CommandError.Write($"Could not read library: {resolvedPath}");
                    return 1;
                }

                inspection.Source = SourceKind.Platform;
                inspection.PlatformVersion = version;
                if (RejectFailedExactIdentifierAudit(
                        inspection,
                        options))
                {
                    return 1;
                }

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspection, subject, null, null, isPlatformAssembly: true,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                int heapExitCode = PopulateMetadataSelection(inspection, options, logger);
                if (heapExitCode != 0)
                    return heapExitCode;
                if (discoveryInspection)
                    return WriteEffectiveSections(
                        resolvedPath!, inspection, options, pipeline, userVerbosity,
                        fullEffectiveDiscovery, discoveryExecutionScope, sourceLinkAvailable,
                        cache: useEffectiveDiscoveryCache,
                        inspectedContentHash: inspectedContentHash);
                if (options.Print)
                    return await WriteLibraryPrintProjectionAsync(inspection, options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspection, options);
                if (RejectEmptyExactSection(inspection, options, pipeline))
                    return 1;
                WarnEmptySections(inspection, options, pipeline);
                ExtractResourcesIfRequested(resolvedPath!, options);
                if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                    return 1;

                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                return Math.Max(
                    IntegrityExitCode(inspection),
                    SelectedInspectionFailureExitCode(
                        options,
                        pipeline,
                        inspection));
            }
            else if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(
                    assemblyPath, options.PackagePath, options.Tfm,
                    options.SourceOptions, options.IncludePrerelease, logger, context.HttpClient);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath, extractTempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion) = extractResult.Value;
                tempDir = extractTempDir;
                packageName = resolvedPackageName;
                packageVersion = resolvedPackageVersion;

                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                {
                    LibraryInspectionSubject? coordinateSubject =
                        SelectInspectionSubjectOrReportFailure(
                            assemblyPaths[0],
                            PackageIntegrationProvenance(
                                assemblyPaths[0],
                                extractPath,
                                packageName,
                                packageVersion));
                    if (coordinateSubject is null)
                        return 1;

                    return await WriteILCoordinateBatchAsync(
                        coordinateSubject,
                        packageName,
                        packageVersion,
                        isPlatformAssembly: false,
                        options,
                        context.HttpClient,
                        logger);
                }

                var inspectionPaths = discoveryInspection && assemblyPaths.Count > 0
                    ? [assemblyPaths[0]]
                    : assemblyPaths;
                AssemblyContextIntegrationsBatch? integrations =
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        groupQueryCatalog,
                        inspectionPaths.Select(path =>
                            new AssemblyContextIntegrationsInput(
                                path,
                                PackageIntegrationProvenance(
                                    path,
                                    extractPath,
                                    packageName,
                                    packageVersion))),
                        trace);
                List<LibraryInspectionSubjectSelection> subjectSelections =
                    inspectionPaths.Select(path =>
                        LibraryInspectionSubject.Select(
                            path,
                            PackageIntegrationProvenance(
                                path,
                                extractPath,
                                packageName,
                                packageVersion),
                            integrations?.AssemblyForInspection(
                                path)))
                    .ToList();
                LibraryInspectionSubjectSelection.Ready? primaryReady =
                    subjectSelections
                        .OfType<LibraryInspectionSubjectSelection.Ready>()
                        .FirstOrDefault();

                // Network-free SourceLink availability probe (see platform branch).
                bool sourceLinkAvailable = fullEffectiveDiscovery
                    && primaryReady is not null
                    && !HasILOffsetCoordinate(options)
                    && await ProbeLocalSourceLinkAsync(
                        primaryReady.Subject,
                        context.HttpClient,
                        logger,
                        isPlatformAssembly: false,
                        packageName: packageName,
                        packageVersion: packageVersion,
                        sourceOptions: options.SourceOptions);

                // Identity of the bytes about to be inspected; see the platform path above.
                string? inspectedContentHash =
                    fullEffectiveDiscovery && primaryReady is not null
                    ? TryGetContentHash(primaryReady.Subject.Path)
                    : null;

                // Check effective sections cache before running full inspection
                if (useEffectiveDiscoveryCache
                    && inspectedContentHash != null
                    && primaryReady is not null)
                {
                    var cached = TryGetCachedEffective(
                        primaryReady.Subject.Path,
                        inspectedContentHash,
                        sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(
                            primaryReady.Subject.Path);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, pipeline, userVerbosity, rootLabel);
                    }
                }

                // Verify package signature if nupkg is available
                SignatureVerificationResult? signatureResult = null;
                if (nupkgPath != null && !discoveryInspection)
                {
                    logger.Log($"Verifying package signature: {Path.GetFileName(nupkgPath)}");
                    signatureResult = await SignatureVerifier.VerifyAsync(nupkgPath);
                }

                // Inspect all assemblies
                InspectionQueryPlan<InspectionQueryContext> queryPlan =
                    queryCatalog.Plan(queries);
                PackageInspectionCollection collection =
                    await CollectPackageInspectionsAsync(
                    inspectionPaths, inspectionOptions, logger, packageName, packageVersion,
                    extractPath, context.HttpClient, signatureResult,
                    queryPlan, integrations,
                    discoveryInspection && !fullEffectiveDiscovery, trace,
                    subjectSelections);
                List<LibraryInspection> inspections =
                    collection.Inspections;
                int descriptorSelectionExitCode =
                    collection.DescriptorSelectionFailures.Count > 0 ? 1 : 0;

                if (inspections.Count == 0)
                {
                    PackageCommand.WriteIdentifierAuditFailures(
                        collection.IdentifierAuditFailures);
                    CommandError.Write("No libraries could be read from the package.");
                    return 1;
                }

                foreach (var insp in inspections)
                    insp.Source = SourceKind.NuGet;
                if (inspections.Count == 1
                    && RejectFailedExactIdentifierAudit(
                        inspections[0],
                        options))
                {
                    return 1;
                }

                bool identifierAuditIncomplete =
                    PackageCommand.WriteIdentifierAuditFailures(
                        collection.IdentifierAuditFailures);
                int identifierAuditExitCode =
                    identifierAuditIncomplete ? 1 : 0;

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspections[0],
                    collection.Subjects[0],
                    packageName, packageVersion, isPlatformAssembly: false,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                int heapExitCode = PopulateMetadataSelection(inspections[0], options, logger);
                if (heapExitCode != 0)
                    return heapExitCode;
                if (discoveryInspection)
                    return Math.Max(
                        Math.Max(
                            identifierAuditExitCode,
                            descriptorSelectionExitCode),
                        WriteEffectiveSections(
                            collection.Subjects[0].Path,
                            inspections[0], options,
                            pipeline, userVerbosity,
                            fullEffectiveDiscovery,
                            discoveryExecutionScope,
                            sourceLinkAvailable,
                            cache: useEffectiveDiscoveryCache,
                            inspectedContentHash:
                                inspectedContentHash,
                            reportIdentifierFailures:
                                !identifierAuditIncomplete));
                if (options.Print)
                    return IntegrityExitCode(
                        Math.Max(
                            Math.Max(
                                identifierAuditExitCode,
                                descriptorSelectionExitCode),
                            await WriteLibraryPrintProjectionAsync(
                                inspections[0],
                                options)),
                        !identifierAuditIncomplete,
                        inspections[0]);
                if (options.Value || options.Urls || options.Paths)
                    return IntegrityExitCode(
                        Math.Max(
                            Math.Max(
                                identifierAuditExitCode,
                                descriptorSelectionExitCode),
                            WriteLibraryShapeProjection(
                                inspections[0],
                                options)),
                        !identifierAuditIncomplete,
                        inspections[0]);
                if (RejectEmptyExactSection(inspections, options, pipeline))
                    return 1;
                WarnEmptySections(inspections, options, pipeline);
                if (collection.Subjects.Count > 0)
                    ExtractResourcesIfRequested(
                        collection.Subjects[0].Path,
                        options);

                if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                    return 1;

                if (inspections.Count == 1 && !IsAllTfmPackageSelection(options))
                    OutputFormatter.WriteLibraryResult(inspections[0], options, pipeline);
                else
                {
                    if (RejectMultiAssemblyMetadataSelection(inspections, options))
                        return 1;
                    OutputFormatter.WriteLibraryResults(inspections, options, pipeline);
                }

                return Math.Max(
                    IntegrityExitCode(
                        Math.Max(
                            identifierAuditExitCode,
                            descriptorSelectionExitCode),
                        !identifierAuditIncomplete,
                        [.. inspections]),
                    SelectedInspectionFailureExitCode(
                        options,
                        pipeline,
                        [.. inspections]));
            }
            else
            {
                // Load from filesystem
                if (!File.Exists(assemblyPath))
                {
                    CommandError.Write($"File not found: {assemblyPath}");
                    return 1;
                }

                AssemblyResolutionProvenance inspectionProvenance =
                    AssemblyResolutionProvenance.Local("library path");
                if (!string.IsNullOrWhiteSpace(options.ILOffsetsPath))
                {
                    LibraryInspectionSubject? coordinateSubject =
                        SelectInspectionSubjectOrReportFailure(
                            assemblyPath!,
                            inspectionProvenance);
                    if (coordinateSubject is null)
                        return 1;

                    return await WriteILCoordinateBatchAsync(
                        coordinateSubject,
                        null,
                        null,
                        isPlatformAssembly: false,
                        options,
                        context.HttpClient,
                        logger);
                }

                AssemblyContextIntegrationsBatch? integrations =
                    AssemblyContextIntegrationsRunner.RunIfRequested(
                        queries,
                        groupQueryCatalog,
                        [
                            new AssemblyContextIntegrationsInput(
                                assemblyPath!,
                                inspectionProvenance),
                        ],
                        trace);
                LibraryInspectionSubject? subject =
                    SelectInspectionSubjectOrReportFailure(
                        assemblyPath!,
                        inspectionProvenance,
                        integrations?.AssemblyForInspection(assemblyPath!));
                if (subject is null)
                    return 1;

                // Network-free SourceLink availability probe (see platform branch).
                bool sourceLinkAvailable = fullEffectiveDiscovery && !HasILOffsetCoordinate(options)
                    && await ProbeLocalSourceLinkAsync(
                        subject,
                        context.HttpClient,
                        logger,
                        isPlatformAssembly: false,
                        sourceOptions: options.SourceOptions);

                // Identity of the bytes about to be inspected; see the platform path above.
                string? inspectedContentHash = fullEffectiveDiscovery ? TryGetContentHash(assemblyPath!) : null;

                // Check effective sections cache before running full inspection
                if (useEffectiveDiscoveryCache && inspectedContentHash != null)
                {
                    var cached = TryGetCachedEffective(assemblyPath!, inspectedContentHash, sourceLinkAvailable);
                    if (cached != null)
                    {
                        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath!);
                        return RenderEffective(FilterEffective(cached.Value.Sections, options), cached.Value.Schema, options, pipeline, userVerbosity, rootLabel);
                    }
                }

                InspectionQueryPlan<InspectionQueryContext> queryPlan =
                    queryCatalog.Plan(queries);
                var inspection = await LibraryMetadataService.InspectAsync(
                    assemblyPath!, inspectionOptions, logger, null, null, context.HttpClient,
                    queryPlan: queryPlan,
                    assemblyReference: subject.AssemblyReference,
                    integrationsEntry: integrations?.EntryFor(assemblyPath!),
                    integrationOpportunitiesEntry:
                        integrations?.OpportunitiesEntryFor(assemblyPath!),
                    discoveryOnly: discoveryInspection && !fullEffectiveDiscovery, trace: trace);
                if (inspection == null)
                {
                    CommandError.Write($"Could not read library: {assemblyPath}");
                    return 1;
                }

                inspection.Source = SourceKind.File;
                if (RejectFailedExactIdentifierAudit(
                        inspection,
                        options))
                {
                    return 1;
                }

                var ilOffsetExitCode = await PopulateILOffsetIfRequestedAsync(
                    inspection, subject, null, null, isPlatformAssembly: false,
                    options, context.HttpClient, logger);
                if (ilOffsetExitCode != 0)
                    return ilOffsetExitCode;
                int heapExitCode = PopulateMetadataSelection(inspection, options, logger);
                if (heapExitCode != 0)
                    return heapExitCode;
                if (discoveryInspection)
                    return WriteEffectiveSections(
                        assemblyPath!, inspection, options, pipeline, userVerbosity,
                        fullEffectiveDiscovery, discoveryExecutionScope, sourceLinkAvailable,
                        cache: useEffectiveDiscoveryCache,
                        inspectedContentHash: inspectedContentHash);
                if (options.Print)
                    return await WriteLibraryPrintProjectionAsync(inspection, options);
                if (options.Value || options.Urls || options.Paths)
                    return WriteLibraryShapeProjection(inspection, options);
                if (RejectEmptyExactSection(inspection, options, pipeline))
                    return 1;
                WarnEmptySections(inspection, options, pipeline);
                ExtractResourcesIfRequested(assemblyPath!, options);
                if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
                    return 1;

                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                return Math.Max(
                    IntegrityExitCode(inspection),
                    SelectedInspectionFailureExitCode(
                        options,
                        pipeline,
                        inspection));
            }
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        finally
        {
            // Cleanup temp directory if we extracted from a package
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static int IntegrityExitCode(params LibraryInspection[] inspections)
        => IntegrityExitCode(0, inspections);

    private static int IntegrityExitCode(
        int currentExitCode,
        params LibraryInspection[] inspections)
        => IntegrityExitCode(
            currentExitCode,
            reportIdentifierFailures: true,
            inspections);

    private static int IntegrityExitCode(
        int currentExitCode,
        bool reportIdentifierFailures,
        params LibraryInspection[] inspections)
    {
        var identifierFailures = inspections
            .Where(
                inspection =>
                    inspection.IdentifierConfusionFailure is not null)
            .Select(
                inspection =>
                    inspection.IdentifierConfusionFailure!.Value)
            .Distinct()
            .ToList();
        if (reportIdentifierFailures)
        {
            foreach (
                IdentifierConfusionAuditFailureKind failure
                in identifierFailures)
            {
                CommandError.WriteWarning(
                    "Identifier audit failed: "
                    + IdentifierConfusionAudit.DescribeFailure(failure));
            }
        }

        if (currentExitCode != 0)
            return currentExitCode;

        return inspections.Any(
                inspection =>
                    inspection.SourceIntegrityMismatches is { Count: > 0 })
            || identifierFailures.Count > 0
            ? 1
            : 0;
    }

    internal static int SelectedInspectionFailureExitCode(
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline,
        params LibraryInspection[] inspections)
    {
        if (options.IncludeSections is not { Count: > 0 })
            return 0;

        return inspections.Any(inspection =>
        {
            var empty = pipeline.GetEmptySections(
                inspection,
                options.Verbosity,
                options.IncludeSections).Empty;
            return (inspection.InspectionFailures ?? []).Any(failure =>
                empty.Any(section =>
                    FailureAffectsSection(
                        failure.Section,
                        section)));
        })
            ? 1
            : 0;
    }

    private static bool RejectFailedExactIdentifierAudit(
        LibraryInspection inspection,
        LibraryOptions options)
    {
        if (inspection.IdentifierConfusionFailure is not { } failure)
            return false;

        bool exactSelection =
            options.IncludeSections is { Count: 1 } sections
            && sections.Contains(SectionNames.IdentifierConfusion)
            && options.ExactIncludeSections?.Contains(
                SectionNames.IdentifierConfusion) == true;
        bool exactDiscovery =
            options.Discover is { Length: 1 }
            && options.Discover[0].Equals(
                SectionNames.IdentifierConfusion,
                StringComparison.OrdinalIgnoreCase);
        if (!exactSelection && !exactDiscovery)
            return false;

        CommandError.Write(
            "Identifier audit could not inspect assembly references: "
            + IdentifierConfusionAudit.DescribeFailure(failure)
            + ".");
        return true;
    }

    private static Task<bool> ProbeLocalSourceLinkAsync(
        LibraryInspectionSubject subject,
        HttpClient httpClient,
        VerboseLogger logger,
        bool isPlatformAssembly,
        string? packageName = null,
        string? packageVersion = null,
        NuGetSourceOptions? sourceOptions = null) =>
        subject.AssemblyReference is { } assembly
            ? LibraryMetadataService.ProbeLocalSourceLinkAsync(
                assembly,
                httpClient,
                logger,
                isPlatformAssembly,
                packageName,
                packageVersion,
                sourceOptions)
            : LibraryMetadataService.ProbeLocalSourceLinkAsync(
                subject.Path,
                httpClient,
                logger,
                isPlatformAssembly,
                packageName,
                packageVersion,
                sourceOptions);

    private static void ReportDescriptorSelectionFailure(
        string path,
        CandidateOpenFailure failure) =>
        CommandError.Write(
            $"Could not select library descriptor for '{path}': "
            + failure.Detail);

    private static LibraryInspectionSubject?
        SelectInspectionSubjectOrReportFailure(
            string path,
            AssemblyResolutionProvenance provenance,
            ResolvedAssemblyReference? preferredAssembly = null)
    {
        LibraryInspectionSubjectSelection selection =
            LibraryInspectionSubject.Select(
                path,
                provenance,
                preferredAssembly);
        if (selection is LibraryInspectionSubjectSelection.Ready ready)
            return ready.Subject;

        ReportDescriptorSelectionFailure(
            path,
            ((LibraryInspectionSubjectSelection.Rejected)selection).Failure);
        return null;
    }

    private static async Task<int> WriteILCoordinateBatchAsync(
        LibraryInspectionSubject subject,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (!File.Exists(options.ILOffsetsPath))
        {
            CommandError.Write($"IL offsets file not found: {options.ILOffsetsPath}");
            return 1;
        }

        if (!TryReadILCoordinates(options.ILOffsetsPath!, out var coordinates, out var readErrors, out var error))
        {
            CommandError.Write(error!);
            return 1;
        }

        HashSet<string> sections = options.IncludeSections is { Count: > 0 }
            ? [.. options.IncludeSections]
            : [.. BatchCoordinateSections];

        var rows = readErrors
            .Select(errorRow => new ILCoordinateBatchRow(null, errorRow.Label, null, null, "error", errorRow.Error))
            .ToList();
        using var service = subject.OpenSourceLink(logger.Log);
        foreach (var coordinate in coordinates)
        {
            var queryOptions = options with
            {
                ILOffsetParameter = coordinate.Coordinate,
                IncludeSections = sections,
                Select = [.. sections],
                Discover = null,
                Print = false,
                Count = false,
                Value = false,
                Urls = false,
                Paths = false
            };
            var resolved = await ILOffsetQuery.ResolveBatchAsync(
                service,
                packageName,
                packageVersion,
                isPlatformAssembly,
                queryOptions,
                httpClient,
                logger);
            rows.Add(resolved.Result is { } result
                ? BuildILCoordinateBatchRow(coordinate, result)
                : new ILCoordinateBatchRow(coordinate.Coordinate, coordinate.Label, null, null, "error", resolved.Error ?? "could not resolve"));
        }

        var batchExitCode = rows.Any(row => row.Meaning == "error") ? 1 : 0;
        var visibleRows = RowWindow.Apply(options.Rows, rows);

        // A coordinate that failed to resolve is still a reported row, so it counts; the
        // non-zero exit remains the signal that some coordinate did not resolve.
        if (LensProjection.TryProject(
                options,
                "--il-offsets",
                visibleRows.Count,
                out var projectionExitCode,
                ["Coordinate", "Label", "Member", "IL Offset", "Meaning", "Evidence"]))
            return projectionExitCode != 0 ? projectionExitCode : batchExitCode;

        if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
            return 1;

        WriteILCoordinateBatchRows(
            [.. visibleRows],
            options with { Rows = null });
        return batchExitCode;    }

    private static readonly string[] BatchCoordinateSections =
    [
        SectionNames.MemberContext,
        SectionNames.InstructionContext,
        SectionNames.ExceptionContext,
        SectionNames.CallsiteContext,
        SectionNames.ReturnAddressContext,
        SectionNames.AllocationContext,
        SectionNames.SafetyContext,
        SectionNames.CostContext
    ];

    private static bool TryReadILCoordinates(string path, out List<ILCoordinateInput> coordinates, out List<ILCoordinateReadError> readErrors, out string? error)
    {
        coordinates = [];
        readErrors = [];
        error = null;
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var coordinateIndex = Array.FindIndex(tokens, token => ILOffsetQuery.TryParse(token, out _, out _));
            if (coordinateIndex < 0)
            {
                readErrors.Add(new ILCoordinateReadError($"{path}:{lineNumber}", "expected a MethodDef token + IL offset coordinate"));
                continue;
            }

            var labelTokens = tokens
                .Where((_, index) => index != coordinateIndex)
                .ToArray();
            coordinates.Add(new ILCoordinateInput(
                tokens[coordinateIndex],
                labelTokens.Length == 0 ? null : string.Join(' ', labelTokens)));
        }

        if (coordinates.Count == 0 && readErrors.Count == 0)
        {
            error = $"{path} did not contain any IL coordinates.";
            return false;
        }

        return true;
    }

    private static ILCoordinateBatchRow BuildILCoordinateBatchRow(ILCoordinateInput input, ILOffsetProjection result)
    {
        var (meaning, evidence) = ExplainILCoordinate(result);
        return new ILCoordinateBatchRow(
            input.Coordinate,
            input.Label,
            result.Method,
            FormatBatchOffset(result),
            meaning,
            evidence);
    }

    private static (string Meaning, string Evidence) ExplainILCoordinate(ILOffsetProjection result)
    {
        // A batch row has one primary meaning: exact operation identity wins over derived
        // correspondence, while semantic facts can still provide the most useful evidence text.
        if (result.AllocationContext is { Count: > 0 } allocations)
        {
            var allocation = allocations[0];
            return ("allocation", $"{allocation.AllocationKind} {allocation.AllocatedType}".Trim());
        }
        if (result.SafetyContext is { Count: > 0 } safetyFacts)
        {
            var safety = safetyFacts[0];
            return ("safety", $"{safety.SafetyKind} {safety.Operation}".Trim());
        }
        if (result.CallsiteContext is { } callsite)
        {
            var evidence = result.CostContext is { Count: > 0 } callCosts
                ? $"{callCosts[0].CostKind} {callCosts[0].Operation}".Trim()
                : $"{callsite.Opcode} {callsite.Callee}";
            return ("callsite", evidence);
        }
        if (result.CostContext is { Count: > 0 } costFacts)
        {
            var cost = costFacts[0];
            return ("cost", $"{cost.CostKind} {cost.Operation}".Trim());
        }
        if (result.ReturnAddressContext is { } returnAddress)
            return ("return address", $"call at {returnAddress.CallOffset} to {returnAddress.Callee}");
        if (result.ExceptionContext is { Count: > 0 } exceptions)
            return ("exception", string.Join(", ", exceptions.Select(e => $"{e.Context} {e.Clause}".Trim())));
        if (result.InstructionContext is { } instruction)
            return ("instruction", $"{instruction.Opcode} {instruction.Operand}".Trim());
        return ("member", result.MemberContext?.Signature ?? result.Method ?? "");
    }

    private static string? FormatBatchOffset(ILOffsetProjection result)
        => result.InstructionContext?.ILOffset is { } offset
            ? FormatHexOffset(offset)
            : result.MemberContext?.ILOffset is { } memberOffset
                ? FormatHexOffset(memberOffset)
                : result.ILOffset;

    private static string FormatHexOffset(string value)
        => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset)
            ? $"IL_{offset:X4}"
            : value;

    private static void WriteILCoordinateBatchRows(List<ILCoordinateBatchRow> rows, LibraryOptions options)
    {
        if (options.JsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new ILCoordinateBatchResult(rows), ILCoordinateBatchJsonContext.Default.ILCoordinateBatchResult));
            return;
        }

        if (!options.Tabular && !options.Tsv && !options.Jsonl && !options.NoHeader)
        {
            Console.WriteLine("## IL Coordinates");
            Console.WriteLine();
        }

        OutputFormatter.WriteTable(Console.Out, !options.NoHeader, (writer, formatter) =>
        {
            var writerOptions = OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl);
            var markoutWriter = new MarkoutWriter(writer, formatter, writerOptions);
            markoutWriter.WriteTable(
                ["Coordinate", "Label", "Member", "IL Offset", "Meaning", "Evidence"],
                ["coordinate", "label", "member", "il_offset", "meaning", "evidence"],
                rows.Select(row => new[]
                {
                    row.Coordinate ?? "",
                    row.Label ?? "",
                    row.Member ?? "",
                    row.ILOffset ?? "",
                    row.Meaning,
                    row.Evidence
                }).ToArray());
            markoutWriter.Flush();
        }, options.Rows);
    }

    private static (LibraryOptions Options, string? Error) NormalizeILOffsetSelection(LibraryOptions options)
    {
        var select = options.Select?.ToList() ?? [];
        string? ilOffset = options.ILOffsetParameter;
        bool hasExplicitSelect = select.Count > 0;

        // Reject "<coordinate section>:<offset>" selectors. The legacy spellings ("IL Offset",
        // "Source Location") stay listed because they still resolve as aliases, and the current
        // name itself contains a colon, so the guard must match name + ':' rather than any colon.
        string[] parameterizedPrefixes =
        [
            "IL Offset:",
            "Source Location:",
            SectionNames.ILOffset + ":",
        ];

        for (var i = 0; i < select.Count; i++)
        {
            var value = select[i].Trim();
            if (parameterizedPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return (options, $"IL offset parameters belong in --il-offset, not in -S. Use --il-offset 0x06000001+0x5 -S \"{SectionNames.ILOffset}\".");
        }

        if (!string.IsNullOrWhiteSpace(ilOffset)
            && options.Discover == null
            && !hasExplicitSelect)
        {
            select.Add(SectionNames.ILOffset);
            select.Add(SectionNames.MemberContext);
            select.Add(SectionNames.InstructionContext);
            select.Add(SectionNames.ExceptionContext);
            select.Add(SectionNames.CallsiteContext);
            select.Add(SectionNames.ReturnAddressContext);
        }

        return (options with
        {
            ILOffsetParameter = ilOffset,
            Select = select.Count == 0 ? null : [.. select]
        }, null);
    }

    internal static string? ApplyCoordinateSectionRequirements(
        LibraryOptions options,
        SelectResult selectResult)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selectResult);
        if (selectResult.Sections is not { } sections)
            return null;

        const string ilCoordinateRequired =
            "IL coordinate sections require --il-offset <token>+<offset>.";
        var heapCoordinateRequired =
            $"\"{MetadataSectionNames.Heap}\" requires --heap <heap>:<address>, for example --heap \"#Strings:0x1a4\".";
        var bodyKindRequired =
            $"\"{SectionNames.BodyShapes}\" requires --where \"Kind=<C# Body Kinds ID>\".";
        var removedILCoordinateSections = false;
        var removedHeapSection = false;
        var removedBodyShapesSection = false;

        if (sections.Overlaps(ILCoordinateSections)
            && string.IsNullOrWhiteSpace(options.ILOffsetParameter))
        {
            if (!selectResult.ExactSections.Overlaps(ILCoordinateSections))
            {
                var count = sections.Count;
                sections.ExceptWith(ILCoordinateSections);
                removedILCoordinateSections = sections.Count != count;
            }
            else if (options.Discover == null)
            {
                return ilCoordinateRequired;
            }
        }

        if (sections.Contains(MetadataSectionNames.Heap)
            && string.IsNullOrWhiteSpace(options.HeapParameter))
        {
            // Reached through @Metadata the section is dropped because a category selects whatever
            // applies. An exact selector is an error because the section cannot exist without its
            // coordinate.
            if (!selectResult.ExactSections.Contains(
                    MetadataSectionNames.Heap))
            {
                removedHeapSection = sections.Remove(
                    MetadataSectionNames.Heap);
            }
            else if (options.Discover == null)
            {
                return heapCoordinateRequired;
            }
        }

        if (sections.Contains(SectionNames.BodyShapes)
            && !options.BodyKindQuery.HasFilter)
        {
            if (!selectResult.ExactSections.Contains(SectionNames.BodyShapes))
            {
                removedBodyShapesSection = sections.Remove(SectionNames.BodyShapes);
            }
            else if (options.Discover == null)
            {
                return bodyKindRequired;
            }
        }

        if (sections.Count != 0
            || (!removedILCoordinateSections
                && !removedHeapSection
                && !removedBodyShapesSection))
        {
            return null;
        }

        if (removedILCoordinateSections)
            return ilCoordinateRequired;
        if (removedHeapSection)
            return heapCoordinateRequired;
        return bodyKindRequired;
    }

    // Catalog-hidden set for the effective (real-assembly) -D flows. Base-category
    // members form the flat catalog; separate domains remain behind their category
    // doors even when a coordinate or other explicit input makes a member effective.
    // Unsafe Members is the one standalone evidence section promoted by a bounded
    // presence probe: it remains uncategorized and explicitly rendered.
    private static IReadOnlySet<string> EffectiveCatalogHidden(
        SectionPipeline<LibraryInspection> pipeline,
        IReadOnlyCollection<string> effective)
    {
        IReadOnlySet<string> hidden =
            pipeline.GetCatalogHiddenSections();
        if (!effective.Contains(
                SectionNames.UnsafeMembers,
                StringComparer.OrdinalIgnoreCase))
        {
            return hidden;
        }

        return hidden
            .Where(section => !section.Equals(
                SectionNames.UnsafeMembers,
                StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rejects direct-library metadata-lens rows when a package resolved to more than one assembly.
    /// That renderer carries no per-image provenance: several assemblies would emit repeated
    /// <c>## Metadata: TypeDef</c> headings whose rows silently belong to different images and
    /// whose row numbering restarts without saying so. Aggregate counts remain safe because they
    /// do not expose image-relative row identities. Package <c>--all-libraries</c> is a separate
    /// renderer that suffixes each metadata heading with the package-relative assembly path. The
    /// direct-library rejection and count allowance are gated by
    /// <c>MetadataLens_MultipleAssemblies_IsRejected</c> in dotnet-inspect.Tests; all-libraries
    /// provenance is gated by
    /// <c>PackageCommand_AllLibraries_BareSelectCount_MapDescribesBareSelectRender</c>.
    /// </summary>
    private static bool RejectMultiAssemblyMetadataSelection(
        IReadOnlyCollection<LibraryInspection> inspections, LibraryOptions options)
    {
        if (options.Count
            || inspections.Count <= 1
            || options.IncludeSections is not { Count: > 0 } selected)
            return false;

        if (!selected.Any(MetadataSectionNames.IsMetadataSection))
            return false;

        CommandError.Write(
            $"{SectionCategoryNames.Metadata} inspects the metadata tables of a single assembly, " +
            $"but this package resolved to {inspections.Count} assemblies.",
            "Select one assembly with --library <path> and retry.");
        return true;
    }

    private static readonly string[] ILCoordinateSections =
    [
        SectionNames.ILOffset,
        SectionNames.MemberContext,
        SectionNames.InstructionContext,
        SectionNames.ExceptionContext,
        SectionNames.CallsiteContext,
        SectionNames.ReturnAddressContext,
        SectionNames.AllocationContext,
        SectionNames.SafetyContext,
        SectionNames.CostContext
    ];

    private static bool HasILOffsetCoordinate(LibraryOptions options)
        => !string.IsNullOrWhiteSpace(options.ILOffsetParameter);

    /// <summary>
    /// True when a heap coordinate was supplied. Like an IL coordinate, it changes which sections
    /// exist, so a discovery catalog computed with one must not be served to a run without one.
    /// </summary>
    private static bool HasHeapCoordinate(LibraryOptions options)
        => !string.IsNullOrWhiteSpace(options.HeapParameter);

    private static LibraryOptions NormalizeReferenceProjection(LibraryOptions options)
    {
        if (options.Discover != null)
            return options;

        var select = options.Select?.ToList() ?? [];
        var tree = options.Tree || options.IncludeDependencies;

        for (var i = 0; i < select.Count; i++)
        {
            if (!select[i].Equals("Dependencies", StringComparison.OrdinalIgnoreCase))
                continue;

            select[i] = SectionNames.References;
            tree = true;
        }

        if ((options.IncludeReferences || options.IncludeDependencies)
            && !select.Contains(SectionNames.References, StringComparer.OrdinalIgnoreCase))
        {
            select.Add(SectionNames.References);
        }

        return options with
        {
            IncludeReferences = false,
            IncludeDependencies = false,
            Select = select.Count > 0 ? [.. select] : null,
            SelectDefault = select.Count > 0 ? false : options.SelectDefault,
            Tree = tree,
        };
    }

    /// <summary>
    /// Rewrites hex table spellings in <c>-S</c> and <c>-D</c> to canonical section names, so
    /// <c>-S "Metadata: 0x02"</c> and <c>-S "Metadata: TypeDef"</c> reach the same section.
    ///
    /// This runs before selection resolution, so everything downstream — the section orderer, the
    /// rendered heading, <c>--count</c>, the schema, the effective-section cache key — sees only
    /// canonical names and cannot treat the two spellings as two sections.
    /// </summary>
    private static (LibraryOptions Options, string? Error) NormalizeMetadataTableAliases(LibraryOptions options)
    {
        var (select, selectError) = ResolveTableAliases(options.Select);
        if (selectError is not null)
            return (options, selectError);

        var (discover, discoverError) = ResolveTableAliases(options.Discover);
        if (discoverError is not null)
            return (options, discoverError);

        if (select is null && discover is null)
            return (options, null);

        return (options with
        {
            Select = select ?? options.Select,
            Discover = discover ?? options.Discover,
        }, null);
    }

    /// <summary>
    /// Resolves every hex table spelling in <paramref name="values"/>. Returns a null array when
    /// nothing needed rewriting, so an untouched selection keeps its original instance.
    /// </summary>
    internal static (string[]? Values, string? Error) ResolveTableAliases(string[]? values)
    {
        if (values is not { Length: > 0 })
            return (null, null);

        string[]? rewritten = null;
        for (int i = 0; i < values.Length; i++)
        {
            if (!MetadataSectionNames.TryResolveTableAlias(values[i], out string canonical, out string? error))
                return (null, error);

            if (!ReferenceEquals(canonical, values[i]))
            {
                rewritten ??= [.. values];
                rewritten[i] = canonical;
            }
        }

        return (rewritten, null);
    }

    internal static OptionError? GetDiscoveryModeError(
        bool effective,
        bool hasDiscovery,
        bool schema)
    {
        if (effective && !hasDiscovery)
            return new OptionError("--effective requires -D/--discover.");
        if (effective && schema)
            return new OptionError("--effective cannot be combined with --schema.");
        return null;
    }

    /// <summary>
    /// Validates the <c>--heap</c> coordinate and, when no selection was given, selects the
    /// section it feeds.
    ///
    /// The coordinate is parsed here — before any assembly is opened — so a malformed one fails
    /// immediately with a diagnostic naming the wrong half, rather than after the cost of an
    /// inspection.
    /// </summary>
    private static (LibraryOptions Options, string? Error) NormalizeHeapSelection(LibraryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HeapParameter))
            return (options, null);

        if (!MetadataHeapCoordinate.TryParse(options.HeapParameter, out _, out _, out string? error))
            return (options, $"invalid --heap value '{options.HeapParameter}': {error}");

        if (options.Discover != null || options.Select is { Length: > 0 })
            return (options, null);

        return (options with { Select = [MetadataSectionNames.Heap] }, null);
    }

    /// <summary>
    /// Checks an explicit root and reads the heap value <c>--heap</c> named onto the model.
    /// A requested root must resolve even when bare discovery renders only category doors.
    /// The heap value is what makes the
    /// coordinate-scoped section applicable. Returns a process exit code, having written its own
    /// diagnostic, exactly as the <c>--il-offset</c> resolution above it does.
    ///
    /// A coordinate that does not resolve is an <em>error</em>, not a malformed cell in an
    /// otherwise successful render. The two cases look alike but are not: a bad heap reference
    /// found inside a projected table row is a fact about the image, so it renders as
    /// <c>!malformed</c> and the command succeeds; a coordinate is the caller's own input, and the
    /// caller asked for exactly one thing that does not exist. Rendering that as a successful row
    /// would exit 0 while answering nothing, and — worse — <c>-D</c> would go on advertising
    /// <c>Metadata: Heap</c> as an available section. <c>--il-offset</c> already draws the line
    /// here (<c>IL offset 0x… is not an instruction boundary</c>, exit 1) and this matches it.
    /// </summary>
    private static int PopulateMetadataSelection(
        LibraryInspection inspection, LibraryOptions options, VerboseLogger logger)
    {
        if (inspection.RequestedMetadataRoot is not null
            && inspection.MetadataImageResult is MetadataImageResult.Failed failed)
        {
            CommandError.Write($"Could not read the requested metadata root: {failed.Error.Message}");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.HeapParameter)
            || (options.Discover == null && options.IncludeSections?.Contains(MetadataSectionNames.Heap) != true))
            return 0;

        if (inspection.MetadataAssemblyPath is not { } path)
            return 0;

        if (!MetadataHeapCoordinate.TryParse(options.HeapParameter, out var heap, out int address, out _))
            throw new UnreachableException("NormalizeHeapSelection rejects a malformed --heap coordinate before this point.");

        string name = MetadataHeapCoordinate.StreamName(heap);
        MetadataValue? value;
        try
        {
            if (inspection.RequestedMetadataRoot is not null)
            {
                if (inspection.MetadataRoot is not { } root)
                {
                    CommandError.Write("The requested metadata root could not be opened; no heap value was read.");
                    return 1;
                }
                value = root.HeapValue(heap, address);
            }
            else
            {
                using var session = AssemblyInspectionSession.Open(path);
                value = session.MetadataHeapValue(heap, address);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Error reading {name} heap at {address} in {path}: {ex.Message}");
            CommandError.Write($"could not read {name} heap at {address}: {ex.Message}");
            return 1;
        }

        switch (value)
        {
            case null:
                CommandError.Write($"could not read {name} heap at {address}: {path} carries no metadata.");
                return 1;

            case MetadataValue.Malformed malformed:
                CommandError.Write($"could not read {name} heap at {address}: {malformed.Detail}");
                return 1;

            default:
                inspection.MetadataHeap = new MetadataHeapLookup(heap, address, value);
                return 0;
        }
    }

    private static async Task<int> PopulateILOffsetIfRequestedAsync(
        LibraryInspection inspection,
        LibraryInspectionSubject subject,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.ILOffsetParameter)
            || (options.Discover == null && options.IncludeSections?.Overlaps(ILCoordinateSections) != true))
            return 0;

        using var service = subject.OpenSourceLink(logger.Log);
        var resolved = await ILOffsetQuery.ResolveAsync(
            service, packageName, packageVersion, isPlatformAssembly, options,
            httpClient, logger);
        if (resolved.ExitCode != 0)
            return resolved.ExitCode;

        inspection.ILOffset = resolved.Result;
        return 0;
    }

    private static bool ValidateLibraryPrintSelection(HashSet<string>? sections)
    {
        if (sections is { Count: 1 } && sections.Contains(SectionNames.ILOffset))
            return true;

        CommandError.Write("--print requires -S/--select to match exactly one printable section.");
        return false;
    }

    internal static bool ValidateReferenceTreeCount(
        bool tree,
        bool count,
        IReadOnlyCollection<string>? sections)
    {
        if (!tree
            || !count
            || sections is not { Count: 1 }
            || !sections.Contains(
                SectionNames.References,
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        CommandError.Write(
            "--count is not available with -S References --tree because "
            + "the reference tree does not declare countable row semantics.");
        return false;
    }

    private static bool ValidateMultiTfmOutput(LibraryOptions options)
    {
        if (!IsAllTfmPackageSelection(options)
            || (options.Discover != null && string.IsNullOrWhiteSpace(options.ILOffsetsPath)))
        {
            return true;
        }

        string? incompatibleShape = options.Tree && !options.Count ? "--tree"
            : options.Print ? "--print"
            : options.Value ? "--value"
            : options.Urls ? "--urls"
            : options.Paths ? "--paths"
            : options.ExtractResources != null ? "--extract-resources"
            : !string.IsNullOrWhiteSpace(options.ILOffsetParameter) ? "--il-offset"
            : !string.IsNullOrWhiteSpace(options.ILOffsetsPath) ? "--il-offsets"
            : !string.IsNullOrWhiteSpace(options.HeapParameter) ? "--heap"
            : null;

        if (incompatibleShape is not null)
        {
            if (options.Tree)
            {
                CommandError.Write(
                    "--tree requires exactly one tree shape; --tfm all selects one tree per inspection. Use Markdown or JSON for all TFMs, or select one --tfm for --tree.");
                return false;
            }

            CommandError.Write(
                $"--tfm all supports full output only as Markdown or JSON, plus aggregate --count; it cannot be combined with {incompatibleShape}.");
            return false;
        }

        if (options.Count)
            return true;

        if (options.Format is OutputFormat.Markdown or OutputFormat.Json)
            return true;

        var tabularFormatName = options.Format switch
        {
            OutputFormat.Table => "--table",
            OutputFormat.Tsv => "--tsv",
            OutputFormat.Jsonl => "--jsonl",
            _ => null
        };
        if (tabularFormatName is not null)
        {
            CommandError.Write(
                $"{tabularFormatName} requires exactly one table shape; --tfm all selects one table per inspection. Use Markdown or JSON, or aggregate --count for all TFMs.");
            return false;
        }

        var formatName = options.Format switch
        {
            OutputFormat.PlainText => "plain-text output (--plaintext)",
            OutputFormat.Mermaid => "Mermaid output (--mermaid)",
            _ => options.Format.ToString()
        };

        CommandError.Write(
            $"--tfm all supports full output only as Markdown or JSON, plus aggregate --count; {formatName} is not supported.");
        return false;
    }

    private static bool IsAllTfmPackageSelection(LibraryOptions options)
        => string.IsNullOrEmpty(options.PlatformAssembly)
            && !string.IsNullOrEmpty(options.PackagePath)
            && string.Equals(options.Tfm, "all", StringComparison.OrdinalIgnoreCase);

    private static int WriteLibraryShapeProjection(LibraryInspection inspection, LibraryOptions options)
    {
        var kind = ShapeProjectionOutput.GetKind(options.Value, options.Urls, options.Paths);
        var section = options.IncludeSections!.Single();
        var rows = section switch
        {
            SectionNames.SourceLinkFiles => ProjectLibrarySourceFiles(inspection, section, kind, options),
            "Library Info" => ProjectLibraryInfo(inspection, section, kind, options),
            SectionNames.ILOffset => ProjectLibraryILOffset(inspection, section, kind, options),
            SectionNames.MemberContext => ProjectLibraryMemberContext(inspection, section, kind, options),
            SectionNames.InstructionContext => ProjectLibraryInstructionContext(inspection, section, kind, options),
            SectionNames.ExceptionContext => ProjectLibraryExceptionContext(inspection, section, kind, options),
            SectionNames.CallsiteContext => ProjectLibraryCallsiteContext(inspection, section, kind, options),
            SectionNames.ReturnAddressContext => ProjectLibraryReturnAddressContext(inspection, section, kind, options),
            _ => []
        };

        if (rows.Count == 0 && section is SectionNames.MemberContext or SectionNames.InstructionContext or SectionNames.ExceptionContext
            or SectionNames.CallsiteContext or SectionNames.ReturnAddressContext)
        {
            if (kind != ShapeProjectionKind.Value)
                CommandError.Write($"section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }

        if (rows.Count == 0 && section is not (SectionNames.SourceLinkFiles or "Library Info") && section != SectionNames.ILOffset)
        {
            CommandError.Write($"section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }
        if (rows.Count == 0 && section == "Library Info" && kind == ShapeProjectionKind.Value)
            return 1;

        return ShapeProjectionOutput.Write(
            rows,
            new ShapeProjectionOptions(
                kind,
                options.ProjectionRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                new ProjectionDestination(options.OutputPath, options.Rows)));
    }

    private static async Task<int> WriteLibraryPrintProjectionAsync(LibraryInspection inspection, LibraryOptions options)
    {
        var section = options.IncludeSections!.Single();
        var projection = section switch
        {
            SectionNames.ILOffset => await ProjectLibraryILOffsetPrintableAsync(inspection, section),
            _ => new PrintProjectionResult([])
        };
        if (projection.Error is not null)
        {
            CommandError.Write(projection.Error);
            return 1;
        }

        if (projection.Documents.Count == 0 && section != SectionNames.ILOffset)
        {
            CommandError.Write($"section '{section}' is not printable.");
            return 1;
        }

        return PrintProjectionOutput.Write(
            projection.Documents,
            new PrintProjectionOptions(
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                Bare: false,
                Destination: new ProjectionDestination(options.OutputPath, options.Rows)));
    }

    private sealed record PrintProjectionResult(IReadOnlyList<PrintableDocument> Documents, string? Error = null);

    private static async Task<PrintProjectionResult> ProjectLibraryILOffsetPrintableAsync(
        LibraryInspection inspection,
        string section)
    {
        if (inspection.ILOffset is not { } result)
            return new PrintProjectionResult([]);

        var (content, error) = await ReadILOffsetSourceLineAsync(result);
        if (error is not null)
            return new PrintProjectionResult([], error);
        if (content is null)
            return new PrintProjectionResult([]);

        return new PrintProjectionResult(
        [
            new PrintableDocument(1, section, result.Method ?? result.File ?? section, result.File, result.Url, content)
        ]);
    }

    internal static Task<(string? Content, string? Error)> ReadILOffsetSourceLineForTestsAsync(ILOffsetProjection result)
        => ReadILOffsetSourceLineAsync(result);

    private static async Task<(string? Content, string? Error)> ReadILOffsetSourceLineAsync(ILOffsetProjection result)
    {
        if (result.Line is not { } line || line < 1)
        {
            return (null, "Source Location row has no source line to print.");
        }

        if (string.IsNullOrWhiteSpace(result.Url))
        {
            return (null, "Source Location row has no printable source body. Use --urls or --paths to inspect available payloads.");
        }

        var rawUrl = StripUrlFragment(GitHubUrlResolver.ConvertBlobToRawUrl(result.Url));
        var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
        var fetch = await PdbSourceAcquisition.FetchVerifiedSourceTextAsync(
            fetcher,
            rawUrl,
            result.SourceChecksumAlgorithm,
            result.SourceChecksum);
        if (fetch.Text is null)
        {
            return (
                null,
                "Could not fetch verified SourceLink source: "
                + (fetch.Failure ?? "source is unavailable."));
        }

        return ReadLine(fetch.Text.ReplaceLineEndings("\n").Split('\n'), line);
    }

    private static (string? Content, string? Error) ReadLine(IEnumerable<string> lines, int line)
    {
        var value = lines.Skip(line - 1).FirstOrDefault();
        if (value is null)
        {
            return (null, $"Source line {line} is out of range.");
        }

        return (value, null);
    }

    private static string StripUrlFragment(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Fragment))
            return url;

        var builder = new UriBuilder(uri) { Fragment = "" };
        return builder.Uri.ToString();
    }

    private static List<ShapeProjectionRow> ProjectLibrarySourceFiles(LibraryInspection inspection, string section, ShapeProjectionKind kind, LibraryOptions options)
    {
        var rows = new LibraryInspectionView(inspection).SourceFilesSection ?? [];
        return rows
            .Select((row, index) =>
            {
                string? value = kind switch
                {
                    ShapeProjectionKind.Urls => row.Url,
                    ShapeProjectionKind.Value => SelectLibrarySourceValue(row, options),
                    _ => null
                };
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : new ShapeProjectionRow(index + 1, section, value, Label: row.Type, Url: row.Url);
            })
            .Where(row => row is not null)
            .Cast<ShapeProjectionRow>()
            .ToList();
    }

    private static string? SelectLibrarySourceValue(SourceFileRow row, LibraryOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "type" => row.Type,
            "url" => row.Url,
            _ => row.Url
        };
    }

    private static List<ShapeProjectionRow> ProjectLibraryILOffset(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (inspection.ILOffset is not { } result)
            return [];

        var value = kind switch
        {
            ShapeProjectionKind.Urls => result.Url,
            ShapeProjectionKind.Paths => result.File,
            ShapeProjectionKind.Value => SelectLibraryILOffsetValue(result, options),
            _ => null
        };

        return string.IsNullOrWhiteSpace(value)
            ? []
            : [new ShapeProjectionRow(1, section, value, Label: result.Method, Url: result.Url, Path: result.File)];
    }

    private static string? SelectLibraryILOffsetValue(ILOffsetProjection result, LibraryOptions options)
    {
        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        return field?.ToLowerInvariant() switch
        {
            "method" => result.Method,
            "token" => result.Token,
            "il offset" or "iloffset" => result.ILOffset,
            "matched offset" or "matchedoffset" => result.MatchedOffset,
            "file" or "path" => result.File,
            "line" => result.Line?.ToString(CultureInfo.InvariantCulture),
            "url" or "source" => result.Url,
            _ => result.Url
        };
    }

    private static List<ShapeProjectionRow> ProjectLibraryMemberContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.MemberContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            CommandError.Write($"--value for {SectionNames.MemberContext} requires --fields <name>.");
            return [];
        }

        var value = SelectMemberContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            CommandError.Write($"field '{field}' has no value in {SectionNames.MemberContext}.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectMemberContextValue(ILOffsetMemberContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "assembly" => context.Assembly,
            "type" => context.Type,
            "type kind" or "typekind" => context.TypeKind,
            "member" => context.Member,
            "signature" => context.Signature,
            "member kind" or "memberkind" => context.MemberKind,
            "visibility" => context.Visibility,
            "static" => context.Static,
            "async" => context.Async,
            "metadata token" or "metadatatoken" or "token" => context.MetadataToken,
            "il offset" or "iloffset" => context.ILOffset,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryInstructionContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.InstructionContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            CommandError.Write($"--value for {SectionNames.InstructionContext} requires --fields <name>.");
            return [];
        }

        var value = SelectInstructionContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            CommandError.Write($"field '{field}' has no value in {SectionNames.InstructionContext}.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectInstructionContextValue(ILOffsetInstructionContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "il offset" or "iloffset" => context.ILOffset,
            "boundary" => context.Boundary,
            "opcode" => context.Opcode,
            "operand kind" or "operandkind" => context.OperandKind,
            "operand" => context.Operand,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            "branch targets" or "branchtargets" => context.BranchTargets,
            "next offset" or "nextoffset" => context.NextOffset,
            "length" => context.Length?.ToString(CultureInfo.InvariantCulture),
            "block" => context.Block?.ToString(CultureInfo.InvariantCulture),
            "terminates block" or "terminatesblock" => context.TerminatesBlock,
            "falls through" or "fallsthrough" => context.FallsThrough,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryExceptionContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.ExceptionContext is not { Count: > 0 } rows)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            CommandError.Write($"--value for {SectionNames.ExceptionContext} requires --fields <name>.");
            return [];
        }

        List<ShapeProjectionRow> projected = [];
        for (var i = 0; i < rows.Count; i++)
        {
            var value = SelectExceptionContextValue(rows[i], field);
            if (!string.IsNullOrWhiteSpace(value))
                projected.Add(new ShapeProjectionRow(i + 1, section, value, Label: field));
        }

        if (projected.Count == 0)
            CommandError.Write($"field '{field}' has no value in {SectionNames.ExceptionContext}.");

        return projected;
    }

    private static string? SelectExceptionContextValue(ILOffsetExceptionContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "region" => context.Region.ToString(CultureInfo.InvariantCulture),
            "context" => context.Context,
            "clause" => context.Clause,
            "try range" or "tryrange" => context.TryRange,
            "handler range" or "handlerrange" => context.HandlerRange,
            "filter range" or "filterrange" => context.FilterRange,
            "caught type" or "caughttype" => context.CaughtType,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryCallsiteContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.CallsiteContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            CommandError.Write($"--value for {SectionNames.CallsiteContext} requires --fields <name>.");
            return [];
        }

        var value = SelectCallsiteContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            CommandError.Write($"field '{field}' has no value in {SectionNames.CallsiteContext}.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectCallsiteContextValue(ILOffsetCallsiteContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "call offset" or "calloffset" or "offset" => context.CallOffset,
            "opcode" => context.Opcode,
            "call kind" or "callkind" => context.CallKind,
            "callee" => context.Callee,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            "return address" or "returnaddress" => context.ReturnAddress,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryReturnAddressContext(
        LibraryInspection inspection,
        string section,
        ShapeProjectionKind kind,
        LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value || inspection.ILOffset?.ReturnAddressContext is not { } context)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            CommandError.Write($"--value for {SectionNames.ReturnAddressContext} requires --fields <name>.");
            return [];
        }

        var value = SelectReturnAddressContextValue(context, field);
        if (string.IsNullOrWhiteSpace(value))
        {
            CommandError.Write($"field '{field}' has no value in {SectionNames.ReturnAddressContext}.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static string? SelectReturnAddressContextValue(ILOffsetReturnAddressContext context, string field)
        => field.ToLowerInvariant() switch
        {
            "il offset" or "iloffset" or "offset" => context.ILOffset,
            "call offset" or "calloffset" => context.CallOffset,
            "opcode" => context.Opcode,
            "call kind" or "callkind" => context.CallKind,
            "callee" => context.Callee,
            "operand token" or "operandtoken" or "token" => context.OperandToken,
            _ => null
        };

    private static List<ShapeProjectionRow> ProjectLibraryInfo(LibraryInspection inspection, string section, ShapeProjectionKind kind, LibraryOptions options)
    {
        if (kind != ShapeProjectionKind.Value)
            return [];
        var info = new LibraryInspectionView(inspection).AssemblyInfoSection;
        if (info is null)
            return [];

        var field = options.Fields?.SingleOrDefault() ?? options.Columns?.SingleOrDefault();
        if (string.IsNullOrWhiteSpace(field))
        {
            CommandError.Write("--value for Library Info requires --fields <name>.");
            return [];
        }

        var values = GetLibraryInfoValues(info);
        if (!values.TryGetValue(field, out var value))
        {
            CommandError.Write($"field '{field}' was not found in Library Info.");
            return [];
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            CommandError.Write($"field '{field}' has no value in Library Info.");
            return [];
        }

        return [new ShapeProjectionRow(1, section, value, Label: field)];
    }

    private static Dictionary<string, string?> GetLibraryInfoValues(LibraryInfoSection info)
    {
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeof(LibraryInfoSection).GetProperties())
        {
            var name = ToPascalCaseWords(property.Name);
            values[name] = FormatLibraryInfoValue(property.GetValue(info));
        }

        return values;
    }

    private static string? FormatLibraryInfoValue(object? value)
        => value switch
        {
            null => null,
            bool b => b ? "Yes" : "No",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static string ToPascalCaseWords(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var builder = new StringBuilder(value.Length + 8);
        builder.Append(value[0]);
        for (var i = 1; i < value.Length; i++)
        {
            var current = value[i];
            var previous = value[i - 1];
            if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
                builder.Append(' ');
            builder.Append(current);
        }

        return builder.ToString();
    }

    internal static int WriteEffectiveSections(
        string assemblyPath,
        LibraryInspection inspection,
        LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline,
        Verbosity userVerbosity,
        bool fullEffectiveness,
        HashSet<string>? effectivenessScope,
        bool sourceLinkAvailable = false,
        bool cache = true,
        string? inspectedContentHash = null,
        bool reportIdentifierFailures = true)
    {
        // Seed the network-free SourceLink-availability fact so the SourceLink section family
        // gates on a cached/embedded/adjacent PDB during discovery (never clears a value the
        // inspection already established from an embedded or adjacent PDB).
        inspection.HasSourceLink |= sourceLinkAvailable;

        if (inspection.UnsafeEvidencePresenceError is { } presenceError)
        {
            CommandError.Write(
                $"Could not determine {SectionNames.UnsafeMembers} applicability for " +
                $"{assemblyPath}: {presenceError.Message}");
            return 1;
        }

        List<string> allEffective;
        if (fullEffectiveness)
        {
            var selected = pipeline.GetAvailableSections(inspection, effectivenessScope)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (options.Discover is { Length: 0 })
            {
                var baseSections = pipeline.BaseSectionNames
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                selected.RemoveWhere(section => !baseSections.Contains(section));

                // Domain doors remain structural orientation in the bare catalog. Their members
                // stay hidden from the flat section list, but one applicable member is needed in
                // the effective schema so the category door survives category filtering.
                foreach (var section in pipeline.GetDiscoverableSections(inspection))
                {
                    if (!baseSections.Contains(section))
                        selected.Add(section);
                }
            }

            allEffective = pipeline.SelectableSectionNames
                .Where(selected.Contains)
                .ToList();
        }
        else
        {
            allEffective = pipeline.GetDiscoverableSections(inspection);
        }

        var schemaMap = MetadataSectionNames.AugmentSchema(
            InspectionContext.Default.GetSchemaInfo<LibraryInspectionView>()!.ToDocumentSchema());

        // Cheap discovery never content-probes fields. Full discovery may narrow dynamic field
        // schemas after the selected producers have run.
        var filteredSchema = fullEffectiveness
            ? FilterSchemaToEffectiveFields(
                inspection, allEffective, schemaMap, pipeline, allEffective.ToArray())
            : schemaMap;
        var failureOptions = options.IncludeSections is not { Count: > 0 }
            && effectivenessScope is { Count: > 0 }
                ? options with { IncludeSections = effectivenessScope }
                : options;
        int inspectionFailureExitCode = SelectedInspectionFailureExitCode(
            failureOptions,
            pipeline,
            inspection);
        bool hasIntegrityFailure =
            inspection.SourceIntegrityMismatches is { Count: > 0 }
            || inspection.IdentifierConfusionFailure is not null;
        if (cache && !hasIntegrityFailure && inspectionFailureExitCode == 0)
            CacheEffective(assemblyPath, inspection.HasSourceLink, allEffective, filteredSchema, inspectedContentHash);

        if (inspectionFailureExitCode != 0)
        {
            if (RejectEmptyExactSection(inspection, failureOptions, pipeline))
            {
                return Math.Max(
                    inspectionFailureExitCode,
                    IntegrityExitCode(
                        0,
                        reportIdentifierFailures,
                        inspection));
            }

            WarnEmptySections(
                [inspection],
                failureOptions,
                pipeline,
                writeEmptyNote: false);
        }

        // Apply user filters
        var effective = FilterEffective(allEffective, options);

        var rootLabel = Path.GetFileNameWithoutExtension(assemblyPath);
        int discoveryExitCode = DiscoverOutput.ExecuteEffective(options.Discover, effective, filteredSchema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
            verbosity: (int)userVerbosity, rootLabel: rootLabel, fullSchema: schemaMap,
            sectionCostAnnotations: pipeline.GetCostAnnotations(),
            sectionCategories: pipeline.GetCategoryMap(),
            catalogHiddenSections: EffectiveCatalogHidden(pipeline, effective),
            listedCategoryDoors: pipeline.GetListedCategoryDoors(),
            projection: options);
        return Math.Max(
            Math.Max(discoveryExitCode, inspectionFailureExitCode),
            IntegrityExitCode(
                0,
                reportIdentifierFailures,
                inspection));
    }

    // ── Effective sections cache ──

    // Bumped to v28: deterministic, non-prefetched unsafe presence changes applicability.
    private const string EffectiveCategory = "effective-v28";

    static LibraryCommand()
    {
        CoreCache.RegisterVersionedCategory("effective-v", EffectiveCategory);
    }

    private static (List<string> Sections, DocumentSchema Schema)? TryGetCachedEffective(string assemblyPath, string contentHash, bool hasSourceLink)
    {
        string key = BuildEffectiveCacheKey(assemblyPath, contentHash, hasSourceLink);
        var cached = CoreCache.TryGet(EffectiveCategory, key, extension: "tsv");
        if (cached == null) return null;

        var sections = new List<string>();
        var schema = new DocumentSchema();
        foreach (var raw in cached.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Entries are written with AppendLine, which is CRLF on Windows. Splitting on '\n'
            // alone leaves the '\r' attached to the last field of every line, so a cached section
            // name would not compare equal to the registered name and would silently escape
            // name-keyed filters such as the catalog-hidden set.
            var line = raw.TrimEnd('\r');
            var parts = line.Split('\t');
            var name = parts[0];
            sections.Add(name);
            if (parts.Length >= 3)
                schema.Add(name, parts[1], parts[2].Split(','));
            else
                schema.AddSection(name);
        }
        return (sections, schema);
    }

    /// <summary>
    /// Stores a discovery catalog under the identity of the bytes it was derived from.
    /// </summary>
    /// <param name="inspectedContentHash">
    /// Content hash captured immediately <em>before</em> the inspection that produced
    /// <paramref name="sections"/>, or <see langword="null"/> when it was not captured.
    /// </param>
    private static void CacheEffective(string assemblyPath, bool hasSourceLink, List<string> sections,
        DocumentSchema filteredSchema, string? inspectedContentHash)
    {
        // The catalog describes the bytes the inspection parsed, but the key is derived from a
        // separate read that happens after it. If the assembly is replaced in between — an
        // ordinary rebuild racing a discovery run — the pre- and post-inspection hashes disagree,
        // and writing the entry would file this catalog under the *replacement's* identity, where
        // it would be served as a correct answer indefinitely. Declining to cache turns that
        // silent, persistent mislabelling into a recomputation on the next run.
        //
        // This narrows the window rather than closing it: the hash and the parse remain two reads,
        // so bytes that change and change back around the inspection still agree. Closing it needs
        // the inspection to report the identity of the image it actually parsed, tracked in #3478.
        var currentContentHash = TryGetContentHash(assemblyPath);
        if (currentContentHash == null) return;
        if (inspectedContentHash != null && !string.Equals(inspectedContentHash, currentContentHash, StringComparison.Ordinal))
            return;

        string key = BuildEffectiveCacheKey(assemblyPath, currentContentHash, hasSourceLink);
        var sb = new System.Text.StringBuilder();
        foreach (var name in sections)
        {
            var section = filteredSchema.GetSection(name);
            if (section != null && section.Items.Length > 0)
                sb.Append($"{name}\t{section.ItemKind}\t{string.Join(',', section.Items.Select(i => i.Name))}").Append('\n');
            else
                sb.Append(name).Append('\n');
        }
        CoreCache.Set(EffectiveCategory, key, sb.ToString(), extension: "tsv");
    }

    /// <summary>
    /// SHA-256 of an assembly's bytes, or <see langword="null"/> when they cannot be read.
    /// </summary>
    private static string? TryGetContentHash(string assemblyPath)
    {
        // A cached catalog is only valid for the bytes it was computed from, so the cache is keyed
        // by content. Neither size nor write time identifies content: a rebuild in place, or
        // copying a different assembly over the same path, routinely produces a same-sized file,
        // and a write time can be preserved by a copy, restored from an archive (whose recorded
        // stamps are coarse and often fixed for reproducibility), or shared by two writes that
        // land inside one filesystem timestamp tick. Any of those served the previous file's
        // catalog. Hashing removes the collision by construction rather than narrowing it:
        // different bytes cannot share a key. It costs a read and a SHA-256 pass — measured at
        // 9.8 ms for the largest assembly in this repository against a ~2.4 s warm discovery run,
        // so well under 1% of the work the cache exists to avoid.
        //
        // Gate: MetadataLensTests.LibraryCommand_DiscoverEffective_SameSizeReplacement_
        // InvalidatesCache and ..._PreservedWriteTimeReplacement_InvalidatesCache pin both
        // collisions; both fail if the key stops being content-derived.
        try
        {
            // Share the file as permissively as the inspector that is about to read it, so a
            // concurrent reader or a pending delete does not turn a cache lookup into a failure.
            using var stream = new FileStream(
                Path.GetFullPath(assemblyPath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The identity of the bytes is unknown, so no cache entry can be proven to describe
            // them. Callers bypass the cache rather than key on a weaker identity: this costs a
            // full discovery run, and the caller reads the same file immediately afterwards,
            // which surfaces the underlying failure with its own diagnostics.
            return null;
        }
    }

    internal static string BuildEffectiveCacheKey(string assemblyPath, string contentHash, bool hasSourceLink)
    {
        // Include a network-free SourceLink-availability token so warming/clearing a cached PDB
        // (which flips whether the SourceLink section family is effective) busts a stale -D
        // catalog.
        //
        // The path is resolved because the key is built from whatever the caller typed. A
        // relative path names different files from different working directories, so two
        // same-sized assemblies at the same relative path — the normal case for one repository
        // and its worktrees — otherwise share a key and serve each other's catalog.
        return $"{Path.GetFullPath(assemblyPath)}#{contentHash}#sl{(hasSourceLink ? 1 : 0)}";
    }

    private static List<string> FilterEffective(List<string> sections, LibraryOptions options)
    {
        if (options.IncludeSections is { Count: > 0 })
            sections = sections.Where(s => options.IncludeSections.Contains(s)).ToList();
        if (!HasILOffsetCoordinate(options))
            sections = sections.Where(s => !ILCoordinateSections.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        // Belt and braces, matching the IL-coordinate line above: the cache is never written while
        // a heap coordinate is present, so a cached listing should not carry this section — but a
        // catalog that advertises a section the coordinate cannot produce is exactly the failure
        // this family exists to avoid, so it is filtered rather than assumed absent.
        if (!HasHeapCoordinate(options))
            sections = sections.Where(s => !s.Equals(MetadataSectionNames.Heap, StringComparison.OrdinalIgnoreCase)).ToList();
        return sections;
    }

    private static int RenderEffective(List<string> effective, DocumentSchema schema, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline, Verbosity userVerbosity = Verbosity.Minimal,
        string? rootLabel = null)
    {
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
            verbosity: (int)userVerbosity, rootLabel: rootLabel,
            sectionCostAnnotations: pipeline.GetCostAnnotations(),
            sectionCategories: pipeline.GetCategoryMap(),
            catalogHiddenSections: EffectiveCatalogHidden(pipeline, effective),
            listedCategoryDoors: pipeline.GetListedCategoryDoors(),
            projection: options);
    }

    /// <summary>
    /// Renders the targeted sections and filters the schema to only fields that produced output.
    /// </summary>
    private static DocumentSchema FilterSchemaToEffectiveFields(LibraryInspection inspection,
        List<string> effectiveSections, DocumentSchema schema, SectionPipeline<LibraryInspection> pipeline,
        string[] discover)
    {
        // Resolve which sections are being discovered
        var targetSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in discover)
        {
            var resolved = schema.ResolveSection(d);
            if (resolved != null && effectiveSections.Contains(resolved))
                targetSections.Add(resolved);
        }
        if (targetSections.Count == 0) return schema;

        var filteredSections = new HashSet<string>(
            targetSections.Where(name =>
                string.Equals(name, LibrarySections.LibraryInfo.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, SectionNames.MemberContext, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        if (filteredSections.Count == 0)
            return schema;

        var view = new LibraryInspectionView(inspection);
        var writerOpts = new MarkoutWriterOptions { IncludeSections = filteredSections };
        var renderManifest = RenderManifestFormatter.Capture(
            view,
            InspectionContext.Default,
            writerOpts,
            schema);

        return DiscoverOutput.FilterSchemaToRenderedFields(
            effectiveSections,
            schema,
            renderManifest,
            filteredSections);
    }

    private static void WarnEmptySections(LibraryInspection inspection, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline) =>
        WarnEmptySections([inspection], options, pipeline);

    internal static void WarnEmptySections(IReadOnlyList<LibraryInspection> inspections, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline, bool writeEmptyNote = true)
    {
        var emptyResults = inspections
            .Select(inspection => pipeline.GetEmptySections(
                inspection, options.Verbosity, options.IncludeSections))
            .ToList();
        if (emptyResults.Count == 0)
            return;

        var empty = emptyResults[0].Empty
            .Where(section => emptyResults.Skip(1).All(
                result => result.Empty.Contains(section, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var requested = emptyResults[0].RequestedCount;
        var relevantFailures = inspections
            .Zip(emptyResults)
            .SelectMany(pair => (pair.First.InspectionFailures ?? [])
                .Where(failure => pair.Second.Empty.Any(
                    section => FailureAffectsSection(failure.Section, section)))
                .Select(failure => (Inspection: pair.First, Failure: failure)))
            .DistinctBy(entry => (entry.Inspection, entry.Failure))
            .ToList();
        foreach (var (inspection, failure) in relevantFailures)
        {
            var prefix = inspections.Count > 1
                ? LibraryViewText.DocumentTitle(inspection) + ": "
                : string.Empty;
            CommandError.WriteWarning(
                $"{prefix}{failure.Section} inspection failed "
                + $"({failure.Finding}): {failure.Reason}");
        }

        var unexplained = empty
            .Where(section => !relevantFailures.Any(
                entry => FailureAffectsSection(entry.Failure.Section, section)))
            .ToList();
        if (!options.Count
            && writeEmptyNote
            && unexplained.Count > 0
            && empty.Count == requested)
        {
            var label = unexplained.Count == 1 ? "section has" : "sections have";
            CommandError.WriteNote(
                $"{unexplained.Count} matched {label} no data: {string.Join(", ", unexplained)}.");
        }
    }

    internal static bool RejectEmptyExactSection(LibraryInspection inspection, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline) =>
        RejectEmptyExactSection([inspection], options, pipeline);

    private static bool RejectEmptyExactSection(IReadOnlyList<LibraryInspection> inspections, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline)
    {
        if (options.Count || options.IncludeSections is not { Count: 1 })
            return false;

        var section = options.IncludeSections.Single();
        if (options.ExactIncludeSections?.Contains(section) != true)
            return false;

        string? emptySection = null;
        foreach (var inspection in inspections)
        {
            var (empty, requested) = pipeline.GetEmptySections(
                inspection, options.Verbosity, options.IncludeSections);
            if (requested != 1 || empty.Count != 1)
                return false;

            emptySection ??= empty[0];
        }
        if (emptySection is null)
            return false;

        bool explainedByFailure = inspections.Any(inspection =>
            (inspection.InspectionFailures ?? []).Any(failure =>
                FailureAffectsSection(
                    failure.Section,
                    emptySection)));
        if (explainedByFailure)
        {
            WarnEmptySections(
                inspections,
                options,
                pipeline,
                writeEmptyNote: false);
            return true;
        }

        if (options.IntegrationQuery.HasFilter
            && section.StartsWith(IntegrationSectionNames.Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        CommandError.WriteLine($"This section ({emptySection}) produced no output.");
        return true;
    }

    internal static bool FailureAffectsSection(string failureSection, string section)
    {
        if (failureSection.Equals(section, StringComparison.OrdinalIgnoreCase))
            return true;

        if (failureSection.Equals(MetadataSectionNames.Image, StringComparison.Ordinal)
            && MetadataSectionNames.IsMetadataSection(section)
            && !section.Equals(MetadataSectionNames.ReadyToRun, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (failureSection.Equals("Classified Methods", StringComparison.Ordinal))
        {
            return section.Equals("Library Info", StringComparison.OrdinalIgnoreCase)
                   || section.Equals("P/Invoke Methods", StringComparison.OrdinalIgnoreCase)
                   || section.Equals("Async Methods", StringComparison.OrdinalIgnoreCase);
        }

        if (failureSection.Equals(
                SectionNames.PerformanceTriage,
                StringComparison.Ordinal))
        {
            return PerformanceKinds.Sections.Contains(
                section,
                StringComparer.OrdinalIgnoreCase);
        }

        if (section.Equals("Library Info", StringComparison.OrdinalIgnoreCase))
        {
            return failureSection is "Extension Methods"
                or "Resources"
                or "Custom Attributes"
                or "Type Forwarders"
                or "Union Types"
                or "Switches"
                or LibraryIntegrationCatalog.RollupName
                or EcosystemIntegrationNames.OpenTelemetry;
        }

        if (failureSection.Equals(EcosystemIntegrationNames.OpenTelemetry, StringComparison.Ordinal))
        {
            return section.Equals(IntegrationSectionNames.OpenTelemetry, StringComparison.OrdinalIgnoreCase);
        }

        return failureSection.Equals(LibraryIntegrationCatalog.RollupName, StringComparison.Ordinal)
               && LibraryIntegrationCatalog.All.Any(
                   descriptor => descriptor.SectionName.Equals(section, StringComparison.OrdinalIgnoreCase));
    }

    private static void ExtractResourcesIfRequested(string assemblyPath, LibraryOptions options)
    {
        if (string.IsNullOrEmpty(options.ExtractResources))
            return;

        using var session = AssemblyInspectionSession.Open(assemblyPath);
        var extracted = session.ExtractResources(options.ExtractResources);
        if (extracted.Count == 0)
        {
            CommandError.WriteLine("No embedded resources found.");
        }
        else
        {
            CommandError.WriteLine($"Extracted {extracted.Count} resource(s) to {options.ExtractResources}");
            foreach (var path in extracted)
            {
                CommandError.WriteLine($"  {Path.GetFileName(path)}");
            }
        }
    }

    private readonly record struct PackageInspectionCollection(
        List<LibraryInspection> Inspections,
        List<LibraryInspectionSubject> Subjects,
        List<(
            string FileName,
            IdentifierConfusionAuditFailureKind FailureKind)>
            IdentifierAuditFailures,
        List<(
            string FileName,
            CandidateOpenFailure Failure)> DescriptorSelectionFailures);

    private static async Task<PackageInspectionCollection>
        CollectPackageInspectionsAsync(
        List<string> assemblyPaths, LibraryOptions options, VerboseLogger logger,
        string? packageName, string? packageVersion, string extractPath,
        HttpClient httpClient, SignatureVerificationResult? signatureResult,
        InspectionQueryPlan<InspectionQueryContext>? queryPlan = null,
        AssemblyContextIntegrationsBatch? integrations = null,
        bool discoveryOnly = false, InspectionTrace? trace = null,
        IReadOnlyList<LibraryInspectionSubjectSelection>?
            subjectSelections = null)
    {
        List<LibraryInspection> inspections = [];
        List<LibraryInspectionSubject> subjects = [];
        List<(
            string FileName,
            IdentifierConfusionAuditFailureKind FailureKind)>
            identifierAuditFailures = [];
        List<(
            string FileName,
            CandidateOpenFailure Failure)> descriptorSelectionFailures = [];

        for (int index = 0; index < assemblyPaths.Count; index++)
        {
            string targetPath = assemblyPaths[index];
            var version = packageVersion ?? (packageName != null ? PackageExtractor.ExtractVersionFromPath(targetPath, packageName) : null);
            string relativePath = Path.GetRelativePath(
                    extractPath,
                    targetPath)
                .Replace('\\', '/');
            LibraryInspectionSubjectSelection subjectSelection =
                subjectSelections is not null
                    ? subjectSelections[index]
                    : LibraryInspectionSubject.Select(
                        targetPath,
                        PackageIntegrationProvenance(
                            targetPath,
                            extractPath,
                            packageName,
                            version),
                        integrations?.AssemblyForInspection(targetPath));
            if (subjectSelection
                is LibraryInspectionSubjectSelection.Rejected rejected)
            {
                descriptorSelectionFailures.Add(
                    (relativePath, rejected.Failure));
                CommandError.WriteWarning(
                    $"Could not select library descriptor for "
                    + $"'{targetPath}': {rejected.Failure.Detail}");
                continue;
            }
            LibraryInspectionSubject subject =
                ((LibraryInspectionSubjectSelection.Ready)subjectSelection)
                    .Subject;

            LibraryInspection? inspection;
            try
            {
                inspection = await LibraryMetadataService.InspectAsync(
                    targetPath,
                    options,
                    logger,
                    packageName,
                    version,
                    httpClient,
                    queryPlan: queryPlan,
                    assemblyReference: subject.AssemblyReference,
                    integrationsEntry:
                        integrations?.EntryFor(targetPath),
                    integrationOpportunitiesEntry:
                        integrations?.OpportunitiesEntryFor(targetPath),
                    discoveryOnly: discoveryOnly,
                    trace: trace);
            }
            catch (
                LibraryMetadataService
                    .IdentifierConfusionReferenceTraversalException ex)
            {
                identifierAuditFailures.Add(
                    (relativePath, ex.FailureKind));
                continue;
            }
            if (inspection == null)
            {
                logger.LogWarning($"Could not read library: {Path.GetFileName(targetPath)}");
                continue;
            }
            if (options.CollectIdentifierConfusionReferenceTree
                && inspection.IdentifierConfusionFailure is { } failure)
            {
                identifierAuditFailures.Add(
                    (relativePath, failure));
            }

            // Populate TFM from path for multi-TFM display
            inspection.Tfm = TfmResolver.ExtractTfmFromPath(relativePath);

            if (signatureResult != null)
            {
                inspection.Publisher = signatureResult.Publisher;
                inspection.PublisherVerified = signatureResult.AuthorVerified;
                inspection.RepositoryVerified = signatureResult.RepositoryVerified;
                inspection.SignatureStatus = signatureResult.StatusMessage;
            }

            inspections.Add(inspection);
            subjects.Add(subject);
        }

        return new PackageInspectionCollection(
            inspections,
            subjects,
            identifierAuditFailures,
            descriptorSelectionFailures);
    }

    private static AssemblyResolutionProvenance PackageIntegrationProvenance(
        string assemblyPath,
        string extractPath,
        string? packageName,
        string? packageVersion)
    {
        if (string.IsNullOrWhiteSpace(packageName)
            || string.IsNullOrWhiteSpace(packageVersion))
        {
            return AssemblyResolutionProvenance.Local(
                "library package extraction");
        }

        string relativePath = Path.GetRelativePath(
            extractPath,
            assemblyPath).Replace('\\', '/');
        return AssemblyResolutionProvenance.Package(
            packageName,
            packageVersion,
            TfmResolver.ExtractTfmFromPath(relativePath),
            rid: null);
    }

    private static async Task<(List<string> assemblyPaths, string extractPath, string? tempDir, string? nupkgPath, string? packageName, string? packageVersion)?> ExtractFromPackageAsync(
        string? assemblyName,
        string packageSource,
        string? tfm,
        NuGetSourceOptions? sourceOptions,
        bool includePrerelease,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(
            httpClient, packageSource, logger.Log, sourceOptions: sourceOptions, includePrerelease: includePrerelease);
        if (!outcome.IsSuccess)
        {
            CommandError.Write($"{outcome.ErrorMessage}");
            return null;
        }
        var resolution = outcome.Result!;

        string extractPath = resolution.ExtractPath;
        string? tempDir = resolution.TempDir;
        string? nupkgPath = resolution.NupkgPath;
        string? resolvedPackageName = resolution.PackageName;
        string? resolvedPackageVersion = resolution.Version;

        // Find DLLs in the extracted package
        string[] allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
        if (allDlls.Length == 0)
        {
            var payload = await TryResolveToolPayloadPackageAsync(
                resolution, packageSource, sourceOptions, logger, httpClient).ConfigureAwait(false);

            if (payload.Error != null)
            {
                CommandError.Write(payload.Error);
                DeleteTempDir(tempDir);
                return null;
            }

            if (payload.Result != null)
            {
                DeleteTempDir(tempDir);
                resolution = payload.Result;
                extractPath = resolution.ExtractPath;
                tempDir = resolution.TempDir;
                nupkgPath = resolution.NupkgPath;
                resolvedPackageName = resolution.PackageName;
                resolvedPackageVersion = resolution.Version;
                allDlls = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
            }
        }

        // --tfm all: return all assemblies from every TFM
        if (string.Equals(tfm, "all", StringComparison.OrdinalIgnoreCase))
        {
            var (candidates, _) = TfmSelector.SelectHighestAssembliesFromPackage(extractPath, tfm);
            if (candidates.Count == 0)
            {
                CommandError.Write("No DLLs found in package.");
                DeleteTempDir(tempDir);
                return null;
            }
            return (candidates, extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        // --tfm <specific>: find assembly by TFM
        if (!string.IsNullOrEmpty(tfm))
        {
            var tfmAssembly = TfmSelector.FindAssemblyByTfm(extractPath, tfm, resolution.PackageName);
            if (tfmAssembly == null)
            {
                CommandError.Write($"No library found for TFM '{tfm}'.");
                CommandError.WriteLine("Available TFMs:");
                var tfms = TfmSelector.GetPackageTfms(allDlls, extractPath);
                foreach (var t in tfms)
                {
                    CommandError.WriteLine($"  {t}");
                }
                DeleteTempDir(tempDir);
                return null;
            }
            logger.Log($"Using TFM: {tfm}");
            return ([tfmAssembly], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        // No --tfm and no assembly name: select the highest-priority TFM (default)
        if (string.IsNullOrEmpty(assemblyName))
        {
            var candidates = TfmSelector.GetPackageAssemblies(extractPath);
            if (candidates.Count == 0)
            {
                CommandError.Write("No DLLs found in package.");
                DeleteTempDir(tempDir);
                return null;
            }

            var (selectedPath, selectedTfm) = TfmSelector.SelectHighestTfmAssembly(candidates, extractPath, resolution.PackageName);
            if (selectedPath == null)
            {
                // No TFM structure found, fall back to first DLL
                return ([candidates[0]], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
            }

            logger.Log($"Using TFM: {selectedTfm}");
            return ([selectedPath], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        var (matchedAssembly, matchedTfm) = TfmSelector.FindAssemblyInPackage(extractPath, assemblyName, tfm);
        if (matchedAssembly == null)
        {
            CommandError.Write($"Library '{assemblyName}' not found in package.");
            CommandError.WriteLine("Use 'dotnet-inspect package <name> --path \"lib/\"' to list available libraries.");
            DeleteTempDir(tempDir);
            return null;
        }

        if (matchedTfm != null)
            logger.Log($"Using TFM: {matchedTfm}");

        logger.Log($"Found: {Path.GetRelativePath(extractPath, matchedAssembly)}");
        return ([matchedAssembly], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
    }

    private sealed record ToolPayloadResolution(PackageExtractionResult? Result, string? Error);

    private static async Task<ToolPayloadResolution> TryResolveToolPayloadPackageAsync(
        PackageExtractionResult package,
        string originalPackageSource,
        NuGetSourceOptions? sourceOptions,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var payloadId = GetToolPayloadPackageId(package.ExtractPath, package.PackageName);
        if (payloadId == null)
            return new(null, null);

        var version = package.Version ?? GetNuspecVersion(package.ExtractPath);
        if (version == null)
            return new(null, $"Tool package '{package.PackageName}' has no DLLs and its version could not be determined.");

        var localPayload = TryFindLocalSiblingPackage(originalPackageSource, payloadId, version);
        var payloadOutcome = localPayload != null
            ? await PackageExtractor.ExtractPackageAsync(httpClient, localPayload, logger.Log).ConfigureAwait(false)
            : await PackageExtractor.ExtractPackageAsync(
                httpClient, payloadId, logger.Log, sourceOptions: sourceOptions, version: version).ConfigureAwait(false);

        if (!payloadOutcome.IsSuccess)
            return new(null, $"Tool package '{package.PackageName}' has no inspectable DLLs and payload package '{payloadId}@{version}' could not be resolved: {payloadOutcome.ErrorMessage}");

        var payload = payloadOutcome.Result!;
        var dlls = Directory.GetFiles(payload.ExtractPath, "*.dll", SearchOption.AllDirectories)
            .Where(d => !d.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dlls.Count == 0)
        {
            DeleteTempDir(payload.TempDir);
            return new(null, $"Tool payload package '{payload.PackageName}@{payload.Version}' does not contain inspectable .NET DLLs.");
        }

        logger.Log($"Tool package has no DLLs; inspecting payload package: {payload.PackageName} {payload.Version}");
        return new(payload, null);
    }

    private static string? GetToolPayloadPackageId(string extractPath, string? packageName)
    {
        var toolsDir = Path.Combine(extractPath, "tools");
        if (Directory.Exists(toolsDir))
        {
            var settings = DotnetToolSettingsParser.FindAndParse(toolsDir);
            var anyPayload = settings?.RuntimeIdentifierPackages?
                .FirstOrDefault(r => r.RuntimeIdentifier.Equals("any", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(anyPayload?.PackageId))
                return anyPayload.PackageId;
        }

        return TryGetSiblingAnyPackageId(packageName);
    }

    private static string? TryGetSiblingAnyPackageId(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return null;

        string[] knownRidSuffixes =
        [
            ".win-x64",
            ".win-arm64",
            ".linux-x64",
            ".linux-arm64",
            ".osx-arm64"
        ];

        var suffix = knownRidSuffixes.FirstOrDefault(s =>
            packageName.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        return suffix == null ? null : packageName[..^suffix.Length] + ".any";
    }

    private static string? TryFindLocalSiblingPackage(string originalPackageSource, string payloadId, string version)
    {
        if (!originalPackageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            return null;

        var directory = Path.GetDirectoryName(Path.GetFullPath(originalPackageSource));
        if (directory == null)
            return null;

        var exact = Path.Combine(directory, $"{payloadId}.{version}.nupkg");
        if (File.Exists(exact))
            return exact;

        return Directory.GetFiles(directory, $"{payloadId}.*.nupkg")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? GetNuspecVersion(string extractPath)
    {
        return NuspecParser.FindAndParse(extractPath)?.Version;
    }

    private static void DeleteTempDir(string? tempDir)
    {
        if (tempDir == null)
            return;

        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

}

internal sealed record LibraryInspectionSubject(
    string Path,
    ResolvedAssemblyReference? AssemblyReference)
{
    internal static LibraryInspectionSubjectSelection Select(
        string path,
        AssemblyResolutionProvenance provenance,
        ResolvedAssemblyReference? preferredAssembly = null)
    {
        if (preferredAssembly is not null)
        {
            return new LibraryInspectionSubjectSelection.Ready(
                new LibraryInspectionSubject(path, preferredAssembly));
        }

        return ResolvedAssemblyReference.SelectFromPath(path, provenance)
            switch
            {
                AssemblyDescriptorSelectionResult.Ready ready =>
                    new LibraryInspectionSubjectSelection.Ready(
                        new LibraryInspectionSubject(path, ready.Reference)),
                AssemblyDescriptorSelectionResult.Descriptorless =>
                    new LibraryInspectionSubjectSelection.Ready(
                        new LibraryInspectionSubject(path, null)),
                AssemblyDescriptorSelectionResult.Rejected rejected =>
                    new LibraryInspectionSubjectSelection.Rejected(
                        rejected.Failure),
                _ => throw new UnreachableException(),
            };
    }

    internal SourceLinkService OpenSourceLink(Action<string>? log = null) =>
        AssemblyReference is null
            ? SourceLinkService.Open(Path, log)
            : SourceLinkService.Open(AssemblyReference, log);
}

internal abstract record LibraryInspectionSubjectSelection
{
    private LibraryInspectionSubjectSelection()
    {
    }

    internal sealed record Ready(LibraryInspectionSubject Subject)
        : LibraryInspectionSubjectSelection;

    internal sealed record Rejected(CandidateOpenFailure Failure)
        : LibraryInspectionSubjectSelection;
}

internal sealed record ILCoordinateInput(string Coordinate, string? Label);

internal sealed record ILCoordinateReadError(string Label, string Error);

internal sealed record ILCoordinateBatchResult(List<ILCoordinateBatchRow> Rows);

[MarkoutSerializable]
internal sealed record ILCoordinateBatchRow(
    [property: MarkoutSkipNull] string? Coordinate,
    [property: MarkoutSkipNull] string? Label,
    [property: MarkoutSkipNull] string? Member,
    [property: MarkoutPropertyName("IL Offset")]
    [property: MarkoutSkipNull] string? ILOffset,
    string Meaning,
    string Evidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ILCoordinateBatchResult))]
internal partial class ILCoordinateBatchJsonContext : JsonSerializerContext
{
}
