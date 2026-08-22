namespace ILInspector.Metadata;

/// <summary>
/// The decoded <c>[JsonIgnore(Condition = ...)]</c> value carried by one
/// authentic attribute row.
/// </summary>
/// <remarks>
/// The members deliberately reuse System.Text.Json's own
/// <c>JsonIgnoreCondition</c> numbering, because the value decoded from
/// metadata is that enum's underlying constant: renumbering here would silently
/// rename every condition. <c>Always</c> is the value a bare
/// <c>[JsonIgnore]</c> carries, matching the attribute's own property default.
/// <c>JsonIgnoreConditionValuesMatchSystemTextJson</c> in
/// <c>ILInspector.Metadata.Tests</c> is the gate that keeps the two in step.
/// </remarks>
public enum JsonWireIgnoreCondition
{
    Never = 0,
    Always = 1,
    WhenWritingDefault = 2,
    WhenWritingNull = 3,

    /// <summary>Ignored when serializing; still read when deserializing.</summary>
    WhenWriting = 4,

    /// <summary>Ignored when deserializing; still written when serializing.</summary>
    WhenReading = 5,
}

/// <summary>
/// Ordered evidence for the authentic <c>[JsonInclude]</c> rows on one member:
/// how many rows carried the expected marker constructor and value blob, and
/// whether any authentic row did not. A malformed row is not absence — the
/// member's opt-in intent is real but its metadata cannot be honored — so
/// consumers must surface it rather than projecting a success-shaped contract.
/// </summary>
public readonly record struct JsonIncludeAttributeEvidence(
    int Count,
    bool HasMalformedRow);
