using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Presentation;

/// <summary>
/// Formats one Diff History next action for a host, such as the CLI's
/// replayable command text.
/// </summary>
public delegate string DiffHistoryNextActionFormatter(
    DiffHistoryNextAction action,
    PackageVersionVector population,
    string type,
    string? member,
    string finding,
    ApiSurfaceScope scope,
    PackageHouseTargetContext targetContext,
    DiffHistoryPackageReplayContext? replayContext);

/// <summary>
/// Projects an available Diff History outcome into its host-neutral Markout
/// view (diff-system.md#adoption-and-retirement, step 2b). The host supplies
/// only how a next action is spelled.
/// </summary>
public static class DiffHistoryPresentation
{
    public static DiffHistoryDocumentView Project(
        DiffHistoryOutcome.Available available,
        DiffHistoryNextActionFormatter formatAction)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(formatAction);
        return new Projector(formatAction).Project(available);
    }

    sealed class Projector(DiffHistoryNextActionFormatter formatAction)
    {
        public DiffHistoryDocumentView Project(
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

        DiffHistoryDocumentView ProjectApiFinding<T>(
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

        DiffHistoryDocumentView ProjectAnalysis<T>(
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

        DiffHistoryDocumentView Project(
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
                    + $"{population.StartVersion}.."
                    + population.EndVersion,
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

        DiffHistoryOutcomeRowView ProjectOutcome(
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
                        nextActions.Select(action => formatAction(
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


        static string Subject<T>(FindingComparison<T>.Complete complete)
            where T : notnull =>
            complete.OldAtoms.FirstOrDefault()?.Subject.Display
            ?? complete.NewAtoms.FirstOrDefault()?.Subject.Display
            ?? "";

    }
}
