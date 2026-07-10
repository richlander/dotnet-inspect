namespace ILInspector.Findings;

/// <summary>
/// The thing a finding is about (a member, an occurrence within a body, an API surface element).
/// Domain-free: <see cref="Key"/> is an opaque stable string the producing layer chooses;
/// <see cref="Display"/> is a human label.
/// </summary>
public sealed record FindingSubject(string Key, string Display);

/// <summary>
/// A typed, reorder-stable vocabulary entry for a finding, following the Roslyn
/// <c>DiagnosticDescriptor</c> precedent. <see cref="Id"/> is a <c>family.kind</c> const string
/// (e.g. <c>il.op</c>, <c>alloc.box</c>); the absence of a numeric code is deliberate so
/// descriptors can be added and reordered without renumbering.
/// </summary>
public sealed record FindingDescriptor(string Id, string Title);

/// <summary>
/// How an old/new pair of findings relates across two versions. This is a <em>transition</em>
/// classification and so lives on a <see cref="PairFinding{T}"/>, never on the atom: a lone
/// <see cref="Finding{T}"/> just <em>is</em> — it has no "Added".
/// </summary>
public enum PairKind
{
    /// <summary>Present on both sides.</summary>
    Present,

    /// <summary>Present only on the new side.</summary>
    Added,

    /// <summary>Present only on the old side.</summary>
    Removed,

    /// <summary>Matched across sides but with a content change.</summary>
    Changed,
}

/// <summary>
/// The kind of difference a fold observed for a matched pair, orthogonal to <see cref="PairKind"/>.
/// A move is deliberately not a polarity: a moved occurrence keeps <see cref="PairKind.Present"/>
/// polarity (its content is unchanged) and carries the move on this facet. Consumers select an
/// equivalence by allow-listing classes (see <see cref="FindingEquivalence"/>).
/// </summary>
public enum FindingDifferenceKind
{
    /// <summary>No observable difference for this pair.</summary>
    None,

    /// <summary>Only incidental encoding differed (short/long form, ldc family).</summary>
    EncodingOnly,

    /// <summary>Same content, different location (a matched pair with a position delta).</summary>
    Moved,
}

/// <summary>
/// The domain-free skeleton shared by an atom (<see cref="Finding{T}"/>) and a transition
/// (<see cref="PairFinding{T}"/>): what a cross-stream consumer needs without knowing the payload
/// type or which producer emitted it. <see cref="IFinding{T}"/> adds the typed payload.
/// </summary>
public interface IFinding
{
    FindingSubject Subject { get; }
    FindingDescriptor Descriptor { get; }
    string? Detail { get; }
}

/// <summary>
/// A finding with its typed payload. Non-variant on <typeparamref name="T"/>: a payload may be a
/// value type, and CLR variance is reference-only.
/// </summary>
public interface IFinding<T> : IFinding
    where T : notnull
{
    T Payload { get; }
}

/// <summary>
/// The atom: a single observation projected from a domain node (an IL op, an allocation, an API
/// member). A single-version query is just a <c>Finding&lt;T&gt;[]</c> census — no matcher, no
/// diff. It carries its content <see cref="Key"/> and a single <see cref="Position"/> in its own
/// stream; the transition concepts (add/remove/change, an old-vs-new delta) belong to
/// <see cref="PairFinding{T}"/>, never here. The payload is non-null and a type parameter rather
/// than <c>object?</c>, so a value-typed payload is not boxed and consumers read it without a
/// cast; absence is expressed by choosing <see cref="IFinding"/>, not by a null payload.
/// </summary>
public sealed record Finding<T>(
    FindingSubject Subject,
    FindingDescriptor Descriptor,
    FindingKey Key,
    int Position,
    T Payload,
    string? Detail = null) : IFinding<T>
    where T : notnull;

/// <summary>
/// The domain-free skeleton of a transition: an old/new pair classified by <see cref="Kind"/> and
/// <see cref="Difference"/>. Extends <see cref="IFinding"/> so a cross-stream fold
/// (<see cref="FindingSummary"/>, <see cref="FindingEquivalence"/>) reads it without knowing the
/// payload type. <see cref="Old"/>/<see cref="New"/> expose each side as a skeleton; polarity is
/// read from their nullability (<see cref="Old"/> is null ⟹ added).
/// </summary>
public interface IPairFinding : IFinding
{
    PairKind Kind { get; }
    FindingDifferenceKind Difference { get; }
    IFinding? Old { get; }
    IFinding? New { get; }
}

