using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>Opaque identity for one owner-issued Analysis source.</summary>
public sealed class AssemblyAnalysisSourceIdentity
{
    internal AssemblyAnalysisSourceIdentity(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}

/// <summary>Detached evidence published by one owner-issued Analysis source.</summary>
public abstract record AssemblyAnalysisSourceReceipt
{
    private protected AssemblyAnalysisSourceReceipt(
        AssemblyAnalysisSourceIdentity source)
    {
        Source = source
            ?? throw new ArgumentNullException(nameof(source));
    }

    public AssemblyAnalysisSourceIdentity Source { get; }
}

/// <summary>
/// One owner-issued source request carried opaquely by assembly orchestration.
/// </summary>
public abstract class AssemblyAnalysisSourceRequest<TResult>
{
    private protected AssemblyAnalysisSourceRequest(
        AssemblyAnalysisSourceIdentity identity)
    {
        Identity = identity
            ?? throw new ArgumentNullException(nameof(identity));
    }

    public AssemblyAnalysisSourceIdentity Identity { get; }

    internal abstract WorkDescription Work { get; }

    internal abstract ProducerDeclaration<TResult> Producer { get; }

    internal abstract bool RequiresReferenceBinding { get; }

    internal abstract AssemblyAnalysisSourceExecution<TResult> Execute(
        AssemblyInspectionSubjectIdentity subject,
        string sourceName,
        PEReader peReader,
        AssemblyReferenceBindingAccess? referenceBinding);
}

/// <summary>
/// One owner-issued request-set plan carried opaquely by assembly orchestration.
/// </summary>
public abstract class AssemblyAnalysisSourceRequestSetPlan
{
    private protected AssemblyAnalysisSourceRequestSetPlan(
        AssemblyAnalysisSourceIdentity identity)
    {
        Identity = identity
            ?? throw new ArgumentNullException(nameof(identity));
    }

    public AssemblyAnalysisSourceIdentity Identity { get; }

    internal abstract bool RequiresReferenceBinding { get; }

    internal abstract AssemblyAnalysisSourceRequestSetExecution Execute(
        AssemblyInspectionSubjectIdentity subject,
        string sourceName,
        PEReader peReader,
        AssemblyReferenceBindingAccess? referenceBinding);
}

/// <summary>Detached execution of one owner-issued source request set.</summary>
public abstract class AssemblyAnalysisSourceRequestSetExecution
{
    private protected AssemblyAnalysisSourceRequestSetExecution(
        AssemblyAnalysisSourceIdentity source)
    {
        Source = source
            ?? throw new ArgumentNullException(nameof(source));
    }

    public AssemblyAnalysisSourceIdentity Source { get; }
}

/// <summary>
/// Resource-free composition of one closed producer description and one
/// owner-issued source request.
/// </summary>
public sealed class AssemblyAnalysisOperation<TResult>
{
    AssemblyAnalysisOperation(
        string sourceName,
        AssemblyAnalysisSourceRequest<TResult> source)
    {
        SourceName = sourceName;
        Source = source;
    }

    /// <summary>Creates an operation without opening or reading its subject.</summary>
    public static AssemblyAnalysisOperation<TResult> Create(
        string sourceName,
        AssemblyAnalysisSourceRequest<TResult> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(source);
        return new(sourceName, source);
    }

    /// <summary>Content-free source name used in diagnostics.</summary>
    public string SourceName { get; }

    /// <summary>The exact closed Producer Planning description.</summary>
    public WorkDescription Work => Source.Work;

    /// <summary>The exact owner-issued source request.</summary>
    public AssemblyAnalysisSourceRequest<TResult> Source { get; }
}

/// <summary>
/// Resource-free composition of one immutable QuerySpace request-set plan
/// and the owner-issued source that will execute its compatible groups.
/// </summary>
public sealed class AssemblyAnalysisRequestSetOperation
{
    AssemblyAnalysisRequestSetOperation(
        string sourceName,
        AssemblyAnalysisSourceRequestSetPlan source)
    {
        SourceName = sourceName;
        Source = source;
    }

