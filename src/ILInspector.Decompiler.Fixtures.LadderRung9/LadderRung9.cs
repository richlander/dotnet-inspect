using System;
using System.Linq.Expressions;

namespace LadderRung9;

// Rung 9 of the decompiler product quality ladder (#1599): dynamic and
// expression-tree honesty. These constructs are source-visible but lower through
// compiler-generated scaffolding; the rung guard requires the decompiler to
// either keep that scaffolding explicit/honestly Partial or recover source syntax
// only after a future proof-backed raise changes the guard.
public class DynamicAndExpressionTrees
{
    public object DynamicAdd(dynamic left, dynamic right)
    {
        return left + right;
    }

    public object DynamicGetLength(dynamic value)
    {
        return value.Length;
    }

    // Close negative for #2984: the receiver is a genuine `object` parameter (no
    // [DynamicAttribute]); the member access only becomes dynamic through the
    // explicit `(dynamic)` cast in source. The redundant-cast drop must NOT fire
    // here — the static type really is object — so `((dynamic)value)` is kept.
    public object DynamicGetLengthOfObject(object value)
    {
        return ((dynamic)value).Length;
    }

    public object DynamicInvoke(dynamic function, int value)
    {
        return function(value);
    }

    public object DynamicInvokeMember(dynamic value, int start, int length)
    {
        return value.Substring(start, length);
    }

    public int DynamicConvert(dynamic value)
    {
        return (int)value;
    }

    public object DynamicNegate(dynamic value)
    {
        return -value;
    }

    public object DynamicConstruct(dynamic value)
    {
        return new DynamicConstructTarget(value);
    }

    public object DynamicSetMember(dynamic value, string name)
    {
        value.Name = name;
        return value;
    }

    public object DynamicGetIndex(dynamic value, int index)
    {
        return value[index];
    }

    public object DynamicSetIndex(dynamic value, int index, object next)
    {
        value[index] = next;
        return value;
    }

    public object DynamicCompoundMember(dynamic value, int delta)
    {
        value.Count += delta;
        return value;
    }

    public void DynamicResultDiscarded(dynamic value)
    {
        value.Reset();
    }

    public void DynamicEventAdd(dynamic value, EventHandler handler)
    {
        value.Changed += handler;
    }

    public void DynamicEventRemove(dynamic value, EventHandler handler)
    {
        value.Changed -= handler;
    }

    public object DynamicNamedOut(dynamic value, string key)
    {
        object result;
        value.TryGet(name: key, result: out result);
        return result;
    }

    public int DynamicRefArgument(dynamic value, int input)
    {
        value.Mutate(ref input);
        return input;
    }

    public Expression<Func<int, int>> SimpleExpressionTree()
    {
        return x => x + 1;
    }

    public Expression<Func<int, bool>> CapturedExpressionTree(int threshold)
    {
        return x => x > threshold;
    }

    public Expression<Func<int, int>> OptionalArgumentExpressionTree()
    {
        return x => Optional(x);
    }

    public Expression<Func<int, int>> NamedArgumentExpressionTree()
    {
        return x => Optional(value: x, delta: 1);
    }

    static int Optional(int value, int delta = 1) => value + delta;
}

public sealed class DynamicConstructTarget
{
    public DynamicConstructTarget(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
