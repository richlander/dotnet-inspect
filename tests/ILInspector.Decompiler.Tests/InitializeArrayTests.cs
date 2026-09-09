using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A constant array is lowered to `new T[N]; RuntimeHelpers.InitializeArray(arr,
// ldtoken <PrivateImplementationDetails>.blob)`. The `ldtoken` of the angle-bracketed
// field renders as a comment, leaving `InitializeArray(arr, )` — CS1525. The pass
// decodes the RVA blob and folds it back into `new T[] { ... }`.
public class InitializeArrayTests
{
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void InitializeArray_FoldsBlobIntoArrayLiteral()
    {
        var output = Render(nameof(CfgSampleClass.RvaIntArray));

        Assert.Contains("new int[] { 11, 22, 33, 44, 55, 66, 77, 88 }", output);
        Assert.DoesNotContain("InitializeArray", output);
        Assert.DoesNotContain("LoadToken", output);
    }
}
