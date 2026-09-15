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
    static readonly TypeRef Unrelated = TypeRef.CoreLib("System", "Unrelated");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef StringArray = TypeRef.SzArray(TypeRef.CoreLib("System", "String"));
    static readonly TypeRef StringEnumerable = TypeRef.GenericInstance(
        TypeRef.CoreLib("System.Collections.Generic", "IEnumerable`1"), [TypeRef.CoreLib("System", "String")]);

    // Renders a this(...) chain to a single-parameter constructor whose parameter
    // is `paramType`, passing `argument`. The printer must spell the argument at
    // its exact parameter type so the recompiled shell rebinds to the same
    // overload the IL selected — a null/reference-upcast argument that reads back
    // without its parameter type silently rebinds to a narrower sibling overload.
    static DecompilerResult RenderChain(TypeRef paramType, IrExpression argument)
    {
        var ctor = new MethodRef(Derived, ".ctor", Void, [paramType], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", Derived), argument]);
        var entry = new Block(0);
        entry.Add(new ExpressionStatement(call));
        entry.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(Void, [new Parameter("value", paramType)], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction(".ctor", Derived, signature, [Derived], container)
        {
            BaseType = Base,
        };
        return CSharpPrinter.Print(function);
    }

    static DecompilerResult RenderConstructor(TypeRef chainDeclaringType, IrExpression receiver, bool diagnose = false)
    {
        var ctor = new MethodRef(chainDeclaringType, ".ctor", Void, [Int32], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [receiver, new Constant(5, Int32)]);
        var entry = new Block(0);
        entry.Add(new ExpressionStatement(call));
        entry.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(Void, [new Parameter("value", Int32)], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction(".ctor", Derived, signature, [Derived], container)
        {
            BaseType = Base,
        };
        if (diagnose)
            new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);
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

    [Fact]
    public void UnrelatedConstructorCallOnThis_DoesNotRenderAsChain()
    {
        var result = RenderConstructor(Unrelated, new LoadArgument(0, "this", Derived), diagnose: true);

        Assert.Null(result.ConstructorChain);
        Assert.Contains("direct constructor call", result.Output);
    }

    // A null literal is type-less, so C# overload resolution re-picks the most
    // specific applicable sibling for the recompiled `this(null)` — a narrower
    // `C(string)` beats the original `C(object)`. Spelling `(object)null` pins the
    // parameter type so the shell rebinds to the constructor the IL selected.
    [Fact]
    public void NullArgument_CastsToReferenceParameterType()
    {
        var result = RenderChain(Object, new Constant(null, Object));

        Assert.Equal("this((object)null)", result.ConstructorChain);
    }

    // string[] is assignable to IEnumerable<string> by array covariance (a no-op
    // reference conversion, no IL). Reading the argument back bare would let it
    // bind to a same-arity sibling that accepts string[] exactly; the (IEnumerable
    // <string>) cast keeps the recompiled shell on the covariant overload (#2726).
    [Fact]
    public void AssignableArgument_CastsToWiderReferenceParameterType()
    {
        var result = RenderChain(StringEnumerable, new LoadLocal(0, StringArray));

        Assert.StartsWith("this((", result.ConstructorChain);
        Assert.Contains("IEnumerable<string>)", result.ConstructorChain);
    }

    // The argument already has the parameter type: no fidelity cast, no noise.
    [Fact]
    public void IdentityReferenceArgument_IsNotCast()
    {
        var result = RenderChain(Object, new LoadLocal(0, Object));

        Assert.Equal("this(V_0)", result.ConstructorChain);
    }

    // Value-typed parameters keep the numeric-coercion path; no reference cast.
    [Fact]
    public void ValueTypeArgument_IsNotCast()
    {
        var result = RenderChain(Int32, new Constant(5, Int32));

        Assert.Equal("this(5)", result.ConstructorChain);
    }
}
