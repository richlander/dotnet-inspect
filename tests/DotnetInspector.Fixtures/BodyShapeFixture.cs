namespace DotnetInspector.Fixtures;

public sealed class BodyShapeFixture
{
    public static object PublicCreation() => new object();

    private static object PrivateCreation() => new Version(1, 2);

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
}
