namespace ILInspector.Decompiler.Annotations;

// Reporting gestures: how an annotation is *shown*, as opposed to what it *is*.
//
// An annotation is data — a fact keyed to a location. Annotations.cs holds that
// data to a deliberate contract: no severity, no Location, no code fix, because
// "annotations describe, they never grade." That contract is precisely why the
// gesture cannot live on the annotation: deciding a fact is worth acting on *is*
// a grade, and it is a property of the report, not of the fact.
//
// So gesture is chosen here, at render time, per view. The same datum renders as
// a side comment in one view and a caret in another without the fact changing.

/// <summary>
/// How an annotation is rendered onto a source view. A reporting-layer choice,
/// deliberately not a member of <see cref="IAnnotation"/>: the annotation model
/// is positive-only and never grades, while choosing the caret gesture is
/// exactly the judgement "this one is worth acting on".
/// </summary>
public enum AnnotationGesture
{
    /// <summary>
    /// A trailing <c>// …</c> comment to the right of the annotated line — the
    /// "interesting, FYI" gesture. The natural home for offset-anchored
    /// descriptive facts, which carry no character span for a caret to point at.
    /// </summary>
    Side,

    /// <summary>
    /// A <c>^^^^</c> underline on the following <c>//</c> line — the "act on
    /// this" gesture. The natural home for span-carrying diagnostics, and the
    /// gesture a descriptive fact is promoted to when a view wants it acted on.
    /// </summary>
    Caret,
}

/// <summary>
/// Chooses a <see cref="AnnotationGesture"/> for each annotation on a render.
/// This is the "reporting" half of the data/reporting split: the fact stream is
/// produced once and describes, and a selector decides — per view, per
/// invocation — which of those facts are promoted from <see cref="AnnotationGesture.Side"/>
/// to <see cref="AnnotationGesture.Caret"/>.
/// </summary>
public sealed class AnnotationGestureSelector
{
    readonly Func<IAnnotation, AnnotationGesture> selector;

    AnnotationGestureSelector(Func<IAnnotation, AnnotationGesture> selector)
        => this.selector = selector;

    /// <summary>
    /// The default report: every fact is a side comment. Preserves the
    /// historical rendering exactly, so a view that asks for no promotion is
    /// byte-identical to one written before gestures existed.
    /// </summary>
    public static AnnotationGestureSelector SideOnly { get; } = new(_ => AnnotationGesture.Side);

    /// <summary>
    /// Promotes to <see cref="AnnotationGesture.Caret"/> every annotation whose
    /// <see cref="AnnotationDescriptor.Category"/> or <see cref="AnnotationDescriptor.Id"/>
    /// matches <paramref name="focus"/>; everything else stays
    /// <see cref="AnnotationGesture.Side"/>. Matching an id by prefix lets
    /// <c>alloc</c> select the whole <c>alloc.*</c> family without naming each
    /// descriptor.
    /// </summary>
    /// <param name="focus">
    /// A category name (e.g. <c>allocation</c>), a descriptor id (e.g.
    /// <c>alloc.box</c>), or a dotted id prefix (e.g. <c>alloc</c>). Compared
    /// case-insensitively. Null or blank yields <see cref="SideOnly"/>.
    /// </param>
    public static AnnotationGestureSelector Focus(string? focus)
    {
        if (string.IsNullOrWhiteSpace(focus))
            return SideOnly;

        string wanted = focus.Trim();
        return new(annotation => Matches(annotation, wanted)
            ? AnnotationGesture.Caret
            : AnnotationGesture.Side);
    }

    static bool Matches(IAnnotation annotation, string focus)
    {
        var descriptor = annotation.Descriptor;
        if (string.Equals(descriptor.Category.ToString(), focus, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(descriptor.Id, focus, StringComparison.OrdinalIgnoreCase))
            return true;

        // Dotted-prefix match, on a segment boundary only: "alloc" selects
        // "alloc.box" but must not select "allocator.x".
        return descriptor.Id.Length > focus.Length
            && descriptor.Id[focus.Length] == '.'
            && descriptor.Id.AsSpan(0, focus.Length).Equals(focus, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The gesture to report <paramref name="annotation"/> with.</summary>
    public AnnotationGesture For(IAnnotation annotation) => selector(annotation);

    /// <summary>True when no annotation in <paramref name="annotations"/> is promoted.</summary>
    public bool AllSide(IReadOnlyList<IAnnotation> annotations)
    {
        foreach (var annotation in annotations)
            if (For(annotation) == AnnotationGesture.Caret)
                return false;
        return true;
    }
}