    public static AssemblyAnalysisRequestSetOperation Create(
        string sourceName,
        AssemblyAnalysisSourceRequestSetPlan source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(source);
        return new(sourceName, source);
    }

    public string SourceName { get; }

    public AssemblyAnalysisSourceRequestSetPlan Source { get; }
}

/// <summary>Why assembly-operation execution was rejected before producer work.</summary>
public enum AssemblyAnalysisRejectionKind
{
    /// <summary>The access was issued for a different operation.</summary>
    OperationAccessMismatch,

    /// <summary>Session-owned admission found no managed metadata source.</summary>
    ManagedMetadataUnavailable,

    /// <summary>A planned producer declared reference binding and the access carries none.</summary>
    ReferenceBindingUnavailable,
}

/// <summary>Result of binding and executing one assembly analysis operation.</summary>
public abstract record AssemblyAnalysisServiceResult<TResult>
{
    private AssemblyAnalysisServiceResult()
    {
    }

    /// <summary>The source and producer execution completed with detached evidence.</summary>
    public sealed record Completed(AssemblyAnalysisExecution<TResult> Execution)
        : AssemblyAnalysisServiceResult<TResult>;

    /// <summary>Execution was rejected before producer work began.</summary>
    public sealed record Rejected(AssemblyAnalysisRejectionKind Kind)
        : AssemblyAnalysisServiceResult<TResult>;
}

/// <summary>Result of binding and executing one request-set operation.</summary>
public abstract record AssemblyAnalysisRequestSetServiceResult
{
    private AssemblyAnalysisRequestSetServiceResult()
    {
    }

    public sealed record Completed(
        AssemblyAnalysisSourceRequestSetExecution Execution)
        : AssemblyAnalysisRequestSetServiceResult;

    public sealed record Rejected(AssemblyAnalysisRejectionKind Kind)
        : AssemblyAnalysisRequestSetServiceResult;
}

/// <summary>
/// Detached publication for one assembly analysis invocation.
/// </summary>
public sealed class AssemblyAnalysisExecution<TResult>
{
    readonly ProducerResult<TResult> _result;

    internal AssemblyAnalysisExecution(
        AssemblyAnalysisOperation<TResult> operation,
        AssemblyInspectionSubjectIdentity subject,
        AssemblyAnalysisSourceReceipt sourceReceipt,
        WorkReceipt workReceipt,
        ProducerResult<TResult> result)
    {
        Operation = operation;
        Subject = subject;
        SourceReceipt = sourceReceipt;
        WorkReceipt = workReceipt;
        _result = result;
    }

    /// <summary>The exact resource-free operation that produced this execution.</summary>
    public AssemblyAnalysisOperation<TResult> Operation { get; }

    /// <summary>The exact detached subject identity used for this invocation.</summary>
    public AssemblyInspectionSubjectIdentity Subject { get; }

    /// <summary>The owner-issued source receipt.</summary>
    public AssemblyAnalysisSourceReceipt SourceReceipt { get; }

    /// <summary>The aggregate Producer Planning participation receipt.</summary>
    public WorkReceipt WorkReceipt { get; }

    /// <summary>Returns the focused result through its exact producer declaration.</summary>
    public ProducerResult<TResult> ResultOf(
        ProducerDeclaration<TResult> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (!ReferenceEquals(Operation.Source.Producer, producer))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not the focused result "
                + "of this assembly analysis operation.");
        }

        return _result;
    }
}

/// <summary>
/// Stateless executor binding one exact assembly operation to session-issued access.
/// </summary>
public sealed class AssemblyAnalysisService
{
    AssemblyAnalysisService()
    {
    }

    public static AssemblyAnalysisService Instance { get; } = new();

