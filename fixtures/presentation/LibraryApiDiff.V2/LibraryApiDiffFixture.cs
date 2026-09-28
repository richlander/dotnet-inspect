namespace LibraryApiDiffFixture;

public sealed class ProjectionReceiver
{
    public int Transform(int value) => value;
}

public static class ProjectionExtensions
{
}

[HistoryTag("after")]
public ref struct TypeDefinitionOnly
{
    public int Value;
}

internal sealed class HistoryTagAttribute(string value) : System.Attribute
{
    public string Value { get; } = value;
}

public class HardChangedType
{
    public int First() => 1;
}

public class OtherHardChangedType
{
    public int Second() => 2;
}

public sealed class MethodConstraintChange
{
    public void Apply<T>()
        where T : Dependency.AfterConstraint
    {
    }
}

public sealed class AddedType
{
    public int First() => 1;

    public int Second() => 2;
}
