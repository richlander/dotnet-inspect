using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Unsafe-evidence presence: a producer over method definitions at
/// declaration depth, with body depth on demand, whose fact is whether the
/// unit has unsafe evidence. It settles an Exists terminal on the first
/// evidence. The algorithm and its presence work budget are the ones the
/// retired index probe used.
/// </summary>
public sealed class UnsafeEvidencePresenceProducer
    : MethodDefinitionProducer<bool, bool>
{
    UnsafeEvidencePresenceProducer()
        : base(
            "UnsafeEvidencePresence",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body)
    {
    }

    public static UnsafeEvidencePresenceProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view)
    {
        var unit = new UnsafePresenceUnit(
            view.TypeHandle,
            view.TypeDefinition,
            view.MethodHandle,
            view.MethodDefinition);
        return view.Lookup.ProbeUnsafeDeclaration(unit) switch
        {
            UnsafePresenceDeclaration.Evidence => true,
            UnsafePresenceDeclaration.NoManagedBody => false,
            _ => view.Lookup.ProbeUnsafeBody(unit, view.GetBody()),
        };
    }

    internal override bool Complete(
        IReadOnlyList<bool> facts,
        MethodDefinitionCompletionView completion) =>
        facts.Contains(true);

    internal override bool Settles(bool fact) => fact;
}

/// <summary>
/// Answers whether a library contains unsafe evidence through a
/// one-producer work description with an Exists terminal.
/// </summary>
public static class UnsafeEvidencePresence
{
    /// <summary>The request: unsafe-evidence presence with an Exists terminal.</summary>
    public static ProducerRequest Request { get; } =
        new(UnsafeEvidencePresenceProducer.Instance, ProducerTerminal.Exists);

    /// <summary>The work description, computed without reading any subject.</summary>
    public static WorkDescription Description { get; } =
        ProducerPlanner.Plan([Request]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException(
                "The unsafe-evidence presence request must plan.");

    /// <summary>Runs the work description and returns the execution with its receipt.</summary>
    public static MethodDefinitionExecution Execute(
        string sourceName,
        PEReader peReader) =>
        MethodDefinitionExecution.Execute(
            Description,
            sourceName,
            peReader);

    public static bool HasEvidence(
        string path,
        PdbContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);

        return context.InspectImage(
            peReader => HasEvidence(path, peReader));
    }

    public static bool HasEvidence(
        string path,
        ImmutableArray<byte> image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (image.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A PE image is required.",
                nameof(image));
        }

        using var peReader = new PEReader(image);
        return HasEvidence(path, peReader);
    }

    static bool HasEvidence(
        string path,
        PEReader peReader)
    {
        ProducerResult<bool> result =
            Execute(path, peReader).ResultOf(
                UnsafeEvidencePresenceProducer.Instance);
        return result.Outcome switch
        {
            ProducerOutcome.Complete or ProducerOutcome.Stopped =>
                result.Value,
            _ => throw new InvalidDataException(
                "Unsafe evidence presence is incomplete because "
                + $"{result.Failure?.Unit} could not be analyzed: "
                + result.Failure?.Message),
        };
    }
}
