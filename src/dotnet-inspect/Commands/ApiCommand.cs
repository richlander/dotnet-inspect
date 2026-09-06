using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Net;
using DotnetInspector.CommandLine;
using DotnetInspector.CSharpBodySlicer;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
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
/// </summary>
public class ApiCommand
{
    internal static bool RejectUniversallyInvalidMemberSelect(
        MemberOptions options)
    {
        string[]? selectors =
            options.Discover is { Length: > 0 }
                ? options.Discover
                : options.Select;
        if (options.IncludeSections is not null
            || selectors is not { Length: > 0 })
        {
            return false;
        }

        return RejectUniversallyInvalidMemberSelect(
            options.Discover,
            options.Select,
            options.SelectDefault,
            options.RouterDeferredTypeOrMember,
            includeMemberTypeView:
                options.RouterDeferredTypeOrMember
                || options.MemberFilter.Count == 0);
    }

    internal static bool RejectUniversallyInvalidMemberSelect(
        string[]? discover,
        string[]? select,
        bool selectDefault,
        bool allowListingPipeline,
        bool includeMemberTypeView)
    {
        var allMemberPipelines = new[]
        {
            ApiMemberSectionDescriptors.CreatePipeline(),
            ApiMemberOverloadSectionDescriptors.CreatePipeline(),
            ApiMemberDetailSectionDescriptors.CreatePipeline(),
        };
        var memberPipelines =
            includeMemberTypeView
                ? allMemberPipelines
                : allMemberPipelines[1..];
        string[] knownSections =
        [
            .. memberPipelines
                .SelectMany(static pipeline =>
                    pipeline.SelectableSectionNames),
        ];
        knownSections =
        [
            .. knownSections.Distinct(
                StringComparer.OrdinalIgnoreCase),
        ];
        string[] defaultSections =
        [
            .. memberPipelines
                .SelectMany(pipeline =>
                    ReferenceEquals(
                        pipeline,
                        allMemberPipelines[2])
                        ? pipeline.FixedOverviewSectionNames
                        : pipeline.InfoSectionNames),
        ];
        Dictionary<string, string[]> categories =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (var pipeline in memberPipelines)
            AddCategories(pipeline.GetCategoryMap());

        bool hasSelection =
            select is { Length: > 0 }
            || selectDefault;
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            select,
            knownSections,
            defaultSections,
            categories,
            selectDefault);
        if (hasSelection
            && IsTotalFailure(selection))
        {
            if (!allowListingPipeline)
                return SelectOutput.WriteUnresolved(selection);

            var listingPipeline =
                ApiTypeSectionDescriptors.CreatePipeline();
            SelectResult listingSelection =
                SelectResolver.ResolveSelectAsSections(
                    select,
                    listingPipeline.SelectableSectionNames,
                    listingPipeline.FixedOverviewSectionNames,
                    listingPipeline.GetCategoryMap(),
                    selectDefault);
            if (IsTotalFailure(listingSelection))
                return SelectOutput.WriteUnresolved(selection);

            knownSections =
                listingPipeline.SelectableSectionNames;
            categories =
                new Dictionary<string, string[]>(
                    listingPipeline.GetCategoryMap(),
                    StringComparer.OrdinalIgnoreCase);
            selection = listingSelection;
        }

        if (discover is not { Length: > 0 })
            return false;

        SelectResult discovery = ResolveDiscovery(
            selection,
            knownSections,
            categories);
        if (IsTotalFailure(discovery)
            && allowListingPipeline)
        {
            var listingPipeline =
                ApiTypeSectionDescriptors.CreatePipeline();
            SelectResult listingSelection =
                SelectResolver.ResolveSelectAsSections(
                    select,
                    listingPipeline.SelectableSectionNames,
                    listingPipeline.FixedOverviewSectionNames,
                    listingPipeline.GetCategoryMap(),
                    selectDefault);
            SelectResult listingDiscovery = ResolveDiscovery(
                listingSelection,
                listingPipeline.SelectableSectionNames,
                listingPipeline.GetCategoryMap());
            if (!IsTotalFailure(listingDiscovery))
                return false;
        }
        return IsTotalFailure(discovery)
            && SelectOutput.WriteUnresolved(discovery);

        SelectResult ResolveDiscovery(
            SelectResult candidateSelection,
            IReadOnlyList<string> candidateSections,
            IReadOnlyDictionary<string, string[]> candidateCategories)
        {
            IReadOnlyList<string> discoverySections = hasSelection
                ? [.. candidateSelection.Sections ?? []]
                : candidateSections;
            var discoverySet = new HashSet<string>(
                discoverySections,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string[]> discoveryCategories =
                candidateCategories
                    .Select(pair => new KeyValuePair<string, string[]>(
                        pair.Key,
                        [.. pair.Value.Where(discoverySet.Contains)]))
                    .Where(pair => pair.Value.Length > 0)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase);
            return SelectResolver.ResolveSelectAsSections(
                discover,
                discoverySections,
                infoSections: [],
                discoveryCategories,
                selectDefault: false);
        }

        void AddCategories(
            IReadOnlyDictionary<string, string[]> source)
        {
            foreach (var (name, sections) in source)
            {
                categories[name] =
                    categories.TryGetValue(
                        name,
                        out string[]? existing)
                        ? existing.Concat(sections)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                        : sections;
            }
        }

