namespace ILInspector.Decompiler.Tests;

public struct StoreElementMutableReceiver
{
    public int Count;

    public override string ToString()
    {
        Count++;
        return Count.ToString();
    }
}

public static class StoreElementReceiverInliningSamples
{
    public static int CopyMutation(
        string[] items,
        StoreElementMutableReceiver value)
    {
        StoreElementMutableReceiver temp = value;
        items[0] = temp.ToString();
        return value.Count;
    }
}
