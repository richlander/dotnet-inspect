using System.Collections.Immutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

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
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Action = TypeRef.CoreLib("System", "Action");
    static readonly TypeRef Target = TypeRef.Definition("Synthetic", "Samples", "Target");

    static IrFunction Function(ImmutableArray<TypeRef> locals, BlockContainer body)
    {
        var signature = new MethodSignature(Void, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Object, signature, locals, body);
    }

    static IrFunction Function(TypeRef returnType, ImmutableArray<Parameter> parameters, ImmutableArray<TypeRef> locals, BlockContainer body)
    {
        var signature = new MethodSignature(returnType, parameters, HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Target, signature, locals, body);
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
    public void CrossAssemblyBackingFieldEvidence_RecoversMatchingProperty()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location, locator: LocateTestAssembly);
        var declaringType = TypeRef.Definition(
            typeof(CfgSampleClass).Assembly.GetName().Name!,
            typeof(CfgSampleClass).Namespace!,
            nameof(CfgSampleClass));
        var backing = new FieldRef(declaringType, "<CompoundProperty>k__BackingField", Int32);

        var upgraded = source.CrossAssembly.Upgrade(backing);

        Assert.Equal("CompoundProperty", upgraded.BackingPropertyName);
    }

    [Fact]
    public void CrossAssemblyBackingFieldEvidence_DeclinesMissingProperty()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location, locator: LocateTestAssembly);
        var declaringType = TypeRef.Definition(
            typeof(CfgSampleClass).Assembly.GetName().Name!,
            typeof(CfgSampleClass).Namespace!,
            nameof(CfgSampleClass));
        var backing = new FieldRef(declaringType, "<Missing>k__BackingField", Int32);

        var upgraded = source.CrossAssembly.Upgrade(backing);

        Assert.Null(upgraded.BackingPropertyName);
    }

    static string? LocateTestAssembly(string assemblyName, AssemblyTrust _)
        => assemblyName == typeof(CfgSampleClass).Assembly.GetName().Name
            ? typeof(CfgSampleClass).Assembly.Location
            : null;

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

    [Fact]
    public void RaisedObjectInitializerUnspellableMember_DegradesToPartial()
    {
        var initializer = new ObjectInitializerExpression(
            NewTarget(),
            isCollection: false,
            [new InitializerEntry("bad-name", [new Constant(1, Int32)])]);
        var function = Function(Target, [], [], Container(new Return(initializer)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedNestedInitializerUnspellableMember_DegradesToPartial()
    {
        var nested = new InitializerBlock(
            isCollection: false,
            [new InitializerEntry("bad-name", [new Constant(1, Int32)])]);
        var initializer = new ObjectInitializerExpression(
            NewTarget(),
            isCollection: false,
            [new InitializerEntry("Inner", [nested])]);
        var function = Function(Target, [], [], Container(new Return(initializer)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedDeconstructionUnspellableFieldTarget_DegradesToPartial()
    {
        var tuple = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple`1"), [Int32]);
        var target = DeconstructionTarget.FieldTarget(new FieldRef(Target, "bad-name", Int32), isThisInstance: false);
        var deconstruction = new DeconstructionAssignment([target], new LoadArgument(0, "tuple", tuple));
        var function = Function(Void, [new Parameter("tuple", tuple)], [], Container(deconstruction, new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedDeconstructionUnspellablePropertyTarget_DegradesToPartial()
    {
        var tuple = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple`1"), [Int32]);
        var setter = new MethodRef(Target, "set_bad-name", Void, [Int32], HasThis: false);
        var target = DeconstructionTarget.Property(setter, instance: null, indexArguments: [], isVirtual: false);
        var deconstruction = new DeconstructionAssignment([target], new LoadArgument(0, "tuple", tuple));
        var function = Function(Void, [new Parameter("tuple", tuple)], [], Container(deconstruction, new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedRecursivePropertyPatternUnspellableProperty_DegradesToPartial()
    {
        var getter = new MethodRef(Target, "get_bad-name", Int32, [], HasThis: true);
        var pattern = new RecursivePropertyDeclarationPattern(
            new LoadArgument(0, "value", Target),
            getter,
            Int32,
            localIndex: 0);
        var function = Function(Boolean, [new Parameter("value", Target)], [Int32], Container(new Return(pattern)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedEventSubscriptionUnspellableEvent_DegradesToPartial()
    {
        var add = new MethodRef(Target, "add_bad-name", Void, [Action], HasThis: false);
        var subscription = new EventSubscription(
            add,
            isAdd: true,
            instance: null,
            new LoadArgument(0, "handler", Action));
        var function = Function(Void, [new Parameter("handler", Action)], [], Container(subscription, new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedAnonymousObjectUnspellableProperty_DegradesToPartial()
    {
        var anonymous = new AnonymousObject(Target, ["bad-name"], [new Constant(1, Int32)]);
        var function = Function(Target, [], [], Container(new Return(anonymous)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedObjectInitializerUsableMember_StaysFull()
    {
        var initializer = new ObjectInitializerExpression(
            NewTarget(),
            isCollection: false,
            [new InitializerEntry("GoodName", [new Constant(1, Int32)])]);
        var function = Function(Target, [], [], Container(new Return(initializer)));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void RaisedEventSubscriptionUsableEvent_StaysFull()
    {
        var add = new MethodRef(Target, "add_GoodName", Void, [Action], HasThis: false);
        var subscription = new EventSubscription(
            add,
            isAdd: true,
            instance: null,
            new LoadArgument(0, "handler", Action));
        var function = Function(Void, [new Parameter("handler", Action)], [], Container(subscription, new Return(null)));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    static NewObject NewTarget()
        => new(new MethodRef(Target, ".ctor", Void, [], HasThis: true), []);
}
