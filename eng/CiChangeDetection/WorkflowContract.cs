using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    private static readonly string[] InspectWebLeafJobs =
    [
        "inspect-web-platform",
        "inspect-web-frontend",
        "inspect-web-msdl-tests",
    ];

    private static readonly string[] InspectWebDotnetJobs =
    [
        "inspect-web-platform",
        "inspect-web-frontend",
        "inspect-web-msdl-tests",
    ];

    internal static readonly string[] TestShards =
    [
        "cli-a",
        "cli-b",
        "contracts",
        "host-analysis",
    ];

    internal static WorkflowContractResult Load(
        string repository,
        string workflowText,
        bool validateProvenancePin = true) =>
        Load(
            repository,
            workflowText,
            validateProvenancePin,
            validateProjectGraph: true);

    private static WorkflowContractResult Load(
        string repository,
        string workflowText,
        bool validateProvenancePin,
        bool validateProjectGraph)
    {
        using TextReader reader = new StringReader(workflowText);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one workflow document, found {yaml.Documents.Count}.");
        }

        if (validateProjectGraph)
        {
            DecompilerProjectGraphPolicy.Validate(repository);
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "workflow root");
        RequireScalarValue(root, "name", "PR CI", "workflow");
        RequireAbsent(root, "run-name", "workflow");
        RequireExactScalarValues(
            GetRequiredMapping(root, "env", "workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_NOLOGO"] = "true",
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
            },
            "workflow.env");
        ValidateWorkflowRunDefaults(root);
        ValidateWorkflowTriggers(root);
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "workflow");
        ValidateTestShardMatrix(jobs);
        ValidateRunnerBudget(jobs);
        ValidateAggregateStructuralCheck(jobs);
        ValidateConsumerStepContracts(jobs);
        YamlMappingNode changes = GetRequiredMapping(jobs, "changes", "jobs");
        RequireAbsent(changes, "if", "jobs.changes");
        RequireAbsent(changes, "continue-on-error", "jobs.changes");
        RequireAbsent(changes, "defaults", "jobs.changes");
        RequireAbsent(changes, "env", "jobs.changes");

        ValidateInspectWebTopology(jobs);
        ValidateInspectWebConsolidatedChecks(jobs);
        ValidateInspectWebBrowser(jobs);
        ValidateInspectWebManagedTests(jobs);
        ValidateInspectWebSdk(jobs);
        ValidatePackageManifestVerifierBuild(jobs);
        ValidateTlaJob(jobs);

        YamlSequenceNode steps = GetRequiredSequence(
            changes,
            "steps",
            "jobs.changes");
        if (steps.Children.Count != 9)
        {
            throw new InvalidOperationException(
                "jobs.changes must contain checkout, setup, self-test, " +
                "provenance, planning, TLA+ upload, Markdown, and skill steps.");
        }

        ValidateCheckoutStep(steps);
        ValidateTlaScopeUploadStep(steps);

        List<(int Index, YamlMappingNode Step)> selfTestSteps = [];
        for (int index = 0; index < steps.Children.Count; index++)
        {
            YamlMappingNode step = RequireMapping(
                steps.Children[index],
                "jobs.changes step");
            if (GetOptionalScalar(step, "name") ==
                "Self-test change detection")
            {
                selfTestSteps.Add((index, step));
            }
        }

        ValidateSetupStep(steps);
        (string provenanceRunSha256, string provenancePin) =
            ValidateProvenanceStep(steps, validateProvenancePin);
        ValidateSelfTestStep(selfTestSteps);
        ValidatePlanningStep(steps);
        ValidateChangesChecks(steps);

        return new WorkflowContractResult(
            provenanceRunSha256,
            provenancePin);
    }

    private static void ValidateWorkflowRunDefaults(YamlMappingNode root)
    {
        if (!TryGetNode(root, "defaults", out YamlNode defaultsNode))
        {
            return;
        }

        YamlMappingNode defaults = RequireMapping(
            defaultsNode,
            "workflow.defaults");
        RequireExactKeys(defaults, ["run"], "workflow.defaults");
        YamlMappingNode run = GetRequiredMapping(
            defaults,
            "run",
            "workflow.defaults");
        RequireExactKeys(
            run,
            ["working-directory"],
            "workflow.defaults.run");
        string workingDirectory = GetRequiredScalar(
            run,
            "working-directory",
            "workflow.defaults.run");
        if (!IsStaticRepositoryRootWorkingDirectory(workingDirectory))
        {
            throw new InvalidOperationException(
                "workflow.defaults.run.working-directory must resolve to " +
                $"the repository root, got {workingDirectory}.");
        }
    }

    private static void ValidateTestShardMatrix(YamlMappingNode jobs)
    {
        YamlMappingNode test = GetRequiredMapping(jobs, "test", "jobs");
        YamlMappingNode strategy = GetRequiredMapping(
            test,
            "strategy",
            "jobs.test");
        RequireExactKeys(
            strategy,
            ["fail-fast", "matrix"],
            "jobs.test.strategy");
        RequireScalarValue(
            strategy,
            "fail-fast",
            "false",
            "jobs.test.strategy");

        YamlMappingNode matrix = GetRequiredMapping(
            strategy,
            "matrix",
            "jobs.test.strategy");
        RequireExactKeys(matrix, ["include"], "jobs.test.strategy.matrix");
        YamlSequenceNode include = GetRequiredSequence(
            matrix,
            "include",
            "jobs.test.strategy.matrix");
        if (include.Children.Count != TestShards.Length)
        {
            throw new InvalidOperationException(
                $"jobs.test must define exactly {TestShards.Length} shards.");
        }

        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (YamlNode entryNode in include.Children)
        {
            YamlMappingNode entry = RequireMapping(
                entryNode,
                "jobs.test.strategy.matrix.include entry");
            RequireExactKeys(
                entry,
                ["os", "rid", "shard"],
                "jobs.test.strategy.matrix.include entry");
            RequireScalarValue(
                entry,
                "os",
                "ubuntu-24.04",
                "jobs.test.strategy.matrix.include entry");
            RequireScalarValue(
                entry,
                "rid",
                "linux-x64",
                "jobs.test.strategy.matrix.include entry");
            string shard = RequireScalar(
                entry.Children[new YamlScalarNode("shard")],
                "jobs.test.strategy.matrix.include entry.shard");
            if (!actual.Add(shard))
            {
                throw new InvalidOperationException(
                    $"jobs.test contains duplicate shard '{shard}'.");
            }
        }

        if (!actual.SetEquals(TestShards))
        {
            throw new InvalidOperationException(
                "jobs.test shard names do not match the approved partition.");
        }
    }

    private static void ValidateRunnerBudget(YamlMappingNode jobs)
    {
        // The only matrix is the approved test shard matrix. A new matrix
        // needs explicit counting here before it can enter the workflow.
        foreach (KeyValuePair<YamlNode, YamlNode> entry in jobs.Children)
        {
            string name = RequireScalar(entry.Key, "job name");
            YamlMappingNode job = RequireMapping(entry.Value, $"jobs.{name}");
            if (name != "test")
            {
                RequireAbsent(job, "strategy", $"jobs.{name}");
            }
        }

        // Count every job, including changes, ci-required, path-gated jobs,
        // and the push-only dependency-policy job. This is an upper bound for
        // every event even if selection rules change.
        int maximumJobs = jobs.Children.Count + TestShards.Length - 1;
        if (maximumJobs > 16)
        {
            throw new InvalidOperationException(
                $"CI exceeds the 16-runner-job budget: {maximumJobs}.");
        }
    }

    private static void ValidateChangesChecks(YamlSequenceNode steps)
    {
        YamlMappingNode markdown = RequireMapping(
            steps.Children[6], "jobs.changes markdown step");
        RequireExactKeys(markdown, ["name", "if", "uses", "with"],
            "jobs.changes markdown step");
        RequireScalarValue(markdown, "name", "Run markdownlint", "jobs.changes markdown step");
        RequireScalarValue(markdown, "if",
            "fromJSON(steps.plan.outputs.plan).validations.markdownlint",
            "jobs.changes markdown step");
        RequireScalarValue(markdown, "uses",
            "DavidAnson/markdownlint-cli2-action@v24",
            "jobs.changes markdown step");
        RequireScalarValue(
            GetRequiredMapping(markdown, "with", "jobs.changes markdown step"),
            "globs", "**/*.md", "jobs.changes markdown step.with");

        YamlMappingNode cache = RequireMapping(
            steps.Children[7], "jobs.changes skill cache step");
        RequireExactKeys(cache, ["name", "if", "uses", "with"],
            "jobs.changes skill cache step");
        RequireScalarValue(cache, "name", "Cache NuGet packages for skill tests",
            "jobs.changes skill cache step");
        RequireScalarValue(cache, "if",
            "fromJSON(steps.plan.outputs.plan).validations.skillGate",
            "jobs.changes skill cache step");
        RequireScalarValue(cache, "uses", "actions/cache@v6",
            "jobs.changes skill cache step");

        YamlMappingNode skill = RequireMapping(
            steps.Children[8], "jobs.changes skill step");
        RequireExactKeys(skill, ["name", "if", "shell", "run"],
            "jobs.changes skill step");
        RequireScalarValue(skill, "name", "Run embedded skill tests",
            "jobs.changes skill step");
        RequireScalarValue(skill, "if",
            "fromJSON(steps.plan.outputs.plan).validations.skillGate",
            "jobs.changes skill step");
        RequireScalarValue(skill, "shell", "bash", "jobs.changes skill step");
        RequireScalarValue(skill, "run",
            "dotnet run --project tests/DotnetInspect.Cli.Tests -c Release -- " +
            "--filter-class \"DotnetInspect.Cli.Tests.SkillCommandTests\"",
            "jobs.changes skill step");
    }

    private static void ValidateInspectWebTopology(YamlMappingNode jobs)
    {
        const string Selection =
            "fromJSON(needs.changes.outputs.plan).validations.inspectWeb";
        foreach (string jobName in InspectWebLeafJobs)
        {
            YamlMappingNode job = GetRequiredMapping(jobs, jobName, "jobs");
            RequireScalarValue(job, "needs", "changes", $"jobs.{jobName}");
            RequireScalarValue(job, "if", Selection, $"jobs.{jobName}");
        }

        YamlMappingNode aggregate =
            GetRequiredMapping(jobs, "inspect-web", "jobs");
        RequireScalarValue(
            aggregate,
            "if",
            $"always() && {Selection}",
            "jobs.inspect-web");
        YamlSequenceNode needs = GetRequiredSequence(
            aggregate,
            "needs",
            "jobs.inspect-web");
        HashSet<string> actual = needs.Children
            .Select(node => RequireScalar(node, "jobs.inspect-web need"))
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> expected =
            InspectWebLeafJobs.Append("changes")
                .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
        {
            throw new InvalidOperationException(
                "jobs.inspect-web.needs must contain changes and every " +
                "inspect-web leaf job exactly once.");
        }

        YamlSequenceNode aggregateSteps = GetRequiredSequence(
            aggregate,
            "steps",
            "jobs.inspect-web");
        string aggregateRun = GetRequiredScalar(
            RequireMapping(aggregateSteps.Children[0], "jobs.inspect-web step"),
            "run",
            "jobs.inspect-web step");
        if (!aggregateRun.Contains(
                $"($results | length) == {InspectWebLeafJobs.Length}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "jobs.inspect-web must require every current leaf result.");
        }
    }

    private static void ValidateInspectWebConsolidatedChecks(
        YamlMappingNode jobs)
    {
        ValidateRequiredRunStep(
            jobs,
            "inspect-web-platform",
            "Run MethodSemantics Browser/Wasm platform probe",
            "eng/run-method-semantics-platform-probe.sh browser");
        ValidateRequiredRunStep(
            jobs,
            "inspect-web-platform",
            "Run local-path admission Browser/Wasm platform probe",
            "eng/run-local-path-admission-platform-probe.sh browser");
        ValidateRequiredRunStep(
            jobs,
            "inspect-web-msdl-tests",
            "Test Inspect Web managed API",
            "dotnet run --project tests/MsdlProxy.Tests -c Release");
        ValidateInspectWebScriptStep(
            jobs,
            "inspect-web-msdl-tests",
            "Publish Inspect Web managed API",
            "src/MsdlProxy/MsdlProxy.csproj");
        ValidateInspectWebScriptStep(
            jobs,
            "inspect-web-msdl-tests",
            "Verify Inspect Web managed API artifact",
            "artifacts/inspect-web-api");
    }

    private static void ValidateInspectWebScriptStep(
        YamlMappingNode jobs,
        string jobName,
        string stepName,
        string requiredCommand)
    {
        YamlSequenceNode steps = GetRequiredSequence(
            GetRequiredMapping(jobs, jobName, "jobs"),
            "steps",
            $"jobs.{jobName}");
        YamlMappingNode[] matches = steps.Children
            .Select(node => RequireMapping(node, $"jobs.{jobName} step"))
            .Where(step => GetOptionalScalar(step, "name") == stepName)
            .ToArray();
        if (matches.Length != 1 || !GetRequiredScalar(
                matches[0],
                "run",
                $"jobs.{jobName} {stepName}").Contains(
                    requiredCommand,
                    StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"jobs.{jobName} must run {stepName} exactly once.");
        }
    }

    private static void ValidateInspectWebBrowser(YamlMappingNode jobs)
    {
        YamlMappingNode browser =
            GetRequiredMapping(jobs, "inspect-web-frontend", "jobs");
        YamlSequenceNode steps = GetRequiredSequence(
            browser,
            "steps",
            "jobs.inspect-web-frontend");
        List<YamlMappingNode> buildSteps = [];
        List<YamlMappingNode> installSteps = [];
        List<YamlMappingNode> testSteps = [];
        foreach (YamlNode stepNode in steps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                "jobs.inspect-web-frontend step");
            switch (GetOptionalScalar(step, "name"))
            {
                case "Build and analyze browser frontend":
                    buildSteps.Add(step);
                    break;
                case "Install Firefox":
                    installSteps.Add(step);
                    break;
                case "Test browser UI in Firefox":
                    testSteps.Add(step);
                    break;
            }
        }

        if (buildSteps.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected one jobs.inspect-web-frontend build step.");
        }
        RequireScalarValue(
            buildSteps[0],
            "working-directory",
            "inspect-web",
            "jobs.inspect-web-frontend build step");
        if (installSteps.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected one jobs.inspect-web-frontend Firefox install step.");
        }
        RequireScalarValue(
            installSteps[0],
            "working-directory",
            "inspect-web",
            "jobs.inspect-web-frontend Firefox install step");
        RequireScalarValue(
            installSteps[0],
            "run",
            "npx playwright install --with-deps firefox",
            "jobs.inspect-web-frontend Firefox install step");

        if (testSteps.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected one jobs.inspect-web-frontend browser test step.");
        }
        RequireScalarValue(
            testSteps[0],
            "working-directory",
            "inspect-web",
            "jobs.inspect-web-frontend test step");
        RequireScalarValue(
            testSteps[0],
            "run",
            "npm run test:browser",
            "jobs.inspect-web-frontend test step");
    }

    private static void ValidateInspectWebManagedTests(YamlMappingNode jobs)
    {
        YamlMappingNode managedTests =
            GetRequiredMapping(jobs, "inspect-web-platform", "jobs");
        YamlSequenceNode steps = GetRequiredSequence(
            managedTests,
            "steps",
            "jobs.inspect-web-platform");
        YamlMappingNode[] managedSteps = steps.Children
            .Select(node => RequireMapping(node, "jobs.inspect-web-platform step"))
            .Where(step => GetOptionalScalar(step, "name") == "Test browser engine")
            .ToArray();
        if (managedSteps.Length != 1)
        {
            throw new InvalidOperationException(
                "jobs.inspect-web-platform must test the browser engine once.");
        }
        YamlMappingNode testStep = managedSteps[0];
        RequireExactKeys(
            testStep,
            ["name", "env", "run"],
            "jobs.inspect-web-platform test step");
        RequireScalarValue(
            testStep,
            "name",
            "Test browser engine",
            "jobs.inspect-web-platform test step");
        RequireScalarValue(
            GetRequiredMapping(
                testStep,
                "env",
                "jobs.inspect-web-platform test step"),
            "MSBuildEnableWorkloadResolver",
            "false",
            "jobs.inspect-web-platform test step.env");
        RequireScalarValue(
            testStep,
            "run",
            "dotnet run --project tests/DotnetInspect.Web.Tests -c Release",
            "jobs.inspect-web-platform test step");
    }

    private static void ValidateInspectWebSdk(YamlMappingNode jobs)
    {
        foreach (string jobName in InspectWebDotnetJobs)
        {
            YamlMappingNode job = GetRequiredMapping(jobs, jobName, "jobs");
            RequireAbsent(
                job,
                "continue-on-error",
                $"jobs.{jobName}");
            RequireAbsent(
                job,
                "defaults",
                $"jobs.{jobName}");
            YamlSequenceNode steps = GetRequiredSequence(
                job,
                "steps",
                $"jobs.{jobName}");
            List<YamlMappingNode> webSdkSteps = [];
            foreach (YamlNode stepNode in steps.Children)
            {
                YamlMappingNode step = RequireMapping(
                    stepNode,
                    $"jobs.{jobName} step");
                if (GetOptionalScalar(step, "uses") == "actions/setup-dotnet@v6")
                {
                    webSdkSteps.Add(step);
                }
            }
            if (webSdkSteps.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one {jobName} setup-dotnet step, " +
                    $"found {webSdkSteps.Count}.");
            }
            YamlMappingNode webSdkWith = GetRequiredMapping(
                webSdkSteps[0],
                "with",
                $"jobs.{jobName} setup-dotnet");
            RequireScalarValue(
                webSdkWith,
                "dotnet-version",
                "11.0.100-rc.1.26425.128",
                $"jobs.{jobName} setup-dotnet.with");
            RequireAbsent(
                webSdkWith,
                "dotnet-quality",
                $"jobs.{jobName} setup-dotnet.with");
        }
    }

    private static void ValidatePackageManifestVerifierBuild(
        YamlMappingNode jobs)
    {
        YamlMappingNode test = GetRequiredMapping(
            jobs,
            "test",
            "jobs");
        YamlSequenceNode steps = GetRequiredSequence(
            test,
            "steps",
            "jobs.test");
        List<YamlMappingNode> verifierBuildSteps = [];
        foreach (YamlNode stepNode in steps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                "jobs.test step");
            if (GetOptionalScalar(step, "name") ==
                "Build package-manifest corpus verifier")
            {
                verifierBuildSteps.Add(step);
            }
        }

        if (verifierBuildSteps.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected one jobs.test package-manifest corpus verifier build step.");
        }

        YamlMappingNode verifierBuildStep = verifierBuildSteps[0];
        RequireExactKeys(
            verifierBuildStep,
            TryGetNode(
                verifierBuildStep,
                "working-directory",
                out _)
                ? ["name", "if", "run", "working-directory"]
                : ["name", "if", "run"],
            "jobs.test package-manifest corpus verifier build step");
        RequireScalarValue(
            verifierBuildStep,
            "if",
            "matrix.shard == 'host-analysis'",
            "jobs.test package-manifest corpus verifier build step");
        RequireScalarValue(
            verifierBuildStep,
            "run",
            "dotnet build eng/verify-package-manifest-corpus.cs -c Release",
            "jobs.test package-manifest corpus verifier build step");
        RequireRepositoryRootWorkingDirectory(
            test,
            verifierBuildStep,
            "jobs.test package-manifest corpus verifier build step");
    }

    private static void ValidateTlaJob(YamlMappingNode jobs)
    {
        YamlMappingNode tla = GetRequiredMapping(jobs, "tla-plus", "jobs");
        RequireAbsent(tla, "continue-on-error", "jobs.tla-plus");
        RequireAbsent(tla, "defaults", "jobs.tla-plus");
        RequireAbsent(tla, "env", "jobs.tla-plus");

        YamlSequenceNode steps = GetRequiredSequence(
            tla,
            "steps",
            "jobs.tla-plus");
        if (steps.Children.Count == 0)
        {
            throw new InvalidOperationException(
                "jobs.tla-plus must contain steps.");
        }

        YamlMappingNode checkout = RequireMapping(
            steps.Children[0],
            "jobs.tla-plus checkout step");
        RequireExactKeys(
            checkout,
            ["uses"],
            "jobs.tla-plus checkout step");
        RequireScalarValue(
            checkout,
            "uses",
            "actions/checkout@v7",
            "jobs.tla-plus checkout step");

        List<YamlMappingNode> downloads = [];
        List<YamlMappingNode> scopeTests = [];
        List<YamlMappingNode> runs = [];
        foreach (YamlNode stepNode in steps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                "jobs.tla-plus step");
            switch (GetOptionalScalar(step, "name"))
            {
                case "Download TLA+ scope evidence":
                    downloads.Add(step);
                    break;
                case "Self-test TLA+ runner scope":
                    scopeTests.Add(step);
                    break;
                case "Run TLA+ checks":
                    runs.Add(step);
                    break;
            }
        }

        if (downloads.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected one jobs.tla-plus scope download step.");
        }
        RequireExactKeys(
            downloads[0],
            ["name", "uses", "with"],
            "jobs.tla-plus scope download step");
        RequireScalarValue(
            downloads[0],
            "uses",
            "actions/download-artifact@v8",
            "jobs.tla-plus scope download step");
        RequireExactScalarValues(
            GetRequiredMapping(
                downloads[0],
                "with",
                "jobs.tla-plus scope download step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] =
                    "${{ fromJSON(needs.changes.outputs.plan).scopes.tla.artifact }}",
                ["path"] = "${{ runner.temp }}/ci-plan",
            },
            "jobs.tla-plus scope download step.with");

        if (scopeTests.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected one jobs.tla-plus scope self-test step.");
        }
        RequireScalarValue(
            scopeTests[0],
            "shell",
            "bash",
            "jobs.tla-plus scope self-test step");
        RequireScalarValue(
            scopeTests[0],
            "run",
            "eng/test-tla-checks.sh",
            "jobs.tla-plus scope self-test step");

        if (runs.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected one jobs.tla-plus run step.");
        }
        RequireScalarValue(
            runs[0],
            "shell",
            "bash",
            "jobs.tla-plus run step");
        RequireExactScalarValues(
            GetRequiredMapping(
                runs[0],
                "env",
                "jobs.tla-plus run step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TLA_SCOPE_ARTIFACT"] =
                    "${{ fromJSON(needs.changes.outputs.plan).scopes.tla.artifact }}",
                ["TLA_SCOPE_FRAMING"] =
                    "${{ fromJSON(needs.changes.outputs.plan).scopes.tla.framing }}",
                ["TLA_SCOPE_RECORD_COUNT"] =
                    "${{ fromJSON(needs.changes.outputs.plan).scopes.tla.recordCount }}",
                ["TLA_SCOPE_SHA256"] =
                    "${{ fromJSON(needs.changes.outputs.plan).scopes.tla.sha256 }}",
            },
            "jobs.tla-plus run step.env");

        string run = GetRequiredScalar(
            runs[0],
            "run",
            "jobs.tla-plus run step");
        if (!run.Contains(
                "[ \"$TLA_SCOPE_ARTIFACT\" != \"ci-plan-tla-paths0\" ]",
                StringComparison.Ordinal)
            || !run.Contains(
                "[ \"$TLA_SCOPE_FRAMING\" != " +
                "\"pathBytesNulTerminated\" ]",
                StringComparison.Ordinal)
            || !run.Contains(
                "actual_sha256=$(sha256sum \"$scope_file\"",
                StringComparison.Ordinal)
            || !run.Contains(
                "\"$actual_sha256\" != \"$TLA_SCOPE_SHA256\"",
                StringComparison.Ordinal)
            || !run.Contains(
                "tr -cd '\\000' < \"$scope_file\"",
                StringComparison.Ordinal)
            || !run.Contains(
                "\"$actual_record_count\" != " +
                "\"$TLA_SCOPE_RECORD_COUNT\"",
                StringComparison.Ordinal)
            || !run.Contains(
                "eng/run-tla-checks.sh --changed-files0 < \"$scope_file\"",
                StringComparison.Ordinal)
            || run.Contains(
                "eng/run-tla-checks.sh --all",
                StringComparison.Ordinal)
            || run.Contains(
                "git diff",
                StringComparison.Ordinal)
            || run.Contains(
                "CI_BEFORE_SHA",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "jobs.tla-plus must verify and consume the planner-produced " +
                "scope without independent provenance or a whole-repository " +
                "fallback.");
        }
    }

    private static void ValidateTlaScopeUploadStep(YamlSequenceNode steps)
    {
        YamlMappingNode upload = RequireMapping(
            steps.Children[5],
            "jobs.changes TLA+ scope upload step");
        RequireExactKeys(
            upload,
            ["name", "if", "uses", "with"],
            "jobs.changes TLA+ scope upload step");
        RequireScalarValue(
            upload,
            "name",
            "Upload TLA+ scope evidence",
            "jobs.changes TLA+ scope upload step");
        RequireScalarValue(
            upload,
            "if",
            "fromJSON(steps.plan.outputs.plan).validations.tla",
            "jobs.changes TLA+ scope upload step");
        RequireScalarValue(
            upload,
            "uses",
            "actions/upload-artifact@v7",
            "jobs.changes TLA+ scope upload step");
        RequireExactScalarValues(
            GetRequiredMapping(
                upload,
                "with",
                "jobs.changes TLA+ scope upload step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] =
                    "${{ fromJSON(steps.plan.outputs.plan).scopes.tla.artifact }}",
                ["path"] =
                    "${{ runner.temp }}/ci-plan/${{ fromJSON(steps.plan.outputs.plan).scopes.tla.artifact }}",
                ["if-no-files-found"] = "error",
                ["retention-days"] = "1",
            },
            "jobs.changes TLA+ scope upload step.with");
    }

    private static void ValidateWorkflowTriggers(YamlMappingNode root)
    {
        YamlMappingNode triggers =
            GetRequiredMapping(root, "on", "workflow");
        RequireExactKeys(
            triggers,
            ["workflow_call", "pull_request", "merge_group"],
            "workflow.on");

        YamlMappingNode workflowCall =
            GetRequiredMapping(triggers, "workflow_call", "workflow.on");
        RequireExactKeys(
            workflowCall,
            ["inputs"],
            "workflow.on.workflow_call");
        YamlMappingNode inputs = GetRequiredMapping(
            workflowCall,
            "inputs",
            "workflow.on.workflow_call");
        RequireExactKeys(
            inputs,
            ["event-name", "base-sha"],
            "workflow.on.workflow_call.inputs");
        ValidateRequiredStringInput(inputs, "event-name");
        ValidateRequiredStringInput(inputs, "base-sha");

        if (!TryGetNode(
                triggers,
                "pull_request",
                out YamlNode pullRequest) ||
            pullRequest is not YamlScalarNode { Value: null or "" })
        {
            throw new InvalidOperationException(
                "workflow.on.pull_request must be unfiltered.");
        }

        YamlMappingNode mergeGroup =
            GetRequiredMapping(triggers, "merge_group", "workflow.on");
        RequireExactKeys(
            mergeGroup,
            ["types"],
            "workflow.on.merge_group");
        YamlSequenceNode mergeGroupTypes =
            GetRequiredSequence(
                mergeGroup,
                "types",
                "workflow.on.merge_group");
        if (mergeGroupTypes.Children.Count != 1 ||
            RequireScalar(
                mergeGroupTypes.Children[0],
                "workflow.on.merge_group.types entry") !=
                "checks_requested")
        {
            throw new InvalidOperationException(
                "workflow.on.merge_group.types must contain only " +
                "checks_requested.");
        }
    }

    private static void ValidateRequiredStringInput(
        YamlMappingNode inputs,
        string inputName) =>
        RequireExactScalarValues(
            GetRequiredMapping(
                inputs,
                inputName,
                "workflow.on.workflow_call.inputs"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["required"] = "true",
                ["type"] = "string",
            },
            $"workflow.on.workflow_call.inputs.{inputName}");

    private static void ValidatePlanningStep(YamlSequenceNode steps)
    {
        YamlMappingNode planningStep = RequireMapping(
            steps.Children[4],
            "jobs.changes planning step");
        RequireExactKeys(
            planningStep,
            ["name", "id", "shell", "run", "env"],
            "jobs.changes planning step");
        RequireScalarValue(
            planningStep,
            "name",
            "Plan changes",
            "jobs.changes planning step");
        RequireScalarValue(
            planningStep,
            "id",
            "plan",
            "jobs.changes planning step");
        RequireScalarValue(
            planningStep,
            "shell",
            "bash",
            "jobs.changes planning step");
        RequireExactScalarValues(
            GetRequiredMapping(
                planningStep,
                "env",
                "jobs.changes planning step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BASH_ENV"] = "",
                ["CI_EVENT_NAME"] =
                    "${{ inputs.event-name || github.event_name }}",
                ["CI_EVENT_BASE_SHA"] =
                    "${{ inputs.base-sha || github.event.merge_group.base_sha || github.event.before }}",
            },
            "jobs.changes planning step.env");
        string run = GetRequiredScalar(
            planningStep,
            "run",
            "jobs.changes planning step");
        if (!run.Contains(
                "case \"$CI_EVENT_NAME\" in",
                StringComparison.Ordinal) ||
            !run.Contains(
                "Unsupported CI planning event: $CI_EVENT_NAME",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "jobs.changes planning step.run must dispatch and report " +
                "through CI_EVENT_NAME.");
        }
    }

    private static void ValidateCheckoutStep(YamlSequenceNode steps)
    {
        YamlMappingNode checkoutStep = RequireMapping(
            steps.Children[0],
            "jobs.changes checkout step");
        RequireExactKeys(
            checkoutStep,
            ["uses", "with"],
            "jobs.changes checkout step");
        RequireScalarValue(
            checkoutStep,
            "uses",
            "actions/checkout@v7",
            "jobs.changes checkout step");
        RequireExactScalarValues(
            GetRequiredMapping(
                checkoutStep,
                "with",
                "jobs.changes checkout step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["fetch-depth"] = "0",
            },
            "jobs.changes checkout step.with");
    }

    private static void ValidateSetupStep(YamlSequenceNode steps)
    {
        YamlMappingNode setupStep = RequireMapping(
            steps.Children[1],
            "jobs.changes .NET setup step");
        RequireExactKeys(
            setupStep,
            ["uses", "with"],
            "jobs.changes .NET setup step");
        RequireScalarValue(
            setupStep,
            "uses",
            "actions/setup-dotnet@v6",
            "jobs.changes .NET setup step");
        RequireExactScalarValues(
            GetRequiredMapping(
                setupStep,
                "with",
                "jobs.changes .NET setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = "11.0.100-rc.1.26425.128",
            },
            "jobs.changes .NET setup step.with");
    }

    private static (string RunSha256, string Pin) ValidateProvenanceStep(
        YamlSequenceNode steps,
        bool validateProvenancePin)
    {
        YamlMappingNode provenanceStep = RequireMapping(
            steps.Children[3],
            "jobs.changes EVIL provenance step");
        RequireExactKeys(
            provenanceStep,
            ["name", "shell", "env", "run"],
            "jobs.changes EVIL provenance step");
        RequireScalarValue(
            provenanceStep,
            "name",
            "Check EVIL history provenance",
            "jobs.changes EVIL provenance step");
        RequireScalarValue(
            provenanceStep,
            "shell",
            "bash",
            "jobs.changes EVIL provenance step");
        YamlMappingNode provenanceEnvironment = GetRequiredMapping(
            provenanceStep,
            "env",
            "jobs.changes EVIL provenance step");
        RequireExactKeys(
            provenanceEnvironment,
            ["EVIL_PROVENANCE_RUN_SHA256"],
            "jobs.changes EVIL provenance step.env");
        string provenancePin = GetRequiredScalar(
            provenanceEnvironment,
            "EVIL_PROVENANCE_RUN_SHA256",
            "jobs.changes EVIL provenance step.env");
        RequireSha256(
            provenancePin,
            "jobs.changes EVIL provenance step.env");
        string provenanceRun = GetRequiredScalar(
            provenanceStep,
            "run",
            "jobs.changes EVIL provenance step");
        string provenanceRunSha256 = ComputeSha256(provenanceRun);
        if (validateProvenancePin)
        {
            ProvenancePin.AssertCurrent(
                provenanceRunSha256,
                provenancePin);
        }
        RequireAbsent(
            provenanceStep,
            "if",
            "jobs.changes EVIL provenance step");
        RequireAbsent(
            provenanceStep,
            "continue-on-error",
            "jobs.changes EVIL provenance step");
        RequireAbsent(
            provenanceStep,
            "working-directory",
            "jobs.changes EVIL provenance step");
        return (provenanceRunSha256, provenancePin);
    }

    private static void ValidateSelfTestStep(
        List<(int Index, YamlMappingNode Step)> selfTestSteps)
    {
        if (selfTestSteps.Count != 1 ||
            selfTestSteps[0].Index != 2)
        {
            throw new InvalidOperationException(
                "Self-test change detection must run once before EVIL " +
                "provenance validation.");
        }

        YamlMappingNode selfTestStep = selfTestSteps[0].Step;
        RequireExactKeys(
            selfTestStep,
            ["name", "shell", "run", "env"],
            "Self-test change detection");
        RequireScalarValue(
            selfTestStep,
            "run",
            "dotnet run eng/test-ci-change-detection.cs",
            "Self-test change detection");
        RequireScalarValue(
            selfTestStep,
            "shell",
            "bash",
            "Self-test change detection");
        RequireExactScalarValues(
            GetRequiredMapping(
                selfTestStep,
                "env",
                "Self-test change detection"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BASH_ENV"] = "",
            },
            "Self-test change detection.env");
        RequireAbsent(
            selfTestStep,
            "if",
            "Self-test change detection");
        RequireAbsent(
            selfTestStep,
            "continue-on-error",
            "Self-test change detection");
        RequireAbsent(
            selfTestStep,
            "working-directory",
            "Self-test change detection");
    }

}
