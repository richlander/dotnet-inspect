namespace ILInspector.Decompiler.Fixtures;

public sealed class SingleCaseValueSwitchSamples(int state, byte[]? buffer)
{
    readonly int _state = state;
    readonly byte[]? _buffer = buffer;

    public long MaxLength => _state switch
    {
        3 => -1L,
        _ => _buffer is null ? -1L : _buffer.Length,
    };

    public static long OneCase(int state, int length)
        => state switch { 3 => -1L, _ => length };

    public static long ZeroCase(int state, int length)
        => state switch { 0 => -1L, _ => length };

    public static int CheckedArm(int state, int length)
        => state switch { 3 => checked(length * 2), _ => unchecked(length + 1) };

    public static int EffectfulSelection(ref int state)
        => Next(ref state) switch
        {
            3 => Next(ref state),
            _ => Next(ref state) + 1,
        };

    public static int DirectReturns(int state, int length)
    {
        if (state == 3)
            return length * 2;
        return length + 1;
    }

    public static object BoxedCapacity(int state, short length)
        => state switch { 3 => (object)length, _ => (object)(length + 1) };

    static int Next(ref int state) => state++;
}
