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
        ResolvedAssemblyReference? SourceAssembly = null,
        ResolvedAssemblyReference? MemberCodeSourceAssembly = null);

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
        ApiType? bodyFilteredType = null;
        if (options.BodyKindQuery.HasFilter)
        {
            bodyFilteredType = BuildFilteredTypeForBodyShapes(apiType, options);
            var bodyEffective = memberPipeline.GetDiscoverableSections(
                    bodyFilteredType,
                    options.IncludeSections,
                    explicitInclude: options is MemberOptions { MemberSectionsPreResolved: true })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var normallyEffective = effective.ToHashSet(StringComparer.OrdinalIgnoreCase);
            effective = memberPipeline.SelectableSectionNames
                .Where(section => BodyKindQueryOptions.Sections.Contains(
                        section, StringComparer.OrdinalIgnoreCase)
                    ? bodyEffective.Contains(section)
                    : normallyEffective.Contains(section))
                .ToList();
        }
        else
        {
            effective = effective
                .Where(section => !BodyKindQueryOptions.Sections.Contains(
                    section, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        effective = DiscoverOutput.RestrictToSchemaSections(effective, fullSchema);
        var unprobed = memberPipeline.GetUnprobedSections();
        var bareDiscover = options.Discover is null or { Length: 0 };
        var discoveryRenderSections = bareDiscover
            ? options.BodyKindQuery.HasFilter
                ? effective
                : options is MemberOptions { OverloadIndex: not null }
                ? [.. effective.Where(s => !unprobed.Contains(s))]
                : [.. effective.Where(memberPipeline.GetCostAnnotations().ContainsKey)]
            : [.. GetRequestedMemberSections(filteredType, options)
                .Where(section => !unprobed.Contains(section))];
        var renderManifest = BuildTypeRenderManifest(filteredType, options, discoveryRenderSections, acquisition);
        if (bodyFilteredType is not null)
        {
            var bodyRenderManifest = BuildTypeRenderManifest(
                bodyFilteredType,
                options,
                discoveryRenderSections,
                acquisition);
            renderManifest.ReplaceSectionsFrom(
                bodyRenderManifest,
                BodyKindQueryOptions.Sections);
        }
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
        IReadOnlySet<string> catalogHiddenSections =
            memberPipeline.GetCatalogHiddenSections();
        if (options.IncludeSections is { Count: > 0 })
        {
            catalogHiddenSections = catalogHiddenSections
                .Where(section =>
                    !options.IncludeSections.Contains(section))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        var schema = DiscoverOutput.FilterSchemaToRenderedColumns(
            queryEffective, fullSchema, renderManifest, TypeFieldLayoutSections);
        return DiscoverOutput.ExecuteEffective(options.Discover, queryEffective, schema,
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
                (int)options.Verbosity,
                options),
            fullSchema: fullSchema,
            sectionCostAnnotations: displayAnnotations,
            sectionCategories: ApiMemberSectionPipelines.GetCategoryMap(memberPipeline),
            catalogHiddenSections: catalogHiddenSections,
            listedCategoryDoors: memberPipeline.GetListedCategoryDoors(),
            exactOnlySections:
                ApiMemberSectionPipelines.GetExactOnlySections(options));
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
                    if (BodyKindQueryOptions.IsSelected(requestedSections))
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
                        memberOptions.IncludeSections, memberOptions,
                        acquisition?.MemberCodeSourceAssembly);
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
                && BodyKindQueryOptions.IsSelected(GetRequestedMemberSections(type, renderOptions)))
            {
                ApiOutputFormatter.PopulateBodyShapes(
                    view,
                    typeBodyShapeDllPath,
                    renderOptions.PdbPath,
                    ApiOutputFormatter.ResolveTypeBodyShapeMethodTokens(
                        BuildFilteredTypeForBodyShapes(type, renderOptions)),
                    renderOptions,
                    acquisition?.SourceAssembly);
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
                    renderOptions.PerformanceTriage,
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

            if (renderOptions.DllPath is not null
                && SectionNames.IncludesBodyMetrics(
                    GetRequestedMemberSections(type, renderOptions)))
            {
                bool restrictImplementationProfiles =
                    ApiMemberSectionPipelines
                        .UsesDetailPipeline(renderOptions)
                    || ApiMemberSectionPipelines
                        .UsesOverloadInventoryPipeline(
                            renderOptions);
                ApiOutputFormatter.PopulateImplementationProfiles(
                    view,
                    restrictImplementationProfiles
                        ? BuildFilteredTypeForBodyShapes(
                            type,
                            renderOptions)
                        : type,
                    TypeAnalysisIndex(),
                            renderOptions is MemberOptions,
                            restrictToModelMembers:
                        restrictImplementationProfiles,
                    selectedMethodToken:
                        (renderOptions as MemberOptions)?
                            .SelectedBodyMethodToken);
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

}
