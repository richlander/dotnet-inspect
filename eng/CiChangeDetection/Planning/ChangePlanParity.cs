using System.Text;

namespace CiChangeDetection.Planning;

/// <summary>
/// Compares the production planner's selections with the legacy shell
/// classifier for every scenario in which both receive the same event and
/// changed-path corpus. The comparison is automatic: a scenario opts out only
/// when it deliberately diverges (a provenance or failure case the planner
/// refuses instead of over-running) or when it mutates the oracle itself.
/// </summary>
internal static class ChangePlanParity
{
    private static readonly Lock PolicyLock = new();
    private static ChangeRoutingPolicy? policy;

    /// <summary>
    /// Reports whether a scenario's event and path corpus are shared with the
    /// planner. Fallback, malformed-oracle, and split-candidate scenarios are
    /// deliberate divergences and are covered by their own fixtures.
    /// </summary>
    /// <param name="scenario">The detection scenario.</param>
    /// <returns>True when both surfaces see the same corpus.</returns>
    internal static bool IsComparable(DetectionScenario scenario) =>
        scenario.ResolutionSucceeds
        && scenario.ReportedChangedFileCount is null
        && !scenario.ChangedFileCountIsString
        && scenario.MalformedFileRecordJson.Length == 0
        && !scenario.ObjectShapedFilePage
        && !scenario.NulFileRecord
        && !scenario.NulPreviousFileRecord
        && scenario.FailDecodeAt == 0
        && !scenario.TruncateRecordStream
        && !scenario.TruncatePushStream
        && !scenario.EmptyPushRecord
        && scenario.TlaCandidateFiles is null
        && scenario.TlaCandidateResolutionSucceeds
        && scenario.FileStatus is "added" or "removed" or "modified"
            or "copied" or "changed" or "unchanged"
        && HasSharedRenameShape(scenario)
        && !(scenario.EventName == "pull_request"
            && scenario.Files.Length == 0);

    /// <summary>
    /// Asserts that the planner's raw routing and effective validation
    /// selections agree with the shell classifier's outputs.
    /// </summary>
    /// <param name="repository">The repository root directory.</param>
    /// <param name="scenario">The detection scenario.</param>
    /// <param name="values">The shell classifier's outputs.</param>
    internal static void Assert(
        string repository,
        DetectionScenario scenario,
        IReadOnlyDictionary<string, string> values)
    {
        PlanEventKind kind = scenario.EventName switch
        {
            "pull_request" => PlanEventKind.PullRequestSyntheticCandidate,
            "push" => PlanEventKind.Push,
            "merge_group" => PlanEventKind.MergeGroup,
            _ => throw new InvalidOperationException(
                $"Unknown parity event name: {scenario.EventName}"),
        };

        List<ChangeRecord> records = [];
        foreach (string previous in Split(scenario.PreviousFiles))
        {
            records.Add(new ChangeRecord(
                ChangeStatus.Deleted,
                Encoding.UTF8.GetBytes(previous)));
        }

        foreach (string file in Split(scenario.Files))
        {
            records.Add(new ChangeRecord(
                ChangeStatus.Modified,
                Encoding.UTF8.GetBytes(file)));
        }

        ChangeEvidence evidence = ChangeEvidence.Create(records);
        RoutingSelections routing = Policy(repository).Route(evidence);
        AssertRaw(values, "code", routing.Code, scenario);
        AssertRaw(values, "csharpdiff", routing.CSharpDiff, scenario);
        AssertRaw(values, "decompiler", routing.Decompiler, scenario);
        AssertRaw(values, "docs", routing.Docs, scenario);
        AssertRaw(values, "ildiff", routing.IlDiff, scenario);
        AssertRaw(values, "ilroundtrip", routing.IlRoundtrip, scenario);
        AssertRaw(values, "packaging", routing.Packaging, scenario);
        AssertRaw(values, "shipped", routing.Shipped, scenario);
        AssertRaw(values, "web", routing.Web, scenario);
        AssertRaw(values, "skills", routing.Skills, scenario);
        AssertRaw(values, "tla", routing.Tla, scenario);

        // The workflow gates most selections on the event. Derive the same
        // effective selections from the oracle's raw outputs and require the
        // plan's typed fields to match them.
        bool preMerge = kind != PlanEventKind.Push;
        ValidationSelections effective =
            ValidationSelections.FromRouting(routing, kind);
        AssertEffective(
            "test",
            Raw(values, "code") && preMerge,
            effective.Test,
            scenario);
        AssertEffective(
            "csharpDiffSmoke",
            Raw(values, "csharpdiff") && preMerge,
            effective.CSharpDiffSmoke,
            scenario);
        AssertEffective(
            "decompilerGates",
            Raw(values, "decompiler") && preMerge,
            effective.DecompilerGates,
            scenario);
        AssertEffective(
            "markdownlint",
            Raw(values, "docs"),
            effective.Markdownlint,
            scenario);
        AssertEffective(
            "ilDiffSmoke",
            Raw(values, "ildiff") && preMerge,
            effective.IlDiffSmoke,
            scenario);
        AssertEffective(
            "ilRoundTrip",
            Raw(values, "ilroundtrip") && preMerge,
            effective.IlRoundTrip,
            scenario);
        AssertEffective(
            "pack",
            Raw(values, "packaging") && preMerge,
            effective.Pack,
            scenario);
        AssertEffective(
            "buildNet10",
            Raw(values, "shipped") && preMerge,
            effective.BuildNet10,
            scenario);
        AssertEffective(
            "inspectWeb",
            Raw(values, "web"),
            effective.InspectWeb,
            scenario);
        AssertEffective(
            "skillGate",
            Raw(values, "skills") && preMerge,
            effective.SkillGate,
            scenario);
        AssertEffective("tla", Raw(values, "tla"), effective.Tla, scenario);
    }

    private static ChangeRoutingPolicy Policy(string repository)
    {
        lock (PolicyLock)
        {
            return policy ??= ChangeRoutingPolicy.Load(repository);
        }
    }

    private static IEnumerable<string> Split(string value) =>
        value.Split('\n').Where(part => part.Length != 0);

    private static bool HasSharedRenameShape(DetectionScenario scenario) =>
        scenario.PreviousFiles.Length == 0
        || (Split(scenario.PreviousFiles).Take(2).Count() == 1
            && Split(scenario.Files).Take(2).Count() == 1);

    private static bool Raw(
        IReadOnlyDictionary<string, string> values,
        string name) =>
        values[name] == "true";

    private static void AssertRaw(
        IReadOnlyDictionary<string, string> values,
        string name,
        bool actual,
        DetectionScenario scenario)
    {
        if (Raw(values, name) != actual)
        {
            throw new InvalidOperationException(
                $"Planner routing diverged from the shell classifier for "
                + $"{name} on event {scenario.EventName} with paths "
                + $"[{scenario.PreviousFiles.Replace('\n', ' ')}"
                + $"|{scenario.Files.Replace('\n', ' ')}]: shell "
                + $"{values[name]}, planner "
                + $"{actual.ToString().ToLowerInvariant()}.");
        }
    }

    private static void AssertEffective(
        string name,
        bool expected,
        bool actual,
        DetectionScenario scenario)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(
                $"Effective selection {name} diverged on event "
                + $"{scenario.EventName} with paths "
                + $"[{scenario.Files.Replace('\n', ' ')}]: expected "
                + $"{expected.ToString().ToLowerInvariant()}, got "
                + $"{actual.ToString().ToLowerInvariant()}.");
        }
    }
}
