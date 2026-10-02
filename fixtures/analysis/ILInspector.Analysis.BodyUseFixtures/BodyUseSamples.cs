namespace AnalysisBodyUseFixtures;

public sealed class BodyUseTarget
{
    public BodyUseTarget? Next;

    public void Touch()
    {
    }
}

public static class BodyUseSource
{
    public static async Task<BodyUseTarget> AsyncUse(
        BodyUseTarget target)
    {
        await Task.Yield();
        target.Touch();
        return target;
    }

    public static BodyUseTarget Exercise(BodyUseTarget target)
    {
        target.Touch();
        _ = target.Next;
        _ = new BodyUseTarget[1];
        object boxed = target;
        if (boxed is BodyUseTarget matched)
            target = matched;
        target = (BodyUseTarget)boxed;
        _ = typeof(BodyUseTarget);
        return Identity<BodyUseTarget>(target);
    }

    public static BodyUseTarget LiftedUse(BodyUseTarget target)
    {
        BodyUseTarget Local() => target;
        return Local();
    }

    public static BodyUseTarget BoundedLiftedOwner()
    {
        var target = new BodyUseTarget();
        target.Next = new BodyUseTarget();
        static BodyUseTarget Local() => new();
        return Local();
    }

    public static IEnumerable<BodyUseTarget> IteratorUse()
    {
        yield return new BodyUseTarget();
    }

    public static Func<Task<BodyUseTarget>> AsyncLambdaUse(
        BodyUseTarget target) =>
        async () =>
        {
            await Task.Yield();
            target.Touch();
            return target;
        };

    public static Type TargetType() => typeof(BodyUseTarget);

    static T Identity<T>(T value) => value;
}

/// <summary>
/// Roslyn lifted shapes whose owner is the innermost declaring Type not
/// prefixed <c>&lt;&gt;</c>. Each lifted body constructs a
/// <see cref="BodyUseTarget"/>.
/// </summary>
public static class BodyUseLiftedShapes
{
    // A display class nested in this Type, captured inside an async method.
    public static async Task<Func<BodyUseTarget>> CapturingLambdaInAsync(
        int seed)
    {
        await Task.Yield();
        int local = seed;
        return () =>
        {
            _ = local;
            return new BodyUseTarget();
        };
    }

    // A non-capturing async lambda: its state machine nests in <>c and its
    // kickoff is the lambda itself.
    public static Func<Task<BodyUseTarget>> AsyncLambdaInCache() =>
        static async () =>
        {
            await Task.Yield();
            return new BodyUseTarget();
        };

    // A local function declared inside a lambda.
    public static Func<BodyUseTarget> LocalFunctionInLambda() =>
        static () =>
        {
            return Make();

            static BodyUseTarget Make() => new();
        };

    // A lambda declared inside an iterator.
    public static IEnumerable<Func<BodyUseTarget>> LambdaInIterator()
    {
        yield return static () => new BodyUseTarget();
    }
}

/// <summary>
/// One MethodSpec instantiation reached from callers with different generic
/// arities.
/// </summary>
public static class BodyUseSharedInstantiation
{
    public static T Pick<T>(T value) => value;

    public static BodyUseTarget FromNonGeneric(BodyUseTarget target) =>
        Pick(target);

    public static BodyUseTarget FromGeneric<U>(BodyUseTarget target) =>
        Pick(target);

    public static BodyUseTarget FromTwoGeneric<U, V>(BodyUseTarget target) =>
        Pick(target);
}
