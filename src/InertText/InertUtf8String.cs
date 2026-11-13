using System.Buffers;
using System.Text;

using InertText.Encoding;

namespace InertText;

/// <summary>
/// Policy-contained UTF-8 text that can be written to a byte sink without a UTF-16 projection.
/// </summary>
public readonly struct InertUtf8String : IEquatable<InertUtf8String>
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly OpaqueByteRange _text;

    /// <summary>Contains the classified source under the selected policy.</summary>
    public InertUtf8String(TextPolicy policy, Utf8RangeReader value) =>
        this = Utf8VisualEncoder.Encode(policy, value);

    internal InertUtf8String(
        OpaqueByteRange text,
        VisualForm forms,
        TextConcern concerns,
        bool truncated = false)
    {
        _text = text;
        Forms = forms;
        Concerns = concerns;
        IsTruncated = truncated;
    }

    /// <summary>The empty contained UTF-8 value.</summary>
    public static InertUtf8String Empty { get; } =
        new(default, VisualForm.None, TextConcern.None);

    /// <summary>The spellings emitted while containing the source.</summary>
    public VisualForm Forms { get; }

    /// <summary>The concerns that required containment.</summary>
    public TextConcern Concerns { get; }

    /// <summary>Whether producing this value dropped content to fit a budget.</summary>
    /// <remarks>
    /// No operation bounds a UTF-8 value yet, so this is false until a byte budget arrives; it
    /// already participates in equality and the <see cref="InertString"/> projection.
    /// </remarks>
    public bool IsTruncated { get; }

    /// <summary>Whether the contained value has no bytes.</summary>
    public bool IsEmpty => _text.IsEmpty;

    /// <summary>The number of bytes in the contained UTF-8 spelling.</summary>
    public int Length => _text.Length;

    /// <summary>Whether containment changed the source spelling.</summary>
    public bool WasEncoded => Forms != VisualForm.None;

    /// <summary>Whether a concerning scalar or ill-formed source byte required containment.</summary>
    public bool RequiredContainment => Concerns != TextConcern.None;

    /// <summary>Writes the contained UTF-8 spelling directly to destination-owned storage.</summary>
    public void WriteTo(IBufferWriter<byte> destination)
    {
        // The bytes are already contained, so this is the exact transfer.
        OpaqueByteRangeEncoder.Encode(_text, destination);
    }

    /// <summary>Projects the same contained spelling and evidence to the UTF-16 currency.</summary>
    public InertString ToInertString() => new(this);

    /// <summary>Returns the contained spelling as UTF-16 for a string sink.</summary>
    public override string ToString() => ToInertString().ToString();

    internal string ToUtf16String() => StrictUtf8.GetString(_text.Bytes);

    /// <inheritdoc />
    /// <remarks>
    /// Matches <see cref="InertString.Equals(InertString)"/>: the contained bytes and
    /// <see cref="IsTruncated"/> participate, and <see cref="Forms"/> and <see cref="Concerns"/>
    /// do not, because the encoder emits one canonical spelling and both are a function of it.
    /// </remarks>
    public bool Equals(InertUtf8String other) =>
        _text.Equals(other._text)
        && IsTruncated == other.IsTruncated;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is InertUtf8String other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(_text, IsTruncated);

    /// <summary>Tests contained UTF-8 and truncation equality.</summary>
    public static bool operator ==(
        InertUtf8String left,
        InertUtf8String right) =>
        left.Equals(right);

    /// <summary>Tests contained UTF-8 or truncation inequality.</summary>
    public static bool operator !=(
        InertUtf8String left,
        InertUtf8String right) =>
        !left.Equals(right);
}
