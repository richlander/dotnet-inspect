using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Whether one method definition has unsafe evidence: the open query behind
/// unsafe-evidence presence. It reads the declaration first and the body only
/// when the declaration cannot decide. The algorithm and its presence work
/// budget are the ones the retired index probe used.
/// </summary>
public struct UnsafeEvidencePredicate : IMethodDefinitionPredicate
{
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public readonly bool Test(scoped MethodDefinitionView view)
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
}

/// <summary>
/// Unsafe-evidence presence as an open query over method definitions, at
/// declaration depth with body depth and the module lookup on demand. Closed
/// with Exists it settles on the first evidence; closed with All it counts the
/// methods with evidence.
/// </summary>
public sealed class UnsafeEvidencePresenceProducer
    : MethodDefinitionPredicateProducer<UnsafeEvidencePredicate>
{
    UnsafeEvidencePresenceProducer()
        : base(
            "UnsafeEvidencePresence",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body
                | MethodDefinitionLayers.ModuleLookup)
    {
    }

    public static UnsafeEvidencePresenceProducer Instance { get; } = new();
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
        ProducerResult<int> result =
            Execute(path, peReader).ResultOf(
                UnsafeEvidencePresenceProducer.Instance);
        return result.Outcome switch
        {
            ProducerOutcome.Complete or ProducerOutcome.Stopped =>
                result.Value > 0,
            _ => throw new InvalidDataException(
                "Unsafe evidence presence is incomplete because "
                + $"{result.Failure?.Unit} could not be analyzed: "
                + result.Failure?.Message),
        };
    }
}
