using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Annotations;

// Hidden-fact annotations: a descriptive layer over the decompiler's IR that
// surfaces facts the source text hides — allocations, unsafety, lifetime
// contracts. The model is modelled on Roslyn analyzers (a registry of
// independent producers, a descriptor/instance split, stable ids) but
// deliberately drops the parts that turn a description into a judgement:
//
//   * no severity        — annotations describe, they never grade.
//   * no Location        — keyed to an IL offset (provenance), projected onto
//                          both the IL view (by offset) and the C# view (via
//                          the IR node that printed a line).
//   * no code fix        — there is nothing to "fix"; the point is to teach.
//
// One further axis Roslyn has no need for: a classifier declares the pipeline
// stage it observes, because a fact is clearest at a particular altitude.
//
// The governing contract is POSITIVE-ONLY: an annotation marks the presence of
// a fact, never the absence of others. There is no "all clear" — any tally is a
// roll-up of what was found, never an oracle that asserts nothing else exists.

/// <summary>
/// The family a hidden-fact annotation belongs to. A curated, in-repo enum
/// rather than an open string: adding a family is a deliberate one-line change,
/// while the classifiers that produce annotations stay independent and
/// additive.
/// </summary>
public enum AnnotationCategory
{
    /// <summary>Heap allocations — boxing, object/array construction, closures, iterator and async state machines.</summary>
    Allocation,

    /// <summary>Unsafe operations — pointer dereferences, <c>stackalloc</c>, unmanaged function pointers, pinning.</summary>
    Unsafety,

    /// <summary>Ref-safety lifetime contracts — ref structs, <c>scoped</c>/<c>[UnscopedRef]</c>, ref returns, byref parameters.</summary>
    Lifetime,

    /// <summary>Whole-assembly cost facts — call-site callee cost and method leverage overlays.</summary>
    Cost,
}

/// <summary>
/// How often the annotated fact materialises on the path that reaches it. A
/// descriptive refinement, never a verdict: <see cref="CachedOnce"/> marks a
/// fact that occurs at most once (e.g. a cached delegate), so a flat
/// "allocates" does not cry wolf on code that allocates lazily.
/// </summary>
public enum AnnotationConditionality
{
    /// <summary>Occurs every time the annotated code is reached.</summary>
    Always,

    /// <summary>Occurs at most once across the lifetime of the program (e.g. a statically cached delegate or singleton).</summary>
    CachedOnce,

    /// <summary>Occurs once per iteration of an enclosing loop.</summary>
    PerIteration,
}

/// <summary>
/// The pipeline stage a classifier observes — its vantage point over the same
/// IR the rewrite passes transform. The read-only analogue of the transformer
/// pipeline's ordering: a box is plainest at <see cref="Imported"/>, a
/// cached-delegate shape only after the tree has been <see cref="Raised"/>.
/// </summary>
public enum AnnotationStage
{
    /// <summary>The freshly imported instruction tree, before any rewrite pass.</summary>
    Imported,

    /// <summary>After lowering passes, before the cosmetic raise (no <c>for</c>/<c>lock</c> sugar yet).</summary>
    Lowered,

    /// <summary>The fully raised tree the C# view prints.</summary>
    Raised,
}

/// <summary>
/// Static metadata for a family of hidden facts — the analogue of Roslyn's
/// <c>DiagnosticDescriptor</c>, deliberately without a severity. Identified by a
/// stable <see cref="Id"/> so that selection, structured output, and automated
/// consumers can name a fact family across versions.
/// </summary>
/// <param name="Id">Stable, dotted identifier for the fact family, e.g. <c>alloc.box</c>. Never reused or renumbered.</param>
/// <param name="Category">The broad family this descriptor belongs to.</param>
/// <param name="Title">A short human-readable description of the fact, e.g. "boxes a value type".</param>
public sealed record AnnotationDescriptor(string Id, AnnotationCategory Category, string Title);

/// <summary>
/// One occurrence of a hidden fact. Positive-only and descriptive: it marks the
/// presence of a fact, never the absence of others. Keyed to
/// <see cref="SourceOffset"/> (an IL offset) so it projects onto both the IL
/// view (by offset) and the C# view (via the node that printed a line);
/// <see cref="Node"/> carries the originating IR node when one is known, which
/// lets a renderer anchor the annotation without re-deriving the offset.
/// </summary>
/// <param name="Descriptor">The fact family this occurrence belongs to.</param>
/// <param name="SourceOffset">IL offset of the originating instruction, or -1 when unknown (a synthetic node with no provenance).</param>
/// <param name="Detail">Optional specifics for this occurrence, e.g. the boxed type name. Folded into the rendered message.</param>
/// <param name="Conditionality">How often the fact materialises at run time. Defaults to <see cref="AnnotationConditionality.Always"/>.</param>
/// <param name="Node">The IR node the fact was found on, when available; never used for identity.</param>
public sealed record Annotation(
    AnnotationDescriptor Descriptor,
    int SourceOffset,
    string? Detail = null,
    AnnotationConditionality Conditionality = AnnotationConditionality.Always,
    IrNode? Node = null);

/// <summary>
/// Produces hidden-fact annotations from the IR. The read-only dual of an
/// <c>IIrPass</c>: it shares the IR substrate but is pure — no mutation, no
/// invariant to preserve, order-independent — and it declares the
/// <see cref="Stage"/> it observes. Adding a fact family is a matter of
/// registering one of these; the core does not change.
/// </summary>
public interface IHiddenFactClassifier
{
    /// <summary>The fact family this classifier owns.</summary>
    AnnotationCategory Category { get; }

    /// <summary>The IR stage this classifier expects to observe.</summary>
    AnnotationStage Stage { get; }

    /// <summary>
    /// Walks <paramref name="function"/> and returns the facts it found.
    /// Positive-only: returns an empty list when nothing is found, never an
    /// "all clear" sentinel.
    /// </summary>
    IReadOnlyList<Annotation> Classify(IrFunction function);
}
