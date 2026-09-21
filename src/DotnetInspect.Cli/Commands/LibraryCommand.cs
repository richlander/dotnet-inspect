using DotnetInspector.Cache;
using DotnetInspect.Cli.CommandLine;
using DotnetInspector.Ecosystems;
using DotnetInspector.MetadataRendering;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;
using ILInspector.Research;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using SignatureVerificationResult = DotnetInspector.Services.SignatureVerificationResult;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using DotnetInspector.SourceSelection;
using Markout;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using InertText;
using Inspector.Findings;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Inspects a single .NET assembly.
/// </summary>
public partial class LibraryCommand
{
    internal static DocumentSchema CreateStructuralSchema()
    {
        DocumentSchema schema = WithReferenceHierarchySchema(
            MetadataSectionNames.AugmentSchema(
                InspectionContext.Default
                    .GetSchemaInfo<LibraryInspectionView>()!
                    .ToDocumentSchema()));
        AddCloneCandidateSchema(schema);
        return schema;
    }

    private static DocumentSchema WithReferenceHierarchySchema(
        DocumentSchema schema)
    {
        var hierarchy =
            DependsAssetSections.CreateSchema().GetSection(
                DependsAssetSections.DependencyHierarchy)
            ?? throw new InvalidOperationException(
                "The shared Depends hierarchy schema is unavailable.");
        var result = new DocumentSchema();
        foreach (string name in schema.SectionNames)
        {
            var section =
                name.Equals(
                    SectionNames.ReferenceHierarchy,
                    StringComparison.OrdinalIgnoreCase)
                    ? hierarchy
                    : schema.GetSection(name);
            if (section is { Items.Length: > 0 })
            {
                result.Add(
                    name,
                    section.ItemKind,
                    section.Items.Select(static item => item.Name).ToArray());
            }
            else
            {
                result.AddSection(name);
            }
        }

        return result;
    }

