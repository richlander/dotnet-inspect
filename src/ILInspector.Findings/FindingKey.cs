using System.Collections.Immutable;

namespace ILInspector.Findings;

/// <summary>
/// A named soft-correspondence tier. <see cref="Id"/> uniquely names the tier and therefore its
/// configured confidence; two projections with the same ID are not distinct tiers. Confidence is
/// below 100 by construction so the default acceptance threshold remains exact-only.
/// </summary>
public sealed record FindingMatchTier
{
    public FindingMatchTier(string Id, int Confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        if (Confidence is <= 0 or >= 100)
            throw new ArgumentOutOfRangeException(
                nameof(Confidence),
                Confidence,
                "Soft match confidence must be between 1 and 99.");

        this.Id = Id;
        this.Confidence = Confidence;
    }

    public string Id { get; }
    public int Confidence { get; }
}

/// <summary>
/// One producer-owned projection of a structured identity. Two residual observations are soft
/// candidates when tier and projected identity agree while the dropped-facet variant differs.
/// The matcher treats <see cref="Variant"/> as opaque data; its meaning belongs to the producer.
/// </summary>
public sealed record FindingSoftKey
{
    public FindingSoftKey(FindingMatchTier Tier, string IdentityKey, string Variant)
    {
        this.Tier = Tier ?? throw new ArgumentNullException(nameof(Tier));
        ArgumentException.ThrowIfNullOrWhiteSpace(IdentityKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(Variant);
        this.IdentityKey = IdentityKey;
        this.Variant = Variant;
    }

    public FindingMatchTier Tier { get; }
    public string IdentityKey { get; }
    public string Variant { get; }
}

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
        : this(IdentityKey, ScopeKey, [])
    {
    }

    public FindingKey(
        string IdentityKey,
        string? ScopeKey,
        IEnumerable<FindingSoftKey> SoftKeys)
    {
        ArgumentNullException.ThrowIfNull(IdentityKey);
        ArgumentNullException.ThrowIfNull(SoftKeys);

        var softKeys = SoftKeys.ToImmutableArray();
        if (softKeys.Any(key => key is null))
            throw new ArgumentException("Soft keys must not contain null values.", nameof(SoftKeys));
        if (softKeys
            .GroupBy(key => key.Tier.Id, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A finding key may carry at most one projection per soft tier.",
                nameof(SoftKeys));
        }

        this.IdentityKey = IdentityKey;
        this.ScopeKey = ScopeKey;
        this.SoftKeys = softKeys;
    }

    public string IdentityKey { get; }
    public string? ScopeKey { get; }
    public ImmutableArray<FindingSoftKey> SoftKeys { get; }

    public bool Equals(FindingKey other)
        => string.Equals(IdentityKey, other.IdentityKey, StringComparison.Ordinal)
            && string.Equals(ScopeKey, other.ScopeKey, StringComparison.Ordinal)
            && FindingValueEquality.SequenceEqual(SoftKeys, other.SoftKeys);

    public override int GetHashCode()
        => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(IdentityKey),
            ScopeKey is null ? 0 : StringComparer.Ordinal.GetHashCode(ScopeKey),
            FindingValueEquality.SequenceHashCode(SoftKeys));

    public void Deconstruct(out string identityKey, out string? scopeKey)
        => (identityKey, scopeKey) = (IdentityKey, ScopeKey);
}
