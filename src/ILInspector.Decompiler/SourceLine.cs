namespace ILInspector.Decompiler;

using ILInspector.Decompiler.Annotations;

/// <summary>
/// Which language a line came from. This is the only discriminator the
/// correlation layer needs: each medium pretty-prints its own lines (indent,
/// braces, IL alignment, inline annotations already live in the text), so the
/// merge owns nothing but the cross-medium framing — and framing an IL line
/// differs from a C# line by exactly this bit.
/// </summary>
public enum SourceLineKind
{
    /// <summary>A C# body line, already indented and braced by the C# producer.</summary>
    CSharp,

    /// <summary>An IL instruction line, already aligned by the IL producer.</summary>
    Il,
}

/// <summary>
/// Fast-path anchored line: the pretty-printed <paramref name="Text"/> plus the IL
/// <paramref name="Offset"/> that anchors it (<c>-1</c> when the line owns no IL,
/// e.g. a brace or blank). Medium-neutral — it serves both the C# and IL fast
/// paths, because both are just display-ready text plus an anchor. A value type
/// with no Kind and no annotations, for the scalar "just give me the body" render
/// where the offset is the only structure a downstream anchor needs. The
/// correlation that buckets IL onto lines lives in the producer, so this carries a
/// point anchor, not a range; the printer is a trivial joiner.
/// </summary>
public readonly record struct SourceLine(string Text, int Offset);

/// <summary>
/// Rich line currency for the correlation layer: an ordered
/// <see cref="AnnotatedSourceLine"/> stream is the interleave substrate. Each line
/// carries its bare <paramref name="Text"/>, an anchoring IL <paramref name="Offset"/>
/// (<c>-1</c> when unanchored), its <paramref name="Kind"/>
/// (<see cref="SourceLineKind.CSharp"/> vs <see cref="SourceLineKind.Il"/> — the one
/// bit the merge printer frames on), and any <paramref name="Annotations"/> that
/// resolved onto it. Annotations stay <em>structured</em> (not baked into the text)
/// so the merge printer has placement freedom the fast path's pre-baked text can't
/// give. A producer emits the already-ordered stream — owning the offset-range
/// correlation internally — and a printer renders it, framing each line by
/// <see cref="Kind"/> and reading indentation straight from the C# line's leading
/// whitespace. This is the <c>correlate → render</c> substrate; the point
/// <see cref="Offset"/> keeps each line addressable for diff, body subset, and the
/// mixed IL+C# view.
/// </summary>
public sealed record AnnotatedSourceLine(
    string Text,
    int Offset,
    SourceLineKind Kind,
    IReadOnlyList<IAnnotation> Annotations)
{
    /// <summary>A line with no annotations attached.</summary>
    public AnnotatedSourceLine(string Text, int Offset, SourceLineKind Kind)
        : this(Text, Offset, Kind, [])
    {
    }
}
