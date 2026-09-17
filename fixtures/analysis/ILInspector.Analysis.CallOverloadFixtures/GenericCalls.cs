namespace Samples;

public static class GenericCalls
{
    public static void Call()
    {
        unsafe
        {
            Target<int>.Invoke(42);
        }
    }

    public static void CallGeneric<T>(T value) => Target<T>.Invoke(value);
}

public static class Target<T>
{
    public static void Invoke(T value) { }
    public static unsafe void Invoke(int value) { }
}
