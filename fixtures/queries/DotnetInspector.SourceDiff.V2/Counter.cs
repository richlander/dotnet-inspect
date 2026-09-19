namespace SourceDiffFixture;

public sealed class Counter
{
    const int BuildValue = 2;

    /// <summary>
    /// Returns the value represented by this fixture version.
    /// </summary>
    /// <returns>The fixture's counter value.</returns>
    public int Value() => 3;

    public int Unchanged() => 7;

    public int SameSource() => BuildValue;

    public int Property => 2;

    int Hidden() => 2;

    public int LocalFunction(int value)
    {
        int Adjust(int input) => input + 2;
        return Adjust(value) + Hidden();
    }

    public int MultipleLocalFunctions(int value)
    {
        int Increment(int input) => input + 2;
        int Scale(int input) => input * 3;
        return Increment(value) + Scale(value);
    }

    public int Reordered()
    {
        int second = 2;
        int first = 1;
        return first + second;
    }

    public int MovedBlock()
    {
        int first = 1;
        int second = 2;
        // First annotation.
        // Second annotation.
        return first + second;
    }

    public int MovedBlockAndEdit()
    {
        int first = 1;
        int second = 2;
        // First annotation.
        // Second annotation.
        return first + second + 1;
    }
}

public sealed class MovedCounter
{
    int Padding() => 0;

    int MorePadding() => 0;

    public int Value() => 3;
}
