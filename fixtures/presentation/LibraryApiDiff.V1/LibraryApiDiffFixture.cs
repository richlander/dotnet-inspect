namespace LibraryApiDiffFixture;

public sealed class ProjectionReceiver
{
}

public static class ProjectionExtensions
{
    public static int Transform(
        this ProjectionReceiver receiver,
        int value) => value;
}

[HistoryTag("before")]
public struct TypeDefinitionOnly
{
    public int Value;
}

internal sealed class HistoryTagAttribute(string value) : System.Attribute
{
    public string Value { get; } = value;
}

internal static class LiteralTransitions
{
    public static string Endpoint() =>
        "https://old.example and https://shared.example";

    public static string Embedded() =>
        "prefix https://embedded.example";
}

public class HardChangedType
{
    public virtual int First() => 1;
}

public class OtherHardChangedType
{
    public virtual int Second() => 2;
}

public sealed class MethodConstraintChange
{
    public void Apply<T>()
        where T : Dependency.BeforeConstraint
    {
    }
}

public sealed class RemovedType
{
    public int First() => 1;

    public int Second() => 2;
}
