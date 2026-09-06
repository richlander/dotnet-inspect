using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    private static void ValidateConsumerStepContracts(YamlMappingNode jobs)
    {
        string[] jobNames =
        [
            "markdownlint",
            "skill-gate",
            "test",
            "dependency-policy",
            "build-net10",
            "decompiler-gates",
            "csharp-diff-smoke",
            "il-diff-smoke",
            "pack",
        ];
        foreach (string jobName in jobNames)
        {
            YamlMappingNode job = GetRequiredMapping(
                jobs,
                jobName,
                "jobs");
            RequireAbsent(
                job,
                "continue-on-error",
                $"jobs.{jobName}");
            RequireAbsent(
                job,
                "defaults",
                $"jobs.{jobName}");
        }

        ValidateConsumerStepGuards(jobs, jobNames);
        ValidateDependencyPolicyJob(jobs);
        ValidateIlDiffTestStep(jobs);
    }

    private static void ValidateDependencyPolicyJob(YamlMappingNode jobs)
    {
        YamlMappingNode job = GetRequiredMapping(
            jobs,
            "dependency-policy",
            "jobs");
        RequireExactKeys(
            job,
            ["needs", "if", "runs-on", "timeout-minutes", "steps"],
            "jobs.dependency-policy");
        RequireScalarValue(
            job,
            "needs",
            "changes",
            "jobs.dependency-policy");
        RequireScalarValue(
            job,
            "if",
            "fromJSON(needs.changes.outputs.plan).validations.dependencyPolicy",
            "jobs.dependency-policy");
        RequireScalarValue(
            job,
            "runs-on",
            "ubuntu-24.04",
            "jobs.dependency-policy");
        RequireScalarValue(
            job,
            "timeout-minutes",
            "20",
            "jobs.dependency-policy");

        YamlSequenceNode steps = GetRequiredSequence(
            job,
            "steps",
            "jobs.dependency-policy");
        if (steps.Children.Count != 5)
        {
            throw new InvalidOperationException(
                "jobs.dependency-policy must contain exactly five steps.");
        }

        YamlMappingNode build = RequireMapping(
            steps.Children[3],
            "jobs.dependency-policy Build step");
        RequireScalarValue(
            build,
            "name",
            "Build",
            "jobs.dependency-policy Build step");
        RequireScalarValue(
            build,
            "run",
            "dotnet build dotnet-inspect.slnx -c Release",
            "jobs.dependency-policy Build step");

        YamlMappingNode validate = RequireMapping(
            steps.Children[4],
            "jobs.dependency-policy Validate dependency policy step");
        RequireScalarValue(
            validate,
            "name",
            "Validate dependency policy",
            "jobs.dependency-policy Validate dependency policy step");
        RequireScalarValue(
            validate,
            "run",
            "dotnet run --project eng/DependencyPolicy -c Release --no-build",
            "jobs.dependency-policy Validate dependency policy step");
    }

    private static void ValidateIlDiffTestStep(YamlMappingNode jobs)
    {
        YamlSequenceNode testSteps = GetRequiredSequence(
            GetRequiredMapping(jobs, "test", "jobs"),
            "steps",
            "jobs.test");
        YamlMappingNode? ilDiffTestStep = null;
        foreach (YamlNode stepNode in testSteps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                "jobs.test step");
            if (GetOptionalScalar(step, "name") != "Run IL diff tests")
            {
                continue;
            }

            if (ilDiffTestStep is not null)
            {
                throw new InvalidOperationException(
                    "jobs.test contains duplicate step: Run IL diff tests.");
            }
            ilDiffTestStep = step;
        }

        if (ilDiffTestStep is null)
        {
            throw new InvalidOperationException(
                "jobs.test is missing step: Run IL diff tests.");
        }
        RequireScalarValue(
            ilDiffTestStep,
            "run",
            "dotnet run --project src/ILInspector.ILDiff.Tests -c Release",
            "jobs.test Run IL diff tests");
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
            ["test/Check ilasm/ildasm/mdv result"] =
                "steps.iltools.outcome == 'failure'",
            ["test/Check GitHub Packages fixture result"] =
                "steps.package_fixture.outcome == 'failure'",
            ["decompiler-gates/Upload gate report"] = "always()",
            ["csharp-diff-smoke/Upload C# Diff smoke artifact"] = "always()",
            ["il-diff-smoke/Upload IL Diff smoke artifact"] = "always()",
        };
        var plannerSelectedIf = new HashSet<string>(StringComparer.Ordinal)
        {
            "test/Restore vendored ILAssembler",
            "test/Run IL round-trip tests (fast)",
            "test/Run decompiler unit tests (fast)",
        };
        var allowedContinueOnError = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "test/Run GitHub Packages fixture test",
            "test/Run PR decompiler corpus sensor",
            "test/Install ilasm/ildasm/mdv",
            "decompiler-gates/Run decompiler gates",
        };
        var allowedShell = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test/Run GitHub Packages fixture test"] = "bash",
            ["test/Run PR decompiler corpus sensor"] = "bash",
            ["test/Install ilasm/ildasm/mdv"] = "bash",
            ["csharp-diff-smoke/Run C# Diff baseline smoke"] = "bash",
            ["il-diff-smoke/Run IL Diff baseline smoke"] = "bash",
            ["skill-gate/Run embedded skill tests"] = "bash",
        };
        var allowedId = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test/Run GitHub Packages fixture test"] =
                "package_fixture",
            ["test/Run PR decompiler corpus sensor"] =
                "decompiler_pr_corpus",
            ["test/Install ilasm/ildasm/mdv"] = "iltools",
            ["decompiler-gates/Run decompiler gates"] = "gates",
        };
        var allowedTimeoutMinutes = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
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
                if (!plannerSelectedIf.Contains(key))
                {
                    ValidateOptionalStepValue(
                        step,
                        "if",
                        key,
                        allowedIf,
                        seenIf);
                }
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
                    if (continueOnError != "true"
                        || !allowedContinueOnError.Contains(key))
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
        if (!allowed.TryGetValue(key, out string? expected)
            || value != expected)
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
