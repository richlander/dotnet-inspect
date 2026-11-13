namespace ILInspector.Decompiler.Tests;

// Compiler-produced receiver spills: using the constant field directly emits
// ldsfld/stloc/ldloca, unlike first assigning its constant value to a named local.
public static class DecimalConstantReceiverSamples
{
    public static int Zero(decimal value) => decimal.Zero.CompareTo(value);
    public static int One(decimal value) => decimal.One.CompareTo(value);
    public static int MinusOne(decimal value) => decimal.MinusOne.CompareTo(value);
    public static int MinValue(decimal value) => decimal.MinValue.CompareTo(value);
    public static int MaxValue(decimal value) => decimal.MaxValue.CompareTo(value);
}
