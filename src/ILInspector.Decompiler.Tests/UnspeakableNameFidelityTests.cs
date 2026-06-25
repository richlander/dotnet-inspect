using System.Collections.Immutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Residual compiler-generated metadata names such as <c>&lt;&gt;c</c> and
/// <c>&lt;M&gt;b__0_0</c> are not valid C# identifiers. When raising leaves them
/// in the final IR, the output must degrade honestly instead of claiming Full.
/// </summary>
public class UnspeakableNameFidelityTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");

    static IrFunction Function(ImmutableArray<TypeRef> locals, BlockContainer body)
    {
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Object, signature, locals, body);
    }

    static BlockContainer Container(params IrNode[] statements)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        foreach (var statement in statements)
            block.Add(statement);
        return container;
    }

    [Fact]
    public void ResidualCompilerGeneratedTypeName_DegradesToPartial()
    {
        var displayClass = TypeRef.Definition("Synthetic", "Samples", "<>c__DisplayClass0_0");
        var ctor = new MethodRef(displayClass, ".ctor", Void, [], HasThis: false);
        var body = Container(
            new StoreLocal(0, displayClass, new NewObject(ctor, [])),
            new Return(null));

        var function = Function([displayClass], body);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void ResidualLambdaMethodName_DegradesToPartial()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "ClosureHolder");
        var lambda = new MethodRef(holder, "<M>b__0_0", Void, [], HasThis: false);
        var body = Container(
            new ExpressionStatement(new DelegateCreation(Action, lambda, isVirtual: false, new Constant(null, Object))),
            new Return(null));

        var function = Function([], body);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void AutoPropertyBackingField_StaysFull()
    {
        var declaringType = TypeRef.Definition("Synthetic", "Samples", "C");
        var backing = new FieldRef(declaringType, "<Count>k__BackingField", Int32)
        {
            BackingPropertyName = "Count",
        };
        var body = Container(new Return(new LoadField(backing, new LoadArgument(0, "this", declaringType))));
        var signature = new MethodSignature(Int32, [], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction("get_Count", declaringType, signature, [], body);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void NameOnlyBackingField_DegradesToPartial()
    {
        var declaringType = TypeRef.Definition("Synthetic", "Samples", "C");
        var backing = new FieldRef(declaringType, "<Count>k__BackingField", Int32);
        var body = Container(new Return(new LoadField(backing, new LoadArgument(0, "this", declaringType))));
        var signature = new MethodSignature(Int32, [], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction("M", declaringType, signature, [], body);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.DoesNotContain("this.Count", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void LocalFunctionMetadataName_StaysFull()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "C");
        var localFunction = new MethodRef(holder, "<M>g__Local|0_0", Void, [], HasThis: false);
        var body = Container(
            new ExpressionStatement(new DelegateCreation(Action, localFunction, isVirtual: false, new Constant(null, Object))),
            new Return(null));

        var function = Function([], body);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }
}
