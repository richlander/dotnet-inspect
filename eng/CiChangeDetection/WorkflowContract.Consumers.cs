using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    private static void ValidateConsumerStepContracts(YamlMappingNode jobs)
    {
        string[] jobNames =
        [
            "test",
            "dependency-policy",
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
        }

        ValidateConsumerStepGuards(jobs, jobNames);
        ValidateDependencyPolicyJob(jobs);
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Build",
            "dotnet build dotnet-inspect.slnx -c Release");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Validate dependency policy",
            "dotnet run --project eng/DependencyPolicy -c Release --no-build");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run CLI route smoke",
            "dotnet run --project tests/DotnetInspect.Cli.Tests -c Release " +
            "--no-build -- --filter-class 'DotnetInspect.Cli.Tests.WorkspacePacketCommandTests' " +
            "--filter-class 'DotnetInspect.Cli.Tests.CliRowSelectionLoweringTests' " +
            "--minimum-expected-tests 2");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run embedded skill tests",
            "dotnet run --project tests/DotnetInspect.Cli.Tests -c Release " +
            "--no-build -- --filter-class \"DotnetInspect.Cli.Tests.SkillCommandTests\"");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run inspection query smoke",
            "dotnet run --project tests/DotnetInspector.Queries.Tests -c Release --no-build -- " +
            "--filter-class 'DotnetInspector.Queries.Tests.InspectionDefinitionTests' " +
            "--filter-class 'DotnetInspector.Queries.Tests.InspectionWorkspaceTests' " +
            "--filter-class 'DotnetInspector.Queries.Tests.PackageAssemblyQueryPlanningTests' " +
            "--filter-class 'DotnetInspector.Queries.Tests.PackageDependencyEvidenceQueryTests' " +
            "--filter-class 'DotnetInspector.Queries.Tests.WorkspaceRealizationTests' " +
            "--filter-class 'DotnetInspector.Queries.Tests.TypeFindPopulationSelectionTests' " +
            "--filter-class 'DotnetInspector.Queries.Tests.IntegrationCensusTests' " +
            "--filter-class 'DotnetInspector.Queries.Tests.ViewFacetRegistryTests' " +
            "--filter-not-trait \"Speed=Slow\" --minimum-expected-tests 300");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run portable query tests",
            "dotnet run --project tests/DotnetInspector.PortableQueries.Tests -c Release " +
            "--no-build -- --filter-not-trait \"Speed=Slow\" --minimum-expected-tests 1300");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run producer-capability adopter tests",
            "dotnet run --project tests/DotnetInspector.Sections.Tests -c Release " +
            "--no-build -- --filter-class 'DotnetInspector.Sections.Tests.PackageChildrenInspectionTests' " +
            "--filter-class 'DotnetInspector.Sections.Tests.QuerySpaceSectionRowCompositionTests' " +
            "--filter-not-trait \"Speed=Slow\" --minimum-expected-tests 22");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run InertText tests",
            "dotnet run --project tests/InertText.Tests -c Release --no-build");
        ValidateRequiredRunStep(
            jobs,
            "decompiler-gates",
            "Build fast decompiler test graphs",
            "dotnet build tests/ILInspector.Decompiler.Tests -c Release\n" +
            "dotnet build tests/DecompilerHarness.Tests -c Release\n");
        ValidateRequiredRunStep(
            jobs,
            "decompiler-gates",
            "Run decompiler unit tests (fast)",
            "dotnet run --project tests/ILInspector.Decompiler.Tests -c Release " +
                "--no-build -- --gate fast");
        ValidateRequiredRunStep(
            jobs,
            "decompiler-gates",
            "Run DecompilerHarness tests",
            "dotnet run --project tests/DecompilerHarness.Tests -c Release --no-build");
        YamlSequenceNode decompilerSteps = GetRequiredSequence(
            GetRequiredMapping(jobs, "decompiler-gates", "jobs"),
            "steps",
            "jobs.decompiler-gates");
        if (decompilerSteps.Children
            .Select(node => RequireMapping(node, "jobs.decompiler-gates step"))
            .Any(step => GetOptionalScalar(step, "name") is
                "Discover expected gate tests" or "Run decompiler gates"))
        {
            throw new InvalidOperationException(
                "The bounded decompiler receipt belongs in daily Deep Inspect.");
        }
        YamlSequenceNode packSteps = GetRequiredSequence(
            GetRequiredMapping(jobs, "pack", "jobs"),
            "steps",
            "jobs.pack");
        if (packSteps.Children
            .Select(node => RequireMapping(node, "jobs.pack step"))
            .Any(step => GetOptionalScalar(step, "name") is
                "Pack linux-x64 AOT package" or "Install tool from local packages"))
        {
            throw new InvalidOperationException(
                "RID-specific AOT packaging and tool install belong in daily Deep Inspect.");
        }
    }

    private static void RequireNamedRunStep(
        YamlNode node,
        string name,
        string run,
        string context)
    {
        YamlMappingNode step = RequireMapping(node, context);
        RequireExactKeys(step, ["name", "run"], context);
        RequireScalarValue(step, "name", name, context);
        RequireScalarValue(step, "run", run, context);
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
        RequireRepositoryRootWorkingDirectory(
            job,
            build,
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
        RequireRepositoryRootWorkingDirectory(
            job,
            validate,
            "jobs.dependency-policy Validate dependency policy step");
    }

    private static void ValidateRequiredRunStep(
        YamlMappingNode jobs,
        string jobName,
        string stepName,
        string command)
    {
        YamlMappingNode job = GetRequiredMapping(jobs, jobName, "jobs");
        YamlSequenceNode steps = GetRequiredSequence(
            job,
            "steps",
            $"jobs.{jobName}");
        YamlMappingNode? requiredStep = null;
        foreach (YamlNode stepNode in steps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                $"jobs.{jobName} step");
            if (GetOptionalScalar(step, "name") != stepName)
            {
                continue;
            }

            if (requiredStep is not null)
            {
                throw new InvalidOperationException(
                    $"jobs.{jobName} contains duplicate step: {stepName}.");
            }
            requiredStep = step;
        }

        if (requiredStep is null)
        {
            throw new InvalidOperationException(
                $"jobs.{jobName} is missing step: {stepName}.");
        }
        RequireScalarValue(
            requiredStep,
            "run",
            command,
            $"jobs.{jobName} {stepName}");
        RequireRepositoryRootWorkingDirectory(
            job,
            requiredStep,
            $"jobs.{jobName} {stepName}");
    }

    private static void RequireRepositoryRootWorkingDirectory(
        YamlMappingNode job,
        YamlMappingNode step,
        string context)
    {
        string? workingDirectory = GetOptionalScalar(
            step,
            "working-directory");
        bool isStepOverride = workingDirectory is not null;
        if (workingDirectory is null
            && TryGetNode(job, "defaults", out YamlNode defaultsNode))
        {
            YamlMappingNode defaults = RequireMapping(
                defaultsNode,
                $"{context} job defaults");
            if (TryGetNode(defaults, "run", out YamlNode runNode))
            {
                workingDirectory = GetOptionalScalar(
                    RequireMapping(
                        runNode,
                        $"{context} job defaults.run"),
                    "working-directory");
            }
        }

        if (workingDirectory is null
            || (isStepOverride
                ? IsRepositoryRootWorkingDirectory(workingDirectory)
                : IsStaticRepositoryRootWorkingDirectory(workingDirectory)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{context} must run from the repository root, got " +
            $"{workingDirectory}.");
    }

    private static bool IsRepositoryRootWorkingDirectory(string value)
    {
        if (value == "${{ github.workspace }}")
        {
            return true;
        }

        return IsStaticRepositoryRootWorkingDirectory(value);
    }

    private static bool IsStaticRepositoryRootWorkingDirectory(string value)
    {
        if (value.StartsWith('/', StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = value.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
            && segments.All(segment => segment == ".");
    }

    private static void ValidateConsumerStepGuards(
        YamlMappingNode jobs,
        IEnumerable<string> jobNames)
    {
        var allowedIf = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["decompiler-gates/Check decompiler test ilasm/ildasm result"] =
                "always() && steps.iltools_decompiler.outcome == 'failure'",
            ["csharp-diff-smoke/Upload C# Diff smoke artifact"] = "always()",
            ["il-diff-smoke/Upload IL Diff smoke artifact"] = "always()",
        };
        var allowedContinueOnError = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "decompiler-gates/Install ilasm/ildasm for decompiler tests",
        };
        var allowedShell = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test/Run embedded skill tests"] = "bash",
            ["decompiler-gates/Install ilasm/ildasm for decompiler tests"] =
                "bash",
            ["csharp-diff-smoke/Run C# Diff baseline smoke"] = "bash",
            ["il-diff-smoke/Run IL Diff baseline smoke"] = "bash",
        };
        var allowedId = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["decompiler-gates/Install ilasm/ildasm for decompiler tests"] =
                "iltools_decompiler",
        };
        var allowedTimeoutMinutes = new Dictionary<string, string>(
            StringComparer.Ordinal);
        var seenIf = new HashSet<string>(StringComparer.Ordinal);
        var seenContinueOnError =
            new HashSet<string>(StringComparer.Ordinal);
        var seenShell = new HashSet<string>(StringComparer.Ordinal);
        var seenId = new HashSet<string>(StringComparer.Ordinal);
        var seenTimeoutMinutes =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (string jobName in jobNames)
        {
            YamlMappingNode job = GetRequiredMapping(
                jobs,
                jobName,
                "jobs");
            YamlSequenceNode steps = GetRequiredSequence(
                job,
                "steps",
                $"jobs.{jobName}");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            bool hasRunStepWithoutShell = false;
            foreach (YamlNode stepNode in steps.Children)
            {
                YamlMappingNode step = RequireMapping(
                    stepNode,
                    $"jobs.{jobName} step");
                bool isRunStep = TryGetNode(step, "run", out _);
                hasRunStepWithoutShell |=
                    isRunStep
                    && !TryGetNode(step, "shell", out _);
                string? identity = GetOptionalScalar(step, "name") ??
                    GetOptionalScalar(step, "uses");
                if (identity is null || !identities.Add(identity))
                {
                    throw new InvalidOperationException(
                        $"jobs.{jobName} steps must have unique names or uses.");
                }

                string key = $"{jobName}/{identity}";
                if (isRunStep)
                {
                    RequireRepositoryRootWorkingDirectory(
                        job,
                        step,
                        key);
                }
                if (jobName == "test")
                {
                    ValidateTestStepGuard(step, identity, key);
                }
                else
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
            }
            if (hasRunStepWithoutShell)
            {
                RequireAbsentFromRunDefaults(
                    job,
                    "shell",
                    $"jobs.{jobName}");
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

    private static void RequireAbsentFromRunDefaults(
        YamlMappingNode job,
        string property,
        string context)
    {
        if (!TryGetNode(job, "defaults", out YamlNode defaultsNode))
        {
            return;
        }

        YamlMappingNode defaults = RequireMapping(
            defaultsNode,
            $"{context}.defaults");
        if (!TryGetNode(defaults, "run", out YamlNode runNode))
        {
            return;
        }

        RequireAbsent(
            RequireMapping(runNode, $"{context}.defaults.run"),
            property,
            $"{context}.defaults.run");
    }

    private static void ValidateTestStepGuard(
        YamlMappingNode step,
        string identity,
        string key)
    {
        if (identity == "Run embedded skill tests")
        {
            RequireScalarValue(step, "if",
                "fromJSON(needs.changes.outputs.plan).validations.skillGate",
                key);
        }
        else
        {
            RequireAbsent(step, "if", key);
        }
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
