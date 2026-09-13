namespace ILInspector.Decompiler.Tests;

// Issue: a user-defined operator with `in`/`ref` parameters is called with the
// operands' addresses (ldarga/ldloca). The operator must spell as `a != b`, not
// `(ref a) != (ref b)` — the latter is CS1525 "Invalid expression term 'ref'".
// Mirrors the Roslyn `SeparatedSyntaxList<T>` op_Inequality used throughout the
// red-green tree `Update` methods.
public readonly struct InOperatorVec
{
    public readonly int X;
    public InOperatorVec(int x) { X = x; }
    public static bool operator ==(in InOperatorVec a, in InOperatorVec b) => a.X == b.X;
    public static bool operator !=(in InOperatorVec a, in InOperatorVec b) => a.X != b.X;
    public override bool Equals(object? o) => o is InOperatorVec v && this == v;
    public override int GetHashCode() => X;
}

public class InOperatorProbe
{
    public InOperatorVec Field;

    public bool Changed(InOperatorVec arg)
    {
        InOperatorVec current = Field;
        return arg != current;
    }
}

// C# 11 user-defined unsigned right shift with an `in` left operand: must
// operator-spell `a >>> n`, not the CS0571 explicit op_UnsignedRightShift call.
public readonly struct ShiftBox
{
    public readonly int Bits;
    public ShiftBox(int bits) { Bits = bits; }
    public static ShiftBox operator >>>(in ShiftBox a, int n) => new ShiftBox(a.Bits >> n);
}

public static class ShiftProbe
{
    public static ShiftBox Shift(ShiftBox value, int n) => value >>> n;
}

public readonly struct BoolBox
{
    public readonly int Value;
    public BoolBox(int value) { Value = value; }
    public static bool operator true(in BoolBox value) => value.Value != 0;
    public static bool operator false(in BoolBox value) => value.Value == 0;
}

public static class BoolBoxProbe
{
    public static bool Choose(BoolBox value) => value ? true : false;

    public static int Branch(BoolBox value)
    {
        if (value)
            return 1;
        return 2;
    }
}
