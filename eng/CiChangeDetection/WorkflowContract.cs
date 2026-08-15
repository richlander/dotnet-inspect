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
        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "workflow");
        ValidateAggregateStructuralCheck(jobs);
        ValidateOutputConsumers(jobs);
        YamlMappingNode changes = GetRequiredMapping(jobs, "changes", "jobs");
        RequireAbsent(changes, "if", "jobs.changes");
        RequireAbsent(changes, "continue-on-error", "jobs.changes");
        RequireAbsent(changes, "defaults", "jobs.changes");
        RequireAbsent(changes, "env", "jobs.changes");

        YamlMappingNode outputMappings =
            GetRequiredMapping(changes, "outputs", "jobs.changes");
        List<string> declaredOutputs = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in outputMappings.Children)
        {
            string name = RequireScalar(
                keyNode,
                "jobs.changes output name");
            string binding = RequireScalar(
                valueNode,
                $"jobs.changes.outputs.{name} binding");
            string expectedBinding =
                "${{ steps.filter.outputs." + name + " }}";
            if (binding != expectedBinding)
            {
                throw new InvalidOperationException(
                    $"Invalid jobs.changes.outputs.{name} binding.");
            }

            declaredOutputs.Add(name);
        }

        if (declaredOutputs.Count == 0)
        {
            throw new InvalidOperationException(
                "jobs.changes must declare at least one output.");
        }
        string[] requiredOutputs =
        [
            "code",
            "csharpdiff",
            "decompiler",
            "docs",
            "ildiff",
            "ilroundtrip",
            "packaging",
            "shipped",
            "web",
            "skills",
        ];
        if (!declaredOutputs.ToHashSet(StringComparer.Ordinal)
            .SetEquals(requiredOutputs))
        {
            throw new InvalidOperationException(
                $"jobs.changes must declare exactly: " +
                $"{string.Join(", ", requiredOutputs)}.");
        }

        ValidateInspectWebSdk(jobs);

        YamlSequenceNode steps = GetRequiredSequence(
            changes,
            "steps",
            "jobs.changes");
        if (steps.Children.Count != 5)
        {
            throw new InvalidOperationException(
                "jobs.changes must contain checkout, setup, self-test, " +
                "provenance, and detection steps.");
        }

        ValidateCheckoutStep(steps);

        List<(int Index, YamlMappingNode Step)> detectionSteps = [];
        List<(int Index, YamlMappingNode Step)> selfTestSteps = [];
        for (int index = 0; index < steps.Children.Count; index++)
        {
            YamlMappingNode step = RequireMapping(
                steps.Children[index],
                "jobs.changes step");
            if (GetOptionalScalar(step, "name") == "Detect changes")
            {
                detectionSteps.Add((index, step));
            }
            else if (GetOptionalScalar(step, "name") ==
                "Self-test change detection")
            {
                selfTestSteps.Add((index, step));
            }
        }

        if (detectionSteps.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one jobs.changes Detect changes step, " +
                $"found {detectionSteps.Count}.");
        }

        if (detectionSteps[0].Index != 4)
        {
            throw new InvalidOperationException(
                "Detect changes must run after checkout, .NET setup, " +
                "self-test, and EVIL provenance validation.");
        }

        ValidateSetupStep(steps);
        (string provenanceRunSha256, string provenancePin) =
            ValidateProvenanceStep(steps, validateProvenancePin);
        ValidateSelfTestStep(selfTestSteps);
        string body = ValidateDetectionStep(
            repository,
            detectionSteps[0].Step);

        return new WorkflowContractResult(
            body,
            declaredOutputs.AsReadOnly(),
            provenanceRunSha256,
            provenancePin);
    }

    private static void ValidateInspectWebSdk(YamlMappingNode jobs)
    {
        YamlMappingNode inspectWeb =
            GetRequiredMapping(jobs, "inspect-web", "jobs");
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

    private static string ValidateDetectionStep(
        string repository,
        YamlMappingNode detectionStep)
    {
        RequireScalarValue(
            detectionStep,
            "id",
            "filter",
            "Detect changes");
        RequireScalarValue(
            detectionStep,
            "shell",
            "bash",
            "Detect changes");
        RequireAbsent(detectionStep, "if", "Detect changes");
        RequireAbsent(
            detectionStep,
            "continue-on-error",
            "Detect changes");
        YamlMappingNode detectionEnvironment =
            GetRequiredMapping(detectionStep, "env", "Detect changes");
        RequireExactScalarValues(
            detectionEnvironment,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["BASH_ENV"] = "",
                ["CI_BEFORE_SHA"] = "${{ github.event.before }}",
                ["CI_PR_NUMBER"] =
                    "${{ github.event.pull_request.number }}",
                ["GH_TOKEN"] = "${{ github.token }}",
            },
            "Detect changes.env");
        const string DetectionScript = "eng/ci-detect-changes.sh";
        RequireScalarValue(
            detectionStep,
            "run",
            DetectionScript,
            "Detect changes");
        string detectionScriptPath = Path.Combine(
            repository,
            DetectionScript);
        string body = File.ReadAllText(detectionScriptPath);
        if (body.Length == 0)
        {
            throw new InvalidOperationException(
                "Detect changes has an empty script.");
        }
        if (!OperatingSystem.IsWindows()
            && (File.GetUnixFileMode(detectionScriptPath)
                & UnixFileMode.UserExecute) == 0)
        {
            throw new InvalidOperationException(
                "Detect changes script must be executable.");
        }
        if (!body.StartsWith(
                "#!/usr/bin/env bash\nset -e -o pipefail\n",
                StringComparison.Ordinal)
            || body.Contains("${{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Detect changes script must own its Bash failure mode and " +
                "contain no unevaluated workflow expressions.");
        }

        return body;
    }
}
