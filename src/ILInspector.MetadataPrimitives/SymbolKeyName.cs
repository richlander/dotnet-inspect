using InertText;
using InertText.Encoding;

namespace ILInspector.Metadata;

/// <summary>
/// The typed view of a Symbol Key's Name part
/// (<c>docs/design/symbol-key.md#name-ownership-and-inert-path</c>): it retains
/// the part's exact UTF-8 payload as an opaque range without exposing it,
/// answers byte-exact matches, and issues the scoped UTF-8 reader a
/// presentation consumer contains through <see cref="InertUtf8String"/>. It
/// never decodes, validates, or visually encodes the name.
/// </summary>
public readonly struct SymbolKeyName : IEquatable<SymbolKeyName>
{
    readonly OpaqueByteRange _name;

    internal SymbolKeyName(OpaqueByteRange name) => _name = name;

    public int Length => _name.Length;

    /// <summary>Whether the stored name equals the caller's UTF-8 query, byte for byte.</summary>
    public bool ValueTextEquals(ReadOnlySpan<byte> utf8Text) => _name.SequenceEqual(utf8Text);

    /// <summary>
    /// The scoped reader a matcher queries or a presentation consumer contains;
    /// the view, as the domain's text classification, is its issuer.
    /// </summary>
    public Utf8RangeReader Read() => Utf8RangeClassification.Read(_name);

    public bool Equals(SymbolKeyName other) => _name.Equals(other._name);

    public override bool Equals(object? obj) => obj is SymbolKeyName other && Equals(other);

    public override int GetHashCode() => _name.GetHashCode();

    public static bool operator ==(SymbolKeyName left, SymbolKeyName right) => left.Equals(right);

    public static bool operator !=(SymbolKeyName left, SymbolKeyName right) => !left.Equals(right);
}
