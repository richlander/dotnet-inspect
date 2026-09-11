using YamlDotNet.RepresentationModel;
using static CiChangeDetection.YamlContractAssertions;

namespace CiChangeDetection;

internal static partial class WorkflowContract
{
    private static void ValidateAggregateStructuralCheck(
        YamlMappingNode jobs)
    {
        YamlMappingNode aggregate = GetRequiredMapping(
            jobs,
            "ci-required",
            "jobs");
        RequireScalarValue(
            aggregate,
            "if",
            "always()",
            "jobs.ci-required");
        RequireAbsent(
            aggregate,
            "continue-on-error",
            "jobs.ci-required");
        RequireAbsent(
            aggregate,
            "defaults",
            "jobs.ci-required");
        YamlMappingNode aggregateEnvironment = GetRequiredMapping(
            aggregate,
            "env",
            "jobs.ci-required");
        RequireExactKeys(
            aggregateEnvironment,
            ["RESULT_FILTER"],
            "jobs.ci-required.env");
        RequireScalarSha256(
            aggregateEnvironment,
            "RESULT_FILTER",
            "D074F21341F3416A1D7FE48A0374CB69C59F52313ADF61FF732E930CFF0AEF29",
            "jobs.ci-required.env");
        YamlSequenceNode needs = GetRequiredSequence(
            aggregate,
            "needs",
            "jobs.ci-required");
        var actualNeeds = needs.Children
            .Select(node => RequireScalar(
                node,
                "jobs.ci-required need"))
            .ToHashSet(StringComparer.Ordinal);
        var expectedNeeds = jobs.Children.Keys
            .Select(node => RequireScalar(node, "job name"))
            .Where(name => name != "ci-required")
            .ToHashSet(StringComparer.Ordinal);
        if (!actualNeeds.SetEquals(expectedNeeds))
        {
            throw new InvalidOperationException(
                "jobs.ci-required.needs must contain every other job " +
                "exactly once.");
        }
        YamlSequenceNode steps = GetRequiredSequence(
            aggregate,
            "steps",
            "jobs.ci-required");
        if (steps.Children.Count != 4)
        {
            throw new InvalidOperationException(
                "jobs.ci-required must contain checkout and exactly three " +
                "enforcement steps.");
        }
        YamlMappingNode checkout = RequireMapping(
            steps.Children[0],
            "jobs.ci-required checkout step");
        RequireExactKeys(
            checkout,
            ["uses"],
            "jobs.ci-required checkout step");
        RequireScalarValue(
            checkout,
            "uses",
            "actions/checkout@v7",
            "jobs.ci-required checkout step");
        var namedSteps =
            new Dictionary<string, (int Index, YamlMappingNode Step)>(
                StringComparer.Ordinal);
        int stepIndex = 0;
        foreach (YamlNode stepNode in steps.Children)
        {
            YamlMappingNode step = RequireMapping(
                stepNode,
                "jobs.ci-required step");
            string? name = GetOptionalScalar(step, "name");
            if (name is not null &&
                !namedSteps.TryAdd(name, (stepIndex, step)))
            {
                throw new InvalidOperationException(
                    $"jobs.ci-required contains duplicate step name: {name}.");
            }
            stepIndex++;
        }

        string[] requiredStepNames =
        [
            "Verify this gate depends on every other job",
            "Self-test the result filter",
            "Verify no required job failed or was cancelled",
        ];
        foreach (string name in requiredStepNames)
        {
            if (!namedSteps.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"jobs.ci-required is missing step: {name}.");
            }
        }
        if (namedSteps[requiredStepNames[0]].Index != 1 ||
            namedSteps[requiredStepNames[1]].Index != 2 ||
            namedSteps[requiredStepNames[2]].Index != 3)
        {
            throw new InvalidOperationException(
                "jobs.ci-required enforcement steps are out of order.");
        }

        YamlMappingNode check = namedSteps[requiredStepNames[0]].Step;
        RequireExactKeys(
            check,
            ["name", "shell", "env", "run"],
            "ci-required structural check");
        RequireScalarValue(
            check,
            "shell",
            "bash",
            "ci-required structural check");
        RequireExactScalarValues(
            GetRequiredMapping(
                check,
                "env",
                "ci-required structural check"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NEEDS"] = "${{ toJSON(needs) }}",
            },
            "ci-required structural check.env");
        RequireAbsent(
            check,
            "if",
            "ci-required structural check");
        RequireAbsent(
            check,
            "continue-on-error",
            "ci-required structural check");
        RequireScalarSha256(
            check,
            "run",
            "2DE85E60935EDA6C488053ACD405D02C4842F620AA0E0307E6E64166AF1A7DF8",
            "ci-required structural check");

        YamlMappingNode filterSelfTest =
            namedSteps[requiredStepNames[1]].Step;
        RequireExactKeys(
            filterSelfTest,
            ["name", "shell", "run"],
            "ci-required result-filter self-test");
        RequireScalarValue(
            filterSelfTest,
            "shell",
            "bash",
            "ci-required result-filter self-test");
        RequireScalarSha256(
            filterSelfTest,
            "run",
            "7BE0D6B90EB8A915BB17ED8BC6B6DA3371197D69E5F98262D854330985E7E5BD",
            "ci-required result-filter self-test");

        YamlMappingNode resultCheck =
            namedSteps[requiredStepNames[2]].Step;
        RequireExactKeys(
            resultCheck,
            ["name", "shell", "env", "run"],
            "ci-required result check");
        RequireScalarValue(
            resultCheck,
            "shell",
            "bash",
            "ci-required result check");
        RequireExactScalarValues(
            GetRequiredMapping(
                resultCheck,
                "env",
                "ci-required result check"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NEEDS"] = "${{ toJSON(needs) }}",
            },
            "ci-required result check.env");
        RequireScalarSha256(
            resultCheck,
            "run",
            "8A91AD84EA333837F96705184446A0E7815286B01B21FA41C7998D6CCAFAC648",
            "ci-required result check");
    }
}
