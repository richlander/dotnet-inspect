namespace SourceDiffFixture;

public sealed class Counter
{
    const int BuildValue = 1;

    /// <summary>
    /// Returns the value represented by this fixture version.
    /// </summary>
    /// <returns>The fixture's counter value.</returns>
    public int Value() => 1 + 2;

    public int Unchanged() => 7;

    public int SameSource() => BuildValue;

    public int Property => 1;

    int Hidden() => 1;

    public int LocalFunction(int value)
    {
        int Adjust(int input) => input + 1;
        return Adjust(value) + Hidden();
    }

    public int Reordered()
    {
        int first = 1;
        int second = 2;
        return first + second;
    }

    public int MovedBlock()
    {
        // First annotation.
        // Second annotation.
        int first = 1;
        int second = 2;
        return first + second;
    }

    public int MovedBlockAndEdit()
    {
        // First annotation.
        // Second annotation.
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
