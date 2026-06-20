using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class TypedConstantsPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    [Fact]
    public void BoolStoredThroughByRef_RetypesConstantToBool()
    {
        // `out bool flag = true` lowers to `stind.i1` over a `ref bool`, whose opcode
        // width is sbyte. Retyping by the address's pointee type (bool), not the
        // opcode type, recovers the boolean literal — otherwise the output is
        // `flag = 1;`, which is not valid C# (CS0029).
        var function = Raised(nameof(CfgSampleClass.SetFlag));
        var store = Assert.Single(function.Descendants.OfType<StoreIndirect>());

        var constant = Assert.IsType<Constant>(store.Value);
        Assert.IsType<bool>(constant.Value);
        Assert.Equal(true, constant.Value);
    }

    [Fact]
    public void BoolStoredThroughByRef_RendersBooleanLiteral()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.SetFlag))).Output;

        Assert.NotNull(output);
        Assert.Contains("flag = true;", output);
        Assert.DoesNotContain("flag = 1;", output);
    }
}
