namespace ILInspector.Decompiler.Tests;

public sealed class StringArraySlotMaterializationSamples
{
    public string[] Value { get; set; } = ["before"];
    public string[] Read() => Value;
    public void Observe() => Value = ["after"];

    public void MutateAndReplace(object replacement)
    {
        object[] alias = Value;
        alias[0] = replacement;
        Observe();
    }

    public static string[] ReadAndObserve(StringArraySlotMaterializationSamples reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static string[] AllocateAndObserve(int length, StringArraySlotMaterializationSamples reader)
    {
        var value = new string[length];
        reader.Observe();
        return value;
    }

    public static string[] InitializeAndObserve(string first, string second, StringArraySlotMaterializationSamples reader)
    {
        var value = new string[] { first, second };
        reader.Observe();
        return value;
    }

    public static string[] MutateAndObserve(StringArraySlotMaterializationSamples reader, object replacement)
    {
        var value = reader.Read();
        reader.MutateAndReplace(replacement);
        return value;
    }

    public static object[] CovariantReturn(StringArraySlotMaterializationSamples reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static bool SwapArrays(string[] first, string[] second)
    {
        (first, second) = (second, first);
        return ReferenceEquals(first, second);
    }
}
