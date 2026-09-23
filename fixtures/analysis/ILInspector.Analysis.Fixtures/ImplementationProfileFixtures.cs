namespace ILInspector.Analysis.ImplementationProfileFixtures;

public static class ImplementationProfileSample
{
    public static int Value { get; set; }

    public static event Action? Changed;

    public static int Analyze(int value)
        => Analyze(value, iterations: 3);

    public static int Analyze(int value, int iterations)
    {
        int total = 0;
        try
        {
            for (int index = 0; index < iterations; index++)
            {
                if ((index & 1) == 0)
                    total += value;
                else
                    total -= value;
            }
        }
        catch (OverflowException)
        {
            return 0;
        }
        return total;
    }

    public static string Analyze(string value)
        => value.Trim();

    public static Func<int, int> Analyze(Func<int, int> selector)
        => Analyze;

    public static async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return value;
    }

    public static async Task<int> AnalyzeAsync(string value)
    {
        await Task.Yield();
        return await AnalyzeAsync(value.Length);
    }

    public static int Other(int value)
        => value + 1;

    private static int Hidden(int value)
        => value * 2;

    public static void RaiseChanged()
        => Changed?.Invoke();
}

public sealed class GenericOverloadSample<T>
{
    public int Route()
        => Route<string, bool>();

    public int Route<TValue>()
        => 1;

    public int Route<TValue, TOther>()
        => 2;
}

public interface IImplementationProfileBodylessSample
{
    int Route(int value);

    int Route(string value);
}
