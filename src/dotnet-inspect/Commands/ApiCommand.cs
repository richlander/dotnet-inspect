using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Net;
using DotnetInspector.CSharpBodySlicer;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using Markout;
using Markout.Formatting;
using DotnetInspector.Services;
using DotnetInspector.Views;

using Decompiler = ILInspector.Decompiler;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Commands;

/// <summary>
/// Shared helpers for type and member commands.
/// Also provides a compatibility shim for callers that use ApiCommand.ExecuteAsync directly.
/// </summary>
public class ApiCommand
{
    public const string Name = "api";

    // ===== Compatibility Shim =====

    public static Task<int> ExecuteAsync(ApiOptions options) => options switch
    {
        MemberOptions mo => MemberCommand.ExecuteAsync(mo),
        TypeOptions to => TypeCommand.ExecuteAsync(to),
        _ => TypeCommand.ExecuteAsync(new TypeOptions
        {
            TypeName = options.TypeName, PackagePath = options.PackagePath, AssemblyPath = options.AssemblyPath,
            PlatformAssembly = options.PlatformAssembly, PlatformFramework = options.PlatformFramework,
            Tfm = options.Tfm, IncludeAll = options.IncludeAll, Verbose = options.Verbose,
            ShowDocs = options.ShowDocs, DocsExplicitlySet = options.DocsExplicitlySet,
            UseLocalDocs = options.UseLocalDocs, ShowSamples = options.ShowSamples,
            BrowsableUrls = options.BrowsableUrls, Verbosity = options.Verbosity,
            JsonOutput = options.JsonOutput, CompactJson = options.CompactJson,
            Tabular = options.Tabular, Tsv = options.Tsv, Jsonl = options.Jsonl,
            TabularExplicitlySet = options.TabularExplicitlySet,
            FormatExplicitlySet = options.FormatExplicitlySet,
            NoHeader = options.NoHeader, Limit = options.Limit, MemberLimit = options.Limit,
            MemberFilter = options.MemberFilter,
            KindFilter = options.KindFilter, UnsafeOnly = options.UnsafeOnly,
            IncludeSections = options.IncludeSections,
            Print = options.Print, PrintRow = options.PrintRow,
            Value = options.Value, Urls = options.Urls, Paths = options.Paths,
            Select = options.Select, SelectDefault = options.SelectDefault,
            Columns = options.Columns, Fields = options.Fields,
            Schema = options.Schema, Count = options.Count, SourceOptions = options.SourceOptions,
            TipLevel = options.TipLevel, RenderOptions = options.RenderOptions,
            RequestAllTaste = options.RequestAllTaste,
            RequestReadableLocalNames = options.RequestReadableLocalNames
        })
    };

    /// <summary>
    /// True when bare <c>-S</c> was requested, carries no explicit section values to fall back on,
    /// and the pipeline publishes no overview sections -- the state that would otherwise render the
    /// full default view instead of a bounded one. Extracted so the decision is directly testable.
    /// </summary>
    internal static bool HasNoBareSelectOverview(ApiOptions options, string[] bareSelectSections)
        => options.SelectDefault
            && options.Select is not { Length: > 0 }
            && bareSelectSections.Length == 0;

