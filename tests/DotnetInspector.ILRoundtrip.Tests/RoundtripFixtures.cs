namespace DotnetInspector.ILRoundtrip.Tests;

/// <summary>
/// Methods disassembled by the round-trip tests. Constraints on fixture bodies:
/// no references to members of this assembly (the vendored ILAssembler does not
/// yet resolve member refs to dotted typedef names) and no switch statements that
/// lower to the IL switch instruction (its comma-separated label list hits an
/// upstream grammar gap). External (BCL) member refs, strings, generics, and
/// exception handlers are all fair game via canonical operand syntax.
/// </summary>
public static class RoundtripFixtures
{
    public static int Add(int a, int b) => a + b;

    public static int Max(int a, int b)
    {
        if (a >= b)
            return a;
        return b;
    }

    public static int SumLoop(int n)
    {
        int sum = 0;
        for (int i = 0; i < n; i++)
            sum += i;
        return sum;
    }

    public static bool IsGreater(double a, double b) => a > b;

    public static T Identity<T>(T value) => value;

    public static string Greet(string name) => string.Concat("Hello, ", name);

    public static int StringLength(string s) => s.Length;

    public static List<int> MakeList(int seed)
    {
        var list = new List<int>();
        list.Add(seed);
        return list;
    }

    public static int ParseOrNegativeOne(string s)
    {
        try
        {
            return int.Parse(s);
        }
        catch (FormatException)
        {
            return -1;
        }
    }
}
