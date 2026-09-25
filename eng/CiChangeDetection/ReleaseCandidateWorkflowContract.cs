using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class ReleaseCandidateWorkflowContract
{
    internal static void AssertMutations(string repository)
    {
        string candidate = File.ReadAllText(
            Path.Combine(
                repository,
                ".github",
                "workflows",
                "release-candidate.yml"));
        string deepInspect = File.ReadAllText(
            Path.Combine(
                repository,
                ".github",
                "workflows",
                "deep-inspect.yml"));

        ValidateCandidate(candidate);
        ValidateDeepInspectAdoption(deepInspect);

        AssertMutationRejected(
            candidate,
            "  cancel-in-progress: false\n",
            "  cancel-in-progress: true\n",
            ValidateCandidate,
            "Release candidate contract accepted active-run cancellation.");
        AssertMutationRejected(
            candidate,
            "    needs: assemble\n",
            "    needs: source\n",
            ValidateCandidate,
            "Release candidate contract accepted certification before asset assembly.");
        AssertMutationRejected(
            candidate,
            "          retention-days: 30\n",
            "          retention-days: 1\n",
            ValidateCandidate,
            "Release candidate contract accepted short-lived release assets.");
        AssertMutationRejected(
            candidate,
            "          retention-days: 30\n" +
            "          include-hidden-files: true\n" +
            "          overwrite: true\n",
            "          retention-days: 30\n" +
            "          include-hidden-files: true\n",
            ValidateCandidate,
            "Release candidate contract accepted a non-rerun-safe artifact.");
        AssertMutationRejected(
            candidate,
            "      - name: Verify production site\n" +
            "        run: eng/verify-inspect-web-site-artifact.sh artifacts/inspect-web-publish\n",
            "",
            ValidateCandidate,
            "Release candidate contract accepted an unverified production site.");
        AssertMutationRejected(
            candidate,
            "      lane: release-candidate\n",
            "      lane: test\n",
            ValidateCandidate,
            "Release candidate contract accepted incomplete Deep Inspect evidence.");
        AssertMutationRejected(
            deepInspect,
            "      inputs.lane == 'census' ||\n" +
            "      inputs.lane == 'release-candidate' ||\n",
            "      inputs.lane == 'census' ||\n",
            ValidateDeepInspectAdoption,
            "Deep Inspect contract accepted a missing release-candidate route.");
    }

    private static void ValidateCandidate(string workflow)
    {
        YamlMappingNode root = LoadRoot(workflow, "release candidate workflow");
        RequireExactKeys(
            root,
            ["name", "on", "permissions", "concurrency", "env", "jobs"],
            "release candidate workflow");
        RequireScalarValue(
            root,
            "name",
            "Nightly release candidate",
            "release candidate workflow");

        YamlMappingNode trigger =
            GetRequiredMapping(root, "on", "release candidate workflow");
        RequireExactKeys(
            trigger,
            ["schedule", "workflow_dispatch"],
            "release candidate trigger");
        YamlSequenceNode schedules =
            GetRequiredSequence(trigger, "schedule", "release candidate trigger");
        if (schedules.Children.Count != 1)
            throw new InvalidOperationException("Release candidate must have one schedule.");
        YamlMappingNode schedule =
            RequireMapping(schedules.Children[0], "release candidate schedule");
        RequireExactScalarValues(
            schedule,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cron"] = "17 0 * * *",
            },
            "release candidate schedule");
        if (!TryGetNode(trigger, "workflow_dispatch", out YamlNode dispatch) ||
            dispatch is not YamlScalarNode { Value: null or "" })
        {
            throw new InvalidOperationException(
                "Release candidate workflow_dispatch must not declare inputs.");
        }

        RequireExactScalarValues(
            GetRequiredMapping(root, "concurrency", "release candidate workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["group"] = "nightly-release-candidate",
                ["cancel-in-progress"] = "false",
            },
            "release candidate concurrency");

        YamlMappingNode jobs =
            GetRequiredMapping(root, "jobs", "release candidate workflow");
        RequireExactKeys(
            jobs,
            [
                "source",
                "build-native",
                "build-portable",
                "build-site",
                "assemble",
                "certify",
                "ready",
                "report-failure",
            ],
            "release candidate jobs");

        YamlMappingNode source =
            GetRequiredMapping(jobs, "source", "release candidate jobs");
        string sourceRun = GetRequiredScalar(
            FindStep(source, "Require green main and an unreleased version"),
            "run",
            "release candidate source validation");
        RequireContains(sourceRun, ".check_runs[] | select(.name == \"ci-required\")");
        RequireContains(sourceRun, "test \"$skill_version\" = \"$version\"");
        RequireContains(
            sourceRun,
            "Version $version does not advance released version $release_version.");

        YamlMappingNode buildSite =
            GetRequiredMapping(jobs, "build-site", "release candidate jobs");
        YamlMappingNode verifySite =
            FindStep(buildSite, "Verify production site");
        RequireScalarValue(
            verifySite,
            "run",
            "eng/verify-inspect-web-site-artifact.sh artifacts/inspect-web-publish",
            "release candidate site verification");

        YamlMappingNode assemble =
            GetRequiredMapping(jobs, "assemble", "release candidate jobs");
        RequireSequenceValues(
            GetRequiredSequence(assemble, "needs", "release candidate assemble"),
            ["source", "build-native", "build-portable", "build-site"],
            "release candidate assemble needs");
        YamlMappingNode upload =
            FindStep(assemble, "Upload immutable candidate");
        YamlMappingNode uploadWith =
            GetRequiredMapping(upload, "with", "release candidate upload");
        RequireScalarValue(
            uploadWith,
            "name",
            "dotnet-inspect-release-candidate",
            "release candidate upload");
        RequireScalarValue(
            uploadWith,
            "retention-days",
            "30",
            "release candidate upload");
        RequireScalarValue(
            uploadWith,
            "include-hidden-files",
            "true",
            "release candidate upload");
        RequireScalarValue(
            uploadWith,
            "overwrite",
            "true",
            "release candidate upload");

        YamlMappingNode certify =
            GetRequiredMapping(jobs, "certify", "release candidate jobs");
        RequireScalarValue(certify, "needs", "assemble", "release candidate certify");
        RequireScalarValue(
            certify,
            "uses",
            "./.github/workflows/deep-inspect.yml",
            "release candidate certify");
        RequireScalarValue(
            GetRequiredMapping(certify, "with", "release candidate certify"),
            "lane",
            "release-candidate",
            "release candidate certify");

        YamlMappingNode ready =
            GetRequiredMapping(jobs, "ready", "release candidate jobs");
        RequireScalarValue(ready, "if", "always()", "release candidate ready");
        RequireSequenceValues(
            GetRequiredSequence(ready, "needs", "release candidate ready"),
            ["assemble", "certify"],
            "release candidate ready needs");

        YamlMappingNode report =
            GetRequiredMapping(jobs, "report-failure", "release candidate jobs");
        RequireScalarValue(
            report,
            "if",
            "${{ github.event_name == 'schedule' && failure() }}",
            "release candidate failure report");
        YamlMappingNode reportStep =
            FindStep(report, "Open or update the tracking issue");
        RequireContains(
            GetRequiredScalar(
                reportStep,
                "run",
                "release candidate failure report"),
            "title=\"Nightly release candidate failed\"");
    }

    private static void ValidateDeepInspectAdoption(string workflow)
    {
        YamlMappingNode root = LoadRoot(workflow, "Deep Inspect workflow");
        YamlMappingNode trigger = GetRequiredMapping(root, "on", "Deep Inspect workflow");
        YamlMappingNode workflowCall =
            GetRequiredMapping(trigger, "workflow_call", "Deep Inspect trigger");
        YamlMappingNode lane =
            GetRequiredMapping(
                GetRequiredMapping(workflowCall, "inputs", "Deep Inspect workflow_call"),
                "lane",
                "Deep Inspect workflow_call inputs");
        RequireScalarValue(lane, "required", "true", "Deep Inspect lane input");
        RequireScalarValue(lane, "type", "string", "Deep Inspect lane input");

        YamlSequenceNode schedules =
            GetRequiredSequence(trigger, "schedule", "Deep Inspect trigger");
        string[] crons = schedules.Children
            .Select(node => GetRequiredScalar(
                RequireMapping(node, "Deep Inspect schedule"),
                "cron",
                "Deep Inspect schedule"))
            .ToArray();
        if (crons.Contains("0 6 * * *", StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Deep Inspect must not independently schedule release certification.");

        YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "Deep Inspect workflow");
        string[] requiredJobs =
        [
            "test",
            "platform-test",
            "decompiler-corpus",
            "release-certification",
            "inspect-web",
            "census",
        ];
        foreach (string jobName in requiredJobs)
        {
            string condition = GetRequiredScalar(
                GetRequiredMapping(jobs, jobName, "Deep Inspect jobs"),
                "if",
                $"Deep Inspect job {jobName}");
            RequireContains(condition, "inputs.lane == 'release-candidate'");
        }
    }

    private static YamlMappingNode LoadRoot(string workflow, string context)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
            throw new InvalidOperationException($"Expected one {context} document.");
        return RequireMapping(yaml.Documents[0].RootNode, $"{context} root");
    }

    private static YamlMappingNode FindStep(YamlMappingNode job, string name)
    {
        YamlSequenceNode steps = GetRequiredSequence(job, "steps", $"job for {name}");
        foreach (YamlNode node in steps.Children)
        {
            YamlMappingNode step = RequireMapping(node, $"step {name}");
            if (GetOptionalScalar(step, "name") == name)
                return step;
        }

        throw new InvalidOperationException($"Missing step '{name}'.");
    }

    private static void RequireContains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected content '{expected}'.");
    }

    private static void RequireSequenceValues(
        YamlSequenceNode sequence,
        IReadOnlyList<string> expected,
        string context)
    {
        string[] actual = sequence.Children
            .Select(node => RequireScalar(node, context))
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{context} expected [{string.Join(", ", expected)}], " +
                $"found [{string.Join(", ", actual)}].");
        }
    }

    private static void AssertMutationRejected(
        string original,
        string oldValue,
        string newValue,
        Action<string> validate,
        string message)
    {
        string mutated = ReplaceExactlyOnce(original, oldValue, newValue, message);
        try
        {
            validate(mutated);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
