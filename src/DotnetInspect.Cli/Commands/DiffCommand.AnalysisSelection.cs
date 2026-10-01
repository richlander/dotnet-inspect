using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;
using InertText;
using Inspector.Findings;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Diff analysis selection: <c>--analysis</c> selects Compare-participating
/// producers from the registered analyses, and <c>-S</c> selects views
/// (<c>Summary</c>, <c>Changes</c>, <c>Transitions</c>) of their result.
/// </summary>
public partial class DiffCommand
{
    /// <summary>A validated analysis set and the admitted views of its result.</summary>
    internal sealed record DiffAnalysisPlan(
        AnalysisSetValidationResult.Accepted Selection,
        IReadOnlyList<string> Views,
        bool DetailedChanges = false);

    static readonly string[] RoutesOutsideAnalysisSelection =
    [
        DiffSections.AnalysisDiff.Name,
        DiffSections.ImplementationDiff.Name,
        DiffSections.ComplexityContext.Name,
        DiffSections.StructuralContext.Name,
    ];

    /// <summary>
    /// True when the request selects an analysis set explicitly, or selects
    /// a view (<c>Summary</c> or <c>Transitions</c>) that exists only over an
    /// analysis-set result. Other requests, including <c>-S @Diff</c>, keep
    /// today's section selection.
    /// </summary>
    internal static bool SelectsAnalysisSet(DiffOptions options)
        => options.Analysis is not null
            || options.IncludeSections?.Contains(DiffSections.Summary.Name) == true
            || options.IncludeSections?.Contains(DiffSections.Transitions.Name) == true;

    internal static AnalysisReportSurfaceKind AnalysisSurfaceOf(
        DiffOptions options,
        out int targetCount)
    {
        if (options.MemberFilter.Count > 0)
        {
            targetCount = options.MemberFilter.Count;
            return AnalysisReportSurfaceKind.Member;
        }
        if (options.TypeFilter.Count > 0)
        {
            targetCount = options.TypeFilter.Count;
            return AnalysisReportSurfaceKind.Type;
        }
        targetCount = 1;
        return AnalysisReportSurfaceKind.Library;
    }

