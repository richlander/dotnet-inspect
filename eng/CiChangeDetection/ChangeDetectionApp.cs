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

        if (args is ["--refresh-decompiler-skip-projects"])
        {
            bool changed = DecompilerSkipProjectsGenerator.Generate(repository);
            Console.WriteLine(changed
                ? "Refreshed eng/decompiler-gate-skip-projects.txt from the "
                    + "evaluated Release decompiler project closure."
                : "eng/decompiler-gate-skip-projects.txt is already current.");
            return 0;
        }

        if (args.Length != 0)
        {
            throw new InvalidOperationException(
                "Usage: dotnet run eng/test-ci-change-detection.cs "
                + "[-- --refresh-evil-provenance-pin | "
                + "--refresh-decompiler-skip-projects]");
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
        ChangePlanTestSuite.Run(repository);

        Console.WriteLine(
            "CI aggregate fail-safe, path canaries, provenance pin mutations, "
            + "change-planner construction, and workflow scope transport "
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
