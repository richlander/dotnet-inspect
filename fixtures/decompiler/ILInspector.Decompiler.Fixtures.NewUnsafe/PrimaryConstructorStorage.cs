using System.Runtime.InteropServices;

namespace ILInspector.Decompiler.Fixtures.NewUnsafe;

[StructLayout(LayoutKind.Explicit)]
public class ExplicitSafePrimaryStorage(int value)
{
    [FieldOffset(0)]
    public safe readonly int Value = value;
}

[StructLayout(LayoutKind.Explicit)]
public struct ExplicitUnsafePrimaryStorage(int value)
{
    [FieldOffset(0)]
    public unsafe int Value = value;
}
