using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class MainCiWorkflowContract
{
    internal static void Validate(string repository)
    {
        string path = Path.Combine(
            repository,
            ".github",
            "workflows",
            "main-ci.yml");
        using TextReader reader = File.OpenText(path);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one Main CI workflow document, found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "Main CI workflow root");
        RequireExactKeys(
            root,
            ["name", "on", "permissions", "jobs"],
            "Main CI workflow");
        RequireScalarValue(root, "name", "Main CI", "Main CI workflow");

        ValidateTrigger(GetRequiredMapping(root, "on", "Main CI workflow"));
        RequireExactScalarValues(
            GetRequiredMapping(root, "permissions", "Main CI workflow"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contents"] = "read",
            },
            "Main CI workflow.permissions");
        ValidateJobs(GetRequiredMapping(root, "jobs", "Main CI workflow"));
    }

    private static void ValidateTrigger(YamlMappingNode trigger)
    {
        RequireExactKeys(trigger, ["push"], "Main CI workflow.on");
        YamlMappingNode push =
            GetRequiredMapping(trigger, "push", "Main CI workflow.on");
        RequireExactKeys(push, ["branches"], "Main CI workflow.on.push");
        YamlSequenceNode branches =
            GetRequiredSequence(push, "branches", "Main CI workflow.on.push");
        if (branches.Children.Count != 1 ||
            RequireScalar(
                branches.Children[0],
                "Main CI workflow.on.push.branches entry") != "main")
        {
            throw new InvalidOperationException(
                "Main CI workflow.on.push.branches must contain only main.");
        }
    }

    private static void ValidateJobs(YamlMappingNode jobs)
    {
        RequireExactKeys(jobs, ["ci"], "Main CI workflow.jobs");
        YamlMappingNode ci =
            GetRequiredMapping(jobs, "ci", "Main CI workflow.jobs");
        RequireExactKeys(
            ci,
            ["permissions", "uses", "with"],
            "Main CI workflow.jobs.ci");
        RequireExactScalarValues(
            GetRequiredMapping(
                ci,
                "permissions",
                "Main CI workflow.jobs.ci"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contents"] = "read",
                ["packages"] = "read",
            },
            "Main CI workflow.jobs.ci.permissions");
        RequireScalarValue(
            ci,
            "uses",
            "./.github/workflows/ci.yml",
            "Main CI workflow.jobs.ci");
        RequireExactScalarValues(
            GetRequiredMapping(ci, "with", "Main CI workflow.jobs.ci"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["event-name"] = "push",
                ["base-sha"] = "${{ github.event.before }}",
            },
            "Main CI workflow.jobs.ci.with");
    }
}
