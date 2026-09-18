namespace Samples;

public static class GenericCalls
{
    public static void Call() => Target<int>.Invoke<byte>(null);
}

public static class Target<T>
{
    public static void Invoke<U>(delegate*<U, void> callback) { }
}
