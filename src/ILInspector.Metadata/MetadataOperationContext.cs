using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

namespace ILInspector.Metadata;

public sealed record MetadataOperationPolicy
{
    public MetadataOperationPolicy(
        long maxMetadataRows,
        long maxMethodImplementationRows = long.MaxValue,
        long maxDeclarationCandidates = long.MaxValue,
        long maxRelationshipEdges = long.MaxValue,
        long maxSignatureBytes = long.MaxValue,
        long maxGenericSubstitutionNodes = long.MaxValue,
        long maxStructuredNodes = long.MaxValue,
        long maxRetainedText = long.MaxValue,
        long maxInterfaceImplementationRows = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxMetadataRows);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxMethodImplementationRows);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxDeclarationCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRelationshipEdges);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSignatureBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxGenericSubstitutionNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStructuredNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedText);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxInterfaceImplementationRows);

        MaxMetadataRows = maxMetadataRows;
        MaxMethodImplementationRows = maxMethodImplementationRows;
        MaxDeclarationCandidates = maxDeclarationCandidates;
        MaxRelationshipEdges = maxRelationshipEdges;
        MaxSignatureBytes = maxSignatureBytes;
        MaxGenericSubstitutionNodes = maxGenericSubstitutionNodes;
        MaxStructuredNodes = maxStructuredNodes;
        MaxRetainedText = maxRetainedText;
        MaxInterfaceImplementationRows = maxInterfaceImplementationRows;
    }

    public static MetadataOperationPolicy Unbounded { get; } =
        new(long.MaxValue);

    public long MaxMetadataRows { get; }
    public long MaxMethodImplementationRows { get; }
    public long MaxDeclarationCandidates { get; }
    public long MaxRelationshipEdges { get; }
    public long MaxSignatureBytes { get; }
    public long MaxGenericSubstitutionNodes { get; }
    public long MaxStructuredNodes { get; }
    public long MaxRetainedText { get; }
    public long MaxInterfaceImplementationRows { get; }
}

public sealed record MetadataOperationCounters(
    long MetadataRows,
    long MethodImplementationRows = 0,
    long DeclarationCandidates = 0,
    long RelationshipEdges = 0,
    long SignatureBytes = 0,
    long GenericSubstitutionNodes = 0,
    long StructuredNodes = 0,
    long RetainedText = 0,
    long InterfaceImplementationRows = 0);

public enum MetadataOperationDimension
{
    MetadataRows,
    MethodImplementationRows,
    DeclarationCandidates,
    RelationshipEdges,
    SignatureBytes,
    GenericSubstitutionNodes,
    StructuredNodes,
    RetainedText,
    InterfaceImplementationRows,
}

public enum MetadataOperationFailureKind
{
    MetadataRowsExceeded,
}

public sealed record MetadataOperationFailure(
    MetadataOperationFailureKind Kind,
    long ImageMetadataRows,
    long MaxMetadataRows,
    MetadataOperationCounters Counters);

public abstract record MetadataImageAdmissionResult
{
    private protected MetadataImageAdmissionResult()
    {
    }

    public sealed record Admitted(
        long ImageMetadataRows,
        MetadataOperationCounters Counters)
        : MetadataImageAdmissionResult;

    public sealed record Rejected(MetadataOperationFailure Failure)
        : MetadataImageAdmissionResult;
}

public sealed class MetadataOperationContext : IDisposable
{
    readonly ConditionalWeakTable<AssemblyInspectionSession, object>
        _attachments = new();
    readonly MetadataOperationPolicy _policy;
    readonly Action<MetadataOperationWorkKind>? _workObserver;
    long _metadataRows;
    long _methodImplementationRows;
    long _declarationCandidates;
    long _relationshipEdges;
    long _signatureBytes;
    long _genericSubstitutionNodes;
    long _structuredNodes;
    long _retainedText;
    long _interfaceImplementationRows;
    bool _disposed;

    public MetadataOperationContext(MetadataOperationPolicy policy)
        : this(policy, workObserver: null)
    {
    }

