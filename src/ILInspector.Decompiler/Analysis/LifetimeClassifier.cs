using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Analysis;

/// <summary>
/// Surfaces ref/span lifetime facts the C# source leaves implicit — the
/// constraints behind the "may expose referenced variables outside their scope"
/// errors (CS8350/CS8352) that are hard to grasp because the rule is invisible at
/// the use site. A pure, read-only walk over the freshly imported IR.
///
/// Descriptive, never a verifier: it only sees code the compiler already
/// accepted, so it states what IS true of the lifetimes here — a span lives on
/// the stack, a return borrows — and never flags an error (there are none to
/// see).
///
/// Precision over recall: it reports only facts it can decide structurally — a
/// by-ref return, a stackalloc-backed span, a ref-struct return — and stays
/// silent on the heuristic borrow relationships it cannot prove.
/// </summary>
public sealed class LifetimeClassifier : IHiddenFactClassifier
{
    static readonly AnnotationDescriptor RefReturn = new("lifetime.ref-return", AnnotationCategory.Lifetime, "returns a reference borrowing from an input or field");
    static readonly AnnotationDescriptor StackBound = new("lifetime.stack-bound", AnnotationCategory.Lifetime, "span backed by stack memory — cannot escape the frame");
    static readonly AnnotationDescriptor RefStructReturn = new("lifetime.ref-struct-return", AnnotationCategory.Lifetime, "returns a ref struct — lifetime-constrained, cannot be boxed or stored on the heap");

    public AnnotationCategory Category => AnnotationCategory.Lifetime;

    public AnnotationStage Stage => AnnotationStage.Imported;

    public IReadOnlyList<Annotation> Classify(IrFunction function)
    {
        var annotations = new List<Annotation>();

        var returnType = function.Signature.ReturnType;
        var returnFact = returnType.Kind == TypeRefKind.ByRef
            ? RefReturn
            : IsRefStruct(returnType) ? RefStructReturn : null;
        string? returnDetail = returnType.Kind == TypeRefKind.ByRef
            ? returnType.ElementType?.ToDisplayString()
            : returnType.ToDisplayString();

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                // The return-site facts ride the actual `return` statements, so
                // they land on `return ref a[0];` in C# and the `ret` in IL.
                case Return { Children.Count: > 0 } ret when returnFact is not null:
                    annotations.Add(Make(returnFact, ret, returnDetail));
                    break;

                // A Span/ReadOnlySpan constructed over a stackalloc: the source
                // says `Span<int> s = stackalloc int[4]`, hiding that the span's
                // memory is the current frame and so cannot escape it.
                case NewObject newObject when IsStackBoundSpan(newObject):
                    annotations.Add(Make(StackBound, newObject, newObject.Constructor.DeclaringType.ToDisplayString()));
                    break;
            }
        }

        return annotations;
    }

    static Annotation Make(AnnotationDescriptor descriptor, IrNode node, string? detail)
        => new(descriptor, node.SourceOffset, detail, AnnotationConditionality.Always, node);

    static bool IsStackBoundSpan(NewObject newObject)
        => IsRefStruct(newObject.Constructor.DeclaringType)
            && newObject.Descendants.OfType<StackAllocate>().Any();

    /// <summary>
    /// True for the framework ref structs <c>Span&lt;T&gt;</c> /
    /// <c>ReadOnlySpan&lt;T&gt;</c> — recognised by name because the
    /// <c>IsByRefLike</c> attribute lives on the (cross-assembly) type definition,
    /// not the <see cref="TypeRef"/>. Matching <c>System</c> avoids colliding with
    /// an unrelated user type of the same simple name.
    /// </summary>
    static bool IsRefStruct(TypeRef type)
    {
        var (simpleName, ns) = type.Kind == TypeRefKind.GenericInstance
            ? (StripArity(type.ElementType?.Name), type.ElementType?.Namespace)
            : (StripArity(type.Name), type.Namespace);
        return ns == "System" && simpleName is "Span" or "ReadOnlySpan";
    }

    static string StripArity(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }
}