    /// <summary>
    /// Validates the analysis set, its views, and view-specific filters
    /// before any acquisition or producer work. A null plan with a true
    /// result means the request is exactly Diff's default <c>api</c>
    /// <c>Changes</c> request and keeps today's delivery.
    /// </summary>
    internal static bool TryPlanAnalysisSet(
        DiffOptions options,
        out DiffAnalysisPlan? plan)
    {
        plan = null;
        string subject = options.Analysis is not null
            ? "--analysis"
            : "Summary and Transitions";
        string[] routes =
        [
            .. RoutesOutsideAnalysisSelection.Where(route =>
                options.IncludeSections?.Contains(route) == true),
        ];
        if (options.AllocRegressionsOnly
            && !routes.Contains(DiffSections.AnalysisDiff.Name))
        {
            routes = [DiffSections.AnalysisDiff.Name, .. routes];
        }
        if (routes.Length > 0)
        {
            CommandError.Write(
                $"{subject} cannot be combined with {string.Join(", ", routes)}; "
                + "those routes are not keyed Finding comparisons and stay "
                + "outside analysis selection until they migrate.");
            return false;
        }
        AnalysisReportSurfaceKind surface =
            AnalysisSurfaceOf(options, out int targetCount);
        InspectionAnalysisConsumerBinding binding =
            DiffAnalysisCommandCapability.Binding;
        AnalysisSetValidationResult validation =
            DiffAnalysisCommandCapability.Catalog.AnalysisCapabilities
                .ValidateSet(
                    binding.Operation,
                    surface,
                    targetCount,
                    options.Analysis);
        if (validation is AnalysisSetValidationResult.Rejected rejected)
        {
            WriteAnalysisSetRejection(rejected, surface);
            return false;
        }

        var selection = (AnalysisSetValidationResult.Accepted)validation;
        bool selectsApi = DiffAnalysisViewAdmission.IncludesApi(selection);
        List<string> views;
        if (options.IncludeSections is { Count: > 0 } sections)
        {
            views =
            [
                .. new[]
                {
                    DiffSections.Summary.Name,
                    DiffSections.Changes.Name,
                    DiffSections.Transitions.Name,
                }.Where(sections.Contains),
            ];
        }
        else
        {
            views =
            [
                selection.Analyses.Length > 1
                    ? DiffSections.Summary.Name
                    : selectsApi
                        ? DiffSections.Changes.Name
                        : DiffSections.Transitions.Name,
            ];
        }

        switch (DiffAnalysisViewAdmission.Validate(
            selection,
            DocumentViews(views)))
        {
            case DiffAnalysisViewRejectionReason.ChangesRequireApi:
                CommandError.Write(
                    "The Changes view projects the 'api' analysis, which is not "
                    + "selected; add --analysis api or select -S Transitions.");
                return false;
            case DiffAnalysisViewRejectionReason
                .TransitionsRequireTypeOrMember:
                CommandError.Write(
                    "The Transitions view requires the Type or Member surface; "
                    + "add --type or --member, or select -S Summary.");
                return false;
        }
        if ((views.Contains(DiffSections.Transitions.Name)
                || views.Contains(DiffSections.Summary.Name))
            && (options.Breaking
                || options.Additive
                || options.ChangedOnly
                || options.NameOnly))
        {
            CommandError.Write(
                "--breaking, --additive, --changed, and --name-only refine "
                + "the Changes view only; they cannot be combined with "
                + "Summary or Transitions.");
            return false;
        }

        if (views is [var only]
            && only == DiffSections.Changes.Name
            && selection.Analyses.Length == 1
            && selectsApi
            && options.Analysis is null
            && !options.EnvelopeOutput
            && !options.JsonOutput)
        {
            return true;
        }

        plan = new DiffAnalysisPlan(
            selection,
            views,
            DetailedChanges:
                options.IncludeSections?.Contains(
                    DiffSections.Changes.Name) == true);
        return true;
    }

    /// <summary>
    /// Lists the Compare-participating analyses <c>--analysis</c> selects,
    /// from the same registrations Diff dispatches on.
    /// </summary>
    static void WriteAnalysisDiscovery()
    {
        var view = new DiffAnalysisDiscoveryView
        {
            Rows =
            [
                .. DiffAnalysisCommandCapability.Catalog.Analyses
                    .Select(registration => (
                        registration.Analysis,
                        Participation: registration.Analysis.ParticipationFor(
                            AnalysisOperationKind.Compare)))
                    .Where(entry => entry.Participation is not null)
                    .Select(entry => new DiffAnalysisDiscoveryRow(
                        entry.Analysis.Id.Value,
                        DiffAnalysisCatalog.Operation.DefaultSet.Contains(
                            entry.Analysis.Id.Value) ? "yes" : "no",
                        string.Join(", ", entry.Participation!.Surfaces.Select(
                            surface => surface.Surface)),
                        string.Join(", ", entry.Participation.Surfaces
                            .SelectMany(surface => surface.Descriptors)
                            .Select(descriptor => descriptor.Id)
                            .Distinct(StringComparer.Ordinal)))),
            ],
        };
        var writer = new MarkoutWriter(new MarkdownFormatter());
        DiffViewContext.Default.Serialize(view, writer);
        Console.WriteLine();
        Console.WriteLine(writer.Complete().TrimEnd());
    }

