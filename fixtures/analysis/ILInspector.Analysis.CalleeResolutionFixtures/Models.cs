namespace CalleeResolution.Models;

// Calls into these types from generic contexts compile to MemberRefs on
// TypeSpecs, so their definition tokens are not MethodDef tokens.
public class Box<T>
{
    T _value = default!;

    public T Get() => _value;

    public T GetTwice()
    {
        Get();
        return Get();
    }
}

public sealed class Pair<TLeft, TRight>
{
    public static Pair<TLeft, TRight> Create() => new();
}

public static class Helper
{
    public static int Value() => 1;
}