    /// <summary>Executes one operation through its exact stack-only access.</summary>
    public AssemblyAnalysisServiceResult<TResult> Execute<TResult>(
        AssemblyAnalysisOperation<TResult> operation,
        scoped AssemblyInspectionOperationAccess<
            AssemblyAnalysisOperation<TResult>> access)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!ReferenceEquals(operation, access.Operation))
        {
            return new AssemblyAnalysisServiceResult<TResult>.Rejected(
                AssemblyAnalysisRejectionKind.OperationAccessMismatch);
        }

        if (!access.HasMetadata)
        {
            return new AssemblyAnalysisServiceResult<TResult>.Rejected(
                AssemblyAnalysisRejectionKind.ManagedMetadataUnavailable);
        }

        if (operation.Source.RequiresReferenceBinding
            && access.ReferenceBinding is null)
        {
            return new AssemblyAnalysisServiceResult<TResult>.Rejected(
                AssemblyAnalysisRejectionKind.ReferenceBindingUnavailable);
        }

        AssemblyInspectionSubjectIdentity subject = access.Subject;
        AssemblyReferenceBindingAccess? referenceBinding =
            access.ReferenceBinding;
        return access.InspectImage(
            peReader => Execute(
                operation,
                subject,
                peReader,
                referenceBinding));
    }

    /// <summary>
    /// Executes one request-set operation through its exact stack-only access.
    /// </summary>
    public AssemblyAnalysisRequestSetServiceResult Execute(
        AssemblyAnalysisRequestSetOperation operation,
        scoped AssemblyInspectionOperationAccess<
            AssemblyAnalysisRequestSetOperation> access)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!ReferenceEquals(operation, access.Operation))
        {
            return new AssemblyAnalysisRequestSetServiceResult.Rejected(
                AssemblyAnalysisRejectionKind.OperationAccessMismatch);
        }

        if (!access.HasMetadata)
        {
            return new AssemblyAnalysisRequestSetServiceResult.Rejected(
                AssemblyAnalysisRejectionKind.ManagedMetadataUnavailable);
        }

        if (operation.Source.RequiresReferenceBinding
            && access.ReferenceBinding is null)
        {
            return new AssemblyAnalysisRequestSetServiceResult.Rejected(
                AssemblyAnalysisRejectionKind.ReferenceBindingUnavailable);
        }

        AssemblyInspectionSubjectIdentity subject = access.Subject;
        AssemblyReferenceBindingAccess? referenceBinding =
            access.ReferenceBinding;
        return access.InspectImage(
            peReader =>
            {
                AssemblyAnalysisSourceRequestSetExecution source =
                    operation.Source.Execute(
                        subject,
                        operation.SourceName,
                        peReader,
                        referenceBinding);
                if (!ReferenceEquals(
                        source.Source,
                        operation.Source.Identity))
                {
                    throw new ProducerContractException(
                        "The assembly analysis source returned an execution "
                        + "for a different source identity.");
                }

                return new AssemblyAnalysisRequestSetServiceResult.Completed(
                    source);
            });
    }

    static AssemblyAnalysisServiceResult<TResult> Execute<TResult>(
        AssemblyAnalysisOperation<TResult> operation,
        AssemblyInspectionSubjectIdentity subject,
        PEReader peReader,
        AssemblyReferenceBindingAccess? referenceBinding)
    {
        AssemblyAnalysisSourceExecution<TResult> source =
            operation.Source.Execute(
                subject,
                operation.SourceName,
                peReader,
                referenceBinding);
        if (!ReferenceEquals(
                source.Receipt.Source,
                operation.Source.Identity))
        {
            throw new ProducerContractException(
                "The assembly analysis source returned a receipt for a "
                + "different source identity.");
        }

        var execution = new AssemblyAnalysisExecution<TResult>(
            operation,
            subject,
            source.Receipt,
            source.WorkReceipt,
            source.Result);
        return new AssemblyAnalysisServiceResult<TResult>.Completed(
            execution);
    }
}

internal readonly struct AssemblyAnalysisSourceExecution<TResult>
{
    internal AssemblyAnalysisSourceExecution(
        AssemblyAnalysisSourceReceipt receipt,
        WorkReceipt workReceipt,
        ProducerResult<TResult> result)
    {
        Receipt = receipt;
        WorkReceipt = workReceipt;
        Result = result;
    }

    internal AssemblyAnalysisSourceReceipt Receipt { get; }

    internal WorkReceipt WorkReceipt { get; }

    internal ProducerResult<TResult> Result { get; }
}
