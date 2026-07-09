namespace ILInspector.Findings;

/// <summary>
/// The thing an finding row is about (a member, an occurrence within a body, an
/// API surface element). Domain-free: <see cref="Key"/> is an opaque stable string
/// the producing layer chooses; <see cref="Display"/> is a human label.
/// </summary>
public sealed record FindingSubject(string Key, string Display);

/// <summary>
/// A typed, reorder-stable vocabulary entry for an finding row, following the
/// Roslyn <c>DiagnosticDescriptor</c> precedent. <see cref="Id"/> is a
/// <c>family.kind</c> const string (e.g. <c>il.op</c>, <c>il.op.moved</c>); the
/// absence of a numeric code is deliberate so descriptors can be added and
/// reordered without renumbering.
/// </summary>
public sealed record FindingDescriptor(string Id, string Title);

/// <summary>
/// Whether an finding row asserts presence or a diff transition. A move is
/// deliberately <em>not</em> a polarity: a moved occurrence keeps
/// <see cref="Present"/> polarity (its content is unchanged) and carries the move
/// on the orthogonal <see cref="FindingDifferenceKind"/> facet.
/// </summary>
public enum FindingKind
{
    /// <summary>Present on both sides (or a single-stream audit fact).</summary>
    Present,

    /// <summary>Present only on the new side.</summary>
    Added,

    /// <summary>Present only on the old side.</summary>
    Removed,

    /// <summary>Matched across sides but with a content change.</summary>
    Changed,
}

/// <summary>
/// The kind of difference a Fold observed for a matched pair, orthogonal to
/// <see cref="FindingKind"/> and to the content-kind carried by
/// <see cref="FindingDescriptor"/>. Consumers select an equivalence by allow-listing
/// classes (see <see cref="FindingEquivalence"/>), which is why this is a
/// separate axis: the same class is folded away by one consumer and is the salient
/// signal for another.
/// </summary>
public enum FindingDifferenceKind
{
    /// <summary>No observable difference for this pair.</summary>
    None,

    /// <summary>
    /// Only incidental encoding differed (short/long form, ldc family). In this
    /// pilot encoding is pre-folded into the occurrence content key, so this class
    /// is reserved for producers that surface raw encoding.
    /// </summary>
    EncodingOnly,

    /// <summary>
    /// Same content, different location (a matched pair with a position delta). For
    /// an ordered stream (IL) this is a real difference; for an unordered set it is
    /// benign. The consuming Fold decides.
    /// </summary>
    Moved,
}

/// <summary>
/// A domain-free identity/location bundle for an occurrence. <see cref="IdentityKey"/>
/// is the canonical content fingerprint (the alignment key); the positions
/// and <see cref="ScopeKey"/> are the structural signals a Fold reads to classify a
/// change (e.g. a move that crossed a scope boundary).
/// </summary>
public sealed record FindingAnchor(
    string IdentityKey,
    int OldIndex = -1,
    int NewIndex = -1,
    string? ScopeKey = null)
{
    /// <summary>The signed position delta for a matched pair, or null if added/removed.</summary>
    public int? IndexDelta
        => OldIndex >= 0 && NewIndex >= 0 ? NewIndex - OldIndex : null;
}

/// <summary>
/// One finding row: the shared envelope every stream (IL diff, C# diff, semantic
/// fact) projects into so cross-stream consumers query a single skeleton. Rich,
/// stream-specific detail rides as an opaque <see cref="Payload"/> for display and is
/// never required to interpret the row.
/// </summary>
public sealed record Finding(
    FindingSubject Subject,
    FindingDescriptor Descriptor,
    FindingKind Kind,
    FindingAnchor Anchor,
    FindingDifferenceKind DifferenceKind = FindingDifferenceKind.None,
    string? Detail = null,
    object? Payload = null);
