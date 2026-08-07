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
        public string? Another;
        public string? Fourth;
        public string? Fifth;
        public string? Sixth;
        public string? Seventh;
        public string? Eighth;
        public string? Ninth;
        public string? Tenth;

#nullable disable
        public string this[string key] => key;
#nullable restore
    }

#nullable disable
    public sealed class ObliviousNullableContext
    {
        public string Value;
    }
#nullable restore
}