/// <summary>
/// A classified transition between two <see cref="Finding{T}"/> atoms — the diff row. Composed of
/// findings: <see cref="Old"/> and <see cref="New"/>, one of which is null for a one-sided
/// add/remove. Implements <see cref="IFinding{T}"/> with a "current" payload projection
/// (<see cref="New"/> if present, else <see cref="Old"/>); both sides remain reachable via
/// <see cref="Old"/>/<see cref="New"/> for consumers that need the change.
/// <para>
/// The type makes an inconsistent pair unrepresentable. <see cref="Kind"/> is <em>derived</em>
/// from the sides (one side ⟹ Added/Removed; both ⟹ Changed when <see cref="ContentChanged"/>,
/// else Present), so there is no polarity to set out of step with them. <see cref="Old"/> and
/// <see cref="New"/> are get-only — a <c>with</c> expression cannot null them out — and the
/// constructor rejects the one remaining degenerate case (neither side present). Only the
/// non-structural facets (<see cref="Difference"/>, <see cref="ContentChanged"/>,
/// <see cref="Detail"/>) remain <c>init</c>-settable for <c>with</c>.
/// </para>
/// </summary>
public sealed record PairFinding<T> : IPairFinding, IFinding<T>
    where T : notnull
{
    public PairFinding(
        Finding<T>? old,
        Finding<T>? @new,
        FindingDifferenceKind difference = FindingDifferenceKind.None,
        bool contentChanged = false,
        string? detail = null)
    {
        if (old is null && @new is null)
            throw new ArgumentException("A PairFinding must have a non-null Old or New side.", nameof(old));

        Old = old;
        New = @new;
        Difference = difference;
        ContentChanged = contentChanged;
        Detail = detail;
    }

    /// <summary>The old-side atom, or null when this pair is an addition. Set only at construction.</summary>
    public Finding<T>? Old { get; }

    /// <summary>The new-side atom, or null when this pair is a removal. Set only at construction.</summary>
    public Finding<T>? New { get; }

    public FindingDifferenceKind Difference { get; init; }

    /// <summary>Marks a both-sides pair as a content change, deriving <see cref="Kind"/> = Changed.</summary>
    public bool ContentChanged { get; init; }

    public string? Detail { get; init; }

    /// <summary>
    /// The transition polarity, derived from the sides (and <see cref="ContentChanged"/> for the
    /// both-present case) so it is always consistent with them.
    /// </summary>
    public PairKind Kind => Old is null
        ? PairKind.Added
        : New is null
            ? PairKind.Removed
            : ContentChanged ? PairKind.Changed : PairKind.Present;

    private Finding<T> Current => New ?? Old
        ?? throw new InvalidOperationException("A PairFinding has neither an Old nor a New side.");

    public FindingSubject Subject => Current.Subject;
    public FindingDescriptor Descriptor => Current.Descriptor;
    public T Payload => Current.Payload;

    IFinding? IPairFinding.Old => Old;
    IFinding? IPairFinding.New => New;

    /// <summary>The signed position delta for a matched pair, or null if one-sided.</summary>
    public int? PositionDelta => Old is not null && New is not null ? New.Position - Old.Position : null;
}

/// <summary>Projections over finding streams shared by producers and the matcher seam.</summary>
public static class FindingExtensions
{
    /// <summary>
    /// Projects an atom stream onto the alignment keys the matcher consumes. Lazy: the ordered
    /// committer materializes once, and a streaming set committer can consume it without an array.
    /// </summary>
    public static IEnumerable<FindingKey> Keys<T>(this IEnumerable<Finding<T>> findings)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(findings);
        return Iterate(findings);

        static IEnumerable<FindingKey> Iterate(IEnumerable<Finding<T>> source)
        {
            foreach (var finding in source)
                yield return finding.Key;
        }
    }
}
