using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class FunctionPointerDiagnosticsPassTests
{
    private static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "Owner");
    private static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    private static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    [Fact]
    public void Run_DiagnosesVirtualFunctionPointerAsLdvirtftn()
    {
        var method = new MethodRef(Owner, "VirtualTarget", Void, [], HasThis: true);
        var function = FunctionWith(new LoadFunctionPointer(method, isVirtual: true, new LoadArgument(0, "this", Owner)));

        new FunctionPointerDiagnosticsPass().Run(function, PassContext.None);

        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedFunctionPointer, diagnostic.Id);
        Assert.Contains("ldvirtftn", diagnostic.Message);
        Assert.Contains("requires a receiver", diagnostic.Message);
        Assert.Contains("VirtualTarget", diagnostic.Message);
        Assert.DoesNotContain("ldftn:", diagnostic.Message);
    }

    [Fact]
    public void Run_DiagnosesStaticFunctionPointerAsLdftn()
    {
        var method = new MethodRef(Owner, "StaticTarget", Void, [], HasThis: false);
        var function = FunctionWith(new LoadFunctionPointer(method, isVirtual: false, instance: null));

        new FunctionPointerDiagnosticsPass().Run(function, PassContext.None);

        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedFunctionPointer, diagnostic.Id);
        Assert.Contains("ldftn", diagnostic.Message);
        Assert.Contains("not a delegate construction", diagnostic.Message);
        Assert.Contains("StaticTarget", diagnostic.Message);
        Assert.DoesNotContain("ldvirtftn", diagnostic.Message);
    }

    private static IrFunction FunctionWith(IrExpression expression)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new ExpressionStatement(expression));
        body.Add(block);

        var signature = new MethodSignature(
            Void,
            ImmutableArray.Create(new Parameter("this", Owner)),
            HasThis: true,
            GenericParameterCount: 0);

        return new IrFunction("M", Owner, signature, [], body);
    }
}
