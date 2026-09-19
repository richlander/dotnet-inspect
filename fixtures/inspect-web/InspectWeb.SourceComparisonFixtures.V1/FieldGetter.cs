namespace SourceDiffFixture;

public class FieldGetter
{
    public int Count => field + 1;
    public int AutomaticCount { get; }
    public int ChangingCount => ++field;
    public int WriteOnly { set { } }
}
