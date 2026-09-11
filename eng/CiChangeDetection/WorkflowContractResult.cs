namespace CiChangeDetection;

internal readonly record struct WorkflowContractResult(
    string ProvenanceRunSha256,
    string ProvenancePin);
