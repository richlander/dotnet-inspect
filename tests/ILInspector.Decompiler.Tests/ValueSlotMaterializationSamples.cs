namespace ILInspector.Decompiler.Tests;

public struct ExactStorageCounter
{
    public int Value;
    public void Increment() => Value++;
}

public static class ValueSlotMaterializationSamples
{
    public static DateTime ReadDateTime(ExactReferenceReader<DateTime> reader, DateTime replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static int? ReadNullable(ExactReferenceReader<int?> reader, int? replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static KeyValuePair<T, int> ReadGenericValue<T>(
        ExactReferenceReader<KeyValuePair<T, int>> reader, KeyValuePair<T, int> replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static ExactStorageCounter ReadMutableValue(
        ExactReferenceReader<ExactStorageCounter> reader, ExactStorageCounter replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static ExactStorageCounter CopyThenMutate(ref ExactStorageCounter source)
    {
        var copy = source;
        source.Increment();
        return copy;
    }

    public static object BoxValue(
        ExactReferenceReader<ExactStorageCounter> reader, ExactStorageCounter replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static object? BoxNullable(ExactReferenceReader<int?> reader, int? replacement)
    {
        var value = reader.Read();
        reader.Observe(replacement);
        return value;
    }

    public static int SwapValues(ExactStorageCounter first, ExactStorageCounter second)
    {
        (first, second) = (second, first);
        return first.Value - second.Value;
    }
}
