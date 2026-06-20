namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Shared shape recognition for compiler-generated iterator state machines, used
/// by <see cref="IteratorReconstructionPass"/> (raise the yields) and
/// <see cref="IteratorAcknowledgmentPass"/> (honest fallback when reconstruction
/// declines). A kickoff is a method returning <c>IEnumerable</c>/<c>IEnumerator</c>
/// (<c>System.Collections[.Generic]</c>) on the user's own type whose body
/// constructs a <c>&lt;Method&gt;d__N</c> state machine; the state machine's own
/// <c>MoveNext</c>/<c>GetEnumerator</c> also construct/return a <c>&lt;X&gt;d__</c>,
/// so the declaring-type guard excludes them.
/// </summary>
internal static class IteratorShapes
{
    public static bool TryGetKickoff(IrFunction function, out NewObject handoff)
    {
        handoff = null!;
        if (IsStateMachineType(function.DeclaringType))
            return false;
        if (!ReturnsEnumerable(function.Signature.ReturnType))
            return false;

        var creation = function.Descendants.OfType<NewObject>()
            .FirstOrDefault(n => GeneratedCodeIdentity.IsIteratorStateMachineConstructor(n.Constructor));
        if (creation is null)
            return false;

        handoff = creation;
        return true;
    }

    public static bool IsStateMachineType(TypeRef type)
        => GeneratedCodeIdentity.IsIteratorStateMachineTypeName(type);

    public static string MetadataName(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType?.Name ?? "" : type.Name;

    static string Namespace(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType?.Namespace ?? "" : type.Namespace;

    static bool ReturnsEnumerable(TypeRef type)
    {
        var ns = Namespace(type);
        if (ns is not ("System.Collections" or "System.Collections.Generic"))
            return false;
        var name = MetadataName(type);
        return name.StartsWith("IEnumerable", StringComparison.Ordinal)
            || name.StartsWith("IEnumerator", StringComparison.Ordinal);
    }
}
