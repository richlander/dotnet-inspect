namespace ILInspector.DecompilerHarness;

// The corpus-method snapshot and changed-method delta artifact schema. Kept in
// their own file (separate from CorpusSensor, which emits them) so the targeted
// changed-method fidelity consumer in FidelityCheck can be linked into the
// decompiler test project without dragging in the rest of the sensor pipeline.

internal sealed record CorpusMethodSnapshot(
    string Assembly,
    string AssemblyPath,
    string Type,
    string Method,
    int Overload,
    string Signature,
    string Fidelity,
    bool FullyRaised,
    string? Residual,
    string? PassBug,
    string Validity,
    string FidelityCheck)
{
    public string DisplayMethod => $"{Assembly}!{Type}::{Method}#{Overload}";
}

internal sealed record CorpusMethodDeltaArtifact(
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    DateTimeOffset BaselineGeneratedUtc,
    DateTimeOffset CurrentGeneratedUtc,
    bool BaselineHasMethodDetails,
    bool CurrentHasMethodDetails,
    IReadOnlyList<CorpusMethodDeltaRow> ChangedMethods);

internal sealed record CorpusMethodDeltaRow(
    string Method,
    string Assembly,
    string AssemblyPath,
    string Type,
    string MethodName,
    int Overload,
    string Signature,
    CorpusMethodSnapshot? Baseline,
    CorpusMethodSnapshot? Current,
    IReadOnlyList<string> Deltas);
