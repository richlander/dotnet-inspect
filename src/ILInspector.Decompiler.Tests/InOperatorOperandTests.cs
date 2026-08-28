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

    [Fact]
    public void OperatorTrueInConditional_RendersOperand_NotExplicitCall()
    {
        string body = RenderFixture(typeof(BoolBoxProbe).FullName!, nameof(BoolBoxProbe.Choose));

        Assert.Contains("return value ? 1 : 0;", body);
        Assert.DoesNotContain("op_True", body);
        Assert.DoesNotContain("op_False", body);
    }

    [Fact]
    public void OperatorTrueInIf_RendersOperand_NotExplicitCall()
    {
        string body = RenderFixture(typeof(BoolBoxProbe).FullName!, nameof(BoolBoxProbe.Branch));

        Assert.Contains("if (value)", body);
        Assert.DoesNotContain("op_True", body);
        Assert.DoesNotContain("op_False", body);
    }

    [Fact]
    public void OperatorFalse_DoesNotGenerateInvalidLogicalNot()
    {
        var boolBox = TypeRef.Definition("Test", "Samples", "BoolBox");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var opFalse = new MethodRef(boolBox, "op_False", boolType, [TypeRef.ByRef(boolBox)], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };
        var body = new BlockContainer();
        var block = new Block();
        var then = new Block();
        then.Add(new Return(new Constant(1, intType)));
        block.Add(new IfStatement(
            new Call(opFalse, isVirtual: false, [new LoadArgumentAddress(0, "value", TypeRef.ByRef(boolBox))]),
            then,
            null));
        block.Add(new Return(new Constant(2, intType)));
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Test", "Samples", "Owner"),
            new MethodSignature(intType, [new Parameter("value", boolBox)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output;

        Assert.NotNull(output);
        Assert.Contains("if ((value ? false : true))", output);
        Assert.DoesNotContain("!value", output);
        Assert.DoesNotContain("op_False", output);
    }

    [Fact]
    public void KnownOperatorWithoutValueSpelling_DegradesRenderedInvocation()
    {
        var truthy = TypeRef.Definition("Test", "Samples", "Truthy");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var opTrue = new MethodRef(
            truthy,
            "op_True",
            boolType,
            [truthy],
            HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new Return(
            new Call(
                opTrue,
                isVirtual: false,
                [new LoadArgument(0, "value", truthy)])));
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Test", "Samples", "Owner"),
            new MethodSignature(
                boolType,
                [new Parameter("value", truthy)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains("Truthy.op_True(value)", result.Output);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id
                == DiagnosticIds.UnrepresentableMetadataName);
    }

    static string RenderFixture(string typeName, string methodName)
    {
        using var source = MetadataSource.Open(typeof(InOperatorProbe).Assembly.Location);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function!.Fidelity);
        var result = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        Assert.NotNull(result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        return result.Output!;
    }
}
