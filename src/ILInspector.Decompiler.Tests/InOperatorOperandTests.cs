using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A user-defined operator with `in`/`ref` parameters is invoked with the
// operands' addresses (ldarga/ldloca). The operator spelling must drop the
// address-of — `a != b`, not `(ref a) != (ref b)` (CS1525). This is the Roslyn
// `SeparatedSyntaxList<T>` op_Inequality used throughout the red-green tree
// `Update` methods (~31 corpus methods were malformed before the fix).
public class InOperatorOperandTests
{
    [Fact]
    public void InParameterOperator_RendersValueOperands_NotRefAddresses()
    {
        string body = RenderFixture(typeof(InOperatorProbe).FullName!, nameof(InOperatorProbe.Changed));

        Assert.Contains("arg != current", body);
        Assert.DoesNotContain("ref arg", body);
        Assert.DoesNotContain("(ref ", body);
    }

    [Fact]
    public void UserDefinedUnsignedRightShift_RendersOperator_NotExplicitCall()
    {
        string body = RenderFixture(typeof(ShiftProbe).FullName!, nameof(ShiftProbe.Shift));

        Assert.Contains("value >>> n", body);
        Assert.DoesNotContain("op_UnsignedRightShift", body);
        Assert.DoesNotContain("(ref ", body);
    }

    static string RenderFixture(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(InOperatorProbe).Assembly.Location);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        return result.Output!;
    }
}
