using System.Text;

namespace ILInspector.Decompiler.Annotations;

/// <summary>
/// The one place that turns a hidden fact into display text, so the C# and IL
/// views read identically — a fact is <c>alloc.box(int)</c> on both, anchored to
/// a statement in C# and to the exact opcode in IL. Format: <c>id(detail)</c>,
/// with the conditionality appended only when it is not the unremarkable
/// <see cref="AnnotationConditionality.Always"/>, so cached-once / per-iteration
/// stand out instead of "always" repeating on every line.
///
/// Being that one place also makes this the right place to fold line
/// terminators. Every consumer bakes the result into a single-line trailing
/// <c>//</c> comment, and <see cref="IAnnotation.Detail"/> carries untrusted
/// metadata text — a callee name, a type name — which the CLR does not require
/// to be free of line terminators. Callers cannot fold on our behalf: facts are
/// appended to IL comment lines after the IL producer has already folded them,
/// so a terminator arriving through a fact would escape a comment the producer
/// believed it had closed. Folding here covers every current and future
/// consumer of fact text.
/// Gate: <c>AnnotationTextTests</c> and
/// <c>UntrustedIlPresentationTests.AnnotatedSource_HostileFactDetailCannotEscapeItsComment</c>.
/// </summary>
public static class AnnotationText
{
    public static string Format(IAnnotation fact)
    {
        var sb = new StringBuilder(fact.Descriptor.Id);
        if (!string.IsNullOrEmpty(fact.Detail))
            sb.Append('(').Append(fact.Detail).Append(')');
        if (fact.Conditionality != AnnotationConditionality.Always)
            sb.Append(' ').Append(Kebab(fact.Conditionality));
        return sb.ToString().ReplaceLineEndings(" ");
    }

    public static string Format(IReadOnlyList<IAnnotation> facts)
        => string.Join("; ", facts.Select(Format));

    static string Kebab(AnnotationConditionality conditionality) => conditionality switch
    {
        AnnotationConditionality.CachedOnce => "cached-once",
        AnnotationConditionality.PerIteration => "per-iteration",
        _ => conditionality.ToString().ToLowerInvariant(),
    };
}