    /// <summary>
    /// Re-resolves <c>-S</c> against the type-listing pipeline for a query that entered the
    /// preamble as a single-type request but renders a listing.
    /// </summary>
    /// <remarks>
    /// <see cref="RunPreamble"/> picks its pipeline from the argument shape, so a dotted prefix
    /// that fails to resolve to a type validated its sections against the single-type pipeline
    /// while <see cref="TypeCommand"/> goes on to render a listing. The two disagree about every
    /// name: <c>-D</c> advertises <c>Classes</c> and <c>Structs</c>, and <c>-S Classes</c> is
    /// rejected. Returns <c>null</c> when resolution failed and the caller should stop with an
    /// error.
    /// </remarks>
    internal static TypeOptions? ReresolveSectionsForListing(TypeOptions options)
    {
        var typePipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var bareSelectSections = typePipeline.InfoSectionNames;

        if (HasNoBareSelectOverview(options, bareSelectSections))
        {
            CommandError.Write(
                "this view publishes no bare -S overview sections.",
                "Use -S <Section> to select one, -D to discover what is available, or -S @All for everything.");
            return null;
        }

        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select,
            typePipeline.SelectableSectionNames,
            bareSelectSections,
            typePipeline.GetCategoryMap(),
            selectDefault: options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selectResult))
            return null;

        var listingOptions = selectResult.Sections != null
            ? options with { IncludeSections = selectResult.Sections, SelectDeferredToListing = false }
            : options with { SelectDeferredToListing = false };
        listingOptions = ApplyImplicitTypeListingColumnScope(listingOptions);

        // The listing can resolve a selector to a different number of sections than the exact-type
        // pipeline did. Validate the resolved listing set even when the original selector was not
        // deferred, so count and tabular output cannot silently consume several sections.
        if (listingOptions.Discover == null && listingOptions.Count
            && !CountOutput.ValidateSingleSection(listingOptions.IncludeSections))
            return null;

        if (listingOptions.Discover == null
            && !OutputFormatResolver.ValidateSingleSectionForTabular(
                listingOptions.TabularExplicitlySet, listingOptions.IncludeSections))
            return null;

        return listingOptions;
    }

    private static TypeOptions ApplyImplicitTypeListingColumnScope(TypeOptions options)
    {
        if (options.IncludeSections is not null
            || options.Columns is not { Length: > 0 }
            || options.Tabular
            || options.JsonOutput)
        {
            return options;
        }

        return options with
        {
            IncludeSections =
            [
                SectionNames.Classes,
                SectionNames.Structs,
                SectionNames.Interfaces,
                SectionNames.Enums,
                SectionNames.Delegates,
            ]
        };
    }

    // ===== Shared Preamble =====

    /// <summary>
    /// True when <c>-S</c> failed against the single-type pipeline but resolves against the type
    /// listing, so the preamble must carry the decision forward instead of rejecting it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This deliberately only intercepts the <em>total</em> failure that would otherwise return 1,
    /// mirroring <see cref="SelectOutput.WriteUnresolved"/>'s own definition of that state. Nothing
    /// that succeeds today can reach the deferral, which bounds the change to invocations that
    /// already exit 1: a partial match still warns and proceeds exactly as before.
    /// </para>
    /// <para>
    /// A name valid for neither pipeline is a plain typo and still fails here, keeping the fast
    /// rejection -- and the single-type suggestions -- for the case that cannot be a listing.
    /// </para>
    /// </remarks>
    internal static bool ShouldDeferSelectToListing(
        ApiOptions options,
        bool singleTypeMode,
        SelectResult singleTypeResult,
        SectionPipeline<ApiSurface> typePipeline)
    {
        // Only `type` falls back from a single-type request to a listing. `member` renders a member
        // view or nothing, and the `api` shim renders nothing at all.
        if (options is not TypeOptions || !singleTypeMode || options.Select is not { Length: > 0 })
            return false;

        bool totalFailure = singleTypeResult.Unresolved.Count > 0
            && singleTypeResult.Sections is null or { Count: 0 };
        if (!totalFailure)
            return false;

        return ResolveSelectForListing(options, typePipeline).Sections is { Count: > 0 };
    }

    private static SelectResult ResolveSelectForListing(ApiOptions options, SectionPipeline<ApiSurface> typePipeline)
        => SelectResolver.ResolveSelectAsSections(
            options.Select,
            typePipeline.SelectableSectionNames,
            typePipeline.InfoSectionNames,
            typePipeline.GetCategoryMap(),
            selectDefault: options.SelectDefault);

    /// <summary>
    /// Reports a deferred <c>-S</c> against the single-type pipeline for a query that turned out to
    /// render a single type after all, restoring the rejection the preamble held back. Returns true
    /// when the caller should stop.
    /// </summary>
    /// <remarks>
    /// Resolution is repeated against the same three inputs the preamble used, so it reproduces the
    /// same total failure and the same message. It reports unconditionally rather than forwarding
    /// <see cref="SelectOutput.WriteUnresolved"/>'s partial-match result: a deferral is only ever
    /// created from a total failure, and treating a hypothetical partial as "carry on" would render
    /// the single-type view with the selector silently dropped.
    /// </remarks>
    internal static bool RejectDeferredSelectForSingleType(ApiOptions options, SectionPipeline<ApiType> memberPipeline)
    {
        if (!options.SelectDeferredToListing)
            return false;

        var result = SelectResolver.ResolveSelectAsSections(
            options.Select,
            memberPipeline.SelectableSectionNames,
            memberPipeline.InfoSectionNames,
            memberPipeline.GetCategoryMap(),
            selectDefault: options.SelectDefault);
        SelectOutput.WriteUnresolved(result);
        return true;
    }

    /// <summary>
    /// Validates an explicit type selector after lookup has established that the exact-type view,
    /// rather than a fallback listing, owns it.
    /// </summary>
    internal static bool ValidateResolvedSingleTypeSelection(TypeOptions options)
    {
        if (options.Discover != null)
            return true;

        var sections = options.IncludeSections;
        if (options is { JsonOutput: true, Count: false }
            && !IsProjectionRequested(options)
            && sections is { Count: > 0 }
            && !ValidateTypeJsonSections(sections))
        {
            return false;
        }

        if (options.Count && !CountOutput.ValidateSingleSection(sections))
            return false;

        var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
        if (shapeCount == 1)
        {
            var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
            if (!ShapeProjectionOutput.ValidateSingleSection(sections, optionName))
                return false;
        }

        if (options.Print && !ValidateApiPrintSelection(sections))
            return false;

        return OutputFormatResolver.ValidateSingleSectionForTabular(
            options.TabularExplicitlySet, sections);
    }

    internal record PreambleResult(
        ApiOptions Options,
        SectionPipeline<ApiSurface> TypePipeline,
        SectionPipeline<ApiType> MemberPipeline,
        InspectionQueryRegistry<ApiSurfaceQueryContext> TypeQueryRegistry);

    internal static (PreambleResult Result, int? Error) RunPreamble(ApiOptions options)
    {
        var typeCatalog = ApiTypeSectionDescriptors.CreateCatalog();
        var typePipeline = typeCatalog.Pipeline;
        var memberPipeline = ApiMemberSectionPipelines.Create(
            options,
            typeCatalog.QueryRegistry.CostOf);
        bool hasTypeName = !string.IsNullOrWhiteSpace(options.TypeName);
        bool typeNameIsGlob = hasTypeName && (options.TypeName!.Contains('*') || options.TypeName!.Contains('?'));
        bool singleTypeMode = options is MemberOptions || (hasTypeName && !typeNameIsGlob);
        var knownSections = singleTypeMode ? memberPipeline.SelectableSectionNames : typePipeline.SelectableSectionNames;
        if (options is MemberOptions memberOptions
            && memberOptions.MemberFilter.Count == 0
            && MightPeelDottedGenericMemberSelector(memberOptions.TypeName))
        {
            knownSections = knownSections
                .Concat(ApiMemberDetailSectionDescriptors.CreatePipeline().SelectableSectionNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // Discovery mode: -D/--discover lists effective sections (resolves source) by
        // default; --schema opts out to the cheap, offline static schema listing.
        if (options.Discover != null && !options.EffectiveDiscovery)
        {
            var schema = singleTypeMode
                ? GetTypeDocumentSchema(options)
                : ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();

            // The shared render models expose sections owned by adjacent API contexts. Static
            // discovery must stay within the active catalog even though it deliberately shows
            // every section in that catalog rather than applying effective-discovery hiding.
            schema = RestrictSchemaToSections(schema, knownSections);
            if (singleTypeMode)
                schema = ToQueryableSchema(schema, options);

            return (null!, DiscoverOutput.Execute(options.Discover, schema,
                tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
                sectionCostAnnotations: singleTypeMode ? memberPipeline.GetCostAnnotations() : null,
                sectionCategories: singleTypeMode ? memberPipeline.GetCategoryMap() : typePipeline.GetCategoryMap(),
                projection: options));
        }

        // Bare -S renders a bounded overview. Type keeps its authored Info preset: Type Info for a
        // single type and API Info for a listing. This reports the same shape for a 250-member class
        // and an 8-member enum, where the member sections it used to render varied from one section
        // to eight.
        //
        // Selected member details join the fixed overview here: Signature is bounded, while the
        // former info preset also included Decompiled Source and therefore grew with the method
        // body. Broad member lists and member-name overload inventories retain their own compact
        // summary presets; they need separate bounded overview designs. The deprecated `api` shim
        // reaches this preamble too but renders nothing at all -- it prints a migration notice and
        // returns -- so it has no bare -S to convert. See #3547.
        //
        // Type listing joins here as of this slice. It previously had no Fixed section to offer --
        // every section it published was a per-kind member table that grows with the assembly -- so
        // its bare -S resolved to an empty set and fell through to the verbosity ladder, printing
        // all five growing tables. #3648 gave it the bounded API Info section, so bare -S can now
        // mean the same thing here that it means everywhere else.
        var usesBoundedOverview = options is TypeOptions
            || ApiMemberSectionPipelines.UsesDetailPipeline(options);
        var bareSelectSections = options is TypeOptions
            ? singleTypeMode
                ? memberPipeline.InfoSectionNames
                : typePipeline.InfoSectionNames
            : usesBoundedOverview
                ? memberPipeline.FixedOverviewSectionNames
            : singleTypeMode
                ? memberPipeline.InfoSectionNames
                : typePipeline.InfoSectionNames;

        // A focused bare -S that resolves to no sections has to fail loudly. SelectResolver
        // hands back an empty-but-non-null set, and IsRequested's `include is { Count: > 0 }` reads
        // that as "no filter at all" and falls through to the verbosity ladder -- turning a request
        // for a bounded overview into the widest output the command has, with the scanner
        // backpressure -S exists to apply switched off.
        if (usesBoundedOverview && HasNoBareSelectOverview(options, bareSelectSections))
        {
            CommandError.Write(
                "this view publishes no bare -S overview sections.",
                "Use -S <Section> to select one or -D to discover authored categories.");
            return (null!, 1);
        }

        // -S/--select with values: resolve as section filter for backpressure
        var selectResult = SelectResolver.ResolveSelectAsSections(
            options.Select,
            knownSections,
            bareSelectSections,
            singleTypeMode ? memberPipeline.GetCategoryMap() : typePipeline.GetCategoryMap(),
            selectDefault: options.SelectDefault);
        if (ShouldDeferSelectToListing(options, singleTypeMode, selectResult, typePipeline))
        {
            // `-D` advertised these names and `-S` rejected them, on the same command line: the
            // preamble was answering for the single-type pipeline while the render is a listing.
            // Hold the rejection rather than resolving it here, because which pipeline is right is
            // not known until the type lookup runs.
            options = options with { SelectDeferredToListing = true };
        }
        else
        {
            if (SelectOutput.WriteUnresolved(selectResult))
                return (null!, 1);
            if (selectResult.Sections != null)
                options = options with { IncludeSections = selectResult.Sections };
        }

        // An explicit selector on a type-shaped request may ultimately belong to either the exact
        // type or a fallback listing. Both catalogs can resolve the same wildcard or category to
        // different section sets, so validation has to wait until lookup chooses the rendering
        // view. Resolution itself still happens here so exact-type execution retains its sections.
        bool deferTypeSelectionValidation = options is TypeOptions
            && singleTypeMode
            && options.Select is { Length: > 0 };

        // A Markdown column projection without -S historically targets the listing's type rows.
        // The curated minimal preset is API Info, whose fact-table columns cannot satisfy that
        // projection, so make the implicit row scope explicit without changing tabular formats.
        if (!singleTypeMode && options is TypeOptions listingOptions)
            options = ApplyImplicitTypeListingColumnScope(listingOptions);

        // A deferred select has no IncludeSections yet, and the preamble cannot know whether a
        // listing or the single-type view will render, so every selection check below has to stand
        // down: judging the empty set reports a requirement to narrow -S that is neither true nor
        // actionable, and judging the listing's sections preempts the single-type view's own, more
        // accurate rejection. ReresolveSectionsForListing re-runs them once the pipeline is known.
        var selectionSections = options.SelectDeferredToListing ? null : options.IncludeSections;
        if (!deferTypeSelectionValidation
            && options.Discover == null
            && singleTypeMode
            && options is TypeOptions { JsonOutput: true, Count: false }
            && !IsProjectionRequested(options)
            && selectionSections is { Count: > 0 }
            && !ValidateTypeJsonSections(selectionSections))
        {
            return (null!, 1);
        }

        if (!deferTypeSelectionValidation
            && options.Discover == null && options.Count && !options.SelectDeferredToListing
            && !CountOutput.ValidateSingleSection(selectionSections))
            return (null!, 1);

        var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
        if (shapeCount > 1)
        {
            CommandError.Write("specify only one of --value, --urls, or --paths.");
            return (null!, 1);
        }

        if (shapeCount == 1)
        {
            var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
            // Discovery renders its own payload and refuses the shape projections itself with
            // an accurate reason; demanding -S first reports a requirement that is not the problem.
            if (!deferTypeSelectionValidation
                && options.Discover == null && !options.SelectDeferredToListing
                && !ShapeProjectionOutput.ValidateSingleSection(selectionSections, optionName))
                return (null!, 1);
            if (options.Count || options.Print)
            {
                CommandError.Write($"{optionName} cannot be combined with --count or --print.");
                return (null!, 1);
            }
            if (options.Rows is not null)
            {
                CommandError.Write($"--rows cannot be combined with {optionName}; use -n N to limit projected output lines or --row N|first|last to select a projected row.");
                return (null!, 1);
            }
        }

        if (options.JsonArray && shapeCount == 0 && !options.Print)
        {
            CommandError.Write("--json-array requires --value, --urls, --paths, or --print.");
            return (null!, 1);
        }

        if (options.JsonArray && (options.JsonOutput || options.Jsonl))
        {
            CommandError.Write("--json-array cannot be combined with --json or --jsonl.");
            return (null!, 1);
        }

        if (!deferTypeSelectionValidation
            && options.Print && options.Discover == null && !options.SelectDeferredToListing
            && !ValidateApiPrintSelection(selectionSections))
            return (null!, 1);

        if (options.Print && options.Rows is not null)
        {
            CommandError.Write("--rows cannot be combined with --print; use --row N|first|last to choose a printed row.");
            return (null!, 1);
        }

        if (options.PrintRow is not null && !options.Print && shapeCount == 0)
        {
            CommandError.Write("--row requires --print, --value, --urls, or --paths.");
            return (null!, 1);
        }

        if (!deferTypeSelectionValidation
            && options.Discover == null
            && !options.SelectDeferredToListing
            && !OutputFormatResolver.ValidateSingleSectionForTabular(options.TabularExplicitlySet, selectionSections))
            return (null!, 1);

        if (options is MemberOptions memberFormat
            && options.Discover is null)
        {
            memberFormat = NormalizeMemberGraphFormat(memberFormat, selectionSections);
            options = memberFormat;
            if (!ValidateMemberGraphFormat(memberFormat, selectionSections))
                return (null!, 1);
        }

        // Auto-promote verbosity when -S targets specific sections
        if (options.IncludeSections is { Count: > 0 })
        {
            var typeVerbosity = typePipeline.GetRequiredVerbosity(options.IncludeSections);
            var memberVerbosity = memberPipeline.GetRequiredVerbosity(options.IncludeSections);
            var requiredVerbosity = typeVerbosity > memberVerbosity ? typeVerbosity : memberVerbosity;
            if (requiredVerbosity > options.Verbosity)
                options = options with { Verbosity = requiredVerbosity };
        }

        // Warn if tabular output is combined with detailed verbosity without section selector
        if (!options.Count)
            OutputFormatResolver.WarnIfTabularDetailMismatch(options.Tabular, options.Verbosity, options.IncludeSections);

        // Resolve the tool-owned .dotnet-inspectconfig once per invocation at the
        // CLI edge and attach the decompiler spelling options to the flowed
        // options. Config discovery lives only here; the decompiler library stays
        // a pure function of explicit PrinterOptions. RenderOptions is attached
        // unconditionally (harmless when no source renders). Parse/read warnings
        // are carried on a latch and emitted at the exact point a decompiled-source
        // render consumes the config (see RenderConfigWarningSink), so a bad config
        // never dirties stderr for a run that does not show styled source — a
        // metadata projection (--json/--count/tabular), a section that does not
        // read source (-S Facts), or a fidelity-only projection (whose result is
        // style-invariant, so the config is genuinely not consumed) — and always
        // surfaces once, never as a silent success, on a run that does.
        //
        // Discovery (-D) is excluded here rather than at the consumption site: it
        // lists which sections would render by probing them into a discarded view,
        // so its internal source render must not be mistaken for user-visible
        // styled output. No latch is attached for a discovery request.
        var renderStyle = RenderStyleConfig.Resolve(Environment.CurrentDirectory);
        // --taste is the one-invocation form of the config's full-taste aggregate.
        // It applies after the file resolves and wins for the knobs the aggregate
        // covers, so an explicit gesture is not silently narrowed by a checked-in
        // config; knobs outside the endorsed set keep whatever the file selected.
        var renderOptions = options.RequestAllTaste
            ? ILInspector.Decompiler.Pipeline.StyleOptionCatalog.ApplyFullTaste(renderStyle.Options)
            : renderStyle.Options;
        // Readable local names are the user-facing CLI default. Library, harness,
        // fidelity, and corpus paths keep PrinterOptions.Default (V_index), while
        // an explicit config value of false restores slot names for CLI rendering.
        // --readable-names is the one-run override for that configuration.
        if (options.RequestReadableLocalNames)
            renderOptions = renderOptions with { ReadableLocalNames = true };
        options = options with
        {
            RenderOptions = renderOptions,
            RenderConfigWarnings = renderStyle.Warnings.Count > 0 && options.Discover is null
                ? new RenderConfigWarningSink(renderStyle.Warnings)
                : null,
        };

        return (new PreambleResult(
            options,
            typePipeline,
            memberPipeline,
            typeCatalog.QueryRegistry), null);
    }

    private static bool ValidateMemberGraphFormat(
        MemberOptions options,
        IReadOnlyCollection<string>? sections)
    {
        if (options.Tree)
        {
            if (options.FormatFlagExplicitlySet)
            {
                CommandError.Write(
                    "--tree is a standalone output format and cannot combine with another output format.");
                return false;
            }

            if (sections is not { Count: 1 }
                || !sections.Contains(SectionNames.CallGraph, StringComparer.OrdinalIgnoreCase))
            {
                CommandError.Write(
                    "--tree requires exactly one selected tree shape.",
                    "Use -S \"Call Graph\" --tree.");
                return false;
            }
        }

        if (options.MermaidOutput
            && (sections is not { Count: 1 }
                || !sections.Contains(SectionNames.CallGraph, StringComparer.OrdinalIgnoreCase)))
        {
            CommandError.Write(
                "--mermaid requires exactly one selected graph.",
                "Use -S \"Call Graph\" --mermaid.");
            return false;
        }

        if (options.EmbeddedMermaid
            && (sections is null
                || !sections.Contains(SectionNames.CallGraph, StringComparer.OrdinalIgnoreCase)))
        {
            CommandError.Write(
                "--markdown --mermaid requires the Call Graph section.",
                "Select it with -S \"Call Graph\"; other Markdown sections may be selected with it.");
            return false;
        }

        return true;
    }

    private static MemberOptions NormalizeMemberGraphFormat(
        MemberOptions options,
        IReadOnlyCollection<string>? sections)
    {
        if (options.Tree && !options.FormatFlagExplicitlySet)
        {
            return options with
            {
                JsonOutput = false,
                Tabular = false,
                Tsv = false,
                Jsonl = false,
                TabularExplicitlySet = false,
                PlainText = false,
                MermaidOutput = false,
            };
        }

        bool onlyCallGraph =
            sections is { Count: 1 }
            && sections.Contains(SectionNames.CallGraph, StringComparer.OrdinalIgnoreCase);
        if (options.MermaidOutput && !options.FormatFlagExplicitlySet && !onlyCallGraph)
            return options with { MermaidOutput = false };

        return options;
    }

    static bool MightPeelDottedGenericMemberSelector(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        var lastDot = FqnParser.LastTopLevelDot(typeName);
        if (lastDot <= 0 || lastDot == typeName.Length - 1)
            return false;

        return MemberTargetSelector.Parse(typeName[(lastDot + 1)..]).GenericArity.HasValue;
    }

    private static bool ValidateApiPrintSelection(HashSet<string>? includeSections)
    {
        if (includeSections is { Count: 1 })
            return true;

        CommandError.Write("--print requires -S/--select to match exactly one printable section.");
        return false;
    }

    internal static void ApplySurfaceFilters(ApiSurface api, ApiOptions options, string? typeFilter = null)
    {
        bool filtersApplied = false;
        if (!string.IsNullOrEmpty(typeFilter))
        {
            api.Types = api.Types
                .Where(t => TypeMatcher.MatchesTypeFilter(t.FullName, typeFilter))
                .ToList();
            filtersApplied = true;
        }

        if (options.KindFilter.Count > 0)
        {
            api.Types = api.Types.Where(t => options.KindFilter.Contains(t.Kind)).ToList();
            filtersApplied = true;
        }

        if (options.UnsafeOnly)
        {
            foreach (var type in api.Types)
            {
                type.Members = type.Members.Where(m => m.IsUnsafe).ToList();
            }
            api.Types = api.Types.Where(t => t.Members.Count > 0).ToList();
            filtersApplied = true;
        }

        if (filtersApplied)
            RecomputeSurfaceCounts(api);
    }

    private static void RecomputeSurfaceCounts(ApiSurface api)
    {
        api.PublicTypeCount = api.Types.Count;
        api.PublicMethodCount = api.Types.Sum(
            type => type.Members.Count(ApiMemberSectionDescriptors.IsMethodLike));
        api.PublicPropertyCount = api.Types.Sum(
            type => type.Members.Count(member => member.Kind == "property"));
        api.PublicFieldCount = api.Types.Sum(
            type => type.Members.Count(member => member.Kind == "field"));
        api.PublicEventCount = api.Types.Sum(
            type => type.Members.Count(member => member.Kind == "event"));
    }

    internal static ApiSurface ProjectSurfaceToSections(
        ApiSurface api,
        HashSet<string>? sections)
    {
        if (sections is not { Count: > 0 })
            return api;

        var selectedKinds = new HashSet<string>(StringComparer.Ordinal);
        if (sections.Contains(SectionNames.Classes))
            selectedKinds.Add("class");
        if (sections.Contains(SectionNames.Structs))
            selectedKinds.Add("struct");
        if (sections.Contains(SectionNames.Interfaces))
            selectedKinds.Add("interface");
        if (sections.Contains(SectionNames.Enums))
            selectedKinds.Add("enum");
        if (sections.Contains(SectionNames.Delegates))
            selectedKinds.Add("delegate");

        var projected = new ApiSurface
        {
            Name = api.Name,
            Version = api.Version,
            Source = api.Source,
            Types = api.Types.Where(type => selectedKinds.Contains(type.Kind)).ToList(),
            InspectionFailures = api.InspectionFailures,
            Library = api.Library,
            Tfm = api.Tfm,
            RepositoryUrl = api.RepositoryUrl,
            TypeForwarders = api.TypeForwarders,
            IsTypeForwardingAssembly = api.IsTypeForwardingAssembly,
            SurfaceClassification = api.SurfaceClassification,
            SurfaceClassificationInspection = api.SurfaceClassificationInspection,
        };

        if (selectedKinds.Count > 0)
        {
            RecomputeSurfaceCounts(projected);
        }
        else
        {
            projected.PublicTypeCount = api.PublicTypeCount;
            projected.PublicMethodCount = api.PublicMethodCount;
            projected.PublicPropertyCount = api.PublicPropertyCount;
            projected.PublicEventCount = api.PublicEventCount;
            projected.PublicFieldCount = api.PublicFieldCount;
        }

        return projected;
    }

    private static string? GetExactSelectedSection(
        ApiOptions options,
        IEnumerable<string> sectionNames)
    {
        var knownSections = sectionNames.ToArray();
        if (options.Select is [var selector])
        {
            var result = SelectResolver.ResolveSelectAsSections(
                [selector],
                knownSections);
            return result.ExactSections.Count == 1
                ? result.ExactSections.Single()
                : null;
        }

        if (options.Select is not null
            || options.SelectDefault
            || options.IncludeSections is not { Count: 1 })
        {
            return null;
        }

        var selected = options.IncludeSections.Single();
        return knownSections.FirstOrDefault(
            section => section.Equals(selected, StringComparison.OrdinalIgnoreCase));
    }

    private static int ReportEmptyExactSection(string section)
    {
        CommandError.WriteLine($"This section ({section}) produced no output.");
        return 1;
    }

    internal static ApiType BuildFilteredTypeForSections(ApiType type, ApiOptions options)
    {
        var members = type.Members.Where(m => !MemberFilters.IsCompilerGenerated(m.Name));

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter));

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe);

        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind));

        var filteredMembers = members.ToList();
        if (options.Limit.HasValue && options.Limit.Value < filteredMembers.Count)
            filteredMembers = OrderMembersForLimit(filteredMembers)
                .Take(options.Limit.Value)
                .ToList();

        return CopyTypeWithMembers(type, filteredMembers);
    }

    private static IOrderedEnumerable<ApiMember> OrderMembersForLimit(
        IEnumerable<ApiMember> members)
        => members
            .OrderBy(member => ApiOutputFormatter.GetMemberSortOrder(member.Kind))
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(ApiOutputFormatter.GetMemberSignatureSortKey, StringComparer.Ordinal);

    private static ApiType CopyTypeWithMembers(ApiType type, List<ApiMember> members)
        => new()
        {
            Namespace = type.Namespace,
            Name = type.Name,
            MetadataName = type.MetadataName,
            DefinitionName = type.DefinitionName,
            Accessibility = type.Accessibility,
            Kind = type.Kind,
            Attributes = type.Attributes,
            EnumUnderlyingType = type.EnumUnderlyingType,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            IsByRefLike = type.IsByRefLike,
            IsReadOnly = type.IsReadOnly,
            BaseType = type.BaseType,
            Interfaces = type.Interfaces,
            DerivedTypes = type.DerivedTypes,
            TypeParameters = type.TypeParameters,
            Members = members,
            SourceFilePath = type.SourceFilePath,
            SourceUrl = type.SourceUrl,
            GitHubBrowseUrl = type.GitHubBrowseUrl,
            SourceLineNumber = type.SourceLineNumber,
            SourceChecksum = type.SourceChecksum,
            SourceChecksumAlgorithm = type.SourceChecksumAlgorithm,
            SourceResolution = type.SourceResolution,
            AdditionalSourceFiles = type.AdditionalSourceFiles,
            IsForwarded = type.IsForwarded,
            SourceAssemblyPath = type.SourceAssemblyPath,
            Documentation = type.Documentation,
        };

    internal static DocumentSchema GetTypeDocumentSchema(ApiOptions options)
    {
        var schema = MergeSchemas(
            ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<MethodGroupsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<MethodsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<MemberIndexView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<OperatorsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<ExplicitInterfaceImplementationsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<ExtensionMethodsView>()!.ToDocumentSchema(),
            ApiViewContext.Default.GetSchemaInfo<EventsView>()!.ToDocumentSchema());
        // MemberCodeView owns source/IL/fact/call-graph sections. Type discovery also needs
        // those schema entries because the type pipeline exposes whole-type code sections.
        var detailSchema = MergeSchemas(schema,
            ApiViewContext.Default.GetSchemaInfo<MemberCodeView>()!.ToDocumentSchema());
        if (!ApiMemberSectionPipelines.UsesDetailPipeline(options))
            return detailSchema;
        if (detailSchema.GetSection(SectionNames.Calls) == null)
            detailSchema.Add(SectionNames.Calls, "column", "IL Offset", "Opcode", "Call Kind", "Callee", "Operand Token", "Return Address");
        if (detailSchema.GetSection(SectionNames.Callers) == null)
            detailSchema.Add(SectionNames.Callers, "column", "Caller", "IL Offset", "Opcode", "Call Kind", "Operand Token", "Return Address");
        if (detailSchema.GetSection(SectionNames.UnsafeOperations) == null)
            detailSchema.Add(SectionNames.UnsafeOperations, "column", "Reason", "Detail", "Kind", "IL", "Token");
        // One bidirectional section, so one field list: the union of what the outbound and inbound
        // halves each used to declare separately.
        detailSchema.Add(SectionNames.CallGraph, "field",
            "Fanout", "FanoutCount",
            "Fanin", "FaninCount",
            "Depth", "MaxDepth",
            "Loop", "InLoop", "Looping",
            "Root", "RootKind", "Classification",
            "Source", "Assembly",
            "Alloc", "Allocations",
            "Copy", "Copies",
            "Unsafe",
            "Reflection",
            "Throw", "Throws", "ThrowSites",
            "Exceptions", "ExceptionTypes", "ConstructedExceptions",
            "Catch", "Catches",
            "Finally", "Finallys",
            "EvidenceIL", "Evidence", "IL");
        return detailSchema;
    }

    private static DocumentSchema MergeSchemas(params DocumentSchema[] schemas)
    {
        var merged = new DocumentSchema();
        foreach (var schema in schemas)
        {
            foreach (var name in schema.SectionNames)
            {
                var section = schema.GetSection(name);
                if (section == null)
                {
                    merged.AddSection(name);
                    continue;
                }

                var items = section.Items.Select(i => i.Name).ToArray();
                if (items.Length > 0)
                    merged.Add(name, section.ItemKind, items);
                else
                    merged.AddSection(name);
            }
        }

        return merged;
    }

    private static DocumentSchema RestrictSchemaToSections(DocumentSchema schema, IReadOnlyCollection<string> sectionNames)
    {
        var filtered = new DocumentSchema();
        foreach (var name in sectionNames)
        {
            var section = schema.GetSection(name);
            if (section == null)
                continue;

            var items = section.Items.Select(i => i.Name).ToArray();
            if (items.Length > 0)
                filtered.Add(name, section.ItemKind, items);
            else
                filtered.AddSection(name);
        }

        return filtered;
    }

    /// <summary>
    /// Acquires the portable PDB for an assembly (symbol server / symbol
    /// package) and returns its on-disk path, so the decompiler can render
    /// real local-variable names instead of V_n slots. Best-effort: returns
    /// null when no PDB can be obtained (offline, Windows PDB, no symbols).
    /// </summary>
    internal static async Task<string?> TryAcquirePdbPathAsync(
        string dllPath,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var service = SourceLinkService.Open(dllPath, logger.Log);
            var context = service.Context;
            if (context.NeedsPdb)
            {
                var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                    ? PackageExtractor.ParsePackageReference(options.PackagePath)
                    : (null, null);
                await SourceEnricher.AcquirePdbAsync(context, httpClient, pkgName, pkgVersion,
                    isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log,
                    sourceOptions: options.SourceOptions,
                    cancellationToken: cancellationToken);
            }
            return context.PortablePdbPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal static HashSet<string> GetRequestedMemberSections(ApiType type, ApiOptions options)
    {
        var pipeline = ApiMemberSectionPipelines.Create(options);
        var sections = new HashSet<string>(
            pipeline.GetEffectiveSections(type, options.Verbosity, options.IncludeSections),
            StringComparer.OrdinalIgnoreCase);
        if (options.Discover is { Length: > 0 } discover)
        {
            var resolved = SelectResolver.ResolveSelectAsSections(
                discover, pipeline.SelectableSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
            if (!resolved.HasError && resolved.Sections is { Count: > 0 })
                sections.UnionWith(resolved.Sections);
        }
        return sections;
    }

    // ===== Full API Surface Rendering =====

    internal static int WriteFullApiOutput(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        ApplySurfaceFilters(api, options, (options as TypeOptions)?.TypeFilter);
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var exactSection = GetExactSelectedSection(options, pipeline.AllSectionNames);
        if (!options.Count
            && exactSection is not null
            && !pipeline.GetEffectiveSections(
                    api,
                    options.Verbosity,
                    options.IncludeSections)
                .Contains(exactSection, StringComparer.OrdinalIgnoreCase))
        {
            return ReportEmptyExactSection(exactSection);
        }

        // Fail closed: the type-listing surface has no dispatch for payload projections
        // (--print/--value/--urls/--paths); its sections are type-name tables that expose no
        // printable payload. Report that honestly before rendering, rather than emitting the
        // whole document and then tripping the projection audit (#3390) with a "bug in
        // dotnet-inspect" message. --count is a payload projection the surface does honor, so
        // it is excluded from this guard.
        if (IsProjectionRequested(options))
            return RejectSurfacePayloadProjection(options);

        if (api.InspectionFailures.Count > 0
            && (options.Count
                || options.Tabular
                || options.Verbosity < Verbosity.Normal))
        {
            CommandError.WriteWarning(
                $"API inspection rejected {api.InspectionFailures.Count} metadata row(s); "
                + "use normal verbosity or JSON for failure details.");
        }

        if (options.JsonOutput && !options.Count)
        {
            // --fields/--columns select table columns; document JSON has no column-slicing
            // facility, so the combination is rejected rather than silently dropped.
            if (IsColumnProjectionRequested(options))
                return RejectColumnProjectionUnderJson(suggestPayloadProjection: false);
            var outputApi = ProjectSurfaceToSections(api, options.IncludeSections);
            Console.WriteLine(JsonSerializer.Serialize(outputApi, ApiJsonContext.Default.ApiSurface));
            return 0;
        }

        var (view, _) = ApiOutputFormatter.BuildFullApiView(api, options);

        if (options.Count)
        {
            var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
            writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
            var markdown = MarkoutSerializer.Serialize(view, ApiViewContext.Default, writerOptions);
            if (!TryReportEmptyProjection(markdown, options))
                return 1;
            CountOutput.WriteCountFromMarkdown(markdown);
        }
        else if (options.Tabular)
        {
            if (ApiOutputFormatter.ShouldRenderSurfaceFactTableView(options))
            {
                // Deliberately the same machinery as the fall-through below -- projection,
                // diagnostics, and row limiting all included -- differing only in WHAT is
                // serialized. Writing straight to the console here instead skipped
                // DiagnoseRendered, so `--fields Value` produced empty output and exit 0 while
                // the same projection against `Type Info` and `Library Info` reported that the
                // field does not exist.
                var factRows = OutputFormatter.RenderProjectedTable(!options.NoHeader, options.Tsv, options.Jsonl,
                    options.Columns, options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(view.ApiInfo!, writer, formatter, ApiViewContext.Default, writerOptions));
                ProjectionDiagnostics.DiagnoseRendered(options.Fields ?? options.Columns, factRows);
                if (!TryReportEmptyProjection(factRows, options))
                    return 1;
                Console.Out.Write(OutputFormatter.LimitRenderedTableRows(factRows, options.Rows, !options.NoHeader));
                return 0;
            }

            var (tableView, _) = ApiOutputFormatter.BuildSurfaceTableView(api, options);
            var rendered = OutputFormatter.RenderProjectedTable(!options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(tableView, writer, formatter, ApiViewContext.Default, writerOptions));
            ProjectionDiagnostics.DiagnoseRendered(options.Fields ?? options.Columns, rendered);
            if (!TryReportEmptyProjection(rendered, options))
                return 1;
            Console.Out.Write(OutputFormatter.LimitRenderedTableRows(rendered, options.Rows, !options.NoHeader));
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
            if (options.PlainText)
            {
                // Buffered rather than written straight to the console so the empty-render gate
                // can see the result. Writing directly is what let an emptying projection print
                // nothing and exit 0 here while every sibling path reported it.
                var plain = new StringWriter();
                MarkoutSerializer.Serialize(view, plain, options.CreateFormatter(), ApiViewContext.Default, writerOptions);
                var plainText = plain.ToString();
                if (!TryReportEmptyProjection(plainText, options))
                    return 1;
                Console.Out.Write(plainText);
            }
            else
            {
                writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
                var markdownWriter = new StringWriter { NewLine = "\n" };
                MarkoutSerializer.Serialize(
                    view, markdownWriter, new MarkdownFormatter(), ApiViewContext.Default, writerOptions);
                var markdown = markdownWriter.ToString().TrimEnd();
                if (!TryReportEmptyProjection(markdown, options))
                    return 1;
                OutputFormatter.WriteLfLine(Console.Out, markdown);
            }
        }

        return 0;
    }

    /// <summary>
    /// Fails a projection that rendered nothing at all, rather than exiting 0 having printed
    /// nothing. Returns false when the caller should stop.
    /// </summary>
    /// <remarks>
    /// This is the gate for "a projection that matches nothing must not look like success".
    /// <see cref="CommandExecutionTests.Type_Listing_UnmatchedProjection_FailsByNameRatherThanRenderingNothing"/>
    /// is the non-vacuity test: it fails if this check stops firing, and
    /// <c>Type_Listing_LegitimateProjections_SurviveTheEmptyRenderGate</c> is the companion that
    /// fails if it starts firing too widely. Both conditions below are load-bearing and each was
    /// added because its absence produced a real false positive:
    ///
    /// <list type="number">
    /// <item>A projection must actually be active. An empty render with no <c>--fields</c> or
    /// <c>--columns</c> gives this projection audit no name to diagnose. Exact empty sections are
    /// enforced separately before rendering.</item>
    /// <item>Every projected name must resolve nowhere. Emptiness alone cannot tell an unknown
    /// name from a known field that happens to hold no value: <c>-S "API Info" --fields Version</c>
    /// against a local .dll renders nothing because that assembly has no version, and <c>Version</c>
    /// is a perfectly valid field that <c>-D "API Info"</c> advertises.</item>
    /// <item>The name must resolve as the KIND being projected. "Valid somewhere in the document"
    /// is too weak on its own: <c>Type</c> is a column of the <c>Classes</c> table and is a field
    /// nowhere, so <c>-S "API Info" --fields Type</c> would be validated by an unrelated section's
    /// column and print nothing at exit 0 -- the success-shaped empty output this gate exists to
    /// prevent.</item>
    /// </list>
    ///
    /// The name check is deliberately a NARROWING condition on an already-empty render, never a
    /// pre-check. Two earlier attempts validated names up front and both produced false negatives,
    /// because the set of legitimately projectable names is wider than any one section's schema:
    /// <c>-S "API Info" --columns Field</c> names a column the fact-table renderer synthesizes and
    /// the schema never lists, and <c>-S Classes --fields Types</c> names a document-level field
    /// that survives regardless of which section is selected. Both of those RENDER, so ordering
    /// emptiness first puts the schema's blind spots out of reach.
    /// </remarks>
    private static bool TryReportEmptyProjection(string rendered, ApiOptions options)
    {
        if (!string.IsNullOrWhiteSpace(rendered))
            return true;

        var names = options.Fields ?? options.Columns;
        if (names is not { Length: > 0 })
            return true;

        // Resolved by KIND across EVERY section, not against the selected sections. Two
        // independent corrections are folded in here, and dropping either one reopens a real
        // false positive found in review:
        //
        // Across all sections, because a document-level field belongs to no section in
        // particular -- `Version` is advertised under `API Info` but survives whichever section
        // is selected -- so checking only the selection reports it unresolved. That is normally
        // unreachable because the document fields keep the render non-empty, but filtering a
        // wildcard-selected table to zero rows
        // (`-t "NoSuchType*" -S "Class*" --fields Version`) empties the render and exposes it.
        //
        // By kind, because "valid somewhere" is too weak on its own: `Type` is a Classes COLUMN
        // and never a field, so `-S "API Info" --fields Type` would otherwise be validated by an
        // unrelated section's column and silently succeed while printing nothing. `--fields` can
        // only be satisfied by a field and `--columns` only by a column.
        var wantedKind = options.Fields is { Length: > 0 } ? "field" : "column";
        var schema = ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();
        var candidates = new List<string>();
        foreach (var section in schema.SectionNames)
        {
            foreach (var item in schema.Discover(section) ?? [])
            {
                if (!string.Equals(item.Kind, wantedKind, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Name only, never StableName. The stable name is the schema's internal
                // identifier and markout does not project by it -- `--fields Assembly`, the
                // stable name of `Library`, renders nothing on base and on head. Accepting it
                // here would let a name the user cannot actually project by satisfy the gate
                // (found by MAI-Code). Of the whole schema only `Library`/`Assembly`,
                // `TFM`/`Tfm`, and `Target Library`/`TargetLibrary` differ at all.
                candidates.Add(item.Name);
            }
        }

        // Matched by markout's own projection matcher rather than by set membership, because
        // projection names may be wildcards: `--fields "Ver*"` legitimately selects `Version`,
        // and an exact comparison rejects it (found by GPT-5.6). Collecting the wanted-kind
        // names into a throwaway single-section schema is what lets markout answer "does this
        // pattern match anything of this kind" -- reimplementing the glob here would be a second
        // matcher that could drift from the one that actually performs the projection.
        const string ProbeSection = "probe";
        var probe = new DocumentSchema().Add(ProbeSection, wantedKind, [.. candidates]);
        if (probe.ValidateProjection(ProbeSection, names).Resolved.Length > 0)
            return true;

        var kind = options.Fields is { Length: > 0 } ? "fields" : "columns";
        CommandError.Write($"No {kind} matched projection: {string.Join(", ", names)}");
        return false;
    }

    // ===== Method Source Resolution =====

    /// <param name="Source">Resolved source, or null when none could be resolved.</param>
    /// <param name="PdbPath">The acquired portable PDB path, when one was acquired.</param>
    /// <param name="MemberHasNoBody">
    /// True when the member carries no IL body, so <paramref name="Source"/> is absent because
    /// there is nothing to show rather than because resolution failed (issue #3299).
    /// </param>
    /// <param name="MemberHasNoAuthoredDeclaration">
    /// True when the member has a body but its source range does not identify one vouched
    /// authored declaration to isolate.
    /// </param>
    /// <param name="MemberSourceTooComplex">
    /// True when verified source exceeded the bounded lexical-complexity limit.
    /// </param>
    /// <param name="MemberSourceCoordinatesInvalid">
    /// True when portable-PDB sequence-point coordinates cannot address the verified source.
    /// </param>
    internal sealed record ResolvedMethodSource(
        MethodSourceContext? Source,
        string? PdbPath,
        bool MemberHasNoBody = false,
        bool MemberHasNoAuthoredDeclaration = false,
        bool MemberSourceTooComplex = false,
        bool MemberSourceCoordinatesInvalid = false);

    internal static async Task<ResolvedMethodSource> ResolveMethodSourceAsync(
        string dllPath, string typeName, string methodName, int overloadIndex,
        ApiOptions options, HttpClient httpClient, VerboseLogger logger, bool fetchSource = true,
        bool publicOnly = true, int metadataToken = 0)
    {
        try
        {
            using var service = SourceLinkService.Open(dllPath, logger.Log);
            var context = service.Context;

            // Acquire PDB if needed (same flow as SourceEnricher)
            if (context.NeedsPdb)
            {
                var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                    ? PackageExtractor.ParsePackageReference(options.PackagePath)
                    : (null, null);

                await SourceEnricher.AcquirePdbAsync(context, httpClient,
                    pkgName, pkgVersion,
                    isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log,
                    sourceOptions: options.SourceOptions);
            }

            // Capture the acquired portable PDB path now so the decompiler can reuse it for local
            // names even when SourceLink/source resolution below fails (PDB available, source not).
            string? pdbPath = context.PortablePdbPath;

            // A member with no IL body has no authored source to resolve, whatever the PDB and
            // SourceLink situation is. Ask metadata for that fact before the resolution attempt,
            // so an empty result can say why instead of looking like a silent failure
            // (issue #3299). Only a definite "no" counts; an unreadable token stays unknown.
            bool memberHasNoBody = metadataToken != 0 && context.MethodHasBody(metadataToken) == false;

            if (!fetchSource || !service.HasPdb || !service.HasSourceLink)
                return new ResolvedMethodSource(null, pdbPath, memberHasNoBody);

            var methodInfo = service.ResolveMethodSource(typeName, methodName, overloadIndex, publicOnly, metadataToken);
            if (methodInfo == null)
                return new ResolvedMethodSource(null, pdbPath, memberHasNoBody);

            // Honor the source the portable PDB records when it is present locally: a non-reproducible
            // (local dev) build keeps a real local path whose exact compiled bytes may exist only here,
            // so the remote SourceLink URL would 404 or differ. The checksum authenticates the on-disk
            // bytes against the portable PDB; remote SourceLink is the fallback for reproducible builds.
            string? content = null;
            var localBytes = DotnetInspector.Services.AuthoredSourceAcquisition.TryReadVerifiedLocalSource(
                methodInfo.FilePath, methodInfo.ChecksumAlgorithm, methodInfo.Checksum);
            byte[]? repoBytes;
            if (localBytes != null)
            {
                content = NormalizeAuthoredSourceLineEndings(
                    DotnetInspector.Services.AuthoredSourceAcquisition.DecodeSourceText(localBytes));
            }
            // Opt-in (--repo): read the committed blob at the SourceLink commit from a local clone,
            // authenticated by the same PDB checksum, before touching the network. Useful for a
            // reproducible build whose sources are private or simply already cloned on this machine.
            else if (options.SourceRepositories.Length > 0
                && (repoBytes = DotnetInspector.Services.LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                    methodInfo.SourceUrl, methodInfo.ChecksumAlgorithm, methodInfo.Checksum,
                    options.SourceRepositories)) != null)
            {
                content = NormalizeAuthoredSourceLineEndings(
                    DotnetInspector.Services.AuthoredSourceAcquisition.DecodeSourceText(repoBytes));
            }
            else if (methodInfo.SourceUrl != null)
            {
                var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
                var fetch = await AuthoredSourceAcquisition.FetchVerifiedSourceTextAsync(
                    fetcher,
                    methodInfo.SourceUrl,
                    methodInfo.ChecksumAlgorithm,
                    methodInfo.Checksum);
                content = fetch.Text is null
                    ? null
                    : NormalizeAuthoredSourceLineEndings(fetch.Text);
                if (fetch.Failure is not null)
                    logger.LogWarning(fetch.Failure);
            }

            if (content == null)
                return new ResolvedMethodSource(null, pdbPath, memberHasNoBody);

            return SliceResolvedMethodSource(
                content,
                methodInfo.StartLine,
                methodInfo.EndLine,
                methodName,
                methodInfo.SourceUrl ?? methodInfo.FilePath,
                pdbPath,
                methodInfo.SequencePointStartLines);
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to resolve method source for {typeName}.{methodName}: {ex.Message}");
            return new ResolvedMethodSource(null, null);
        }
    }

    internal static string NormalizeAuthoredSourceLineEndings(string content)
        // Normalize only CR/LF forms. Other characters recognized by string.ReplaceLineEndings,
        // including form feed, are not C# physical line breaks and must not shift PDB coordinates.
        => content.Replace("\r\n", "\n").Replace('\r', '\n');

    internal static ResolvedMethodSource SliceResolvedMethodSource(
        string content,
        int startLine,
        int endLine,
        string methodName,
        string sourceLocation,
        string? pdbPath,
        IReadOnlyList<int>? visibleSequencePointStartLines = null)
    {
        try
        {
            string? sourceCode = BodySlicer.ExtractMethodBody(
                content,
                startLine,
                endLine,
                methodName,
                visibleSequencePointStartLines);

            // The range does not identify one authored declaration: report no source rather than
            // a type header, initializer, or structurally unknown span.
            return sourceCode is null
                ? new ResolvedMethodSource(
                    null,
                    pdbPath,
                    MemberHasNoAuthoredDeclaration: true)
                : new ResolvedMethodSource(
                    new MethodSourceContext(sourceCode, sourceLocation),
                    pdbPath);
        }
        catch (CSharpTextComplexityException)
        {
            return new ResolvedMethodSource(
                null,
                pdbPath,
                MemberSourceTooComplex: true);
        }
        catch (InvalidSequencePointCoordinatesException)
        {
            return new ResolvedMethodSource(
                null,
                pdbPath,
                MemberSourceCoordinatesInvalid: true);
        }
    }

    // ===== Single Type Rendering =====

    // --json selects an output format; --print/--value/--urls/--paths select an
    // output shape. They compose, so the plain type-surface serializer must not
    // claim a request that a projection owns.
    private static bool IsProjectionRequested(ApiOptions options)
        => options.Print || options.Value || options.Urls || options.Paths;

    // --fields/--columns select table columns. They compose with the row-oriented formats
    // (--table/--tsv/--jsonl) and, when paired with a scalar payload projection, pick which
    // column feeds --value/--print. They do not compose with document --json, which renders the
    // whole typed graph and has no column-slicing (jq-style) facility, so the combination is
    // rejected rather than silently dropped. See dotnet-inspect#3386 and richlander/markout#173.
    private static bool IsColumnProjectionRequested(ApiOptions options)
        => options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };

    private static int RejectColumnProjectionUnderJson(bool suggestPayloadProjection)
    {
        var hint = suggestPayloadProjection
            ? " Use --tsv, --jsonl, or --table to project columns, or add --value/--print to project a payload."
            : " Use --tsv, --jsonl, or --table to project columns.";
        CommandError.Write(
            "--fields/--columns select table columns and cannot be combined with --json, "
            + "which renders the whole document." + hint);
        return 1;
    }

    private static int RejectSurfacePayloadProjection(ApiOptions options)
    {
        var flag = options.Print ? "--print"
            : options.Value ? "--value"
            : options.Urls ? "--urls"
            : "--paths";
        CommandError.Write(
            $"{flag} is not supported when listing types; the listing exposes no printable "
            + "payload. Inspect a single type (for example `type <Name>`) to project a member payload.");
        return 1;
    }

    private static readonly HashSet<string> TypeJsonModelSections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            SectionNames.TypeInfo,
            SectionNames.Values,
            SectionNames.TypeParameters,
            SectionNames.TypeInterfaces,
            SectionNames.Baseclass,
            SectionNames.Constructors,
            SectionNames.Finalizer,
            SectionNames.Fields,
            SectionNames.Properties,
            SectionNames.MethodGroups,
            SectionNames.Methods,
            SectionNames.MemberIndex,
            SectionNames.Operators,
            SectionNames.ExplicitInterfaceImplementations,
            SectionNames.ExtensionMethods,
            SectionNames.Events,
            SectionNames.SourceFiles,
        };

    private static bool ValidateTypeJsonSections(IReadOnlyCollection<string> sections)
    {
        var unsupported = sections
            .Where(section => !TypeJsonModelSections.Contains(section))
            .OrderBy(section => section, StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length == 0)
            return true;

        CommandError.Write(
            $"--json cannot represent the selected type section(s): {string.Join(", ", unsupported)}.",
            "Use Markdown, --table, --tsv, or --jsonl so section-produced rows are preserved.");
        return false;
    }

    internal static async Task<int> WriteTypeOutputAsync(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options, TextWriter? output = null)
    {
        var sink = output ?? Console.Out;

        if (IsInvalidAnnotatedSourceDocumentJsonSelection(options))
        {
            CommandError.Write(
                $"section '{SectionNames.AnnotatedSourceDocument}' must be the only selected section under --json.");
            return 1;
        }

        if (options is TypeOptions { ShapeOutput: true } typeOptions && !options.Count)
        {
            ApiOutputFormatter.WriteShapeOutput(
                type,
                foundIn,
                packageName,
                packageVersion,
                options.MemberFilter,
                options.KindFilter,
                options.Verbosity,
                typeOptions.MemberLimit);
            return 0;
        }

        if (options is TypeOptions { JsonOutput: true } && !options.Count)
        {
            var pipeline = ApiMemberSectionPipelines.Create(options);
            var jsonExactSection = GetExactSelectedSection(options, pipeline.AllSectionNames);
            if (jsonExactSection is not null
                && !pipeline.GetEffectiveSections(
                        BuildTypeForJsonOutput(type, options),
                        options.Verbosity,
                        options.IncludeSections)
                    .Contains(jsonExactSection, StringComparer.OrdinalIgnoreCase))
            {
                return ReportEmptyExactSection(jsonExactSection);
            }
        }

        bool sourceDocumentJson = IsAnnotatedSourceDocumentJson(options);
        bool barePayloadRenderer =
            options.Bare && !options.Count && !options.JsonOutput;
        if (options is MemberOptions memberOptions
            && (memberOptions.MemberSourceTooComplex
                || memberOptions.MemberSourceCoordinatesInvalid)
            && !IsProjectionRequested(options)
            && !barePayloadRenderer
            && (options.Count
                || options.Tabular
                || options.JsonOutput)
            && GetRequestedMemberSections(type, options)
                .Overlaps([SectionNames.OriginalSource, SectionNames.SourceDiff]))
        {
            string format = options.Count
                ? "--count"
                : options.Jsonl
                    ? "--jsonl"
                    : options.Tsv
                        ? "--tsv"
                        : options.Tabular
                            ? "--table"
                            : "Document --json";
            string guidance = options.Count
                ? "Remove --count to render the section failure."
                : "Use Markdown/plaintext output, or add --print to project the section payload.";
            string failure = memberOptions.MemberSourceTooComplex
                ? "Authored source extraction stopped because the source exceeds the lexical "
                    + "complexity limit."
                : "Authored source extraction stopped because the portable-PDB sequence-point "
                    + "coordinates cannot address the verified source.";
            CommandError.Write(
                failure + $" {format} cannot represent this code-section "
                + "failure. " + guidance);
            return 1;
        }

        if (options.JsonOutput && !options.Count && !IsProjectionRequested(options) && !sourceDocumentJson)
        {
            // --fields/--columns select table columns; document JSON has no column-slicing
            // facility, so the combination is rejected rather than silently dropped. A scalar
            // payload projection (--value/--print) does compose, and is handled above.
            if (IsColumnProjectionRequested(options))
                return RejectColumnProjectionUnderJson(suggestPayloadProjection: true);
            WriteJsonTypeOutput(type, options);
            return 0;
        }

        var view = ApiOutputFormatter.BuildTypeView(type, foundIn, packageName, packageVersion, apiSource, selectedTfm, options);
        EventsView? eventsView = null;
        MethodGroupsView? methodGroupsView = null;
        MethodsView? methodsView = null;
        MemberIndexView? memberIndexView = null;
        OperatorsView? operatorsView = null;
        ExplicitInterfaceImplementationsView? explicitInterfaceImplementationsView = null;
        ExtensionMethodsView? extensionMethodsView = null;

        // Populate enum values declaratively (pipeline controls visibility via IncludeSections)
        if (type.Kind == "enum")
            ApiOutputFormatter.PopulateEnumValues(view, type, options);

        bool fullSerializer = options.Verbosity != Verbosity.Quiet;

        if (fullSerializer && view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (options is MemberOptions { OverloadIndex: not null })
            {
                ApiOutputFormatter.PopulateMemberSignature(view, type, options);
            }
            else if (options is MemberOptions { CtorOnly: true } && options.Verbosity >= Verbosity.Normal
                && type.Members.Any(m => m.Kind == "constructor"))
            {
                ApiOutputFormatter.PopulateConstructorOverloads(view, type, options);
            }
            else
            {
                var renderMemberGroups = ApiOutputFormatter.ShouldRenderMemberGroups(options);
                var renderMemberRows = ApiOutputFormatter.ShouldRenderMemberRows(options);
                var renderSupplementalRows = ApiOutputFormatter.ShouldRenderSupplementalMemberRows(options);
                if (renderMemberGroups)
                {
                    methodGroupsView ??= new MethodGroupsView();
                    eventsView ??= new EventsView();
                    ApiOutputFormatter.PopulateMemberSummarySections(
                        view, methodGroupsView, eventsView, type, options, methodGroupsOnly: renderMemberRows);
                }
                if (renderMemberRows || renderSupplementalRows)
                {
                    methodsView ??= new MethodsView();
                    operatorsView ??= new OperatorsView();
                    explicitInterfaceImplementationsView ??= new ExplicitInterfaceImplementationsView();
                    extensionMethodsView ??= new ExtensionMethodsView();
                    eventsView ??= new EventsView();
                    ApiOutputFormatter.PopulateMemberSections(
                        view,
                        methodsView,
                        operatorsView,
                        explicitInterfaceImplementationsView,
                        extensionMethodsView,
                        eventsView,
                        type,
                        options,
                        renderSupplementalRows ? ApiOutputFormatter.SupplementalMemberKinds : null);
                }
                if (ShouldRenderMemberIndex(options))
                {
                    memberIndexView ??= new MemberIndexView();
                    ApiOutputFormatter.PopulateMemberIndex(memberIndexView, type, options);
                }
            }

            if (ShouldRenderSourceLocations(options))
                ApiOutputFormatter.PopulateMemberSourceLocations(view, type, options);

            // --index: populate code sections and custom attributes
            // Can be called with a specific overload for all sections, or without an overload
            // for Callers-only mode (aggregates across all overloads).
            if (options is MemberOptions { DllPath: not null } mo4 
                && (mo4.OverloadIndex.HasValue || mo4.HasCallerScope))
            {
                var requestedSections = GetRequestedMemberSections(type, mo4);
                var methods = ApiOutputFormatter.ResolveBodyMethods(type, requestedSections);
                if (methods.Count > 0)
                {
                    var analysisInspection = new ApiMemberAnalysisInspection(
                        mo4.DllPath!, methods, requestedSections, mo4.CallerScopeAssemblies, mo4);
                    ApiOutputFormatter.PopulateIndexSections(view, type, methods, mo4.DllPath!,
                        mo4.OverloadIndex.HasValue ? mo4.OverloadIndex.Value - 1 : null,
                        requestedSections, analysisInspection, mo4.PdbPath, mo4.IncludeSections, mo4);
                }
            }

            // Type-scope analysis sections share one index build per type (built lazily, only
            // when such a section is requested) instead of opening one session per section.
            Analysis.LibraryBodyIndex? typeAnalysisIndex = null;
            Analysis.LibraryBodyIndex TypeAnalysisIndex() =>
                typeAnalysisIndex ??= ApiAnalysisInspection.OpenTypeAnalysisIndex(
                    options.DllPath!, GetRequestedMemberSections(type, options), type, options);

            if (options.DllPath is not null
                && GetRequestedMemberSections(type, options).Contains(SectionNames.UnsafeMembers))
            {
                ApiOutputFormatter.PopulateUnsafeMembers(view, type, TypeAnalysisIndex());
            }

            if (options.DllPath is { } exceptionRegionsDllPath
                && (GetRequestedMemberSections(type, options).Contains(SectionNames.ExceptionRegions)
                    || options.IncludeSections?.Contains(SectionNames.ExceptionRegions) == true))
            {
                var exceptionRegions = ApiAnalysisInspection.ResolveExceptionRegions(
                    exceptionRegionsDllPath,
                    type.Members.Where(member => member.MetadataToken is not null
                        && ApiMemberSectionDescriptors.IsMethodLike(member)));
                ApiOutputFormatter.PopulateTypeExceptionRegions(view, type, exceptionRegions, options.IncludeSections);
            }

            if (options.DllPath is not null
                && (GetRequestedMemberSections(type, options).Contains(SectionNames.CalledTypes)
                    || options.IncludeSections?.Contains(SectionNames.CalledTypes) == true))
            {
                ApiOutputFormatter.PopulateCalledTypes(view, type, TypeAnalysisIndex(), options.IncludeSections);
            }

            var semanticSections = GetRequestedMemberSections(type, options);
            if (options is not MemberOptions
                && options.DllPath is not null
                && semanticSections.Overlaps(SemanticFactSections))
            {
                ApiOutputFormatter.PopulateTypeSemanticFacts(view, type, TypeAnalysisIndex(), semanticSections, options.IncludeSections);
            }

            if (options.DllPath is not null
                && GetRequestedMemberSections(type, options).Contains(SectionNames.PerformanceTriage))
            {
                ApiOutputFormatter.PopulateOptimizationOpportunities(view, type, TypeAnalysisIndex(), options.IncludeSections,
                    options.PerformanceTriage,
                    restrictToModelMembers: ApiMemberSectionPipelines.UsesDetailPipeline(options)
                        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(options));
            }

            if (options.DllPath is not null
                && GetRequestedMemberSections(type, options).Contains(SectionNames.TopLeverage))
            {
                ApiOutputFormatter.PopulateTopLeverage(view, type, TypeAnalysisIndex(),
                    restrictToModelMembers: ApiMemberSectionPipelines.UsesDetailPipeline(options)
                        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(options));
            }

            // Source code (already resolved in command layer)
            if (options is MemberOptions mo5
                && GetRequestedMemberSections(type, mo5).Overlaps([SectionNames.OriginalSource, SectionNames.SourceDiff]))
            {
                if (mo5.MethodSource is { } resolvedSource)
                {
                    view.MemberCode ??= new MemberCodeView();
                    view.MemberCode.OriginalSourceCode = new Markout.CodeSection("csharp", resolvedSource.SourceCode);
                }
                else if (OriginalSourceUnavailableNote(mo5) is { } note)
                {
                    view.MemberCode ??= new MemberCodeView();
                    view.MemberCode.OriginalSourceCode = new Markout.CodeSection("csharp", note);
                    view.MemberCode.OriginalSourceUnavailable = true;
                }
            }

            PopulateSourceDiff(
                view,
                GetRequestedMemberSections(type, options),
                options is MemberOptions { MemberSourceTooComplex: true },
                options is MemberOptions { MemberSourceCoordinatesInvalid: true });

        }

        if (sourceDocumentJson)
        {
            if (view.MemberCode?.AnnotatedSourceDocument is not { } sourceDocument)
            {
                CommandError.Write(AnnotatedSourceDocumentError(view.MemberCode));
                return 1;
            }

            JsonOutputHelper.Write(
                sourceDocument,
                AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument,
                AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument,
                options.CompactJson);
            return 0;
        }

        // Whole-type decompilation (type command; member flows populate per
        // member above). Explicit-only: requires -S "Decompiled Source".
        // Sits OUTSIDE the member-sections region so enum types (which
        // populate EnumValues and skip that region) also compose.
        if (fullSerializer
            && options is not MemberOptions
            && options.DllPath is { } typeDllPath
            && options.IncludeSections is { Count: > 0 }
            && GetRequestedMemberSections(type, options).Contains(SectionNames.DecompiledSource))
        {
            // A whole-type decompiled-source render consumes the resolved config.
            var resolver = ApiAnalysisInspection.CreateReferenceResolver(typeDllPath, options);
            using var metadata = new Decompiler.Pipeline.MetadataContext(resolver);
            var listing = Decompiler.MemberBodyProducer.Project(
                type, typeDllPath, options.PdbPath, resolver, metadata, options.RenderOptions).Output;
            if (listing is not null)
            {
                // Surface pending config warnings only once the styled listing is
                // actually produced, so a type whose Project yields no body (e.g.
                // an enum) never emits a spurious warning.
                options.RenderConfigWarnings?.EmitOnce();
                view.MemberCode ??= new MemberCodeView();
                view.MemberCode.DecompiledSourceCode = new Markout.CodeSection("csharp", listing);
            }
        }

        if (options is TypeOptions
            && !options.Count
            && !IsProjectionRequested(options)
            && GetExactSelectedSection(
                options,
                ApiMemberSectionPipelines.Create(options).AllSectionNames) is { } exactSection)
        {
            var document = new TypeRenderDocument(
                view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode,
                ApiOutputFormatter.BuildTypeWriterOptions(type, options));
            if (!DocumentRendersSection(document, exactSection))
                return ReportEmptyExactSection(exactSection);
        }

        if (options.Print)
        {
            int result = await PrintApiProjectionAsync(view, options);
            ApiOutputFormatter.WriteCallGraphWarning(view);
            return result;
        }

        if (options.Value || options.Urls || options.Paths)
        {
            int result = WriteApiShapeProjection(view, options);
            ApiOutputFormatter.WriteCallGraphWarning(view);
            return result;
        }

        if (options.Count)
        {
            // A call graph declares edge rows in its projection. Count those rows directly
            // rather than scanning any rendered lowering, whose syntax cannot answer the
            // row question.
            if (options.IncludeSections is { Count: 1 } sections
                && sections.Contains(SectionNames.CallGraph)
                && view.MemberCode?.CallGraphRowCount is { } graphRows)
            {
                CountOutput.WriteCount(graphRows);
                ApiOutputFormatter.WriteCallGraphWarning(view);
                return 0;
            }

            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
            var sw = new StringWriter { NewLine = "\n" };
            var writer = new Markout.MarkoutWriter(sw, new MarkdownFormatter(), writerOptions);
            ApiOutputFormatter.SerializeTypeDocument(
                view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer);
            writer.Flush();
            CountOutput.WriteCountFromMarkdown(sw.ToString().TrimEnd());
            ApiOutputFormatter.WriteCallGraphWarning(view);
            return 0;
        }

        if (options is MemberOptions { Tree: true } or { MermaidOutput: true })
        {
            var graph = view.MemberCode?.CallGraph;
            if (graph is null)
            {
                CommandError.Write(
                    "Call Graph output requires exactly one selected method overload.",
                    "Select an overload by Name:N, Name~digest, or --index N.");
                return 1;
            }

            if (graph.IsEmpty)
            {
                sink.WriteLine("No inbound callers or outbound calls found for this method.");
            }
            else
            {
                IMarkoutFormatter formatter = options.Tree
                    ? new PlainTextFormatter()
                    : new MermaidFormatter();
                var graphWriter = new MarkoutWriter(sink, formatter);
                graphWriter.WriteGraph(graph);
                graphWriter.Flush();
            }

            ApiOutputFormatter.WriteCallGraphWarning(view);
            return 0;
        }

        // --bare: only the selected payload — no heading, fence, separator, or tips.
        if (options.Bare)
        {
            if (!TryGetBareApiPayload(view, options, out var raw, out var error))
            {
                CommandError.Write(error);
                return 1;
            }
            // The payload is decompiled source, IL, or an overlay — LF on every platform. Terminate
            // it with LF too so --bare stays byte-stable for machine consumers.
            OutputFormatter.WriteLfLine(sink, raw.TrimEnd());
            ApiOutputFormatter.WriteCallGraphWarning(view);
            return 0;
        }

        if (options.Tabular)
        {
            if (ApiOutputFormatter.ShouldRenderSectionedTabularView(type, options))
            {
                var writerOpts = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
                OutputFormatter.ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                OutputFormatter.WriteTable(sink, !options.NoHeader,
                    (writer, formatter) =>
                    {
                        var markoutWriter = new MarkoutWriter(writer, formatter, writerOpts);
                        ApiOutputFormatter.SerializeTypeDocument(
                            view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                            explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, markoutWriter);
                        markoutWriter.Flush();
                    }, options.Rows);
            }
            else
            {
                var (tableView, _) = ApiOutputFormatter.BuildTypeTableView(type, options);
                OutputFormatter.WriteProjectedTable(sink, !options.NoHeader, options.Tsv, options.Jsonl,
                    options.Columns, options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(tableView, writer, formatter, ApiViewContext.Default, writerOptions),
                    options.Rows);
            }
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            if (options.PlainText)
            {
                var writer = new Markout.MarkoutWriter(sink, options.CreateFormatter(), writerOptions);
                ApiOutputFormatter.SerializeTypeDocument(
                    view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                    explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer);
                writer.Flush();
            }
            else
            {
                if (SelectResolver.IsActiveAllSelector(options.Select, options.IncludeSections))
                {
                    var pipeline = ApiMemberSectionPipelines.Create(options);
                    writerOptions.SectionOrder = pipeline.GetAllSelectorSections(type);
                }
                else if (SelectResolver.IsActiveInfoSelector(options.SelectDefault, options.IncludeSections))
                {
                    var pipeline = ApiMemberSectionPipelines.Create(options);
                    writerOptions.SectionOrder = pipeline.InfoSectionNames;
                }

                writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
                var sw = new StringWriter { NewLine = "\n" };
                var writer = new Markout.MarkoutWriter(sw, options.CreateFormatter(), writerOptions);
                ApiOutputFormatter.SerializeTypeDocument(
                    view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                    explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer);
                writer.Flush();
                var markdown = sw.ToString().TrimEnd();
                OutputFormatter.WriteLfLine(sink, markdown);
            }
        }
        ApiOutputFormatter.WriteSignatureDecodeWarning(view);
        ApiOutputFormatter.WriteCallGraphWarning(view);
        return 0;
    }

    private static async Task<int> PrintApiProjectionAsync(TypeView view, ApiOptions options)
    {
        var section = options.IncludeSections!.Single();
        if (section.Equals(SectionNames.SourceFiles, StringComparison.OrdinalIgnoreCase))
        {
            return await PrintUrlProjectionAsync(
                section,
                view.SourceFileRows?.Select((row, index) => (
                    Row: index + 1,
                    Label: (string?)row.Url,
                    Url: (string?)row.Url,
                    row.Checksum,
                    row.ChecksumAlgorithm)),
                options);
        }

        if (section.Equals(SectionNames.SourceLocations, StringComparison.OrdinalIgnoreCase))
        {
            return await PrintUrlProjectionAsync(
                section,
                view.SourceLocationRows?.Select((row, index) => (
                    Row: index + 1,
                    Label: (string?)row.File ?? row.Url,
                    Url: row.Url,
                    row.Checksum,
                    row.ChecksumAlgorithm)),
                options);
        }

        var documents = section switch
        {
            SectionNames.OriginalSource => CodeSectionDocument(section, "Original Source", (options as MemberOptions)?.MethodSource?.SourceUrl, view.MemberCode?.OriginalSourceCode.Content),
            SectionNames.DecompiledSource => CodeSectionDocument(section, "Decompiled Source", null, view.MemberCode?.DecompiledSourceCode.Content),
            SectionNames.AnnotatedSource => CodeSectionDocument(section, "Annotated Source", null, view.MemberCode?.AnnotatedSourceCode.Content),
            SectionNames.SourceDiff => CodeSectionDocument(section, "Source Diff", null, view.MemberCode?.SourceDiffCode.Content),
            SectionNames.IL => CodeSectionDocument(section, "IL", null, view.MemberCode?.ILCode.Content),
            _ => []
        };

        if (documents.Count == 0
            && section is not (SectionNames.SourceFiles or SectionNames.SourceLocations or SectionNames.OriginalSource
                or SectionNames.DecompiledSource or SectionNames.AnnotatedSource or SectionNames.SourceDiff or SectionNames.IL))
        {
            CommandError.Write($"section '{section}' is not printable.");
            return 1;
        }

        return PrintProjectionOutput.Write(
            documents,
            new PrintProjectionOptions(
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                OutputPath: null));
    }

    private static int WriteApiShapeProjection(TypeView view, ApiOptions options)
    {
        var kind = ShapeProjectionOutput.GetKind(options.Value, options.Urls, options.Paths);
        var section = options.IncludeSections!.Single();
        var rows = section switch
        {
            SectionNames.SourceFiles => ProjectTypeSourceFiles(view, section, kind),
            SectionNames.SourceLocations => ProjectSourceLocations(view, section, kind, options),
            _ => []
        };

        if (rows.Count == 0
            && section is not (SectionNames.SourceFiles or SectionNames.SourceLocations))
        {
            CommandError.Write($"section '{section}' does not expose {kind.ToString().ToLowerInvariant()} values.");
            return 1;
        }

        return ShapeProjectionOutput.Write(
            rows,
            new ShapeProjectionOptions(kind, options.PrintRow, options.JsonOutput, options.Jsonl, options.JsonArray));
    }

    private static List<ShapeProjectionRow> ProjectTypeSourceFiles(TypeView view, string section, ShapeProjectionKind kind)
    {
        // Number by position in the rendered section, then drop valueless rows.
        // Renumbering after the filter would relabel the survivors 1..N and break
        // the correspondence with the table the reader is looking at.
        return (view.SourceFileRows ?? [])
            .Select((row, index) => (Number: index + 1, row.Url))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Url))
            .Select(entry =>
            {
                var value = entry.Url!;
                return kind switch
                {
                    ShapeProjectionKind.Urls or ShapeProjectionKind.Value =>
                        new ShapeProjectionRow(entry.Number, section, value, Url: value),
                    _ => null
                };
            })
            .Where(row => row is not null)
            .Cast<ShapeProjectionRow>()
            .ToList();
    }

    private static List<ShapeProjectionRow> ProjectSourceLocations(TypeView view, string section, ShapeProjectionKind kind, ApiOptions options)
    {
        List<ShapeProjectionRow> rows = [];
        var sourceRows = view.SourceLocationRows ?? [];
        for (var i = 0; i < sourceRows.Count; i++)
        {
            var row = sourceRows[i];
            string? value = kind switch
            {
                ShapeProjectionKind.Urls => row.Url,
                ShapeProjectionKind.Paths => Uncode(row.File),
                ShapeProjectionKind.Value => SelectSourceLocationValue(row, options),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value))
                continue;
            rows.Add(new ShapeProjectionRow(
                i + 1,
                section,
                value,
                Label: Uncode(row.Selector),
                Url: kind == ShapeProjectionKind.Urls ? value : row.Url,
                Path: kind == ShapeProjectionKind.Paths ? value : Uncode(row.File)));
        }

        return rows;
    }

    private static string? SelectSourceLocationValue(MemberSourceLocationRow row, ApiOptions options)
    {
        var column = options.Columns?.SingleOrDefault() ?? options.Fields?.SingleOrDefault();
        return column?.ToLowerInvariant() switch
        {
            "selector" => Uncode(row.Selector),
            "signature" => Uncode(row.Signature),
            "file" or "path" => Uncode(row.File),
            "line" => row.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "end line" or "end_line" => row.EndLine?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "url" => row.Url,
            _ => row.Url
        };
    }

    private static string? Uncode(string? value)
    {
        if (value is null)
            return null;
        if (value is { Length: > 1 } && value[0] == '`' && value[^1] == '`')
            return WebUtility.HtmlDecode(value[1..^1]);
        const string open = "<code>";
        const string close = "</code>";
        return value.StartsWith(open, StringComparison.OrdinalIgnoreCase)
            && value.EndsWith(close, StringComparison.OrdinalIgnoreCase)
                ? WebUtility.HtmlDecode(value[open.Length..^close.Length])
                : WebUtility.HtmlDecode(value);
    }

    private static List<PrintableDocument> CodeSectionDocument(string section, string label, string? url, string? content)
        => string.IsNullOrEmpty(content)
            ? []
            : [new PrintableDocument(1, section, label, null, url, content)];

    /// <summary>
    /// Selects the row addressed by <paramref name="selector"/> from the rows a
    /// section rendered, or explains why no row could be selected.
    ///
    /// Numbering covers every rendered row. Filtering to rows that carry a
    /// payload and then indexing that shorter list positionally is how --row came
    /// to address a sequence the reader cannot count: with rows 1-2 carrying no
    /// URL, --row 1 returned the row displayed third. Printability is therefore
    /// checked last, against the row the caller actually named, so a row with
    /// nothing behind it reports that rather than yielding its neighbour.
    /// </summary>
    internal static (int Row, string? Label, string? Url)? SelectPrintableRow(
        IReadOnlyList<(int Row, string? Label, string? Url)> rows,
        RowSelector? selector,
        out string error)
    {
        error = "";
        if (rows.Count == 0)
        {
            error = "selected section has no rows.";
            return null;
        }

        if (selector is null && rows.Count != 1)
        {
            error = $"selected section has {rows.Count} rows; use --row N|first|last to choose one row.";
            return null;
        }

        var rowNumbers = rows.Select(row => row.Row).ToList();
        var targetRow = selector?.Resolve(rowNumbers) ?? rowNumbers[0];
        var position = RowNumbering.IndexOf(rowNumbers, targetRow);
        if (position < 0)
        {
            error = $"row {targetRow} is not in this section. Use --row {RowNumbering.Describe(rowNumbers)}, first, or last.";
            return null;
        }

        var selected = rows[position];
        if (string.IsNullOrWhiteSpace(selected.Url))
        {
            error = $"row {targetRow} has no printable document.";
            return null;
        }

        return selected;
    }

    private static async Task<int> PrintUrlProjectionAsync(
        string section,
        IEnumerable<(
            int Row,
            string? Label,
            string? Url,
            byte[]? Checksum,
            string? ChecksumAlgorithm)>? rows,
        ApiOptions options)
    {
        var materialized = (rows ?? []).ToList();
        var selection = SelectPrintableRow(
            materialized.Select(row => (row.Row, row.Label, row.Url)).ToList(),
            options.PrintRow,
            out var selectionError);
        if (selection is not { } selectedRow)
        {
            CommandError.Write(selectionError);
            return 1;
        }

        var rawUrl = GitHubUrlResolver.ConvertBlobToRawUrl(selectedRow.Url!);
        var selectedSource = materialized.Single(row => row.Row == selectedRow.Row);
        var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
        var fetch = await AuthoredSourceAcquisition.FetchVerifiedSourceTextAsync(
            fetcher,
            rawUrl,
            selectedSource.ChecksumAlgorithm,
            selectedSource.Checksum);
        if (fetch.Text is null)
        {
            CommandError.Write(
                $"failed to fetch verified source for row {selectedRow.Row}: "
                + (fetch.Failure ?? "source is unavailable."));
            return 1;
        }

        var document = new PrintableDocument(
            selectedRow.Row,
            section,
            string.IsNullOrWhiteSpace(selectedRow.Label) ? rawUrl : selectedRow.Label!,
            null,
            rawUrl,
            fetch.Text);

        return PrintProjectionOutput.Write(
            [document],
            new PrintProjectionOptions(
                Row: null,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                OutputPath: null));
    }

    private static bool TryGetBareApiPayload(TypeView view, ApiOptions options, out string raw, out string error)
    {
        raw = "";
        error = "";

        if (options.IncludeSections is not { Count: 1 } included)
        {
            error = "--bare requires exactly one -S section.";
            return false;
        }

        var section = included.First();
        raw = section switch
        {
            SectionNames.DecompiledSource => view.MemberCode?.DecompiledSourceCode.Content ?? "",
            SectionNames.AnnotatedSource => view.MemberCode?.AnnotatedSourceCode.Content ?? "",
            SectionNames.CostOverlay => view.MemberCode?.CostOverlayCode.Content ?? "",
            SectionNames.SemanticsOverlay => view.MemberCode?.SemanticsOverlayCode.Content ?? "",
            SectionNames.OriginalSource => view.MemberCode?.OriginalSourceCode.Content ?? "",
            SectionNames.SourceDiff => view.MemberCode?.SourceDiffCode.Content ?? "",
            SectionNames.IL => view.MemberCode?.ILCode.Content ?? "",
            SectionNames.SourceFiles => BareUrlColumn(view.SourceFileRows?.Select(row => row.Url), SectionNames.SourceFiles, out error),
            SectionNames.SourceLocations => BareUrlColumn(view.SourceLocationRows?.Select(row => row.Url), SectionNames.SourceLocations, out error),
            _ => ""
        };

        if (raw.Length > 0)
            return true;

        if (error.Length == 0)
            error = "--bare requires a single selected payload with content.";
        return false;
    }

    private static string BareUrlColumn(IEnumerable<string?>? urls, string section, out string error)
    {
        error = "";
        var values = urls?
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .ToList() ?? [];

        if (values.Count > 0)
            return string.Join('\n', values);

        error = $"--bare found no URL in section '{section}'.";
        return "";
    }

    /// <summary>
    /// Restricts a plain-discovery schema to the columns queryable under the active options.
    /// The view schema is a union of all rendering variants, so it advertises columns that only
    /// specific historical variants surface. Deprecated columns are hidden from discovery to keep
    /// what is listed consistent with what the user can actually project. This is the
    /// option/contract level gate (data-independent); the data-level gate is effective discovery.
    /// </summary>
    /// <remarks>
    /// <c>Select</c> was the old overload-index column. Member selectors now live in the
    /// dedicated <c>Member Index</c> section, so the historical column is not queryable.
    /// </remarks>
    internal static DocumentSchema ToQueryableSchema(DocumentSchema schema, ApiOptions options)
    {
        return DiscoverOutput.WithoutColumn(schema, "Select");
    }

    /// <summary>
    /// Executes effective discovery (<c>-D</c>) for a single type. Shared by the
    /// type and member commands so both paths apply identical queryability filtering:
    /// <list type="bullet">
    /// <item>Section gate: <see cref="DiscoverOutput.RestrictToSchemaSections"/> drops pipeline
    /// sections absent from the active schema, then <see cref="DiscoverOutput.RestrictToRenderedSections"/> drops schema sections that
    /// render no data for this type (e.g. Custom Attributes when the type has no attributes),
    /// so every listed section is queryable via <c>-D &lt;Section&gt;</c> and actually has data.</item>
    /// <item>Column gate: <see cref="DiscoverOutput.FilterSchemaToRenderedColumns"/> renders the
    /// type at the active options and keeps only columns that appear, dropping columns the
    /// active options never surface and columns with no data
    /// (e.g. Obsolete when no member is obsolete). Sections in
    /// <see cref="TypeFieldLayoutSections"/> are matched on rendered field rows instead, because
    /// their table columns are literally "Field" and "Value".</item>
    /// </list>
    /// This keeps effective discovery consistent with what the user can actually query and see.
    /// </summary>
    /// <summary>
    /// Type-view sections rendered as a <c>Field</c>/<c>Value</c> fact table rather than one
    /// column per schema item. Effective discovery must match these on rendered field rows, not
    /// on table columns. Mirrors the equivalent set in <c>LibraryCommand</c>.
    /// </summary>
    private static readonly HashSet<string> TypeFieldLayoutSections =
        new(StringComparer.OrdinalIgnoreCase) { SectionNames.TypeInfo };

    /// <summary>
    /// Where a type was acquired from. Not derivable from <see cref="ApiType"/>, so it has to be
    /// carried in from the command that resolved it. Effective discovery needs it because
    /// <c>Type Info</c> reports these as identity facts; without it the render manifest cannot
    /// observe them and <c>-D</c> under-reports fields that <c>-S</c> visibly renders.
    /// </summary>
    internal sealed record TypeAcquisitionContext(
        string? FoundIn,
        string? PackageName,
        string? PackageVersion,
        string? ApiSource,
        string? SelectedTfm);

    internal static int ExecuteEffectiveDiscovery(
        ApiType apiType, SectionPipeline<ApiType> memberPipeline, ApiOptions options,
        TypeAcquisitionContext? acquisition = null)
    {
        var fullSchema = RestrictSchemaToSections(
            GetTypeDocumentSchema(options),
            memberPipeline.AllSectionNames);
        var filteredType = BuildFilteredTypeForSections(apiType, options);
        var effective = memberPipeline.GetDiscoverableSections(filteredType, options.IncludeSections);
        effective = DiscoverOutput.RestrictToSchemaSections(effective, fullSchema);
        var unprobed = memberPipeline.GetUnprobedSections();
        var bareDiscover = options.Discover is null or { Length: 0 };
        var discoveryRenderSections = bareDiscover
            ? options is MemberOptions { OverloadIndex: not null }
                ? [.. effective.Where(s => !unprobed.Contains(s))]
                : [.. effective.Where(memberPipeline.GetCostAnnotations().ContainsKey)]
            : (IReadOnlyCollection<string>?)null;
        var renderManifest = BuildTypeRenderManifest(filteredType, options, discoveryRenderSections, acquisition);
        // Unprobed sections may render empty and must be opt-in by policy, so the
        // normal opt-in annotation is sufficient and avoids double labels.
        var displayAnnotations = memberPipeline.GetCostAnnotations();
        var queryEffective = effective;
        var specificSectionDiscover = options.Discover is { Length: > 0 }
            && options.Discover.Any(name => !name.StartsWith("@", StringComparison.Ordinal));
        if (specificSectionDiscover)
        {
            var renderedKept = DiscoverOutput.RestrictToRenderedSections(effective, fullSchema, renderManifest);
            var keep = new HashSet<string>(renderedKept, StringComparer.OrdinalIgnoreCase);
            foreach (var section in effective)
            {
                if (unprobed.Contains(section)
                    || displayAnnotations.TryGetValue(section, out var annotation)
                       && annotation.Equals(SectionAnnotations.OptIn, StringComparison.OrdinalIgnoreCase))
                    keep.Add(section);
            }
            queryEffective = effective.Where(keep.Contains).ToList();
        }
        var schema = DiscoverOutput.FilterSchemaToRenderedColumns(
            queryEffective, fullSchema, renderManifest, TypeFieldLayoutSections);
        return DiscoverOutput.ExecuteEffective(options.Discover, queryEffective, schema,
            tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl, markdown: !options.Tabular && !options.JsonOutput,
            verbosity: (int)options.Verbosity, fullSchema: fullSchema,
            sectionCostAnnotations: displayAnnotations,
            sectionCategories: memberPipeline.GetCategoryMap(),
            catalogHiddenSections: memberPipeline.IsCuratedCatalog
                ? memberPipeline.GetCatalogHiddenSections()
                : null,
            listedCategoryDoors: memberPipeline.IsCuratedCatalog
                ? memberPipeline.GetListedCategoryDoors()
                : null,
            projection: options);
    }

    /// <summary>
    /// Renders the type's member/enum sections to Markdown.
    /// </summary>
    internal static string RenderTypeSectionsMarkdown(ApiType type, ApiOptions options, IReadOnlyCollection<string>? discoverySections = null)
    {
        var documents = BuildTypeRenderDocuments(type, options, discoverySections);
        var sw = new StringWriter { NewLine = "\n" };
        for (int i = 0; i < documents.Count; i++)
        {
            if (i > 0)
                sw.WriteLine();

            var writer = new MarkoutWriter(sw, new Markout.MarkdownFormatter(), documents[i].WriterOptions);
            documents[i].Serialize(writer);
            writer.Flush();
        }

        return sw.ToString();
    }

    /// <summary>
    /// Captures the sections and table columns emitted by the same type-document
    /// serializer used for normal output. Effective discovery consumes this typed
    /// manifest instead of recovering structure from rendered Markdown.
    /// </summary>
    internal static RenderedSectionManifest BuildTypeRenderManifest(
        ApiType type,
        ApiOptions options,
        IReadOnlyCollection<string>? discoverySections = null,
        TypeAcquisitionContext? acquisition = null)
    {
        var formatter = new RenderManifestFormatter(GetTypeDocumentSchema(options));
        foreach (var document in BuildTypeRenderDocuments(type, options, discoverySections, acquisition))
        {
            formatter.BeginDocument(document.WriterOptions);
            var writer = new MarkoutWriter(TextWriter.Null, formatter, document.WriterOptions);
            document.Serialize(writer);
            writer.Flush();
        }

        return formatter.Manifest;
    }

    private static IReadOnlyList<TypeRenderDocument> BuildTypeRenderDocuments(
        ApiType type,
        ApiOptions options,
        IReadOnlyCollection<string>? discoverySections,
        TypeAcquisitionContext? acquisition = null)
    {
        if (discoverySections is not { Count: > 0 })
            return [BuildTypeRenderDocument(type, options, acquisition)];

        return
        [
            BuildTypeRenderDocument(type, options with { Discover = null }, acquisition),
            BuildTypeRenderDocument(type, options with
            {
                Discover = null,
                IncludeSections = new HashSet<string>(discoverySections, StringComparer.OrdinalIgnoreCase),
            }, acquisition)
        ];
    }

    private static TypeRenderDocument BuildTypeRenderDocument(
        ApiType type, ApiOptions options, TypeAcquisitionContext? acquisition = null)
    {
        var renderOptions = options with
        {
            Columns = null,
            Fields = null,
            PlainText = false,
            JsonOutput = false,
            Tabular = false,
        };
        if (options.Discover is { Length: > 0 } discover)
        {
            var pipeline = ApiMemberSectionPipelines.Create(options);
            var resolved = SelectResolver.ResolveSelectAsSections(
                discover, pipeline.SelectableSectionNames, pipeline.InfoSectionNames, pipeline.GetCategoryMap());
            if (!resolved.HasError && resolved.Sections is { Count: > 0 })
            {
                var include = renderOptions.IncludeSections is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(renderOptions.IncludeSections, StringComparer.OrdinalIgnoreCase);
                include.UnionWith(resolved.Sections);
                renderOptions = renderOptions with { IncludeSections = include };
            }
        }

        var view = ApiOutputFormatter.BuildTypeView(
            type, acquisition?.FoundIn, acquisition?.PackageName, acquisition?.PackageVersion,
            acquisition?.ApiSource, acquisition?.SelectedTfm, renderOptions);
        EventsView? eventsView = null;
        MethodGroupsView? methodGroupsView = null;
        MethodsView? methodsView = null;
        MemberIndexView? memberIndexView = null;
        OperatorsView? operatorsView = null;
        ExplicitInterfaceImplementationsView? explicitInterfaceImplementationsView = null;
        ExtensionMethodsView? extensionMethodsView = null;

        if (type.Kind == "enum")
            ApiOutputFormatter.PopulateEnumValues(view, type, renderOptions);

        if (view.EnumValues == null && view.EnumValuesWithDocs == null)
        {
            if (renderOptions is MemberOptions { OverloadIndex: not null })
                ApiOutputFormatter.PopulateMemberSignature(view, type, renderOptions);
            else
            {
                var renderMemberGroups = ApiOutputFormatter.ShouldRenderMemberGroups(renderOptions);
                var renderMemberRows = ApiOutputFormatter.ShouldRenderMemberRows(renderOptions);
                var renderSupplementalRows = ApiOutputFormatter.ShouldRenderSupplementalMemberRows(renderOptions);
                if (renderMemberGroups)
                {
                    methodGroupsView ??= new MethodGroupsView();
                    eventsView ??= new EventsView();
                    ApiOutputFormatter.PopulateMemberSummarySections(
                        view, methodGroupsView, eventsView, type, renderOptions, methodGroupsOnly: renderMemberRows);
                }
                if (renderMemberRows || renderSupplementalRows)
                {
                    methodsView ??= new MethodsView();
                    operatorsView ??= new OperatorsView();
                    explicitInterfaceImplementationsView ??= new ExplicitInterfaceImplementationsView();
                    extensionMethodsView ??= new ExtensionMethodsView();
                    eventsView ??= new EventsView();
                    ApiOutputFormatter.PopulateMemberSections(
                        view,
                        methodsView,
                        operatorsView,
                        explicitInterfaceImplementationsView,
                        extensionMethodsView,
                        eventsView,
                        type,
                        renderOptions,
                        renderSupplementalRows ? ApiOutputFormatter.SupplementalMemberKinds : null);
                }
                if (ShouldRenderMemberIndex(renderOptions))
                {
                    memberIndexView ??= new MemberIndexView();
                    ApiOutputFormatter.PopulateMemberIndex(memberIndexView, type, renderOptions);
                }
            }

            if (ShouldRenderSourceLocations(renderOptions))
                ApiOutputFormatter.PopulateMemberSourceLocations(view, type, renderOptions);

            if (renderOptions is MemberOptions { DllPath: not null } memberOptions
                && (memberOptions.OverloadIndex.HasValue || memberOptions.HasCallerScope))
            {
                var requestedSections = GetRequestedMemberSections(type, memberOptions);
                var methods = ApiOutputFormatter.ResolveBodyMethods(type, requestedSections);
                if (methods.Count > 0)
                {
                    var analysisInspection = new ApiMemberAnalysisInspection(
                        memberOptions.DllPath!, methods, requestedSections,
                        memberOptions.CallerScopeAssemblies, memberOptions);
                    ApiOutputFormatter.PopulateIndexSections(view, type, methods,
                        memberOptions.DllPath!,
                        memberOptions.OverloadIndex.HasValue ? memberOptions.OverloadIndex.Value - 1 : null,
                        requestedSections, analysisInspection, memberOptions.PdbPath,
                        memberOptions.IncludeSections, memberOptions);
                }

                if (requestedSections.Overlaps([SectionNames.OriginalSource, SectionNames.SourceDiff]))
                {
                    if (memberOptions.MethodSource is { } resolvedSource)
                    {
                        view.MemberCode ??= new MemberCodeView();
                        view.MemberCode.OriginalSourceCode = new Markout.CodeSection("csharp", resolvedSource.SourceCode);
                    }
                    else if (OriginalSourceUnavailableNote(memberOptions) is { } note)
                    {
                        view.MemberCode ??= new MemberCodeView();
                        view.MemberCode.OriginalSourceCode = new Markout.CodeSection("csharp", note);
                        view.MemberCode.OriginalSourceUnavailable = true;
                    }
                }
                PopulateSourceDiff(
                    view,
                    requestedSections,
                    memberOptions.MemberSourceTooComplex,
                    memberOptions.MemberSourceCoordinatesInvalid);
            }

            Analysis.LibraryBodyIndex? typeAnalysisIndex = null;
            Analysis.LibraryBodyIndex TypeAnalysisIndex() =>
                typeAnalysisIndex ??= ApiAnalysisInspection.OpenTypeAnalysisIndex(
                    renderOptions.DllPath!, GetRequestedMemberSections(type, renderOptions), type, renderOptions);

            if (renderOptions.DllPath is not null
                && GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.UnsafeMembers))
            {
                ApiOutputFormatter.PopulateUnsafeMembers(view, type, TypeAnalysisIndex());
            }

            if (renderOptions.DllPath is { } exceptionRegionsDllPath
                && (GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.ExceptionRegions)
                    || renderOptions.IncludeSections?.Contains(SectionNames.ExceptionRegions) == true))
            {
                var exceptionRegions = ApiAnalysisInspection.ResolveExceptionRegions(
                    exceptionRegionsDllPath,
                    type.Members.Where(member => member.MetadataToken is not null
                        && ApiMemberSectionDescriptors.IsMethodLike(member)));
                ApiOutputFormatter.PopulateTypeExceptionRegions(
                    view, type, exceptionRegions, renderOptions.IncludeSections);
            }

            if (renderOptions.DllPath is not null
                && (GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.CalledTypes)
                    || renderOptions.IncludeSections?.Contains(SectionNames.CalledTypes) == true))
            {
                ApiOutputFormatter.PopulateCalledTypes(view, type, TypeAnalysisIndex(), renderOptions.IncludeSections);
            }

            var semanticSections = GetRequestedMemberSections(type, renderOptions);
            if (renderOptions is not MemberOptions
                && renderOptions.DllPath is not null
                && semanticSections.Overlaps(SemanticFactSections))
            {
                ApiOutputFormatter.PopulateTypeSemanticFacts(view, type, TypeAnalysisIndex(), semanticSections, renderOptions.IncludeSections);
            }

            if (renderOptions.DllPath is not null
                && GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.PerformanceTriage))
            {
                ApiOutputFormatter.PopulateOptimizationOpportunities(view, type, TypeAnalysisIndex(), renderOptions.IncludeSections,
                    restrictToModelMembers: ApiMemberSectionPipelines.UsesDetailPipeline(renderOptions)
                        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(renderOptions));
            }

            if (renderOptions.DllPath is not null
                && GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.TopLeverage))
            {
                ApiOutputFormatter.PopulateTopLeverage(view, type, TypeAnalysisIndex(),
                    restrictToModelMembers: ApiMemberSectionPipelines.UsesDetailPipeline(renderOptions)
                        || ApiMemberSectionPipelines.UsesOverloadInventoryPipeline(renderOptions));
            }
        }

        return new TypeRenderDocument(
            view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
            explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode,
            ApiOutputFormatter.BuildTypeWriterOptions(type, renderOptions));
    }

    private sealed record TypeRenderDocument(
        TypeView View,
        EventsView? Events,
        MethodGroupsView? MethodGroups,
        MethodsView? Methods,
        MemberIndexView? MemberIndex,
        OperatorsView? Operators,
        ExplicitInterfaceImplementationsView? ExplicitInterfaceImplementations,
        ExtensionMethodsView? ExtensionMethods,
        MemberCodeView? MemberCode,
        MarkoutWriterOptions WriterOptions)
    {
        internal void Serialize(MarkoutWriter writer)
            => ApiOutputFormatter.SerializeTypeDocument(
                View,
                Events,
                MethodGroups,
                Methods,
                MemberIndex,
                Operators,
                ExplicitInterfaceImplementations,
                ExtensionMethods,
                MemberCode,
                writer);
    }

    private static bool DocumentRendersSection(
        TypeRenderDocument document,
        string section)
    {
        var output = new StringWriter { NewLine = "\n" };
        var writer = new MarkoutWriter(
            output,
            new MarkdownFormatter(),
            document.WriterOptions);
        document.Serialize(writer);
        writer.Flush();
        string heading = $"## {section}";
        return output.ToString()
            .Split('\n')
            .Any(line => line.TrimEnd('\r').Equals(
                heading,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Stands in for Original Source when the selected member carries no IL body. A C# comment
    /// so it reads naturally inside the section's <c>csharp</c> fence, mirroring how
    /// <see cref="SourceTextDiffRenderer"/> reports an unavailable diff input (issue #3299).
    /// </summary>
    internal const string BodylessMemberNote =
        "// This member has no IL body, so it has no authored source to show.";

    /// <summary>
    /// Stands in for Original Source when the selected member has an IL body but its source range
    /// does not identify one authored declaration that can be shown. Generated members may map to
    /// a type header or initializer, and structurally unknown ranges are deliberately not guessed;
    /// saying so beats rendering unrelated or truncated source (issue #3299's principle, applied
    /// to a second cause).
    /// </summary>
    internal const string NoAuthoredDeclarationNote =
        "// This member's source range does not identify one authored declaration that can be shown.\n"
        + "// Generated members and ambiguous or structurally unknown source ranges can have this shape.";

    internal const string SourceTooComplexNote =
        "// Authored source extraction stopped because the source exceeds the lexical complexity limit.";

    internal const string SourceCoordinatesInvalidNote =
        "// Authored source extraction stopped because the portable-PDB sequence-point coordinates "
        + "cannot address the verified source.";

    internal static string? OriginalSourceUnavailableNote(MemberOptions options) =>
        options.MemberHasNoBody
            ? BodylessMemberNote
            : options.MemberSourceTooComplex
                ? SourceTooComplexNote
                : options.MemberSourceCoordinatesInvalid
                    ? SourceCoordinatesInvalidNote
                    : options.MemberHasNoAuthoredDeclaration
                        ? NoAuthoredDeclarationNote
                        : null;

    private static void PopulateSourceDiff(
        TypeView view,
        IReadOnlySet<string> requestedSections,
        bool sourceTooComplex,
        bool sourceCoordinatesInvalid)
    {
        if (!requestedSections.Contains(SectionNames.SourceDiff))
            return;

        view.MemberCode ??= new MemberCodeView();
        if (sourceTooComplex)
        {
            view.MemberCode.SourceDiffCode = new Markout.CodeSection(
                "diff",
                "# Original Source unavailable because authored source extraction exceeded "
                + "the lexical complexity limit.");
            return;
        }
        if (sourceCoordinatesInvalid)
        {
            view.MemberCode.SourceDiffCode = new Markout.CodeSection(
                "diff",
                "# Original Source unavailable because portable-PDB sequence-point coordinates "
                + "cannot address the verified source.");
            return;
        }

        view.MemberCode.SourceDiffCode = new Markout.CodeSection(
            "diff",
            SourceTextDiffRenderer.CreateUnifiedDiff(
                // The bodyless note is an explanation, not source text: leave the diff's
                // "before" side unavailable so it reports that rather than diffing the note.
                view.MemberCode.OriginalSourceUnavailable ? null : view.MemberCode.OriginalSourceCode.Content,
                view.MemberCode.DecompiledSourceCode.Content,
                "Original Source",
                "Decompiled Source"));
    }

    private static void WriteJsonTypeOutput(ApiType type, ApiOptions options)
    {
        var outputType = type;
        var (members, membersChanged) = GetJsonOutputMembers(type, options);

        // -S/--select scopes JSON to the requested sections, mirroring the markdown view.
        if (options.IncludeSections is { Count: > 0 } sections)
        {
            outputType = ProjectTypeToSections(type, members, sections);
        }

        else if (membersChanged)
            outputType = CopyTypeWithMembers(type, members);

        // Project the durable identity (Digest + Canonical Signature) onto each member so
        // JSON consumers get the same overload handle the Markdown Digest column exposes.
        // Computed against the resolved declaring type, matching the table's anchor.
        foreach (var member in outputType.Members)
        {
            var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
            member.Digest = anchor.Fingerprint;
            member.CanonicalSignature = anchor.CanonicalSignature;
        }

        if (options.CompactJson)
            Console.WriteLine(JsonSerializer.Serialize(outputType, ApiTypeCompactJsonContext.Default.ApiType));
        else
            Console.WriteLine(JsonSerializer.Serialize(outputType, ApiTypeJsonContext.Default.ApiType));
    }

    private static ApiType BuildTypeForJsonOutput(ApiType type, ApiOptions options)
    {
        var (members, membersChanged) = GetJsonOutputMembers(type, options);
        return membersChanged ? CopyTypeWithMembers(type, members) : type;
    }

    private static (List<ApiMember> Members, bool Changed) GetJsonOutputMembers(
        ApiType type,
        ApiOptions options)
    {
        IEnumerable<ApiMember> members = type.Members;
        bool changed = false;

        if (options.MemberFilter.Count > 0)
        {
            members = members.Where(
                member => TypeMatcher.MatchesMemberFilter(member.Name, options.MemberFilter));
            changed = true;
        }

        if (options.KindFilter.Count > 0)
        {
            members = members.Where(member => options.KindFilter.Contains(member.Kind));
            changed = true;
        }

        if (options.UnsafeOnly)
        {
            members = members.Where(member => member.IsUnsafe);
            changed = true;
        }

        var filtered = changed ? members.ToList() : type.Members;
        if (options.Limit.HasValue && filtered.Count > options.Limit.Value)
        {
            filtered = OrderMembersForLimit(filtered)
                .Take(options.Limit.Value)
                .ToList();
            changed = true;
        }

        return (filtered, changed);
    }

    private static bool IsAnnotatedSourceDocumentJson(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && options.IncludeSections is { Count: 1 } sections
           && sections.Contains(SectionNames.AnnotatedSourceDocument)
           && HasOnlyExplicitAnnotatedSourceDocumentSelectors(options);

    private static bool IsInvalidAnnotatedSourceDocumentJsonSelection(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && options.IncludeSections is { Count: > 0 } sections
           && sections.Contains(SectionNames.AnnotatedSourceDocument)
           && options.Select?.Any(IsExplicitAnnotatedSourceDocumentSelector) == true
           && (sections.Count != 1
               || !HasOnlyExplicitAnnotatedSourceDocumentSelectors(options));

    private static bool HasOnlyExplicitAnnotatedSourceDocumentSelectors(ApiOptions options)
        => options.Select is { Length: > 0 } selectors
           && selectors.All(IsExplicitAnnotatedSourceDocumentSelector);

    private static bool IsExplicitAnnotatedSourceDocumentSelector(string selector)
        => selector.Equals(
            SectionNames.AnnotatedSourceDocument,
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRenderMemberIndex(ApiOptions options)
        => options.IncludeSections?.Contains(SectionNames.MemberIndex) == true;

    private static bool ShouldRenderSourceLocations(ApiOptions options)
        => options.IncludeSections?.Contains(SectionNames.SourceLocations) == true;

    internal static string AnnotatedSourceDocumentError(MemberCodeView? memberCode)
        => memberCode?.AnnotatedSourceDocumentFailure is { } failure
            ? string.Join(
                "; ",
                failure.Diagnostics.Select(diagnostic => diagnostic.ToString()))
            : $"section '{SectionNames.AnnotatedSourceDocument}' produced no payload.";

    private static readonly HashSet<string> SemanticFactSections = new(StringComparer.OrdinalIgnoreCase)
    {
        SectionNames.AllocationFacts,
        SectionNames.SafetyFacts,
        SectionNames.CostFacts
    };

    /// <summary>
    /// Maps each member section name to the predicate that selects its members.
    /// </summary>
    private static readonly Dictionary<string, Func<ApiMember, bool>> MemberSectionPredicates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.Values] = m => m.Kind == "field" && m.EnumValue.HasValue,
            [SectionNames.Fields] = m => m.Kind == "field" && !m.EnumValue.HasValue,
            [SectionNames.Properties] = m => m.Kind == "property",
            [SectionNames.MethodGroups] = m => m.Kind == "method",
            [SectionNames.Methods] = m => m.Kind == "method",
            [SectionNames.MemberIndex] = m => !MemberFilters.IsCompilerGenerated(m.Name),
            [SectionNames.Operators] = m => m.Kind == "operator",
            [SectionNames.ExplicitInterfaceImplementations] = m => m.Kind == "explicit-interface-implementation",
            [SectionNames.ExtensionMethods] = m => m.Kind == "extension-method",
            [SectionNames.Constructors] = m => m.Kind == "constructor",
            [SectionNames.Finalizer] = m => m.Kind == "finalizer",
            [SectionNames.Events] = m => m.Kind == "event",
            [SectionNames.CustomAttributes] = ApiMemberSectionDescriptors.IsMethodLike,
            [SectionNames.SourceLocations] = ApiMemberSectionDescriptors.IsMethodLike,
        };

    /// <summary>
    /// Builds a copy of <paramref name="type"/> scoped to the requested sections: members are
    /// restricted to the selected member sections, and the Baseclass / Interfaces / Type
    /// Parameters facets are retained only when their section is selected. Identity fields
    /// (namespace, name, kind) are always preserved.
    /// </summary>
    private static ApiType ProjectTypeToSections(ApiType type, IEnumerable<ApiMember> members, HashSet<string> sections)
    {
        var predicates = MemberSectionPredicates
            .Where(kv => sections.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        var scopedMembers = predicates.Count > 0
            ? members.Where(m => predicates.Any(p => p(m))).ToList()
            : [];
        bool typeInfo = sections.Contains(SectionNames.TypeInfo);
        bool sourceFiles = sections.Contains(SectionNames.SourceFiles);

        return new ApiType
        {
            Namespace = type.Namespace,
            Name = type.Name,
            MetadataName = type.MetadataName,
            DefinitionName = type.DefinitionName,
            Accessibility = type.Accessibility,
            Kind = type.Kind,
            Attributes = sections.Contains(SectionNames.CustomAttributes) ? type.Attributes : [],
            EnumUnderlyingType = typeInfo || sections.Contains(SectionNames.Values)
                ? type.EnumUnderlyingType
                : null,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            IsByRefLike = type.IsByRefLike,
            IsReadOnly = type.IsReadOnly,
            BaseType = (typeInfo || sections.Contains(SectionNames.Baseclass))
                && IsRenderableBaseType(type.BaseType)
                    ? type.BaseType
                    : null,
            Interfaces = typeInfo || sections.Contains(SectionNames.TypeInterfaces)
                ? type.Interfaces
                : [],
            TypeParameters = typeInfo || sections.Contains(SectionNames.TypeParameters)
                ? type.TypeParameters
                : [],
            Members = scopedMembers,
            SourceFilePath = sourceFiles ? type.SourceFilePath : null,
            SourceUrl = sourceFiles ? type.SourceUrl : null,
            GitHubBrowseUrl = sourceFiles ? type.GitHubBrowseUrl : null,
            SourceLineNumber = sourceFiles ? type.SourceLineNumber : null,
            SourceResolution = sourceFiles ? type.SourceResolution : null,
            SourceChecksum = sourceFiles ? type.SourceChecksum : null,
            SourceChecksumAlgorithm = sourceFiles ? type.SourceChecksumAlgorithm : null,
            AdditionalSourceFiles = sourceFiles ? type.AdditionalSourceFiles : [],
            IsForwarded = type.IsForwarded,
            Documentation = type.Documentation,
        };
    }

    /// <summary>
    /// Mirrors the Baseclass section's CanRender: a base type is meaningful only when it is
    /// present and not one of the implicit roots (Object/ValueType/Enum).
    /// </summary>
    private static bool IsRenderableBaseType(string? baseType)
        => !string.IsNullOrEmpty(baseType)
           && baseType is not ("System.Object" or "System.ValueType" or "System.Enum");

}
