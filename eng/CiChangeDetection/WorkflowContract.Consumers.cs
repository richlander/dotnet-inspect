using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    private static void ValidateOutputConsumers(YamlMappingNode jobs)
    {
        var conditions = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["markdownlint"] = "needs.changes.outputs.docs == 'true'",
            ["skill-gate"] =
                "needs.changes.outputs.skills == 'true' && " +
                "github.event_name == 'pull_request'",
            ["test"] =
                "needs.changes.outputs.code == 'true' && " +
                "github.event_name == 'pull_request'",
            ["test-windows"] =
                "needs.changes.outputs.code == 'true' && " +
                "github.event_name == 'pull_request'",
            ["build-net10"] =
                "needs.changes.outputs.shipped == 'true' && " +
                "github.event_name == 'pull_request'",
            ["decompiler-gates"] =
                "github.event_name == 'pull_request' && " +
                "needs.changes.outputs.decompiler == 'true'",
            ["csharp-diff-smoke"] =
                "github.event_name == 'pull_request' && " +
                "needs.changes.outputs.csharpdiff == 'true'",
            ["il-diff-smoke"] =
                "github.event_name == 'pull_request' && " +
                "needs.changes.outputs.ildiff == 'true'",
            ["pack"] =
                "github.event_name == 'pull_request' && " +
                "needs.changes.outputs.packaging == 'true'",
        };
        foreach ((string jobName, string condition) in conditions)
        {
            YamlMappingNode job = GetRequiredMapping(
                jobs,
                jobName,
                "jobs");
            RequireScalarValue(
                job,
                "needs",
                "changes",
                $"jobs.{jobName}");
            RequireScalarValue(
                job,
                "if",
                condition,
                $"jobs.{jobName}");
            RequireAbsent(
                job,
                "continue-on-error",
                $"jobs.{jobName}");
            RequireAbsent(
                job,
                "defaults",
                $"jobs.{jobName}");
        }
        ValidateConsumerStepGuards(jobs, conditions.Keys);

        YamlSequenceNode testSteps = GetRequiredSequence(
            GetRequiredMapping(jobs, "test", "jobs"),
            "steps",
            "jobs.test");
        var roundtripSteps = new Dictionary<string, YamlMappingNode>(
            StringComparer.Ordinal);
        foreach (YamlNode stepNode in testSteps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                "jobs.test step");
            string? name = GetOptionalScalar(step, "name");
            if (name is "Restore vendored ILAssembler" or
                "Run IL round-trip tests (fast)")
            {
                if (!roundtripSteps.TryAdd(name, step))
                {
                    throw new InvalidOperationException(
                        $"jobs.test contains duplicate step: {name}.");
                }
            }
        }

        string roundtripCondition =
            "matrix.rid == 'linux-x64' && " +
            "needs.changes.outputs.ilroundtrip == 'true'";
        foreach (string name in new[]
        {
            "Restore vendored ILAssembler",
            "Run IL round-trip tests (fast)",
        })
        {
            if (!roundtripSteps.TryGetValue(
                name,
                out YamlMappingNode? step))
            {
                throw new InvalidOperationException(
                    $"jobs.test is missing step: {name}.");
            }
            RequireScalarValue(
                step,
                "if",
                roundtripCondition,
                $"jobs.test {name}");
            RequireAbsent(
                step,
                "continue-on-error",
                $"jobs.test {name}");
        }
    }

    private static void ValidateConsumerStepGuards(
        YamlMappingNode jobs,
        IEnumerable<string> jobNames)
    {
        var allowedIf = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test/Upload PR decompiler corpus artifact"] = "always()",
            ["test/Check PR decompiler corpus result"] =
                "steps.decompiler_pr_corpus.outcome == 'failure'",
            ["test/Restore vendored ILAssembler"] =
                "matrix.rid == 'linux-x64' && " +
                "needs.changes.outputs.ilroundtrip == 'true'",
            ["test/Run IL round-trip tests (fast)"] =
                "matrix.rid == 'linux-x64' && " +
                "needs.changes.outputs.ilroundtrip == 'true'",
            ["test/Check ilasm/ildasm/mdv result"] =
                "steps.iltools.outcome == 'failure'",
            ["test-windows/Run CLI tests (all)"] =
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
            ["test-windows/Run CSharpText tests"] =
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
            ["test-windows/Run decompiler unit tests (fast)"] =
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
            ["test-windows/Run NuGetFetch tests (offline)"] =
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
            ["test-windows/Run metadata tests"] =
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
            ["test-windows/Run services tests"] =
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
            ["test-windows/Run query tests"] =
                "${{ !cancelled() && steps.build.outcome == 'success' }}",
            ["test-windows/Check ilasm/ildasm result"] =
                "${{ !cancelled() && steps.build.outcome == 'success' && " +
                "steps.iltools.outcome != 'success' }}",
            ["decompiler-gates/Upload gate report"] = "always()",
            ["csharp-diff-smoke/Upload C# Diff smoke artifact"] = "always()",
            ["il-diff-smoke/Upload IL Diff smoke artifact"] = "always()",
        };
        var allowedContinueOnError = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "test/Run PR decompiler corpus sensor",
            "test/Install ilasm/ildasm/mdv",
            "test-windows/Install ilasm/ildasm",
            "decompiler-gates/Run decompiler gates",
        };
        var allowedShell = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test/Run PR decompiler corpus sensor"] = "bash",
            ["test/Install ilasm/ildasm/mdv"] = "bash",
            ["test-windows/Install ilasm/ildasm"] = "bash",
            ["test-windows/Check ilasm/ildasm result"] = "bash",
            ["csharp-diff-smoke/Run C# Diff baseline smoke"] = "bash",
            ["il-diff-smoke/Run IL Diff baseline smoke"] = "bash",
            ["skill-gate/Run embedded skill tests"] = "bash",
        };
        var allowedId = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test/Run PR decompiler corpus sensor"] =
                "decompiler_pr_corpus",
            ["test/Install ilasm/ildasm/mdv"] = "iltools",
            ["test-windows/Build"] = "build",
            ["test-windows/Install ilasm/ildasm"] = "iltools",
            ["decompiler-gates/Run decompiler gates"] = "gates",
        };
        var allowedTimeoutMinutes = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test-windows/Install ilasm/ildasm"] = "5",
            ["decompiler-gates/Run decompiler gates"] = "45",
        };
        var seenIf = new HashSet<string>(StringComparer.Ordinal);
        var seenContinueOnError =
            new HashSet<string>(StringComparer.Ordinal);
        var seenShell = new HashSet<string>(StringComparer.Ordinal);
        var seenId = new HashSet<string>(StringComparer.Ordinal);
        var seenTimeoutMinutes =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (string jobName in jobNames)
        {
            YamlSequenceNode steps = GetRequiredSequence(
                GetRequiredMapping(jobs, jobName, "jobs"),
                "steps",
                $"jobs.{jobName}");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (YamlNode stepNode in steps.Children)
            {
                YamlMappingNode step = RequireMapping(
                    stepNode,
                    $"jobs.{jobName} step");
                string? identity = GetOptionalScalar(step, "name") ??
                    GetOptionalScalar(step, "uses");
                if (identity is null || !identities.Add(identity))
                {
                    throw new InvalidOperationException(
                        $"jobs.{jobName} steps must have unique names or uses.");
                }

                string key = $"{jobName}/{identity}";
                ValidateOptionalStepValue(
                    step,
                    "if",
                    key,
                    allowedIf,
                    seenIf);
                ValidateOptionalStepValue(
                    step,
                    "shell",
                    key,
                    allowedShell,
                    seenShell);
                ValidateOptionalStepValue(
                    step,
                    "id",
                    key,
                    allowedId,
                    seenId);
                ValidateOptionalStepValue(
                    step,
                    "timeout-minutes",
                    key,
                    allowedTimeoutMinutes,
                    seenTimeoutMinutes);

                string? continueOnError =
                    GetOptionalScalar(step, "continue-on-error");
                if (continueOnError is not null)
                {
                    if (continueOnError != "true" ||
                        !allowedContinueOnError.Contains(key))
                    {
                        throw new InvalidOperationException(
                            $"{key} has unapproved continue-on-error.");
                    }
                    seenContinueOnError.Add(key);
                }
                RequireAbsent(step, "working-directory", key);
            }
        }

        RequireSeenExactly(
            seenIf,
            allowedIf.Keys,
            "consumer step if conditions");
        RequireSeenExactly(
            seenContinueOnError,
            allowedContinueOnError,
            "consumer step continue-on-error");
        RequireSeenExactly(
            seenShell,
            allowedShell.Keys,
            "consumer step shell overrides");
        RequireSeenExactly(
            seenId,
            allowedId.Keys,
            "consumer step ids");
        RequireSeenExactly(
            seenTimeoutMinutes,
            allowedTimeoutMinutes.Keys,
            "consumer step timeout minutes");
    }

    private static void ValidateOptionalStepValue(
        YamlMappingNode step,
        string property,
        string key,
        IReadOnlyDictionary<string, string> allowed,
        ISet<string> seen)
    {
        string? value = GetOptionalScalar(step, property);
        if (value is null)
        {
            return;
        }
        if (!allowed.TryGetValue(key, out string? expected) ||
            value != expected)
        {
            throw new InvalidOperationException(
                $"{key}.{property} is not approved.");
        }
        seen.Add(key);
    }

    private static void RequireSeenExactly(
        IReadOnlySet<string> actual,
        IEnumerable<string> expected,
        string context)
    {
        if (!actual.SetEquals(expected))
        {
            throw new InvalidOperationException(
                $"{context} do not match the approved set.");
        }
    }
}
