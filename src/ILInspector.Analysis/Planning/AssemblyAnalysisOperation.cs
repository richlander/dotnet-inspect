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

/// <summary>Opaque identity for one owner-issued Method-definition source request.</summary>
public sealed class MethodDefinitionSourceRequestIdentity
{
    internal MethodDefinitionSourceRequestIdentity()
    {
    }
}

/// <summary>
/// One resource-free request to run a closed, single-producer
/// description over Method definitions.
/// </summary>
public sealed class MethodDefinitionSourceRequest<TResult>
{
    MethodDefinitionSourceRequest(
        WorkDescription work,
        ProducerDeclaration<TResult> producer)
    {
        Work = work;
        Producer = producer;
        Identity = new();
        Terminal = work.TerminalOf(producer);
        DeclaredLayers = MethodDefinitionExecution.FieldsRead(work);
    }

    /// <summary>Creates the reference Method-source request without reading a subject.</summary>
    public static MethodDefinitionSourceRequest<TResult> Create(
        WorkDescription work,
        ProducerDeclaration<TResult> producer)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(producer);
        if (!work.Contains(producer) || !work.WasRequested(producer))
        {
            throw new ProducerContractException(
                $"Producer '{producer.Identity}' is not a requested producer "
                + "in this work description.");
        }

        if (work.Producers.Length != 1
            || !ReferenceEquals(work.Producers[0], producer))
        {
            throw new ProducerContractException(
                "The reference Method source accepts exactly one planned "
                + "producer until QuerySpace request collapse lands.");
        }

        foreach (ProducerDeclaration planned in work.Producers)
        {
            if (planned is not IMethodDefinitionProducer)
            {
                throw new ProducerContractException(
                    $"Producer '{planned.Identity}' is not a "
                    + "method-definition producer.");
            }
        }

        return new(work, producer);
    }

    /// <summary>This request's exact identity.</summary>
    public MethodDefinitionSourceRequestIdentity Identity { get; }

    /// <summary>The focused producer result this request publishes.</summary>
    public ProducerDeclaration<TResult> Producer { get; }

    /// <summary>The closing applied to the focused producer.</summary>
    public ProducerTerminal Terminal { get; }

    /// <summary>The source layers declared by the closed work description.</summary>
    public MethodDefinitionLayers DeclaredLayers { get; }

    internal WorkDescription Work { get; }
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

/// <summary>How the Method source settled one accepted request.</summary>
public enum MethodDefinitionSourceCompletion
{
    /// <summary>The source exhausted the selected Method-definition population.</summary>
    Exhausted,

    /// <summary>The request's terminal settled before source exhaustion.</summary>
    Satisfied,

    /// <summary>A producer failure prevented the source request from settling.</summary>
    ProducerFailed,

    /// <summary>A critical producer work bound aborted the source request.</summary>
    Aborted,
}

/// <summary>Detached evidence of the Method-source work actually performed.</summary>
public sealed record MethodDefinitionSourceReceipt(
    MethodDefinitionSourceRequestIdentity Request,
    ProducerTerminal Terminal,
    MethodDefinitionLayers DeclaredLayers,
    MethodDefinitionSourceCompletion Completion,
    int DefinitionsVisited,
    int BodiesAcquired,
    int ModuleLookups);

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

    static AssemblyAnalysisServiceResult<TResult> Execute<TResult>(
        AssemblyAnalysisOperation<TResult> operation,
        AssemblyInspectionSubjectIdentity subject,
        PEReader peReader)
    {
        MethodDefinitionSourceRequest<TResult> request =
            operation.MethodDefinitions;
        MethodDefinitionExecution interim =
            MethodDefinitionExecution.Execute(
                operation.Work,
                operation.SourceName,
                peReader);
        ProducerResult<TResult> result =
            interim.ResultOf(request.Producer);
        WorkReceipt workReceipt = interim.Receipt;
        ProducerParticipation participation =
            workReceipt.For(request.Producer);

        int bodiesAcquired = 0;
        int moduleLookups = 0;
        foreach (ProducerLayerParticipation layer in participation.Layers)
        {
            if (string.Equals(
                    layer.Layer,
                    nameof(MethodDefinitionLayers.Body),
                    StringComparison.Ordinal))
            {
                bodiesAcquired = layer.Acquired;
            }
            else if (string.Equals(
                    layer.Layer,
                    nameof(MethodDefinitionLayers.ModuleLookup),
                    StringComparison.Ordinal))
            {
                moduleLookups = layer.Acquired;
            }
        }

        MethodDefinitionSourceCompletion completion = result.Outcome switch
        {
            ProducerOutcome.Complete =>
                MethodDefinitionSourceCompletion.Exhausted,
            ProducerOutcome.Stopped =>
                MethodDefinitionSourceCompletion.Satisfied,
            ProducerOutcome.Failed or ProducerOutcome.PrerequisiteFailed =>
                MethodDefinitionSourceCompletion.ProducerFailed,
            ProducerOutcome.Aborted =>
                MethodDefinitionSourceCompletion.Aborted,
            _ => throw new ProducerContractException(
                $"Unknown producer outcome '{result.Outcome}'."),
        };
        var sourceReceipt = new MethodDefinitionSourceReceipt(
            request.Identity,
            request.Terminal,
            request.DeclaredLayers,
            completion,
            workReceipt.UnitsVisited,
            bodiesAcquired,
            moduleLookups);
        var execution = new AssemblyAnalysisExecution<TResult>(
            operation,
            subject,
            sourceReceipt,
            workReceipt,
            result);
        return new AssemblyAnalysisServiceResult<TResult>.Completed(
            execution);
    }
}
