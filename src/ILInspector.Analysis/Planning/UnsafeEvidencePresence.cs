using System.Collections.Immutable;
using System.Reflection.Metadata;
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
/// with Exists it settles on the first evidence; closed with Count it counts the
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
        Execute(sourceName, peReader, Description);

    /// <summary>Runs the supplied work description and returns the execution with its receipt.</summary>
    public static MethodDefinitionExecution Execute(
        string sourceName,
        PEReader peReader,
        WorkDescription description) =>
        MethodDefinitionExecution.Execute(
            description,
            sourceName,
            peReader);

    public static bool HasEvidence(
        string path,
        PdbContext context)
        => RequireEvidence(Inspect(path, context, Description));

    /// <summary>Runs the supplied plan and returns its detached answer and receipt.</summary>
    public static UnsafeEvidencePresenceInspection Inspect(
        string path,
        PdbContext context,
        WorkDescription description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(description);

        return context.InspectImage(
            peReader => Inspect(path, peReader, description));
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
        return RequireEvidence(Inspect(path, peReader, Description));
    }

    static UnsafeEvidencePresenceInspection Inspect(
        string path,
        PEReader peReader,
        WorkDescription description)
    {
        MethodDefinitionExecution execution = Execute(
            path,
            peReader,
            description);
        return Project(
            execution.ResultOf(
                UnsafeEvidencePresenceProducer.Instance),
            execution.Receipt,
            peReader);
    }

    /// <summary>
    /// Projects one detached producer execution while the owning image remains
    /// available for the failure label.
    /// </summary>
    public static UnsafeEvidencePresenceInspection Project(
        ProducerResult<int> result,
        WorkReceipt receipt,
        PEReader peReader)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(peReader);
        if (result.Outcome is ProducerOutcome.Complete or ProducerOutcome.Stopped)
        {
            return new UnsafeEvidencePresenceInspection.Available(
                result.Value > 0,
                receipt);
        }

        // The failure is recorded by token; presenting it resolves that one
        // method's name with a length-checked, capped read.
        int token = result.Failure?.UnitToken ?? result.Critical?.UnitToken ?? 0;
        string unit = token != 0
            ? MethodRowProjection.FailureLabel(peReader.GetMetadataReader(), token).ToString()
            : result.Failure?.Unit ?? result.Critical?.Unit ?? "(unknown)";
        string reason = result.Failure?.Message ?? result.Critical?.Message ?? "";
        return new UnsafeEvidencePresenceInspection.Incomplete(
            new InvalidDataException(
                "Unsafe evidence presence is incomplete because "
                + $"{unit} could not be analyzed: {reason}"),
            result.Outcome,
            receipt);
    }

    static bool RequireEvidence(UnsafeEvidencePresenceInspection inspection) =>
        inspection switch
        {
            UnsafeEvidencePresenceInspection.Available available =>
                available.HasEvidence,
            UnsafeEvidencePresenceInspection.Incomplete incomplete =>
                throw incomplete.Error,
            _ => throw new InvalidOperationException(
                "Unknown unsafe-evidence inspection outcome."),
        };
}

/// <summary>Detached answer or execution failure for one inspection.</summary>
public abstract record UnsafeEvidencePresenceInspection
{
    private UnsafeEvidencePresenceInspection()
    {
    }

    /// <summary>The producer settled with an available Boolean answer.</summary>
    public sealed record Available(
        bool HasEvidence,
        WorkReceipt Receipt)
        : UnsafeEvidencePresenceInspection;

    /// <summary>The producer did not complete after execution began.</summary>
    public sealed record Incomplete(
        InvalidDataException Error,
        ProducerOutcome Outcome,
        WorkReceipt Receipt)
        : UnsafeEvidencePresenceInspection;
}
