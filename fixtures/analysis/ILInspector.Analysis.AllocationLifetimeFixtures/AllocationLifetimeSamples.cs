namespace ILInspector.Analysis.AllocationLifetimeFixtures;

public static class AllocationLifetimeSamples
{
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
