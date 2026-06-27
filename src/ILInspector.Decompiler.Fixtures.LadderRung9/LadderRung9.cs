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

    public object DynamicInvoke(dynamic function, int value)
    {
        return function(value);
    }

    public object DynamicInvokeMember(dynamic value, int start, int length)
    {
        return value.Substring(start, length);
    }

    public Expression<Func<int, int>> SimpleExpressionTree()
    {
        return x => x + 1;
    }

    public Expression<Func<int, bool>> CapturedExpressionTree(int threshold)
    {
        return x => x > threshold;
    }
}
