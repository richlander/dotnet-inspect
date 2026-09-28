namespace Collision;

public sealed class Outer
{
    public sealed class Inner
    {
    }
}

public static class NestedCollisionExposure
{
    public static void Expose() =>
        Capture<Outer.Inner>();

    static void Capture<T>()
    {
    }
}
