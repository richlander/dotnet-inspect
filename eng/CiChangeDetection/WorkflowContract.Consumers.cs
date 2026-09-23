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
                "matrix.shard == 'host-policy' && always()",
                "host-policy"),
            ["Check PR decompiler corpus result"] = (
                "matrix.shard == 'host-policy' && " +
                "steps.decompiler_pr_corpus.outcome == 'failure'",
                "host-policy"),
            ["Restore vendored ILAssembler"] = (
                "matrix.shard == 'analysis' && matrix.rid == 'linux-x64' && " +
                "fromJSON(needs.changes.outputs.plan).validations.ilRoundTrip",
                "analysis"),
            ["Run IL round-trip tests (fast)"] = (
                "matrix.shard == 'analysis' && matrix.rid == 'linux-x64' && " +
                "fromJSON(needs.changes.outputs.plan).validations.ilRoundTrip",
                "analysis"),
            ["Check contract test ilasm/ildasm/mdv result"] = (
                "matrix.shard == 'contracts' && " +
                "steps.iltools_contracts.outcome == 'failure'",
                "contracts"),
            ["Check GitHub Packages fixture result"] = (
                "matrix.shard == 'host-policy' && " +
                "steps.package_fixture.outcome == 'failure'",
                "host-policy"),
        };

    private static void ValidateConsumerStepContracts(YamlMappingNode jobs)
    {
        string[] jobNames =
        [
            "markdownlint",
            "skill-gate",
            "repository-guards",
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
        }

        ValidateConsumerStepGuards(jobs, jobNames);
        ValidateRepositoryGuardsJob(jobs);
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
    }

    private static void ValidateRepositoryGuardsJob(YamlMappingNode jobs)
    {
        YamlMappingNode job = GetRequiredMapping(
            jobs,
            "repository-guards",
            "jobs");
        RequireExactKeys(
            job,
            ["needs", "if", "runs-on", "timeout-minutes", "steps"],
            "jobs.repository-guards");
        RequireScalarValue(
            job,
            "needs",
            "changes",
            "jobs.repository-guards");
        RequireScalarValue(
            job,
            "if",
            "fromJSON(needs.changes.outputs.plan).validations.repositoryGuards",
            "jobs.repository-guards");
        RequireScalarValue(
            job,
            "runs-on",
            "ubuntu-24.04",
            "jobs.repository-guards");
        RequireScalarValue(
            job,
            "timeout-minutes",
            "15",
            "jobs.repository-guards");

        YamlSequenceNode steps = GetRequiredSequence(
            job,
            "steps",
            "jobs.repository-guards");
        if (steps.Children.Count != 4)
        {
            throw new InvalidOperationException(
                "jobs.repository-guards must contain exactly four steps.");
        }

        YamlMappingNode checkout = RequireMapping(
            steps.Children[0],
            "jobs.repository-guards checkout step");
        RequireExactKeys(
            checkout,
            ["uses"],
            "jobs.repository-guards checkout step");
        RequireScalarValue(
            checkout,
            "uses",
            "actions/checkout@v7",
            "jobs.repository-guards checkout step");

        YamlMappingNode setup = RequireMapping(
            steps.Children[1],
            "jobs.repository-guards setup step");
        RequireExactKeys(
            setup,
            ["name", "uses", "with"],
            "jobs.repository-guards setup step");
        RequireScalarValue(
            setup,
            "name",
            "Setup .NET",
            "jobs.repository-guards setup step");
        RequireScalarValue(
            setup,
            "uses",
            "actions/setup-dotnet@v6",
            "jobs.repository-guards setup step");
        RequireExactScalarValues(
            GetRequiredMapping(
                setup,
                "with",
                "jobs.repository-guards setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = "11.0.100-rc.1.26425.128",
            },
            "jobs.repository-guards setup step.with");

        YamlMappingNode cache = RequireMapping(
            steps.Children[2],
            "jobs.repository-guards cache step");
        RequireExactKeys(
            cache,
            ["name", "uses", "with"],
            "jobs.repository-guards cache step");
        RequireScalarValue(
            cache,
            "name",
            "Cache NuGet packages",
            "jobs.repository-guards cache step");
        RequireScalarValue(
            cache,
            "uses",
            "actions/cache@v6",
            "jobs.repository-guards cache step");
        RequireExactScalarValues(
            GetRequiredMapping(
                cache,
                "with",
                "jobs.repository-guards cache step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["path"] = "~/.nuget/packages",
                ["key"] =
                    "nuget-${{ runner.os }}-${{ runner.arch }}-" +
                    "${{ hashFiles('**/*.csproj', '**/*.props', " +
                    "'**/*.targets', '**/*.slnx') }}",
                ["restore-keys"] =
                    "nuget-${{ runner.os }}-${{ runner.arch }}-\n",
            },
            "jobs.repository-guards cache step.with");

        RequireNamedRunStep(
            steps.Children[3],
            "Run legacy source-identity guard",
            "dotnet run --project tests/NuGetFetch.Tests -c Release -- " +
                "--filter-method \"*LegacyPackageSourceIdentitySurfaceMatchesMigrationSet\" " +
                "--minimum-expected-tests 1\n",
            "jobs.repository-guards source-identity step");
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
            ["skill-gate/Run embedded skill tests"] = "bash",
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
            bool hasRunStepWithoutWorkingDirectory = false;
            foreach (YamlNode stepNode in steps.Children)
            {
                YamlMappingNode step = RequireMapping(
                    stepNode,
                    $"jobs.{jobName} step");
                hasRunStepWithoutShell |=
                    TryGetNode(step, "run", out _)
                    && !TryGetNode(step, "shell", out _);
                hasRunStepWithoutWorkingDirectory |=
                    TryGetNode(step, "run", out _)
                    && !TryGetNode(step, "working-directory", out _);
                string? identity = GetOptionalScalar(step, "name") ??
                    GetOptionalScalar(step, "uses");
                if (identity is null || !identities.Add(identity))
                {
                    throw new InvalidOperationException(
                        $"jobs.{jobName} steps must have unique names or uses.");
                }

                string key = $"{jobName}/{identity}";
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
            if (hasRunStepWithoutWorkingDirectory)
            {
                RequireRootWorkingDirectoryFromRunDefaults(
                    job,
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

    private static void RequireRootWorkingDirectoryFromRunDefaults(
        YamlMappingNode job,
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

        YamlMappingNode run = RequireMapping(
            runNode,
            $"{context}.defaults.run");
        string? workingDirectory = GetOptionalScalar(
            run,
            "working-directory");
        if (workingDirectory is null
            || IsStaticRepositoryRootWorkingDirectory(workingDirectory))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{context}.defaults.run.working-directory must resolve to " +
            $"the repository root, got {workingDirectory}.");
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
