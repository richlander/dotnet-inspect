using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    private static readonly HashSet<string> CommonTestSteps =
    [
        "actions/checkout@v7",
        "Setup .NET",
        "Cache NuGet packages",
        "Build",
    ];

    private static readonly Dictionary<string, (string Condition, string Shard)>
        SpecialTestStepConditions = new(StringComparer.Ordinal)
        {
            ["Upload PR decompiler corpus artifact"] = (
                "matrix.shard == 'host-analysis' && always()",
                "host-analysis"),
            ["Check PR decompiler corpus result"] = (
                "matrix.shard == 'host-analysis' && " +
                "steps.decompiler_pr_corpus.outcome == 'failure'",
                "host-analysis"),
            ["Restore vendored ILAssembler"] = (
                "matrix.shard == 'host-analysis' && matrix.rid == 'linux-x64' && " +
                "fromJSON(needs.changes.outputs.plan).validations.ilRoundTrip",
                "host-analysis"),
            ["Run IL round-trip tests (fast)"] = (
                "matrix.shard == 'host-analysis' && matrix.rid == 'linux-x64' && " +
                "fromJSON(needs.changes.outputs.plan).validations.ilRoundTrip",
                "host-analysis"),
            ["Check contract test ilasm/ildasm/mdv result"] = (
                "matrix.shard == 'contracts' && " +
                "steps.iltools_contracts.outcome == 'failure'",
                "contracts"),
            ["Check GitHub Packages fixture result"] = (
                "matrix.shard == 'host-analysis' && " +
                "steps.package_fixture.outcome == 'failure'",
                "host-analysis"),
            ["Run legacy source-identity guard"] = (
                "matrix.shard == 'host-analysis' && " +
                "fromJSON(needs.changes.outputs.plan).validations.repositoryGuards",
                "host-analysis"),
            ["Build tool for net10.0 fallback"] = (
                "matrix.shard == 'host-analysis' && " +
                "fromJSON(needs.changes.outputs.plan).validations.buildNet10",
                "host-analysis"),
        };

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
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run legacy source-identity guard",
            "dotnet run --project tests/NuGetFetch.Tests -c Release -- " +
                "--filter-method \"*LegacyPackageSourceIdentitySurfaceMatchesMigrationSet\" " +
                "--minimum-expected-tests 1\n");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Build tool for net10.0 fallback",
            "dotnet build src/DotnetInspect.Cli -c Release " +
                "-p:DefaultTargetFramework=net10.0 -p:PublishAot=false");
        ValidateDependencyPolicyJob(jobs);
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run IL diff tests",
            "dotnet run --project tests/ILInspector.ILDiff.Tests -c Release");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run NetworkAccess tests",
            "dotnet run --project tests/NetworkAccess.Tests -c Release");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run BinaryFetch tests",
            "dotnet run --project tests/BinaryFetch.Tests -c Release");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run ZipFetch tests",
            "dotnet run --project tests/ZipFetch.Tests -c Release");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run UntrustedDocuments tests",
            "dotnet run --project tests/UntrustedDocuments.Tests -c Release");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run DotnetInspector.Networking tests",
            "dotnet run --project tests/DotnetInspector.Networking.Tests -c Release");
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
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run DotnetInspector.Cache tests",
            "dotnet run --project tests/DotnetInspector.Cache.Tests -c Release");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run DotnetInspector.Packages tests",
            "dotnet run --project tests/DotnetInspector.Packages.Tests -c Release");
        ValidateRequiredRunStep(
            jobs,
            "test",
            "Run inspection query tests",
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
            ["decompiler-gates/Upload gate report"] = "always()",
            ["decompiler-gates/Check decompiler test ilasm/ildasm result"] =
                "always() && steps.iltools_decompiler.outcome == 'failure'",
            ["csharp-diff-smoke/Upload C# Diff smoke artifact"] = "always()",
            ["il-diff-smoke/Upload IL Diff smoke artifact"] = "always()",
        };
        var allowedContinueOnError = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "test/Run GitHub Packages fixture test",
            "test/Run PR decompiler corpus sensor",
            "test/Install ilasm/ildasm/mdv for contract tests",
            "decompiler-gates/Install ilasm/ildasm for decompiler tests",
            "decompiler-gates/Run decompiler gates",
        };
        var allowedShell = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test/Run GitHub Packages fixture test"] = "bash",
            ["test/Run PR decompiler corpus sensor"] = "bash",
            ["test/Install ilasm/ildasm/mdv for contract tests"] = "bash",
            ["decompiler-gates/Install ilasm/ildasm for decompiler tests"] =
                "bash",
            ["csharp-diff-smoke/Run C# Diff baseline smoke"] = "bash",
            ["il-diff-smoke/Run IL Diff baseline smoke"] = "bash",
        };
        var allowedId = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["test/Run GitHub Packages fixture test"] =
                "package_fixture",
            ["test/Run PR decompiler corpus sensor"] =
                "decompiler_pr_corpus",
            ["test/Install ilasm/ildasm/mdv for contract tests"] =
                "iltools_contracts",
            ["decompiler-gates/Install ilasm/ildasm for decompiler tests"] =
                "iltools_decompiler",
            ["decompiler-gates/Run decompiler gates"] = "gates",
        };
        var allowedTimeoutMinutes = new Dictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["decompiler-gates/Run decompiler gates"] = "5",
        };
        var seenIf = new HashSet<string>(StringComparer.Ordinal);
        var seenContinueOnError =
            new HashSet<string>(StringComparer.Ordinal);
        var seenShell = new HashSet<string>(StringComparer.Ordinal);
        var seenId = new HashSet<string>(StringComparer.Ordinal);
        var seenTimeoutMinutes =
            new HashSet<string>(StringComparer.Ordinal);
        var seenTestShards = new HashSet<string>(StringComparer.Ordinal);

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
                    ValidateTestStepGuard(
                        step,
                        identity,
                        key,
                        seenTestShards);
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
        RequireSeenExactly(
            seenTestShards,
            TestShards,
            "test shard step guards");
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
        string key,
        ISet<string> seenShards)
    {
        if (CommonTestSteps.Contains(identity))
        {
            RequireAbsent(step, "if", key);
            return;
        }

        string condition = GetOptionalScalar(step, "if")
            ?? throw new InvalidOperationException(
                $"{key} must select one test shard.");
        if (SpecialTestStepConditions.TryGetValue(
                identity,
                out (string Condition, string Shard) special))
        {
            if (condition != special.Condition)
            {
                throw new InvalidOperationException(
                    $"{key}.if is not the approved shard condition.");
            }

            seenShards.Add(special.Shard);
            return;
        }

        foreach (string shard in TestShards)
        {
            if (condition == $"matrix.shard == '{shard}'")
            {
                seenShards.Add(shard);
                return;
            }
        }

        throw new InvalidOperationException(
            $"{key}.if must select exactly one approved test shard.");
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
