namespace ILInspector.Decompiler.Tests;

public static class PrimitiveJoinBindingSamples
{
    public static byte Conditional(bool choose)
        => ObserveByte(choose ? (byte)17 : (byte)18);

    public static uint SwitchExpression(int value)
        => ObserveUInt(value switch
        {
            0 => 0u,
            1 => 1u,
            _ => (uint)value,
        });

    public static uint Coalesce(uint? value)
        => ObserveUInt(value ?? uint.MaxValue);

    public static char Character(bool choose)
        => ObserveCharacter(choose ? 'a' : 'b');

    static byte ObserveByte(byte value) => value;
    static uint ObserveUInt(uint value) => value;
    static char ObserveCharacter(char value) => value;
}
