using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Net;
using DotnetInspect.Cli.CommandLine;
using CSharpText.MemberSlicing;
using DotnetInspect.Cli.Inspectors;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using Markout;
using Markout.Formatting;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;

using Decompiler = ILInspector.Decompiler;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Shared helpers for type and member commands.
/// </summary>
public partial class ApiCommand
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

        if (options.CountDefaultPopulation)
            options = LowerDefaultCountPopulation(options, singleTypeMode: false);

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
                "Use -S <Section> to select one, -D to discover what is available, or -S @Surface for the type-list surface.");
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
            || options.SelectDefault
            || options.CountDefaultPopulation;
        if (hasCatalogDependentSelection)
        {
            if (listingOptions.Discover == null && listingOptions.Count
                && (!CountOutput.ValidateSectionsSelected(
                        listingOptions.IncludeSections, fixedOverview: false)
                    || !CountOutput.ValidateMapFormat(
                        listingOptions.Format,
                        listingOptions.CountDefaultPopulation
                            ? null
                            : OutputFormatter.ResolveCountMapSections(
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
        if (options is TypeOptions
            {
                Count: true,
                Discover: null,
                Select: null,
                SelectDefault: false,
                IncludeSections: null,
            } typeCountOptions
            && !typeCountOptions.BodyKindQuery.HasFilter
            && !typeCountOptions.PerformanceTriage.HasFilters
            && !typeCountOptions.CloneCandidateQuery.HasPredicates)
        {
            options = LowerDefaultCountPopulation(
                typeCountOptions,
                singleTypeMode);
        }
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
                $"Use -S <Section> to select one, -D to discover what is available, or -S {SectionCategoryNames.Member} for the ordinary member view.");
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
        foreach (string section in
                 ApiMemberSectionPipelines.GetExactOnlySections(options))
        {
            (options, string? selectionError) =
                NormalizeExactOnlySectionSelection(
                    options,
                    memberPipeline.SelectableSectionNames,
                    section);
            if (selectionError is not null)
            {
                CommandError.Write(selectionError);
                return (null!, 1);
            }
        }
        if (options is
            {
                BodyKindQuery.HasFilter: true,
                Select: null,
                SelectDefault: false,
                Discover: null or { Length: 0 },
                IncludeSections: null,
            })
        {
            var bodyShapeSelection = new SelectResult(
                new HashSet<string>(
                    [SectionNames.BodyShapes],
                    StringComparer.OrdinalIgnoreCase),
                []);
            if (ApplyBodyShapeSelectionRequirements(
                    options,
                    bodyShapeSelection) is { } bodyShapeError)
            {
                CommandError.Write(bodyShapeError);
                return (null!, 1);
            }
            options = options with
            {
                IncludeSections = bodyShapeSelection.Sections,
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
        var countMapSections =
            options is TypeOptions { CountDefaultPopulation: true }
                ? null
                : singleTypeMode
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

    private static TypeOptions LowerDefaultCountPopulation(
        TypeOptions options,
        bool singleTypeMode)
    {
        var sections = new HashSet<string>(
            singleTypeMode
                ? [SectionNames.MemberIndex]
                : ApiTypeSectionDescriptors.FindingSectionNames,
            StringComparer.OrdinalIgnoreCase);
        return options with
        {
            IncludeSections = sections,
            ExactIncludeSectionsOverride = new HashSet<string>(
                sections,
                StringComparer.OrdinalIgnoreCase),
            CountDefaultPopulation = true,
        };
    }

    private static (ApiOptions Options, string? Error) NormalizeExactOnlySectionSelection(
        ApiOptions options,
        IReadOnlyList<string> memberSections,
        string section)
    {
        var normalized = SelectResolver.NormalizeExactOnlySection(
            options.Select,
            options.IncludeSections,
            options.ExactIncludeSections,
            memberSections,
            section);
        return normalized.Error is null
            ? (options with { IncludeSections = normalized.Sections }, null)
            : (options, normalized.Error);
    }

    internal static string? ApplyBodyShapeSelectionRequirements(
        ApiOptions options,
        SelectResult selectResult)
    {
        if (selectResult.Sections is not { } sections)
            return options.BodyKindQuery.HasFilter
                && options.Select is { Length: > 0 }
                ? $"--where Kind=... targets section '{SectionNames.BodyShapes}' "
                    + $"or '{SectionNames.BodyShapeSummary}'."
                : null;

        bool selected = BodyKindQueryOptions.IsSelected(sections);
        if (options.BodyKindQuery.HasFilter)
        {
            return selected
                ? null
                : $"--where Kind=... targets section '{SectionNames.BodyShapes}' "
                    + $"or '{SectionNames.BodyShapeSummary}'. Omit -S or select one of these sections.";
        }

        if (!selected)
            return null;

        string required =
            $"Section '{sections.First(section => BodyKindQueryOptions.Sections.Contains(
                section, StringComparer.OrdinalIgnoreCase))}' "
            + "requires --where \"Kind=<C# Body Kinds ID>\".";
        bool explicitlyTargetsBodyShapes =
            options is MemberOptions { MemberSectionsPreResolved: true }
                ? BodyKindQueryOptions.IsSelected(selectResult.ExactSections)
                : TargetsBodyShapes(options, options.Select);
        if (explicitlyTargetsBodyShapes
            || options.EffectiveDiscovery && TargetsBodyShapes(options, options.Discover)
            || sections.All(section => BodyKindQueryOptions.Sections.Contains(
                section, StringComparer.OrdinalIgnoreCase)))
        {
            return required;
        }

        sections.ExceptWith(BodyKindQueryOptions.Sections);
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
                && resolved.Sections is { Count: > 0 } sections
                && sections.All(section => BodyKindQueryOptions.Sections.Contains(
                    section, StringComparer.OrdinalIgnoreCase)))
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
        SurfaceSubjects? materialized =
            filtersConstraintSubjects
                ? CaptureSurfaceSubjects(api.Types)
                : null;

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

        if (materialized is not null)
            ReprojectSurfaceFailures(api, materialized);
    }

    private static SurfaceSubjects CaptureSurfaceSubjects(
        IReadOnlyList<ApiType> types)
    {
        var definitions =
            new HashSet<MetadataTypeDefinitionName>();
        var tokens = new HashSet<int>();
        var subjects =
            new HashSet<ApiSurfaceInspectionSubject>();
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

        return new SurfaceSubjects(
            definitions,
            tokens,
            subjects);

        void Add(string? path, int? token)
        {
            if (token is not int value)
                return;

            tokens.Add(value);
            subjects.Add(
                new ApiSurfaceInspectionSubject(path, value));
        }
    }

    private static void ReprojectSurfaceFailures(
        ApiSurface surface,
        SurfaceSubjects materialized)
    {
        SurfaceSubjects retained =
            CaptureSurfaceSubjects(surface.Types);
        surface.ReprojectConstraintResolutionFailures(
            subject =>
                retained.Tokens.Contains(subject.SubjectToken)
                && (subject.SourceAssemblyPath is null
                    || retained.Subjects.Contains(subject)
                    || retained.Subjects.Contains(
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
                return materialized.Definitions.Contains(owner)
                    && !retained.Definitions.Contains(owner);
            }
            if (!failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
            {
                if (failure.AffectedTypeDefinitions.Any(
                        retained.Definitions.Contains))
                {
                    return false;
                }

                return failure.AffectedTypeDefinitions.All(
                    materialized.Definitions.Contains);
            }
            if (failure.OwningTypeToken is not int token)
                return false;

            return Includes(materialized, token, failure.SourceAssemblyPath)
                && !Includes(retained, token, failure.SourceAssemblyPath);
        }

        static bool Includes(
            SurfaceSubjects subjects,
            int token,
            string? path) =>
            subjects.Tokens.Contains(token)
            && (path is null
                || subjects.Subjects.Contains(
                    new ApiSurfaceInspectionSubject(
                        path,
                        token))
                || subjects.Subjects.Contains(
                    new ApiSurfaceInspectionSubject(
                        null,
                        token)));
    }

    private sealed record SurfaceSubjects(
        HashSet<MetadataTypeDefinitionName> Definitions,
        HashSet<int> Tokens,
        HashSet<ApiSurfaceInspectionSubject> Subjects);

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
        if (BodyKindQueryOptions.IsSelected(options.IncludeSections))
        {
            var bodyFiltered = BuildFilteredTypeForBodyShapes(type, options);
            var (emptyBodySections, _) = pipeline.GetEmptySections(
                bodyFiltered,
                options.Verbosity,
                options.IncludeSections);
            empty.RemoveAll(BodyKindQueryOptions.Sections.Contains);
            empty.AddRange(emptyBodySections.Where(BodyKindQueryOptions.Sections.Contains));
        }

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
        => BuildFilteredType(type, options, excludeCompilerGeneratedNames: true);

    internal static ApiType BuildFilteredTypeForBodyShapes(ApiType type, ApiOptions options)
        => BuildFilteredType(type, options, excludeCompilerGeneratedNames: false);

    private static ApiType BuildFilteredType(
        ApiType type,
        ApiOptions options,
        bool excludeCompilerGeneratedNames)
    {
        IEnumerable<ApiMember> members = type.Members;
        if (excludeCompilerGeneratedNames)
            members = members.Where(m => !MemberFilters.IsCompilerGenerated(m.Name));

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
        LibraryCommand.AddCloneCandidateSchema(detailSchema);
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
            initialSourceAssembly: null,
            acquisitionSourceAssembly: null,
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
            initialSourceAssembly: sourceAssembly,
            acquisitionSourceAssembly: sourceAssembly,
            options,
            logger,
            httpClient,
            cancellationToken,
            fallbackPackageName,
            fallbackPackageVersion).ConfigureAwait(false);

    /// <summary>
    /// Reads embedded or adjacent PDB evidence from the selected path, then uses the selected
    /// supplier only when external PDB acquisition is required.
    /// </summary>
    internal static async Task<string?> TryAcquirePdbPathFromSelectedPathAsync(
        string dllPath,
        ResolvedAssemblyReference acquisitionSourceAssembly,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken = default,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
        => await TryAcquirePdbPathCoreAsync(
            dllPath,
            initialSourceAssembly: null,
            acquisitionSourceAssembly,
            options,
            logger,
            httpClient,
            cancellationToken,
            fallbackPackageName,
            fallbackPackageVersion).ConfigureAwait(false);

    static async Task<string?> TryAcquirePdbPathCoreAsync(
        string dllPath,
        ResolvedAssemblyReference? initialSourceAssembly,
        ResolvedAssemblyReference? acquisitionSourceAssembly,
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
            using var service = initialSourceAssembly is null
                ? SourceLinkService.Open(dllPath, logger.Log)
                : SourceLinkService.Open(initialSourceAssembly, logger.Log);
            var context = service.Context;
            if (context.NeedsPdb)
            {
                var (pkgName, pkgVersion) = !string.IsNullOrEmpty(options.PackagePath)
                    ? PackageExtractor.ParsePackageReference(options.PackagePath)
                    : (null, null);
                pkgName = fallbackPackageName ?? pkgName;
                pkgVersion = fallbackPackageVersion ?? pkgVersion;
                if (acquisitionSourceAssembly is null)
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
                        acquisitionSourceAssembly,
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
        catch when (acquisitionSourceAssembly is null)
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
                categories,
                exactOnlySections:
                    ApiMemberSectionPipelines.GetExactOnlySections(options));
            var discoveredSections = new HashSet<string>(
                resolved.Sections ?? [],
                StringComparer.OrdinalIgnoreCase);
            if (!options.BodyKindQuery.HasFilter)
                discoveredSections.ExceptWith(BodyKindQueryOptions.Sections);
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

}
