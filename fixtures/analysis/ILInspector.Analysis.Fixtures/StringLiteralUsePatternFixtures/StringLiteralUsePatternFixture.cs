namespace ILInspector.Analysis.StringLiteralUsePatternFixtures;

public static class StringLiteralUsePatternFixture
{
    public const string ConstantOnlyMarker =
        "literal-marker-present-only-as-a-constant";

    [Obsolete("literal-marker-present-only-in-an-attribute")]
    public static void AttributeOnlyMarker()
    {
    }

    public static string RepeatedFirst() =>
        "shared-literal-use-marker";

    public static string RepeatedSecond() =>
        "shared-literal-use-marker";

    public static string BoundaryPair(bool left) =>
        left ? "boundary-left" : "boundary-right";

    public static string OrdinalCase() =>
        "Ordinal-Case-Marker";

    public static string Precomposed() =>
        "caf\u00E9-literal-marker";

    public static string Decomposed() =>
        "cafe\u0301-literal-marker";

    public static string Bmp() =>
        "\u96EA-literal-marker";

    public static string SupplementaryPlane() =>
        "rocket-\U0001F680-literal-marker";

    public static string EmbeddedNull() =>
        "embedded\0nul-literal-marker";
}

public abstract class StringLiteralUseBodylessFixture
{
    public abstract string NoBody();
}
