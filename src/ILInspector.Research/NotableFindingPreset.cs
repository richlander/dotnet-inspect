using System.Collections.Frozen;
using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;
using ILInspector.Metadata;

namespace ILInspector.Research;

// The notable set: a presentation preset over hidden-fact descriptors and the
// attributes their findings carry. Like AnnotationGestureSelector, it is chosen
// per render and lives beside the finding, never on it, so the positive-only
// contract in Annotations.cs holds: a finding that is not notable is still a
// finding and is still in the map. The preset's semantics are owned by
// docs/design/notable-findings.md#the-notable-set; its place in the annotation
// model is owned by docs/design/hidden-fact-annotations.md.

/// <summary>
/// Why a finding counts toward the notable set. Any one criterion suffices.
/// </summary>
public enum NotableCriterion
{
    /// <summary>A fact the C# text does not show: a box, closure, state machine, delegate, or enumerator allocation.</summary>
    Hidden,

    /// <summary>A visible allocation a loop multiplies.</summary>
    Amplified,

    /// <summary>A caller obligation or a safety boundary: an admitted <c>unsafe.*</c> or <c>lifetime.*</c> fact.</summary>
    Contract,

    /// <summary>
    /// A fact that contradicts the Library's compilation profile. No admitted
    /// descriptor carries this criterion yet; it is reserved for the
    /// Member-header async classification descriptor the provenance owner adds.
    /// </summary>
    Expectation,
}

/// <summary>
/// Why a finding does not count toward the notable set. Descriptive of the
/// preset's decision, never a grade of the fact.
/// </summary>
public enum NotableDemotion
{
    /// <summary>The finding is notable.</summary>
    None,

    /// <summary>The descriptor is outside the preset's vocabulary, such as a cost or semantics fact.</summary>
    OutsidePreset,

    /// <summary>A visible allocation on a straight-line path: the source already says <c>new</c>.</summary>
    VisibleStraightLine,

    /// <summary>The allocation runs on an error path or feeds a throw.</summary>
    ErrorPath,

    /// <summary>The allocation occurs at most once, such as a cached delegate.</summary>
    CachedOnce,

    /// <summary>The Library's compilation profile explains the fact, so one Library mark names the profile instead.</summary>
    LibraryExplained,
}

/// <summary>The preset's decision for one finding.</summary>
/// <param name="Criterion">The criterion that admits the finding, or null when it is demoted.</param>
/// <param name="Demotion">Why the finding is not notable; <see cref="NotableDemotion.None"/> when it is.</param>
public readonly record struct NotableClassification(
    NotableCriterion? Criterion,
    NotableDemotion Demotion)
{
    /// <summary>True when the finding counts toward the Current mark.</summary>
    public bool IsNotable => Criterion is not null;

    internal static NotableClassification Admit(NotableCriterion criterion) =>
        new(criterion, NotableDemotion.None);

    internal static NotableClassification Demote(NotableDemotion demotion) =>
        new(null, demotion);
}

/// <summary>
/// Member-level facts the preset reads beside the finding. The compilation
/// profile adopter supplies them; the preset itself issues none.
/// </summary>
/// <param name="ProfileExplainsStateMachine">
/// True when the Library's compilation profile explains this Member's class
/// state machine: a Debug build and an async method that is not an iterator.
/// Iterators and async iterators are never explained, because their state
/// machines are classes in every build.
/// </param>
/// <param name="StateMachine">
/// The state-machine kind the Member's own attributes claim, or null when
/// unknown. It selects the iterator or async refinement of the
/// <c>alloc.statemachine</c> catalog entry.
/// </param>
public readonly record struct NotableFindingContext(
    bool ProfileExplainsStateMachine = false,
    StateMachineClaimKind? StateMachine = null)
{
    /// <summary>No Member-level fact is known; every demotion reads the finding alone.</summary>
    public static NotableFindingContext None => default;

    /// <summary>
    /// The context for a Member under <paramref name="profile"/>: the
    /// Library-explained fact is the profile's rule applied to the Member's
    /// claimed state-machine kind.
    /// </summary>
    public static NotableFindingContext For(LibraryCompilationProfile profile, StateMachineClaimKind? stateMachine) =>
        new(profile.ExplainsStateMachine(stateMachine), stateMachine);
}

/// <summary>
/// The product's default focus for a symbol list: the owner-issued notable
/// set over the descriptors the annotation model declares. A filter over the
/// attributes producers already emit; it runs no analysis.
/// </summary>
public static class NotableFindingPreset
{
    /// <summary>Descriptors admitted as <see cref="NotableCriterion.Hidden"/> on sight.</summary>
    public static ImmutableArray<string> HiddenDescriptorIds { get; } =
    [
        "alloc.box",
        "alloc.closure",
        "alloc.statemachine",
        "alloc.delegate",
        "alloc.enumerator",
    ];

