namespace SourceDiffFixture;

public sealed class Counter
{
    const int BuildValue = 2;

    public int Value() => 3;

    public int Unchanged() => 7;

    public int SameSource() => BuildValue;

    public int Reordered()
    {
        int second = 2;
        int first = 1;
        return first + second;
    }
}

public sealed class MovedCounter
{
    int Padding() => 0;

    int MorePadding() => 0;

    public int Value() => 3;
}
