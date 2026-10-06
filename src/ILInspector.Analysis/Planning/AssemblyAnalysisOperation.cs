using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>An owner-issued Analysis source composed by an assembly operation.</summary>
public enum AssemblyAnalysisSourceKind
{
    /// <summary>Method definitions in metadata order.</summary>
    MethodDefinitions,
}

/// <summary>
/// Resource-free composition of one closed producer description and one
/// owner-issued Method source request.
/// </summary>
public sealed class AssemblyAnalysisOperation<TResult>
{
    AssemblyAnalysisOperation(
        string sourceName,
        MethodDefinitionSourceRequest<TResult> methodDefinitions)
    {
        SourceName = sourceName;
        MethodDefinitions = methodDefinitions;
    }

    /// <summary>Creates an operation without opening or reading its subject.</summary>
    public static AssemblyAnalysisOperation<TResult> Create(
        string sourceName,
        MethodDefinitionSourceRequest<TResult> methodDefinitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(methodDefinitions);
        return new(sourceName, methodDefinitions);
    }

    /// <summary>Content-free source name used in diagnostics.</summary>
    public string SourceName { get; }

    /// <summary>The exact closed Producer Planning description.</summary>
    public WorkDescription Work => MethodDefinitions.Work;

    /// <summary>The exact owner-issued Method source request.</summary>
    public MethodDefinitionSourceRequest<TResult> MethodDefinitions { get; }

    /// <summary>The owner-issued source kinds composed by this operation.</summary>
    public ImmutableArray<AssemblyAnalysisSourceKind> SourceKinds =>
        [AssemblyAnalysisSourceKind.MethodDefinitions];
}

/// <summary>
/// Resource-free composition of one immutable QuerySpace request-set plan
/// and the Method source that will execute its compatible groups.
/// </summary>
public sealed class AssemblyAnalysisRequestSetOperation
{
    AssemblyAnalysisRequestSetOperation(
        string sourceName,
        MethodDefinitionSourceRequestSetPlan methodDefinitions)
    {
        SourceName = sourceName;
        MethodDefinitions = methodDefinitions;
    }

    public static AssemblyAnalysisRequestSetOperation Create(
        string sourceName,
        MethodDefinitionSourceRequestSetPlan methodDefinitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(methodDefinitions);
        return new(sourceName, methodDefinitions);
    }

    public string SourceName { get; }

    public MethodDefinitionSourceRequestSetPlan MethodDefinitions { get; }

    public ImmutableArray<AssemblyAnalysisSourceKind> SourceKinds =>
        [AssemblyAnalysisSourceKind.MethodDefinitions];
}

/// <summary>Why assembly-operation execution was rejected before producer work.</summary>
public enum AssemblyAnalysisRejectionKind
{
    /// <summary>The access was issued for a different operation.</summary>
    OperationAccessMismatch,

    /// <summary>Session-owned admission found no managed metadata source.</summary>
    ManagedMetadataUnavailable,
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
        MethodDefinitionSourceRequestSetExecution Execution)
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
        MethodDefinitionSourceReceipt sourceReceipt,
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

    /// <summary>The owner-issued Method-source receipt.</summary>
    public MethodDefinitionSourceReceipt SourceReceipt { get; }

    /// <summary>The aggregate Producer Planning participation receipt.</summary>
    public WorkReceipt WorkReceipt { get; }

    /// <summary>Returns the focused result through its exact producer declaration.</summary>
    public ProducerResult<TResult> ResultOf(
        ProducerDeclaration<TResult> producer)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (!ReferenceEquals(Operation.MethodDefinitions.Producer, producer))
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

        AssemblyInspectionSubjectIdentity subject = access.Subject;
        return access.InspectImage(
            peReader => Execute(operation, subject, peReader));
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

        AssemblyInspectionSubjectIdentity subject = access.Subject;
        return access.InspectImage(
            peReader =>
                new AssemblyAnalysisRequestSetServiceResult.Completed(
                    MethodQuerySource.Execute(
                        operation.MethodDefinitions,
                        subject,
                        operation.SourceName,
                        peReader)));
    }

    static AssemblyAnalysisServiceResult<TResult> Execute<TResult>(
        AssemblyAnalysisOperation<TResult> operation,
        AssemblyInspectionSubjectIdentity subject,
        PEReader peReader)
    {
        MethodDefinitionSourceRequest<TResult> request =
            operation.MethodDefinitions;
        MethodQuerySourceExecution<TResult> source =
            MethodQuerySource.Execute(
                request,
                subject,
                operation.SourceName,
                peReader);
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
