using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LoadIndirectBoolTests
{
    static IrFunction Imported(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function!;
    }

    static string Print(string methodName)
    {
        var function = Imported(methodName);
        IrPasses.Run(function);
        function.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void LoadIndirectOverRefBool_TypesAsBool()
    {
        // `ldind.u1` carries a byte storage width; the address is `ref bool`, so the
        // loaded value's type is the bool pointee, not byte.
        var load = Imported(nameof(CfgSampleClass.RefBoolIsClear))
            .Descendants.OfType<LoadIndirect>().First();

        Assert.Equal("Boolean", load.ResultType?.Name);
    }

    [Fact]
    public void RefBoolComparedToConstant_RecoversBoolNotInt()
    {
        // `flag == 0` is CS0019; the bool pointee lets the constant retype and fold.
        var output = Print(nameof(CfgSampleClass.RefBoolIsClear));

        Assert.Contains("!(flag)", output);
        Assert.DoesNotContain("== 0", output);
    }
}
