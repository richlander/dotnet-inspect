using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ConstructorCallDiagnosticsPassTests
{
    static readonly TypeRef Owner = TypeRef.CoreLib("Synthetic", "Owner");
    static readonly TypeRef Base = TypeRef.CoreLib("Synthetic", "Base");
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Unrelated = TypeRef.CoreLib("Synthetic", "Unrelated");
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
        var function = Function(".ctor", [new ExpressionStatement(call)], baseType: Base);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(function.Body.Blocks[0].Children));
        Assert.Same(call, statement.Expression);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void Run_LeavesParameterStoreBeforeParameterlessObjectBaseCallLiftable()
    {
        // The record / primary-constructor shape: the synthesized ctor stores its
        // parameters into the backing fields and then calls the implicit
        // parameterless object..ctor. object's ctor is a guaranteed no-op, so the
        // base call stays liftable (Full), not an owned residual — see #1639. The
        // param-storing field assignments render as body statements.
        var field = new FieldRef(Owner, "_value", Int32);
        var store = new StoreField(field, new LoadArgument(0, "this", Owner), new LoadArgument(1, "value", Int32));
        var ctor = new MethodRef(ObjectType, ".ctor", Void, [], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", Owner)]);
        var function = Function(".ctor", [store, new ExpressionStatement(call)], baseType: ObjectType);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(function.Body.Blocks[0].Children[1]);
        Assert.Same(call, statement.Expression);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void Run_MarksParameterStoreBeforeNonObjectBaseCallUnsupported()
    {
        // A parameterless base call to a NON-object base type is not provably a
        // no-op: eliding it would reorder the preceding param stores after a base
        // ctor that could observe pre-base state (e.g. via a virtual the derived
        // type overrides). It must stay an explicit residual, not silently Full.
        var field = new FieldRef(Owner, "_value", Int32);
        var store = new StoreField(field, new LoadArgument(0, "this", Owner), new LoadArgument(1, "value", Int32));
        var ctor = new MethodRef(Base, ".ctor", Void, [], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", Owner)]);
        var function = Function(".ctor", [store, new ExpressionStatement(call)], baseType: Base);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(function.Body.Blocks[0].Children[1]);
        Assert.IsType<UnsupportedNode>(statement.Expression);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void Run_MarksParameterStoreBeforeParameterizedBaseCallUnsupported()
    {
        // A non-parameterless base call preceded by a parameter store is NOT the
        // elidable implicit : base(); it carries arguments and has no faithful
        // statement spelling, so it must remain an explicit residual.
        var field = new FieldRef(Owner, "_value", Int32);
        var store = new StoreField(field, new LoadArgument(0, "this", Owner), new LoadArgument(1, "value", Int32));
        var ctor = new MethodRef(Base, ".ctor", Void, [Int32], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", Owner), new Constant(5, Int32)]);
        var function = Function(".ctor", [store, new ExpressionStatement(call)], baseType: Base);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(function.Body.Blocks[0].Children[1]);
        Assert.IsType<UnsupportedNode>(statement.Expression);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void Run_MarksUnrelatedConstructorCallOnThisUnsupported()
    {
        var ctor = new MethodRef(Unrelated, ".ctor", Void, [], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", Owner)]);
        var function = Function(".ctor", [new ExpressionStatement(call)], baseType: Base);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(function.Body.Blocks[0].Children));
        Assert.IsType<UnsupportedNode>(statement.Expression);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void Run_LeavesGenericThisConstructorChain()
    {
        var genericOwner = TypeRef.Definition("Synthetic", "Samples", "Owner`1");
        var genericInstance = TypeRef.GenericInstance(genericOwner, [TypeRef.GenericParameter(0, "T")]);
        var ctor = new MethodRef(genericInstance, ".ctor", Void, [], HasThis: true);
        var call = new Call(ctor, isVirtual: false, [new LoadArgument(0, "this", genericOwner)]);
        var function = Function(".ctor", [new ExpressionStatement(call)], owner: genericOwner);

        new ConstructorCallDiagnosticsPass().Run(function, PassContext.None);

        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(function.Body.Blocks[0].Children));
        Assert.Same(call, statement.Expression);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    static IrFunction Function(string name, IEnumerable<IrNode> statements, TypeRef? baseType = null, TypeRef? owner = null)
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

        return new IrFunction(name, owner ?? Owner, signature, [], body)
        {
            BaseType = baseType,
        };
    }
}
