namespace ILInspector.Analysis.OverloadFamilyLensFixtures;

public static class SiblingDelegation
{
    public static int Parse(bool value)
        => Parse(value ? 1 : 0);

    public static int Parse(int value)
        => value;
}

public static class SiblingDelegationChain
{
    public static int Parse(bool value)
        => Parse(value ? 1L : 0L);

    public static int Parse(long value)
        => Parse((int)value);

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

public static class FamilyOverloadConvergence
{
    public static int Convert(bool value)
        => Convert(value ? 1 : 0);

    public static int Convert(long value)
        => Convert((int)value);

    public static int Convert(int value)
        => value;
}

public static class RecursiveSiblings
{
    public static int Traverse(bool value)
        => Traverse(value ? 1 : 0);

    public static int Traverse(int value)
        => Traverse(value != 0);
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

public sealed class ConstructorConstruction
{
    public ConstructorConstruction()
    {
        _ = new ConstructorConstruction(0);
    }

    public ConstructorConstruction(int value)
        => Value = value;

    public int Value { get; }
}

public static class SingleImplementation
{
    public static int Only(int value)
        => value;
}
