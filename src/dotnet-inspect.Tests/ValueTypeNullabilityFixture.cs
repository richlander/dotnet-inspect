namespace DotnetInspector.Tests;

/// <summary>
/// Compiler-produced witness for nullable byte application to value-constrained generic
/// parameters. The nullable fields make Roslyn select a nullable enclosing context, while
/// the methods distinguish structural <see cref="Nullable{T}"/> from a bare parameter.
/// </summary>
public static class ValueTypeNullabilityFixture
{
    public static string? A;
    public static string? B;
    public static string? C;

    public readonly struct Handler<T>
    {
        public Handler(T? value)
        {
        }
    }

    public static void NullableValue<T>(
        T? value,
        Handler<T> message,
        string? path = null) where T : struct
    {
    }

    public static void PlainValue<T>(
        T value,
        Handler<T> message,
        string? path = null) where T : unmanaged
    {
    }

    public static void Open<T>(T? value)
    {
    }
}

public sealed class ValueTypeNullabilityContainer<T> where T : struct
{
    public string? A;
    public string? B;
    public string? C;
    public string? D;
    public T Value;
    public T? Maybe;
}

public sealed class OpenNullabilityContainer<T>
{
    public string? A;
    public string? B;
    public string? C;
    public string? D;
    public T? Maybe;
}
