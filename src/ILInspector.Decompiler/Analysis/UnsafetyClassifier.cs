using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Analysis;

/// <summary>
/// Surfaces unsafe operations the C# source states quietly (an
/// <c>unsafe</c> block, a <c>*p</c>, a <c>stackalloc</c>) or hides entirely (a
/// function-pointer call behind a <c>delegate*</c>): pointer dereferences, stack
/// allocation, and indirect calls. A pure, read-only walk over the freshly
/// imported IR, the <see cref="AnnotationStage.Imported"/> stage dual of the
/// allocation classifier — at this altitude these are still literal opcodes.
///
/// Precision over recall: a pointer dereference is reported only when the
/// address is a genuine unmanaged <see cref="TypeRefKind.Pointer"/>, never a safe
/// managed <see cref="TypeRefKind.ByRef"/> (a <c>ref</c> local, <c>in</c>
/// parameter, or <c>Span</c> indexer), so <c>ref</c> code is never flagged.
///
/// Positive-only: it reports the unsafe operations it can see and stays silent
/// otherwise. It never claims a method is safe.
/// </summary>
public sealed class UnsafetyClassifier : IHiddenFactClassifier
{
    static readonly AnnotationDescriptor Deref = new("unsafe.deref", AnnotationCategory.Unsafety, "dereferences a pointer");
    static readonly AnnotationDescriptor StackAlloc = new("unsafe.stackalloc", AnnotationCategory.Unsafety, "allocates on the stack");
    static readonly AnnotationDescriptor Calli = new("unsafe.calli", AnnotationCategory.Unsafety, "calls through a function pointer");

    public AnnotationCategory Category => AnnotationCategory.Unsafety;

    public AnnotationStage Stage => AnnotationStage.Imported;

    public IReadOnlyList<Annotation> Classify(IrFunction function)
    {
        var annotations = new List<Annotation>();

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadIndirect load when IsPointer(load.Address):
                    annotations.Add(Make(Deref, load, IndirectType(load.Type, load.Address)));
                    break;

                case StoreIndirect store when IsPointer(store.Address):
                    annotations.Add(Make(Deref, store, IndirectType(store.Type, store.Address)));
                    break;

                case StackAllocate stackAlloc:
                    annotations.Add(Make(StackAlloc, stackAlloc, "byte*"));
                    break;

                case CallIndirect calli:
                    annotations.Add(Make(Calli, calli, calli.ReturnType.ToDisplayString()));
                    break;
            }
        }

        return annotations;
    }

    static Annotation Make(AnnotationDescriptor descriptor, IrNode node, string? detail)
        => new(descriptor, node.SourceOffset, detail, AnnotationConditionality.Always, node);

    /// <summary>
    /// True when the address is an unmanaged pointer — the line that separates an
    /// unsafe <c>*p</c> from a safe <c>ref</c> dereference (a managed
    /// <see cref="TypeRefKind.ByRef"/>, which this deliberately excludes).
    /// </summary>
    static bool IsPointer(IrExpression address)
        => address.ResultType?.Kind == TypeRefKind.Pointer;

    /// <summary>
    /// The dereferenced type: the indirection type carried by the opcode suffix
    /// (<c>ldind.i4</c> → <c>int</c>) when present — more precise than the
    /// pointer's element type, which can differ after a reinterpreting cast (an
    /// <c>int</c> stored through a lowered <c>byte*</c> scratch buffer) — falling
    /// back to the pointer's element type.
    /// </summary>
    static string? IndirectType(TypeRef? indirectionType, IrExpression address)
        => indirectionType?.ToDisplayString() ?? PointedType(address);

    static string? PointedType(IrExpression address)
        => address.ResultType?.ElementType?.ToDisplayString();
}
