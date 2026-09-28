namespace ILInspector.Analysis.AllocationLifetimeFixtures;

public static class AllocationLifetimeSamples
{
    static int[]? s_captured;

    public static string ConstructFromLocalChars(
        char first,
        char second,
        char third,
        char fourth)
    {
        var chars = new char[4];
        chars[0] = first;
        chars[1] = second;
        chars[2] = third;
        chars[3] = fourth;
        return new string(chars);
    }

    public static char[] ReturnLocalChars(char value)
    {
        var chars = new char[4];
        chars[0] = value;
        return chars;
    }

    public static int GenuinePrimitiveStaysLocal()
        => new uint[2].Length;

    public static int PrimitiveLookalikeStaysLocal()
        => new global::System.UIntPtr[2].Length;

    public static int ReadThroughRefLocal()
    {
        int[]? values = new int[1];
        ref int[]? alias = ref values;
        return alias!.Length;
    }

    public static void PassArrayByReference()
    {
        int[]? values = new int[1];
        Capture(ref values);
    }

    static void Capture(ref int[]? values) => s_captured = values;

    public static int AllocateInsideLoop(int count)
    {
        int sum = 0;
        for (int i = 0; i < count; i++)
        {
            var values = new int[2];
            values[0] = i;
            values[1] = i + 1;
            sum += values[0] + values[1];
        }
        return sum;
    }
}
