using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static class DeepInspectOracleWorkflowContract
{
    private const string WorkflowPath = ".github/workflows/deep-inspect.yml";
    private const string JobName = "authored-corpus-ratchet";
    private const string RestoreStep = "Restore the vendored authored-source corpus";
    private const string PrepareOracleStep = "Prepare the source-oracle assembly pool";
    private const string RunOracleStep = "Run the whole-file source-oracle gate";
    private const string PrepareEvilStep = "Prepare the EVIL assembly pool";
    private const string RunEvilStep = "Run the authored-corpus benchmark (regression ratchet)";
    private const string UploadStep = "Upload the run JSON";
    private const string WeeklyCron = "0 9 * * 1";
    private const string JobCondition =
        "(github.event_name == 'schedule' && " +
        "github.event.schedule == '0 9 * * 1') || " +
        "inputs.lane == 'authored-corpus' || " +
        "inputs.lane == 'all'";

    private const string JobConditionYaml = """
            if: >-
              (github.event_name == 'schedule' && github.event.schedule == '0 9 * * 1') ||
              inputs.lane == 'authored-corpus' ||
              inputs.lane == 'all'
        """;

    private const string OracleRun = """
        set -euo pipefail
        mkdir -p artifacts/deep-inspect
        mapfile -t oracle_assemblies < "$RUNNER_TEMP/source-oracle-assemblies.txt"
        dotnet run --project tools/DecompilerHarness -c Release -- \
          --benchmark-authored-corpus external/authored-source-corpus/oracle/corpus.jsonl \
          --source-oracle-manifest external/authored-source-corpus/oracle/manifest.json \
          --json "${oracle_assemblies[@]}" \
          > artifacts/deep-inspect/source-oracle-run.json
        """;

    internal static void Validate(string repository)
    {
        string workflow = File.ReadAllText(
            Path.Combine(repository, WorkflowPath));
        ValidateWorkflow(workflow);
    }

    internal static void AssertMutations(string repository)
    {
        string workflow = File.ReadAllText(
            Path.Combine(repository, WorkflowPath));

        AssertMutationRejected(
            workflow,
            $"      - name: {RunOracleStep}\n",
            $"      - name: {RunOracleStep}\n        if: ${{{{ false }}}}\n",
            "Deep Inspect source-oracle contract accepted a disabled gate.");
        AssertMutationRejected(
            workflow,
            $"      - name: {RunOracleStep}\n",
            $"      - name: {RunOracleStep}\n        continue-on-error: true\n",
            "Deep Inspect source-oracle contract accepted a non-blocking gate.");
        AssertMutationRejected(
            workflow,
            "            artifacts/deep-inspect/source-oracle-run.json\n",
            "",
            "Deep Inspect source-oracle contract accepted an unwired artifact.");
        AssertMutationRejected(
            workflow,
            JobConditionYaml,
            "    if: ${{ false }}",
            "Deep Inspect source-oracle contract accepted a disabled job.");
        AssertMutationRejected(
            workflow,
            JobConditionYaml,
            JobConditionYaml + "\n    needs: nightly",
            "Deep Inspect source-oracle contract accepted a dependent job.");
        AssertMutationRejected(
            workflow,
            $"    - cron: '{WeeklyCron}'\n",
            "",
            "Deep Inspect source-oracle contract accepted a missing weekly trigger.");
        AssertMutationRejected(
            workflow,
            "          - authored-corpus\n",
            "",
            "Deep Inspect source-oracle contract accepted a missing manual lane.");
    }

    private static void ValidateWorkflow(string workflow)
    {
        using TextReader reader = new StringReader(workflow);
        YamlStream yaml = [];
        yaml.Load(reader);
        if (yaml.Documents.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one Deep Inspect workflow document, " +
                $"found {yaml.Documents.Count}.");
        }

        YamlMappingNode root = RequireMapping(
            yaml.Documents[0].RootNode,
            "Deep Inspect workflow root");
        ValidateTriggers(root);
        YamlMappingNode jobs = GetRequiredMapping(
            root,
            "jobs",
            "Deep Inspect workflow");
        YamlMappingNode job = GetRequiredMapping(
            jobs,
            JobName,
            "Deep Inspect jobs");
        RequireScalarValue(
            job,
            "if",
            JobCondition,
            $"jobs.{JobName}");
        RequireAbsent(job, "needs", $"jobs.{JobName}");
        RequireAbsent(job, "continue-on-error", $"jobs.{JobName}");
        RequireAbsent(job, "defaults", $"jobs.{JobName}");

        YamlSequenceNode steps = GetRequiredSequence(
            job,
            "steps",
            $"jobs.{JobName}");
        var namedSteps = new Dictionary<string, (int Index, YamlMappingNode Step)>(
            StringComparer.Ordinal);
        for (int index = 0; index < steps.Children.Count; index++)
        {
            YamlMappingNode step = RequireMapping(
                steps.Children[index],
                $"jobs.{JobName} step");
            string? name = GetOptionalScalar(step, "name");
            if (name is not null &&
                !namedSteps.TryAdd(name, (index, step)))
            {
                throw new InvalidOperationException(
                    $"jobs.{JobName} contains duplicate step: {name}.");
            }
        }

        (int restoreIndex, YamlMappingNode restore) =
            GetRequiredStep(namedSteps, RestoreStep);
        (int prepareOracleIndex, YamlMappingNode prepareOracle) =
            GetRequiredStep(namedSteps, PrepareOracleStep);
        (int runOracleIndex, YamlMappingNode runOracle) =
            GetRequiredStep(namedSteps, RunOracleStep);
        (int prepareEvilIndex, _) =
            GetRequiredStep(namedSteps, PrepareEvilStep);
        (int runEvilIndex, _) =
            GetRequiredStep(namedSteps, RunEvilStep);
        (int uploadIndex, YamlMappingNode upload) =
            GetRequiredStep(namedSteps, UploadStep);

        if (!(restoreIndex < prepareOracleIndex &&
            prepareOracleIndex < runOracleIndex &&
            runOracleIndex < prepareEvilIndex &&
            prepareEvilIndex < runEvilIndex &&
            runEvilIndex < uploadIndex))
        {
            throw new InvalidOperationException(
                "Deep Inspect source-oracle and EVIL steps are out of order.");
        }

        ValidateBlockingRunStep(
            restore,
            "bash eng/restore-authored-source-corpus.sh",
            RestoreStep);
        ValidateBlockingRunStep(
            prepareOracle,
            "bash eng/prepare-authored-source-oracles.sh " +
            "\"$RUNNER_TEMP/source-oracle-assemblies.txt\"",
            PrepareOracleStep);
        ValidateBlockingRunStep(
            runOracle,
            OracleRun + "\n",
            RunOracleStep,
            expectedShell: "bash");

        RequireScalarValue(
            upload,
            "if",
            "always()",
            $"jobs.{JobName} {UploadStep}");
        RequireScalarValue(
            upload,
            "uses",
            "actions/upload-artifact@v4",
            $"jobs.{JobName} {UploadStep}");
        RequireAbsent(
            upload,
            "continue-on-error",
            $"jobs.{JobName} {UploadStep}");
        YamlMappingNode uploadWith = GetRequiredMapping(
            upload,
            "with",
            $"jobs.{JobName} {UploadStep}");
        RequireScalarValue(
            uploadWith,
            "name",
            "deep-inspect-authored-corpus",
            $"jobs.{JobName} {UploadStep}.with");
        RequireScalarValue(
            uploadWith,
            "path",
            """
            artifacts/deep-inspect/authored-corpus-run.json
            artifacts/deep-inspect/source-oracle-run.json

            """,
            $"jobs.{JobName} {UploadStep}.with");
        RequireScalarValue(
            uploadWith,
            "if-no-files-found",
            "warn",
            $"jobs.{JobName} {UploadStep}.with");
    }

    private static void ValidateTriggers(YamlMappingNode root)
    {
        YamlMappingNode triggers = GetRequiredMapping(
            root,
            "on",
            "Deep Inspect workflow");
        YamlSequenceNode schedules = GetRequiredSequence(
            triggers,
            "schedule",
            "Deep Inspect workflow.on");
        int weeklySchedules = schedules.Children.Count(node =>
        {
            YamlMappingNode schedule = RequireMapping(
                node,
                "Deep Inspect workflow.on.schedule entry");
            return GetRequiredScalar(
                schedule,
                "cron",
                "Deep Inspect workflow.on.schedule entry") == WeeklyCron;
        });
        if (weeklySchedules != 1)
        {
            throw new InvalidOperationException(
                $"Deep Inspect workflow must declare the {WeeklyCron} " +
                $"schedule exactly once.");
        }

        YamlMappingNode dispatch = GetRequiredMapping(
            triggers,
            "workflow_dispatch",
            "Deep Inspect workflow.on");
        YamlMappingNode inputs = GetRequiredMapping(
            dispatch,
            "inputs",
            "Deep Inspect workflow.on.workflow_dispatch");
        YamlMappingNode lane = GetRequiredMapping(
            inputs,
            "lane",
            "Deep Inspect workflow.on.workflow_dispatch.inputs");
        YamlSequenceNode options = GetRequiredSequence(
            lane,
            "options",
            "Deep Inspect workflow.on.workflow_dispatch.inputs.lane");
        var optionValues = options.Children
            .Select(node => RequireScalar(
                node,
                "Deep Inspect workflow lane option"))
            .ToList();
        foreach (string required in new[] { "authored-corpus", "all" })
        {
            if (optionValues.Count(option => option == required) != 1)
            {
                throw new InvalidOperationException(
                    $"Deep Inspect workflow must declare the {required} " +
                    $"manual lane exactly once.");
            }
        }
    }

    private static (int Index, YamlMappingNode Step) GetRequiredStep(
        IReadOnlyDictionary<string, (int Index, YamlMappingNode Step)> steps,
        string name) =>
        steps.TryGetValue(name, out var step)
            ? step
            : throw new InvalidOperationException(
                $"jobs.{JobName} is missing step: {name}.");

    private static void ValidateBlockingRunStep(
        YamlMappingNode step,
        string expectedRun,
        string name,
        string? expectedShell = null)
    {
        string context = $"jobs.{JobName} {name}";
        RequireScalarValue(step, "run", expectedRun, context);
        RequireAbsent(step, "if", context);
        RequireAbsent(step, "continue-on-error", context);
        if (expectedShell is null)
        {
            RequireAbsent(step, "shell", context);
        }
        else
        {
            RequireScalarValue(step, "shell", expectedShell, context);
        }
    }

    private static void AssertMutationRejected(
        string workflow,
        string oldValue,
        string newValue,
        string message)
    {
        string mutated = ReplaceExactlyOnce(
            workflow,
            oldValue,
            newValue,
            message);
        using (TextReader reader = new StringReader(mutated))
        {
            YamlStream syntax = [];
            syntax.Load(reader);
        }

        try
        {
            ValidateWorkflow(mutated);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
