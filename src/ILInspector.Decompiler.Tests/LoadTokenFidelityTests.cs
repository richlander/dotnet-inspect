using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Type tokens raise to <c>typeof(T)</c>; method and field tokens have no C#
/// expression spelling unless a pass consumes them. Left flat, the printer can
/// only emit a comment, which is invalid in expression positions.
/// </summary>
public class LoadTokenFidelityTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    static IrFunction Function(IrExpression expression)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ExpressionStatement(expression));
        block.Add(new Return(null));

        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Object, signature, [], container);
    }

    [Fact]
    public void FieldToken_DegradesToPartialAndReportsRemark()
    {
        var function = Function(new LoadToken(RuntimeTokenKind.Field, null, "C.F"));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var remark = Assert.Single(FidelityRemarks.Collect(function),
            r => r.Code == DiagnosticIds.UnsupportedRuntimeToken);
        Assert.Contains("ldtoken", remark.Reason);
    }

    [Fact]
    public void MethodToken_DegradesToPartial()
    {
        var function = Function(new LoadToken(RuntimeTokenKind.Method, null, "C.M"));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void TypeToken_StaysFull()
    {
        var function = Function(new LoadToken(RuntimeTokenKind.Type, Object, Object.ToDisplayString()));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(FidelityRemarks.Collect(function));
    }
}
