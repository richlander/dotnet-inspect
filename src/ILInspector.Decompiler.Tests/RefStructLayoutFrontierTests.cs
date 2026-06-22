using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RefStructLayoutFrontierTests
{
    static IrFunction Import(Type type, string methodName)
    {
        using var source = MetadataSource.Open(typeof(LifetimeSampleClass).Assembly.Location);
        string typeName = type.FullName!.Replace('+', '.');
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        return function!;
    }

    static IrFunction Raised(Type type, string methodName)
    {
        var function = Import(type, methodName);
        IrPasses.Run(function);
        function.CheckInvariant();
        return function;
    }

    [Fact]
    public void RefStructWithSpanField_CallerImportsAndPrintsAtFullFidelity()
    {
        var function = Raised(typeof(LifetimeSampleClass), nameof(LifetimeSampleClass.ReadSpanBackedView));
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Diagnostics);
        Assert.Contains("SpanBackedView view = new SpanBackedView(span);", output);
        Assert.Contains("return view.At(index) + view.Length;", output);
        Assert.DoesNotContain("IsByRefLike", output);
        Assert.DoesNotContain("/*", output);
    }

    [Fact]
    public void RefStructWithSpanField_MembersImportAndPrintAtFullFidelity()
    {
        var type = typeof(LifetimeSampleClass.SpanBackedView);

        var ctor = Raised(type, ".ctor");
        var at = Raised(type, nameof(LifetimeSampleClass.SpanBackedView.At));
        var length = Raised(type, "get_Length");

        Assert.Equal(DecompilationFidelity.Full, ctor.Fidelity);
        Assert.Equal(DecompilationFidelity.Full, at.Fidelity);
        Assert.Equal(DecompilationFidelity.Full, length.Fidelity);

        Assert.Contains("_span = span;", CSharpPrinter.PrintRaised(ctor).Output);
        Assert.Contains("return _span[index];", CSharpPrinter.PrintRaised(at).Output);
        Assert.Contains("return _span.Length;", CSharpPrinter.PrintRaised(length).Output);
    }
}
