using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    internal static void AssertWorkingDirectoryMutations(
        string repository,
        string workflowText)
    {
        AssertRejected(
            repository,
            workflowText,
            root =>
            {
                YamlMappingNode jobs = GetRequiredMapping(root, "jobs", "workflow");
                AddNode(jobs, "extra-pr-job-a", new YamlMappingNode(), "jobs");
                AddNode(jobs, "extra-pr-job-b", new YamlMappingNode(), "jobs");
            },
            "16-runner-job budget",
            "additional PR job exceeds runner budget");

        AssertAccepted(
            repository,
            workflowText,
            root => AddWorkflowRunDefault(root, "."),
            "static workflow repository-root default");
        AssertRejected(
            repository,
            workflowText,
            root => AddWorkflowRunDefault(root, "tests"),
            "workflow.defaults.run.working-directory",
            "non-root workflow default");
        AssertRejected(
            repository,
            workflowText,
            root => AddWorkflowRunDefault(
                root,
                "${{ github.workspace }}"),
            "workflow.defaults.run.working-directory",
            "workflow default expression");

        AssertAccepted(
            repository,
            workflowText,
            root => AddTestRunDefault(root, "."),
            "static inherited job repository-root default");
        AssertRejected(
            repository,
            workflowText,
            root => AddTestRunDefault(root, "inspect-web"),
            "test/Build",
            "inherited non-root job default");
        AssertRejected(
            repository,
            workflowText,
            root => AddTestRunDefault(
                root,
                "${{ github.workspace }}"),
            "test/Build",
            "inherited job default expression");

        AssertAccepted(
            repository,
            workflowText,
            root => AddTestStepWorkingDirectory(
                root,
                "${{ github.workspace }}"),
            "step workspace override");
        AssertRejected(
            repository,
            workflowText,
            root => AddTestStepWorkingDirectory(root, "tests"),
            "test/Run NetworkAccess tests",
            "non-root step override");

    }

    private static void AssertAccepted(
        string repository,
        string workflowText,
        Action<YamlMappingNode> mutation,
        string scenario)
    {
        try
        {
            _ = Load(
                repository,
                Mutate(workflowText, mutation),
                validateProvenancePin: true,
                validateProjectGraph: false);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Working-directory mutation should accept {scenario}.",
                exception);
        }
    }

    private static void AssertRejected(
        string repository,
        string workflowText,
        Action<YamlMappingNode> mutation,
        string expectedMessage,
        string scenario)
    {
        try
        {
            _ = Load(
                repository,
                Mutate(workflowText, mutation),
                validateProvenancePin: true,
                validateProjectGraph: false);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(
                expectedMessage,
                StringComparison.Ordinal))
        {
            return;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Working-directory mutation rejected {scenario} " +
                "at the wrong boundary.",
                exception);
        }

        throw new InvalidOperationException(
            $"Working-directory mutation unexpectedly accepted {scenario}.");
    }

    private static string Mutate(
        string workflowText,
        Action<YamlMappingNode> mutation)
    {
        using TextReader reader = new StringReader(workflowText);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected one workflow document for mutation.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "workflow root");
        mutation(root);

        using var writer = new StringWriter();
        yaml.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static void AddWorkflowRunDefault(
        YamlMappingNode root,
        string workingDirectory) =>
        AddNode(
            root,
            "defaults",
            RunDefaults(workingDirectory),
            "workflow");

    private static void AddTestRunDefault(
        YamlMappingNode root,
        string workingDirectory) =>
        AddJobRunDefault(
            root,
            "test",
            workingDirectory);

    private static void AddJobRunDefault(
        YamlMappingNode root,
        string jobName,
        string workingDirectory)
    {
        YamlMappingNode jobs = GetRequiredMapping(
            root,
            "jobs",
            "workflow");
        YamlMappingNode job = GetRequiredMapping(
            jobs,
            jobName,
            "jobs");
        AddNode(
            job,
            "defaults",
            RunDefaults(workingDirectory),
            $"jobs.{jobName}");
    }

    private static void AddTestStepWorkingDirectory(
        YamlMappingNode root,
        string workingDirectory)
    {
        YamlMappingNode jobs = GetRequiredMapping(
            root,
            "jobs",
            "workflow");
        YamlMappingNode test = GetRequiredMapping(
            jobs,
            "test",
            "jobs");
        YamlSequenceNode steps = GetRequiredSequence(
            test,
            "steps",
            "jobs.test");
        YamlMappingNode step = steps.Children
            .Select(node => RequireMapping(
                node,
                "jobs.test step"))
            .Single(candidate =>
                GetOptionalScalar(candidate, "name")
                == "Run NetworkAccess tests");
        AddNode(
            step,
            "working-directory",
            new YamlScalarNode(workingDirectory),
            "jobs.test Run NetworkAccess tests");
    }

    private static YamlMappingNode RunDefaults(
        string workingDirectory)
    {
        YamlMappingNode run = [];
        run.Children.Add(
            new YamlScalarNode("working-directory"),
            new YamlScalarNode(workingDirectory));
        YamlMappingNode defaults = [];
        defaults.Children.Add(
            new YamlScalarNode("run"),
            run);
        return defaults;
    }

    private static void AddNode(
        YamlMappingNode mapping,
        string key,
        YamlNode value,
        string context)
    {
        if (TryGetNode(mapping, key, out _))
        {
            throw new InvalidOperationException(
                $"{context} already declares {key}.");
        }

        mapping.Children.Add(
            new YamlScalarNode(key),
            value);
    }
}
