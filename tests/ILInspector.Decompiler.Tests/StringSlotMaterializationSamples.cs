namespace ILInspector.Decompiler.Tests;

public sealed class StringSlotMaterializationSamples
{
    public string Value { get; set; } = "";
    public string Read() => Value;
    public void Observe(string value) => Value = value;

    public static string ReadAndObserve(StringSlotMaterializationSamples reader)
    {
        var value = reader.Read();
        reader.Observe("observed");
        return value;
    }
}
