namespace ILInspector.Decompiler.Tests;

public static class ExactMdArraySlotMaterializationSamples
{
    public static int[,] ReadIntegers(Func<int[,]> read, Action observe)
    {
        var value = read();
        observe();
        return value;
    }

    public static string[,,] ReadRankThree(Func<string[,,]> read, Action observe)
    {
        var value = read();
        observe();
        return value;
    }

    public static T[,] ReadGeneric<T>(Func<T[,]> read, Action observe)
    {
        var value = read();
        observe();
        return value;
    }

    public static int[,] AllocateAndObserve(int rows, int columns, Action observe)
    {
        var value = new int[rows, columns];
        observe();
        return value;
    }

    public static int[,] InitializeAndObserve(int first, int second, Action observe)
    {
        var value = new int[,] { { first, second }, { second, first } };
        observe();
        return value;
    }

    public static int[,] CopyThenReplace(ref int[,] current, int[,] replacement)
    {
        var value = current;
        current = replacement;
        return value;
    }

    public static int[,] MutateAndObserve(Func<int[,]> read, int replacement, Action observe)
    {
        var value = read();
        value[0, 0] = replacement;
        observe();
        return value;
    }

    public static object[,] CovariantReturn(Func<string[,]> read, Action observe)
    {
        var value = read();
        observe();
        return value;
    }

    public static bool SwapArrays(int[,] first, int[,] second)
    {
        (first, second) = (second, first);
        return ReferenceEquals(first, second);
    }
}
