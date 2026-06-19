using System.Text;

namespace ILInspector.Decompiler.Analysis;

/// <summary>
/// The one place that turns a hidden fact into display text, so the C# and IL
/// views read identically — a fact is <c>alloc.box(int)</c> on both, anchored to
/// a statement in C# and to the exact opcode in IL. Format: <c>id(detail)</c>,
/// with the conditionality appended only when it is not the unremarkable
/// <see cref="AnnotationConditionality.Always"/>, so cached-once / per-iteration
/// stand out instead of "always" repeating on every line.
/// </summary>
public static class AnnotationText
{
    public static string Format(Annotation fact)
    {
        var sb = new StringBuilder(fact.Descriptor.Id);
        if (!string.IsNullOrEmpty(fact.Detail))
            sb.Append('(').Append(fact.Detail).Append(')');
        if (fact.Conditionality != AnnotationConditionality.Always)
            sb.Append(' ').Append(Kebab(fact.Conditionality));
        return sb.ToString();
    }

    public static string Format(IReadOnlyList<Annotation> facts)
        => string.Join("; ", facts.Select(Format));

    static string Kebab(AnnotationConditionality conditionality) => conditionality switch
    {
        AnnotationConditionality.CachedOnce => "cached-once",
        AnnotationConditionality.PerIteration => "per-iteration",
        _ => conditionality.ToString().ToLowerInvariant(),
    };
}
