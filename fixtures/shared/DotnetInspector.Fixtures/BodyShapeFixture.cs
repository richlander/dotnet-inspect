namespace DotnetInspector.Fixtures;

public interface IBodyShapeValue
{
    object Value { get; }
    event Action Changed;
}

public interface IBodyShapePrefixMethods
{
    int get_Count();

    void set_Count();
}

public sealed class BodyShapeFixture :
    IBodyShapeValue,
    IBodyShapePrefixMethods
{
    public static object PublicCreation() => new object();

    public static int[] PublicSmallArray() => new int[3];

    public static bool PublicLocalFunctionBox<T>(T left, T right)
    {
        return EqualsCore(left, right);
        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }

    private static object PrivateCreation() => new Version(1, 2);

    object IBodyShapeValue.Value => new object();

    event Action IBodyShapeValue.Changed
    {
        add => GC.KeepAlive(new object());
        remove { }
    }

    int IBodyShapePrefixMethods.get_Count() => 1;

    void IBodyShapePrefixMethods.set_Count() => GC.KeepAlive(this);

    public static string Classify(int value) =>
        value switch
        {
            < 0 => "negative",
            0 => "zero",
            _ => "positive"
        };

    public static string Branch(bool value)
    {
        if (value)
        {
            return "yes";
        }

        return "no";
    }

    public static string ReadableLocal(int value)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(value);
        if (value >= 0)
            builder.Append('+');
        else
            builder.Append('-');
        return builder.ToString();
    }
}

public static class BodyShapeFixtureExtensions
{
    public static object ProjectedCreation(this BodyShapeFixture value) => new();
}

public sealed class GenericBodyShapeFixture<T>
{
    public static object Create() => new object();
}

public sealed class OverloadedIndexerBodyShapeFixture
{
    public string this[int index]
    {
        get => index.ToString();
        set => GC.KeepAlive(value);
    }

    public string this[string key]
    {
        get => key.ToString();
        set => Console.WriteLine(value);
    }
}
