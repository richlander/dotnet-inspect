using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;
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
        IReadOnlyList<string> Views);

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
        if (options.EnvelopeOutput || options.JsonOutput)
        {
            CommandError.Write(
                "--envelope and --json are not yet supported for the "
                + "analysis-set result; its JSON transport lands with its "
                + "Browser/Wasm adoption.");
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
        bool selectsApi = selection.Analyses.Any(IsApi);
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

        if (views.Contains(DiffSections.Changes.Name) && !selectsApi)
        {
            CommandError.Write(
                "The Changes view projects the 'api' analysis, which is not "
                + "selected; add --analysis api or select -S Transitions.");
            return false;
        }
        if (views.Contains(DiffSections.Transitions.Name)
            && surface == AnalysisReportSurfaceKind.Library)
        {
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
            && selection.Analyses is [var single]
            && IsApi(single))
        {
            return true;
        }

        plan = new DiffAnalysisPlan(selection, views);
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

    static void WriteNonComparedOutcomes(DiffAnalysisResult result)
    {
        foreach (DiffAnalysisOutcome outcome in result.Outcomes)
        {
            switch (outcome)
            {
                case DiffAnalysisOutcome.Failed failed:
                    CommandError.Write(
                        $"Analysis '{failed.Analysis.Id.Value}' failed: "
                        + failed.Diagnostic);
                    break;
                case DiffAnalysisOutcome.Unavailable unavailable:
                    CommandError.WriteWarning(
                        $"Analysis '{unavailable.Analysis.Id.Value}' was not "
                        + $"compared: {unavailable.Reason}");
                    break;
            }
        }
    }

    static bool IsApi(AnalysisDescriptor analysis)
        => analysis.ParticipationFor(AnalysisOperationKind.Compare)
            ?.Surfaces.Any(surface => surface.ProducerRoute
                == DiffAnalysisCatalog.ApiRoute) == true;

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
                    fromPaths,
                    toPaths,
                    fromSurface,
                    toSurface,
                    fromVersion,
                    toVersion,
                    planned,
                    plan.Selection)
                .Projected
                .SelectMany(entry => entry.Rows),
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
        DiffAnalysisResult Result,
        IReadOnlyList<(DiffAnalysisOutcome Outcome, IReadOnlyList<FindingTransitionRow> Rows)>
            Projected);

    /// <summary>
    /// Resolves request targets, runs every selected analysis's registered
    /// producer once, and projects each outcome's transitions. A target
    /// resolution failure throws before any producer runs.
    /// </summary>
    static AnalysisSetRun RunAnalysisSet(
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        ApiSurface fromSurface,
        ApiSurface toSurface,
        string fromVersion,
        string toVersion,
        DiffOptions options,
        AnalysisSetValidationResult.Accepted selection)
    {
        AnalysisReportSurfaceKind surface = selection.Surface;
        string[] bodyTargeted =
        [
            .. selection.Analyses
                .Where(analysis => analysis.ParticipationFor(
                        AnalysisOperationKind.Compare)
                    ?.For(surface)?.ProducerRoute is { } route
                    && (route == DiffAnalysisCatalog.RetainedResearchRoute
                        || route == DiffAnalysisCatalog.BodySignalRoute))
                .Select(analysis => analysis.Id.Value),
        ];

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
                    options.TypeFilter,
                    requireBodyTargets: bodyTargeted.Length > 0,
                    bodySectionName: bodyTargeted.Length > 0
                        ? $"--analysis {bodyTargeted[0]}"
                        : "--analysis");
            }
            catch (InvalidOperationException ex)
                when (ex is not DiffAnalysisTargetException)
            {
                throw new DiffAnalysisTargetException(ex.Message);
            }
        }
        IReadOnlyList<string> typeNames = surface switch
        {
            AnalysisReportSurfaceKind.Type => ResolveFindingTypeNames(
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
                "--analysis"));
        DiffAnalysisResult result = DiffAnalysisOperation.Execute(
            DiffAnalysisCommandCapability.Catalog,
            selection,
            input);

        var scope = new TransitionScope(
            surface,
            [.. typeNames],
            targets,
            fromVersion,
            toVersion);
        return new AnalysisSetRun(
            result,
            [
                .. result.Outcomes.Select(outcome =>
                    (outcome, TransitionRows(outcome, scope))),
            ]);
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
            inputs.FromPaths,
            inputs.ToPaths,
            inputs.FromSurface,
            inputs.ToSurface,
            inputs.FromVersion,
            inputs.ToVersion,
            options,
            plan.Selection);
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
        bool selectsApi = selection.Analyses.Any(IsApi);
        DiffAnalysisResult result = run.Result;
        var projected = run.Projected;

        IReadOnlyList<ApiDiffInspectionFailure> inspectionFailures =
            selectsApi
                ? ApiDiffAnalyzer.ProjectInspectionFailures(
                    fromSurface,
                    toSurface)
                : [];
        bool failed = result.Outcomes.Any(static outcome =>
                outcome is DiffAnalysisOutcome.Failed)
            || inspectionFailures.Count > 0;

        DiffAnalysisSummaryView? summaryView =
            plan.Views.Contains(DiffSections.Summary.Name)
                ? DiffOutputFormatter.BuildAnalysisSummaryView(
                    name,
                    [.. projected.Select(entry => SummaryRow(entry.Outcome, entry.Rows))],
                    fromVersion,
                    toVersion)
                : null;
        TransitionsView? transitionsView =
            plan.Views.Contains(DiffSections.Transitions.Name)
                ? DiffOutputFormatter.BuildTransitionsView(
                    name,
                    [.. projected.SelectMany(entry => entry.Rows)],
                    fromVersion,
                    toVersion)
                : null;
        ApiDiff? changes = null;
        if (plan.Views.Contains(DiffSections.Changes.Name)
            && result.Outcomes.FirstOrDefault(outcome => IsApi(outcome.Analysis))
                is DiffAnalysisOutcome.Compared
                {
                    Comparison: KeyedFindingComparison.Api api,
                })
        {
            changes = BuildApiDiff(
                api.Comparison,
                fromSurface,
                toSurface,
                options);
        }

        // Every analysis that did not compare stays visible, whichever views
        // are selected. The Changes view never stands in for an api
        // analysis that did not compare; other selected views still render.
        WriteNonComparedOutcomes(result);
        if (plan.Views.Contains(DiffSections.Changes.Name) && changes is null)
        {
            CommandError.Write(
                "The Changes view requires a compared 'api' analysis, "
                + "and 'api' did not compare; see the diagnostic above.");
            if (plan.Views is [_])
                return 1;
            failed = true;
        }

        if (plan.Views is [var onlyView])
        {
            if (options.Tabular || options.Tsv || options.Jsonl)
            {
                object view = onlyView == DiffSections.Summary.Name
                    ? summaryView!
                    : onlyView == DiffSections.Transitions.Name
                        ? transitionsView!
                        : DiffOutputFormatter.BuildDetailedChangesView(
                            name,
                            ApplyFilters(changes ?? new ApiDiff(), options),
                            fromVersion,
                            toVersion);
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
            if (inspectionFailures.Count == 0 || options.NameOnly)
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
                            : RenderDiff(
                                name,
                                changes ?? new ApiDiff(),
                                fromVersion,
                                toVersion,
                                options));
                if (options.NameOnly)
                    WriteIncompleteComparisonDiagnostic(inspectionFailures);
                return failed ? 1 : 0;
            }
        }

        DiffDetailedChangesView? changesView = changes is null
            ? null
            : DiffOutputFormatter.BuildDetailedChangesView(
                name,
                ApplyFilters(changes, options),
                fromVersion,
                toVersion);
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
            default:
                throw new InvalidOperationException("Unknown Diff analysis view.");
        }
    }

    /// <summary>The request surface a Transitions or Summary view projects.</summary>
    sealed record TransitionScope(
        AnalysisReportSurfaceKind Surface,
        HashSet<string> TypeNames,
        ResolvedDiffMemberTargets? MemberTargets,
        string FromVersion,
        string ToVersion);

    static DiffAnalysisSummaryRow SummaryRow(
        DiffAnalysisOutcome outcome,
        IReadOnlyList<FindingTransitionRow> rows)
    {
        int Count(PairKind kind)
            => rows.Count(row => row.Transition == $"PairFinding.{kind}");
        return outcome switch
        {
            DiffAnalysisOutcome.Compared => new DiffAnalysisSummaryRow(
                outcome.Identity,
                "Compared",
                Count(PairKind.Added),
                Count(PairKind.Removed),
                Count(PairKind.Changed),
                Count(PairKind.Present),
                rows.FirstOrDefault(row =>
                    row.Transition == "FindingComparison.Failed")?.Detail
                    ?? (outcome is DiffAnalysisOutcome.Compared
                        {
                            Comparison: KeyedFindingComparison.Api
                            {
                                Comparison.ApiDiff.InspectionFailures.Count: > 0 and var failures,
                            },
                        }
                        ? $"incomplete: {failures} metadata inspection failure(s)"
                        : null)),
            DiffAnalysisOutcome.Unavailable unavailable => new DiffAnalysisSummaryRow(
                outcome.Identity,
                "Unavailable",
                0,
                0,
                0,
                0,
                unavailable.Reason),
            DiffAnalysisOutcome.Failed failure => new DiffAnalysisSummaryRow(
                outcome.Identity,
                "Failed",
                0,
                0,
                0,
                0,
                failure.Diagnostic),
            _ => throw new InvalidOperationException("Unknown Diff analysis outcome."),
        };
    }

    /// <summary>
    /// Projects one outcome's per-Finding transitions over the request
    /// surface, in the analysis's descriptor declaration order.
    /// </summary>
    static IReadOnlyList<FindingTransitionRow> TransitionRows(
        DiffAnalysisOutcome outcome,
        TransitionScope scope)
    {
        string fromVersion = scope.FromVersion;
        string toVersion = scope.ToVersion;
        string descriptors = string.Join(
            ", ",
            outcome.Participation.Descriptors.Select(descriptor => descriptor.Id));
        switch (outcome)
        {
            case DiffAnalysisOutcome.Unavailable unavailable:
                return [new FindingTransitionRow(
                    "Unavailable", descriptors, outcome.Identity,
                    fromVersion, toVersion, "n/a", "n/a", unavailable.Reason)];
            case DiffAnalysisOutcome.Failed failure:
                return [new FindingTransitionRow(
                    "Failed", descriptors, outcome.Identity,
                    fromVersion, toVersion, "n/a", "n/a", failure.Diagnostic)];
        }

        var compared = (DiffAnalysisOutcome.Compared)outcome;
        List<FindingTransitionRow> rows = [];
        foreach (FindingDescriptor descriptor in outcome.Participation.Descriptors)
        {
            IEnumerable<FindingTransitionRow> descriptorRows = compared.Comparison switch
            {
                KeyedFindingComparison.Api api =>
                    ApiTransitionRows(api.Comparison, descriptor, scope),
                KeyedFindingComparison.Retained retained =>
                    RetainedTransitionRows(retained.Comparisons, descriptor, scope),
                _ => throw new InvalidOperationException(
                    "Unknown keyed Finding comparison."),
            };
            rows.AddRange(descriptorRows);
        }
        return rows;
    }

    // The api comparison is library-wide, so its whole-comparison topology
    // marker (FindingComparison.Complete with no pairs) is not a fact about
    // the request surface; failed comparisons still project as rows.
    static IEnumerable<FindingTransitionRow> ApiTransitionRows(
        ApiFindingComparison comparison,
        FindingDescriptor descriptor,
        TransitionScope scope)
        => ApiComparisonTransitionRows(comparison, descriptor, scope)
            .Where(row => row.Transition != "FindingComparison.Complete");

    static IEnumerable<FindingTransitionRow> ApiComparisonTransitionRows(
        ApiFindingComparison comparison,
        FindingDescriptor descriptor,
        TransitionScope scope)
    {
        string fromVersion = scope.FromVersion;
        string toVersion = scope.ToVersion;
        if (descriptor.Id == MetadataFindings.TypeDescriptor.Id)
        {
            return ComparisonRows(
                    comparison.Types,
                    MetadataFindings.TypeDescriptor,
                    "API surface",
                    fromVersion,
                    toVersion,
                    emitEmptyComparison: false,
                    pair => ToTypeTransitionRow(pair, fromVersion, toVersion),
                    scope.Surface == AnalysisReportSurfaceKind.Library
                        ? null
                        : pair => scope.TypeNames.Contains(TypeTarget(pair)))
                .OrderBy(row => row.Target, StringComparer.Ordinal);
        }
        if (descriptor.Id == MetadataFindings.MemberDescriptor.Id)
        {
            Func<PairFinding<ApiMemberHandle>, bool>? include = scope.Surface switch
            {
                AnalysisReportSurfaceKind.Library => null,
                AnalysisReportSurfaceKind.Type =>
                    pair => scope.TypeNames.Contains(MemberTypeTarget(pair)),
                _ => pair => MatchesMemberPair(
                    pair,
                    scope.MemberTargets
                        ?? throw new InvalidOperationException(
                            "Member targets were not resolved.")),
            };
            return ComparisonRows(
                    comparison.Members,
                    MetadataFindings.MemberDescriptor,
                    "API surface",
                    fromVersion,
                    toVersion,
                    emitEmptyComparison: false,
                    pair => ToMemberTransitionRow(pair, fromVersion, toVersion),
                    include)
                .OrderBy(row => row.Target, StringComparer.Ordinal);
        }
        throw new InvalidOperationException(
            $"The api comparison carries no '{descriptor.Id}' Findings.");
    }

    static IEnumerable<FindingTransitionRow> RetainedTransitionRows(
        RetainedFindingComparisonSet comparisons,
        FindingDescriptor descriptor,
        TransitionScope scope)
    {
        string fromVersion = scope.FromVersion;
        string toVersion = scope.ToVersion;
        IEnumerable<FindingTransitionRow> Rows<T>(
            bool emitEmptyComparison,
            Func<ResearchSubjectKey, PairFinding<T>, string, string, FindingTransitionRow>
                toTransitionRow)
            where T : notnull
            => comparisons.Get<T>(descriptor)
                .SelectMany(comparison => RetainedComparisonRows(
                    comparison,
                    fromVersion,
                    toVersion,
                    emitEmptyComparison,
                    toTransitionRow))
                .OrderBy(row => row.Target, StringComparer.Ordinal)
                .ThenBy(row => row.Transition, StringComparer.Ordinal);

        return descriptor.Id switch
        {
            var id when id == MetadataFindings.AttributeDescriptor.Id =>
                Rows<ApiAttributeHandle>(
                    emitEmptyComparison: false,
                    (_, pair, from, to) => ToAttributeTransitionRow(pair, from, to)),
            var id when id == AnalysisFindings.AllocationDescriptor.Id =>
                Rows<AllocationOccurrence>(false, ToAllocationTransitionRow),
            var id when id == AnalysisFindings.CallSiteDescriptor.Id =>
                Rows<DirectCall>(false, ToCallSiteTransitionRow),
            var id when id == AnalysisFindings.UnsafetyDescriptor.Id =>
                Rows<UnsafetyOccurrence>(false, ToUnsafetyTransitionRow),
            var id when id == CSharpFindings.LineDescriptor.Id =>
                Rows<CSharpCanonicalLine>(true, ToCSharpTransitionRow),
            var id when id == IlFindings.OperationDescriptor.Id =>
                Rows<CanonicalIlOperation>(true, ToIlTransitionRow),
            _ => throw new InvalidOperationException(
                $"No Transitions projection is registered for '{descriptor.Id}'."),
        };
    }
}
