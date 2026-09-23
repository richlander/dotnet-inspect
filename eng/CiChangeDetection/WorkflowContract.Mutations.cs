using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    internal static void AssertWorkingDirectoryMutations(
        string repository,
        string workflowText)
    {
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
            root => AddSkillGateRunDefault(root, "."),
            "static inherited job repository-root default");
        AssertRejected(
            repository,
            workflowText,
            root => AddSkillGateRunDefault(root, "inspect-web"),
            "skill-gate/Run embedded skill tests",
            "inherited non-root job default");
        AssertRejected(
            repository,
            workflowText,
            root => AddSkillGateRunDefault(
                root,
                "${{ github.workspace }}"),
            "skill-gate/Run embedded skill tests",
            "inherited job default expression");

        AssertAccepted(
            repository,
            workflowText,
            root => AddSkillGateStepWorkingDirectory(
                root,
                "${{ github.workspace }}"),
            "step workspace override");
        AssertRejected(
            repository,
            workflowText,
            root => AddSkillGateStepWorkingDirectory(root, "tests"),
            "skill-gate/Run embedded skill tests",
            "non-root step override");

        AssertAccepted(
            repository,
            workflowText,
            root =>
            {
                AddSkillGateRunDefault(root, "inspect-web");
                AddSkillGateStepWorkingDirectory(root, ".");
            },
            "fully shadowed non-root job default");
        AssertAccepted(
            repository,
            workflowText,
            root => AddJobRunDefault(
                root,
                "markdownlint",
                "inspect-web"),
            "action-only job default");
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

    private static void AddSkillGateRunDefault(
        YamlMappingNode root,
        string workingDirectory) =>
        AddJobRunDefault(
            root,
            "skill-gate",
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

    private static void AddSkillGateStepWorkingDirectory(
        YamlMappingNode root,
        string workingDirectory)
    {
        YamlMappingNode jobs = GetRequiredMapping(
            root,
            "jobs",
            "workflow");
        YamlMappingNode skillGate = GetRequiredMapping(
            jobs,
            "skill-gate",
            "jobs");
        YamlSequenceNode steps = GetRequiredSequence(
            skillGate,
            "steps",
            "jobs.skill-gate");
        YamlMappingNode step = steps.Children
            .Select(node => RequireMapping(
                node,
                "jobs.skill-gate step"))
            .Single(candidate =>
                GetOptionalScalar(candidate, "name")
                == "Run embedded skill tests");
        AddNode(
            step,
            "working-directory",
            new YamlScalarNode(workingDirectory),
            "jobs.skill-gate Run embedded skill tests");
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
