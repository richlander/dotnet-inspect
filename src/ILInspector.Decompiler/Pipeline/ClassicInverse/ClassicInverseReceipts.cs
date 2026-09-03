using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>Which request body a receipt classifies.</summary>
internal enum ClassicInverseBodyId
{
    Kickoff,
    Execution,
}

/// <summary>
/// The one disposition every in-scope physical region carries. The set is
/// closed: a region with no rule is not a region, it is a decline.
/// </summary>
internal enum ClassicInverseRegionDisposition
{
    /// <summary>Authenticated lowering scaffolding.</summary>
    Protocol,

    /// <summary>Contributes reconstructed user behavior.</summary>
    Semantic,

    /// <summary>Positively proven semantically inert for the declared method.</summary>
    Preserved,
}

/// <summary>
/// How a structured ancestor on a consumed node's path is accounted for.
/// </summary>
internal enum ClassicInverseAncestorKind
{
    /// <summary>Reproduced in the output, with a named output context.</summary>
    Reproduced,

    /// <summary>Exact lowering-shell scaffolding whose semantics are accounted elsewhere.</summary>
    Protocol,

    /// <summary>Proven to change neither execution, ordering, value, nor exception behavior.</summary>
    Transparent,
}

/// <summary>
/// One physical region of an unmodified import snapshot, addressed by its
/// child-index path from the body root.
/// <para>
/// <paramref name="OwnsSubtree"/> distinguishes a receipt that claims the whole
/// subtree from a protocol <em>frame</em> whose designated child slots carry
/// their own receipts. The partition verifier requires that a subtree-owning
/// receipt has no descendant receipt and that every in-scope node is covered
/// exactly once.
/// </para>
/// </summary>
internal sealed record ClassicInversePhysicalRegion(
    ClassicInverseBodyId Body,
    ImmutableArray<int> Path,
    string NodeForm,
    ClassicInverseRegionDisposition Disposition,
    bool OwnsSubtree,
    string Rule,
    ImmutableArray<int> ImportOffsets)
{
    internal string Signature =>
        $"{Body}{ClassicInverseSignature.Path(Path)}:{NodeForm}:{Disposition}"
        + $":{(OwnsSubtree ? "subtree" : "frame")}:{Rule}"
        + $":[{string.Join(",", ImportOffsets)}]";
}

/// <summary>
/// The named correspondence rule a recipe used to turn one input region into
/// one output region. Each rule has an explicit shape obligation the verifier
/// re-checks; a recipe cannot license a realization by assertion alone.
/// </summary>
internal enum ClassicInverseRealizationRule
{
    /// <summary>The operand of the compiler's <c>GetAwaiter</c> call becomes the <c>await</c> operand.</summary>
    AwaitedOperand,

    /// <summary>The compiler's <c>GetResult</c> call becomes the <c>await</c> expression.</summary>
    AwaitResult,

    /// <summary>A value expression cloned and remapped from hoisted storage to kickoff parameters.</summary>
    ValueExpression,

    /// <summary>A hoisted or local result store becomes an output store or return.</summary>
    ResultStore,

    /// <summary>A user statement becomes an output statement.</summary>
    Statement,

    /// <summary>A branch condition becomes a reproduced output condition.</summary>
    ControlCondition,

    /// <summary>The compiler's hoisted loop collection becomes the <c>foreach</c> collection.</summary>
    LoopCollection,

    /// <summary>The compiler's hoisted loop element read becomes the <c>foreach</c> variable read.</summary>
    LoopElement,

    /// <summary>The compiler's hoisted accumulator update becomes the loop-body store.</summary>
    LoopAccumulator,
}

/// <summary>
/// One input semantic region and the single output region that realizes it.
/// The verifier checks the effect sequence of both sides, so an omitted,
/// duplicated, reordered, or invented effect fails.
/// </summary>
internal sealed record ClassicInverseSemanticRealization(
    ClassicInverseBodyId Body,
    ImmutableArray<int> SourcePath,
    ImmutableArray<int> OutputPath,
    ClassicInverseRealizationRule Rule,
    ImmutableArray<string> SourceEffects,
    ImmutableArray<string> OutputEffects)
{
    internal string Signature =>
        $"{Body}{ClassicInverseSignature.Path(SourcePath)}"
        + $"->{ClassicInverseSignature.Path(OutputPath)}:{Rule}"
        + $":in[{ClassicInverseSignature.Sequence(SourceEffects)}]"
        + $":out[{ClassicInverseSignature.Sequence(OutputEffects)}]";
}

/// <summary>One ancestor step on a consumed node's structured path.</summary>
internal sealed record ClassicInverseAncestorStep(
    ImmutableArray<int> Path,
    string NodeForm,
    ClassicInverseAncestorKind Kind,
    string Rule,
    ImmutableArray<int> OutputContextPath)
{
    internal string Signature =>
        $"{ClassicInverseSignature.Path(Path)}:{NodeForm}:{Kind}:{Rule}"
        + $":{ClassicInverseSignature.Path(OutputContextPath)}";
}

/// <summary>
/// The uninterrupted parent path from one consumed semantic node to its recipe
/// root. A path that does not reach the root, or that carries an unclassified
/// step, declines.
/// </summary>
internal sealed record ClassicInverseAncestorReceipt(
    ClassicInverseBodyId Body,
    ImmutableArray<int> ConsumedPath,
    ImmutableArray<ClassicInverseAncestorStep> Steps)
{
    internal string Signature =>
        $"{Body}{ClassicInverseSignature.Path(ConsumedPath)}:"
        + ClassicInverseSignature.Sequence(Steps.Select(static s => s.Signature));
}
