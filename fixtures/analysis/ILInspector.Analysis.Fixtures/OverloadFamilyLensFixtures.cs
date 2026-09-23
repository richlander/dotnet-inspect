namespace ILInspector.Analysis.OverloadFamilyLensFixtures;

public static class SiblingDelegation
{
    public static int Parse(string value)
        => Parse(value.Length);

    public static int Parse(int value)
        => value;
}

public static class IndependentImplementations
{
    public static int Measure(string value)
        => value.Length;

    public static int Measure(int value)
        => value * 2;
}

public static class PrivateHelperConvergence
{
    public static string Normalize(string value)
        => NormalizeCore(value);

    public static string Normalize(ReadOnlySpan<char> value)
        => NormalizeCore(value.ToString());

    private static string NormalizeCore(string value)
        => value.Trim();
}

public sealed class ConstructorDelegation
{
    public ConstructorDelegation()
        : this(0)
    {
    }

    public ConstructorDelegation(int value)
        => Value = value;

    public int Value { get; }
}

public static class SingleImplementation
{
    public static int Only(int value)
        => value;
}
