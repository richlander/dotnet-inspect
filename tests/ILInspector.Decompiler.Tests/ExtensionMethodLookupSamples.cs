namespace ILInspector.Decompiler.Tests.MethodGroupLookup;

public static class CompetingSelectExtensions
{
    public static IEnumerable<int> Select(
        this IEnumerable<string> values,
        Func<string, int> selector)
        => [-123];
}

public static class OutputInferenceMethodGroupLookupSamples
{
    public static IEnumerable<int> CallWithCompetingExtension(
        IEnumerable<string> values)
        => values.Select<string, int>(Parse);

    public static int Parse(string value) => value.Length;
}
