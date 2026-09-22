namespace ILInspector.Decompiler.Tests;

public static class ReferenceConditionalBindingSamples
{
    public static string? SameType(bool choose, string left, string right) => Observe(choose ? left : right);
    public static string? NullTrue(bool choose, string value) => Observe(choose ? null : value);
    public static string? NullFalse(bool choose, string value) => Observe(choose ? value : null);
    public static string? Nested(bool outer, bool inner, string left, string right)
        => Observe(outer ? null : inner ? left : right);
    public static Func<bool, string, string?> Lambda()
        => (choose, value) => Observe(choose ? null : value);
    public static string? LocalFunction(bool choose, string value)
    {
        static string? Pick(bool flag, string text) => Observe(flag ? null : text);
        return Pick(choose, value);
    }
    public static char Character(bool choose) => ObserveCharacter(choose ? 'a' : 'b');
    public static string? Observe(string? value) => value;
    public static char ObserveCharacter(char value) => value;
}
