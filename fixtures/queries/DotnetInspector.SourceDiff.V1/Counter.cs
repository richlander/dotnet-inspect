namespace SourceDiffFixture;

public sealed class Counter
{
    const int BuildValue = 1;

    public int Value() => 1 + 2;

    public int Unchanged() => 7;

    public int SameSource() => BuildValue;

    public int Reordered()
    {
        int first = 1;
        int second = 2;
        return first + second;
    }

    public int BeforeOnly() => 9;
}

public sealed class MovedCounter
{
    public int Value() => 1 + 2;
}
