using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class DiffHistoryOutput
{
    internal static DiffHistoryDocumentView Project(
        DiffHistoryOutcome.Available available) =>
        available.Document switch
        {
            DiffHistoryDocument.ApiMembers value =>
                Project(
                    value.Content.Population,
                    value.Content.TypeFullName,
                    member: null,
                    MetadataFindings.MemberDescriptor.Id,
                    value.Content.EvaluationPlan,
                    value.Content.UsedProbeCount,
                    value.Content.AuthorizedProbeCount,
                    value.Content.TerminalOutcome,
                    value.Content.NextActions,
                    value.Content.Scope,
                    value.Content.TargetContext,
                    value.Content.ReplayContext,
                    ProjectEvaluations(
                        value.Content.Evaluations,
                        static evaluation => evaluation.Address,
                        static evaluation => evaluation.Version,
                        static evaluation => evaluation.Inspection),
                    ProjectProbes(
                        value.Content.Probes,
                        static probe => probe.Step,
                        static probe => probe.Purpose,
                        static probe => probe.SelectedInterval,
                        static probe => probe.Evaluation.Address,
                        static probe => probe.Evaluation.Version,
                        static probe => probe.Evaluation.Inspection,
                        static probe => probe.Learning),
                    ProjectTransitions(
                        value.Content.Transitions,
                        MetadataFindings.MemberDescriptor.Id,
                        static finding => finding.Payload.Identity),
                    ProjectChangedVersions(
                        value.Content.ChangedVersions)),
            DiffHistoryDocument.ApiTypes value =>
                ProjectApiFinding(
                    value.Content,
                    member: null,
                    MetadataFindings.TypeDescriptor.Id,
                    static finding => finding.Payload.TypeFullName),
            DiffHistoryDocument.ApiAttributes value =>
                ProjectApiFinding(
                    value.Content,
                    member: null,
                    MetadataFindings.AttributeDescriptor.Id,
                    static finding => finding.Payload.Attribute),
            DiffHistoryDocument.ExactApiMember value =>
                ProjectApiFinding(
                    value.Content.History,
                    value.Content.Selection.Member?.Identity
                        ?? value.Content.Selection.Selector.NormalizedSelector,
                    MetadataFindings.MemberDescriptor.Id,
                    static finding => finding.Payload.Identity),
            DiffHistoryDocument.Allocations value =>
                ProjectAnalysis(
                    value.Content,
                    AnalysisFindings.AllocationDescriptor.Id,
                    static finding => FindingTargetFormatter.Format(finding)),
            DiffHistoryDocument.CallSites value =>
                ProjectAnalysis(
                    value.Content,
                    AnalysisFindings.CallSiteDescriptor.Id,
                    static finding => FindingTargetFormatter.Format(finding)),
            DiffHistoryDocument.Unsafety value =>
                ProjectAnalysis(
                    value.Content,
                    AnalysisFindings.UnsafetyDescriptor.Id,
                    static finding => FindingTargetFormatter.Format(finding)),
            _ => throw new InvalidOperationException(
                "Unknown Diff History document."),
        };

    internal static int WriteProjected(
        DiffHistoryDocumentView view,
        DiffOptions options,
        HashSet<string> selectedSections)
    {
        if (!ProjectionDiagnostics.ValidateProjection(
                DiffHistorySections.CreateSchema(),
                selectedSections,
                options.Fields,
                options.Columns))
        {
            return 1;
        }
        if (!TryApplyRowSelection(
                view,
                selectedSections,
                options.SemanticRowSelection,
                out DiffHistoryDocumentView selected))
        {
            return 1;
        }

        selected = SelectSections(selected, selectedSections);
        if (options.JsonOutput)
        {
            if (ProjectionAudit.RejectUnloweredJson(
                    options,
                    options.JsonOutput))
            {
                return 1;
            }
            Console.WriteLine(JsonSerializer.Serialize(
                selected,
                DiffHistoryViewJsonContext.Default
                    .DiffHistoryDocumentView));
            return 0;
        }

        if (options.Tabular || options.Tsv || options.Jsonl)
        {
            string section = selectedSections.Single();
            WriteTable(section, selected, options);
            return 0;
        }

        var writer = new MarkoutWriter(new MarkdownFormatter());
        DiffHistoryViewContext.Default.Serialize(selected, writer);
        Console.WriteLine(writer.ToString().TrimEnd());
        return 0;
    }

    internal static int WriteCount(
        DiffHistorySectionAvailable available,
        DiffOptions options)
    {
        switch (available.Count)
        {
            case SectionCountOutcome<
                    DiffHistoryCountCohort,
                    DiffHistoryChangedVersionCountEvidence>.Completed
                    completed:
                CountOutput.WriteCount(completed.Counts.Single().Value);
                return 0;
            case SectionCountOutcome<
                    DiffHistoryCountCohort,
                    DiffHistoryChangedVersionCountEvidence>.SourceForCount:
                CommandError.Write(
                    "The selected evaluations do not establish an exact Changed Versions count.");
                return 1;
            case SectionCountOutcome<
                    DiffHistoryCountCohort,
                    DiffHistoryChangedVersionCountEvidence>.Semantic semantic:
                CommandError.Write(
                    $"Count row selection stage {semantic.StageNumber} requires "
                    + $"position {semantic.RequiredPosition}, but only "
                    + $"{semantic.AvailableCount} Changed Versions are available.");
                return 1;
            case null:
                CommandError.Write(
                    "Diff History completed without its requested Count component.");
                return 1;
            default:
                throw new InvalidOperationException(
                    "Unknown Diff History Count outcome.");
        }
    }

    static DiffHistoryDocumentView ProjectApiFinding<T>(
        DiffHistoryApiFindingDocument<T> content,
        string? member,
        string finding,
        Func<Finding<T>, string> target)
        where T : notnull =>
        Project(
            content.Population,
            content.TypeFullName,
            member,
            finding,
            content.EvaluationPlan,
            content.UsedProbeCount,
            content.AuthorizedProbeCount,
            content.TerminalOutcome,
            content.NextActions,
            content.Scope,
            content.TargetContext,
            content.ReplayContext,
            ProjectEvaluations(
                content.Evaluations,
                static evaluation => evaluation.Address,
                static evaluation => evaluation.Version,
                static evaluation => evaluation.Inspection),
            ProjectProbes(
                content.Probes,
                static probe => probe.Step,
                static probe => probe.Purpose,
                static probe => probe.SelectedInterval,
                static probe => probe.Evaluation.Address,
                static probe => probe.Evaluation.Version,
                static probe => probe.Evaluation.Inspection,
                static probe => probe.Learning),
            ProjectTransitions(
                content.Transitions,
                finding,
                target),
            ProjectChangedVersions(content.ChangedVersions));

    static DiffHistoryDocumentView ProjectAnalysis<T>(
        DiffHistoryAnalysisDocument<T> content,
        string finding,
        Func<Finding<T>, string> target)
        where T : notnull =>
        Project(
            content.Population,
            content.Selector.Type,
            content.SourceReceipt?.Member.StableSelector
                ?? content.Selector.Member,
            finding,
            content.EvaluationPlan,
            content.UsedProbeCount,
            content.AuthorizedProbeCount,
            content.TerminalOutcome,
            content.NextActions,
            content.Selector.IncludeAll
                ? ApiSurfaceScope.IncludeAll
                : ApiSurfaceScope.Public,
            content.TargetContext,
            content.ReplayContext,
            ProjectEvaluations(
                content.Evaluations,
                static evaluation => evaluation.Address,
                static evaluation => evaluation.Version,
                static evaluation => evaluation.Inspection),
            ProjectProbes(
                content.Probes,
                static probe => probe.Step,
                static probe => probe.Purpose,
                static probe => probe.SelectedInterval,
                static probe => probe.Evaluation.Address,
                static probe => probe.Evaluation.Version,
                static probe => probe.Evaluation.Inspection,
                static probe => probe.Learning),
            ProjectTransitions(
                content.Transitions,
                finding,
                target),
            ProjectChangedVersions(content.ChangedVersions));

    static DiffHistoryDocumentView Project(
        DotnetInspector.Packages.PackageVersionVector population,
        string type,
        string? member,
        string finding,
        DiffHistoryEvaluationPlan evaluationPlan,
        int usedProbeCount,
        int? authorizedProbeCount,
        DiffHistoryTerminalOutcome terminalOutcome,
        ImmutableArray<DiffHistoryNextAction> nextActions,
        ApiSurfaceScope scope,
        DotnetInspector.Packages.PackageHouseTargetContext targetContext,
        DiffHistoryPackageReplayContext? replayContext,
        List<DiffHistoryEvaluationRowView> evaluations,
        List<DiffHistoryProbeRowView> probes,
        List<DiffHistoryTransitionRowView> transitions,
        List<DiffHistoryChangedVersionRowView> changedVersions) =>
        new()
        {
            Title = "Diff History",
            Range =
                $"{population.PackageId}@"
                + $"{population.Start.ToNormalizedString()}.."
                + population.End.ToNormalizedString(),
            Type = type,
            Member = member,
            Finding = finding,
            Policy = evaluationPlan.Policy.ToString(),
            Outcome =
            [
                ProjectOutcome(
                    terminalOutcome,
                    usedProbeCount,
                    authorizedProbeCount,
                    nextActions,
                    population,
                    type,
                    member,
                    finding,
                    scope,
                    targetContext,
                    replayContext),
            ],
            ProbeTrace = probes,
            Evaluations = evaluations,
            Transitions = transitions,
            ChangedVersions = changedVersions,
        };

    static List<DiffHistoryEvaluationRowView> ProjectEvaluations<T, TEvaluation>(
        IEnumerable<TEvaluation> evaluations,
        Func<TEvaluation, DotnetInspector.Packages.PackageVersionAddress>
            address,
        Func<TEvaluation, FindingVersion> version,
        Func<TEvaluation, FindingInspection<T>> inspection)
        where T : notnull =>
        [
            .. evaluations
                .OrderBy(evaluation => address(evaluation).Position)
                .Select(evaluation =>
                {
                    FindingInspection<T> result = inspection(evaluation);
                    (string state, int? count, string? detail) =
                        DescribeInspection(result);
                    return new DiffHistoryEvaluationRowView(
                        address(evaluation).Selector,
                        version(evaluation).Display,
                        state,
                        count,
                        detail);
                }),
        ];

    static List<DiffHistoryProbeRowView> ProjectProbes<T, TProbe>(
        IEnumerable<TProbe> probes,
        Func<TProbe, int> step,
        Func<TProbe, DiffHistoryProbePurpose> purpose,
        Func<TProbe, DiffHistoryInterval?> selectedInterval,
        Func<TProbe, DotnetInspector.Packages.PackageVersionAddress> address,
        Func<TProbe, FindingVersion> version,
        Func<TProbe, FindingInspection<T>> inspection,
        Func<TProbe, DiffHistoryProbeLearning> learning)
        where T : notnull =>
        [
            .. probes
                .OrderBy(step)
                .Select(probe =>
                {
                    (string state, _, _) =
                        DescribeInspection(inspection(probe));
                    return new DiffHistoryProbeRowView(
                        step(probe),
                        purpose(probe).ToString(),
                        address(probe).Selector,
                        version(probe).Display,
                        selectedInterval(probe) is { } interval
                            ? FormatInterval(interval)
                            : null,
                        state,
                        DescribeLearning(learning(probe)));
                }),
        ];

    static List<DiffHistoryTransitionRowView> ProjectTransitions<T>(
        IEnumerable<DiffHistoryTransition<T>> transitions,
        string finding,
        Func<Finding<T>, string> target)
        where T : notnull
    {
        List<DiffHistoryTransitionRowView> rows = [];
        foreach (DiffHistoryTransition<T> transition in transitions)
        {
            string from = transition.Source.NormalizedVersion;
            string to = transition.Destination.NormalizedVersion;
            string span = transition.IsPopulationAdjacent
                ? "Adjacent"
                : $"Gap ({transition.UnevaluatedBetween.Length})";
            switch (transition.Comparison.Value)
            {
                case FindingComparison<T>.Failed failed:
                    rows.Add(new(
                        from,
                        to,
                        span,
                        "Failed",
                        finding,
                        failed.OldInspection.Value
                            is FindingInspection<T>.Failed oldFailure
                                ? oldFailure.Error.Subject.Display
                                : failed.NewInspection.Value
                                    is FindingInspection<T>.Failed newFailure
                                        ? newFailure.Error.Subject.Display
                                        : "",
                        failed.Failure));
                    break;
                case FindingComparison<T>.Complete complete:
                    if (!complete.Transition.IsSameTopology)
                    {
                        rows.Add(new(
                            from,
                            to,
                            span,
                            $"{complete.Transition.Old}To"
                                + complete.Transition.New,
                            finding,
                            Subject(complete),
                            "The focused inspection state changed."));
                    }

                    int before = rows.Count;
                    foreach (PairFinding<T> pair in complete.Pairs.Where(
                        static pair =>
                            pair.Kind != PairKind.Present
                            || pair.Difference != FindingDifferenceKind.None))
                    {
                        var sides = (IPairFinding)pair;
                        Finding<T>? atom =
                            sides.New as Finding<T>
                            ?? sides.Old as Finding<T>;
                        rows.Add(new(
                            from,
                            to,
                            span,
                            pair.Kind.ToString(),
                            pair.Descriptor.Id,
                            atom is null
                                ? pair.Subject.Display
                                : target(atom),
                            pair.Detail));
                    }
                    if (rows.Count == before
                        && complete.Transition.IsSameTopology)
                    {
                        rows.Add(new(
                            from,
                            to,
                            span,
                            "Unchanged",
                            finding,
                            Subject(complete),
                            null));
                    }
                    break;
            }
        }
        return rows;
    }

    static List<DiffHistoryChangedVersionRowView>
        ProjectChangedVersions<T>(
            IEnumerable<DiffHistoryChangedVersionAssessment<T>>
                assessments)
        where T : notnull =>
        [
            .. assessments
                .Where(static assessment =>
                    assessment.State
                        == DiffHistoryChangedVersionState.Changed)
                .Select(static assessment =>
                    new DiffHistoryChangedVersionRowView(
                        assessment.Destination.Selector,
                        assessment.Destination.NormalizedVersion,
                        assessment.Predecessor.NormalizedVersion,
                        assessment.State.ToString(),
                        null)),
        ];

    static DiffHistoryOutcomeRowView ProjectOutcome(
        DiffHistoryTerminalOutcome terminal,
        int usedProbes,
        int? authorizedProbes,
        ImmutableArray<DiffHistoryNextAction> nextActions,
        DotnetInspector.Packages.PackageVersionVector population,
        string type,
        string? member,
        string finding,
        ApiSurfaceScope scope,
        DotnetInspector.Packages.PackageHouseTargetContext targetContext,
        DiffHistoryPackageReplayContext? replayContext)
    {
        (
            string result,
            string? resolved,
            string? unresolved,
            string? blocked) =
            terminal switch
            {
                DiffHistoryTerminalOutcome.FullPopulationCompleted =>
                    ("Full population completed", null, null, null),
                DiffHistoryTerminalOutcome.ExplicitCheckpointsCompleted =>
                    ("Explicit checkpoints completed", null, null, null),
                DiffHistoryTerminalOutcome.RepresentativeSurveyCompleted =>
                    ("Representative survey completed", null, null, null),
                DiffHistoryTerminalOutcome
                        .MajorVersionRepresentativesCompleted =>
                    ("Major-version sample completed", null, null, null),
                DiffHistoryTerminalOutcome.BoundariesResolved value =>
                    (
                        "Boundaries resolved",
                        FormatIntervals(value.Boundaries),
                        null,
                        null),
                DiffHistoryTerminalOutcome.EqualEndpoints value =>
                    (
                        "Equal endpoints",
                        FormatInterval(value.Endpoints),
                        null,
                        null),
                DiffHistoryTerminalOutcome.BudgetExhausted value =>
                    (
                        "Probe budget exhausted",
                        FormatIntervals(value.ResolvedBoundaries),
                        FormatIntervals(value.UnresolvedIntervals),
                        null),
                DiffHistoryTerminalOutcome.BlockedByFailure value =>
                    (
                        "Blocked by failure",
                        FormatIntervals(value.ResolvedBoundaries),
                        FormatIntervals(value.UnresolvedIntervals),
                        FormatBlocked(value)),
                _ => throw new InvalidOperationException(
                    "Unknown Diff History terminal outcome."),
            };
        return new(
            result,
            authorizedProbes is { } authorized
                ? $"{usedProbes}/{authorized}"
                : usedProbes.ToString(),
            resolved,
            unresolved,
            blocked,
            nextActions.IsEmpty
                ? null
                : string.Join(
                    "; ",
                    nextActions.Select(action => FormatAction(
                        action,
                        population,
                        type,
                        member,
                        finding,
                        scope,
                        targetContext,
                        replayContext))));
    }

    static (string State, int? Count, string? Detail)
        DescribeInspection<T>(FindingInspection<T> inspection)
        where T : notnull =>
        inspection.Value switch
        {
            FindingInspection<T>.Complete complete =>
                ("Complete", complete.Findings.Length, null),
            FindingInspection<T>.Absent absent =>
                (absent.Kind.ToString(), 0, absent.Detail),
            FindingInspection<T>.Failed failed =>
                ("Failed", null, failed.Error.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Finding inspection."),
        };

    static string DescribeLearning(DiffHistoryProbeLearning learning) =>
        learning.ChangedIntervals.IsEmpty
            ? learning.Kind.ToString()
            : $"{learning.Kind}: "
                + FormatIntervals(learning.ChangedIntervals);

    static string FormatBlocked(
        DiffHistoryTerminalOutcome.BlockedByFailure value)
    {
        IEnumerable<string> failed =
            value.FailedAddresses.Select(static address =>
                $"{address.Selector} ({address.NormalizedVersion})");
        IEnumerable<string> intervals =
            value.BlockedIntervals.Select(FormatInterval);
        return string.Join("; ", failed.Concat(intervals));
    }

    static string? FormatIntervals(
        ImmutableArray<DiffHistoryInterval> intervals) =>
        intervals.IsEmpty
            ? null
            : string.Join(", ", intervals.Select(FormatInterval));

    static string FormatInterval(DiffHistoryInterval interval) =>
        $"{interval.Source.NormalizedVersion}.."
        + interval.Destination.NormalizedVersion;

    static string FormatAction(
        DiffHistoryNextAction action,
        DotnetInspector.Packages.PackageVersionVector population,
        string type,
        string? member,
        string finding,
        ApiSurfaceScope scope,
        DotnetInspector.Packages.PackageHouseTargetContext targetContext,
        DiffHistoryPackageReplayContext? replayContext) =>
        action switch
        {
            DiffHistoryNextAction.Probe probe =>
                FormatProbeAction(
                    probe,
                    population,
                    type,
                    member,
                    finding,
                    scope,
                    targetContext,
                    replayContext),
            DiffHistoryNextAction.PairwiseDiff diff =>
                FormatPairwiseAction(diff),
            _ => throw new InvalidOperationException(
                "Unknown Diff History next action."),
        };

    static string FormatProbeAction(
        DiffHistoryNextAction.Probe action,
        DotnetInspector.Packages.PackageVersionVector population,
        string type,
        string? member,
        string finding,
        ApiSurfaceScope scope,
        DotnetInspector.Packages.PackageHouseTargetContext targetContext,
        DiffHistoryPackageReplayContext? replayContext)
    {
        string command =
            "dotnet-inspect diff --history --package "
            + ShellCommandText.Quote(
                $"{population.PackageId}@"
                + $"{population.Start.ToNormalizedString()}.."
                + population.End.ToNormalizedString())
            + " --type "
            + ShellCommandText.Quote(type);
        if (member is not null)
        {
            command +=
                " --member "
                + ShellCommandText.Quote(member);
        }
        command +=
            " --finding "
            + ShellCommandText.Quote(finding);
        foreach (var address in action.SelectedAddresses)
        {
            command +=
                " --at "
                + ShellCommandText.Quote(address.NormalizedVersion);
        }
        if (replayContext?.IncludePrerelease is true)
        {
            command += " --preview";
        }
        command += FormatContextArguments(
            scope,
            targetContext,
            replayContext);
        return command;
    }

    static string FormatPairwiseAction(
        DiffHistoryNextAction.PairwiseDiff action)
    {
        string command =
            "dotnet-inspect diff --package "
            + ShellCommandText.Quote(
                $"{action.PackageId}@"
                + $"{action.Boundary.Source.NormalizedVersion}.."
                + action.Boundary.Destination.NormalizedVersion)
            + " --type "
            + ShellCommandText.Quote(action.TypeFullName);
        if (action.Member is { } member)
        {
            command +=
                " --member "
                + ShellCommandText.Quote(member.StableSelector);
        }
        command +=
            " --finding "
            + ShellCommandText.Quote(action.Finding);
        if (action.TargetContext.RequestedFramework is { } framework)
        {
            command +=
                " --tfm "
                + ShellCommandText.Quote(framework);
        }
        command += FormatContextArguments(
            action.Scope,
            action.TargetContext,
            action.ReplayContext,
            includeTargetFramework: false);
        return command;
    }

    static string FormatContextArguments(
        ApiSurfaceScope scope,
        DotnetInspector.Packages.PackageHouseTargetContext targetContext,
        DiffHistoryPackageReplayContext? replayContext,
        bool includeTargetFramework = true)
    {
        string arguments = "";
        if (scope == ApiSurfaceScope.IncludeAll)
            arguments += " --all";
        if (includeTargetFramework
            && targetContext.RequestedFramework is { } framework)
        {
            arguments +=
                " --tfm "
                + ShellCommandText.Quote(framework);
        }
        if (replayContext is not null)
        {
            string replay = PackageReplaySourceArguments.Format(
                new PackageReplaySources(
                    replayContext.Sources,
                    replayContext.AdditionalSources,
                    replayContext.ConfigFile,
                    replayContext.ConfigDirectory));
            if (replay.Length > 0)
                arguments += " " + replay;
        }
        return arguments;
    }

    static string Subject<T>(FindingComparison<T>.Complete complete)
        where T : notnull =>
        complete.OldAtoms.FirstOrDefault()?.Subject.Display
        ?? complete.NewAtoms.FirstOrDefault()?.Subject.Display
        ?? "";

    static DiffHistoryDocumentView SelectSections(
        DiffHistoryDocumentView view,
        HashSet<string> selected) =>
        new()
        {
            Title = view.Title,
            Range = view.Range,
            Type = view.Type,
            Member = view.Member,
            Finding = view.Finding,
            Policy = view.Policy,
            Outcome = selected.Contains(DiffHistorySections.Outcome)
                ? view.Outcome
                : null,
            ProbeTrace = selected.Contains(DiffHistorySections.ProbeTrace)
                ? view.ProbeTrace
                : null,
            Evaluations = selected.Contains(DiffHistorySections.Evaluations)
                ? view.Evaluations
                : null,
            Transitions = selected.Contains(DiffHistorySections.Transitions)
                ? view.Transitions
                : null,
            ChangedVersions =
                selected.Contains(DiffHistorySections.ChangedVersions)
                    ? view.ChangedVersions
                    : null,
        };

    static bool TryApplyRowSelection(
        DiffHistoryDocumentView view,
        HashSet<string> selectedSections,
        RowSelectionIntent<string>? rowSelection,
        out DiffHistoryDocumentView selectedView)
    {
        selectedView = view;
        if (rowSelection is not { Operations.Count: > 0 })
            return true;

        if (!TrySelect(
                rowSelection,
                selectedSections,
                DiffHistorySections.Outcome,
                view.Outcome,
                out IReadOnlyList<DiffHistoryOutcomeRowView>? outcome)
            || !TrySelect(
                rowSelection,
                selectedSections,
                DiffHistorySections.ProbeTrace,
                view.ProbeTrace,
                out IReadOnlyList<DiffHistoryProbeRowView>? probes)
            || !TrySelect(
                rowSelection,
                selectedSections,
                DiffHistorySections.Evaluations,
                view.Evaluations,
                out IReadOnlyList<DiffHistoryEvaluationRowView>? evaluations)
            || !TrySelect(
                rowSelection,
                selectedSections,
                DiffHistorySections.Transitions,
                view.Transitions,
                out IReadOnlyList<DiffHistoryTransitionRowView>? transitions)
            || !TrySelect(
                rowSelection,
                selectedSections,
                DiffHistorySections.ChangedVersions,
                view.ChangedVersions,
                out IReadOnlyList<DiffHistoryChangedVersionRowView>? changed))
        {
            return false;
        }

        selectedView = new()
        {
            Title = view.Title,
            Range = view.Range,
            Type = view.Type,
            Member = view.Member,
            Finding = view.Finding,
            Policy = view.Policy,
            Outcome = outcome is null ? null : [.. outcome],
            ProbeTrace = probes is null ? null : [.. probes],
            Evaluations = evaluations is null ? null : [.. evaluations],
            Transitions = transitions is null ? null : [.. transitions],
            ChangedVersions = changed is null ? null : [.. changed],
        };
        return true;
    }

    static bool TrySelect<T>(
        RowSelectionIntent<string> intent,
        HashSet<string> selectedSections,
        string section,
        IReadOnlyList<T>? source,
        out IReadOnlyList<T>? selected)
    {
        selected = source;
        return !selectedSections.Contains(section)
            || CliSemanticRowSelection.TrySelect(
                intent,
                source ?? [],
                section,
                FormatRowSelectionFailure,
                out selected);
    }

    static string FormatRowSelectionFailure(
        RowsCohortSemanticFailure<string> failure) =>
        $"Diff History row selection stage {failure.Failure.StageNumber} "
        + $"for '{failure.Identity}' requires row "
        + $"{failure.Failure.RequiredPosition}, but only "
        + $"{failure.Failure.AvailableCount} rows are available.";

    static void WriteTable(
        string section,
        DiffHistoryDocumentView view,
        DiffOptions options)
    {
        OutputFormatter.WriteProjectedTable(
            Console.Out,
            !options.NoHeader,
            options.Tsv,
            options.Jsonl,
            options.Columns,
            options.Fields,
            (writer, formatter, writerOptions) =>
            {
                switch (section)
                {
                    case DiffHistorySections.Outcome:
                        MarkoutSerializer.Serialize(
                            new DiffHistoryOutcomeView
                            {
                                Rows = view.Outcome,
                            },
                            writer,
                            formatter,
                            DiffHistoryViewContext.Default,
                            writerOptions);
                        break;
                    case DiffHistorySections.ProbeTrace:
                        MarkoutSerializer.Serialize(
                            new DiffHistoryProbeTraceView
                            {
                                Rows = view.ProbeTrace,
                            },
                            writer,
                            formatter,
                            DiffHistoryViewContext.Default,
                            writerOptions);
                        break;
                    case DiffHistorySections.Evaluations:
                        MarkoutSerializer.Serialize(
                            new DiffHistoryEvaluationsView
                            {
                                Rows = view.Evaluations,
                            },
                            writer,
                            formatter,
                            DiffHistoryViewContext.Default,
                            writerOptions);
                        break;
                    case DiffHistorySections.Transitions:
                        MarkoutSerializer.Serialize(
                            new DiffHistoryTransitionsView
                            {
                                Rows = view.Transitions,
                            },
                            writer,
                            formatter,
                            DiffHistoryViewContext.Default,
                            writerOptions);
                        break;
                    case DiffHistorySections.ChangedVersions:
                        MarkoutSerializer.Serialize(
                            new DiffHistoryChangedVersionsView
                            {
                                Rows = view.ChangedVersions,
                            },
                            writer,
                            formatter,
                            DiffHistoryViewContext.Default,
                            writerOptions);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unknown Diff History section '{section}'.");
                }
            },
            maxRows: null);
    }
}
