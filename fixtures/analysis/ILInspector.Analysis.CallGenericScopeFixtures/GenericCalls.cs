namespace Samples;

public static class GenericCalls
{
    public static void Call<T>() => Target<T>.Invoke();

    public static void CallVarArg<T>(T value) => VarArgTarget.Invoke(__arglist(value));

    public static void CallAttributedLocal()
    {
        Local<int>();

        static void Local<T>() => Target<T>.Invoke();
    }

    public static void CallNeighbor<T>() => Target<T>.Invoke();
}

public static class VarArgTarget
{
    public static void Invoke(__arglist) { }
}

public static class Target<T>
{
    public static void Invoke() { }
}
