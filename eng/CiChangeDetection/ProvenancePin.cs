namespace CiChangeDetection;

internal static class ProvenancePin
{
    internal static void AssertCurrent(
        string provenanceRunSha256,
        string provenancePin)
    {
        if (provenanceRunSha256 == provenancePin)
        {
            return;
        }

        throw new InvalidOperationException(
            $"jobs.changes EVIL_PROVENANCE_RUN_SHA256 is stale: step.run hashes to " +
            $"{provenanceRunSha256}, but the pin is {provenancePin}. " +
            "Refresh it with 'dotnet run eng/test-ci-change-detection.cs -- " +
            "--refresh-evil-provenance-pin'.");
    }

    internal static string Refresh(
        string workflowText,
        WorkflowContractResult contract) =>
        contract.ProvenanceRunSha256 == contract.ProvenancePin
            ? workflowText
            : YamlContractAssertions.ReplaceExactlyOnce(
                workflowText,
                contract.ProvenancePin,
                contract.ProvenanceRunSha256,
                "EVIL provenance SHA-256 pin");

    internal static void AssertMutations(
        string workflowText,
        Action<string> validateWorkflow,
        Func<string, string> refreshWorkflow)
    {
        const string command = "--verify-authored-corpus-history";
        string stale = YamlContractAssertions.ReplaceExactlyOnce(
            workflowText,
            command,
            command + " --history-path /tmp/ci-pin-mutation.jsonl",
            "EVIL provenance command");
        GateAssertions.AssertInvalidOperation(
            () => validateWorkflow(stale),
            "jobs.changes EVIL_PROVENANCE_RUN_SHA256 is stale");

        string refreshed = refreshWorkflow(stale);
        validateWorkflow(refreshed);
    }
}
