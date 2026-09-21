using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

namespace ILInspector.Metadata;

internal sealed record MetadataOperationPolicy
{
    public MetadataOperationPolicy(long maxMetadataRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxMetadataRows);
        MaxMetadataRows = maxMetadataRows;
    }

    public static MetadataOperationPolicy Unbounded { get; } =
        new(long.MaxValue);

    public long MaxMetadataRows { get; }
}

internal sealed record MetadataOperationCounters(long MetadataRows);

internal enum MetadataOperationFailureKind
{
    MetadataRowsExceeded,
}

internal sealed record MetadataOperationFailure(
    MetadataOperationFailureKind Kind,
    long ImageMetadataRows,
    long MaxMetadataRows,
    MetadataOperationCounters Counters);

internal abstract record MetadataImageAdmissionResult
{
    private protected MetadataImageAdmissionResult()
    {
    }

    internal sealed record Admitted(
        long ImageMetadataRows,
        MetadataOperationCounters Counters)
        : MetadataImageAdmissionResult;

    internal sealed record Rejected(MetadataOperationFailure Failure)
        : MetadataImageAdmissionResult;
}

internal sealed class MetadataOperationContext : IDisposable
{
    readonly ConditionalWeakTable<AssemblyInspectionSession, object>
        _attachments = new();
    readonly MetadataOperationPolicy _policy;
    long _metadataRows;
    bool _disposed;

    public MetadataOperationContext(MetadataOperationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    public MetadataOperationCounters Counters
    {
        get
        {
            EnsureAlive();
            return new MetadataOperationCounters(_metadataRows);
        }
    }

    internal void Attach(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureAlive();
        session.EnsureAliveForDeclarationSession();

        if (_attachments.TryGetValue(session, out _))
        {
            throw new InvalidOperationException(
                "This assembly inspection session is already attached to the metadata operation.");
        }

        _attachments.Add(session, new object());
    }

    internal MetadataImageAdmissionResult AdmitImage(MetadataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        EnsureAlive();

        long imageMetadataRows = 0;
        foreach (TableIndex table in Enum.GetValues<TableIndex>())
            imageMetadataRows += reader.GetTableRowCount(table);

        if (imageMetadataRows > _policy.MaxMetadataRows - _metadataRows)
        {
            return new MetadataImageAdmissionResult.Rejected(
                new MetadataOperationFailure(
                    MetadataOperationFailureKind.MetadataRowsExceeded,
                    imageMetadataRows,
                    _policy.MaxMetadataRows,
                    new MetadataOperationCounters(_metadataRows)));
        }

        _metadataRows += imageMetadataRows;
        return new MetadataImageAdmissionResult.Admitted(
            imageMetadataRows,
            new MetadataOperationCounters(_metadataRows));
    }

    internal void EnsureAlive() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose() => _disposed = true;
}
