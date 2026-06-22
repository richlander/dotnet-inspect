namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises direct <c>System.ValueTuple&lt;...&gt;</c> constructor calls into C#
/// tuple literals. This covers the compiler's ordinary tuple-creation lowering:
/// <c>(a, b)</c> becomes <c>new ValueTuple&lt;T1, T2&gt;(a, b)</c>. Arities 2-7 map
/// one constructor to one literal; an eight-or-more element tuple uses the nested
/// <c>TRest</c> representation — <c>(a, …, h)</c> is
/// <c>new ValueTuple&lt;T1..T7, ValueTuple&lt;T8&gt;&gt;(a, …, g, new ValueTuple&lt;T8&gt;(h))</c>
/// — which this pass flattens back into one <c>(a, …, h)</c> literal. The rest is
/// only flattened when it is constructed inline; a non-inline eighth argument (a
/// pre-existing <c>ValueTuple</c> value) is a hand-written construction, not a
/// literal, and stays a constructor.
/// </summary>
public sealed class TupleCreationPass : IIrPass
{
    public string Name => "tuple-creation";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            // A rest tuple's inner ValueTuple nodes are flattened into their outer
            // literal, so by the time the snapshot reaches them they are detached.
            if (newObject.Parent is null || newObject.Parent is ExpressionStatement)
                continue;
            if (TupleLiteralElements(newObject) is not { } elements)
                continue;

            // Validation is complete and non-mutating; now move the gathered leaf
            // elements out of their (possibly nested) constructors into the literal.
            foreach (var element in elements)
                element.Detach();
            var tuple = new TupleExpression(newObject.Constructor.DeclaringType, elements);
            context.Stepper.StepOver("raise ValueTuple constructor to tuple literal", newObject);
            newObject.ReplaceWith(tuple);
        }
    }

    /// <summary>
    /// The flattened elements of a tuple-literal construction (arity 2-7, or the
    /// eight-or-more nested-<c>TRest</c> form), or null when <paramref name="newObject"/>
    /// is not a complete tuple literal. Pure — gathers element references without
    /// mutating the tree, so a partial match leaves nothing detached.
    /// </summary>
    static List<IrExpression>? TupleLiteralElements(NewObject newObject)
    {
        if (!MemberIdentity.IsValueTupleConstructorOfArity(newObject, out int arity) || arity < 2)
            return null;
        var elements = new List<IrExpression>();
        return CollectElements(newObject, arity, elements) ? elements : null;
    }

    /// <summary>
    /// Appends one ValueTuple construction's elements in order. Arities 1-7
    /// contribute their arguments directly; arity 8 contributes its first seven and
    /// then the nested rest, which must itself be an inline ValueTuple construction
    /// (csc always builds the rest inline for a literal). False when the rest is not
    /// inline — a hand-written <c>new ValueTuple&lt;…&gt;(…, restValue)</c>.
    /// </summary>
    static bool CollectElements(NewObject newObject, int arity, List<IrExpression> elements)
    {
        var arguments = newObject.Arguments;
        if (arity <= 7)
        {
            elements.AddRange(arguments);
            return true;
        }

        for (int i = 0; i < 7; i++)
            elements.Add(arguments[i]);
        return arguments[7] is NewObject rest
            && MemberIdentity.IsValueTupleConstructorOfArity(rest, out int restArity)
            && CollectElements(rest, restArity, elements);
    }
}
