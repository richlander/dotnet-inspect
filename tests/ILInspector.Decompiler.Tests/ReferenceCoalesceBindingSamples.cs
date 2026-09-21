namespace ILInspector.Decompiler.Tests;

public static class ReferenceCoalesceBindingSamples
{
    public static string SameType(string left, string right) => left ?? right;
    public static string NullRight(string left) => left ?? null!;
    public static object ObjectRight(string left, object right) => left ?? right;
    public static object ObjectLeft(object left, string right) => left ?? right;
    public static int NullableValue(int? left, int right) => left ?? right;
    public static string ObjectOverload(string left, string right) => Select((object)(left ?? right));
    public static string SpilledObjectOverload(string left, string right) => Select(Side(), (object)(left ?? right));
    public static ReferenceCoalesceChoice ConstructorOverload(string left, string right) => new((object)(left ?? right));
    public static Func<string, string> NestedObjectOverload(string fallback) => value => Select((object)(value ?? fallback));
    public static string LocalFunctionObjectOverload(string left, string fallback)
    {
        string Read(string value) => Select((object)(value ?? fallback));
        return Read(left);
    }

    public static string Select(object value) => "object";
    public static string Select(string value) => "string";
    public static string Select(int side, object value) => "object";
    public static string Select(int side, string value) => "string";
    public static int Side() => 0;
}

public sealed class ReferenceCoalesceChoice
{
    public ReferenceCoalesceChoice(object value) { }
    public ReferenceCoalesceChoice(string value) { }
}
