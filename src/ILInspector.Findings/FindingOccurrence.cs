using System.Collections.Immutable;

namespace ILInspector.Findings;

/// <summary>
/// A single domain-free item within an ordered stream, presented as an alignment
/// <see cref="Key"/> to the <see cref="FindingMatcher"/> for cross-version matching, and (with
/// its typed <see cref="Payload"/>) to <see cref="FindingFold"/>. Domain producers project their
/// nodes (IL operations, IR nodes, API members) into this shape, choosing the payload type they
/// want carried onto the produced <see cref="Finding{T}"/>.
/// </summary>
/// <param name="IdentityKey">The canonical content fingerprint (see <see cref="FindingKey"/>).</param>
/// <param name="ScopeKey">Optional structural scope tag; null when unknown or not modeled.</param>
/// <param name="Payload">
/// Typed domain detail carried onto the produced <see cref="Finding{T}"/> for display. Never
/// inspected by the matcher, which sees only <see cref="Key"/>.
/// </param>
public sealed record FindingOccurrence<T>(
    string IdentityKey,
    string? ScopeKey = null,
    T? Payload = default)
{
    /// <summary>The alignment key the matcher consumes.</summary>
    public FindingKey Key => new(IdentityKey, ScopeKey);
}

/// <summary>Helpers for projecting a typed occurrence stream onto the matcher's key view.</summary>
public static class FindingOccurrenceExtensions
{
    /// <summary>Projects a typed occurrence stream onto the alignment keys the matcher consumes.</summary>
    public static ImmutableArray<FindingKey> ToKeys<T>(this IReadOnlyList<FindingOccurrence<T>> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        var builder = ImmutableArray.CreateBuilder<FindingKey>(occurrences.Count);
        foreach (var occurrence in occurrences)
            builder.Add(occurrence.Key);

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Projects a typed occurrence stream onto the alignment keys the matcher consumes. This
    /// overload takes <see cref="ImmutableArray{T}"/> directly so the product path does not box
    /// the struct to <see cref="IReadOnlyList{T}"/> or enumerate through the boxed interface.
    /// </summary>
    public static ImmutableArray<FindingKey> ToKeys<T>(this ImmutableArray<FindingOccurrence<T>> occurrences)
    {
        var builder = ImmutableArray.CreateBuilder<FindingKey>(occurrences.Length);
        foreach (var occurrence in occurrences)
            builder.Add(occurrence.Key);

        return builder.MoveToImmutable();
    }
}
