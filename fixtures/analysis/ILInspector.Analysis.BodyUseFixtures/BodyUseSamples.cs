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

    public static IEnumerable<BodyUseTarget> IteratorUse()
    {
        yield return new BodyUseTarget();
    }

    public static Type TargetType() => typeof(BodyUseTarget);

    static T Identity<T>(T value) => value;
}