        static bool IsTotalFailure(SelectResult result) =>
            result.Unresolved.Count > 0
            && result.Sections is null or { Count: 0 };
    }

    internal static bool RejectRouteIndependentOptionShape(
        MemberOptions options)
    {
        if (!options.RouterDeferredTypeOrMember
            || (options.Discover is not null
                && !options.EffectiveDiscovery))
        {
            return false;
        }

        return !ValidateRouteIndependentOptionShape(options);
    }

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
        var bareSelectSections = typePipeline.FixedOverviewSectionNames;

        if (options.DiscoverDeferredToListing)
        {
            SelectResult discoverResult =
                ResolveDiscoveryForListing(
                    options,
                    typePipeline);
            if (DiscoverOutput.WriteUnresolvedSections(
                    discoverResult))
            {
                return null;
            }

            options = options with
            {
                DiscoverDeferredToListing = false,
            };
        }

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
            ? options with
            {
                IncludeSections = selectResult.Sections,
                ExactIncludeSectionsOverride = selectResult.ExactSections,
                SelectDeferredToListing = false
            }
            : options with { SelectDeferredToListing = false };

        // Re-check selection arity against the final listing catalog. The payload projections are
        // deliberately not re-checked: the listing refuses them outright further down, and that
        // reason is the useful one.
        bool hasCatalogDependentSelection =
            options.SelectDeferredToListing
            || options.Select is { Length: > 0 }
            || options.SelectDefault;
        if (hasCatalogDependentSelection)
        {
            if (listingOptions.Discover == null && listingOptions.Count
                && (!CountOutput.ValidateSectionsSelected(
                        listingOptions.IncludeSections, fixedOverview: false)
                    || !CountOutput.ValidateMapFormat(
                        listingOptions.Format,
                        OutputFormatter.ResolveCountMapSections(
                            typePipeline, listingOptions.IncludeSections, fixedOverview: false),
                        listingOptions.Tree,
                        listingOptions.EmbeddedMermaid)))
            {
                return null;
            }

            if (!listingOptions.Count
                && !OutputFormatResolver.ValidateSingleSectionForTabular(
                    listingOptions.TabularExplicitlySet, listingOptions.IncludeSections))
                return null;
        }

        return listingOptions;
    }

    // ===== Shared Preamble =====

    /// <summary>
    /// True when a single-type request's selection can belong to a type listing, so
    /// catalog-dependent validation must wait for target resolution.
    /// </summary>
    /// <remarks>
    /// A name valid for neither pipeline is a plain typo and still fails here, keeping the fast
    /// rejection -- and the single-type suggestions -- for the case that cannot be a listing.
    /// </remarks>
    internal static bool ShouldDeferSelectToListing(
        ApiOptions options,
        bool singleTypeMode,
        SectionPipeline<ApiSurface> typePipeline)
    {
        if (options is not TypeOptions
            || !singleTypeMode
            || (options.Select is not { Length: > 0 } && !options.SelectDefault))
            return false;

        return ResolveSelectForListing(options, typePipeline).Sections is { Count: > 0 };
    }

    private static SelectResult ResolveSelectForListing(ApiOptions options, SectionPipeline<ApiSurface> typePipeline)
        => SelectResolver.ResolveSelectAsSections(
            options.Select,
            typePipeline.SelectableSectionNames,
            typePipeline.FixedOverviewSectionNames,
            typePipeline.GetCategoryMap(),
            selectDefault: options.SelectDefault);

    private static SelectResult ResolveDiscoveryForListing(
        ApiOptions options,
        SectionPipeline<ApiSurface> typePipeline)
        => SelectResolver.ResolveSelectAsSections(
            options.Discover,
            typePipeline.SelectableSectionNames,
            typePipeline.FixedOverviewSectionNames,
            typePipeline.GetCategoryMap(),
            selectDefault: false);

    internal static bool ShouldDeferDiscoveryToListing(
        ApiOptions options,
        bool singleTypeMode,
        SelectResult singleTypeResult,
        SectionPipeline<ApiSurface> typePipeline)
    {
        if (options is not TypeOptions
            || !singleTypeMode
            || options.Discover is not { Length: > 0 })
        {
            return false;
        }

        bool totalFailure =
            singleTypeResult.Unresolved.Count > 0
            && singleTypeResult.Sections
                is null or { Count: 0 };
        return totalFailure
            && ResolveDiscoveryForListing(
                    options,
                    typePipeline)
                .Sections is { Count: > 0 };
    }

    /// <summary>
    /// Resolves a deferred selection and validates its output shape after lookup
    /// chooses the single-type catalog.
    /// </summary>
    internal static TypeOptions? ReresolveSectionsForSingleType(TypeOptions options)
    {
        if (!options.SelectDeferredToListing)
            return options;

        var (preamble, error) = RunPreamble(
            options with { SelectDeferredToListing = false },
            allowListingFallback: false);
        return error.HasValue ? null : (TypeOptions)preamble.Options;
    }

    internal static bool RejectDeferredDiscoveryForSingleType(
        ApiOptions options,
        SectionPipeline<ApiType> memberPipeline)
    {
        if (!options.DiscoverDeferredToListing)
            return false;

        SelectResult result =
            SelectResolver.ResolveSelectAsSections(
                options.Discover,
                memberPipeline.SelectableSectionNames,
                memberPipeline.FixedOverviewSectionNames,
                memberPipeline.GetCategoryMap(),
                selectDefault: false);
        DiscoverOutput.WriteUnresolvedSections(result);
        return true;
    }

    internal record PreambleResult(
        ApiOptions Options,
        SectionPipeline<ApiSurface> TypePipeline,
        SectionPipeline<ApiType> MemberPipeline);

    internal static (PreambleResult Result, int? Error) RunPreamble(
        ApiOptions options,
        ResolvedMemberInspectionPlan? resolvedPlan = null,
        bool allowListingFallback = true)
    {
        options = options with { UserVerbosityOverride = options.UserVerbosity };
        if (options.Discover is not null
            && !options.EffectiveDiscovery)
        {
            StructuralDiscoveryPlan structuralPlan =
                StructuralViewRegistry.CreateApiPlan(options);
            StructuralDiscoveryRequest request =
                StructuralDiscoveryRequest.From(options);
            int exitCode = structuralPlan switch
            {
                StructuralDiscoveryPlan.Resolved resolved =>
                    StructuralViewRegistry.Execute(
                        resolved.Route,
                        request),
                StructuralDiscoveryPlan.Alternatives alternatives =>
                    StructuralViewRegistry.Execute(
                        alternatives.Value,
                        request),
                _ => 1,
            };
            return (null!, exitCode);
        }

        if (options is MemberOptions { IncludeSections: not null } preResolvedMemberOptions)
            options = preResolvedMemberOptions with { MemberSectionsPreResolved = true };

        resolvedPlan ??=
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options);
        var typePipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var memberPipeline =
            resolvedPlan.Selection.Catalog
                is InspectionCatalogIdentity.ApiMember
                or InspectionCatalogIdentity.ApiMemberOverload
                or InspectionCatalogIdentity.ApiMemberDetail
                ? ApiInspectionCatalogRegistry.CreateMemberPipeline(
                    resolvedPlan.Selection.Catalog,
                    resolvedPlan.Intent.Members.OverloadIndex)
                : ApiMemberSectionDescriptors.CreatePipeline();
        bool singleTypeMode =
            resolvedPlan.Selection.Catalog
                != InspectionCatalogIdentity.ApiType;
        var knownSections = singleTypeMode
            ? memberPipeline.SelectableSectionNames
            : typePipeline.SelectableSectionNames;
        // Bare -S renders the fixed overview: the sections whose length does not depend on which
        // type you are looking at. For a single type that is Type Info, so `type X -S` reports the
        // same shape for a 250-member class and an 8-member enum, where the member sections it used
        // to render varied from one section to eight.
        //
        // Selected member details join the fixed overview here: Signature is bounded, while the
        // former info preset also included Decompiled Source and therefore grew with the method
        // body. Broad member lists and member-name overload inventories retain their own compact
        // summary presets; they need separate bounded overview designs. See #3547.
        //
        // Type listing joins here as of this slice. It previously had no Fixed section to offer --
        // every section it published was a per-kind member table that grows with the assembly -- so
        // its bare -S resolved to an empty set and fell through to the verbosity ladder, printing
        // all five growing tables. #3648 gave it the bounded API Info section, so bare -S can now
        // mean the same thing here that it means everywhere else.
        var usesFixedOverview = options is TypeOptions
            || resolvedPlan.Selection.Catalog
                == InspectionCatalogIdentity.ApiMemberDetail;
        var bareSelectSections = usesFixedOverview
            ? singleTypeMode
                ? memberPipeline.FixedOverviewSectionNames
                : typePipeline.FixedOverviewSectionNames
            : singleTypeMode
                ? memberPipeline.InfoSectionNames
                : typePipeline.InfoSectionNames;

        // A fixed-overview bare -S that resolves to no sections has to fail loudly. SelectResolver
        // hands back an empty-but-non-null set, and IsRequested's `include is { Count: > 0 }` reads
        // that as "no filter at all" and falls through to the verbosity ladder -- turning a request
        // for a bounded overview into the widest output the command has, with the scanner
        // backpressure -S exists to apply switched off.
        if (usesFixedOverview && HasNoBareSelectOverview(options, bareSelectSections))
        {
            CommandError.Write(
                "this view publishes no bare -S overview sections.",
                "Use -S <Section> to select one, -D to discover what is available, or -S @All for everything.");
            return (null!, 1);
        }

        // Resolve raw selectors unless member lookup already supplied the authoritative set.
        // Both paths still enforce Body Shapes requirements before acquisition.
        if (options is not MemberOptions { MemberSectionsPreResolved: true })
        {
            bool hasSelection =
                options.Select is { Length: > 0 }
                || options.SelectDefault;
            SelectResult selectResult =
                options.Discover is null
                || !hasSelection
                    ? resolvedPlan.Selection.ToSelectResult()
                    : SelectResolver.ResolveSelectAsSections(
                        options.Select,
                        knownSections,
                        bareSelectSections,
                        singleTypeMode
                            ? memberPipeline.GetCategoryMap()
                            : typePipeline.GetCategoryMap(),
                        selectDefault: options.SelectDefault);
            bool discoveryOnly =
                options.Discover is not null
                && !hasSelection;
            if (allowListingFallback
                && ShouldDeferSelectToListing(options, singleTypeMode, typePipeline))
            {
                options = options with { SelectDeferredToListing = true };
            }
            else if (allowListingFallback
                && discoveryOnly
                && ShouldDeferDiscoveryToListing(
                    options,
                    singleTypeMode,
                    selectResult,
                    typePipeline))
            {
                options = options with
                {
                    DiscoverDeferredToListing = true,
                };
            }
            else
            {
                if (discoveryOnly
                    ? DiscoverOutput.WriteUnresolvedSections(
                        selectResult)
                    : SelectOutput.WriteUnresolved(selectResult))
                {
                    return (null!, 1);
                }
                if (ApplyBodyShapeSelectionRequirements(
                        options,
                        selectResult) is { } bodyShapeError)
                {
                    CommandError.Write(bodyShapeError);
                    return (null!, 1);
                }
                if (!discoveryOnly
                    && selectResult.Sections != null)
                {
                    options = options with
                    {
                        IncludeSections = selectResult.Sections,
                        ExactIncludeSectionsOverride = selectResult.ExactSections,
                    };
                }
            }
        }
        else if (options is MemberOptions { IncludeSections: { } preResolvedSections })
        {
            var selectResult = new SelectResult(
                new HashSet<string>(
                    preResolvedSections,
                    StringComparer.OrdinalIgnoreCase),
                [])
            {
                ExactSections = new HashSet<string>(
                    options.ExactIncludeSections ?? [],
                    StringComparer.OrdinalIgnoreCase)
            };
            if (ApplyBodyShapeSelectionRequirements(
                    options,
                    selectResult) is { } bodyShapeError)
            {
                CommandError.Write(bodyShapeError);
                return (null!, 1);
            }
            options = options with
            {
                IncludeSections = selectResult.Sections,
                ExactIncludeSectionsOverride = selectResult.ExactSections,
            };
        }
        (options, string? findingCensusSelectionError) =
            NormalizeFindingCensusSelection(
                options,
                memberPipeline.SelectableSectionNames);
        if (findingCensusSelectionError is not null)
        {
            CommandError.Write(findingCensusSelectionError);
            return (null!, 1);
        }
        if (options is
            {
                BodyKindQuery.HasFilter: true,
                Select: null,
                SelectDefault: false,
                Discover: null,
                IncludeSections: null,
            })
        {
            options = options with
            {
                IncludeSections = [SectionNames.BodyShapes],
            };
        }

        // A deferred select has no IncludeSections yet, and the preamble cannot know whether a
        // listing or the single-type view will render, so every selection check below has to stand
        // down: judging the empty set reports a requirement to narrow -S that is neither true nor
        // actionable, and judging the listing's sections preempts the single-type view's own, more
        // accurate rejection. ReresolveSectionsForListing re-runs them once the pipeline is known.
        var selectionSections = options.SelectDeferredToListing ? null : options.IncludeSections;
        var countMapSelectionSections = selectionSections;
        if (selectionSections is { Count: > 0 }
            && options is MemberOptions { HasCallerScope: true })
        {
            countMapSelectionSections = new HashSet<string>(
                selectionSections,
                StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.Callers
            };
        }
        var countMapSections = singleTypeMode
            ? OutputFormatter.ResolveCountMapSections(
                memberPipeline, countMapSelectionSections, fixedOverview: false)
            : OutputFormatter.ResolveCountMapSections(
                typePipeline, countMapSelectionSections, fixedOverview: false);
        if (options.Discover == null && options.Count && !options.SelectDeferredToListing
            && (!CountOutput.ValidateSectionsSelected(selectionSections, fixedOverview: false)
                || !CountOutput.ValidateMapFormat(
                    options.Format, countMapSections, options.Tree, options.EmbeddedMermaid)))
        {
            return (null!, 1);
        }

        var shapeCount = ShapeProjectionOutput.ActiveShapeCount(options.Value, options.Urls, options.Paths);
        if (!ValidateActiveShapeCount(shapeCount))
            return (null!, 1);

        if (shapeCount == 1)
        {
            var optionName = options.Value ? "--value" : options.Urls ? "--urls" : "--paths";
            // Discovery renders its own payload and refuses the shape projections itself with
            // an accurate reason; demanding -S first reports a requirement that is not the problem.
            if (options.Discover == null && !options.SelectDeferredToListing
                && !ShapeProjectionOutput.ValidateSingleSection(selectionSections, optionName))
                return (null!, 1);
            if (!ValidateShapeProjectionModifiers(options, optionName))
                return (null!, 1);
        }

        if (!ValidateProjectionModifiers(options, shapeCount))
            return (null!, 1);

        if (options.Print && options.Discover == null && !options.SelectDeferredToListing
            && !ValidateApiPrintSelection(selectionSections))
            return (null!, 1);

        if (!options.SelectDeferredToListing
            && !options.Count
            && !OutputFormatResolver.ValidateSingleSectionForTabular(options.TabularExplicitlySet, selectionSections))
            return (null!, 1);

        if (options is MemberOptions memberFormat
            && options.Discover is null
            && !(options.Count && countMapSections is null))
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

        if (options.RenderOptions is not null)
            return (new PreambleResult(options, typePipeline, memberPipeline), null);

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

        return (new PreambleResult(options, typePipeline, memberPipeline), null);
    }

    private static (ApiOptions Options, string? Error) NormalizeFindingCensusSelection(
        ApiOptions options,
        IReadOnlyList<string> memberSections)
    {
        if (options.IncludeSections?.Contains(SectionNames.FindingCensus) != true
            || options.ExactIncludeSections?.Contains(SectionNames.FindingCensus) == true)
        {
            return (options, null);
        }

        bool hasNonExactFindingCensusSelector =
            options.Select?.Any(selector =>
            {
                if (selector.StartsWith('@'))
                    return false;
                var (matches, _) = SelectResolver.ResolveSingle(
                    selector,
                    memberSections);
                return matches.Count == 1
                       && matches[0].Equals(
                           SectionNames.FindingCensus,
                           StringComparison.OrdinalIgnoreCase);
            }) == true;
        if (hasNonExactFindingCensusSelector)
        {
            return (
                options,
                $"section '{SectionNames.FindingCensus}' requires an exact -S selector.");
        }

        bool hasBroadFindingCensusSelector =
            options.Select?.Any(selector =>
            {
                if (selector.StartsWith('@'))
                    return false;
                var (matches, _) = SelectResolver.ResolveSingle(
                    selector,
                    memberSections);
                return matches.Count > 1
                       && matches.Contains(
                           SectionNames.FindingCensus,
                           StringComparer.OrdinalIgnoreCase);
            }) == true;
        if (!SelectResolver.IsAllSelector(options.Select)
            && !hasBroadFindingCensusSelector)
        {
            return (
                options,
                $"section '{SectionNames.FindingCensus}' cannot be selected through a category.");
        }

        var sections = new HashSet<string>(
            options.IncludeSections,
            StringComparer.OrdinalIgnoreCase);
        sections.Remove(SectionNames.FindingCensus);
        return (options with { IncludeSections = sections }, null);
    }

    internal static string? ApplyBodyShapeSelectionRequirements(
        ApiOptions options,
        SelectResult selectResult)
    {
        if (selectResult.Sections is not { } sections)
            return options.BodyKindQuery.HasFilter
                && options.Select is { Length: > 0 }
                ? $"--where Kind=... targets section '{SectionNames.BodyShapes}'."
                : null;

        bool selected = sections.Contains(SectionNames.BodyShapes);
        if (options.BodyKindQuery.HasFilter)
        {
            return selected
                ? null
                : $"--where Kind=... targets section '{SectionNames.BodyShapes}'. "
                    + $"Omit -S or include -S \"{SectionNames.BodyShapes}\".";
        }

        if (!selected)
            return null;

        const string required =
            "Section 'Body Shapes' requires --where \"Kind=<C# Body Kinds ID>\".";
        bool explicitlyTargetsBodyShapes =
            options is MemberOptions { MemberSectionsPreResolved: true }
                ? selectResult.ExactSections.Contains(SectionNames.BodyShapes)
                : TargetsBodyShapes(options, options.Select);
        if (explicitlyTargetsBodyShapes
            || sections.Count == 1)
        {
            return required;
        }

        sections.Remove(SectionNames.BodyShapes);
        return null;
    }

    internal static bool TargetsBodyShapes(
        ApiOptions options,
        string[]? selectors)
    {
        if (selectors is not { Length: > 0 })
            return false;

        var pipeline = ApiMemberSectionPipelines.Create(options);
        foreach (var selector in selectors)
        {
            var resolved = SelectResolver.ResolveSelectAsSections(
                [selector],
                pipeline.SelectableSectionNames,
                pipeline.InfoSectionNames,
                pipeline.GetCategoryMap());
            if (!resolved.HasError
                && resolved.Sections is { Count: 1 } sections
                && sections.Contains(SectionNames.BodyShapes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidateMemberGraphFormat(
        MemberOptions options,
        IReadOnlyCollection<string>? sections)
    {
        if (!ValidateMemberGraphFormatConflict(options))
            return false;

        if (options.Tree)
        {
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

    private static bool ValidateMemberGraphFormatConflict(MemberOptions options)
    {
        if (!options.Tree || !options.FormatFlagExplicitlySet)
            return true;

        CommandError.Write(
            "--tree is a standalone output format and cannot combine with another output format.");
        return false;
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

    private static bool ValidateRouteIndependentOptionShape(
        ApiOptions options,
        int? activeShapeCount = null)
    {
        if (options is MemberOptions memberOptions
            && !ValidateMemberGraphFormatConflict(memberOptions))
        {
            return false;
        }

        var shapeCount = activeShapeCount
            ?? ShapeProjectionOutput.ActiveShapeCount(
                options.Value,
                options.Urls,
                options.Paths);
        if (!ValidateActiveShapeCount(shapeCount))
            return false;

        if (shapeCount == 1)
        {
            var optionName = options.Value
                ? "--value"
                : options.Urls
                    ? "--urls"
                    : "--paths";
            if (!ValidateShapeProjectionModifiers(options, optionName))
                return false;
        }

        return ValidateProjectionModifiers(options, shapeCount);
    }

    private static bool ValidateActiveShapeCount(int shapeCount)
    {
        if (shapeCount > 1)
        {
            CommandError.Write(
                "specify only one of --value, --urls, or --paths.");
            return false;
        }

        return true;
    }

    private static bool ValidateShapeProjectionModifiers(
        ApiOptions options,
        string optionName)
    {
        if (options.Count || options.Print)
        {
            CommandError.Write(
                $"{optionName} cannot be combined with --count or --print.");
            return false;
        }
        if (options.Rows is not null)
        {
            CommandError.Write(
                $"--rows cannot be combined with {optionName}; use -n N to limit projected output lines or --row N|first|last to select a projected row.");
            return false;
        }

        return true;
    }

    private static bool ValidateProjectionModifiers(
        ApiOptions options,
        int shapeCount)
    {
        if (options.JsonArray && shapeCount == 0 && !options.Print)
        {
            CommandError.Write(
                "--json-array requires --value, --urls, --paths, or --print.");
            return false;
        }

        if (options.JsonArray && (options.JsonOutput || options.Jsonl))
        {
            CommandError.Write(
                "--json-array cannot be combined with --json or --jsonl.");
            return false;
        }

        if (options.Print && options.Rows is not null)
        {
            CommandError.Write(
                "--rows cannot be combined with --print; use --row N|first|last to choose a printed row.");
            return false;
        }

        if (options.PrintRow is not null
            && !options.Print
            && shapeCount == 0)
        {
            CommandError.Write(
                "--row requires --print, --value, --urls, or --paths.");
            return false;
        }

        return true;
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
        bool filtersConstraintSubjects =
            !string.IsNullOrEmpty(typeFilter)
            || options.KindFilter.Count > 0
            || options.UnsafeOnly;
        var materializedSubjects =
            new HashSet<ApiSurfaceInspectionSubject>();
        var materializedTokens = new HashSet<int>();
        var materializedDefinitions =
            new HashSet<MetadataTypeDefinitionName>();
        if (filtersConstraintSubjects)
        {
            AddRetainedTypes(
                api.Types,
                materializedDefinitions,
                materializedTokens,
                materializedSubjects);
        }

        if (!string.IsNullOrEmpty(typeFilter))
        {
            api.Types = api.Types
                .Where(t => TypeMatcher.MatchesTypeFilter(t.FullName, typeFilter))
                .ToList();
            api.PublicTypeCount = api.Types.Count;
        }

        if (options.KindFilter.Count > 0)
        {
            api.Types = api.Types.Where(t => options.KindFilter.Contains(t.Kind)).ToList();
            api.PublicTypeCount = api.Types.Count;
        }

        if (options.UnsafeOnly)
        {
            foreach (var type in api.Types)
            {
                type.Members = type.Members.Where(m => m.IsUnsafe).ToList();
            }
            api.Types = api.Types.Where(t => t.Members.Count > 0).ToList();
            api.PublicTypeCount = api.Types.Count;
            api.PublicMethodCount = api.Types.Sum(t => t.Members.Count(ApiMemberSectionDescriptors.IsMethodLike));
            api.PublicPropertyCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "property"));
            api.PublicFieldCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "field"));
            api.PublicEventCount = api.Types.Sum(t => t.Members.Count(m => m.Kind == "event"));
        }

        if (filtersConstraintSubjects)
            ReprojectConstraintFailures(api);

        void ReprojectConstraintFailures(ApiSurface surface)
        {
            var retainedSubjects =
                new HashSet<ApiSurfaceInspectionSubject>();
            var retainedTokens = new HashSet<int>();
            var retainedDefinitions =
                new HashSet<MetadataTypeDefinitionName>();
            foreach (ApiType type in surface.Types)
            {
                if (type.DefinitionName is { } definition)
                    retainedDefinitions.Add(definition);
                Add(type.SourceAssemblyPath, type.MetadataToken);
                foreach (ApiMember member in type.Members)
                {
                    Add(type.SourceAssemblyPath, member.MetadataToken);
                    Add(type.SourceAssemblyPath, member.GetterToken);
                    Add(type.SourceAssemblyPath, member.SetterToken);
                    Add(type.SourceAssemblyPath, member.AdderToken);
                    Add(type.SourceAssemblyPath, member.RemoverToken);
                }
            }

            surface.ReprojectConstraintResolutionFailures(
                subject =>
                    retainedTokens.Contains(subject.SubjectToken)
                    && (subject.SourceAssemblyPath is null
                        || retainedSubjects.Contains(subject)
                        || retainedSubjects.Contains(
                            new ApiSurfaceInspectionSubject(
                                null,
                                subject.SubjectToken))));
            surface.InspectionFailures.RemoveAll(
                failure =>
                    failure.Operation
                        != ApiSurfaceInspectionFailure
                            .GenericParameterConstraintResolutionOperation
                    && ExcludesOwnedFailure(failure));

            bool ExcludesOwnedFailure(
                ApiSurfaceInspectionFailure failure)
            {
                if (failure.OwningTypeDefinition is { } owner)
                {
                    return materializedDefinitions.Contains(owner)
                        && !retainedDefinitions.Contains(owner);
                }
                if (!failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
                {
                    if (failure.AffectedTypeDefinitions.Any(
                            retainedDefinitions.Contains))
                    {
                        return false;
                    }

                    return failure.AffectedTypeDefinitions.All(
                        materializedDefinitions.Contains);
                }
                if (failure.OwningTypeToken is not int token)
                    return false;

                return IncludesMaterializedOwner(
                           token,
                           failure.SourceAssemblyPath)
                    && !IncludesRetainedOwner(
                        token,
                        failure.SourceAssemblyPath);
            }

            void Add(string? path, int? token)
            {
                if (token is not int value)
                    return;

                retainedTokens.Add(value);
                retainedSubjects.Add(
                    new ApiSurfaceInspectionSubject(path, value));
            }

            bool IncludesRetainedOwner(
                int token,
                string? path) =>
                retainedTokens.Contains(token)
                && (path is null
                    || retainedSubjects.Contains(
                        new ApiSurfaceInspectionSubject(
                            path,
                            token))
                    || retainedSubjects.Contains(
                        new ApiSurfaceInspectionSubject(
                            null,
                            token)));
        }

        void AddRetainedTypes(
            IReadOnlyList<ApiType> types,
            HashSet<MetadataTypeDefinitionName> definitions,
            HashSet<int> tokens,
            HashSet<ApiSurfaceInspectionSubject> subjects)
        {
            foreach (ApiType type in types)
            {
                if (type.DefinitionName is { } definition)
                    definitions.Add(definition);
                Add(type.SourceAssemblyPath, type.MetadataToken);
                foreach (ApiMember member in type.Members)
                {
                    Add(type.SourceAssemblyPath, member.MetadataToken);
                    Add(type.SourceAssemblyPath, member.GetterToken);
                    Add(type.SourceAssemblyPath, member.SetterToken);
                    Add(type.SourceAssemblyPath, member.AdderToken);
                    Add(type.SourceAssemblyPath, member.RemoverToken);
                }
            }

            void Add(string? path, int? token)
            {
                if (token is not int value)
                    return;

                tokens.Add(value);
                subjects.Add(
                    new ApiSurfaceInspectionSubject(path, value));
            }
        }

        bool IncludesMaterializedOwner(
            int token,
            string? path) =>
            materializedTokens.Contains(token)
            && (path is null
                || materializedSubjects.Contains(
                    new ApiSurfaceInspectionSubject(
                        path,
                        token))
                || materializedSubjects.Contains(
                    new ApiSurfaceInspectionSubject(
                        null,
                        token)));
    }

    /// <summary>
    /// Writes a stderr note when sections explicitly requested via -S matched the schema
    /// but produced no data for this type (e.g. the enum-only "Values" section on a class).
    /// This distinguishes "valid but empty" from a typo (which yields a "not found" error)
    /// and from a silent empty render. Only meaningful for section-rendering output, so the
    /// caller must skip JSON (ignores -S), shape, and tabular output.
    /// </summary>
    internal static void WarnEmptySelectedSections(ApiType type, ApiOptions options, SectionPipeline<ApiType> pipeline)
    {
        if (options.IncludeSections is not { Count: > 0 })
            return;
        var sectionsPreResolved = options is MemberOptions { MemberSectionsPreResolved: true };
        if (SelectResolver.IsActiveInfoSelector(
                options.SelectDefault,
                options.IncludeSections,
                sectionsPreResolved)
            || SelectResolver.IsActiveAllSelector(
                options.Select,
                options.IncludeSections,
                sectionsPreResolved))
            return;

        var filtered = BuildFilteredTypeForSections(type, options);
        var (empty, _) = pipeline.GetEmptySections(filtered, options.Verbosity, options.IncludeSections);
        if (empty.Count == 0)
            return;

        bool filtersActive = options.MemberFilter.Count > 0 || options.KindFilter.Count > 0
            || options.UnsafeOnly || options.Limit.HasValue;
        var suffix = filtersActive ? " after filters" : "";

        if (empty.Count == 1)
            CommandError.WriteNote($"section '{empty[0]}' has no data for {type.FullName}{suffix}.");
        else
            CommandError.WriteNote($"{empty.Count} sections have no data for {type.FullName}{suffix}: {string.Join(", ", empty)}.");
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
            filteredMembers = filteredMembers.Take(options.Limit.Value).ToList();

        return new ApiType
        {
            Namespace = type.Namespace,
            Name = type.Name,
            // Preserve the exact metadata name so the filtered copy keeps matching
            // via ApiOutputFormatter.SameType (which prefers MetadataName over the
            // lossy '+'→'.' fallback) when it reaches the type-scope analysis path.
            MetadataName = type.MetadataName,
            DefinitionName = type.DefinitionName,
            IntroducedTypeParameterCounts =
                type.IntroducedTypeParameterCounts,
            Kind = type.Kind,
            // Every identity fact carries over: this copy exists to narrow Members, and anything
            // else it drops silently changes what sections and discovery see. Omitting the two
            // struct modifiers made `-D "Type Info"` hide the Modifiers row that `-S` rendered for
            // every readonly/ref struct, because discovery builds its manifest from this copy.
            Accessibility = type.Accessibility,
            Attributes = type.Attributes,
            EnumUnderlyingType = type.EnumUnderlyingType,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            IsByRefLike = type.IsByRefLike,
            IsReadOnly = type.IsReadOnly,
            SourceAssemblyPath = type.SourceAssemblyPath,
            MetadataToken = type.MetadataToken,
            BaseType = type.BaseType,
            Interfaces = type.Interfaces,
            DerivedTypes = type.DerivedTypes,
            TypeParameters = type.TypeParameters,
            Members = filteredMembers,
            SourceFilePath = type.SourceFilePath,
            SourceUrl = type.SourceUrl,
            GitHubBrowseUrl = type.GitHubBrowseUrl,
            SourceLineNumber = type.SourceLineNumber,
            SourceChecksum = type.SourceChecksum,
            SourceChecksumAlgorithm = type.SourceChecksumAlgorithm,
            SourceResolution = type.SourceResolution,
            AdditionalSourceFiles = type.AdditionalSourceFiles,
            IsForwarded = type.IsForwarded,
            Documentation = type.Documentation
        };
    }

    internal static DocumentSchema GetTypeDocumentSchema(ApiOptions options)
        => GetTypeDocumentSchema(
            ApiMemberSectionPipelines.UsesDetailPipeline(options));

    private static DocumentSchema GetTypeDocumentSchema(
        bool includeExactMemberColumns)
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
        if (!includeExactMemberColumns)
            return detailSchema;
        if (detailSchema.GetSection(SectionNames.Calls) == null)
            detailSchema.Add(SectionNames.Calls, "column", "IL Offset", "Evidence Method", "Opcode", "Call Kind", "Callee", "Operand Token", "Return Address");
        if (detailSchema.GetSection(SectionNames.Callers) == null)
            detailSchema.Add(SectionNames.Callers, "column", "Caller", "Evidence Method", "IL Offset", "Opcode", "Call Kind", "Operand Token", "Return Address");
        if (detailSchema.GetSection(SectionNames.UnsafeOperations) == null)
            detailSchema.Add(SectionNames.UnsafeOperations, "column", "Reason", "Detail", "Kind", "IL", "Token");
        // One bidirectional section, so one field list: the union of what the outbound and inbound
        // halves each used to declare separately.
        detailSchema.Add(
            SectionNames.CallGraph,
            "field",
            CallGraphFieldSelection.Names);
        return detailSchema;
    }

    internal static DocumentSchema GetStructuralSchema(
        InspectionCatalogIdentity identity)
    {
        if (identity == InspectionCatalogIdentity.ApiType)
        {
            return ApiViewContext.Default
                .GetSchemaInfo<CliApiSurface>()!
                .ToDocumentSchema();
        }

        if (identity is not (
            InspectionCatalogIdentity.ApiMember
            or InspectionCatalogIdentity.ApiMemberOverload
            or InspectionCatalogIdentity.ApiMemberDetail))
        {
            throw new ArgumentOutOfRangeException(
                nameof(identity),
                identity,
                "The requested identity is not an API catalog.");
        }
        ApiInspectionCatalog catalog =
            ApiInspectionCatalogRegistry.Get(identity);
        return RestrictSchemaToSections(
            GetTypeDocumentSchema(
                identity
                == InspectionCatalogIdentity.ApiMemberDetail),
            catalog.SectionNames);
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
        => await TryAcquirePdbPathCoreAsync(
            dllPath,
            sourceAssembly: null,
            options,
            logger,
            httpClient,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<string?> TryAcquirePdbPathAsync(
        string dllPath,
        ResolvedAssemblyReference sourceAssembly,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken = default,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
        => await TryAcquirePdbPathCoreAsync(
            dllPath,
            sourceAssembly,
            options,
            logger,
            httpClient,
            cancellationToken,
            fallbackPackageName,
            fallbackPackageVersion).ConfigureAwait(false);

    static async Task<string?> TryAcquirePdbPathCoreAsync(
        string dllPath,
        ResolvedAssemblyReference? sourceAssembly,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var service = sourceAssembly is null
                ? SourceLinkService.Open(dllPath, logger.Log)
                : SourceLinkService.Open(sourceAssembly, logger.Log);
            var context = service.Context;
            if (context.NeedsPdb)
            {
                var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                    ? PackageExtractor.ParsePackageReference(options.PackagePath)
                    : (null, null);
                pkgName = fallbackPackageName ?? pkgName;
                pkgVersion = fallbackPackageVersion ?? pkgVersion;
                if (sourceAssembly is null)
                {
                    await SourceEnricher.AcquirePdbAsync(
                        context,
                        httpClient,
                        pkgName,
                        pkgVersion,
                        isPlatformAssembly:
                            !string.IsNullOrEmpty(
                                options.PlatformAssembly),
                        logger.Log,
                        sourceOptions: options.SourceOptions,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await SourceEnricher.AcquirePdbAsync(
                        context,
                        sourceAssembly,
                        httpClient,
                        logger.Log,
                        sourceOptions: options.SourceOptions,
                        cancellationToken: cancellationToken,
                        fallbackPackageName: pkgName,
                        fallbackPackageVersion: pkgVersion);
                }
            }
            return context.PortablePdbPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch when (sourceAssembly is null)
        {
            return null;
        }
    }

    internal static HashSet<string> GetRequestedMemberSections(ApiType type, ApiOptions options)
    {
        var pipeline = ApiMemberSectionPipelines.Create(options);
        var explicitInclude = options is MemberOptions { MemberSectionsPreResolved: true };
        if (options.Discover is { Length: > 0 } discover)
        {
            bool hasSelection =
                options.IncludeSections is not null
                || options.Select is { Length: > 0 }
                || options.SelectDefault;
            IReadOnlyList<string> discoveryScope =
                hasSelection
                    ? [.. pipeline.GetCandidateSections(
                        options.Verbosity,
                        options.IncludeSections)]
                    : pipeline.SelectableSectionNames;
            var discoverySet = new HashSet<string>(
                discoveryScope,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string[]> categories =
                pipeline.GetCategoryMap()
                    .Select(pair =>
                        new KeyValuePair<string, string[]>(
                            pair.Key,
                            [.. pair.Value.Where(
                                discoverySet.Contains)]))
                    .Where(pair => pair.Value.Length > 0)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase);
            var resolved = SelectResolver.ResolveSelectAsSections(
                discover,
                discoveryScope,
                infoSections: [],
                categories);
            var discoveredSections = new HashSet<string>(
                resolved.Sections ?? [],
                StringComparer.OrdinalIgnoreCase);
            if (!options.BodyKindQuery.HasFilter)
                discoveredSections.Remove(SectionNames.BodyShapes);
            return discoveredSections;
        }

        return new HashSet<string>(
            pipeline.GetEffectiveSections(
                type,
                options.Verbosity,
                options.IncludeSections,
                explicitInclude: explicitInclude),
            StringComparer.OrdinalIgnoreCase);
    }

    // ===== Full API Surface Rendering =====

    internal static bool HasRejectedMetadataRows(
        ApiSurface api) =>
        CountRejectedMetadataRows(api) > 0;

    internal static int CountRejectedMetadataRows(
        ApiSurface api) =>
        api.InspectionFailures.Count(
            static failure =>
                failure.Operation
                    != ApiSurfaceInspectionFailure
                        .GenericParameterConstraintResolutionOperation);

    internal static void WriteConstraintResolutionDiagnostics(
        ApiSurface api)
    {
        foreach (ApiSurfaceInspectionFailure failure
            in api.InspectionFailures)
        {
            if (failure.Operation
                != ApiSurfaceInspectionFailure
                    .GenericParameterConstraintResolutionOperation)
            {
                continue;
            }

            WriteConstraintResolutionDiagnostic(failure);
        }
    }

    internal static int WriteSelectedSurfaceDiagnostics(
        ApiSurface api,
        ApiType selectedType,
        HashSet<string>? selectedMemberNames = null)
    {
        WarnSelectedApiInspectionIncomplete(
            api,
            selectedType,
            selectedMemberNames);
        int rejectedRows = CountRejectedMetadataRows(api);
        if (rejectedRows == 0)
            return 0;

        CommandError.WriteWarning(
            $"API inspection rejected {rejectedRows} metadata row(s); "
            + "selected output excludes failure details.");
        return 1;
    }

    internal static int WriteFullApiOutput(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        ApplySurfaceFilters(api, options, (options as TypeOptions)?.TypeFilter);
        int successExitCode =
            HasRejectedMetadataRows(api) ? 1 : 0;

        // Fail closed: the type-listing surface has no dispatch for payload projections
        // (--print/--value/--urls/--paths); its sections are type-name tables that expose no
        // printable payload. Report that honestly before rendering, rather than emitting the
        // whole document and then tripping the projection audit (#3390) with a "bug in
        // dotnet-inspect" message. --count is a payload projection the surface does honor, so
        // it is excluded from this guard.
        if (IsProjectionRequested(options))
            return RejectSurfacePayloadProjection(options);

        bool failureDetailsRendered =
            !options.Count
            && (options.JsonOutput
                || (options.Tabular
                    ? ApiOutputFormatter
                        .ShouldRenderSurfaceInspectionFailureTableView(
                            options)
                    : ApiOutputFormatter
                        .RendersInspectionFailures(
                            api,
                            options)));
        bool constraintDetailsRendered =
            !options.Count
            && (options.JsonOutput
                || (options.Tabular
                    && ApiOutputFormatter
                        .ShouldRenderSurfaceInspectionFailureTableView(
                            options)));
        if (!failureDetailsRendered)
        {
            int rejectedRows = CountRejectedMetadataRows(api);
            if (rejectedRows > 0)
            {
                CommandError.WriteWarning(
                    $"API inspection rejected {rejectedRows} metadata row(s); "
                    + "use default Markdown verbosity or JSON for failure details.");
            }
        }
        if (!constraintDetailsRendered)
            WriteConstraintResolutionDiagnostics(api);

        if (options.JsonOutput && !options.Count)
        {
            // --fields/--columns select table columns; document JSON has no column-slicing
            // facility, so the combination is rejected rather than silently dropped.
            if (IsColumnProjectionRequested(options))
                return RejectColumnProjectionUnderJson(suggestPayloadProjection: false);
            Console.WriteLine(JsonSerializer.Serialize(api, ApiJsonContext.Default.ApiSurface));
            return successExitCode;
        }

        var (view, _) = ApiOutputFormatter.BuildFullApiView(api, options);

        if (options.Count)
        {
            var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
            writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
            var projection = CountProjectionFormatter.Capture(
                view, ApiViewContext.Default, writerOptions);
            if (!TryReportEmptyProjection(projection.WroteAnyContent, options))
                return 1;
            var ordered = OutputFormatter.ResolveCountMapSections(
                ApiTypeSectionDescriptors.CreatePipeline(),
                options.IncludeSections,
                fixedOverview: false);
            CountOutput.Write(
                projection, ordered, options.Format, options.NoHeader);
        }
        else if (options.Tabular)
        {
            if (ApiOutputFormatter
                .ShouldRenderSurfaceInspectionFailureTableView(
                    options))
            {
                var failureRows =
                    OutputFormatter.RenderProjectedTable(
                        !options.NoHeader,
                        options.Tsv,
                        options.Jsonl,
                        options.Columns,
                        options.Fields,
                        (writer, formatter, writerOptions) =>
                        {
                            writerOptions.IncludeSections =
                                [SectionNames.InspectionFailures];
                            MarkoutSerializer.Serialize(
                                view,
                                writer,
                                formatter,
                                ApiViewContext.Default,
                                writerOptions);
                        });
                ProjectionDiagnostics.DiagnoseRendered(
                    options.Fields ?? options.Columns,
                    failureRows);
                if (!TryReportEmptyProjection(
                        failureRows,
                        options))
                {
                    return 1;
                }
                Console.Out.Write(
                    OutputFormatter.LimitRenderedTableRows(
                        failureRows,
                        options.Rows,
                        !options.NoHeader));
                return successExitCode;
            }

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
                return successExitCode;
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
            writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
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
                var markdownWriter = new StringWriter { NewLine = "\n" };
                MarkoutSerializer.Serialize(
                    view, markdownWriter, new MarkdownFormatter(), ApiViewContext.Default, writerOptions);
                var markdown = markdownWriter.ToString().TrimEnd();
                if (!TryReportEmptyProjection(markdown, options))
                    return 1;
                OutputFormatter.WriteLfLine(Console.Out, markdown);
            }
        }

        return successExitCode;
    }

    internal static bool WarnSelectedApiInspectionIncomplete(
        ApiSurface api,
        ApiType selectedType,
        HashSet<string>? selectedMemberNames = null)
    {
        HashSet<int> subjectTokens = [];
        if (selectedType.MetadataToken is int typeToken)
            subjectTokens.Add(typeToken);
        foreach (ApiMember member in selectedType.Members)
        {
            if (selectedMemberNames is { Count: > 0 }
                && !TypeMatcher.MatchesMemberFilter(
                    member.Name,
                    selectedMemberNames))
            {
                continue;
            }

            Add(member.MetadataToken);
            Add(member.GetterToken);
            Add(member.SetterToken);
            Add(member.AdderToken);
            Add(member.RemoverToken);
        }

        var failures =
            api.ConstraintResolutionFailuresBySubject
                .Where(pair =>
                    subjectTokens.Contains(pair.Key.SubjectToken)
                    && (pair.Key.SourceAssemblyPath is null
                        || string.Equals(
                            pair.Key.SourceAssemblyPath,
                            selectedType.SourceAssemblyPath,
                            StringComparison.Ordinal)))
                .SelectMany(pair => pair.Value)
                .Where(failure =>
                    failure.SourceAssemblyPath is null
                    || string.Equals(
                        failure.SourceAssemblyPath,
                        selectedType.SourceAssemblyPath,
                        StringComparison.Ordinal))
                .DistinctBy(failure => (
                    failure.SubjectAssembly,
                    failure.DependencyAssembly,
                    failure.SubjectToken,
                    failure.Mechanism,
                    failure.Kind,
                    failure.Detail))
                .Take(
                    ApiSurface.MaxVisibleConstraintResolutionFailures + 1)
                .ToList();
        if (failures.Count == 0)
            return false;

        foreach (ApiSurfaceInspectionFailure failure in failures.Take(
            ApiSurface.MaxVisibleConstraintResolutionFailures))
        {
            WriteConstraintResolutionDiagnostic(failure);
        }
        if (failures.Count
            > ApiSurface.MaxVisibleConstraintResolutionFailures)
        {
            CommandError.WriteWarning(
                "Additional generic-constraint classification diagnostics "
                    + "were suppressed.");
        }
        return true;

        void Add(int? token)
        {
            if (token is int value)
                subjectTokens.Add(value);
        }
    }

    static void WriteConstraintResolutionDiagnostic(
        ApiSurfaceInspectionFailure failure)
    {
        if (failure.SubjectToken == 0
            && failure.Kind == "ResourceLimit")
        {
            CommandError.WriteWarning(
                "Generic-constraint classification was incomplete: "
                    + failure.Detail);
            return;
        }

        string assembly =
            failure.SubjectAssembly is null
                ? ""
                : $" in '{AssemblyIdentityFormatter.Format(
                    failure.SubjectAssembly)}'";
        string dependency =
            failure.DependencyAssembly is null
                ? ""
                : $" via '{AssemblyIdentityFormatter.Format(
                    failure.DependencyAssembly)}'";
        CommandError.WriteWarning(
            "Generic-constraint classification was incomplete"
                + $"{assembly}{dependency} "
                + $"at 0x{failure.SubjectToken:X8} "
                + $"({failure.Mechanism}/{failure.Kind}): "
                + failure.Detail);
    }

    /// <summary>
    /// Fails a projection whose names cannot apply to the selected shape, rather than exiting 0
    /// with an empty or partially unrelated render. Returns false when the caller should stop.
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
    /// <c>--columns</c> is an honest empty answer -- <c>-S Interfaces</c> against a library that
    /// has no interfaces -- and reporting it as failure would turn a valid zero-row query into an
    /// error, and only in some output formats.</item>
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
    /// The name check is normally a narrowing condition on an already-empty render. When another
    /// selected section writes content, it also runs if the selection contains a section of the
    /// projected kind; otherwise unrelated rows can hide an invalid projection. It remains
    /// disabled for a cross-kind projection such as <c>-S Classes --fields NoSuchField</c>, where
    /// fields intentionally do not constrain the selected table.
    ///
    /// The candidates still come from every section, not only the selection. Two earlier attempts
    /// validated against selected-section names and produced false negatives, because the set of
    /// legitimately projectable names is wider than any one section's schema:
    /// <c>-S "API Info" --columns Field</c> names a column the fact-table renderer synthesizes and
    /// the schema never lists, and <c>-S Classes --fields Types</c> names a document-level field
    /// that survives regardless of which section is selected. The candidates below include the
    /// product-owned fact-table columns when API Info is selected.
    /// </remarks>
    private static bool TryReportEmptyProjection(
        string rendered,
        ApiOptions options,
        DocumentSchema? schema = null)
        => TryReportEmptyProjection(!string.IsNullOrWhiteSpace(rendered), options, schema);

    private static bool ProjectionIncludesSection(
        DocumentSchema schema,
        string section,
        ApiOptions options)
    {
        if (options.Fields is not { Length: > 0 }
            && options.Columns is not { Length: > 0 })
        {
            return true;
        }

        var sectionSchema = schema.GetSection(section);
        return sectionSchema is not null
            && ((options.Fields is { Length: > 0 } fields
                    && sectionSchema.ItemKind.Equals(
                        "field", StringComparison.OrdinalIgnoreCase)
                    && schema.ValidateProjection(section, fields).Resolved.Length > 0)
                || (options.Columns is { Length: > 0 } columns
                    && sectionSchema.ItemKind.Equals(
                        "column", StringComparison.OrdinalIgnoreCase)
                    && schema.ValidateProjection(section, columns).Resolved.Length > 0));
    }

    private static bool TryReportEmptyProjection(
        bool wroteAnyContent,
        ApiOptions options,
        DocumentSchema? schema = null)
    {
        var names = options.Fields ?? options.Columns;
        if (names is not { Length: > 0 })
            return true;

        var wantedKind = options.Fields is { Length: > 0 } ? "field" : "column";
        schema ??= ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();
        if (wroteAnyContent
            && options.IncludeSections is { Count: > 0 } sections
            && !sections.Any(section =>
                schema.GetSection(section)?.ItemKind.Equals(
                    wantedKind,
                    StringComparison.OrdinalIgnoreCase) == true))
        {
            return true;
        }

        // Resolved by KIND across EVERY section, not against the selected sections. Two
        // independent corrections are folded in here, and dropping either one reopens a real
        // false positive found in review:
        //
        // Across all sections, because a document-level field belongs to no section in
        // particular -- `Version` is advertised under `API Info` but survives whichever section
        // is selected -- so checking only the selection reports it unresolved. That is normally
        // unreachable because the document fields keep the render non-empty, but filtering the
        // selected table to zero rows (`-t "NoSuchType*" -S Classes --fields Version`) empties
        // the render and exposes it.
        //
        // By kind, because "valid somewhere" is too weak on its own: `Type` is a Classes COLUMN
        // and never a field, so `-S "API Info" --fields Type` would otherwise be validated by an
        // unrelated section's column and silently succeed while printing nothing. `--fields` can
        // only be satisfied by a field and `--columns` only by a column.
        var candidates = new List<string>();
        if (string.Equals(
                wantedKind,
                "column",
                StringComparison.OrdinalIgnoreCase)
            && options.IncludeSections?.Contains(SectionNames.ApiInfo) == true)
        {
            candidates.Add("Field");
            candidates.Add("Value");
        }

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
    /// <param name="MemberHasNoPdbDeclaration">
    /// True when the member has a body but its PDB source range does not identify one declaration
    /// to isolate.
    /// </param>
    /// <param name="MemberSourceTooComplex">
    /// True when verified source exceeded the bounded lexical-complexity limit.
    /// </param>
    /// <param name="MemberSourceCoordinatesInvalid">
    /// True when portable-PDB sequence-point coordinates cannot address the verified source.
    /// </param>
    /// <param name="PdbSourceUnavailableReason">
    /// Visible explanation when PDB source acquisition failed for another reason.
    /// </param>
    internal sealed record ResolvedMethodSource(
        MethodSourceContext? Source,
        string? PdbPath,
        bool MemberHasNoBody = false,
        bool MemberHasNoPdbDeclaration = false,
        bool MemberSourceTooComplex = false,
        bool MemberSourceCoordinatesInvalid = false,
        string? PdbSourceUnavailableReason = null);

    internal static async Task<ResolvedMethodSource> ResolveMethodSourceAsync(
        string dllPath, string typeName, string methodName, int overloadIndex,
        ApiOptions options, HttpClient httpClient, VerboseLogger logger, bool fetchSource = true,
        bool publicOnly = true, int sourceMetadataToken = 0,
        string? memberMetadataAssemblyPath = null, int memberMetadataToken = 0,
        ResolvedAssemblyReference? sourceAssembly = null,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
    {
        try
        {
            // A member with no IL body has no PDB source to resolve, whatever the PDB and
            // SourceLink situation is. The selected MethodDef token belongs to the assembly that
            // supplied the API member, which may differ from the runtime facade opened for PDB
            // lookup. Preserve that identity instead of applying the token to the wrong image;
            // only when no selected MethodDef identity is available, use the same name/overload
            // fallback as source lookup
            // (issue #3299).
            bool? memberHasBody = ResolveMemberBodyState(
                dllPath,
                typeName,
                methodName,
                overloadIndex,
                publicOnly,
                memberMetadataAssemblyPath,
                memberMetadataToken,
                logger.Log);
            if (memberHasBody == false)
            {
                return new ResolvedMethodSource(
                    null,
                    null,
                    MemberHasNoBody: true);
            }

            using var service = SourceLinkService.Open(dllPath, logger.Log);
            var context = service.Context;

            // Acquire PDB if needed (same flow as SourceEnricher)
            if (context.NeedsPdb)
            {
                var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                    ? PackageExtractor.ParsePackageReference(options.PackagePath)
                    : (null, null);
                pkgName = fallbackPackageName ?? pkgName;
                pkgVersion = fallbackPackageVersion ?? pkgVersion;

                if (sourceAssembly is null)
                {
                    await SourceEnricher.AcquirePdbAsync(
                        context,
                        httpClient,
                        pkgName,
                        pkgVersion,
                        isPlatformAssembly:
                            !string.IsNullOrEmpty(
                                options.PlatformAssembly),
                        logger.Log,
                        sourceOptions: options.SourceOptions);
                }
                else
                {
                    await SourceEnricher.AcquirePdbAsync(
                        context,
                        sourceAssembly,
                        httpClient,
                        logger.Log,
                        sourceOptions: options.SourceOptions,
                        fallbackPackageName: pkgName,
                        fallbackPackageVersion: pkgVersion);
                }
            }

            // Capture the acquired portable PDB path now so the decompiler can reuse it for local
            // names even when SourceLink/source resolution below fails (PDB available, source not).
            string? pdbPath = context.PortablePdbPath;

            if (!fetchSource)
                return new ResolvedMethodSource(null, pdbPath);
            if (!service.HasPdb)
            {
                return new ResolvedMethodSource(
                    null,
                    pdbPath,
                    PdbSourceUnavailableReason: NoPortablePdbReason);
            }

            var methodInfo = service.ResolveMethodSource(
                typeName,
                methodName,
                overloadIndex,
                publicOnly,
                sourceMetadataToken);
            if (methodInfo == null)
            {
                return new ResolvedMethodSource(
                    null,
                    pdbPath,
                    PdbSourceUnavailableReason: NoPdbSourceMappingReason);
            }

            // Honor the source the portable PDB records when it is present locally: a non-reproducible
            // (local dev) build keeps a real local path whose exact compiled bytes may exist only here,
            // so the remote SourceLink URL would 404 or differ. The checksum authenticates the on-disk
            // bytes against the portable PDB; remote SourceLink is the fallback for reproducible builds.
            string? content = null;
            SourceChecksumVerification checksumVerification =
                SourceChecksumVerification.Unavailable;
            var localBytes = DotnetInspector.Services.PdbSourceAcquisition.TryReadVerifiedLocalSource(
                methodInfo.FilePath, methodInfo.ChecksumAlgorithm, methodInfo.Checksum);
            byte[]? repoBytes;
            if (localBytes != null)
            {
                checksumVerification = PdbSourceAcquisition.VerifyChecksum(
                    methodInfo.ChecksumAlgorithm,
                    methodInfo.Checksum,
                    localBytes);
                content = NormalizePdbSourceLineEndings(
                    DotnetInspector.Services.PdbSourceAcquisition.DecodeSourceText(localBytes));
            }
            // Opt-in (--repo): read the committed blob at the SourceLink commit from a local clone,
            // authenticated by the same PDB checksum, before touching the network. Useful for a
            // reproducible build whose sources are private or simply already cloned on this machine.
            else if (options.SourceRepositories.Length > 0
                && (repoBytes = DotnetInspector.Services.LocalRepoSourceAcquisition.TryReadVerifiedRepoBlob(
                    methodInfo.SourceUrl, methodInfo.ChecksumAlgorithm, methodInfo.Checksum,
                    options.SourceRepositories)) != null)
            {
                checksumVerification = PdbSourceAcquisition.VerifyChecksum(
                    methodInfo.ChecksumAlgorithm,
                    methodInfo.Checksum,
                    repoBytes);
                content = NormalizePdbSourceLineEndings(
                    DotnetInspector.Services.PdbSourceAcquisition.DecodeSourceText(repoBytes));
            }
            else if (methodInfo.SourceUrl != null)
            {
                var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
                var fetch = await PdbSourceAcquisition.FetchVerifiedSourceTextAsync(
                    fetcher,
                    methodInfo.SourceUrl,
                    methodInfo.ChecksumAlgorithm,
                    methodInfo.Checksum);
                content = fetch.Text is null
                    ? null
                    : NormalizePdbSourceLineEndings(fetch.Text);
                checksumVerification = fetch.ChecksumVerification;
                if (fetch.Failure is not null)
                    logger.LogWarning(fetch.Failure);
            }

            if (content == null)
            {
                return new ResolvedMethodSource(
                    null,
                    pdbPath,
                    PdbSourceUnavailableReason: NoMatchingPdbSourceReason);
            }

            return SliceResolvedMethodSource(
                content,
                methodInfo.StartLine,
                methodInfo.EndLine,
                methodName,
                methodInfo.SourceUrl ?? methodInfo.FilePath,
                pdbPath,
                methodInfo.SequencePointStartLines,
                methodInfo.ChecksumAlgorithm,
                methodInfo.Checksum,
                checksumVerification);
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to resolve method source for {typeName}.{methodName}: {ex.Message}");
            return new ResolvedMethodSource(
                null,
                null,
                PdbSourceUnavailableReason: PdbSourceInspectionFailedReason);
        }
    }

    internal static bool? ResolveMemberBodyState(
        string dllPath,
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly,
        string? memberMetadataAssemblyPath,
        int memberMetadataToken,
        Action<string>? log)
    {
        bool hasMemberToken =
            memberMetadataToken != 0
            && memberMetadataAssemblyPath is { Length: > 0 };
        bool tokenAddressesLookupImage =
            hasMemberToken
            && LibraryMetadataService
                .ReferenceTreePathComparer(OperatingSystem.IsWindows())
                .Equals(
                    Path.GetFullPath(dllPath),
                    Path.GetFullPath(memberMetadataAssemblyPath!));

        if (hasMemberToken)
        {
            using var memberContext = PdbContext.OpenMetadataOnly(
                tokenAddressesLookupImage
                    ? dllPath
                    : memberMetadataAssemblyPath!,
                tokenAddressesLookupImage ? log : null);
            return memberContext.MethodHasBody(memberMetadataToken);
        }

        using var lookupContext = PdbContext.OpenMetadataOnly(dllPath, log);
        return lookupContext.MethodHasBody(
            typeName,
            methodName,
            overloadIndex,
            publicOnly);
    }

    internal static string NormalizePdbSourceLineEndings(string content)
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
        IReadOnlyList<int>? visibleSequencePointStartLines = null,
        string? checksumAlgorithm = null,
        byte[]? checksum = null,
        SourceChecksumVerification checksumVerification =
            SourceChecksumVerification.Unavailable)
    {
        try
        {
            string? sourceCode = BodySlicer.ExtractMethodBody(
                content,
                startLine,
                endLine,
                methodName,
                visibleSequencePointStartLines);

            // The PDB range does not identify one declaration: report no source rather than
            // a type header, initializer, or structurally unknown span.
            return sourceCode is null
                ? new ResolvedMethodSource(
                    null,
                    pdbPath,
                    MemberHasNoPdbDeclaration: true)
                : new ResolvedMethodSource(
                    new MethodSourceContext(
                        sourceCode,
                        sourceLocation,
                        checksumAlgorithm,
                        checksum is null ? null : Convert.ToHexString(checksum),
                        checksumVerification),
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

    internal static async Task<int> WriteTypeOutputAsync(ApiType type, string? foundIn, string? packageName, string? packageVersion, string? apiSource, string? selectedTfm, ApiOptions options, TextWriter? output = null, ResolvedAssemblyReference? sourceAssembly = null)
    {
        var sink = output ?? Console.Out;

        if (IsInvalidAnnotatedSourceDocumentJsonSelection(options))
        {
            CommandError.Write(
                $"section '{SectionNames.AnnotatedSourceDocument}' must be the only selected section under --json.");
            return 1;
        }
        if (IsInvalidFindingCensusJsonSelection(options))
        {
            CommandError.Write(
                $"section '{SectionNames.FindingCensus}' must be the only selected section under --json.");
            return 1;
        }
        if (IsInvalidFindingCensusProjection(options))
        {
            CommandError.Write(
                $"section '{SectionNames.FindingCensus}' is an indivisible document payload; "
                + "use Markdown/plaintext or exact singleton --json without row, column, count, or payload projection.");
            return 1;
        }
        bool findingCensusExplicitlySelected =
            HasExplicitFindingCensusSelector(options);
        if (findingCensusExplicitlySelected
            && (type.Members.Count != 1
                || !type.Members.Any(ApiMemberSectionDescriptors.IsBodyBacked)))
        {
            CommandError.Write(
                $"section '{SectionNames.FindingCensus}' requires one selected body-backed member.");
            return 1;
        }

        if (options is TypeOptions { ShapeOutput: true } typeOptions && !options.Count)
        {
            if (LensProjection.TryProject(
                    options,
                    "--shape",
                    rowCount: 0,
                    out var projectionExitCode))
            {
                return projectionExitCode;
            }

            if (options.JsonOutput
                && (options.Fields is { Length: > 0 }
                    || options.Columns is { Length: > 0 }))
            {
                CommandError.Write(
                    "--fields/--columns are not available with --shape, which "
                    + "renders a tree rather than projected rows. Replace "
                    + "--json --shape with --table, --tsv, or --jsonl for "
                    + "projected rows, or omit --fields/--columns to keep tree output.");
                return 1;
            }

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

        if (options is MemberOptions
            {
                MemberSourceComparison: { } comparison,
                MemberSourceDiffPresentation: null
            } sourceOptions
            && GetRequestedMemberSections(type, sourceOptions)
                .Contains(SectionNames.SourceDiff))
        {
            options = sourceOptions with
            {
                MemberSourceDiffPresentation =
                    MemberSourceDiffPresentationAdapter.Create(comparison),
            };
        }

        bool sourceDocumentJson = IsAnnotatedSourceDocumentJson(options);
        bool findingCensusJson = IsFindingCensusJson(options);
        bool barePayloadRenderer =
            options.Bare && !options.Count && !options.JsonOutput;
        string? exactSourceFailure =
            options is MemberOptions exactSourceOptions
                ? ExactSourceFailure(exactSourceOptions)
                : null;
        bool exactSourceDiffFailure =
            options is MemberOptions sourceDiffOptions
            && sourceDiffOptions.ExactIncludeSections?
                .Contains(SectionNames.SourceDiff) == true
            && exactSourceFailure is { Length: > 0 };
        if (options is MemberOptions memberOptions
            && (exactSourceDiffFailure
                || (!memberOptions.MemberHasNoBody
                    && (memberOptions.MemberSourceTooComplex
                        || memberOptions.MemberSourceCoordinatesInvalid
                        || (!memberOptions.MemberHasNoPdbDeclaration
                            && exactSourceFailure is { Length: > 0 }))))
            && !IsProjectionRequested(options)
            && !barePayloadRenderer
            && (options.Count
                || options.Tabular
                || options.JsonOutput)
            && GetRequestedMemberSections(type, options)
                .Overlaps([SectionNames.PdbSource, SectionNames.SourceDiff]))
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
                ? "Use Markdown/plaintext without --count, or replace --count with --print."
                : "Use Markdown/plaintext output, or add --print to project the section payload.";
            string failure = memberOptions.MemberSourceTooComplex
                ? "PDB source extraction stopped because the source exceeds the lexical "
                    + "complexity limit."
                : memberOptions.MemberSourceCoordinatesInvalid
                    ? "PDB source extraction stopped because the portable-PDB sequence-point "
                        + "coordinates cannot address the verified source."
                    : exactSourceFailure!;
            CommandError.Write(
                failure + $" {format} cannot represent this code-section "
                + "failure. " + guidance);
            return 1;
        }

        if (options.JsonOutput && !options.Count && !IsProjectionRequested(options)
            && !sourceDocumentJson && !findingCensusJson)
        {
            if (GetRequestedMemberSections(type, options)
                    .Contains(SectionNames.PerformanceTriage)
                && HasExplicitPerformanceTriageSelector(options))
            {
                CommandError.Write(
                    "Document --json cannot represent Performance Triage analysis. "
                    + "Use --jsonl, --tsv, --table, or --print.");
                return 1;
            }
            if (GetRequestedMemberSections(type, options)
                    .Contains(SectionNames.BodyShapes)
                && options.IncludeSections?.Contains(
                    SectionNames.BodyShapes) == true)
            {
                CommandError.Write(
                    "Document --json cannot represent Body Shapes analysis. "
                    + "Use --jsonl, --tsv, or --table.");
                return 1;
            }
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
                    if (requestedSections.Contains(SectionNames.BodyShapes))
                    {
                        ApiOutputFormatter.PopulateBodyShapes(
                            view,
                            mo4.DllPath!,
                            mo4.PdbPath,
                            methods,
                            mo4);
                    }
                    var analysisInspection = new ApiMemberAnalysisInspection(
                        mo4.DllPath!, methods, requestedSections, mo4.CallerScopeAssemblies, mo4);
                    ApiOutputFormatter.PopulateIndexSections(view, type, methods, mo4.DllPath!,
                        mo4.OverloadIndex.HasValue ? mo4.OverloadIndex.Value - 1 : null,
                        requestedSections, analysisInspection, mo4.PdbPath, mo4.IncludeSections, mo4);
                }
            }

            if (options is TypeOptions
                && options.DllPath is { } typeBodyShapeDllPath
                && GetRequestedMemberSections(type, options).Contains(SectionNames.BodyShapes))
            {
                ApiOutputFormatter.PopulateBodyShapes(
                    view,
                    typeBodyShapeDllPath,
                    options.PdbPath,
                    ApiOutputFormatter.ResolveTypeBodyShapeMethodTokens(type),
                    options);
            }

            // Type-scope analysis sections share one index build per type (built lazily, only
            // when such a section is requested) instead of opening one session per section.
            Analysis.LibraryBodyIndex? typeAnalysisIndex = null;
            Analysis.LibraryBodyIndex TypeAnalysisIndex() =>
                typeAnalysisIndex ??= ApiAnalysisInspection.OpenTypeAnalysisIndex(
                    options.DllPath!, GetRequestedMemberSections(type, options), type, options, sourceAssembly);

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
                        && ApiMemberSectionDescriptors.IsMethodLike(member)),
                    sourceAssembly);
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
                && GetRequestedMemberSections(type, mo5).Overlaps([SectionNames.PdbSource, SectionNames.SourceDiff]))
            {
                PopulatePdbSource(view, mo5);
            }

            PopulateSourceDiff(
                view,
                GetRequestedMemberSections(type, options),
                options is MemberOptions { MemberSourceTooComplex: true },
                options is MemberOptions { MemberSourceCoordinatesInvalid: true },
                (options as MemberOptions)?.MemberSourceComparison,
                (options as MemberOptions)?.MemberSourceDiffPresentation,
                options.UserVerbosity >= Verbosity.Detailed);

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
                Decompiler.AnnotatedSourceDocumentJsonContext.Default.AnnotatedSourceDocument,
                Decompiler.AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument,
                options.CompactJson);
            return 0;
        }

        if (findingCensusExplicitlySelected
            && view.MemberCode?.FindingCensus is null)
        {
            CommandError.Write(FindingCensusError(view.MemberCode));
            return 1;
        }

        if (findingCensusJson)
        {
            if (view.MemberCode?.FindingCensus is not { } findingCensus)
            {
                CommandError.Write(FindingCensusError(view.MemberCode));
                return 1;
            }

            JsonOutputHelper.Write(
                findingCensus,
                MemberFindingCensusJsonContext.Default.MemberFindingCensusEnvelope,
                MemberFindingCensusCompactJsonContext.Default.MemberFindingCensusEnvelope,
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
            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
            var schema = GetTypeDocumentSchema(options);
            var projection = CountProjectionFormatter.Capture(
                writer => ApiOutputFormatter.SerializeTypeDocument(
                    view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                    explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer),
                writerOptions);
            // A call graph declares directed edges as its row unit. The count formatter observes
            // the graph as content but deliberately does not infer rows from a rendered lowering,
            // so add the product-owned, already-windowed edge cardinality to the same projection
            // used by scalar and multi-section reductions.
            if (options.IncludeSections?.Contains(SectionNames.CallGraph) == true
                && ProjectionIncludesSection(
                    schema, SectionNames.CallGraph, options)
                && view.MemberCode?.CallGraphRowCount is { } graphRows)
            {
                projection.RecordRows(SectionNames.CallGraph, graphRows);
            }
            if (!TryReportEmptyProjection(
                    projection.WroteAnyContent,
                    options,
                    schema))
                return 1;
            var ordered = OutputFormatter.ResolveCountMapSections(
                ApiMemberSectionPipelines.Create(options),
                options.IncludeSections,
                fixedOverview: false);
            CountOutput.Write(
                projection, ordered, options.Format, options.NoHeader);
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
            writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
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
                if (SelectResolver.IsActiveAllSelector(
                    options.Select,
                    options.IncludeSections,
                    options is MemberOptions { MemberSectionsPreResolved: true }))
                {
                    var pipeline = ApiMemberSectionPipelines.Create(options);
                    writerOptions.SectionOrder = pipeline.GetAllSelectorSections(type);
                }
                else if (SelectResolver.IsActiveInfoSelector(
                    options.SelectDefault,
                    options.IncludeSections,
                    options is MemberOptions { MemberSectionsPreResolved: true }))
                {
                    var pipeline = ApiMemberSectionPipelines.Create(options);
                    writerOptions.SectionOrder = pipeline.InfoSectionNames;
                }

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
                    row.FilePath,
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
                    FilePath: row.FilePath,
                    row.Checksum,
                    row.ChecksumAlgorithm)),
                options);
        }

        var documents = section switch
        {
            SectionNames.PdbSource => CodeSectionDocument(section, SectionNames.PdbSource, MemberSourceUrl(options as MemberOptions), view.MemberCode?.PdbSourceCode.Content),
            SectionNames.DecompiledSource => CodeSectionDocument(section, "Decompiled Source", null, view.MemberCode?.DecompiledSourceCode.Content),
            SectionNames.AnnotatedSource => CodeSectionDocument(section, "Annotated Source", null, view.MemberCode?.AnnotatedSourceCode.Content),
            SectionNames.SourceDiff => CodeSectionDocument(section, "Source Diff", MemberSourceUrl(options as MemberOptions), view.MemberCode?.SourceDiffCode?.Content),
            SectionNames.IL => CodeSectionDocument(section, "IL", null, view.MemberCode?.ILCode.Content),
            _ => []
        };

        if (documents.Count == 0
            && section is not (SectionNames.SourceFiles or SectionNames.SourceLocations or SectionNames.PdbSource
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
                new ProjectionDestination(null, options.Rows)));
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
            new ShapeProjectionOptions(
                kind,
                options.PrintRow,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                new ProjectionDestination(null, options.Rows)));
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
            string? FilePath,
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
        var fetch = await PdbSourceAcquisition.AcquireVerifiedSourceTextAsync(
            fetcher,
            selectedSource.FilePath,
            rawUrl,
            selectedSource.ChecksumAlgorithm,
            selectedSource.Checksum,
            options.SourceRepositories);
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
                new ProjectionDestination(null, options.Rows)));
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
            SectionNames.FindingCensus => view.MemberCode?.FindingCensusCode.Content ?? "",
            SectionNames.CostOverlay => view.MemberCode?.CostOverlayCode.Content ?? "",
            SectionNames.SemanticsOverlay => view.MemberCode?.SemanticsOverlayCode.Content ?? "",
            SectionNames.PdbSource => view.MemberCode?.PdbSourceCode.Content ?? "",
            SectionNames.SourceDiff => view.MemberCode?.SourceDiffCode?.Content ?? "",
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
        string? SelectedTfm,
        ResolvedAssemblyReference? SourceAssembly = null);

    internal static int ExecuteEffectiveDiscovery(
        ApiType apiType, SectionPipeline<ApiType> memberPipeline, ApiOptions options,
        TypeAcquisitionContext? acquisition = null)
    {
        var fullSchema = GetTypeDocumentSchema(options);
        var filteredType = BuildFilteredTypeForSections(apiType, options);
        var effective = memberPipeline.GetDiscoverableSections(
            filteredType,
            options.IncludeSections,
            explicitInclude: options is MemberOptions { MemberSectionsPreResolved: true });
        if (!options.BodyKindQuery.HasFilter)
        {
            effective = effective
                .Where(section => !section.Equals(
                    SectionNames.BodyShapes,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
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
            sectionCategories: ApiMemberSectionPipelines.GetCategoryMap(memberPipeline),
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
                    if (requestedSections.Contains(SectionNames.BodyShapes))
                    {
                        ApiOutputFormatter.PopulateBodyShapes(
                            view,
                            memberOptions.DllPath!,
                            memberOptions.PdbPath,
                            methods,
                            memberOptions);
                    }
                    var analysisInspection = new ApiMemberAnalysisInspection(
                        memberOptions.DllPath!, methods, requestedSections,
                        memberOptions.CallerScopeAssemblies, memberOptions);
                    ApiOutputFormatter.PopulateIndexSections(view, type, methods,
                        memberOptions.DllPath!,
                        memberOptions.OverloadIndex.HasValue ? memberOptions.OverloadIndex.Value - 1 : null,
                        requestedSections, analysisInspection, memberOptions.PdbPath,
                        memberOptions.IncludeSections, memberOptions);
                }

                if (requestedSections.Overlaps([SectionNames.PdbSource, SectionNames.SourceDiff]))
                {
                    PopulatePdbSource(view, memberOptions);
                }
                PopulateSourceDiff(
                    view,
                    requestedSections,
                    memberOptions.MemberSourceTooComplex,
                    memberOptions.MemberSourceCoordinatesInvalid,
                    memberOptions.MemberSourceComparison,
                    memberOptions.MemberSourceDiffPresentation,
                    memberOptions.UserVerbosity >= Verbosity.Detailed);
            }

            if (renderOptions is TypeOptions
                && renderOptions.DllPath is { } typeBodyShapeDllPath
                && GetRequestedMemberSections(type, renderOptions).Contains(SectionNames.BodyShapes))
            {
                ApiOutputFormatter.PopulateBodyShapes(
                    view,
                    typeBodyShapeDllPath,
                    renderOptions.PdbPath,
                    ApiOutputFormatter.ResolveTypeBodyShapeMethodTokens(type),
                    renderOptions);
            }

            Analysis.LibraryBodyIndex? typeAnalysisIndex = null;
            Analysis.LibraryBodyIndex TypeAnalysisIndex() =>
                typeAnalysisIndex ??= ApiAnalysisInspection.OpenTypeAnalysisIndex(
                    renderOptions.DllPath!, GetRequestedMemberSections(type, renderOptions), type, renderOptions,
                    acquisition?.SourceAssembly);

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
                        && ApiMemberSectionDescriptors.IsMethodLike(member)),
                    acquisition?.SourceAssembly);
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

    /// <summary>
    /// Stands in for PDB Source when the selected member carries no IL body. A C# comment
    /// so it reads naturally inside the section's <c>csharp</c> fence, mirroring how
    /// <see cref="SourceTextDiffRenderer"/> reports an unavailable diff input (issue #3299).
    /// </summary>
    internal const string BodylessMemberNote =
        "// This member has no IL body, so it has no PDB source to show.";

    internal const string NoPdbDeclarationReason =
        "This member's PDB source range does not identify one declaration that can be shown.";

    internal const string NoPdbDeclarationDetail =
        "Generated members and ambiguous or structurally unknown source ranges can have this shape.";

    /// <summary>
    /// Stands in for PDB Source when the selected member has an IL body but its source range
    /// does not identify one declaration that can be shown. Generated members may map to
    /// a type header or initializer, and structurally unknown ranges are deliberately not guessed;
    /// saying so beats rendering unrelated or truncated source (issue #3299's principle, applied
    /// to a second cause).
    /// </summary>
    internal const string NoPdbDeclarationNote =
        "// " + NoPdbDeclarationReason + "\n"
        + "// " + NoPdbDeclarationDetail;

    internal const string SourceTooComplexReason =
        "PDB source extraction stopped because the source exceeds the lexical complexity limit.";

    internal const string SourceTooComplexNote =
        "// " + SourceTooComplexReason;

    internal const string SourceCoordinatesInvalidReason =
        "PDB source extraction stopped because the portable-PDB sequence-point coordinates "
        + "cannot address the verified source.";

    internal const string SourceCoordinatesInvalidNote =
        "// " + SourceCoordinatesInvalidReason;

    internal const string NoPortablePdbReason =
        "No portable PDB is available for the selected member.";

    internal const string NoPdbSourceMappingReason =
        "The selected member has no portable-PDB source mapping.";

    internal const string NoMatchingPdbSourceReason =
        "No checksum-matching PDB source could be acquired locally or through SourceLink.";

    internal const string PdbSourceInspectionFailedReason =
        "PDB source inspection failed.";

    internal static string? PdbSourceUnavailableNote(MemberOptions options) =>
        options.MemberHasNoBody
            ? BodylessMemberNote
            : options.MemberSourceTooComplex
                ? SourceTooComplexNote
                : options.MemberSourceCoordinatesInvalid
                    ? SourceCoordinatesInvalidNote
                    : options.MemberHasNoPdbDeclaration
                        ? NoPdbDeclarationNote
                        : options.PdbSourceUnavailableReason is { Length: > 0 } reason
                            ? $"// {reason}"
                            : null;

    private static void PopulatePdbSource(
        TypeView view,
        MemberOptions options)
    {
        if (PdbAttempt(options.MemberSourceComparison)
            is AssemblyMemberPdbSourceAttempt.Available available)
        {
            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.PdbSourceCode =
                new Markout.CodeSection(
                    "csharp",
                    available.Inspection.Text!);
            return;
        }

        string? note = options.MemberHasNoBody
            ? BodylessMemberNote
            : options.MemberSourceComparison is { } comparison
                ? $"// {PdbSourceUnavailableReason(comparison)}"
            : options.MethodSource is { } resolvedSource
                ? null
                : PdbSourceUnavailableNote(options);
        if (options.MethodSource is { } source
            && options.MemberSourceComparison is null)
        {
            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.PdbSourceCode =
                new Markout.CodeSection("csharp", source.SourceCode);
        }
        else if (note is not null)
        {
            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.PdbSourceCode =
                new Markout.CodeSection("csharp", note);
            view.MemberCode.PdbSourceUnavailable = true;
        }
    }

    private static void PopulateSourceDiff(
        TypeView view,
        IReadOnlySet<string> requestedSections,
        bool sourceTooComplex,
        bool sourceCoordinatesInvalid,
        AssemblyMemberSourceComparisonEntry? comparison,
        MemberSourceDiffPresentationResult? presentationResult,
        bool detailed)
    {
        if (!requestedSections.Contains(SectionNames.SourceDiff))
            return;

        view.MemberCode ??= new MemberCodeView();
        if (sourceTooComplex)
        {
            view.MemberCode.SourceDiffCode = new SourceDiffOutput(
                "PDB Source unavailable because PDB source extraction exceeded "
                + "the lexical complexity limit.");
            return;
        }
        if (sourceCoordinatesInvalid)
        {
            view.MemberCode.SourceDiffCode = new SourceDiffOutput(
                "PDB Source unavailable because portable-PDB sequence-point coordinates "
                + "cannot address the verified source.");
            return;
        }

        if (comparison is null)
        {
            view.MemberCode.SourceDiffCode = new SourceDiffOutput(
                "Member source comparison was not available.");
            return;
        }

        MemberSourceDiffPresentationResult result =
            presentationResult
            ?? MemberSourceDiffPresentationAdapter.Create(comparison);
        SourceDiffOutput diff = result switch
        {
            MemberSourceDiffPresentationResult.Available available =>
                SourceTextDiffRenderer.CreateOutput(
                    available.Presentation,
                    detailed),
            MemberSourceDiffPresentationResult.Failed failed =>
                new SourceDiffOutput(
                    $"Source diff projection failed: {failed.Failure.Detail}"),
            MemberSourceDiffPresentationResult.Unavailable unavailable =>
                new SourceDiffOutput(
                    SourceDiffUnavailableReason(unavailable.Comparison)),
            _ => throw new InvalidOperationException(
                "Unknown member source diff presentation result."),
        };

        if (PdbAttempt(comparison)
                is AssemblyMemberPdbSourceAttempt.Available pdb
            && pdb.Inspection.Document is { } document
            && document.ChecksumAlgorithm is { Length: > 0 } checksumAlgorithm
            && document.Checksum is { Length: > 0 } checksum
            && pdb.Inspection.ChecksumVerification is
                SourceChecksumVerification.Exact
                    or SourceChecksumVerification.LineEndingNormalized)
        {
            string location = CSharpText.CSharpIdentifier.ContainRenderedText(
                document.ResolvedUrl ?? document.OriginalPath);
            string algorithm = CSharpText.CSharpIdentifier.ContainRenderedText(
                checksumAlgorithm);
            string containedChecksum =
                CSharpText.CSharpIdentifier.ContainRenderedText(checksum);
            string integrity = pdb.Inspection.ChecksumVerification switch
            {
                SourceChecksumVerification.Exact =>
                    $"PDB source document bytes match portable-PDB {algorithm} checksum {containedChecksum}.",
                SourceChecksumVerification.LineEndingNormalized =>
                    $"PDB source document matches portable-PDB {algorithm} checksum {containedChecksum} "
                    + "after CR/LF normalization.",
                _ => throw new InvalidOperationException("Checksum evidence requires a successful verification."),
            };
            diff = diff.WithMetadata(
                new Markout.MarkoutField("PDB source", location),
                new Markout.MarkoutField("Integrity", integrity));
        }

        view.MemberCode.SourceDiffCode = diff;
    }

    private static AssemblyMemberPdbSourceAttempt? PdbAttempt(
        AssemblyMemberSourceComparisonEntry? comparison)
        => comparison switch
        {
            AssemblyMemberSourceComparisonEntry.Available available =>
                available.Pdb,
            AssemblyMemberSourceComparisonEntry.Unavailable unavailable =>
                unavailable.Pdb,
            _ => null,
        };

    internal static string SourceDiffUnavailableReason(
        AssemblyMemberSourceComparisonEntry comparison)
        => comparison switch
        {
            AssemblyMemberSourceComparisonEntry.Available available =>
                available.Pdb
                    is AssemblyMemberPdbSourceAttempt.Unavailable
                    ? $"Source diff unavailable: PDB comparison unavailable: "
                        + $"{StatusReason(PdbAttemptReason(available.Pdb))}."
                    : $"Source diff unavailable: Decompiled comparison unavailable: "
                        + $"{StatusReason(DecompilerAttemptReason(available.Decompiled))}.",
            AssemblyMemberSourceComparisonEntry.Unavailable unavailable =>
                $"Source diff unavailable: PDB comparison unavailable: "
                + $"{StatusReason(PdbAttemptReason(unavailable.Pdb))}; "
                + "Decompiled comparison unavailable: "
                + $"{StatusReason(DecompilerAttemptReason(unavailable.Decompiled))}.",
            AssemblyMemberSourceComparisonEntry.NotFound notFound =>
                $"Source diff unavailable: {notFound.Failure.Detail}",
            AssemblyMemberSourceComparisonEntry.Failed failed =>
                $"Source diff unavailable: {failed.Failure.Detail}",
            AssemblyMemberSourceComparisonEntry.Rejected =>
                "Source diff unavailable because the selected assembly image was rejected.",
            _ => throw new InvalidOperationException(
                "Unknown member source comparison result."),
        };

    private static string? ExactSourceFailure(
        MemberOptions options)
    {
        if (options.ExactIncludeSections?
                .Contains(SectionNames.SourceDiff) == true)
        {
            return options.MemberSourceDiffPresentation switch
            {
                MemberSourceDiffPresentationResult.Available => null,
                MemberSourceDiffPresentationResult.Failed failed =>
                    $"Source diff projection failed: {failed.Failure.Detail}",
                MemberSourceDiffPresentationResult.Unavailable unavailable =>
                    SourceDiffUnavailableReason(unavailable.Comparison),
                null => options.PdbSourceUnavailableReason,
                _ => throw new InvalidOperationException(
                    "Unknown member source diff presentation result."),
            };
        }

        return options.ExactIncludeSections?
                .Contains(SectionNames.PdbSource) == true
            ? options.PdbSourceUnavailableReason
            : null;
    }

    private static string StatusReason(string reason)
    {
        string[] lines = reason
            .ReplaceLineEndings("\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);
        return string.Join(
                " ",
                lines.Select(line => line.TrimStart('/', ' ')))
            .TrimEnd('.');
    }

    internal static string PdbSourceUnavailableReason(
        AssemblyMemberSourceComparisonEntry comparison)
        => PdbAttempt(comparison) is { } attempt
            ? PdbAttemptReason(attempt)
            : comparison switch
            {
                AssemblyMemberSourceComparisonEntry.NotFound notFound =>
                    notFound.Failure.Detail,
                AssemblyMemberSourceComparisonEntry.Failed failed =>
                    failed.Failure.Detail,
                AssemblyMemberSourceComparisonEntry.Rejected =>
                    "The selected assembly image was rejected.",
                _ => "PDB source is unavailable.",
            };

    private static string? MemberSourceUrl(MemberOptions? options)
        => PdbAttempt(options?.MemberSourceComparison) switch
        {
            AssemblyMemberPdbSourceAttempt.Available
            {
                Inspection.Document: { } document
            } => document.ResolvedUrl ?? document.OriginalPath,
            _ => options?.MethodSource?.SourceUrl,
        };

    private static string PdbAttemptReason(
        AssemblyMemberPdbSourceAttempt attempt)
        => attempt switch
        {
            AssemblyMemberPdbSourceAttempt.Available =>
                "PDB comparison is available",
            AssemblyMemberPdbSourceAttempt.Unavailable unavailable =>
                unavailable.Inspection.Outcome switch
                {
                    PdbMemberSourceOutcome.PortablePdbUnavailable =>
                        NoPortablePdbReason,
                    PdbMemberSourceOutcome.PortablePdbAcquisitionFailed =>
                        "Portable PDB acquisition failed.",
                    PdbMemberSourceOutcome.SourceMappingUnavailable =>
                        NoPdbSourceMappingReason,
                    PdbMemberSourceOutcome.NoVouchedDeclaration =>
                        NoPdbDeclarationReason + " "
                            + NoPdbDeclarationDetail,
                    PdbMemberSourceOutcome.SourceTooComplex =>
                        SourceTooComplexReason,
                    PdbMemberSourceOutcome.InvalidSequencePointCoordinates =>
                        SourceCoordinatesInvalidReason,
                    PdbMemberSourceOutcome.SourceExtractionFailed
                        or PdbMemberSourceOutcome.InspectionFailed =>
                        PdbSourceInspectionFailedReason,
                    _ => NoMatchingPdbSourceReason,
                },
            _ => throw new InvalidOperationException(
                "Unknown PDB source attempt."),
        };

    private static string DecompilerAttemptReason(
        AssemblyMemberDecompiledSourceAttempt attempt)
        => attempt switch
        {
            AssemblyMemberDecompiledSourceAttempt.Available =>
                "available",
            AssemblyMemberDecompiledSourceAttempt.Unavailable unavailable =>
                unavailable.Status == Decompiler.MemberBodyProductionStatus.Absent
                    ? "the member has no renderable body"
                    : "decompilation failed",
            _ => throw new InvalidOperationException(
                "Unknown decompiled source attempt."),
        };

    private static void WriteJsonTypeOutput(ApiType type, ApiOptions options)
    {
        var outputType = type;
        var members = type.Members;

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter)).ToList();

        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind)).ToList();

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe).ToList();

        if (options.Limit.HasValue && members.Count > options.Limit.Value)
            members = members
                .OrderBy(m => ApiOutputFormatter.GetMemberSortOrder(m.Kind))
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .ThenBy(ApiOutputFormatter.GetMemberSignatureSortKey, StringComparer.Ordinal)
                .Take(options.Limit.Value)
                .ToList();

        // -S/--select scopes JSON to the requested sections, mirroring the markdown view.
        if (options.IncludeSections is { } sections
            && (sections.Count > 0
                || options is MemberOptions { MemberSectionsPreResolved: true }))
        {
            outputType = ProjectTypeToSections(type, members, sections);
        }

        else if (members != type.Members)
        {
            outputType = new ApiType
            {
                Namespace = type.Namespace,
                Name = type.Name,
                MetadataName = type.MetadataName,
                DefinitionName = type.DefinitionName,
                IntroducedTypeParameterCounts =
                    type.IntroducedTypeParameterCounts,
                Kind = type.Kind,
                Layout = type.Layout,
                MemorySafety = type.MemorySafety,
                IsSealed = type.IsSealed,
                IsAbstract = type.IsAbstract,
                IsStatic = type.IsStatic,
                BaseType = type.BaseType,
                Interfaces = type.Interfaces,
                Members = members,
                SourceFilePath = type.SourceFilePath,
                SourceUrl = type.SourceUrl,
                GitHubBrowseUrl = type.GitHubBrowseUrl,
                SourceLineNumber = type.SourceLineNumber,
                SourceChecksum = type.SourceChecksum,
                SourceChecksumAlgorithm = type.SourceChecksumAlgorithm,
                AdditionalSourceFiles = type.AdditionalSourceFiles,
                Documentation = type.Documentation
            };
        }

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
           && HasExplicitAnnotatedSourceDocumentSelector(options)
           && (sections.Count != 1
               || !HasOnlyExplicitAnnotatedSourceDocumentSelectors(options));

    private static bool HasOnlyExplicitAnnotatedSourceDocumentSelectors(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections is { Count: 1 } exactSections
              && exactSections.Contains(SectionNames.AnnotatedSourceDocument)
            : options.Select is { Length: > 0 } selectors
              && selectors.All(IsExplicitAnnotatedSourceDocumentSelector);

    private static bool HasExplicitAnnotatedSourceDocumentSelector(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections?.Contains(
                SectionNames.AnnotatedSourceDocument) == true
            : options.Select?.Any(IsExplicitAnnotatedSourceDocumentSelector) == true;

    private static bool IsExplicitAnnotatedSourceDocumentSelector(string selector)
        => selector.Equals(
            SectionNames.AnnotatedSourceDocument,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsFindingCensusJson(ApiOptions options)
        => options.JsonOutput
           && !options.Count
           && !IsProjectionRequested(options)
           && !IsColumnProjectionRequested(options)
           && options.Limit is null
           && !IsLineLimitRequested()
           && options.Rows is null
           && options.IncludeSections is { Count: 1 } sections
           && sections.Contains(SectionNames.FindingCensus)
           && HasOnlyExplicitFindingCensusSelectors(options);

    private static bool IsInvalidFindingCensusJsonSelection(ApiOptions options)
        => options.JsonOutput
           && options.IncludeSections is { Count: > 0 } sections
           && sections.Contains(SectionNames.FindingCensus)
           && HasExplicitFindingCensusSelector(options)
           && (sections.Count != 1
               || !HasOnlyExplicitFindingCensusSelectors(options));

    private static bool IsInvalidFindingCensusProjection(ApiOptions options)
        => options.IncludeSections?.Contains(SectionNames.FindingCensus) == true
           && HasExplicitFindingCensusSelector(options)
           && (options.Count
               || options.Tabular
               || options.Tsv
               || options.Jsonl
               || IsProjectionRequested(options)
               || IsColumnProjectionRequested(options)
               || options.Limit is not null
               || IsLineLimitRequested()
               || options.Rows is not null);

    private static bool IsLineLimitRequested()
        => ArgumentPreprocessor.HeadLines is not null
           || ArgumentPreprocessor.TailLines is not null;

    private static bool HasOnlyExplicitFindingCensusSelectors(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections is { Count: 1 } exactSections
              && exactSections.Contains(SectionNames.FindingCensus)
            : options.Select is { Length: > 0 } selectors
              && selectors.All(IsExplicitFindingCensusSelector);

    private static bool HasExplicitFindingCensusSelector(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections?.Contains(SectionNames.FindingCensus) == true
            : options.Select?.Any(IsExplicitFindingCensusSelector) == true;

    private static bool IsExplicitFindingCensusSelector(string selector)
        => selector.Equals(
            SectionNames.FindingCensus,
            StringComparison.OrdinalIgnoreCase);

    private static bool HasExplicitPerformanceTriageSelector(ApiOptions options)
        => options is MemberOptions { MemberSectionsPreResolved: true }
            ? options.ExactIncludeSections?.Contains(
                SectionNames.PerformanceTriage) == true
            : options.Select?.Any(static selector =>
                   selector.Equals(
                       SectionNames.PerformanceTriage,
                       StringComparison.OrdinalIgnoreCase)
                   || selector.Equals(
                       "Optimization Opportunities",
                       StringComparison.OrdinalIgnoreCase)) == true;

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

    internal static string FindingCensusError(MemberCodeView? memberCode)
        => memberCode?.FindingCensusFailure
            ?? $"section '{SectionNames.FindingCensus}' produced no payload.";

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
            [SectionNames.Operators] = m => m.Kind == "operator",
            [SectionNames.ExplicitInterfaceImplementations] = m => m.Kind == "explicit-interface-implementation",
            [SectionNames.ExtensionMethods] = m => m.Kind == "extension-method",
            [SectionNames.Constructors] = m => m.Kind == "constructor",
            [SectionNames.Finalizer] = m => m.Kind == "finalizer",
            [SectionNames.Events] = m => m.Kind == "event",
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

        return new ApiType
        {
            Namespace = type.Namespace,
            Name = type.Name,
            MetadataName = type.MetadataName,
            DefinitionName = type.DefinitionName,
            IntroducedTypeParameterCounts =
                type.IntroducedTypeParameterCounts,
            Kind = type.Kind,
            Layout = type.Layout,
            MemorySafety = type.MemorySafety,
            IsSealed = type.IsSealed,
            IsAbstract = type.IsAbstract,
            IsStatic = type.IsStatic,
            BaseType = sections.Contains(SectionNames.Baseclass) && IsRenderableBaseType(type.BaseType) ? type.BaseType : null,
            Interfaces = sections.Contains(SectionNames.TypeInterfaces) ? type.Interfaces : [],
            TypeParameters = sections.Contains(SectionNames.TypeParameters) ? type.TypeParameters : [],
            Members = scopedMembers,
            SourceFilePath = type.SourceFilePath,
            SourceUrl = type.SourceUrl,
            GitHubBrowseUrl = type.GitHubBrowseUrl,
            SourceLineNumber = type.SourceLineNumber,
            SourceChecksum = type.SourceChecksum,
            SourceChecksumAlgorithm = type.SourceChecksumAlgorithm,
            AdditionalSourceFiles = type.AdditionalSourceFiles,
            Documentation = type.Documentation
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
