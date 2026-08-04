namespace ILInspector.Metadata.Tests;

public static class NullableContextScopeFixture
{
    public static string? A;
    public static string? B;
    public static string? C;
    public static string? D;
    public static string? E;
    public static string? F;
    public static string? G;
    public static string? H;
    public static string? I;
    public static string? J;

    public static void Enabled(string? value)
    {
    }

#nullable disable
    public static void Oblivious(string value)
    {
    }
#nullable restore

    public sealed class InheritedNullableContext
    {
        public string? Maybe;
        public string? Also;
    }

#nullable disable
    public sealed class ObliviousNullableContext
    {
        public string Value;
    }
#nullable restore
}
