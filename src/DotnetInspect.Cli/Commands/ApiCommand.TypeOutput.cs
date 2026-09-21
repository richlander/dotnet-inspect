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

    internal static async Task<int> WriteTypeOutputAsync(
        ApiType type,
        string? foundIn,
        string? packageName,
        string? packageVersion,
        string? apiSource,
        string? selectedTfm,
        ApiOptions options,
        TextWriter? output = null,
        ResolvedAssemblyReference? sourceAssembly = null,
        ResolvedAssemblyReference? memberCodeSourceAssembly = null,
        HttpClient? sourceClient = null)
    {
        var sink = output ?? Console.Out;

        if (options is MemberOptions partsOptions && MemberSourcePartsOutput.Handles(partsOptions))
        {
            return await MemberSourcePartsOutput.WriteAsync(
                type, partsOptions, memberCodeSourceAssembly ?? sourceAssembly,
                packageName, packageVersion,
                sourceClient ?? DotnetInspector.Networking.HttpClientFactory.Shared, sink);
        }

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
        if (IsInvalidFactsJsonSelection(options))
        {
            CommandError.Write(
                $"section '{SectionNames.Facts}' must be the only selected section under unprojected --json.");
            return 1;
        }
        if (IsInvalidFactsJsonWindow(options))
        {
            CommandError.Write(
                $"section '{SectionNames.Facts}' exact --json is a complete typed document; "
                + "use --table, --tsv, --jsonl, or an explicit field/column projection for row shaping.");
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
                    "type tree output",
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
                    "--fields/--columns are not available with type tree output, "
                    + "which renders a tree rather than projected rows. Use "
                    + "--table, --tsv, or --jsonl for "
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
        bool factsJson = IsFactsJson(options);
        bool projectedFactsJson = IsProjectedFactsJson(options);
        bool callsJson = IsCallsJson(options);
        bool callersJson = IsCallersJson(options);
        bool typeApiDeclarationsJson =
            options.JsonOutput
            && !options.Count
            && !IsProjectionRequested(options)
            && options is TypeOptions
            {
                IncludeSections: { Count: 1 },
                TypeApiDeclarationInspection: not null,
            }
            && options.IncludeSections.Contains(
                SectionNames.ApiDeclarations);
        bool nativePayloadRenderer =
            options.UsesNativePayloadDefault;
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
            && !nativePayloadRenderer
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
            && !typeApiDeclarationsJson
            && !sourceDocumentJson && !findingCensusJson && !factsJson
            && !projectedFactsJson && !callsJson && !callersJson)
        {
            if (SectionNames.IncludesBodyMetrics(
                    GetRequestedMemberSections(type, options)))
            {
                CommandError.Write(
                    $"Document --json cannot represent "
                    + $"{(options is MemberOptions ? SectionNames.MemberMetrics : SectionNames.TypeMetrics)} analysis. "
                    + "Use --jsonl, --tsv, or --table.");
                return 1;
            }
            if (GetRequestedMemberSections(type, options)
                    .Contains(SectionNames.PerformanceTriage)
                && HasExplicitPerformanceTriageSelector(options))
            {
                CommandError.Write(
                    "Document --json cannot represent Performance Triage analysis. "
                    + "Use --jsonl, --tsv, --table, or --print.");
                return 1;
            }
            if (BodyKindQueryOptions.IsSelected(GetRequestedMemberSections(type, options))
                && BodyKindQueryOptions.IsSelected(options.IncludeSections))
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

        if (typeApiDeclarationsJson)
        {
            InspectionEnvelope<TypeApiDeclarationResult> inspection =
                ((TypeOptions)options).TypeApiDeclarationInspection!;
            JsonOutputHelper.Write(
                inspection,
                TypeApiDeclarationInspectionJsonContext.Default
                    .InspectionEnvelopeTypeApiDeclarationResult,
                TypeApiDeclarationInspectionJsonContext.Default
                    .InspectionEnvelopeTypeApiDeclarationResult,
                options.CompactJson);
            return inspection.Content.Outcome
                == TypeApiDeclarationOutcome.Available
                    ? 0
                    : 1;
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
                    if (BodyKindQueryOptions.IsSelected(requestedSections))
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
                        requestedSections, analysisInspection, mo4.PdbPath,
                        mo4.IncludeSections, mo4,
                        memberCodeSourceAssembly);
                }
            }

            if (options is TypeOptions
                && options.DllPath is { } typeBodyShapeDllPath
                && BodyKindQueryOptions.IsSelected(GetRequestedMemberSections(type, options)))
            {
                ApiOutputFormatter.PopulateBodyShapes(
                    view,
                    typeBodyShapeDllPath,
                    options.PdbPath,
                    ApiOutputFormatter.ResolveTypeBodyShapeMethodTokens(
                        BuildFilteredTypeForBodyShapes(type, options)),
                    options,
                    sourceAssembly);
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

            if (options.DllPath is not null
                && SectionNames.IncludesBodyMetrics(
                    GetRequestedMemberSections(type, options)))
            {
                bool restrictImplementationProfiles =
                    ApiMemberSectionPipelines
                        .UsesDetailPipeline(options)
                    || ApiMemberSectionPipelines
                        .UsesOverloadInventoryPipeline(options);
                ApiOutputFormatter.PopulateImplementationProfiles(
                    view,
                    restrictImplementationProfiles
                        ? BuildFilteredTypeForBodyShapes(
                            type,
                            options)
                        : type,
                    TypeAnalysisIndex(),
                            options is MemberOptions,
                            restrictToModelMembers:
                        restrictImplementationProfiles,
                    selectedMethodToken:
                        (options as MemberOptions)?
                            .SelectedBodyMethodToken);
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

        if (options is MemberOptions
            {
                CallRowSelection: { } callRowSelection,
            })
        {
            List<CallSiteRow> callRows =
                view.MemberCode?.CallRows ?? [];
            if (!CliSemanticRowSelection.TrySelect(
                    callRowSelection,
                    callRows,
                    "Member Calls",
                    failure =>
                        $"Member Calls row selection stage "
                        + $"{failure.Failure.StageNumber} requires call row "
                        + $"{failure.Failure.RequiredPosition}, but only "
                        + $"{failure.Failure.AvailableCount} call rows are "
                        + "available.",
                    out IReadOnlyList<CallSiteRow> selectedCallRows))
            {
                return 1;
            }

            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.CallRows = [.. selectedCallRows];
        }

        if (options is MemberOptions
            {
                CallerRowSelection: { } callerRowSelection,
            })
        {
            List<CallerSiteRow> callerRows =
                view.MemberCode?.CallerRows ?? [];
            bool sourceColumnRequired =
                !MemberCodeView.CallerSourceIsUniform(callerRows);
            if (!CliSemanticRowSelection.TrySelect(
                    callerRowSelection,
                    callerRows,
                    "Member Callers",
                    failure =>
                        $"Member Callers row selection stage "
                        + $"{failure.Failure.StageNumber} requires caller row "
                        + $"{failure.Failure.RequiredPosition}, but only "
                        + $"{failure.Failure.AvailableCount} caller rows are "
                        + "available.",
                    out IReadOnlyList<CallerSiteRow> selectedCallerRows))
            {
                return 1;
            }

            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.CallerRows = sourceColumnRequired
                ? [.. selectedCallerRows.Select(
                    row => row with { SourceColumnRequired = true })]
                : [.. selectedCallerRows];
        }

        if (options is MemberOptions
            {
                MemberHasNoBody: true,
                DllPath: { } memberDllPath,
                OverloadIndex: not null,
            }
            && type.Members.Count == 1
            && GetRequestedMemberSections(type, options)
                .Contains(SectionNames.DecompiledSource))
        {
            var resolver = ApiAnalysisInspection.CreateReferenceResolver(
                memberDllPath,
                options);
            using var metadata =
                new Decompiler.Pipeline.MetadataContext(resolver);
            ResolvedAssemblyReference? projectionAssembly =
                memberCodeSourceAssembly ?? sourceAssembly;
            Decompiler.MemberRenderResult projection =
                projectionAssembly is null
                    ? Decompiler.MemberBodyProducer.ProduceMember(
                        type,
                        type.Members[0],
                        memberDllPath,
                        options.PdbPath,
                        resolver,
                        metadata,
                        options.RenderOptions)
                    : Decompiler.MemberBodyProducer.ProduceMember(
                        type,
                        type.Members[0],
                        projectionAssembly,
                        resolver,
                        metadata,
                        options.RenderOptions);
            if (TryWriteMemorySafetyModeUnavailable(projection.Failure))
                return 1;
        }

        if (options is MemberOptions
            && GetRequestedMemberSections(type, options)
                .Contains(SectionNames.DecompiledSource)
            && TryWriteMemorySafetyModeUnavailable(
                view.MemberCode?.DecompiledSourceFailure))
        {
            return 1;
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

        if (callsJson)
        {
            if (view.MemberCode is not { CallRows: not null } memberCode)
            {
                CommandError.Write(
                    $"section '{SectionNames.Calls}' produced no payload.");
                return 1;
            }

            OutputFormatter.WriteProjectedJson(
                sink,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = [SectionNames.Calls];
                    if (memberCode.CallRows.Count == 0)
                    {
                        MarkoutSerializer.Serialize(
                            new EmptyMemberCallsView(),
                            writer,
                            formatter,
                            ApiViewContext.Default,
                            writerOptions);
                    }
                    else
                    {
                        MarkoutSerializer.Serialize(
                            memberCode,
                            writer,
                            formatter,
                            ApiViewContext.Default,
                            writerOptions);
                    }
                },
                !options.CompactJson);
            return 0;
        }

        if (callersJson)
        {
            if (view.MemberCode is not { CallerRows: not null } memberCode)
            {
                CommandError.Write(
                    $"section '{SectionNames.Callers}' produced no payload.");
                return 1;
            }

            OutputFormatter.WriteProjectedJson(
                sink,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = [SectionNames.Callers];
                    MarkoutSerializer.Serialize(
                        memberCode,
                        writer,
                        formatter,
                        ApiViewContext.Default,
                        writerOptions);
                },
                !options.CompactJson);
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

        if (factsJson)
        {
            if (view.MemberCode?.FactsDocument is not { } facts)
            {
                CommandError.Write(
                    $"section '{SectionNames.Facts}' produced no payload.");
                return 1;
            }

            JsonOutputHelper.Write(
                facts,
                MemberFactsJsonContext.Default.MemberFactsDocument,
                MemberFactsCompactJsonContext.Default.MemberFactsDocument,
                options.CompactJson);
            return 0;
        }

        if (projectedFactsJson)
        {
            if (view.MemberCode is not { FactRows: { } factRows } memberCode)
            {
                CommandError.Write(
                    $"section '{SectionNames.Facts}' produced no payload.");
                return 1;
            }

            RowSelectionIntent<string>? rowSelection =
                (options as MemberOptions)?.FactsRowSelection;
            if (!CliSemanticRowSelection.TrySelect(
                    rowSelection,
                    factRows,
                    "Member Facts",
                    failure =>
                        $"Member Facts row selection stage "
                        + $"{failure.Failure.StageNumber} requires fact row "
                        + $"{failure.Failure.RequiredPosition}, but only "
                        + $"{failure.Failure.AvailableCount} fact rows are "
                        + "available.",
                    out IReadOnlyList<FactRow> selectedFacts))
            {
                return 1;
            }

            memberCode.FactRows = [.. selectedFacts];
            OutputFormatter.WriteProjectedJson(
                sink,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = [SectionNames.Facts];
                    MarkoutSerializer.Serialize(
                        memberCode,
                        writer,
                        formatter,
                        ApiViewContext.Default,
                        writerOptions);
                },
                !options.CompactJson);
            return 0;
        }

        // Whole-type decompilation (type command; member flows populate per
        // member above). Explicit-only: requires -S "Decompiled Source".
        // Sits OUTSIDE the member-sections region so enum types (which
        // populate EnumValues and skip that region) also compose.
        if (fullSerializer
            && options is TypeOptions declarationOptions
            && options.IncludeSections is { Count: > 0 }
            && GetRequestedMemberSections(type, options)
                .Contains(SectionNames.ApiDeclarations))
        {
            if (declarationOptions.TypeApiDeclarationInspection is not
                { } inspection)
            {
                CommandError.Write(
                    "The completed Type API declaration inspection is unavailable.");
                return 1;
            }
            if (inspection.Content
                is not
                {
                    Outcome: TypeApiDeclarationOutcome.Available,
                    Text: { } declaration,
                })
            {
                if (inspection.Diagnostics.Length == 0)
                {
                    CommandError.Write(
                        "Type API declaration inspection did not produce an available declaration.");
                }
                return 1;
            }

            view.MemberCode ??= new MemberCodeView();
            view.MemberCode.ApiDeclarationsCode =
                new Markout.CodeSection("csharp", declaration);
        }

        if (fullSerializer
            && options is TypeOptions decompilationOptions
            && options.DllPath is not null
            && options.IncludeSections is { Count: > 0 }
            && GetRequestedMemberSections(type, options).Contains(SectionNames.DecompiledSource))
        {
            if (decompilationOptions.TypeDecompilationInspection is not { } inspection)
            {
                WriteTypeDecompilationFailure(
                    "The completed type decompilation inspection is unavailable.");
                return 1;
            }

            if (inspection.Content
                is AssemblyTypeDecompilationEntry.Settled settled)
            {
                Decompiler.CSharpDecompilationAttempt attempt =
                    settled.Attempt;
                if (attempt.Status
                    is Decompiler.CSharpDecompilationStatus.Failed
                        or Decompiler.CSharpDecompilationStatus.Incomplete)
                {
                    if (attempt.DiagnosticSummary
                        is { Length: > 0 } detail)
                    {
                        CommandError.Write(detail);
                    }
                    else
                    {
                        WriteTypeDecompilationFailure(
                            $"Type decompilation ended with status {attempt.Status}.");
                    }
                    return 1;
                }
                if (attempt.Status
                    == Decompiler.CSharpDecompilationStatus.Available)
                {
                    if (attempt.Text is not { } listing)
                    {
                        WriteTypeDecompilationFailure(
                            "Type decompilation reported available source without text.");
                        return 1;
                    }

                    options.RenderConfigWarnings?.EmitOnce();
                    view.MemberCode ??= new MemberCodeView();
                    view.MemberCode.DecompiledSourceCode =
                        new Markout.CodeSection(
                            "csharp",
                            listing);
                }
            }
            else
            {
                string detail = inspection.Content switch
                {
                    AssemblyTypeDecompilationEntry.Unavailable unavailable =>
                        unavailable.Failure.Detail,
                    AssemblyTypeDecompilationEntry.Rejected rejected =>
                        $"{rejected.Failure.Kind}: {rejected.Failure.Detail}",
                    _ => "Type decompilation did not settle.",
                };
                WriteTypeDecompilationFailure(detail);
                return 1;
            }
        }

        if (options.Print)
        {
            int result = await PrintApiProjectionAsync(
                view, type, options, memberCodeSourceAssembly ?? sourceAssembly,
                packageName, packageVersion,
                sourceClient ?? DotnetInspector.Networking.HttpClientFactory.Shared);
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

            var projectionManifest = new RenderedSectionManifest();
            MergeCallGraphRenderedFields(
                projectionManifest,
                view.MemberCode?.CallGraphRenderedFieldEvidence.GraphFields
                    ?? CallGraphRenderedFieldEvidence.Empty.GraphFields);
            if (!DiagnoseProjection(
                    projectionManifest,
                    options,
                    GetTypeDocumentSchema(options),
                    options.IncludeSections,
                    requireMatchedProjection: false))
            {
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

        if (options.UsesNativePayloadDefault)
        {
            if (TryGetNativeApiPayload(view, options, out var raw))
            {
                OutputFormatter.WriteLfLine(sink, raw.TrimEnd());
                ApiOutputFormatter.WriteCallGraphWarning(view);
                return 0;
            }
        }

        if (options.Tabular)
        {
            var renderedWriter = new StringWriter { NewLine = "\n" };
            RenderedSectionManifest projectionManifest;
            IEnumerable<CallGraphField> callGraphRenderedFields = [];
            if (ApiOutputFormatter.ShouldRenderSectionedTabularView(type, options))
            {
                var writerOpts = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
                OutputFormatter.ConfigureTableWriterOptions(writerOpts, options.Tsv, options.Jsonl);
                callGraphRenderedFields =
                    GetCallGraphEdgeTableRenderedFields(view, writerOpts);
                OutputFormatter.WriteTable(renderedWriter, !options.NoHeader,
                    (writer, formatter) =>
                    {
                        var markoutWriter = new MarkoutWriter(writer, formatter, writerOpts);
                        ApiOutputFormatter.SerializeTypeDocument(
                            view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                            explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, markoutWriter);
                        markoutWriter.Flush();
                    }, options.Rows);

                writerOpts.RowWindow = RowWindow.ToMarkout(options.Rows);
                var formatter = new RenderManifestFormatter(
                    GetTypeDocumentSchema(options));
                formatter.BeginDocument(writerOpts);
                var manifestWriter = new MarkoutWriter(
                    TextWriter.Null,
                    formatter,
                    writerOpts);
                ApiOutputFormatter.SerializeTypeDocument(
                    view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                    explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, manifestWriter);
                manifestWriter.Flush();
                projectionManifest = formatter.Manifest;
                if (options.Columns is { Length: > 0 }
                    && options.Fields is { Length: > 0 })
                {
                    MarkoutWriterOptions fieldWriterOptions =
                        ApiOutputFormatter.BuildTypeWriterOptions(
                            type,
                            options with { Columns = null });
                    OutputFormatter.ConfigureTableWriterOptions(
                        fieldWriterOptions,
                        options.Tsv,
                        options.Jsonl);
                    fieldWriterOptions.RowWindow =
                        RowWindow.ToMarkout(options.Rows);
                    var fieldFormatter = new RenderManifestFormatter(
                        GetTypeDocumentSchema(options));
                    fieldFormatter.BeginDocument(fieldWriterOptions);
                    var fieldManifestWriter = new MarkoutWriter(
                        TextWriter.Null,
                        fieldFormatter,
                        fieldWriterOptions);
                    ApiOutputFormatter.SerializeTypeDocument(
                        view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                        explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, fieldManifestWriter);
                    fieldManifestWriter.Flush();
                    projectionManifest.MergeRenderedFieldTablesFrom(
                        fieldFormatter.Manifest);
                }
            }
            else
            {
                var (tableView, _) = ApiOutputFormatter.BuildTypeTableView(type, options);
                OutputFormatter.WriteProjectedTable(
                    renderedWriter,
                    !options.NoHeader,
                    options.Tsv,
                    options.Jsonl,
                    options.Columns, options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(tableView, writer, formatter, ApiViewContext.Default, writerOptions),
                    options.Rows);
                projectionManifest = OutputFormatter.CaptureProjectedTableManifest(
                    options.Tsv,
                    options.Jsonl,
                    options.Columns,
                    options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(
                            tableView,
                            writer,
                            formatter,
                            ApiViewContext.Default,
                            writerOptions),
                    GetTypeDocumentSchema(options),
                    options.Rows,
                    options.IncludeSections is { Count: 1 }
                        ? options.IncludeSections.Single()
                        : null,
                    lockRootScope: true);
            }

            MergeCallGraphRenderedFields(
                projectionManifest,
                callGraphRenderedFields);
            if (!DiagnoseProjection(
                    projectionManifest,
                    options,
                    GetTypeDocumentSchema(options),
                    options.IncludeSections))
                return 1;

            sink.Write(renderedWriter.ToString());
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
            writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
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

            DocumentSchema schema = GetTypeDocumentSchema(options);
            var manifestFormatter = new RenderManifestFormatter(schema);
            manifestFormatter.BeginDocument(writerOptions);
            var manifestWriter = new MarkoutWriter(
                TextWriter.Null,
                manifestFormatter,
                writerOptions);
            ApiOutputFormatter.SerializeTypeDocument(
                view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, manifestWriter);
            manifestWriter.Flush();
            RenderedSectionManifest projectionManifest =
                manifestFormatter.Manifest;
            MergeCallGraphRenderedFields(
                projectionManifest,
                options.PlainText
                || options.EmbeddedMermaid
                    ? view.MemberCode?.CallGraphRenderedFieldEvidence.GraphFields
                        ?? CallGraphRenderedFieldEvidence.Empty.GraphFields
                    : GetCallGraphEdgeTableRenderedFields(view, writerOptions));

            if (options.Columns is { Length: > 0 }
                && options.Fields is { Length: > 0 })
            {
                MarkoutWriterOptions fieldWriterOptions =
                    ApiOutputFormatter.BuildTypeWriterOptions(
                        type,
                        options with { Columns = null });
                fieldWriterOptions.RowWindow =
                    RowWindow.ToMarkout(options.Rows);
                fieldWriterOptions.SectionOrder = writerOptions.SectionOrder;
                var fieldFormatter = new RenderManifestFormatter(schema);
                fieldFormatter.BeginDocument(fieldWriterOptions);
                var fieldManifestWriter = new MarkoutWriter(
                    TextWriter.Null,
                    fieldFormatter,
                    fieldWriterOptions);
                ApiOutputFormatter.SerializeTypeDocument(
                    view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                    explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, fieldManifestWriter);
                fieldManifestWriter.Flush();
                projectionManifest.MergeRenderedFieldTablesFrom(
                    fieldFormatter.Manifest);
            }

            if (!DiagnoseProjection(
                    projectionManifest,
                    options,
                    schema,
                    options.IncludeSections,
                    requireMatchedProjection:
                        options.IncludeSections is not { Count: > 0 }))
                return 1;

            var renderedWriter = new StringWriter { NewLine = "\n" };
            var writer = new MarkoutWriter(
                renderedWriter,
                options.CreateFormatter(),
                writerOptions);
            ApiOutputFormatter.SerializeTypeDocument(
                view, eventsView, methodGroupsView, methodsView, memberIndexView, operatorsView,
                explicitInterfaceImplementationsView, extensionMethodsView, view.MemberCode, writer);
            writer.Flush();
            string rendered = renderedWriter.ToString();
            if (options.PlainText)
                sink.Write(rendered);
            else
                OutputFormatter.WriteLfLine(sink, rendered.TrimEnd());
        }
        ApiOutputFormatter.WriteSignatureDecodeWarning(view);
        ApiOutputFormatter.WriteCallGraphWarning(view);
        return 0;
    }

    private static void MergeCallGraphRenderedFields(
        RenderedSectionManifest manifest,
        IEnumerable<CallGraphField> fields)
    {
        manifest.RecordFields(
            SectionNames.CallGraph,
            fields.SelectMany(CallGraphFieldSelection.NamesFor));
    }

    private static IEnumerable<CallGraphField> GetCallGraphEdgeTableRenderedFields(
        TypeView view,
        MarkoutWriterOptions writerOptions)
    {
        if (view.MemberCode is not
            {
                CallGraph: { } graph,
                CallGraphRenderedFieldEvidence: { } evidence,
            })
        {
            return [];
        }

        MarkoutProjection? projection = writerOptions.Projection;
        if (projection?.IncludeColumns is null)
            return evidence.GraphFields;

        var table = GraphLowering.ToEdgeTable(graph);
        if (!projection.TryResolveColumns(
                table.Headers.AsSpan(),
                out ColumnProjectionResolution resolution))
        {
            return [];
        }

        bool includesFrom = resolution.ColumnMap.Any(index =>
            index >= 0
            && index < table.Headers.Length
            && table.Headers[index].Equals(
                "From",
                StringComparison.OrdinalIgnoreCase));
        bool includesTo = resolution.ColumnMap.Any(index =>
            index >= 0
            && index < table.Headers.Length
            && table.Headers[index].Equals(
                "To",
                StringComparison.OrdinalIgnoreCase));

        return (includesFrom, includesTo) switch
        {
            (true, true) => evidence.GraphFields,
            (true, false) => evidence.FromFields,
            (false, true) => evidence.ToFields,
            _ => [],
        };
    }

    private static async Task<int> PrintApiProjectionAsync(
        TypeView view,
        ApiType type,
        ApiOptions options,
        ResolvedAssemblyReference? sourceAssembly,
        string? packageName,
        string? packageVersion,
        HttpClient sourceClient)
    {
        var section = options.IncludeSections!.Single();
        if (section.Equals(SectionNames.SourceFiles, StringComparison.OrdinalIgnoreCase))
        {
            var rows = view.SourceFileRows ?? [];
            var selection = SelectPrintableRow(
                rows.Select((row, index) => (
                    Row: index + 1,
                    Label: (string?)row.Url,
                    Url: (string?)row.Url)).ToList(),
                options.PrintRow,
                out var selectionError);
            if (selection is not { } selected)
            {
                CommandError.Write(selectionError);
                return 1;
            }

            var source = rows[selected.Row - 1];
            return await AuthoredSourceDocumentPrinter.PrintAsync(
                type,
                new PrintableRow(selected.Row, section, source.Url, null, source.Url),
                source.FilePath,
                options, sourceAssembly, packageName, packageVersion, sourceClient);
        }

        if (section.Equals(SectionNames.SourceLocations, StringComparison.OrdinalIgnoreCase))
        {
            var rows = view.SourceLocationRows ?? [];
            var selection = SelectPrintableRow(
                rows.Select((row, index) => (
                    Row: index + 1,
                    Label: (string?)row.File ?? row.Url,
                    Url: row.Url)).ToList(),
                options.PrintRow,
                out var selectionError);
            if (selection is not { } selected)
            {
                CommandError.Write(selectionError);
                return 1;
            }

            return await AuthoredSourceDocumentPrinter.PrintAsync(
                type,
                new PrintableRow(
                    selected.Row,
                    section,
                    string.IsNullOrWhiteSpace(selected.Label) ? selected.Url! : selected.Label,
                    null,
                    selected.Url),
                rows[selected.Row - 1].FilePath,
                options, sourceAssembly, packageName, packageVersion, sourceClient);
        }

        var documents = section switch
        {
            SectionNames.ApiDeclarations => CodeSectionDocument(section, SectionNames.ApiDeclarations, null, view.MemberCode?.ApiDeclarationsCode.Content),
            SectionNames.PdbSource => CodeSectionDocument(section, SectionNames.PdbSource, MemberSourceUrl(options as MemberOptions), view.MemberCode?.PdbSourceCode.Content),
            SectionNames.DecompiledSource => CodeSectionDocument(section, "Decompiled Source", null, view.MemberCode?.DecompiledSourceCode.Content),
            SectionNames.AnnotatedSource => CodeSectionDocument(section, "Annotated Source", null, view.MemberCode?.AnnotatedSourceCode.Content),
            SectionNames.SourceDiff => CodeSectionDocument(section, "Source Diff", MemberSourceUrl(options as MemberOptions), view.MemberCode?.SourceDiffCode?.Content),
            SectionNames.IL => CodeSectionDocument(section, "IL", null, view.MemberCode?.ILCode.Content),
            _ => []
        };

        if (documents.Count == 0
            && section is not (SectionNames.SourceFiles or SectionNames.SourceLocations or SectionNames.ApiDeclarations or SectionNames.PdbSource
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
                new ProjectionDestination(null, options.Rows),
                Markdown: options.UsesMarkdownPayloadFormat));
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

    private static void WriteTypeDecompilationFailure(string detail)
    {
        Decompiler.DecompilerResult failure =
            Decompiler.DecompilerResult.Failure(
                Decompiler.DiagnosticIds.InternalError,
                detail);
        CommandError.Write(string.Join(
            Environment.NewLine,
            failure.Diagnostics.Select(
                static diagnostic => diagnostic.ToString())));
    }

    private static List<PrintableDocument> CodeSectionDocument(
        string section,
        string label,
        string? url,
        string? content)
        => string.IsNullOrEmpty(content)
            ? []
            : [new PrintableDocument(1, section, label, null, url, content)
            {
                Language = section switch
                {
                    SectionNames.IL => "il",
                    SectionNames.SourceDiff => "diff",
                    _ => "csharp"
                }
            }];

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

    private static bool TryGetNativeApiPayload(
        TypeView view,
        ApiOptions options,
        out string raw)
    {
        raw = "";

        if (options.IncludeSections is not { Count: 1 } included)
            return false;

        var section = included.First();
        raw = section switch
        {
            SectionNames.ApiDeclarations => view.MemberCode?.ApiDeclarationsCode.Content ?? "",
            SectionNames.DecompiledSource => view.MemberCode?.DecompiledSourceCode.Content ?? "",
            SectionNames.AnnotatedSource => view.MemberCode?.AnnotatedSourceCode.Content ?? "",
            SectionNames.FindingCensus => view.MemberCode?.FindingCensusCode.Content ?? "",
            SectionNames.CostOverlay => view.MemberCode?.CostOverlayCode.Content ?? "",
            SectionNames.SemanticsOverlay => view.MemberCode?.SemanticsOverlayCode.Content ?? "",
            SectionNames.PdbSource => view.MemberCode?.PdbSourceCode.Content ?? "",
            SectionNames.SourceDiff => view.MemberCode?.SourceDiffCode?.Content ?? "",
            SectionNames.IL => view.MemberCode?.ILCode.Content ?? "",
            _ => ""
        };

        if (raw.Length > 0)
            return true;

        return false;
    }

}
