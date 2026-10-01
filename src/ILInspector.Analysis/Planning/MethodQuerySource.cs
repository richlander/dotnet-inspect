using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>The direct physical MethodDef population selected by a source plan.</summary>
public enum MethodDefinitionSourceBreadth
{
    /// <summary>Every MethodDef in metadata order.</summary>
    AllDefinitions,
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
    internal MethodDefinitionSourceRequest(
        WorkDescription work,
        ProducerDeclaration<TResult> producer)
    {
        Work = work;
        Producer = producer;
        Identity = new();
        Breadth = MethodDefinitionSourceBreadth.AllDefinitions;
        Terminal = work.TerminalOf(producer);
        DeclaredLayers = MethodDefinitionExecution.FieldsRead(work);
    }

    /// <summary>Creates the reference Method-source request without reading a subject.</summary>
    public static MethodDefinitionSourceRequest<TResult> Create(
        WorkDescription work,
        ProducerDeclaration<TResult> producer) =>
        MethodQuerySource.Plan(work, producer);

    /// <summary>This request's exact identity.</summary>
    public MethodDefinitionSourceRequestIdentity Identity { get; }

    /// <summary>The direct physical population selected by this request.</summary>
    public MethodDefinitionSourceBreadth Breadth { get; }

    /// <summary>The focused producer result this request publishes.</summary>
    public ProducerDeclaration<TResult> Producer { get; }

    /// <summary>The closing applied to the focused producer.</summary>
    public ProducerTerminal Terminal { get; }

    /// <summary>The source layers declared by the closed work description.</summary>
    public MethodDefinitionLayers DeclaredLayers { get; }

    internal WorkDescription Work { get; }
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
    AssemblyInspectionSubjectIdentity Subject,
    MethodDefinitionSourceBreadth Breadth,
    ProducerTerminal Terminal,
    MethodDefinitionLayers DeclaredLayers,
    MethodDefinitionSourceCompletion Completion,
    int DefinitionsVisited,
    int BodiesAcquired,
    int ModuleLookups);

internal readonly struct MethodQuerySourceExecution<TResult>
{
    internal MethodQuerySourceExecution(
        MethodDefinitionSourceReceipt receipt,
        WorkReceipt workReceipt,
        ProducerResult<TResult> result)
    {
        Receipt = receipt;
        WorkReceipt = workReceipt;
        Result = result;
    }

    internal MethodDefinitionSourceReceipt Receipt { get; }

    internal WorkReceipt WorkReceipt { get; }

    internal ProducerResult<TResult> Result { get; }
}

internal static class MethodQuerySource
{
    internal static MethodDefinitionSourceRequest<TResult> Plan<TResult>(
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

    internal static MethodQuerySourceExecution<TResult> Execute<TResult>(
        MethodDefinitionSourceRequest<TResult> request,
        AssemblyInspectionSubjectIdentity subject,
        string sourceName,
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(peReader);

        MethodDefinitionExecution interim =
            MethodDefinitionExecution.Execute(
                request.Work,
                sourceName,
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
        var receipt = new MethodDefinitionSourceReceipt(
            request.Identity,
            subject,
            request.Breadth,
            request.Terminal,
            request.DeclaredLayers,
            completion,
            workReceipt.UnitsVisited,
            bodiesAcquired,
            moduleLookups);
        return new(receipt, workReceipt, result);
    }
}
