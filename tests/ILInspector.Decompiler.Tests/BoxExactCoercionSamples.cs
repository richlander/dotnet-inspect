namespace ILInspector.Decompiler.Tests;

// #9437: csc boxes each value at the type written in source. The decompiler
// must spell every boxed integer at exactly that type, or the recompiled
// body boxes a different one. The unchanged samples pin the boxes that were
// already spelled correctly.
public static class BoxExactCoercionSamples
{
    public static object[] NarrowLiterals() => new object[] { (sbyte)-1, (ushort)65535, (short)-1, (byte)255, (short)1 };

    public static object[] UnsignedLiterals() => new object[] { 5u, 5ul, 3000000000u, 0xFFFFFFFFFFul };

    public static object[] NativeLiterals() => new object[] { (nint)5, (nuint)5 };

    public static object WidenedByte(byte b) => (int)b;

    public static object WidenedChar(char c) => (int)c;

    // Roslyn's JsonWriter.AppendCharAsUnicode shape: a boxed char would make
    // the x4 format throw.
    public static string HexChar(char c) => string.Format("{0:x4}", (int)c);

    public static short UnboxOverBox() => (short)(object)(short)1;

    public static object[] Unchanged(int i, long l) => new object[] { 1, 'c', true, 2L, i, l, 1.5f, 2.5d, StringComparison.Ordinal, (StringComparison)99 };
}
