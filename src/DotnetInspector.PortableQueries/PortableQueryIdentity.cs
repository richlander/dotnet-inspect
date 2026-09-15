namespace DotnetInspector.PortableQueries;

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
/// Identity is syntactic, like canonical form: two spellings a vocabulary would
/// treat as equal can share as different links. A vocabulary that wants
/// spelling-independent identity normalizes before constructing intent, where the
/// user can see it happen.
/// </para>
/// </remarks>
/// <param name="Vocabulary">Owner-issued identity of the vocabulary the terms resolve against.</param>
/// <param name="Payload">The canonical payload, exactly as <see cref="PortableQueryPayloadCodec.Encode"/> emits it.</param>
public readonly record struct PortableQueryIdentity(string Vocabulary, string Payload)
{
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
}
