using CiChangeDetection.Planning;

namespace CiChangeDetection;

/// <summary>
/// Runs the repository's CI change-detection contract gate.
/// </summary>
public static class ChangeDetectionApp
{
    /// <summary>
    /// Runs the gate or refreshes the EVIL provenance pin.
    /// </summary>
    /// <param name="args">Command-line arguments passed by the file-based entrypoint.</param>
    /// <returns>Zero when the requested operation succeeds.</returns>
    public static int Run(string[] args)
    {
        string repository = Environment.CurrentDirectory;
        string workflowPath = Path.Combine(
            repository,
            ".github",
            "workflows",
            "ci.yml");
        string workflowText = File.ReadAllText(workflowPath);

        if (args is ["--refresh-evil-provenance-pin"])
        {
            WorkflowContractResult contract = LoadContract(
                repository,
                workflowText,
                validateProvenancePin: false);
            string refreshed = ProvenancePin.Refresh(workflowText, contract);
            if (refreshed == workflowText)
            {
                Console.WriteLine("EVIL provenance pin is already current.");
            }
            else
            {
                File.WriteAllText(workflowPath, refreshed);
                Console.WriteLine(
                    "Refreshed the EVIL provenance run SHA-256 in .github/workflows/ci.yml.");
            }
            return 0;
        }

        if (args.Length != 0)
        {
            throw new InvalidOperationException(
                "Usage: dotnet run eng/test-ci-change-detection.cs [-- --refresh-evil-provenance-pin]");
        }

        InspectWebProjectGraphPolicy.Validate(repository);
        WorkflowContractResult result = LoadContract(
            repository,
            workflowText,
            validateProvenancePin: true);
        PromotionWorkflowContract.AssertMutations(repository);
        ProvenancePin.AssertMutations(
            workflowText,
            mutated => _ = LoadContract(
                repository,
                mutated,
                validateProvenancePin: true),
            mutated =>
            {
                WorkflowContractResult mutatedContract = LoadContract(
                    repository,
                    mutated,
                    validateProvenancePin: false);
                return ProvenancePin.Refresh(mutated, mutatedContract);
            });
        DetectionTestSuite.Run(repository);
        ChangePlanTestSuite.Run(repository);

        Console.WriteLine(
            "CI aggregate fail-safe, legacy classifier parity, path canaries, "
            + "provenance pin mutations, and change-planner construction "
            + "passed.");
        return 0;
    }

    private static WorkflowContractResult LoadContract(
        string repository,
        string workflowText,
        bool validateProvenancePin) =>
        WorkflowContract.Load(
            repository,
            workflowText,
            validateProvenancePin);
}
