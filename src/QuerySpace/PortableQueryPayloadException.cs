namespace QuerySpace;

/// <summary>
/// Why a payload or an intent was refused.
/// </summary>
/// <remarks>
/// A closed set. Every rejection the codec can produce is one of these, and the
/// witnesses in <c>docs/design/models/portable-query-payload/vectors.json</c> name
/// them by the texts <see cref="PortableQueryPayloadFailure.TextOf"/> spells.
/// </remarks>
public enum PortableQueryPayloadFailureKind
{
    /// <summary>The text is not JSON.</summary>
    Malformed,

    /// <summary>The payload is not a JSON object.</summary>
    NotAnObject,

    /// <summary>A property the closed shape does not declare.</summary>
    UnknownProperty,

    /// <summary>One property spelled twice.</summary>
    DuplicateProperty,

    /// <summary>An empty part on the wire; canonical form never contains one.</summary>
    EmptyPart,

    /// <summary>A tuple, or a part, whose shape is not the declared one.</summary>
    BadArity,

    /// <summary>A number outside the declared domain or spelled non-canonically.</summary>
    BadInteger,

    /// <summary><c>null</c> anywhere but a window bound.</summary>
    NullOutsideWindow,

    /// <summary>A token no model identity spells.</summary>
    UnknownToken,

    /// <summary>An identity naming nothing.</summary>
    EmptyIdentity,

    /// <summary>A lone surrogate, which is refused rather than repaired.</summary>
    UnpairedSurrogate,

    /// <summary>A declared maximum exceeded, charged as parsed.</summary>
    LimitExceeded,

    /// <summary>One dimension bounded twice.</summary>
    RepeatedBoundDimension,

    /// <summary>A closed window whose end precedes its start.</summary>
    WindowUnordered,

    /// <summary>A ranking role naming a stage that is not the ranking kind.</summary>
    RoleNotTop,

    /// <summary>The baseline role twice, or one ranking stage claimed twice.</summary>
    DuplicateRole,

    /// <summary>Structurally valid bytes that are not the one canonical spelling.</summary>
    NonCanonical
}

/// <summary>
/// The wire texts naming each <see cref="PortableQueryPayloadFailureKind"/>.
/// </summary>
/// <remarks>
/// These texts are how the normative vectors name a rejection, so they are written
/// out rather than derived from member names: renaming a member must not silently
/// retarget a witness.
/// </remarks>
public static class PortableQueryPayloadFailure
{
    /// <summary>Spells one failure kind.</summary>
    public static string TextOf(PortableQueryPayloadFailureKind kind) => kind switch
    {
        PortableQueryPayloadFailureKind.Malformed => "malformed",
        PortableQueryPayloadFailureKind.NotAnObject => "not-an-object",
        PortableQueryPayloadFailureKind.UnknownProperty => "unknown-property",
        PortableQueryPayloadFailureKind.DuplicateProperty => "duplicate-property",
        PortableQueryPayloadFailureKind.EmptyPart => "empty-part",
        PortableQueryPayloadFailureKind.BadArity => "bad-arity",
        PortableQueryPayloadFailureKind.BadInteger => "bad-integer",
        PortableQueryPayloadFailureKind.NullOutsideWindow => "null-outside-window",
        PortableQueryPayloadFailureKind.UnknownToken => "unknown-token",
        PortableQueryPayloadFailureKind.EmptyIdentity => "empty-identity",
        PortableQueryPayloadFailureKind.UnpairedSurrogate => "unpaired-surrogate",
        PortableQueryPayloadFailureKind.LimitExceeded => "limit-exceeded",
        PortableQueryPayloadFailureKind.RepeatedBoundDimension => "repeated-bound-dimension",
        PortableQueryPayloadFailureKind.WindowUnordered => "window-unordered",
        PortableQueryPayloadFailureKind.RoleNotTop => "role-not-top",
        PortableQueryPayloadFailureKind.DuplicateRole => "duplicate-role",
        PortableQueryPayloadFailureKind.NonCanonical => "non-canonical",
        _ => throw PortableQueryModel.Undefined(kind, nameof(kind))
    };

    /// <summary>Reads one failure kind, or reports that no kind has this text.</summary>
    public static bool TryParse(
        string text,
        out PortableQueryPayloadFailureKind kind)
    {
        foreach (PortableQueryPayloadFailureKind candidate
            in Enum.GetValues<PortableQueryPayloadFailureKind>())
        {
            if (string.Equals(TextOf(candidate), text, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}

/// <summary>
/// One all-or-nothing payload rejection. No partial intent accompanies it.
/// </summary>
public sealed class PortableQueryPayloadException : Exception
{
    public PortableQueryPayloadException(
        PortableQueryPayloadFailureKind kind,
        string message)
        : base(message) => Kind = kind;

    public PortableQueryPayloadException(
        PortableQueryPayloadFailureKind kind,
        string message,
        Exception innerException)
        : base(message, innerException) => Kind = kind;

    public PortableQueryPayloadFailureKind Kind { get; }
}
