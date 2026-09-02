using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    internal static WorkflowContractResult Load(
        string repository,
        string workflowText,
        bool validateProvenancePin = true)
    {
        using TextReader reader = new StringReader(workflowText);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one workflow document, found {yaml.Documents.Count}.");
        }

        DecompilerProjectGraphPolicy.Validate(repository);

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "workflow root");
        RequireExactScalarValues(
            GetRequiredMapping(root, "env", "workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DOTNET_NOLOGO"] = "true",
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
            },
            "workflow.env");
        RequireAbsent(root, "defaults", "workflow");
        ValidateWorkflowTriggers(root);
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "workflow");
        ValidateAggregateStructuralCheck(jobs);
        YamlMappingNode changes = GetRequiredMapping(jobs, "changes", "jobs");
        RequireAbsent(changes, "if", "jobs.changes");
        RequireAbsent(changes, "continue-on-error", "jobs.changes");
        RequireAbsent(changes, "defaults", "jobs.changes");
        RequireAbsent(changes, "env", "jobs.changes");

        ValidateInspectWebSdk(jobs);
        ValidatePackageManifestVerifierBuild(jobs);
        ValidateTlaJob(jobs);

        YamlSequenceNode steps = GetRequiredSequence(
            changes,
            "steps",
            "jobs.changes");
        if (steps.Children.Count != 5)
        {
            throw new InvalidOperationException(
                "jobs.changes must contain checkout, setup, self-test, " +
                "provenance, and planning steps.");
        }

        ValidateCheckoutStep(steps);

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

        return new WorkflowContractResult(
            provenanceRunSha256,
            provenancePin);
    }

    private static void ValidateInspectWebSdk(YamlMappingNode jobs)
    {
        YamlMappingNode inspectWeb =
            GetRequiredMapping(jobs, "inspect-web", "jobs");
        RequireAbsent(
            inspectWeb,
            "continue-on-error",
            "jobs.inspect-web");
        RequireAbsent(
            inspectWeb,
            "defaults",
            "jobs.inspect-web");
        YamlSequenceNode inspectWebSteps = GetRequiredSequence(
            inspectWeb,
            "steps",
            "jobs.inspect-web");
        List<YamlMappingNode> webSdkSteps = [];
        foreach (YamlNode stepNode in inspectWebSteps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                "jobs.inspect-web step");
            if (GetOptionalScalar(step, "uses") == "actions/setup-dotnet@v5")
            {
                webSdkSteps.Add(step);
            }
        }
        if (webSdkSteps.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one inspect-web setup-dotnet step, " +
                $"found {webSdkSteps.Count}.");
        }
        YamlMappingNode webSdkWith = GetRequiredMapping(
            webSdkSteps[0],
            "with",
            "jobs.inspect-web setup-dotnet");
        RequireScalarValue(
            webSdkWith,
            "dotnet-version",
            "11.0.x",
            "jobs.inspect-web setup-dotnet.with");
        RequireScalarValue(
            webSdkWith,
            "dotnet-quality",
            "preview",
            "jobs.inspect-web setup-dotnet.with");
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

        RequireExactKeys(
            verifierBuildSteps[0],
            ["name", "run"],
            "jobs.test package-manifest corpus verifier build step");
        RequireScalarValue(
            verifierBuildSteps[0],
            "run",
            "dotnet build eng/verify-package-manifest-corpus.cs -c Release",
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
            ["uses", "with"],
            "jobs.tla-plus checkout step");
        RequireScalarValue(
            checkout,
            "uses",
            "actions/checkout@v6",
            "jobs.tla-plus checkout step");
        RequireExactScalarValues(
            GetRequiredMapping(
                checkout,
                "with",
                "jobs.tla-plus checkout step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["fetch-depth"] = "0",
            },
            "jobs.tla-plus checkout step.with");

        List<YamlMappingNode> scopeTests = [];
        List<YamlMappingNode> runs = [];
        foreach (YamlNode stepNode in steps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                "jobs.tla-plus step");
            switch (GetOptionalScalar(step, "name"))
            {
                case "Self-test TLA+ runner scope":
                    scopeTests.Add(step);
                    break;
                case "Run TLA+ checks":
                    runs.Add(step);
                    break;
            }
        }

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
                ["CI_BEFORE_SHA"] =
                    "${{ github.event.pull_request.base.sha || " +
                    "github.event.merge_group.base_sha || " +
                    "github.event.before }}",
            },
            "jobs.tla-plus run step.env");

        string run = GetRequiredScalar(
            runs[0],
            "run",
            "jobs.tla-plus run step");
        if (!run.Contains(
                "git diff --no-renames --name-only -z " +
                "\"$CI_BEFORE_SHA\" HEAD --",
                StringComparison.Ordinal)
            || !run.Contains(
                "eng/run-tla-checks.sh --changed-files0",
                StringComparison.Ordinal)
            || run.Contains(
                "eng/run-tla-checks.sh --all",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "jobs.tla-plus must pipe the base-to-head changed-file " +
                "stream to the scoped TLA+ runner without a whole-repository " +
                "fallback.");
        }
    }

    private static void ValidateWorkflowTriggers(YamlMappingNode root)
    {
        YamlMappingNode triggers =
            GetRequiredMapping(root, "on", "workflow");
        RequireExactKeys(
            triggers,
            ["push", "pull_request", "merge_group"],
            "workflow.on");

        YamlMappingNode push =
            GetRequiredMapping(triggers, "push", "workflow.on");
        RequireExactKeys(push, ["branches"], "workflow.on.push");
        YamlSequenceNode pushBranches =
            GetRequiredSequence(push, "branches", "workflow.on.push");
        if (pushBranches.Children.Count != 1 ||
            RequireScalar(
                pushBranches.Children[0],
                "workflow.on.push.branches entry") != "main")
        {
            throw new InvalidOperationException(
                "workflow.on.push.branches must contain only main.");
        }

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
            "actions/checkout@v6",
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
            "actions/setup-dotnet@v5",
            "jobs.changes .NET setup step");
        RequireExactScalarValues(
            GetRequiredMapping(
                setupStep,
                "with",
                "jobs.changes .NET setup step"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = "11.0.x",
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
