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
