using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ConstructorCallDiagnosticsPassTests
{
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "Owner");
    static readonly TypeRef Base = TypeRef.CoreLib("Synthetic", "Base");
    static readonly TypeRef StructType = TypeRef.CoreLib("Synthetic", "StructType");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Fact]
    public void Run_MarksUnraisedDirectConstructorCallUnsupported()
    {
        var ctor = new MethodRef(StructType, ".ctor", Void, [Int32], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadStackSlot(0, StructType), new Constant(5, Int32)]);
        var function = Function("M", [new ExpressionStatement(call)]);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(function.Body.Blocks[0].Children));
        var marker = Assert.IsType<UnsupportedNode>(statement.Expression);
        Assert.Contains("direct constructor call", marker.Reason);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void Run_LeavesLiftableConstructorChain()
    {
        var ctor = new MethodRef(Base, ".ctor", Void, [Int32], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", Owner), new Constant(5, Int32)]);
        var function = Function(".ctor", [new ExpressionStatement(call)]);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(function.Body.Blocks[0].Children));
        Assert.Same(call, statement.Expression);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void Run_MarksConstructorChainWithParameterStoreBeforeCallUnsupported()
    {
        var field = new FieldRef(Owner, "_value", Int32);
        var store = new StoreField(field, new LoadArgument(0, "this", Owner), new LoadArgument(1, "value", Int32));
        var ctor = new MethodRef(Base, ".ctor", Void, [], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", Owner)]);
        var function = Function(".ctor", [store, new ExpressionStatement(call)]);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(function.Body.Blocks[0].Children[1]);
        Assert.IsType<UnsupportedNode>(statement.Expression);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    static IrFunction Function(string name, IEnumerable<IrNode> statements)
    {
        var body = new BlockContainer();
        var block = new Block();
        foreach (var statement in statements)
            block.Add(statement);
        body.Add(block);

        var signature = new MethodSignature(
            Void,
            ImmutableArray.Create(new Parameter("value", Int32)),
            HasThis: true,
            GenericParameterCount: 0);

        return new IrFunction(name, Owner, signature, [], body);
    }
}
