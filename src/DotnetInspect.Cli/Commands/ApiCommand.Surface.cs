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
        if (options is TypeOptions typeOptions
            && !TrySelectTypeListingRows(api, typeOptions))
        {
            return 1;
        }
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
            var ordered =
                options is TypeOptions { CountDefaultPopulation: true }
                    ? null
                    : OutputFormatter.ResolveCountMapSections(
                        ApiTypeSectionDescriptors.CreatePipeline(),
                        options.IncludeSections,
                        fixedOverview: false);
            CountOutput.Write(
                projection, ordered, options.Format, options.NoHeader);
        }
        else if (options.Tabular)
        {
            if (ApiOutputFormatter
                .ShouldRenderSurfaceSectionedTableView(
                    options))
            {
                string section = options.IncludeSections!.Single();
                var sectionRows =
                    OutputFormatter.RenderProjectedTable(
                        !options.NoHeader,
                        options.Tsv,
                        options.Jsonl,
                        options.Columns,
                        options.Fields,
                        (writer, formatter, writerOptions) =>
                        {
                            writerOptions.IncludeSections =
                                [section];
                            MarkoutSerializer.Serialize(
                                view,
                                writer,
                                formatter,
                                ApiViewContext.Default,
                                writerOptions);
                        },
                        options.Rows);
                RenderedSectionManifest sectionManifest =
                    OutputFormatter.CaptureProjectedTableManifest(
                        options.Tsv,
                        options.Jsonl,
                        options.Columns,
                        options.Fields,
                        (writer, formatter, writerOptions) =>
                        {
                            writerOptions.IncludeSections =
                                [section];
                            MarkoutSerializer.Serialize(
                                view,
                                writer,
                                formatter,
                                ApiViewContext.Default,
                                writerOptions);
                        },
                        ApiViewContext.Default
                            .GetSchemaInfo<CliApiSurface>()!
                            .ToDocumentSchema(),
                        options.Rows,
                        rootSection: section);
                if (!DiagnoseProjection(
                        sectionManifest,
                        options,
                        sections: [section]))
                {
                    return 1;
                }
                Console.Out.Write(sectionRows);
                return successExitCode;
            }

            if (ApiOutputFormatter.ShouldRenderSurfaceFactTableView(options))
            {
                var factRows = OutputFormatter.RenderProjectedTable(!options.NoHeader, options.Tsv, options.Jsonl,
                    options.Columns, options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(view.ApiInfo!, writer, formatter, ApiViewContext.Default, writerOptions));
                RenderedSectionManifest factManifest =
                    OutputFormatter.CaptureProjectedTableManifest(
                        options.Tsv,
                        options.Jsonl,
                        options.Columns,
                        options.Fields,
                        (writer, formatter, writerOptions) =>
                            MarkoutSerializer.Serialize(
                                view.ApiInfo!,
                                writer,
                                formatter,
                                ApiViewContext.Default,
                                writerOptions),
                        ApiViewContext.Default
                            .GetSchemaInfo<CliApiSurface>()!
                            .ToDocumentSchema(),
                        options.Rows,
                        rootSection: SectionNames.ApiInfo);
                if (!DiagnoseProjection(
                        factManifest,
                        options,
                        sections: [SectionNames.ApiInfo]))
                    return 1;
                Console.Out.Write(OutputFormatter.LimitRenderedTableRows(factRows, options.Rows, !options.NoHeader));
                return successExitCode;
            }

            var (tableView, _) = ApiOutputFormatter.BuildSurfaceTableView(api, options);
            var rendered = OutputFormatter.RenderProjectedTable(!options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(tableView, writer, formatter, ApiViewContext.Default, writerOptions));
            RenderedSectionManifest tableManifest =
                OutputFormatter.CaptureProjectedTableManifest(
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
                    ApiViewContext.Default
                        .GetSchemaInfo<CliApiSurface>()!
                        .ToDocumentSchema(),
                    options.Rows,
                    rootSection: options.IncludeSections is { Count: 1 }
                        ? options.IncludeSections.Single()
                        : null,
                    lockRootScope: true);
            if (!DiagnoseProjection(
                    tableManifest,
                    options,
                    sections: options.IncludeSections))
                return 1;
            Console.Out.Write(OutputFormatter.LimitRenderedTableRows(rendered, options.Rows, !options.NoHeader));
        }
        else
        {
            var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
            writerOptions.RowWindow = RowWindow.ToMarkout(options.Rows);
            DocumentSchema schema = ApiViewContext.Default
                .GetSchemaInfo<CliApiSurface>()!
                .ToDocumentSchema();
            RenderedSectionManifest manifest = RenderManifestFormatter.Capture(
                view,
                ApiViewContext.Default,
                writerOptions,
                schema);
            if (options.Columns is { Length: > 0 }
                && options.Fields is { Length: > 0 })
            {
                var fieldOptions = options with { Columns = null };
                MarkoutWriterOptions fieldWriterOptions =
                    ApiOutputFormatter.BuildWriterOptions(
                        api,
                        fieldOptions);
                fieldWriterOptions.RowWindow =
                    RowWindow.ToMarkout(options.Rows);
                manifest.MergeRenderedFieldTablesFrom(
                    RenderManifestFormatter.Capture(
                        view,
                        ApiViewContext.Default,
                        fieldWriterOptions,
                        schema));
            }

            if (!DiagnoseProjection(
                    manifest,
                    options,
                    schema,
                    options.IncludeSections))
                return 1;

            if (options.PlainText)
            {
                // Buffered rather than written straight to the console so the empty-render gate
                // can see the result. Writing directly is what let an emptying projection print
                // nothing and exit 0 here while every sibling path reported it.
                var plain = new StringWriter();
                MarkoutSerializer.Serialize(view, plain, options.CreateFormatter(), ApiViewContext.Default, writerOptions);
                var plainText = plain.ToString();
                Console.Out.Write(plainText);
            }
            else
            {
                var markdownWriter = new StringWriter { NewLine = "\n" };
                MarkoutSerializer.Serialize(
                    view, markdownWriter, new MarkdownFormatter(), ApiViewContext.Default, writerOptions);
                var markdown = markdownWriter.ToString().TrimEnd();
                OutputFormatter.WriteLfLine(Console.Out, markdown);
            }
        }

        return successExitCode;
    }

    private static bool TrySelectTypeListingRows(
        ApiSurface api,
        TypeOptions options)
    {
        if (options.TypeListingRowSelection is null)
            return true;

        SurfaceSubjects materialized =
            CaptureSurfaceSubjects(api.Types);
        if (!CliSemanticRowSelection.TrySelect(
                options.TypeListingRowSelection,
                api.Types,
                "Type",
                failure =>
                    $"Type row selection stage "
                    + $"{failure.Failure.StageNumber} requires row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} rows are available.",
                out IReadOnlyList<ApiType> selected))
        {
            return false;
        }

        api.Types = [.. selected];
        api.PublicTypeCount = api.Types.Count;
        api.PublicMethodCount =
            api.Types.Sum(
                type => type.Members.Count(
                    ApiMemberSectionDescriptors.IsMethodLike));
        api.PublicPropertyCount =
            api.Types.Sum(
                type => type.Members.Count(
                    member => member.Kind == "property"));
        api.PublicFieldCount =
            api.Types.Sum(
                type => type.Members.Count(
                    member => member.Kind == "field"));
        api.PublicEventCount =
            api.Types.Sum(
                type => type.Members.Count(
                    member => member.Kind == "event"));
        ReprojectSurfaceFailures(api, materialized);
        return true;
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
        List<string> candidates = GetProjectionCandidates(schema, options, wantedKind);

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

    internal static bool DiagnoseProjection(
        RenderedSectionManifest manifest,
        ApiOptions options,
        DocumentSchema? schema = null,
        IReadOnlyCollection<string>? sections = null,
        bool requireMatchedProjection = true)
    {
        string[]? requested = options.Fields ?? options.Columns;
        if (requested is not { Length: > 0 })
            return true;

        schema ??= ApiViewContext.Default
            .GetSchemaInfo<CliApiSurface>()!
            .ToDocumentSchema();
        string wantedKind = options.Fields is { Length: > 0 }
            ? "field"
            : "column";

        ProjectionDiagnostics.DiagnoseProjected(
            requested,
            manifest,
            schema,
            wantedKind,
            sections,
            fieldSectionsAsColumns: true);

        return !requireMatchedProjection || TryReportEmptyProjection(
            manifest.HasAnyData,
            options,
            schema);
    }

    private static List<string> GetProjectionCandidates(
        DocumentSchema schema,
        ApiOptions options,
        string wantedKind)
    {
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

        foreach (string section in schema.SectionNames)
        {
            foreach (var item in schema.Discover(section) ?? [])
            {
                if (!string.Equals(item.Kind, wantedKind, StringComparison.OrdinalIgnoreCase))
                    continue;

                candidates.Add(item.Name);
            }
        }

        return candidates;
    }

}
