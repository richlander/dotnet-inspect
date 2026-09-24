namespace Collision.Outer;

public sealed class Inner
{
}

public static class NamespacedCollisionExposure
{
    public static void Expose() =>
        Capture<Inner>();

    static void Capture<T>()
    {
    }
}
