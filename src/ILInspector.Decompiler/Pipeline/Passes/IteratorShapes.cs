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

    /// <summary>
    /// The narrow kickoff shape both iterator consumers replace wholesale: the entire body is
    /// <c>return new &lt;M&gt;d__N(state);</c>, where the construction may be decorated by the
    /// captured-parameter/this object initializer (<c>&lt;&gt;3__param</c>, <c>&lt;&gt;4__this</c>).
    /// Anything else means user-visible work precedes or feeds the iterator handoff. The
    /// construction arguments and capture entries must themselves be inert (the state
    /// <see cref="Constant"/> and parameter/this loads), so a side-effecting expression
    /// consumed by the handoff (e.g. <c>new &lt;M&gt;d__0(SideEffect())</c>) is not silently
    /// dropped when the body is replaced. Shared so the reconstruction and acknowledgment
    /// gates cannot drift apart (#1362/#1471).
    /// </summary>
    public static bool IsNarrowKickoffHandoff(IrFunction function, NewObject handoff)
    {
        if (function.Body.Children is not [Block block])
            return false;
        if (block.Children is not [Return { Value: { } returned }])
            return false;

        if (returned is ObjectInitializerExpression initializer)
        {
            if (!ReferenceEquals(initializer.Creation, handoff))
                return false;
            // Children[0] is the Creation; the rest are the capture entries' argument values.
            foreach (var entry in initializer.Children.Skip(1).Cast<IrExpression>())
                if (!IsInertCapture(entry))
                    return false;
        }
        else if (!ReferenceEquals(returned, handoff))
        {
            return false;
        }

        return handoff.Arguments.All(IsInertCapture);
    }

    // The only expressions a real compiler iterator kickoff feeds to the state-machine
    // construction: the state Constant and direct loads of the captured parameters/this.
    static bool IsInertCapture(IrExpression expression)
        => expression is Constant or LoadArgument;
}
