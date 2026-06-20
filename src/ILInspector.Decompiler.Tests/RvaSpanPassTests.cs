using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RvaSpanPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_runtimeFieldHandle = TypeRef.CoreLib("System", "RuntimeFieldHandle");
    static readonly TypeRef s_readOnlySpanInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [s_int]);

    [Fact]
    public void CreateSpan_FromUserRuntimeHelpersLookalike_IsNotRaised()
    {
        var function = BuildCreateSpanLookalike();

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<SpanLiteral>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "CreateSpan");
        function.CheckInvariant();
    }

    static IrFunction BuildCreateSpanLookalike()
    {
        var createSpan = new MethodRef(
            TypeRef.Definition("UserAssembly", "System.Runtime.CompilerServices", "RuntimeHelpers"),
            "CreateSpan",
            s_readOnlySpanInt,
            [s_runtimeFieldHandle],
            HasThis: false)
        {
            TypeArguments = [s_int],
        };
        var token = new LoadToken(RuntimeTokenKind.Field, null, "User.Field")
        {
            FieldRvaData = [1, 0, 0, 0],
        };

        var block = new Block();
        block.Add(new Return(new Call(createSpan, isVirtual: false, [token])));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(s_readOnlySpanInt, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [], body);
    }
}
