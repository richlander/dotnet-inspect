using System.Runtime.CompilerServices;

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
/// payload type. Each concrete case (<see cref="Added{T}"/>, <see cref="Removed{T}"/>,
/// <see cref="Present{T}"/>, <see cref="Changed{T}"/>) fixes its own <see cref="Kind"/> and exposes
/// exactly the sides it has via <see cref="Old"/>/<see cref="New"/>.
/// </summary>
public interface IPairFinding : IFinding
{
    PairKind Kind { get; }
    FindingDifferenceKind Difference { get; }
    IFinding? Old { get; }
    IFinding? New { get; }
}

/// <summary>
/// A classified transition between two <see cref="Finding{T}"/> atoms — the diff row, modeled as a
/// discriminated union (.NET 11 <c>[Union]</c>) over four nested cases (<see cref="Added"/>,
/// <see cref="Removed"/>, <see cref="Present"/>, <see cref="Changed"/>). Because each case carries
/// exactly the atoms it has, an inconsistent transition — a polarity that disagrees with its sides,
/// or a pair with neither side — is <em>unrepresentable by construction</em>. There is no invariant
/// to enforce and nothing a <c>with</c> expression can desync: the closed set of cases is the
/// invariant. A reference union (a <c>record class</c>, not the struct sugar) so there is no
/// <c>default</c>/empty state to guard and no boxing when a case is viewed as <see cref="IPairFinding"/>.
/// <para>
/// The cases are <em>nested</em> so their four otherwise-generic names stay scoped to the union
/// (<c>PairFinding&lt;T&gt;.Present</c>) rather than claiming the producer-facing namespace, and each
/// inherits <typeparamref name="T"/> from the enclosing union. Pattern-match the union itself
/// (<c>pair switch { PairFinding&lt;T&gt;.Added … }</c>, not <c>pair.Value switch</c>) for
/// compiler-checked exhaustiveness; a fifth case then breaks the build rather than falling through.
/// </para>
/// </summary>
[Union]
public sealed record PairFinding<T> : IPairFinding
    where T : notnull
{
    public PairFinding(Added value) => Value = Guard(value);
    public PairFinding(Removed value) => Value = Guard(value);
    public PairFinding(Present value) => Value = Guard(value);
    public PairFinding(Changed value) => Value = Guard(value);

    // A constructed wrapper always holds a case. (A `default`/null PairFinding<T> reference is a
    // separate, NRT-visible concern inherent to any reference type, not an empty union state.)
    static IPairFinding Guard(IPairFinding value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }

    /// <summary>The active case: <see cref="Added"/>, <see cref="Removed"/>, <see cref="Present"/>, or <see cref="Changed"/>.</summary>
    public object? Value { get; }

    private IPairFinding Case => (IPairFinding)Value!;

    public PairKind Kind => Case.Kind;
    public FindingDifferenceKind Difference => Case.Difference;
    public FindingSubject Subject => Case.Subject;
    public FindingDescriptor Descriptor => Case.Descriptor;
    public string? Detail => Case.Detail;
    IFinding? IPairFinding.Old => Case.Old;
    IFinding? IPairFinding.New => Case.New;

    /// <summary>A transition present only on the new side: a one-sided addition.</summary>
    public sealed record Added(Finding<T> New, string? Detail = null) : IPairFinding
    {
        // A null-forgiving (`null!`) caller could otherwise slip a missing atom past the non-null
        // annotation and reintroduce a case with no side; guard the required atom at construction.
        public Finding<T> New { get; } = New ?? throw new ArgumentNullException(nameof(New));

        public PairKind Kind => PairKind.Added;
        public FindingDifferenceKind Difference => FindingDifferenceKind.None;
        public FindingSubject Subject => New.Subject;
        public FindingDescriptor Descriptor => New.Descriptor;
        IFinding? IPairFinding.Old => null;
        IFinding? IPairFinding.New => New;
    }

    /// <summary>A transition present only on the old side: a one-sided removal.</summary>
    public sealed record Removed(Finding<T> Old, string? Detail = null) : IPairFinding
    {
        public Finding<T> Old { get; } = Old ?? throw new ArgumentNullException(nameof(Old));

        public PairKind Kind => PairKind.Removed;
        public FindingDifferenceKind Difference => FindingDifferenceKind.None;
        public FindingSubject Subject => Old.Subject;
        public FindingDescriptor Descriptor => Old.Descriptor;
        IFinding? IPairFinding.Old => Old;
        IFinding? IPairFinding.New => null;
    }

    /// <summary>
    /// A matched pair whose content is unchanged. May still carry a non-structural
    /// <see cref="Difference"/> (e.g. <see cref="FindingDifferenceKind.Moved"/>): a move keeps Present
    /// polarity because the content is the same, only the location differs.
    /// </summary>
    public sealed record Present(
        Finding<T> Old,
        Finding<T> New,
        FindingDifferenceKind Difference = FindingDifferenceKind.None,
        string? Detail = null) : IPairFinding
    {
        public Finding<T> Old { get; } = Old ?? throw new ArgumentNullException(nameof(Old));
        public Finding<T> New { get; } = New ?? throw new ArgumentNullException(nameof(New));

        public PairKind Kind => PairKind.Present;
        public FindingSubject Subject => New.Subject;
        public FindingDescriptor Descriptor => New.Descriptor;
        IFinding? IPairFinding.Old => Old;
        IFinding? IPairFinding.New => New;
    }

    /// <summary>A matched pair whose content changed across the two sides.</summary>
    public sealed record Changed(
        Finding<T> Old,
        Finding<T> New,
        FindingDifferenceKind Difference = FindingDifferenceKind.None,
        string? Detail = null) : IPairFinding
    {
        public Finding<T> Old { get; } = Old ?? throw new ArgumentNullException(nameof(Old));
        public Finding<T> New { get; } = New ?? throw new ArgumentNullException(nameof(New));

        public PairKind Kind => PairKind.Changed;
        public FindingSubject Subject => New.Subject;
        public FindingDescriptor Descriptor => New.Descriptor;
        IFinding? IPairFinding.Old => Old;
        IFinding? IPairFinding.New => New;
    }
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
