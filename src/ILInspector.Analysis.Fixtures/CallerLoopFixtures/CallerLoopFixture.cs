namespace ILInspector.Analysis.CallerLoopFixtures;

public class CallerLoopFixture
{
    public object BoxDirect(int value) => value;

    public object? InvokeDirectInLoop(int count)
    {
        object? result = null;
        for (int i = 0; i < count; i++)
            result = BoxDirect(i);
        return result;
    }

    public object BoxOutsideLoop(int value) => value;

    public object InvokeOutsideLoop(int value) => BoxOutsideLoop(value);

    public object BoxFunctionTarget(int value) => value;

    public Delegate? LoadFunctionInLoop(int count)
    {
        Func<int, object>? callback = null;
        for (int i = 0; i < count; i++)
            callback = BoxFunctionTarget;
        return callback;
    }

    public object? BoxConditionally(bool enabled, int value)
        => enabled ? value : null;

    public object? InvokeConditionalInLoop(int count, bool enabled)
    {
        object? result = null;
        for (int i = 0; i < count; i++)
            result = BoxConditionally(enabled, i);
        return result;
    }

    public object RecursiveBox(int value)
        => value <= 0 ? value : RecursiveBox(value - 1);

    public object? TraverseRecursively(int[] values, int depth)
    {
        object? result = BuildTraversalNode(depth);
        foreach (int _ in values)
        {
            if (depth > 0)
                result = TraverseRecursively(values, depth - 1);
        }
        return result;
    }

    public object? TraverseConditionally(int[] values, int depth, bool enabled)
    {
        object? result = BuildConditionalTraversalNode(enabled, depth);
        foreach (int _ in values)
        {
            if (depth > 0)
                result = TraverseConditionally(values, depth - 1, enabled);
        }
        return result;
    }

    public object? TraverseMutualA(int[] values, int depth)
    {
        object? result = BuildMutualTraversalNode(depth);
        foreach (int _ in values)
        {
            if (depth > 0)
                result = TraverseMutualB(values, depth - 1);
        }
        return result;
    }

    object? TraverseMutualB(int[] values, int depth)
    {
        object? result = null;
        foreach (int _ in values)
        {
            if (depth > 0)
                result = TraverseMutualA(values, depth - 1);
        }
        return result;
    }

    public virtual object? TraverseVirtually(int[] values, int depth)
    {
        object? result = BuildVirtualTraversalNode(depth);
        foreach (int _ in values)
        {
            if (depth > 0)
                result = TraverseVirtually(values, depth - 1);
        }
        return result;
    }

    public Delegate? LoadSelfFunctionInLoop(int count)
    {
        Func<int, Delegate?>? callback = null;
        for (int i = 0; i < count; i++)
            callback = LoadSelfFunctionInLoop;
        return callback;
    }

    public object BuildTraversalNode(int value) => value;

    public object? BuildConditionalTraversalNode(bool enabled, int value)
        => enabled ? value : null;

    public object BuildMutualTraversalNode(int value) => value;

    public object BuildVirtualTraversalNode(int value) => value;

    public virtual object BoxVirtual(int value) => value;

    public object? InvokeVirtualInLoop(int count)
    {
        object? result = null;
        for (int i = 0; i < count; i++)
            result = BoxVirtual(i);
        return result;
    }

    public CallerLoopConstructorTarget? ConstructInLoop(int count)
    {
        CallerLoopConstructorTarget? result = null;
        for (int i = 0; i < count; i++)
            result = new CallerLoopConstructorTarget(i);
        return result;
    }
}

public sealed class CallerLoopConstructorTarget
{
    public CallerLoopConstructorTarget(int value) => Boxed = value;

    public object Boxed { get; }
}

public sealed class GenericTraversalFixture<T>
{
    public object? TraverseRecursively(int[] values, int depth)
    {
        object? result = BuildTraversalNode(depth);
        foreach (int _ in values)
        {
            if (depth > 0)
                result = TraverseRecursively(values, depth - 1);
        }
        return result;
    }

    public object BuildTraversalNode(int value) => value;
}
