using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.Planning;

namespace ILInspector.Analysis.Tests;

/// <summary>One visited MethodDef's semantic identity and caller contract.</summary>
internal sealed record SemanticIdentityObservation(
    MethodSemanticIdentityResult Result,
    CallerUnsafeMode CallerUnsafeMode)
{
    /// <summary>The current <see cref="MethodIdentity"/>, when an identity was published.</summary>
    public MethodIdentity? MethodIdentity => Result switch
    {
        MethodSemanticIdentityResult.Decoded decoded =>
            decoded.Identity.ToMethodIdentity(CallerUnsafeMode),
        MethodSemanticIdentityResult.SignatureBoundExceeded bounded =>
            bounded.Identity.ToMethodIdentity(CallerUnsafeMode),
        _ => null,
    };
}

/// <summary>
/// Reads every visited MethodDef's semantic identity and composes it with
/// the declared memory-safety contract, so the source's identity can be
/// compared with Library Body Analysis's <see cref="MethodIdentity"/>.
/// </summary>
internal sealed class SemanticIdentityProbe
    : MethodDefinitionProducer<
        KeyValuePair<int, SemanticIdentityObservation>,
        ImmutableDictionary<int, SemanticIdentityObservation>.Builder,
        ImmutableDictionary<int, SemanticIdentityObservation>>
{
    SemanticIdentityProbe(string identity, MethodDefinitionLayers layers)
        : base(identity, version: 1, tier: 0, layers)
    {
    }

    public static SemanticIdentityProbe Instance { get; } =
        new(
            "SemanticIdentityProbe",
            MethodDefinitionLayers.SemanticIdentity
            | MethodDefinitionLayers.MemorySafetyContracts);

    public static SemanticIdentityProbe Second { get; } =
        new(
            "SecondSemanticIdentityProbe",
            MethodDefinitionLayers.SemanticIdentity
            | MethodDefinitionLayers.MemorySafetyContracts);

    /// <summary>Reads identity only, without the contract.</summary>
    public static SemanticIdentityProbe IdentityOnly { get; } =
        new(
            "IdentityOnlySemanticIdentityProbe",
            MethodDefinitionLayers.SemanticIdentity);

    internal override KeyValuePair<int, SemanticIdentityObservation> Visit(
        scoped MethodDefinitionView view)
    {
        MethodSemanticIdentityResult result = view.SemanticIdentity;
        CallerUnsafeMode mode =
            (Layers & MethodDefinitionLayers.MemorySafetyContracts) != 0
                ? CallerUnsafeModeFacts.FromContract(
                    view.GetMemorySafetyContract(
                        MetadataTokens.MethodDefinitionHandle(
                            view.Token & 0x00FFFFFF)))
                : CallerUnsafeMode.None;
        return new(view.Token, new(result, mode));
    }

    internal override ImmutableDictionary<int, SemanticIdentityObservation>.Builder Seed() =>
        ImmutableDictionary.CreateBuilder<int, SemanticIdentityObservation>();

    internal override ImmutableDictionary<int, SemanticIdentityObservation>.Builder Accumulate(
        ImmutableDictionary<int, SemanticIdentityObservation>.Builder accumulator,
        KeyValuePair<int, SemanticIdentityObservation> fact)
    {
        accumulator.Add(fact.Key, fact.Value);
        return accumulator;
    }

    internal override ImmutableDictionary<int, SemanticIdentityObservation> Complete(
        ImmutableDictionary<int, SemanticIdentityObservation>.Builder accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator.ToImmutable();
}

/// <summary>Declares no semantic identity, then reads it.</summary>
internal sealed class UndeclaredSemanticIdentityProbe
    : MethodDefinitionProducer<int, int, int>
{
    UndeclaredSemanticIdentityProbe()
        : base(
            "UndeclaredSemanticIdentityProbe",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Flags)
    {
    }

    public static UndeclaredSemanticIdentityProbe Instance { get; } = new();

    internal override int Visit(scoped MethodDefinitionView view) =>
        view.SemanticIdentity is null ? 0 : 1;

    internal override int Seed() => 0;

    internal override int Accumulate(int accumulator, int fact) =>
        accumulator + fact;

    internal override int Complete(
        int accumulator,
        MethodDefinitionCompletionView completion) =>
        accumulator;
}