    internal static void AddCloneCandidateSchema(DocumentSchema schema)
        => CloneCandidatesCommand.AddStructuralSchema(schema);

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
                : BodyKindQueryOptions.Sections.Contains(
                    section,
                    StringComparer.OrdinalIgnoreCase)
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
        new("ReadyToRun applicability", ReadyToRunImageQuery.Definition),
        new("References applicability", AssemblyReferencesQuery.Definition),
    ];

    internal static readonly HostQueryDemand[]
        BareDiscoveryQueries =
        [
            new("Unsafe Members applicability",
                UnsafeEvidencePresenceQuery.Definition),
        ];

    public static Task<int> ExecuteAsync(LibraryOptions options) =>
        ExecuteAsync(options, workspaceLoadOptions: null);

    internal static async Task<int> ExecuteAsync(
        LibraryOptions options,
        WorkspaceContextLoadOptions? workspaceLoadOptions)
    {
        if (!LibrarySourceAdapter.TryBind(
                options,
                out LibrarySourceBinding? source,
                out string? sourceError))
        {
            CommandError.Write(sourceError!);
            return 1;
        }

        options = source!.ApplyTo(options);
        if (source.Selector is SourceSelector.PackageSource
            && (options.WorkspacePacket is not null
                || options.NamesakeLibrary
                || string.IsNullOrWhiteSpace(options.AssemblyName)))
        {
            return await ExecutePackageAsync(
                options,
                source.PackageTarget,
                workspaceLoadOptions).ConfigureAwait(false);
        }

        return await ExecuteBoundAsync(
            options,
            source,
            preResolvedPackage: null).ConfigureAwait(false);
    }

    internal static async Task<int> ExecuteResolvedPackageAsync(
        LibraryOptions options,
        PackageExtractionResult resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (!LibrarySourceAdapter.TryBind(
                options,
                out LibrarySourceBinding? source,
                out string? sourceError))
        {
            CommandError.Write(sourceError!);
            return 1;
        }

        options = source!.ApplyTo(options);
        if (source.Selector is not SourceSelector.PackageSource)
        {
            CommandError.Write(
                "Resolved Package Library execution requires a Package "
                    + "source selector.");
            return 1;
        }

        return await ExecuteBoundAsync(
            options,
            source,
            resolution).ConfigureAwait(false);
    }

    static async Task<int> ExecuteBoundAsync(
        LibraryOptions options,
        LibrarySourceBinding source,
        PackageExtractionResult? preResolvedPackage)
    {
        if (!options.Trace)
        {
            return await ExecuteCoreAsync(
                options,
                source,
                trace: null,
                preResolvedPackage).ConfigureAwait(false);
        }

        // Rendered in a finally so a failed run still reports the work it did before failing —
        // which is exactly when "what did this actually scan?" is worth knowing.
        var trace = new InspectionTrace
        {
            Command = new InertString(
                TextPolicy.Field,
                options.CoordinateRequest is not null
                    ? "library coordinate"
                    : "library"),
            Target = new InertString(
                TextPolicy.Field,
                source.Target),
        };

        try
        {
            return await ExecuteCoreAsync(
                options,
                source,
                trace,
                preResolvedPackage).ConfigureAwait(false);
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

    private static async Task<int> ExecuteCoreAsync(
        LibraryOptions options,
        LibrarySourceBinding source,
        InspectionTrace? trace,
        PackageExtractionResult? preResolvedPackage)
    {
        if (options.IntegrationQuery.HasFilter
            && (options.BodyKindQuery.HasFilter || options.PerformanceTriage.HasFilters
                || options.PerformanceTriage.HasRanking
                || options.CloneCandidateQuery.HasPredicates))
        {
            CommandError.Write(
                "Integration ecosystem queries cannot be combined with Clone Candidates, "
                + "Body Shapes, or Performance Triage predicates/ranking.");
            return 1;
        }
        if (options.IntegrationQuery.HasFilter
            && (options.ExtractResources is not null
                || options.Print || options.Value || options.Urls || options.Paths))
        {
            CommandError.Write(
                "Integration ecosystem queries support section rows, columns, and counts, not coordinate or extraction operations.");
            return 1;
        }
        var assemblyPath = source.AssemblyName;
        var catalog = LibrarySections.CreateCatalog();
        var sections = catalog.Sections;
        var pipeline = catalog.Pipeline;
        var queryCatalog = catalog.QueryCatalog;
        var groupQueryCatalog = catalog.GroupQueryCatalog;

        var schemaMap = CreateStructuralSchema();
        bool hasInputSource = source.Selector is not null;

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
        if (options.IncludeDependencies)
        {
            CommandError.Write(
                "--dependencies has been removed. Use '-S \"Reference Hierarchy\" --tree'.");
            return 1;
        }
        options = NormalizeReferenceProjection(options);

        if (GetDiscoveryModeError(
                options.Effective,
                options.Discover is not null,
                options.Schema) is { } discoveryModeError)
        {
            CommandError.Write(discoveryModeError);
            return 1;
        }

        if (MetadataRootDiscoveryModeError(
                options,
                hasInputSource) is { } metadataRootDiscoveryError)
        {
            CommandError.Write(metadataRootDiscoveryError);
            return 1;
        }

        if (options.Discover is not null && options.Schema)
        {
            return StructuralViewRegistry.Execute(
                StructuralViewRegistry.Route(
                    options.CoordinateRequest is null
                        ? StructuralViewIdentity.DirectLibrary
                        : StructuralViewIdentity.LibraryCoordinate,
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
                    || options.Discover.Length == 0);
            if (requiresInspection)
            {
                // Handled after data collection below.
            }
            else
            {
                return StructuralViewRegistry.Execute(
                    StructuralViewRegistry.Route(
                        options.CoordinateRequest is null
                            ? StructuralViewIdentity.DirectLibrary
                            : StructuralViewIdentity.LibraryCoordinate,
                        InspectionCatalogIdentity.Library),
                    StructuralDiscoveryRequest.From(options));
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

        var normalized = NormalizeILCoordinateSelection(options);
        if (normalized.Error is not null)
        {
            CommandError.Write(normalized.Error);
            return 1;
        }
        options = normalized.Options;

        var heapNormalized = NormalizeHeapCoordinateSelection(options);
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
        var cloneSelection = SelectResolver.NormalizeExactOnlySection(
            options.Select,
            options.IncludeSections,
            options.ExactIncludeSections,
            sections.SelectableSectionNames,
            SectionNames.CloneCandidates);
        if (cloneSelection.Error is not null)
        {
            CommandError.Write(cloneSelection.Error);
            return 1;
        }
        options = options with { IncludeSections = cloneSelection.Sections };
        var implementationProfilesSelection =
            SelectResolver.NormalizeExactOnlySection(
                options.Select,
                options.IncludeSections,
                options.ExactIncludeSections,
                sections.SelectableSectionNames,
                SectionNames.ImplementationProfiles);
        if (implementationProfilesSelection.Error is not null)
        {
            CommandError.Write(
                implementationProfilesSelection.Error);
            return 1;
        }
        options = options with
        {
            IncludeSections =
                implementationProfilesSelection.Sections,
        };

        if (MetadataRootSelectionError(options) is { } metadataRootError)
        {
            CommandError.Write(metadataRootError);
            return 1;
        }

        if (options.Discover is null || fullEffectiveDiscovery)
        {
            if (options.IntegrationQuery.HasFilter)
            {
                string[] integrationSections =
                    [
                        IntegrationSectionNames.Integrations,
                        IntegrationSectionNames.Opportunities,
                    ];
                if (options.IncludeSections is not { Count: > 0 })
                {
                    options = options with
                    {
                        IncludeSections =
                            [IntegrationSectionNames.Integrations],
                        FixedOverview = false,
                    };
                }
                else if (!options.IncludeSections.Overlaps(integrationSections))
                {
                    CommandError.Write(
                        "Integration --where predicates target Integrations. "
                        + "Omit -S or include Integrations or Integration Opportunities.");
                    return 1;
                }
            }
            bool bodyShapesSelected = BodyKindQueryOptions.IsSelected(options.IncludeSections);
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
                    $"--where Kind=... targets section '{SectionNames.BodyShapes}' "
                    + $"or '{SectionNames.BodyShapeSummary}'. Omit -S or select one of these sections.");
                return 1;
            }
            if (bodyShapesSelected && !options.BodyKindQuery.HasFilter)
            {
                CommandError.Write(
                    $"Section '{options.IncludeSections!.First(section => BodyKindQueryOptions.Sections.Contains(
                        section, StringComparer.OrdinalIgnoreCase))}' requires "
                    + "--where \"Kind=<C# Body Kinds ID>\".");
                return 1;
            }
            bool cloneCandidatesSelected =
                CloneCandidatesCommand.IsSelected(
                    options.IncludeSections);
            if (options.CloneCandidateQuery.HasPredicates
                && options.IncludeSections is not { Count: > 0 })
            {
                options = options with
                {
                    IncludeSections =
                    [
                        SectionNames.CloneCandidates,
                    ],
                    ExactIncludeSectionsOverride =
                    [
                        SectionNames.CloneCandidates,
                    ],
                };
                cloneCandidatesSelected = true;
            }
            if (options.CloneCandidateQuery.HasPredicates
                && !cloneCandidatesSelected)
            {
                CommandError.Write(
                    $"--where Breadth=... and Discovery=... target section '{SectionNames.CloneCandidates}'. "
                    + "Omit -S or select that section.");
                return 1;
            }
        }

        options = options with
        {
            UserIncludeSectionsOverride = options.IncludeSections is { Count: > 0 }
                ? new HashSet<string>(options.IncludeSections, StringComparer.OrdinalIgnoreCase)
                : [],
        };

        if (options.JsonOutput
            && !options.Count
            && options.IncludeSections is { Count: > 0 }
            && !LibraryOutputCapabilities.Catalog.Supports(
                DiscoveryOutputMode.Json,
                options.IncludeSections))
        {
            if (options.IncludeSections.Contains(
                    SectionNames.ImplementationProfiles))
            {
                CommandError.Write(
                    "Document --json cannot represent Implementation Profiles analysis. "
                    + "Use --jsonl, --tsv, or --table.");
            }
            else
            {
                CommandError.Write(
                    "Document --json with Reference Hierarchy requires that section to be selected alone.");
            }
            return 1;
        }

        if (options.ReferenceHierarchyDepth is < 1)
        {
            CommandError.Write("--depth must be at least 1.");
            return 1;
        }

        if (options.ReferenceHierarchyDepth is not null
            && (options.Discover != null
                || options.IncludeSections is not { Count: 1 }
                || !options.IncludeSections.Contains(
                    SectionNames.ReferenceHierarchy)))
        {
            CommandError.Write(
                "--depth requires exactly '-S \"Reference Hierarchy\"'.");
            return 1;
        }

        if (!ValidateMultiTfmOutput(options))
            return 1;

        if (options.Tree && options.Discover == null)
        {
            if (!LibraryOutputCapabilities.Catalog.Supports(
                    DiscoveryOutputMode.Tree,
                    options.IncludeSections))
            {
                CommandError.Write(
                    options.IncludeSections is { Count: 1 }
                    && options.IncludeSections.Contains(
                        SectionNames.References)
                        ? "References is direct evidence and cannot be rendered as a hierarchy. Use '-S \"Reference Hierarchy\" --tree'."
                        : "--tree requires exactly '-S \"Reference Hierarchy\"'.");
                return 1;
            }
        }

        if (options.Format == OutputFormat.Mermaid
            && options.Discover == null
            && !options.Count
            && !LibraryOutputCapabilities.Catalog.Supports(
                DiscoveryOutputMode.Mermaid,
                options.IncludeSections))
        {
            CommandError.Write(
                options.IncludeSections is { Count: 1 }
                && options.IncludeSections.Contains(
                    SectionNames.References)
                    ? "References is direct evidence and has no Mermaid topology. Use '-S \"Reference Hierarchy\" --mermaid'."
                    : "--mermaid requires exactly '-S \"Reference Hierarchy\"'.");
            return 1;
        }

        if (options.Tree && options.Discover == null)
        {
            if (options.Print
                || options.Value
                || options.Urls
                || options.Paths
                || options.Columns is { Length: > 0 }
                || options.Fields is { Length: > 0 }
                || options.Count
                || options.JsonOutput
                || options.JsonArray
                || options.Tabular
                || options.Tsv
                || options.Jsonl
                || options.NoHeader
                || options.TabularExplicitlySet)
            {
                CommandError.Write(
                    "--tree cannot be combined with count, shape, tabular, JSON, or field/column projections.");
                return 1;
            }
        }

        bool referenceHierarchySelected =
            options.IncludeSections?.Contains(
                SectionNames.ReferenceHierarchy) == true;
        if (referenceHierarchySelected
            && options.IncludeSections is { Count: > 1 }
            && (options.Columns is { Length: > 0 }
                || options.Fields is { Length: > 0 }))
        {
            CommandError.Write(
                "--columns/--fields with Reference Hierarchy requires that section to be selected alone.");
            return 1;
        }
        if (referenceHierarchySelected
            && (options.Print
                || options.Value
                || options.Urls
                || options.Paths))
        {
            CommandError.Write(
                "Reference Hierarchy supports document, count, row, tree, and tabular projections, not shape or print projections.");
            return 1;
        }
        if (!string.IsNullOrEmpty(options.OutputPath)
            && !options.Count
            && (options.IncludeSections is not { Count: 1 }
                || !referenceHierarchySelected))
        {
            CommandError.Write(
                "--out currently requires exactly '-S \"Reference Hierarchy\"' or --count for library inspection.");
            return 1;
        }

        if (options.CoordinateRequest
                is LibraryCoordinateRequest.HeapPoint
            && options.IncludeSections is { Count: > 0 }
            && !options.IncludeSections.Contains(MetadataSectionNames.Heap))
        {
            CommandError.Write(
                $"library coordinate requires the heap coordinate section. "
                + $"Omit -S or include -S \"{MetadataSectionNames.Heap}\".");
            return 1;
        }

        if (options.CoordinateRequest
                is LibraryCoordinateRequest.IlPoint
            && options.IncludeSections is { Count: > 0 }
            && !options.IncludeSections.Overlaps(ILCoordinateSections))
        {
            CommandError.Write(
                $"library coordinate requires an IL coordinate section. "
                + $"Omit -S or include -S \"{SectionNames.ILOffset}\", "
                + $"-S \"{SectionNames.MemberContext}\", "
                + $"-S \"{SectionNames.InstructionContext}\", "
                + $"-S \"{SectionNames.ExceptionContext}\", "
                + $"-S \"{SectionNames.CallsiteContext}\", or "
                + $"-S \"{SectionNames.ReturnAddressContext}\".");
            return 1;
        }

        // Coordinate file mode counts resolved coordinate rows, not section rows, so it does not
        // need a section filter to make --count meaningful.
        var ilOffsetsBatchMode =
            options.CoordinateRequest
                is LibraryCoordinateRequest.FilePopulation;

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
            && !CloneCandidatesCommand.IsSelected(
                options.IncludeSections)
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
                options.TabularExplicitlySet,
                options.IncludeSections,
                sections =>
                    LibraryOutputCapabilities.Catalog.Supports(
                        DiscoveryOutputMode.Table,
                        sections)))
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
            && options.CoordinateRequest is null
            && options.MetadataRoot == MetadataRootKind.Cli;

        if (trace is not null)
            trace.Verbosity = new InertString(TextPolicy.Field, options.Verbosity.ToString());
        List<HostQueryDemand> commandQueryDemand = [];
        if (discoveryInspection)
        {
            commandQueryDemand.AddRange(DiscoveryQueries);
            if (options.Discover is { Length: 0 })
                commandQueryDemand.AddRange(BareDiscoveryQueries);
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
        bool wantsEcosystemDependencies =
            sectionPlan.Demands.Any(
                static demand =>
                    demand.Section == SectionNames.LibraryInfo
                    || demand.Section
                        == SectionNames.EcosystemDependencies);
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
                CollectIdentifierConfusionReferenceTree =
                    candidates.Contains(SectionNames.IdentifierConfusion),
            };
        }

        // Check for valid input source
        if (source.Selector is null)
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
            string? packageName = null;
            string? packageVersion = null;

            if (source.Selector
                is SourceSelector.PlatformLibrary platform)
            {
                var (resolvedPath, framework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                    platform.Name,
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
                if (options.CoordinateRequest
                        is LibraryCoordinateRequest.FilePopulation
                    && options.Discover is null)
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
                    await AssemblyContextIntegrationsRunner.RunIfRequestedAsync(
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
                if (CloneCandidatesCommand.IsSelected(
                        options.IncludeSections))
                {
                    return await ExecuteCloneCandidatesAsync(
                        subject,
                        options,
                        rootPackageDirectory: null);
                }

                // Network-free SourceLink availability probe: drives the SourceLink section
                // family in -D and keys the effective cache so a warmed/cleared PDB busts a
                // stale catalog. Skipped (false) outside discovery.
                bool sourceLinkAvailable = fullEffectiveDiscovery
                    && options.CoordinateRequest
                        is not LibraryCoordinateRequest.IlPoint
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
                ApplyLibraryEcosystemDependencies(
                    inspection,
                    subject,
                    wantsEcosystemDependencies,
                    RequiresLibraryEcosystemDiagnosticDisclosure(options),
                    logger);
                if (!discoveryInspection)
                {
                    await PopulateReferenceHierarchyAsync(
                        inspection,
                        resolvedPath!,
                        options,
                        context);
                }
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
                int heapExitCode = PopulateMetadataHeapIfRequested(inspection, options, logger);
                if (heapExitCode != 0)
                    return heapExitCode;
                if (discoveryInspection)
                    return WriteEffectiveSections(
                        resolvedPath!, inspection, options, pipeline, userVerbosity,
                        fullEffectiveDiscovery, discoveryExecutionScope, sourceLinkAvailable,
                        cache: useEffectiveDiscoveryCache,
                        inspectedContentHash: inspectedContentHash);
                if (!TrySelectAssemblyReferences(inspection, options.ReferenceRowSelection))
                    return 1;
                if (!TrySelectLibraryEcosystemDependencies(
                        inspection,
                        options.EcosystemDependencyRowSelection))
                {
                    return 1;
                }
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
                if (RejectInexactReferenceHierarchyCount(
                        options,
                        inspection))
                {
                    return 1;
                }

                OutputFormatter.WriteLibraryResult(inspection, options, pipeline);
                return Math.Max(
                    IntegrityExitCode(inspection),
                    SelectedInspectionFailureExitCode(
                        options,
                        pipeline,
                        inspection));
            }
            else if (source.Selector
                is SourceSelector.PackageSource)
            {
                // Extract from package
                var extractResult = await ExtractFromPackageAsync(
                    assemblyPath,
                    source.PackageTarget!,
                    options.Tfm,
                    options.SourceOptions,
                    options.IncludePrerelease,
                    logger,
                    context.HttpClient,
                    preResolvedPackage);
                if (extractResult == null)
                {
                    return 1;
                }

                var (assemblyPaths, extractPath, extractTempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion) = extractResult.Value;
                tempDir = extractTempDir;
                packageName = resolvedPackageName;
                packageVersion = resolvedPackageVersion;

                if (options.CoordinateRequest
                        is LibraryCoordinateRequest.FilePopulation
                    && options.Discover is null)
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
                    await AssemblyContextIntegrationsRunner.RunIfRequestedAsync(
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
                if (CloneCandidatesCommand.IsSelected(
                        options.IncludeSections))
                {
                    if (subjectSelections.Count != 1
                        || primaryReady is null)
                    {
                        CommandError.Write(
                            $"Section '{SectionNames.CloneCandidates}' requires one exact library. "
                            + "Name the assembly within the package.");
                        return 1;
                    }

                    return await ExecuteCloneCandidatesAsync(
                        primaryReady.Subject,
                        options,
                        extractPath);
                }

                // Network-free SourceLink availability probe (see platform branch).
                bool sourceLinkAvailable = fullEffectiveDiscovery
                    && primaryReady is not null
                    && options.CoordinateRequest
                        is not LibraryCoordinateRequest.IlPoint
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
                if (wantsEcosystemDependencies)
                {
                    for (int index = 0;
                         index < inspections.Count;
                         index++)
                    {
                        ApplyLibraryEcosystemDependencies(
                            inspections[index],
                            collection.Subjects[index],
                            wantsEcosystemDependencies: true,
                            RequiresLibraryEcosystemDiagnosticDisclosure(
                                options),
                            logger);
                    }
                }
                if (!discoveryInspection)
                {
                    for (int index = 0;
                         index < inspections.Count;
                         index++)
                    {
                        await PopulateReferenceHierarchyAsync(
                            inspections[index],
                            collection.Subjects[index].Path,
                            options,
                            context);
                    }
                }
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
                int heapExitCode = PopulateMetadataHeapIfRequested(inspections[0], options, logger);
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
                if (inspections.Count == 1
                    && !TrySelectAssemblyReferences(
                        inspections[0],
                        options.ReferenceRowSelection))
                {
                    return 1;
                }
                if (inspections.Count == 1
                    && !TrySelectLibraryEcosystemDependencies(
                        inspections[0],
                        options.EcosystemDependencyRowSelection))
                {
                    return 1;
                }
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
                if (RejectInexactReferenceHierarchyCount(
                        options,
                        [.. inspections]))
                {
                    return 1;
                }

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
                if (options.CoordinateRequest
                        is LibraryCoordinateRequest.FilePopulation
                    && options.Discover is null)
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
                    await AssemblyContextIntegrationsRunner.RunIfRequestedAsync(
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
                if (CloneCandidatesCommand.IsSelected(
                        options.IncludeSections))
                {
                    return await ExecuteCloneCandidatesAsync(
                        subject,
                        options,
                        rootPackageDirectory: null);
                }

                // Network-free SourceLink availability probe (see platform branch).
                bool sourceLinkAvailable = fullEffectiveDiscovery
                    && options.CoordinateRequest
                        is not LibraryCoordinateRequest.IlPoint
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
                ApplyLibraryEcosystemDependencies(
                    inspection,
                    subject,
                    wantsEcosystemDependencies,
                    RequiresLibraryEcosystemDiagnosticDisclosure(options),
                    logger);
                if (!discoveryInspection)
                {
                    await PopulateReferenceHierarchyAsync(
                        inspection,
                        assemblyPath!,
                        options,
                        context);
                }
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
                int heapExitCode = PopulateMetadataHeapIfRequested(inspection, options, logger);
                if (heapExitCode != 0)
                    return heapExitCode;
                if (discoveryInspection)
                    return WriteEffectiveSections(
                        assemblyPath!, inspection, options, pipeline, userVerbosity,
                        fullEffectiveDiscovery, discoveryExecutionScope, sourceLinkAvailable,
                        cache: useEffectiveDiscoveryCache,
                        inspectedContentHash: inspectedContentHash);
                if (!TrySelectAssemblyReferences(inspection, options.ReferenceRowSelection))
                    return 1;
                if (!TrySelectLibraryEcosystemDependencies(
                        inspection,
                        options.EcosystemDependencyRowSelection))
                {
                    return 1;
                }
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
                if (RejectInexactReferenceHierarchyCount(
                        options,
                        inspection))
                {
                    return 1;
                }

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
        foreach (DependsAssetProjection projection in inspections
                     .Select(static inspection =>
                         inspection.ReferenceHierarchyProjection)
                     .OfType<DependsAssetProjection>())
        {
            DependsCommand.WriteAssetDiagnostics(projection);
            currentExitCode = Math.Max(
                currentExitCode,
                DependsCommand.AssetExitCode(projection));
        }

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

    internal static async Task PopulateReferenceHierarchyAsync(
        LibraryInspection inspection,
        string assemblyPath,
        LibraryOptions options,
        CommandContext context)
    {
        if (options.IncludeSections?.Contains(
                SectionNames.ReferenceHierarchy) != true)
        {
            return;
        }

        inspection.ReferenceHierarchyProjection =
            await DependsCommand.AcquireLibrarySubjectProjectionAsync(
                assemblyPath,
                inspection.Tfm ?? options.Tfm,
                options.SourceOptions,
                options.ReferenceHierarchyDepth,
                context);
    }

    internal static bool RejectInexactReferenceHierarchyCount(
        LibraryOptions options,
        params LibraryInspection[] inspections)
    {
        if (!options.Count
            || options.IncludeSections is not { Count: 1 }
            || !options.IncludeSections.Contains(
                SectionNames.ReferenceHierarchy))
        {
            return false;
        }

        if (inspections.All(
                static inspection =>
                    inspection.ReferenceHierarchyProjection is { } projection
                    && DependsCommand.IsExactAssetRowSet(
                        projection,
                        DependsAssetSections.DependencyHierarchy)))
        {
            return false;
        }

        CommandError.Write(
            "--count cannot report an exact 'Reference Hierarchy' count because the requested reference evidence is incomplete.");
        return true;
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

    private static Task<int> ExecuteCloneCandidatesAsync(
        LibraryInspectionSubject subject,
        LibraryOptions options,
        string? rootPackageDirectory)
    {
        if (subject.AssemblyReference is not { } assembly)
        {
            CommandError.Write(
                $"Section '{SectionNames.CloneCandidates}' requires a managed assembly with a readable identity.");
            return Task.FromResult(1);
        }

        return CloneCandidatesCommand.ExecuteAsync(
            assembly,
            subject.Path,
            new StructuralCloneSearchSeed.Library(),
            options.CloneCandidateQuery,
            CloneCandidateOutputOptions.From(options),
            new CloneCandidateWorkspaceOptions(
                rootPackageDirectory,
                ProjectAssetsPath: null,
                options.Tfm,
                options.SourceOptions));
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
        if (options.CoordinateRequest
            is not LibraryCoordinateRequest.FilePopulation
            {
                Population: { } population,
            })
        {
            throw new UnreachableException(
                "Coordinate file execution requires an admitted population.");
        }

        HashSet<string> sections = options.IncludeSections is { Count: > 0 }
            ? [.. options.IncludeSections]
            : [.. BatchCoordinateSections];

        IEnumerable<ILCoordinatePopulationRecord> records =
            population.Records;

        var rows = new List<ILCoordinateBatchRow>();
        var analysisOptions = options with
        {
            IncludeSections = sections,
        };
        using var service = subject.OpenSourceLink(
            ILOffsetQuery.RequiresAnalysis(analysisOptions),
            logger.Log);
        ILOffsetAnalysisPreparation analysis =
            ILOffsetQuery.PrepareAnalysis(
                service,
                analysisOptions,
                records
                    .OfType<
                        ILCoordinatePopulationRecord.Coordinate>()
                    .Select(record => record.MethodToken));
        foreach (ILCoordinatePopulationRecord record in records)
        {
            if (record is ILCoordinatePopulationRecord.Malformed malformed)
            {
                rows.Add(
                    new ILCoordinateBatchRow(
                        null,
                        malformed.Label,
                        null,
                        null,
                        "error",
                        malformed.Error));
                continue;
            }

            var coordinate =
                (ILCoordinatePopulationRecord.Coordinate)record;
            var resolved = await ResolveILCoordinateAsync(
                service,
                coordinate,
                sections,
                packageName,
                packageVersion,
                isPlatformAssembly,
                options,
                httpClient,
                logger,
                analysis: analysis);
            rows.Add(resolved.Result is { } result
                ? BuildILCoordinateBatchRow(coordinate, result)
                : new ILCoordinateBatchRow(
                    coordinate.Value,
                    coordinate.Label,
                    null,
                    null,
                    "error",
                    ILOffsetQuery.FormatFailure(resolved.Failure)));
        }

        var batchExitCode = rows.Any(row => row.Meaning == "error") ? 1 : 0;
        if (!CliSemanticRowSelection.TrySelectOrApplyLegacy(
                options.CoordinateRowSelection,
                options.Rows,
                rows,
                "IL coordinate",
                failure =>
                    $"IL coordinate row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} rows are available.",
                out IReadOnlyList<ILCoordinateBatchRow> visibleRows))
        {
            return 1;
        }

        // A coordinate that failed to resolve is still a reported row, so it counts; the
        // non-zero exit remains the signal that some coordinate did not resolve.
        if (LensProjection.TryProject(
                options,
                "library coordinate --file",
                visibleRows.Count,
                out var projectionExitCode,
                ["Coordinate", "Label", "Member", "IL Offset", "Meaning", "Evidence"]))
            return projectionExitCode != 0 ? projectionExitCode : batchExitCode;

        if (ProjectionAudit.RejectUnloweredJson(options, options.JsonOutput))
            return 1;

        WriteILCoordinateBatchRows(
            [.. visibleRows],
            options with { Rows = null });
        return batchExitCode;
    }

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

    private static Task<(
        int ExitCode,
        ILOffsetProjection? Result,
        ILOffsetProjectionFailure? Failure)> ResolveILCoordinateAsync(
        SourceLinkService service,
        ILCoordinatePopulationRecord.Coordinate coordinate,
        HashSet<string> sections,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger,
        bool allowNonBoundaryContextAbsence = false,
        ILOffsetAnalysisPreparation? analysis = null)
    {
        var queryOptions = options with
        {
            CoordinateRequest =
                new LibraryCoordinateRequest.IlPoint(
                    coordinate.Value,
                    coordinate.MethodToken,
                    coordinate.ILOffset),
            IncludeSections = sections,
            Select = [.. sections],
            Discover = null,
            Print = false,
            Count = false,
            Value = false,
            Urls = false,
            Paths = false
        };
        return allowNonBoundaryContextAbsence
            ? ILOffsetQuery.ResolveDiscoveryAsync(
                service,
                packageName,
                packageVersion,
                isPlatformAssembly,
                queryOptions,
                httpClient,
                logger,
                analysis)
            : ILOffsetQuery.ResolveBatchAsync(
                service,
                packageName,
                packageVersion,
                isPlatformAssembly,
                queryOptions,
                httpClient,
                logger,
                analysis);
    }

    private static ILCoordinateBatchRow BuildILCoordinateBatchRow(
        ILCoordinatePopulationRecord.Coordinate input,
        ILOffsetProjection result)
    {
        var (meaning, evidence) = ExplainILCoordinate(result);
        return new ILCoordinateBatchRow(
            input.Value,
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

    private static (LibraryOptions Options, string? Error) NormalizeILCoordinateSelection(
        LibraryOptions options)
    {
        var select = options.Select?.ToList() ?? [];
        bool hasILCoordinate =
            options.CoordinateRequest
                is LibraryCoordinateRequest.IlPoint;
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
            {
                return (
                    options,
                    "IL coordinate parameters belong in the coordinate argument, "
                    + $"not in -S. Use library coordinate 0x06000001+0x5 "
                    + $"--library <path> -S \"{SectionNames.ILOffset}\".");
            }
        }

        if (hasILCoordinate
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
            "IL coordinate sections require library coordinate "
            + "<token>+<offset>.";
        var heapCoordinateRequired =
            $"\"{MetadataSectionNames.Heap}\" requires library coordinate "
            + "\"<heap>:<address>\", for example library coordinate "
            + "\"#Strings:0x1a4\".";
        var bodyKindRequired =
            $"\"{sections.FirstOrDefault(section => BodyKindQueryOptions.Sections.Contains(
                section, StringComparer.OrdinalIgnoreCase)) ?? SectionNames.BodyShapes}\" "
            + "requires --where \"Kind=<C# Body Kinds ID>\".";
        var removedILCoordinateSections = false;
        var removedHeapSection = false;
        var removedBodyShapesSection = false;

        if (sections.Overlaps(ILCoordinateSections)
            && !HasILCoordinateRequest(options))
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
            && options.CoordinateRequest
                is not LibraryCoordinateRequest.HeapPoint)
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

        if (BodyKindQueryOptions.IsSelected(sections)
            && !options.BodyKindQuery.HasFilter)
        {
            if (!BodyKindQueryOptions.IsSelected(selectResult.ExactSections))
            {
                sections.ExceptWith(BodyKindQueryOptions.Sections);
                removedBodyShapesSection = true;
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
    /// do not expose image-relative row identities. Package Library aggregate output is a separate
    /// renderer that suffixes each metadata heading with the package-relative assembly path. The
    /// direct-library rejection and count allowance are gated by
    /// <c>MetadataLens_MultipleAssemblies_IsRejected</c> in DotnetInspect.Cli.Tests; all-libraries
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

    private static string? MetadataRootSelectionError(LibraryOptions options)
    {
        if (options.MetadataRoot == MetadataRootKind.Cli)
            return null;

        if (options.IncludeSections?.Any(
                MetadataSectionNames.IsMetadataSection) == true)
        {
            return null;
        }

        return "--metadata-root requires -S @Metadata or a Metadata: section.";
    }

    private static string? MetadataRootDiscoveryModeError(
        LibraryOptions options,
        bool hasInputSource)
    {
        if (options.MetadataRoot == MetadataRootKind.Cli
            || options.Discover is null)
        {
            return null;
        }

        if (!options.Effective)
        {
            return "--metadata-root requires --effective with -D because "
                + "structural discovery does not inspect an image.";
        }

        return hasInputSource
            ? null
            : "--metadata-root requires a library path, package, or --platform "
                + "with -D because effective discovery must inspect an image.";
    }

    private static LibraryOptions NormalizeReferenceProjection(LibraryOptions options)
    {
        if (options.Discover != null)
            return options;

        var select = options.Select?.ToList() ?? [];

        if (options.IncludeReferences
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
        };
    }

    private static bool TrySelectAssemblyReferences(
        LibraryInspection inspection,
        RowSelectionIntent<string>? intent)
    {
        if (intent is null
            || inspection.AssemblyReferenceInspection?.Value
                is not FindingInspection<AssemblyReference>.Complete complete)
        {
            return true;
        }

        AssemblyReference[] references =
        [
            .. complete.Findings
                .Select(static finding => finding.Payload)
                .OrderBy(
                    static reference => reference.Name,
                    StringComparer.Ordinal),
        ];
        if (!CliSemanticRowSelection.TrySelect(
                intent,
                references,
                "Library references",
                failure =>
                    $"Library reference row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} direct reference "
                    + $"{(failure.Failure.AvailableCount == 1 ? "row is" : "rows are")} available.",
                out IReadOnlyList<AssemblyReference> selected))
        {
            return false;
        }

        inspection.AssemblyReferenceDisplayOrder = selected;
        if (inspection.AssemblyInfo is not null)
            inspection.AssemblyInfo.References = [.. selected];
        return true;
    }

    private static bool TrySelectLibraryEcosystemDependencies(
        LibraryInspection inspection,
        RowSelectionIntent<string>? intent)
    {
        if (intent is null)
            return true;

        IReadOnlyList<EcosystemDependencyRecognitionEntry> rows =
            inspection.EcosystemDependencyRecognitionInspection?.Content
                switch
                {
                    EcosystemDependencyRecognitionOutcome.Complete complete =>
                        complete.Document.Classification.Recognized,
                    EcosystemDependencyRecognitionOutcome.Incomplete incomplete =>
                        incomplete.Document.Classification.Recognized,
                    _ => [],
                };
        if (!CliSemanticRowSelection.TrySelect(
                intent,
                rows,
                "Library ecosystem dependencies",
                failure =>
                    $"Library ecosystem dependency row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} "
                    + $"{(failure.Failure.AvailableCount == 1 ? "row is" : "rows are")} available.",
                out IReadOnlyList<
                    EcosystemDependencyRecognitionEntry> selected))
        {
            return false;
        }

        inspection.EcosystemDependencyRows = selected;
        return true;
    }

    private static void ApplyLibraryEcosystemDependencies(
        LibraryInspection inspection,
        LibraryInspectionSubject subject,
        bool wantsEcosystemDependencies,
        bool discloseEmptyDetailDiagnostics,
        VerboseLogger logger)
    {
        if (!wantsEcosystemDependencies)
            return;

        if (inspection.AssemblyReferencesQueryResult is not { } references)
        {
            throw new InvalidOperationException(
                "Library ecosystem recognition requires the assembly-reference query result.");
        }
        if (subject.AssemblyReference is not { } assembly)
        {
            CommandError.WriteWarning(
                "Library ecosystem recognition is unavailable because the "
                + "selected input is not an assembly.");
            return;
        }
        if (!TryCreateExactLibrarySourceCoordinate(
                assembly,
                out ExactLibrarySourceCoordinate? source))
        {
            CommandError.WriteWarning(
                "Library ecosystem recognition is unavailable because the "
                + "selected source does not have an exact Library coordinate.");
            return;
        }

        InspectionEnvelope<EcosystemDependencyRecognitionOutcome> result =
            LibraryEcosystemDependencyRecognitionInspection.Execute(
                source,
                references);
        inspection.EcosystemDependencyRecognitionInspection = result;
        bool discloseDiagnostics =
            discloseEmptyDetailDiagnostics
            && result.Content switch
            {
                EcosystemDependencyRecognitionOutcome.Incomplete incomplete =>
                    incomplete.Document.Classification.Recognized.IsEmpty,
                EcosystemDependencyRecognitionOutcome.Unavailable => true,
                _ => false,
            };
        foreach (InspectionDiagnostic diagnostic in result.Diagnostics)
        {
            string message = $"{diagnostic.Code}: {diagnostic.Summary}";
            if (discloseDiagnostics)
                CommandError.WriteWarning(message);
            else
                logger.Log(message);
        }
    }

    private static bool TryCreateExactLibrarySourceCoordinate(
        ResolvedAssemblyReference assembly,
        [NotNullWhen(true)]
        out ExactLibrarySourceCoordinate? source)
    {
        var identity = new ManagedMetadataIdentity.Assembly(
            assembly.Identity);
        try
        {
            source = assembly.Provenance switch
            {
                AssemblyResolutionProvenance.PackageAsset package =>
                    new ExactLibrarySourceCoordinate.Package(
                        PackageSourceCoordinate.Create(
                            package.PackageId,
                            package.PackageVersion),
                        identity),
                AssemblyResolutionProvenance.PlatformAsset platform
                    when TryGetPlatformFamily(
                        platform.Framework,
                        out PlatformFamily family) =>
                    new ExactLibrarySourceCoordinate.Platform(
                        new PlatformLibraryPopulationDeclaration(family),
                        identity),
                AssemblyResolutionProvenance.ProjectAsset =>
                    new ExactLibrarySourceCoordinate.Project(identity),
                AssemblyResolutionProvenance.LocalAsset
                    or AssemblyResolutionProvenance.DesignatedAsset =>
                    new ExactLibrarySourceCoordinate.Local(identity),
                _ => null,
            };
            return source is not null;
        }
        catch (ArgumentException)
        {
            source = null;
            return false;
        }
    }

    private static bool TryGetPlatformFamily(
        string value,
        out PlatformFamily family)
    {
        if (value.Equals(
                "runtime",
                StringComparison.OrdinalIgnoreCase))
        {
            family = PlatformFamily.DotNetRuntime;
            return true;
        }
        if (value.Equals(
                "aspnetcore",
                StringComparison.OrdinalIgnoreCase))
        {
            family = PlatformFamily.AspNetCore;
            return true;
        }

        family = default;
        return false;
    }

    private static bool RequiresLibraryEcosystemDiagnosticDisclosure(
        LibraryOptions options) =>
        options.IncludeSections is { } sections
        && sections.Contains(SectionNames.EcosystemDependencies)
        && !sections.Contains(SectionNames.LibraryInfo);

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
    /// Selects the heap-coordinate section when the child admitted a heap point
    /// and no explicit section was given.
    /// </summary>
    private static (LibraryOptions Options, string? Error)
        NormalizeHeapCoordinateSelection(
            LibraryOptions options)
    {
        if (options.CoordinateRequest
            is not LibraryCoordinateRequest.HeapPoint)
            return (options, null);

        if (options.Discover != null || options.Select is { Length: > 0 })
            return (options, null);

        return (options with { Select = [MetadataSectionNames.Heap] }, null);
    }

    /// <summary>
    /// Reads the heap value named by the Coordinate child onto the model, which makes the
    /// coordinate-scoped section applicable. Returns a process exit code, having written its own
    /// diagnostic, exactly as the IL-coordinate resolution above it does.
    ///
    /// A coordinate that does not resolve is an <em>error</em>, not a malformed cell in an
    /// otherwise successful render. The two cases look alike but are not: a bad heap reference
    /// found inside a projected table row is a fact about the image, so it renders as
    /// <c>!malformed</c> and the command succeeds; a coordinate is the caller's own input, and the
    /// caller asked for exactly one thing that does not exist. Rendering that as a successful row
    /// would exit 0 while answering nothing, and — worse — <c>-D</c> would go on advertising
    /// <c>Metadata: Heap</c> as an available section.
    /// </summary>
    private static int PopulateMetadataHeapIfRequested(
        LibraryInspection inspection, LibraryOptions options, VerboseLogger logger)
    {
        if (options.CoordinateRequest
                is not LibraryCoordinateRequest.HeapPoint coordinate
            || (options.Discover == null && options.IncludeSections?.Contains(MetadataSectionNames.Heap) != true))
            return 0;

        if (inspection.MetadataAssemblyPath is not { } path)
            return 0;

        string name =
            MetadataHeapCoordinate.StreamName(coordinate.Heap);
        if (inspection.MetadataImageResult
            is not MetadataImageResult.Available available)
        {
            return 0;
        }

        MetadataValue? value;
        try
        {
            if (available.Root is { } root)
            {
                value = root.HeapValue(
                    coordinate.Heap,
                    coordinate.Address);
            }
            else
            {
                using var session = AssemblyInspectionSession.Open(path);
                value = session.MetadataHeapValue(
                    coordinate.Heap,
                    coordinate.Address);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                $"Error reading {name} heap at {coordinate.Address} "
                + $"in {path}: {ex.Message}");
            CommandError.Write(
                $"could not read {name} heap at {coordinate.Address}: "
                + ex.Message);
            return 1;
        }

        switch (value)
        {
            case null:
                CommandError.Write(
                    $"could not read {name} heap at {coordinate.Address}: "
                    + $"{path} carries no metadata.");
                return 1;

            case MetadataValue.Malformed malformed:
                CommandError.Write(
                    $"could not read {name} heap at {coordinate.Address}: "
                    + malformed.Detail);
                return 1;

            default:
                inspection.MetadataHeap =
                    new MetadataHeapLookup(
                        coordinate.Heap,
                        coordinate.Address,
                        value);
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
        if (options.CoordinateRequest
                is LibraryCoordinateRequest.FilePopulation
            && options.Discover is not null)
        {
            return await PopulateILCoordinatePopulationForDiscoveryAsync(
                inspection,
                subject,
                packageName,
                packageVersion,
                isPlatformAssembly,
                options,
                httpClient,
                logger);
        }

        if (options.CoordinateRequest
                is not LibraryCoordinateRequest.IlPoint
            || (options.Discover == null && options.IncludeSections?.Overlaps(ILCoordinateSections) != true))
            return 0;

        using var service = subject.OpenSourceLink(
            ILOffsetQuery.RequiresAnalysis(options),
            logger.Log);
        var resolved = await ILOffsetQuery.ResolveAsync(
            service, packageName, packageVersion, isPlatformAssembly, options,
            httpClient, logger);
        if (resolved.ExitCode != 0)
            return resolved.ExitCode;

        inspection.ILOffset = resolved.Result;
        return 0;
    }

    private static async Task<int> PopulateILCoordinatePopulationForDiscoveryAsync(
        LibraryInspection inspection,
        LibraryInspectionSubject subject,
        string? packageName,
        string? packageVersion,
        bool isPlatformAssembly,
        LibraryOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (options.CoordinateRequest
            is not LibraryCoordinateRequest.FilePopulation
            {
                Population: { } population,
            })
        {
            throw new UnreachableException(
                "Coordinate discovery requires an admitted population.");
        }

        HashSet<string> sections = options.IncludeSections is { Count: > 0 }
            ? [.. options.IncludeSections]
            : [];
        var projections = new List<ILOffsetProjection>();
        var failed = false;
        var analysisOptions = options with
        {
            IncludeSections = sections,
        };
        using var service = subject.OpenSourceLink(
            ILOffsetQuery.RequiresAnalysis(analysisOptions),
            logger.Log);
        ILOffsetAnalysisPreparation analysis =
            ILOffsetQuery.PrepareAnalysis(
                service,
                analysisOptions,
                population.Records
                    .OfType<
                        ILCoordinatePopulationRecord.Coordinate>()
                    .Select(record => record.MethodToken));
        foreach (ILCoordinatePopulationRecord record in population.Records)
        {
            if (record is ILCoordinatePopulationRecord.Malformed malformed)
            {
                CommandError.Write(malformed.Error);
                failed = true;
                continue;
            }

            var coordinate =
                (ILCoordinatePopulationRecord.Coordinate)record;
            var resolved = await ResolveILCoordinateAsync(
                service,
                coordinate,
                sections,
                packageName,
                packageVersion,
                isPlatformAssembly,
                options,
                httpClient,
                logger,
                allowNonBoundaryContextAbsence: true,
                analysis);
            if (resolved.Result is { } result)
            {
                projections.Add(result);
            }
            else
            {
                CommandError.Write(
                    $"Could not resolve coordinate '{coordinate.Value}': "
                    + ILOffsetQuery.FormatFailure(resolved.Failure));
                failed = true;
            }
        }

        if (failed)
            return 1;
        if (projections.Count == 0)
        {
            CommandError.Write(
                "Coordinate file contains no resolvable records for effective discovery.");
            return 1;
        }

        inspection.ILOffset =
            MergeILCoordinateProjectionsForDiscovery(projections);
        return 0;
    }

    private static ILOffsetProjection MergeILCoordinateProjectionsForDiscovery(
        List<ILOffsetProjection> projections)
    {
        ILOffsetProjection first = projections[0];
        ILOffsetMemberContext[] members =
            [.. projections
                .Select(projection => projection.MemberContext)
                .OfType<ILOffsetMemberContext>()];
        ILOffsetInstructionContext[] instructions =
            [.. projections
                .Select(projection => projection.InstructionContext)
                .OfType<ILOffsetInstructionContext>()];
        ILOffsetCallsiteContext[] callsites =
            [.. projections
                .Select(projection => projection.CallsiteContext)
                .OfType<ILOffsetCallsiteContext>()];
        ILOffsetReturnAddressContext[] returnAddresses =
            [.. projections
                .Select(projection => projection.ReturnAddressContext)
                .OfType<ILOffsetReturnAddressContext>()];
        List<ILOffsetExceptionContext> exceptions =
            projections
                .SelectMany(projection =>
                    projection.ExceptionContext ?? [])
                .ToList();
        List<ILOffsetAllocationContext> allocations =
            projections
                .SelectMany(projection =>
                    projection.AllocationContext ?? [])
                .ToList();
        List<ILOffsetSafetyContext> safety =
            projections
                .SelectMany(projection =>
                    projection.SafetyContext ?? [])
                .ToList();
        List<ILOffsetCostContext> costs =
            projections
                .SelectMany(projection =>
                    projection.CostContext ?? [])
                .ToList();

        // Effective discovery asks which evidence exists anywhere in the admitted population.
        // The aggregate is never rendered as one coordinate; it only drives section applicability
        // and field filtering before DiscoverOutput lowers the discovery document.
        return new ILOffsetProjection
        {
            Method = first.Method,
            Token = first.Token,
            ILOffset = first.ILOffset,
            MatchedOffset = projections
                .Select(projection => projection.MatchedOffset)
                .FirstOrDefault(value => value is not null),
            File = projections
                .Select(projection => projection.File)
                .FirstOrDefault(value => value is not null),
            Line = projections
                .Select(projection => projection.Line)
                .FirstOrDefault(value => value is not null),
            Url = projections
                .Select(projection => projection.Url)
                .FirstOrDefault(value => value is not null),
            SourceChecksum = projections
                .Select(projection => projection.SourceChecksum)
                .FirstOrDefault(value => value is not null),
            SourceChecksumAlgorithm = projections
                .Select(projection => projection.SourceChecksumAlgorithm)
                .FirstOrDefault(value => value is not null),
            MemberContext = MergeMemberContextsForDiscovery(members),
            InstructionContext = MergeInstructionContextsForDiscovery(instructions),
            ExceptionContext = exceptions.Count == 0 ? null : exceptions,
            CallsiteContext = MergeCallsiteContextsForDiscovery(callsites),
            ReturnAddressContext =
                MergeReturnAddressContextsForDiscovery(returnAddresses),
            AllocationContext = allocations.Count == 0 ? null : allocations,
            SafetyContext = safety.Count == 0 ? null : safety,
            CostContext = costs.Count == 0 ? null : costs,
        };
    }

    private static ILOffsetMemberContext? MergeMemberContextsForDiscovery(
        ILOffsetMemberContext[] contexts)
        => contexts.Length == 0
            ? null
            : new ILOffsetMemberContext
            {
                Assembly = FirstPresentReference(contexts, context => context.Assembly),
                Type = FirstPresentReference(contexts, context => context.Type),
                TypeKind = FirstPresentReference(contexts, context => context.TypeKind),
                Member = FirstPresentReference(contexts, context => context.Member),
                Signature = FirstPresentReference(contexts, context => context.Signature),
                MemberKind = FirstPresentReference(contexts, context => context.MemberKind),
                Visibility = FirstPresentReference(contexts, context => context.Visibility),
                Static = FirstPresentReference(contexts, context => context.Static),
                Async = FirstPresentReference(contexts, context => context.Async),
                MetadataToken = FirstPresentReference(
                    contexts,
                    context => context.MetadataToken),
                ILOffset = FirstPresentReference(contexts, context => context.ILOffset)
            };

    private static ILOffsetInstructionContext? MergeInstructionContextsForDiscovery(
        ILOffsetInstructionContext[] contexts)
        => contexts.Length == 0
            ? null
            : new ILOffsetInstructionContext
            {
                ILOffset = FirstPresentReference(contexts, context => context.ILOffset),
                Boundary = FirstPresentReference(contexts, context => context.Boundary),
                Opcode = FirstPresentReference(contexts, context => context.Opcode),
                OperandKind = FirstPresentReference(
                    contexts,
                    context => context.OperandKind),
                Operand = FirstPresentReference(contexts, context => context.Operand),
                OperandToken = FirstPresentReference(
                    contexts,
                    context => context.OperandToken),
                BranchTargets = FirstPresentReference(
                    contexts,
                    context => context.BranchTargets),
                NextOffset = FirstPresentReference(contexts, context => context.NextOffset),
                Length = FirstPresentValue(contexts, context => context.Length),
                Block = FirstPresentValue(contexts, context => context.Block),
                TerminatesBlock =
                    FirstPresentReference(contexts, context => context.TerminatesBlock),
                FallsThrough =
                    FirstPresentReference(contexts, context => context.FallsThrough)
            };

    private static ILOffsetCallsiteContext? MergeCallsiteContextsForDiscovery(
        ILOffsetCallsiteContext[] contexts)
        => contexts.Length == 0
            ? null
            : new ILOffsetCallsiteContext
            {
                CallOffset = FirstPresentReference(contexts, context => context.CallOffset),
                Opcode = FirstPresentReference(contexts, context => context.Opcode),
                CallKind = FirstPresentReference(contexts, context => context.CallKind),
                Callee = FirstPresentReference(contexts, context => context.Callee),
                OperandToken = FirstPresentReference(
                    contexts,
                    context => context.OperandToken),
                ReturnAddress = FirstPresentReference(
                    contexts,
                    context => context.ReturnAddress)
            };

    private static ILOffsetReturnAddressContext? MergeReturnAddressContextsForDiscovery(
        ILOffsetReturnAddressContext[] contexts)
        => contexts.Length == 0
            ? null
            : new ILOffsetReturnAddressContext
            {
                ILOffset = FirstPresentReference(contexts, context => context.ILOffset),
                CallOffset = FirstPresentReference(contexts, context => context.CallOffset),
                Opcode = FirstPresentReference(contexts, context => context.Opcode),
                CallKind = FirstPresentReference(contexts, context => context.CallKind),
                Callee = FirstPresentReference(contexts, context => context.Callee),
                OperandToken = FirstPresentReference(
                    contexts,
                    context => context.OperandToken)
            };

    private static T? FirstPresentReference<TContext, T>(
        IEnumerable<TContext> contexts,
        Func<TContext, T?> selector)
        where T : class
        => contexts
            .Select(selector)
            .FirstOrDefault(value => value is not null);

    private static T? FirstPresentValue<TContext, T>(
        IEnumerable<TContext> contexts,
        Func<TContext, T?> selector)
        where T : struct
        => contexts
            .Select(selector)
            .FirstOrDefault(value => value is not null);

    private static bool ValidateLibraryPrintSelection(HashSet<string>? sections)
    {
        if (sections is { Count: 1 } && sections.Contains(SectionNames.ILOffset))
            return true;

        CommandError.Write("--print requires -S/--select to match exactly one printable section.");
        return false;
    }

    private static bool ValidateMultiTfmOutput(LibraryOptions options)
    {
        if (!IsAllTfmPackageSelection(options)
            || options.Discover != null)
        {
            return true;
        }

        string? incompatibleShape = options.Tree && !options.Count ? "--tree"
            : options.Print ? "--print"
            : options.Value ? "--value"
            : options.Urls ? "--urls"
            : options.Paths ? "--paths"
            : options.ExtractResources != null ? "--extract-resources"
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
        var fetcher = new SourceFetch(DotnetInspector.Networking.HttpClientFactory.SharedUntrustedFetch);
        var fetch = await PdbSourceHouse.FetchVerifiedSourceTextAsync(
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
            if (effectivenessScope?.Contains(
                    SectionNames.ReferenceHierarchy) == true)
            {
                selected.Add(SectionNames.ReferenceHierarchy);
            }

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

        var schemaMap = CreateStructuralSchema();

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
            DiscoveryOutputRequest.Create(
                OutputFormatResolver.ResolveStored(
                    options.Format,
                    options.JsonOutput,
                    options.PlainText,
                    options.Tabular,
                    options.Tsv,
                    options.Jsonl),
                options.Tree,
                options.TabularExplicitlySet,
                options.NoHeader,
                (int)userVerbosity,
                options),
            rootLabel: rootLabel, fullSchema: schemaMap,
            sectionCostAnnotations: pipeline.GetCostAnnotations(),
            sectionCategories: pipeline.GetCategoryMap(),
            catalogHiddenSections: EffectiveCatalogHidden(pipeline, effective),
            listedCategoryDoors: pipeline.GetListedCategoryDoors());
        return Math.Max(
            Math.Max(discoveryExitCode, inspectionFailureExitCode),
            IntegrityExitCode(
                0,
                reportIdentifierFailures,
                inspection));
    }

    // ── Effective sections cache ──

    // Bumped to v29: ReadyToRun applicability adds sections and a category door.
    private const string EffectiveCategory = "effective-v29";

    static LibraryCommand()
    {
        PersistentCache.RegisterVersionedCategory("effective-v", EffectiveCategory);
    }

    private static (List<string> Sections, DocumentSchema Schema)? TryGetCachedEffective(string assemblyPath, string contentHash, bool hasSourceLink)
    {
        string key = BuildEffectiveCacheKey(assemblyPath, contentHash, hasSourceLink);
        var cached = PersistentCache.TryGet(EffectiveCategory, key, extension: "tsv");
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
        PersistentCache.Set(EffectiveCategory, key, sb.ToString(), extension: "tsv");
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
        if (!HasILCoordinateRequest(options))
        {
            sections = sections
                .Where(section => !ILCoordinateSections.Contains(
                    section,
                    StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        if (options.CoordinateRequest
            is not LibraryCoordinateRequest.HeapPoint)
        {
            sections = sections
                .Where(section => !section.Equals(
                    MetadataSectionNames.Heap,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return sections;
    }

    private static bool HasILCoordinateRequest(LibraryOptions options) =>
        options.CoordinateRequest
            is LibraryCoordinateRequest.IlPoint
                or LibraryCoordinateRequest.FilePopulation;

    private static int RenderEffective(List<string> effective, DocumentSchema schema, LibraryOptions options,
        SectionPipeline<LibraryInspection> pipeline, Verbosity userVerbosity = Verbosity.Minimal,
        string? rootLabel = null)
    {
        return DiscoverOutput.ExecuteEffective(options.Discover, effective, schema,
            DiscoveryOutputRequest.Create(
                OutputFormatResolver.ResolveStored(
                    options.Format,
                    options.JsonOutput,
                    options.PlainText,
                    options.Tabular,
                    options.Tsv,
                    options.Jsonl),
                options.Tree,
                options.TabularExplicitlySet,
                options.NoHeader,
                (int)userVerbosity,
                options),
            rootLabel: rootLabel,
            sectionCostAnnotations: pipeline.GetCostAnnotations(),
            sectionCategories: pipeline.GetCategoryMap(),
            catalogHiddenSections: EffectiveCatalogHidden(pipeline, effective),
            listedCategoryDoors: pipeline.GetListedCategoryDoors());
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
                || ILCoordinateSections.Contains(name, StringComparer.OrdinalIgnoreCase)),
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

        return DiscoverOutput.FilterSchemaToRenderedItems(
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

        // A requested alternate root that is not present makes the metadata lens
        // inapplicable. Preserve the ordinary no-data outcome even for an exact
        // metadata section instead of treating absence as an empty producer failure.
        if (MetadataSectionNames.IsMetadataSection(section)
            && inspections.All(static inspection =>
                inspection.MetadataImageResult is MetadataImageResult.MissingRoot))
        {
            return false;
        }

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
            && (section.Equals(
                    IntegrationSectionNames.Integrations,
                    StringComparison.OrdinalIgnoreCase)
                || section.Equals(
                    IntegrationSectionNames.Opportunities,
                    StringComparison.OrdinalIgnoreCase)))
            return false;

        CommandError.WriteLine($"This section ({emptySection}) produced no output.");
        return true;
    }

    internal static bool FailureAffectsSection(string failureSection, string section)
    {
        if (failureSection.Equals(section, StringComparison.OrdinalIgnoreCase))
            return true;

        if (failureSection.Equals(SectionNames.BodyShapes, StringComparison.OrdinalIgnoreCase)
            && section.Equals(SectionNames.BodyShapeSummary, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (failureSection.Equals(MetadataSectionNames.Image, StringComparison.Ordinal)
            && MetadataSectionNames.IsMetadataSection(section))
        {
            return true;
        }

        if (failureSection.Equals(ReadyToRunSectionNames.Image, StringComparison.Ordinal)
            && ReadyToRunSectionNames.All.Contains(
                section,
                StringComparer.OrdinalIgnoreCase))
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
            return section.Equals(
                IntegrationSectionNames.Integrations,
                StringComparison.OrdinalIgnoreCase);
        }

        return failureSection.Equals(LibraryIntegrationCatalog.RollupName, StringComparison.Ordinal)
               && section.Equals(
                   IntegrationSectionNames.Integrations,
                   StringComparison.OrdinalIgnoreCase);
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
        PackageReferenceTarget packageTarget,
        string? tfm,
        NuGetSourceOptions? sourceOptions,
        bool includePrerelease,
        VerboseLogger logger,
        HttpClient httpClient,
        PackageExtractionResult? preResolvedPackage)
    {
        PackageExtractionResult resolution;
        if (preResolvedPackage is null)
        {
            var outcome = await PackageExtractor.ExtractPackageAsync(
                httpClient,
                packageTarget,
                logger.Log,
                sourceOptions: sourceOptions,
                includePrerelease: includePrerelease);
            if (!outcome.IsSuccess)
            {
                CommandError.Write($"{outcome.ErrorMessage}");
                return null;
            }
            resolution = outcome.Result!;
        }
        else
        {
            resolution = preResolvedPackage;
        }

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
                resolution,
                packageTarget,
                sourceOptions,
                logger,
                httpClient).ConfigureAwait(false);

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

        if (!string.IsNullOrEmpty(assemblyName))
        {
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

        // --tfm <specific>: find the package-primary assembly by TFM
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
        var defaultCandidates = TfmSelector.GetPackageAssemblies(extractPath);
        if (defaultCandidates.Count == 0)
        {
            CommandError.Write("No DLLs found in package.");
            DeleteTempDir(tempDir);
            return null;
        }

        var (selectedPath, selectedTfm) = TfmSelector.SelectHighestTfmAssembly(defaultCandidates, extractPath, resolution.PackageName);
        if (selectedPath == null)
        {
            // No TFM structure found, fall back to first DLL
            return ([defaultCandidates[0]], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
        }

        logger.Log($"Using TFM: {selectedTfm}");
        return ([selectedPath], extractPath, tempDir, nupkgPath, resolvedPackageName, resolvedPackageVersion);
    }

    private sealed record ToolPayloadResolution(PackageExtractionResult? Result, string? Error);

    private static async Task<ToolPayloadResolution> TryResolveToolPayloadPackageAsync(
        PackageExtractionResult package,
        PackageReferenceTarget originalPackageTarget,
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

        var localPayload = originalPackageTarget.IsLocalFile
            ? TryFindLocalSiblingPackage(
                originalPackageTarget.OriginalArgument,
                payloadId,
                version)
            : null;
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

    internal SourceLinkService OpenSourceLink(
        bool prefetch,
        Action<string>? log = null) =>
        prefetch
            ? AssemblyReference is null
                ? SourceLinkService.OpenPrefetched(Path, log)
                : SourceLinkService.OpenPrefetched(
                    AssemblyReference,
                    log)
            : OpenSourceLink(log);
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
