namespace ILInspector.Metadata;

/// <summary>
/// Evidence for the authentic <c>[Flags]</c> rows on one type: how many rows
/// carried the expected marker constructor and value blob, and whether any
/// authentic row did not. Mirrors <see cref="JsonIncludeAttributeEvidence"/>,
/// because the same distinction applies — a malformed authentic row is not
/// absence, and a consumer that folded it into <c>false</c> would project a
/// wire contract from metadata it could not read.
/// </summary>
/// <remarks>
/// <see cref="Count"/> stays a count rather than a boolean so a duplicated
/// authentic row is visible too. <c>[Flags]</c> is
/// <c>AllowMultiple = false</c>, so a second row cannot come from a compiler
/// and cannot be assumed to mean the same thing as the first.
/// </remarks>
public readonly record struct FlagsAttributeEvidence(
    int Count,
    bool HasMalformedRow);
