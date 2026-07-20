using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class HarnessOpcodeTests
{
    [Theory]
    [InlineData("ldarg.0", "ldarg")]
    [InlineData("ldarg.s", "ldarg")]
    [InlineData("ldarga.s", "ldarga")]
    [InlineData("ldloc.3", "ldloc")]
    [InlineData("ldloc.s", "ldloc")]
    [InlineData("ldloca.s", "ldloca")]
    [InlineData("stloc.0", "stloc")]
    [InlineData("stloc.s", "stloc")]
    [InlineData("ldc.i4.7", "ldc.i4")]
    [InlineData("ldc.i4.s", "ldc.i4")]
    public void Canonicalize_NormalizesMacrosWithoutCollapsingAddressLoads(
        string opcode,
        string expected)
    {
        Assert.Equal(expected, HarnessOpcode.Canonicalize(opcode));
    }

    [Theory]
    [InlineData("ldarg", "ldarga")]
    [InlineData("ldloc", "ldloca")]
    public void Canonicalize_PreservesValueVersusAddressLoads(string valueLoad, string addressLoad)
    {
        Assert.NotEqual(
            HarnessOpcode.Canonicalize(valueLoad),
            HarnessOpcode.Canonicalize(addressLoad));
    }
}
