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

// Public overloads that forward into a non-public same-name implementation
// (the JsonDocument.Parse shape), and a public hub that a non-public
// overload also calls (the JsonConvert.ToString shape).
public static class ImplementationProfileHiddenImplementationSample
{
    public static int Parse(string text)
        => Parse(text.AsSpan(), strict: false);

    public static int Parse(char[] text)
        => Parse(text.AsSpan(), strict: true);

    static int Parse(ReadOnlySpan<char> text, bool strict)
    {
        int total = 0;
        foreach (char character in text)
        {
            if (character is >= '0' and <= '9')
                total = (total * 10) + (character - '0');
            else if (strict)
                throw new FormatException(text.ToString());
            else if (character == ',')
                continue;
            else
                break;
        }
        return total;
    }

    public static string Describe(int value)
    {
        if (value < 0)
            return "negative " + (-value).ToString();
        return value switch
        {
            0 => "zero",
            1 => "one",
            _ => value.ToString(),
        };
    }

    public static string Describe(string value)
        => Describe(value.Length);

    internal static string Describe(TimeSpan value)
        => Describe((int)value.TotalSeconds);
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
