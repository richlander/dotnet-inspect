using System.Collections.Immutable;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Whether one method definition has a positive unsafe member census role,
/// by the census visit's own evidence rule.
/// </summary>
public struct UnsafeMemberEvidencePredicate : IMethodDefinitionPredicate
{
    public readonly bool Test(scoped MethodDefinitionView view) =>
        UnsafeMemberCensusProducer.HasEvidence(view);
}

/// <summary>
/// Unsafe member presence as an open query over method definitions: the
/// discovery probe for the <c>Unsafe Members</c> section, as
/// <c>docs/design/unsafe-members-section.md#discovery</c> defines it. Closed
/// with Exists it settles on the first definition with a census role.
/// </summary>
public sealed class UnsafeMemberPresenceProducer
    : MethodDefinitionPredicateProducer<UnsafeMemberEvidencePredicate>
{
    UnsafeMemberPresenceProducer()
        : base(
            "UnsafeMemberPresence",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Body
                | MethodDefinitionLayers.Calls
                | MethodDefinitionLayers.MemorySafetyContracts
                | MethodDefinitionLayers.SemanticIdentity)
    {
    }

    public static UnsafeMemberPresenceProducer Instance { get; } = new();

    internal override ImmutableArray<MethodBodyAnalyzerDeclaration>
        InstructionAnalyzers =>
        [MethodBodyAnalyzerDeclarations.ResolvedCalls];

    // The closed-query kernel visits without the fused instruction
    // analyzers that the census rule's resolved calls need.
    internal override bool AllowsKernel => false;
}

/// <summary>
/// The owner-issued unsafe member presence request with an Exists terminal.
/// </summary>
public static class UnsafeMemberPresence
{
    /// <summary>The request: unsafe member presence with an Exists terminal.</summary>
    public static ProducerRequest Request { get; } =
        new(UnsafeMemberPresenceProducer.Instance, ProducerTerminal.Exists);

    /// <summary>The work description, computed without reading any subject.</summary>
    public static WorkDescription Description { get; } =
        ProducerPlanner.Plan([Request]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException(
                "The unsafe member presence request must plan.");
}
