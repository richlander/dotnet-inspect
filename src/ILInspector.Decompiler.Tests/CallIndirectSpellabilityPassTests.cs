using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class CallIndirectSpellabilityPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_intPtr = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef s_managedFp = TypeRef.FunctionPointer(s_int, [s_int], "");

    static IrFunction BuildCalli(IrExpression pointer, string convention, bool isInstance, int extraArgs = 0)
    {
        var arguments = new List<IrExpression>();
        for (int i = 0; i < extraArgs; i++)
            arguments.Add(new LoadArgument(2 + i, $"r{i}", s_int));
        arguments.Add(new LoadArgument(1, "x", s_int));
        var call = new CallIndirect(pointer, arguments, s_int, [s_int])
        {
            CallingConvention = convention,
            IsInstance = isInstance,
        };
        var block = new Block(0);
        block.Add(new Return(call));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_int, [new Parameter("p", pointer.ResultType ?? s_intPtr), new Parameter("x", s_int)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    // #1435 positive canary: a typed managed delegate*<int,int> operand with a
    // matching convention, non-instance, stays Full and renders fp(x).
    [Fact]
    public void TypedManagedDelegatePointer_StaysFullAndRendersInvocation()
    {
        var function = BuildCalli(new LoadArgument(0, "p", s_managedFp), convention: "", isInstance: false);

        new CallIndirectSpellabilityPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<CallIndirect>());
        Assert.Empty(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("p(x)", CSharpPrinter.Print(function).Output);
    }

    // #1435 (1): an untyped IntPtr operand is not a callable delegate*; fp(x) would
    // not compile. Must degrade to Partial with an honest marker.
    [Fact]
    public void UntypedPointerOperand_DegradesToPartial()
    {
        var function = BuildCalli(new LoadArgument(0, "p", s_intPtr), convention: "", isInstance: false);

        new CallIndirectSpellabilityPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CallIndirect>());
        Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.DoesNotContain("p(x)", CSharpPrinter.Print(function).Output);
    }

    // #1435 (2): an instance function pointer has no C# delegate* spelling (and the
    // receiver/parameter lists are off by one).
    [Fact]
    public void InstanceCalli_DegradesToPartial()
    {
        var function = BuildCalli(new LoadArgument(0, "p", s_managedFp), convention: "", isInstance: true, extraArgs: 1);

        new CallIndirectSpellabilityPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CallIndirect>());
        Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    // #1435 (3): the call-site convention does not match the operand delegate*'s
    // convention — fp(x) would mis-describe the call.
    [Fact]
    public void ConventionMismatch_DegradesToPartial()
    {
        var function = BuildCalli(new LoadArgument(0, "p", s_managedFp), convention: "unmanaged[Cdecl]", isInstance: false);

        new CallIndirectSpellabilityPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CallIndirect>());
        Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    // #1435 (adversarial review): the call-site signature disagrees with the
    // operand delegate*'s signature — a delegate*<int,int> (one parameter) invoked
    // through a two-parameter calli would render `p(a, b)`, invalid for the operand.
    [Fact]
    public void SignatureArityMismatch_DegradesToPartial()
    {
        var call = new CallIndirect(
            new LoadArgument(0, "p", s_managedFp),
            [new LoadArgument(1, "a", s_int), new LoadArgument(2, "b", s_int)],
            s_int,
            [s_int, s_int])
        {
            CallingConvention = "",
            IsInstance = false,
        };
        var block = new Block(0);
        block.Add(new Return(call));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_int, [new Parameter("p", s_managedFp), new Parameter("a", s_int), new Parameter("b", s_int)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new CallIndirectSpellabilityPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CallIndirect>());
        Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    // #1435 (adversarial review): a `&Method` address has a delegate* result type
    // but cannot be invoked directly — `(&Target)(x)` is invalid C# (CS0149); it
    // needs a `(delegate*<...>)` cast first. Decline rather than emit it at Full.
    [Fact]
    public void AddressOfMethodOperand_DegradesToPartial()
    {
        var target = new MethodRef(TypeRef.Definition("Synthetic", "", "T"), "Target", s_int, [s_int], HasThis: false);
        var call = new CallIndirect(new AddressOfMethod(target), [new LoadArgument(1, "x", s_int)], s_int, [s_int])
        {
            CallingConvention = "",
            IsInstance = false,
        };
        var block = new Block(0);
        block.Add(new Return(call));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_int, [new Parameter("x", s_int)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new CallIndirectSpellabilityPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<CallIndirect>());
        Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }
}


