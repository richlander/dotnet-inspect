namespace QuerySpace;

/// <summary>
/// What makes two shared queries the same query: one vocabulary identity paired
/// with one canonical payload.
/// </summary>
/// <remarks>
/// <para>
/// Equality is decided on the pair and nowhere else. Payload bytes alone are not an
/// identity, because the same bytes under two vocabularies are two different
/// queries: the keys inside them resolve against different namespaces. The
/// vocabulary is therefore never a property of the payload — it travels beside it,
/// as the packet tuple's query ID and as the same pair in any standalone encoding,
/// so it appears exactly once and cannot disagree with itself.
/// </para>
/// <para>
/// Both halves are validated on the way in, and there is no other way in. An
/// identity built from a payload that is merely payload-shaped would compare
/// unequal to the same query's canonical form, which is exactly the deduplication
/// failure the pair exists to prevent, so a payload becomes an identity only by
/// being emitted here or by being confirmed canonical here.
/// </para>
/// <para>
/// Identity is syntactic, like canonical form: two spellings a vocabulary would
/// treat as equal can share as different links. A vocabulary that wants
/// spelling-independent identity normalizes before constructing intent, where the
/// user can see it happen.
/// </para>
/// </remarks>
public sealed record PortableQueryIdentity
{
    private PortableQueryIdentity(string vocabulary, string payload)
    {
        Vocabulary = vocabulary;
        Payload = payload;
    }

    /// <summary>Owner-issued identity of the vocabulary the terms resolve against.</summary>
    public string Vocabulary { get; }

    /// <summary>
    /// The canonical payload, exactly as
    /// <see cref="PortableQueryPayloadCodec.Encode"/> emits it.
    /// </summary>
    public string Payload { get; }

    /// <summary>
    /// Pairs a vocabulary with the canonical payload of an intent.
    /// </summary>
    public static PortableQueryIdentity Create(
        string vocabulary,
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(vocabulary);
        ArgumentNullException.ThrowIfNull(intent);

        return new(
            vocabulary,
            PortableQueryPayloadCodec.Encode(intent, cancellationToken));
    }

    /// <summary>
    /// Pairs a vocabulary with a payload that arrived already encoded — from a
    /// packet's query table, a stored record, or a share link.
    /// </summary>
    /// <exception cref="PortableQueryPayloadException">
    /// The payload is not the one canonical spelling of an admissible intent.
    /// </exception>
    public static PortableQueryIdentity FromCanonicalPayload(
        string vocabulary,
        string payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(vocabulary);
        ArgumentNullException.ThrowIfNull(payload);

        PortableQueryPayloadCodec.Decode(payload, cancellationToken);
        return new(vocabulary, payload);
    }
}
