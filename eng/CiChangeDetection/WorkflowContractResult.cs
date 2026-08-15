using System.Collections.ObjectModel;

namespace CiChangeDetection;

internal readonly record struct WorkflowContractResult(
    string DetectionBody,
    ReadOnlyCollection<string> Outputs,
    string ProvenanceRunSha256,
    string ProvenancePin);
