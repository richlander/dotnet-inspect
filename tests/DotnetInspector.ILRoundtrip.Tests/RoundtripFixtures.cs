namespace DotnetInspector.ILRoundtrip.Tests;

/// <summary>
/// Methods disassembled by the round-trip tests. Bodies must stay free of
/// constructs whose operands the disassembler does not yet render in canonical
/// ilasm syntax (member refs, field refs, string literals with assembly-qualified
/// types) — these fixtures exercise the raw-output round-trip path.
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
}
