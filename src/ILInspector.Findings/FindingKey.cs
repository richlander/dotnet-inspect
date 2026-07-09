namespace ILInspector.Findings;

/// <summary>
/// The domain-free alignment key the <see cref="FindingMatcher"/> operates on: a content
/// fingerprint plus an optional structural scope tag. Deliberately a value type carrying no
/// payload — the matcher never needs the domain detail, so keeping the payload out of the core
/// keeps the matcher monomorphic (no generic instantiation, no boxing, no interface dispatch in
/// the hot loop). A producer feeds keys projected from its typed
/// <see cref="FindingOccurrence{T}"/> stream (see <c>ToKeys</c>).
/// </summary>
/// <param name="IdentityKey">
/// The canonical content fingerprint. Two occurrences are candidate "same thing" matches iff
/// their identity keys are equal, so the producing layer must fold all incidental encoding
/// (short/long form, register renumbering) into this key.
/// </param>
/// <param name="ScopeKey">
/// An optional structural scope tag (enclosing loop/try/region); a corroborating signal for move
/// detection. Null when unknown or not modeled.
/// </param>
public readonly record struct FindingKey(string IdentityKey, string? ScopeKey = null);
