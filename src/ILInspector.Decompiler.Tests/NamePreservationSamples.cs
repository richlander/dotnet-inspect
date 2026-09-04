namespace ILInspector.Decompiler.Tests;

public static class NamePreservationSamples
{
    public static unsafe int* StackAllocGenericNameCollision<__stackalloc>()
    {
        int* p = stackalloc int[1];
        return p;
    }

    public static void StoreElementNamedReceiverTemp(
        string[] items,
        int i,
        ReadOnlySpan<char> item)
    {
        ReadOnlySpan<char> trimmed = MemoryExtensions.Trim(item);
        items[i] = trimmed.ToString();
    }
}
