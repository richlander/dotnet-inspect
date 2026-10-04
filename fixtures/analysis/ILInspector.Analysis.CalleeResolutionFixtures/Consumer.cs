using System.Collections.Generic;
using CalleeResolution.Models;

namespace CalleeResolution.Services;

public static class Consumer
{
    public static int UseClosedGeneric() => new Box<int>().Get();

    public static T UseOpenGeneric<T>() => new Box<T>().GetTwice();

    public static object CreatePair() => Pair<string, int>.Create();

    public static int UseDirect() => Helper.Value();

    public static void UseExternal(List<int> items) => items.Add(1);

    public static int UseArray(int[,] grid) => grid[0, 0];

    public static unsafe void UseIndirect(delegate*<void> target) => target();

    // The lambda body is lifted into Consumer.<>c; its ldftn target and its
    // call to Helper.Value both belong to UseLambda through Analysis's
    // declared-source association.
    public static System.Func<int> UseLambda() => static () => Helper.Value();
}
