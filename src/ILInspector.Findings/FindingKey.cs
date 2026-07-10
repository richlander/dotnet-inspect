namespace ILInspector.Findings;

/// <summary>
/// The domain-free alignment key the <see cref="FindingMatcher"/> operates on: a content
/// fingerprint plus an optional structural scope tag. Deliberately a value type carrying no
/// payload — the matcher never needs the domain detail, so keeping the payload out of the core
/// keeps the matcher monomorphic (no generic instantiation, no boxing). The matcher accepts a
/// lazy <see cref="System.Collections.Generic.IEnumerable{T}"/> of keys and materializes the
/// ordered path once to a concrete <see cref="FindingKey"/> array, so the LCS hot loop indexes an
/// array directly rather than through an interface. A producer feeds keys projected from its
/// typed <see cref="Finding{T}"/> stream (see <c>Keys</c>).
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
public readonly record struct FindingKey
{
    public FindingKey(string IdentityKey, string? ScopeKey = null)
    {
        ArgumentNullException.ThrowIfNull(IdentityKey);
        this.IdentityKey = IdentityKey;
        this.ScopeKey = ScopeKey;
    }

    public string IdentityKey { get; }
    public string? ScopeKey { get; }

    public void Deconstruct(out string identityKey, out string? scopeKey)
        => (identityKey, scopeKey) = (IdentityKey, ScopeKey);
}