    static void WriteAnalysisDiagnostics(
        IReadOnlyList<InspectionDiagnostic> diagnostics)
    {
        foreach (InspectionDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Code
                == "diff-analysis.api-inspection-failure")
            {
                continue;
            }

            string summary = diagnostic.Summary.ToString();
            switch (diagnostic.Severity)
            {
                case InspectionDiagnosticSeverity.Information:
                    CommandError.WriteNote(summary);
                    break;
                case InspectionDiagnosticSeverity.Warning:
                    CommandError.WriteWarning(summary);
                    break;
                case InspectionDiagnosticSeverity.Error:
                    CommandError.Write(summary);
                    break;
            }
        }
    }

    static void WriteAnalysisSetRejection(
        AnalysisSetValidationResult.Rejected rejected,
        AnalysisReportSurfaceKind surface)
    {
        CommandError.Write("The --analysis set was rejected.");
        foreach (AnalysisSetEntryRejection rejection in rejected.Rejections)
        {
            string entry = rejection.RequestedIdentity is null
                ? "(set)"
                : $"'{rejection.RequestedIdentity}'";
            string reason = (rejection.SetReason, rejection.RequestReason) switch
            {
                (AnalysisSetRejectionReason.Unknown, _) =>
                    "is not a registered analysis; use one of: "
                    + string.Join(", ", DiffAnalysisCommandCapability.Identities),
                (AnalysisSetRejectionReason.NotParticipating, _) =>
                    "does not take part in Diff comparisons",
                (AnalysisSetRejectionReason.Duplicate, _) =>
                    "repeats an earlier entry",
                (AnalysisSetRejectionReason.Empty, _) =>
                    rejection.Position is null
                        ? "is empty; name at least one analysis"
                        : "is an empty entry",
                (_, AnalysisRequestRejectionReason.UnsupportedSurface) =>
                    $"does not support the {surface} surface; it supports "
                    + SurfacesOf(rejection.Analysis),
                (_, AnalysisRequestRejectionReason.UnsupportedTargetRole) =>
                    $"requires {CardinalityOf(rejection)} at the {surface} surface",
                _ => "is invalid",
            };
            CommandError.WriteDetail($"{entry} {reason}.");
        }
    }

    static string SurfacesOf(AnalysisDescriptor? analysis)
        => analysis?.ParticipationFor(AnalysisOperationKind.Compare) is { } participation
            ? string.Join(
                ", ",
                participation.Surfaces.Select(surface => surface.Surface switch
                {
                    AnalysisReportSurfaceKind.Member => "Member (--member)",
                    AnalysisReportSurfaceKind.Type => "Type (--type)",
                    AnalysisReportSurfaceKind.Library => "Library (no --type or --member)",
                    var other => other.ToString(),
                }))
            : "no Diff surface";

    static string CardinalityOf(AnalysisSetEntryRejection rejection)
    {
        int minimum = rejection.TargetRoles.Sum(role => role.MinimumCount);
        long maximum = rejection.TargetRoles.Sum(role => (long)role.MaximumCount);
        return minimum == maximum
            ? $"exactly {minimum} target{(minimum == 1 ? "" : "s")}"
            : $"at least {minimum} target{(minimum == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Rejects pairwise <c>--finding</c>, mapping each former descriptor
    /// (case-insensitively) to its one analysis identity. The old spelling
    /// is never accepted.
    /// </summary>
    static void WriteRetiredFindingGuidance(string finding)
    {
        string? analysis = DiffAnalysisCommandCapability.Catalog.Analyses
            .Where(registration => registration.Analysis.ParticipationFor(
                AnalysisOperationKind.Compare) is not null)
            .FirstOrDefault(registration => registration.Analysis.Participations
                .SelectMany(participation => participation.Surfaces)
                .SelectMany(surface => surface.Descriptors)
                .Any(descriptor => string.Equals(
                    descriptor.Id,
                    finding.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            ?.Analysis.Id.Value;
        CommandError.Write(
            analysis is null
                ? "Pairwise diff no longer accepts --finding; select analyses "
                    + "with --analysis ("
                    + string.Join(", ", DiffAnalysisCommandCapability.Identities)
                    + "). --history keeps --finding."
                : $"Pairwise diff no longer accepts --finding; use --analysis "
                    + $"{analysis} (with -S Transitions for per-Finding rows). "
                    + "--history keeps --finding.");
    }

    /// <summary>
    /// Plans and runs one analysis set over already-acquired endpoints and
    /// returns the Transitions projection in selection and descriptor order.
    /// Target resolution failures throw before any producer runs.
    /// </summary>
    internal static IReadOnlyList<FindingTransitionRow> BuildAnalysisTransitions(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
    {
        DiffOptions planned = options.IncludeSections is null
            ? options with
            {
                IncludeSections = new HashSet<string>(
                    [DiffSections.Transitions.Name],
                    StringComparer.OrdinalIgnoreCase),
            }
            : options;
        if (!TryPlanAnalysisSet(planned, out DiffAnalysisPlan? plan)
            || plan is null)
        {
            throw new InvalidOperationException(
                "The analysis set or its views were rejected.");
        }
        return
        [
            .. RunAnalysisSet(
                    "Diff",
                    fromPaths,
                    toPaths,
                    fromSurface,
                    toSurface,
                    fromVersion,
                    toVersion,
                    planned,
                    plan)
                .Inspection.Content.Transitions
                .GetValueOrDefault()
                .Select(ToView),
        ];
    }

    internal static IReadOnlyList<FindingTransitionRow> BuildAnalysisTransitions(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options)
        => BuildAnalysisTransitions(
            [],
            [],
            fromSurface,
            toSurface,
            fromVersion,
            toVersion,
            options);

    internal sealed record AnalysisSetRun(
        InspectionEnvelope<DiffAnalysisDocument> Inspection);

    /// <summary>
    /// Resolves request targets, runs every selected analysis's registered
    /// producer once, and projects each outcome's transitions. A target
    /// resolution failure throws before any producer runs.
    /// </summary>
    static AnalysisSetRun RunAnalysisSet(
        string name,
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options,
        DiffAnalysisPlan plan)
    {
        AnalysisSetValidationResult.Accepted selection = plan.Selection;
        AnalysisReportSurfaceKind surface = selection.Surface;

        // Member targets resolve once, before any producer runs, whatever the
        // set: a target failure is a request failure, never an outcome.
        ResolvedDiffMemberTargets? targets = null;
        if (surface == AnalysisReportSurfaceKind.Member)
        {
            try
            {
                targets = ResolveMemberTargetIdentities(
                    fromSurface,
                    toSurface,
                    options.MemberFilter,
                    options.TypeFilter);
            }
            catch (InvalidOperationException ex)
                when (ex is not DiffAnalysisTargetException)
            {
                throw new DiffAnalysisTargetException(ex.Message);
            }
        }
        IReadOnlyList<string> typeNames = surface switch
        {
            AnalysisReportSurfaceKind.Type => DiffAnalysisTypeFilter.Resolve(
                fromSurface,
                toSurface,
                options.TypeFilter),
            AnalysisReportSurfaceKind.Member when targets is not null =>
                [.. targets.TypeNames.Order(StringComparer.Ordinal)],
            _ => [],
        };

        var input = new DiffAnalysisInput(
            fromSurface,
            toSurface,
            fromPaths,
            toPaths,
            options.TypeFilter,
            typeNames,
            targets?.MemberIdentities,
            descriptors => RequireBodySignalComparison(
                BodySignalComparisonQuery.Execute(
                    CreateBodySignalComparisonInput(
                        fromPaths,
                        toPaths,
                        options,
                        fromSurface,
                        toSurface,
                        descriptors)),
                options,
                "--analysis"),
            () => RequireImplementationComparison(
                ImplementationComparisonQuery.Execute(
                    CreateImplementationComparisonInput(
                        fromPaths,
                        toPaths,
                        options,
                        fromSurface,
                        toSurface))));
        return new AnalysisSetRun(
            DiffAnalysisInspection.Execute(
                new DiffAnalysisInspectionRequest(
                    name,
                    fromVersion,
                    toVersion,
                    DiffAnalysisCommandCapability.Catalog,
                    selection,
                    input,
                    DocumentViews(plan.Views))));
    }

    /// <summary>
    /// Runs the analysis set over the acquired endpoints and writes the
    /// selected views.
    /// </summary>
    static int ExecuteAnalysisSet(
        DiffInputs inputs,
        DiffOptions options,
        DiffAnalysisPlan plan)
    {
        AnalysisSetRun run = RunAnalysisSet(
            inputs.Name,
            inputs.FromPaths,
            inputs.ToPaths,
            inputs.FromSurface,
            inputs.ToSurface,
            inputs.FromVersion,
            inputs.ToVersion,
            options,
            plan);
        return WriteAnalysisSet(
            inputs.Name,
            inputs.FromSurface,
            inputs.ToSurface,
            inputs.FromVersion,
            inputs.ToVersion,
            options,
            plan,
            run);
    }

    /// <summary>
    /// Writes the selected views over one analysis-set result.
    /// </summary>
    internal static int WriteAnalysisSet(
        string name,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options,
        DiffAnalysisPlan plan,
        AnalysisSetRun run)
    {
        AnalysisSetValidationResult.Accepted selection = plan.Selection;
        bool selectsApi = DiffAnalysisViewAdmission.IncludesApi(selection);
        InspectionEnvelope<DiffAnalysisDocument> inspection = run.Inspection;
        DiffAnalysisDocument document = inspection.Content;

        IReadOnlyList<ApiDiffInspectionFailure> inspectionFailures =
            selectsApi
                ? ApiDiffAnalyzer.ProjectInspectionFailures(
                    fromSurface,
                    toSurface)
                : [];
        bool failed = document.Outcomes.Any(static outcome =>
                outcome.Kind == DiffAnalysisDocumentOutcomeKind.Failed)
            || inspectionFailures.Count > 0;

        DiffAnalysisSummaryView? summaryView =
            plan.Views.Contains(DiffSections.Summary.Name)
                ? DiffOutputFormatter.BuildAnalysisSummaryView(
                    name,
                    [
                        .. document.Summary.GetValueOrDefault()
                            .Select(ToView),
                    ],
                    fromVersion,
                    toVersion)
                : null;
        TransitionsView? transitionsView =
            plan.Views.Contains(DiffSections.Transitions.Name)
                ? DiffOutputFormatter.BuildTransitionsView(
                    name,
                    [
                        .. document.Transitions.GetValueOrDefault()
                            .Select(ToView),
                    ],
                    fromVersion,
                    toVersion)
                : null;
        ApiDiff? changes = null;
        if (plan.Views.Contains(DiffSections.Changes.Name)
            && document.GetApiChanges() is { } apiChanges)
        {
            changes = apiChanges;
        }

        // Every analysis that did not compare stays visible, whichever views
        // are selected. The Changes view never stands in for an api
        // analysis that did not compare; other selected views still render.
        WriteAnalysisDiagnostics(inspection.Diagnostics);
        bool changesUnavailable =
            plan.Views.Contains(DiffSections.Changes.Name)
            && changes is null;
        if (changesUnavailable)
        {
            CommandError.Write(
                "The Changes view requires a compared 'api' analysis, "
                + "and 'api' did not compare; see the diagnostic above.");
            failed = true;
        }

        if (options.EnvelopeOutput || options.JsonOutput)
        {
            return DiffAnalysisOutput.Write(inspection, options)
                ? failed ? 1 : 0
                : 1;
        }

        if (changesUnavailable && plan.Views is [_])
            return 1;

        if (plan.Views is [var onlyView])
        {
            if (options.NameOnly)
            {
                Console.WriteLine(
                    RenderAnalysisChanges(
                        name,
                        document.Changes!,
                        changes ?? new ApiDiff(),
                        fromVersion,
                        toVersion,
                        options));
                WriteIncompleteComparisonDiagnostic(inspectionFailures);
                return failed ? 1 : 0;
            }
            if (options.Tabular || options.Tsv || options.Jsonl)
            {
                object view = onlyView == DiffSections.Summary.Name
                    ? summaryView!
                    : onlyView == DiffSections.Transitions.Name
                        ? transitionsView!
                        : plan.DetailedChanges
                            ? BuildAnalysisDetailedChangesView(
                                name,
                                document.Changes!,
                                changes ?? new ApiDiff(),
                                fromVersion,
                                toVersion,
                                options)
                            : BuildAnalysisChangesTableView(
                                name,
                                document.Changes!,
                                changes ?? new ApiDiff(),
                                fromVersion,
                                toVersion,
                                options);
                OutputFormatter.WriteProjectedTable(
                    Console.Out,
                    !options.NoHeader,
                    options.Tsv,
                    options.Jsonl,
                    options.Columns,
                    options.Fields,
                    (writer, formatter, writerOptions) =>
                        SerializeAnalysisView(view, writer, formatter, writerOptions),
                    options.Rows);
                WriteIncompleteComparisonDiagnostic(inspectionFailures);
                return failed ? 1 : 0;
            }
            if (inspectionFailures.Count == 0)
            {
                Console.WriteLine(
                    onlyView == DiffSections.Summary.Name
                        ? DiffOutputFormatter.RenderAnalysisSummaryView(
                            summaryView!,
                            OutputFormatter.CreateWindowedOptions(options.Rows))
                        : onlyView == DiffSections.Transitions.Name
                            ? DiffOutputFormatter.RenderTransitionsView(
                                transitionsView!,
                                OutputFormatter.CreateWindowedOptions(options.Rows))
                            : RenderAnalysisChanges(
                                name,
                                document.Changes!,
                                changes ?? new ApiDiff(),
                                fromVersion,
                                toVersion,
                                options));
                return failed ? 1 : 0;
            }
        }

        DiffDetailedChangesView? changesView = changes is null
            ? null
            : BuildAnalysisDetailedChangesView(
                name,
                document.Changes!,
                changes,
                fromVersion,
                toVersion,
                options);
        Console.WriteLine(
            DiffOutputFormatter.RenderDocumentView(
                DiffOutputFormatter.BuildDocumentView(
                    name,
                    fromVersion,
                    toVersion,
                    changesView,
                    analysisDiff: null,
                    implementationDiff: null,
                    inspectionFailures,
                    summary: summaryView,
                    transitions: transitionsView),
                OutputFormatter.CreateWindowedOptions(options.Rows)));
        return failed ? 1 : 0;
    }

    private static DiffDetailedChangesView BuildAnalysisDetailedChangesView(
        string name,
        DiffAnalysisChangesDocument document,
        ApiDiff changes,
        string fromVersion,
        string toVersion,
        DiffOptions options)
    {
        IReadOnlyList<TypeDiff> classified =
            FilterAnalysisClassifiedChanges(changes, options);
        var classifiedByType = classified.ToDictionary(
            type => type.TypeFullName,
            StringComparer.Ordinal);
        bool includeUnclassified = !options.Breaking && !options.Additive;
        int typeCount = ChangedTypeCount(
            document,
            classifiedByType,
            includeUnclassified);
        return DiffOutputFormatter.BuildDetailedChangesView(
            name,
            classified,
            fromVersion,
            toVersion,
            includeUnclassified
                ? [
                    .. document.Types.SelectMany(type =>
                        type.UnclassifiedChanges.Select(change =>
                            new DiffOutputFormatter.OrderedDetailedChangeRow(
                                type.Type,
                                new DiffDetailedChangeRow(
                                    DiffViewText.Field("~"),
                                    DiffViewText.Field("unclassified"),
                                    DiffViewText.Field(
                                        TypeMatcher.GetSimpleName(type.Type)),
                                    DiffViewText.Field(change.Member ?? ""),
                                    DiffViewText.Field(change.Kind.ToString()),
                                    DiffViewText.Field(change.Detail),
                                    InertString.Empty,
                                    InertString.Empty)))),
                ]
                : [],
            AnalysisChangesSummary(classified, typeCount));
    }

    private static DiffTableView BuildAnalysisChangesTableView(
        string name,
        DiffAnalysisChangesDocument document,
        ApiDiff changes,
        string fromVersion,
        string toVersion,
        DiffOptions options)
    {
        IReadOnlyList<TypeDiff> classified =
            FilterAnalysisClassifiedChanges(changes, options);
        var classifiedByType = classified.ToDictionary(
            type => type.TypeFullName,
            StringComparer.Ordinal);
        bool includeUnclassified = !options.Breaking && !options.Additive;
        List<DiffOutputFormatter.OrderedTableRow> additionalRows =
        [
            .. document.Types
                .Where(type =>
                    includeUnclassified
                    && !classifiedByType.ContainsKey(type.Type)
                    && !type.UnclassifiedChanges.IsEmpty)
                .Select(type =>
                    new DiffOutputFormatter.OrderedTableRow(
                        type.Type,
                        new DiffTableRow(
                            "~",
                            TypeMatcher.GetSimpleName(type.Type),
                            "unclassified API changes"))),
        ];
        int typeCount = ChangedTypeCount(
            document,
            classifiedByType,
            includeUnclassified);
        return DiffOutputFormatter.BuildTableView(
            name,
            classified,
            fromVersion,
            toVersion,
            additionalRows,
            AnalysisChangesSummary(classified, typeCount));
    }

    private static string RenderAnalysisChanges(
        string name,
        DiffAnalysisChangesDocument document,
        ApiDiff changes,
        string fromVersion,
        string toVersion,
        DiffOptions options)
    {
        IReadOnlyList<TypeDiff> classified =
            FilterAnalysisClassifiedChanges(changes, options);
        var classifiedByType = classified.ToDictionary(
            type => type.TypeFullName,
            StringComparer.Ordinal);
        bool includeUnclassified = !options.Breaking && !options.Additive;
        int typeCount = ChangedTypeCount(
            document,
            classifiedByType,
            includeUnclassified);

        if (options.NameOnly)
        {
            return OutputFormatter.RenderTable(
                showHeader: false,
                (writer, formatter) =>
                {
                    var nameWriter = new MarkoutWriter(
                        writer,
                        formatter,
                        OutputFormatter.CreateTableWriterOptions(
                            options.Tsv,
                            options.Jsonl));
                    foreach (DiffAnalysisChangedType type in document.Types)
                    {
                        if (classifiedByType.ContainsKey(type.Type)
                            || includeUnclassified
                                && !type.UnclassifiedChanges.IsEmpty)
                        {
                            nameWriter.WriteListItem(
                                CSharpIdentifier.ContainRenderedText(
                                    type.Type));
                        }
                    }
                    nameWriter.Flush();
                });
        }

        return DiffOutputFormatter.RenderFullMarkdown(
            name,
            classified,
            includeUnclassified
                ? [
                    .. document.Types
                        .SelectMany(type => type.UnclassifiedChanges)
                        .Select(change => new DiffChangeRow(
                            DiffViewText.Field(
                                TypeMatcher.GetSimpleName(change.Type)),
                            DiffViewText.Field(change.Detail))),
                ]
                : [],
            AnalysisChangesSummary(classified, typeCount),
            fromVersion,
            toVersion,
            OutputFormatter.CreateWindowedOptions(options.Rows));
    }

    private static IReadOnlyList<TypeDiff> FilterAnalysisClassifiedChanges(
        ApiDiff changes,
        DiffOptions options)
    {
        if (!options.Breaking && !options.Additive)
            return changes.TypeDiffs;

        List<TypeDiff> filtered =
        [
            .. changes.TypeDiffs.Select(type => new TypeDiff(
                type.TypeFullName,
                [
                    .. type.Changes.Where(change =>
                        options.Breaking
                            && change.Classification
                                == ChangeClassification.Breaking
                        || options.Additive
                            && change.Classification
                                == ChangeClassification.Additive),
                ]))
                .Where(type => type.Changes.Count > 0),
        ];
        if (changes.TypeDiffs.Count > 0 && filtered.Count == 0)
        {
            CommandError.WriteNote(
                "classification filter removed all changes after "
                    + "type/member filters.");
        }
        return filtered;
    }

    private static int ChangedTypeCount(
        DiffAnalysisChangesDocument document,
        IReadOnlyDictionary<string, TypeDiff> classified,
        bool includeUnclassified)
        => document.Types.Count(type =>
            classified.ContainsKey(type.Type)
            || includeUnclassified && !type.UnclassifiedChanges.IsEmpty);

    private static string AnalysisChangesSummary(
        IReadOnlyList<TypeDiff> classified,
        int typeCount)
    {
        if (typeCount == 0)
            return "no changes";
        int breaking = classified.Sum(type => type.BreakingCount);
        int additive = classified.Sum(type => type.AdditiveCount);
        int potentiallyBreaking =
            classified.Sum(type => type.PotentiallyBreakingCount);
        string counts =
            breaking == 0 && additive == 0 && potentiallyBreaking == 0
                ? "unclassified API changes"
                : DiffOutputFormatter.FormatSummaryCounts(
                    breaking,
                    additive,
                    potentiallyBreaking);
        return $"{counts} across {typeCount} types";
    }

    static void SerializeAnalysisView(
        object view,
        TextWriter writer,
        IMarkoutFormatter formatter,
        MarkoutWriterOptions writerOptions)
    {
        switch (view)
        {
            case DiffAnalysisSummaryView summary:
                MarkoutSerializer.Serialize(summary, writer, formatter, DiffViewContext.Default, writerOptions);
                break;
            case TransitionsView transitions:
                MarkoutSerializer.Serialize(transitions, writer, formatter, DiffViewContext.Default, writerOptions);
                break;
            case DiffDetailedChangesView changes:
                MarkoutSerializer.Serialize(changes, writer, formatter, DiffViewContext.Default, writerOptions);
                break;
            case DiffTableView changes:
                MarkoutSerializer.Serialize(changes, writer, formatter, DiffViewContext.Default, writerOptions);
                break;
            default:
                throw new InvalidOperationException("Unknown Diff analysis view.");
        }
    }

    private static DiffAnalysisDocumentViews DocumentViews(
        IReadOnlyList<string> views)
    {
        DiffAnalysisDocumentViews result = DiffAnalysisDocumentViews.None;
        if (views.Contains(DiffSections.Changes.Name))
            result |= DiffAnalysisDocumentViews.Changes;
        if (views.Contains(DiffSections.Summary.Name))
            result |= DiffAnalysisDocumentViews.Summary;
        if (views.Contains(DiffSections.Transitions.Name))
            result |= DiffAnalysisDocumentViews.Transitions;
        return result;
    }

    private static DotnetInspect.Cli.Views.DiffAnalysisSummaryRow ToView(
        DotnetInspector.ResearchSections.DiffAnalysisSummaryRow row)
        => new(
            row.Analysis,
            row.Outcome.ToString(),
            row.Added,
            row.Removed,
            row.Changed,
            row.Present,
            row.Detail);

    private static FindingTransitionRow ToView(
        DiffAnalysisTransitionRow row)
    {
        var view = new FindingTransitionRow(
            row.Transition,
            row.Finding,
            row.Target,
            row.From,
            row.To,
            row.Old,
            row.New,
            row.Detail);
        return row.OldInspection is not null
                && row.NewInspection is not null
            ? view.WithInspectionStates(
                row.OldInspection,
                row.NewInspection)
            : view;
    }
}