    internal MetadataOperationContext(
        MetadataOperationPolicy policy,
        Action<MetadataOperationWorkKind>? workObserver)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        _workObserver = workObserver;
    }

    public MetadataOperationCounters Counters
    {
        get
        {
            EnsureAlive();
            return SnapshotCounters();
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
                    SnapshotCounters()));
        }

        _metadataRows += imageMetadataRows;
        return new MetadataImageAdmissionResult.Admitted(
            imageMetadataRows,
            SnapshotCounters());
    }

    internal void Charge(
        MetadataOperationDimension dimension,
        long amount = 1)
    {
        EnsureAlive();
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (amount == 0)
            return;

        ref long consumed = ref Counter(dimension);
        long limit = Limit(dimension);
        EnsureCanCharge(dimension, amount, consumed, limit);

        consumed += amount;
    }

    internal void EnsureCanCharge(
        MetadataOperationDimension dimension,
        long amount)
    {
        EnsureAlive();
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (amount == 0)
            return;

        EnsureCanCharge(
            dimension,
            amount,
            Counter(dimension),
            Limit(dimension));
    }

    void EnsureCanCharge(
        MetadataOperationDimension dimension,
        long amount,
        long consumed,
        long limit)
    {
        if (amount <= limit - consumed)
            return;

        throw new MetadataOperationBudgetExceededException(
            dimension,
            limit,
            amount,
            SnapshotCounters());
    }

    ref long Counter(MetadataOperationDimension dimension)
    {
        switch (dimension)
        {
            case MetadataOperationDimension.MetadataRows:
                return ref _metadataRows;
            case MetadataOperationDimension.MethodImplementationRows:
                return ref _methodImplementationRows;
            case MetadataOperationDimension.DeclarationCandidates:
                return ref _declarationCandidates;
            case MetadataOperationDimension.RelationshipEdges:
                return ref _relationshipEdges;
            case MetadataOperationDimension.SignatureBytes:
                return ref _signatureBytes;
            case MetadataOperationDimension.GenericSubstitutionNodes:
                return ref _genericSubstitutionNodes;
            case MetadataOperationDimension.StructuredNodes:
                return ref _structuredNodes;
            case MetadataOperationDimension.RetainedText:
                return ref _retainedText;
            case MetadataOperationDimension.InterfaceImplementationRows:
                return ref _interfaceImplementationRows;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(dimension),
                    dimension,
                    "Unknown metadata operation dimension.");
        }
    }

    long Limit(MetadataOperationDimension dimension) =>
        dimension switch
        {
            MetadataOperationDimension.MetadataRows =>
                _policy.MaxMetadataRows,
            MetadataOperationDimension.MethodImplementationRows =>
                _policy.MaxMethodImplementationRows,
            MetadataOperationDimension.DeclarationCandidates =>
                _policy.MaxDeclarationCandidates,
            MetadataOperationDimension.RelationshipEdges =>
                _policy.MaxRelationshipEdges,
            MetadataOperationDimension.SignatureBytes =>
                _policy.MaxSignatureBytes,
            MetadataOperationDimension.GenericSubstitutionNodes =>
                _policy.MaxGenericSubstitutionNodes,
            MetadataOperationDimension.StructuredNodes =>
                _policy.MaxStructuredNodes,
            MetadataOperationDimension.RetainedText =>
                _policy.MaxRetainedText,
            MetadataOperationDimension.InterfaceImplementationRows =>
                _policy.MaxInterfaceImplementationRows,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dimension),
                dimension,
                "Unknown metadata operation dimension."),
        };

    MetadataOperationCounters SnapshotCounters() =>
        new(
            _metadataRows,
            _methodImplementationRows,
            _declarationCandidates,
            _relationshipEdges,
            _signatureBytes,
            _genericSubstitutionNodes,
            _structuredNodes,
            _retainedText,
            _interfaceImplementationRows);

    internal void EnsureAlive() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    internal void ObserveWork(MetadataOperationWorkKind kind)
    {
        EnsureAlive();
        _workObserver?.Invoke(kind);
    }

    public void Dispose() => _disposed = true;
}

internal enum MetadataOperationWorkKind
{
    TypeNodeMaterialization,
    TypeNameMaterialization,
    PublicKeyTokenMaterialization,
    TypeNodeCreation,
    TypeNodeTextRetention,
    GenericContextConstruction,
    GenericParameterNameMaterialization,
    DeclarationNameMaterialization,
    CandidateNameMaterialization,
    TypeDefinitionIndexMaterialization,
    TypeDefinitionIndexTextRetention,
    InterfaceImplementationRowRead,
}

internal sealed class MetadataOperationBudgetExceededException(
    MetadataOperationDimension dimension,
    long limit,
    long attemptedCharge,
    MetadataOperationCounters counters) : Exception
{
    internal MetadataOperationDimension Dimension { get; } = dimension;
    internal long Limit { get; } = limit;
    internal long AttemptedCharge { get; } = attemptedCharge;
    internal MetadataOperationCounters Counters { get; } = counters;
}
