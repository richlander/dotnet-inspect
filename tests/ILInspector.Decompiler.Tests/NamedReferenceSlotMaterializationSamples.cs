namespace ILInspector.Decompiler.Tests;

public sealed class ExactReferenceReader<T>(T value)
{
    public T Value { get; set; } = value;
    public T Read() => Value;
    public void Observe(T replacement) => Value = replacement;
}

public static class NamedReferenceSlotMaterializationSamples
{
    public static Uri ReadClass(ExactReferenceReader<Uri> reader, Uri replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static IDisposable ReadInterface(ExactReferenceReader<IDisposable> reader, IDisposable replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static Action ReadDelegate(ExactReferenceReader<Action> reader, Action replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static List<T> ReadGenericClass<T>(ExactReferenceReader<List<T>> reader, List<T> replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static IEnumerable<T> ReadGenericInterface<T>(
        ExactReferenceReader<IEnumerable<T>> reader, IEnumerable<T> replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static Func<T> ReadGenericDelegate<T>(ExactReferenceReader<Func<T>> reader, Func<T> replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static List<int> AllocateAndObserve(ExactReferenceReader<int> reader, int replacement)
    {
        var value = new List<int>();
        reader.Observe(replacement);
        return value;
    }

    public static IEnumerable<object> CovariantReturn(
        ExactReferenceReader<IEnumerable<string>> reader, IEnumerable<string> replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static bool SwapClasses(Uri first, Uri second)
    {
        (first, second) = (second, first);
        return ReferenceEquals(first, second);
    }

    public static T ReadUnconstrained<T>(ExactReferenceReader<T> reader, T replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }
}
