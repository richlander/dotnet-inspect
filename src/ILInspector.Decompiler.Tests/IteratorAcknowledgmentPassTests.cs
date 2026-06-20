using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IteratorAcknowledgmentPassTests
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

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void IteratorKickoff_ReplacesHandoffWithHonestMarker()
    {
        var function = Raised(nameof(CfgSampleClass.YieldTwo));

        // The misleading `return new <YieldTwo>d__0(-2);` handoff is gone.
        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.DoesNotContain(function.Descendants.OfType<Return>(), _ => true);

        // An honest iterator marker stands in its place.
        var marker = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal("iterator", marker.Opcode);
        Assert.Contains("yield body", marker.Reason);
        Assert.Contains(">d__", marker.Reason);
    }

    [Fact]
    public void IteratorKickoff_CapsFidelityAtPartial()
    {
        Assert.Equal(DecompilationFidelity.Partial, Raised(nameof(CfgSampleClass.YieldTwo)).Fidelity);
    }

    [Fact]
    public void IteratorKickoff_RendersMarkerComment_NotAStub()
    {
        var output = Print(nameof(CfgSampleClass.YieldTwo));

        Assert.Contains("iterator", output);
        Assert.Contains("not reconstructed", output);
        // No plausible-but-meaningless state-machine construction stub.
        Assert.DoesNotContain("return new", output);
    }

    [Fact]
    public void ParameterizedIterator_IsAlsoAcknowledged()
    {
        var function = Raised(nameof(CfgSampleClass.YieldRange));

        var marker = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal("iterator", marker.Opcode);
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void NonIteratorReturningEnumerable_IsNotAcknowledged()
    {
        var function = Raised(nameof(CfgSampleClass.NotAnIterator));

        // No iterator marker: the method has no state machine to acknowledge.
        Assert.DoesNotContain(function.Descendants.OfType<UnsupportedNode>(), u => u.Opcode == "iterator");
        Assert.Contains("source", Print(nameof(CfgSampleClass.NotAnIterator)));
    }
}
