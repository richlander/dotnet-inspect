namespace ILInspector.Decompiler.Tests;

public ref struct ExactByRefLikeValue(int value)
{
    public int Value { get; } = value;
}

public ref struct ExactSpanReader(Span<int> value)
{
    Span<int> _value = value;

    public Span<int> Read() => _value;
    public void Observe(Span<int> replacement) => _value = replacement;
}

public ref struct ExactReadOnlySpanReader(ReadOnlySpan<int> value)
{
    ReadOnlySpan<int> _value = value;

    public ReadOnlySpan<int> Read() => _value;
    public void Observe(ReadOnlySpan<int> replacement) => _value = replacement;
}

public ref struct ExactByRefLikeValueReader(ExactByRefLikeValue value)
{
    ExactByRefLikeValue _value = value;

    public ExactByRefLikeValue Read() => _value;
    public void Observe(ExactByRefLikeValue replacement) => _value = replacement;
}

public static class ByRefLikeSlotMaterializationSamples
{
    public static Span<int> ReadSpan(ExactSpanReader reader, Span<int> replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static ReadOnlySpan<int> ReadReadOnlySpan(
        ExactReadOnlySpanReader reader,
        ReadOnlySpan<int> replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static ExactByRefLikeValue ReadCustom(
        ExactByRefLikeValueReader reader,
        ExactByRefLikeValue replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static int PreserveAcrossMutation(Span<int> source, Span<int> replacement)
    {
        Span<int> preserved = source;
        source[0]++;
        source = replacement;
        return preserved[0] + source.Length;
    }

    public static int PreserveStackBackedValue(int value)
    {
        Span<int> source = stackalloc int[1];
        source[0] = value;
        Span<int> preserved = source;
        source[0]++;
        return preserved[0];
    }
}
