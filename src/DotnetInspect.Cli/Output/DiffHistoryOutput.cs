using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.PackageQueries;
using DotnetInspector.Presentation;
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
        DiffHistoryPresentation.Project(available, FormatAction);


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
