namespace ILInspector.Decompiler.Tests;

public sealed class ByteArraySlotMaterializationSamples
{
    public byte[] Value { get; set; } = [0];
    public byte[] Read() => Value;
    public void Observe() => Value = [99];

    public void MutateAndReplace()
    {
        Value[0] = 255;
        Observe();
    }

    public static byte[] ReadAndObserve(ByteArraySlotMaterializationSamples reader)
    {
        var value = reader.Read();
        reader.Observe();
        return value;
    }

    public static byte[] AllocateAndObserve(int length, ByteArraySlotMaterializationSamples reader)
    {
        var value = new byte[length];
        reader.Observe();
        return value;
    }

    public static byte[] InitializeAndObserve(byte first, byte second, ByteArraySlotMaterializationSamples reader)
    {
        var value = new byte[] { first, second };
        reader.Observe();
        return value;
    }

    public static byte[] MutateAndObserve(ByteArraySlotMaterializationSamples reader)
    {
        var value = reader.Read();
        reader.MutateAndReplace();
        return value;
    }

    public static bool SwapArrays(byte[] first, byte[] second)
    {
        (first, second) = (second, first);
        return ReferenceEquals(first, second);
    }
}