    /// <summary>Visible allocations admitted as <see cref="NotableCriterion.Amplified"/> only when a loop multiplies them.</summary>
    public static ImmutableArray<string> AmplifiedDescriptorIds { get; } =
    [
        "alloc.array",
        "alloc.new",
    ];

    /// <summary>Descriptors admitted as <see cref="NotableCriterion.Contract"/> on sight.</summary>
    public static ImmutableArray<string> ContractDescriptorIds { get; } =
    [
        "unsafe.deref",
        "unsafe.stackalloc",
        "unsafe.calli",
        "lifetime.ref-return",
        "lifetime.stack-bound",
        "lifetime.ref-struct-return",
        "lifetime.pointer-return",
        "lifetime.stack-escape",
    ];

    /// <summary>
    /// Descriptors admitted as <see cref="NotableCriterion.Expectation"/>.
    /// Empty until the map admits a Member-header descriptor for the Member's
    /// async classification; the compilation profile line and the trait filter
    /// present Expectation without a mark until then.
    /// </summary>
    public static ImmutableArray<string> ExpectationDescriptorIds { get; } = [];

    /// <summary>Every descriptor the preset can admit, sorted by ordinal id.</summary>
    public static ImmutableArray<string> AdmittedDescriptorIds { get; } =
    [
        .. HiddenDescriptorIds
            .Concat(AmplifiedDescriptorIds)
            .Concat(ContractDescriptorIds)
            .Concat(ExpectationDescriptorIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    static readonly FrozenSet<string> Hidden =
        HiddenDescriptorIds.ToFrozenSet(StringComparer.Ordinal);
    static readonly FrozenSet<string> Amplified =
        AmplifiedDescriptorIds.ToFrozenSet(StringComparer.Ordinal);
    static readonly FrozenSet<string> Contract =
        ContractDescriptorIds.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True when <paramref name="annotation"/> counts toward the Current mark.</summary>
    public static bool IsNotable(
        IAnnotation annotation,
        NotableFindingContext context = default) =>
        Classify(annotation, context).IsNotable;

    /// <summary>
    /// Decides whether <paramref name="annotation"/> is notable and why.
    /// Demotion by path or frequency is read before amplification, so an
    /// allocation in a loop inside a catch block stays an error-path fact.
    /// </summary>
    public static NotableClassification Classify(
        IAnnotation annotation,
        NotableFindingContext context = default)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        string id = annotation.Descriptor.Id;

        if (Contract.Contains(id))
            return NotableClassification.Admit(NotableCriterion.Contract);
        if (!Hidden.Contains(id) && !Amplified.Contains(id))
            return NotableClassification.Demote(NotableDemotion.OutsidePreset);

        var occurrence = (annotation as Annotation<AllocationOccurrence>)?.Payload;
        if (occurrence is { PathContext: AllocationPathContext.ErrorPath }
            || occurrence is { Escape: AllocationEscape.ThrowPath })
            return NotableClassification.Demote(NotableDemotion.ErrorPath);
        if (annotation.Conditionality == AnnotationConditionality.CachedOnce
            || occurrence is { Frequency: AllocationFrequency.CachedOnce })
            return NotableClassification.Demote(NotableDemotion.CachedOnce);

        if (Hidden.Contains(id))
        {
            return id == "alloc.statemachine" && context.ProfileExplainsStateMachine
                ? NotableClassification.Demote(NotableDemotion.LibraryExplained)
                : NotableClassification.Admit(NotableCriterion.Hidden);
        }

        return IsLoopMultiplied(annotation, occurrence)
            ? NotableClassification.Admit(NotableCriterion.Amplified)
            : NotableClassification.Demote(NotableDemotion.VisibleStraightLine);
    }

    /// <summary>
    /// True when a loop multiplies the fact: conditionality
    /// <see cref="AnnotationConditionality.PerIteration"/>, an allocation's
    /// path context <see cref="AllocationPathContext.LoopBody"/> or
    /// multiplicity <see cref="AllocationMultiplicity.Loop"/>, or an unsafe
    /// operation's producer-supplied <see cref="UnsafetyOccurrence.InLoop"/>.
    /// </summary>
    public static bool IsLoopMultiplied(IAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return IsLoopMultiplied(
            annotation,
            (annotation as Annotation<AllocationOccurrence>)?.Payload);
    }

    static bool IsLoopMultiplied(IAnnotation annotation, AllocationOccurrence? occurrence) =>
        annotation.Conditionality == AnnotationConditionality.PerIteration
        || occurrence is { PathContext: AllocationPathContext.LoopBody }
        || occurrence is { Multiplicity: AllocationMultiplicity.Loop }
        || annotation is Annotation<UnsafetyOccurrence> { Payload.InLoop: true };
}
