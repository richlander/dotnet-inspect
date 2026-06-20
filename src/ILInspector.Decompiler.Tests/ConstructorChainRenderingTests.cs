using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A `call` (not `newobj`) to a constructor is only ever a this(...)/base(...)
// chain, so its receiver is always `this`. The import sometimes routes `this`
// through a copied temp (`MyType V_0 = this; V_0..ctor(...)`) instead of a bare
// ldarg.0; the printer must still spell the chain by keyword and never leak the
// `.ctor` member name (which is never valid C#).
public class ConstructorChainRenderingTests
{
    static readonly TypeRef Derived = TypeRef.CoreLib("System", "MyDerived");
    static readonly TypeRef Base = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    static DecompilerResult RenderConstructor(TypeRef chainDeclaringType, IrExpression receiver)
    {
        var ctor = new MethodRef(chainDeclaringType, ".ctor", Void, [Int32], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [receiver, new Constant(5, Int32)]);
        var entry = new Block(0);
        entry.Add(new ExpressionStatement(call));
        entry.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(Void, [new Parameter("value", Int32)], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction(".ctor", Derived, signature, [Derived], container);
        return CSharpPrinter.Print(function);
    }

    [Fact]
    public void CopiedThisReceiver_RendersBaseChain()
    {
        var result = RenderConstructor(Base, new LoadLocal(0, Derived));

        Assert.Equal("base(5)", result.ConstructorChain);
        Assert.DoesNotContain(".ctor", result.Output);
    }

    [Fact]
    public void CopiedThisReceiver_RendersThisChain()
    {
        var result = RenderConstructor(Derived, new LoadLocal(0, Derived));

        Assert.Equal("this(5)", result.ConstructorChain);
        Assert.DoesNotContain(".ctor", result.Output);
    }

    [Fact]
    public void DirectThisReceiver_StillRendersBaseChain()
    {
        var result = RenderConstructor(Base, new LoadArgument(0, "this", Derived));

        Assert.Equal("base(5)", result.ConstructorChain);
        Assert.DoesNotContain(".ctor", result.Output);
    }
}
