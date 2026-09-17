namespace ILInspector.Decompiler.Tests;

public static unsafe class ExactPointerSlotMaterializationSamples
{
    public static int* Current;

    public static int* ReadAndObserve(int* current, Action observe)
    {
        var value = ReadPointer(current);
        observe();
        return value;
    }

    public static int* CopyThenReplace(int* replacement)
    {
        var value = Current;
        Current = replacement;
        return value;
    }

    public static int* MutateAndObserve(int* current, int replacement, Action observe)
    {
        var value = ReadPointer(current);
        *value = replacement;
        observe();
        return value;
    }

    public static int StackAllocated(int value)
    {
        byte* buffer = stackalloc byte[32];
        Initialize(buffer, value);
        return buffer[0] + buffer[31];
    }

    public static void Initialize(byte* buffer, int value)
    {
        buffer[0] = (byte)value;
        buffer[31] = (byte)(value + 1);
    }

    public static ulong Accumulate(ulong* values, int index, ulong value)
    {
        values[index] += value;
        return values[index];
    }

    public static void* ReturnAsVoid(int* current, Action observe)
    {
        var value = ReadPointer(current);
        observe();
        return value;
    }

    public static bool SwapPointers(int* first, int* second)
    {
        var temporary = first;
        first = second;
        second = temporary;
        return first == second;
    }

    static int* ReadPointer(int* value) => value;
}
