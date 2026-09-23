using System.Collections.Immutable;

namespace CSharpText;

/// <summary>
/// One exact part of a decoded C# source buffer.
/// </summary>
/// <param name="Start">The absolute zero-based UTF-16 start.</param>
/// <param name="Length">The number of UTF-16 code units.</param>
/// <param name="Lines">The one-based inclusive physical lines touched by the span.</param>
public readonly record struct MemberTextPart(
    int Start,
    int Length,
    LineRange Lines)
{
    /// <summary>The absolute exclusive UTF-16 end.</summary>
    public int End => Start + Length;
}

/// <summary>
/// Exact lexical parts for one supported member declaration in the original decoded source text.
/// </summary>
public sealed record MemberTextParts(
    MemberTextPart Member,
    MemberTextPart Declaration,
    ImmutableArray<MemberTextPart> XmlDocumentation,
    ImmutableArray<MemberTextPart> Attributes,
    MemberTextPart Signature,
    MemberTextPart? Body);
