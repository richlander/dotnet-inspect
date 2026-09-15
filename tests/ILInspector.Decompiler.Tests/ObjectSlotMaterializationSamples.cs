namespace ILInspector.Decompiler.Tests;

public sealed class ObjectSlotMaterializationSamples
{
    public object Value { get; set; } = new();
    public object Read() => Value;
    public void Observe(object value) => Value = value;

    public static object ReadAndObserve(ObjectSlotMaterializationSamples reader)
    {
        var value = reader.Read();
        reader.Observe("observed");
        return value;
    }

    public static object BoxAndObserve(int value, ObjectSlotMaterializationSamples reader)
    {
        object boxed = value;
        reader.Observe("observed");
        return boxed;
    }

    public static bool SwapObjects(object first, object second)
    {
        (first, second) = (second, first);
        return ReferenceEquals(first, second);
    }
}
