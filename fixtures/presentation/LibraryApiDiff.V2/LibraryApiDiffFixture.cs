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

internal static class LiteralTransitions
{
    public static string Endpoint() =>
        "https://new.example and https://shared.example";

    public static string Embedded() =>
        "prefix https://embedded.example";
}

public class HardChangedType
{
    public int First() => 1;
}

public class OtherHardChangedType
{
    public int Second() => 2;
}

public sealed class BodyOnlyChange
{
    public int Value() => 2;
}

[SummaryTag("after")]
public sealed class AttributeOnlyChange
{
}

public sealed class SummaryTagAttribute(string value) : System.Attribute
{
    public string Value { get; } = value;
}

public sealed class MemberAttributeOnlyChange
{
    [SummaryTag("after")]
    public void Value()
    {
    }
}

public sealed class MemberAdditionContainer
{
    public void Added()
    {
    }
}

public sealed class AccessorAddition
{
    public int Value { get; set; } = 1;
}

public sealed class ExceptionRegionChange
{
    public int Value()
    {
        try
        {
            Throw();
            return 1;
        }
        catch (System.InvalidOperationException)
        {
            return 0;
        }
    }

    static void Throw()
    {
    }
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
