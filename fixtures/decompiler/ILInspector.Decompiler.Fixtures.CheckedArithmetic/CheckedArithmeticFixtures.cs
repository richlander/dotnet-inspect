using System;

namespace ILInspector.Decompiler.Fixtures.CheckedArithmetic;

/// <summary>
/// Representative source compiled with project-wide overflow checking enabled.
/// Plain arithmetic here lowers to checked IL opcodes, unlike the repo default
/// unchecked context.
/// </summary>
public static class CheckedArithmeticFixtures
{
    public static int Add(int left, int right) => left + right;

    public static int Subtract(int left, int right) => left - right;

    public static int Multiply(int left, int right) => left * right;

    public static int Increment(int value)
    {
        value++;
        return value;
    }

    public static int CompoundAdd(int value, int delta)
    {
        value += delta;
        return value;
    }

    public static short NarrowToInt16(int value) => (short)value;

    public static byte AddThenNarrowToByte(int left, int right) => (byte)(left + right);

    public static ulong SignedToUInt64(long value) => (ulong)value;

    public static int UncheckedIsland(int left, int right) => unchecked(left + right);

    public static int CatchesOverflow(int left, int right)
    {
        try
        {
            return left + right;
        }
        catch (OverflowException)
        {
            return -1;
        }
    }
}
