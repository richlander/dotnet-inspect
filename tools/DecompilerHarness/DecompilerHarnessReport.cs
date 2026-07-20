namespace ILInspector.DecompilerHarness;

/// <summary>
/// Non-generic surface for heterogeneous collections of harness reports.
/// Test-kind conclusions remain on the typed payload; this contract describes
/// only whether the harness run itself produced usable evidence.
/// </summary>
internal interface IDecompilerHarnessReport
{
    HarnessReportDescriptor Descriptor { get; }
    HarnessRunDisposition Disposition { get; }
    IReadOnlyList<HarnessBlocker> Blockers { get; }
    IReadOnlyList<HarnessArtifact> Artifacts { get; }
}

internal sealed record HarnessReportDescriptor
{
    public HarnessReportDescriptor(string id, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        Id = id;
        SchemaVersion = schemaVersion;
    }

    public string Id { get; }
    public int SchemaVersion { get; }
}

/// <summary>
/// Execution disposition, not the domain verdict. A completed report may
/// legitimately contain regressions, failed fixtures, or fidelity differences.
/// </summary>
internal enum HarnessRunDisposition
{
    Completed,
    Partial,
    Blocked,
    Failed,
}

internal sealed record HarnessBlocker(string Code, string Detail);

internal sealed record HarnessArtifact(string Kind, string Path);

/// <summary>
/// Domain-neutral harness evidence envelope. The payload retains the native
/// vocabulary of its test kind (census buckets, RTS statuses, validity defects,
/// fidelity outcomes, and so on).
/// </summary>
internal sealed record DecompilerHarnessReport<T> : IDecompilerHarnessReport
    where T : notnull
{
    public DecompilerHarnessReport(
        HarnessReportDescriptor descriptor,
        T payload,
        HarnessRunDisposition disposition = HarnessRunDisposition.Completed,
        IReadOnlyList<HarnessBlocker>? blockers = null,
        IReadOnlyList<HarnessArtifact>? artifacts = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Disposition = disposition;
        Blockers = blockers ?? [];
        Artifacts = artifacts ?? [];

        if (disposition == HarnessRunDisposition.Completed && Blockers.Count != 0)
            throw new ArgumentException("A completed harness report cannot carry blockers.", nameof(blockers));
    }

    public HarnessReportDescriptor Descriptor { get; }
    public T Payload { get; }
    public HarnessRunDisposition Disposition { get; }
    public IReadOnlyList<HarnessBlocker> Blockers { get; }
    public IReadOnlyList<HarnessArtifact> Artifacts { get; }
}
