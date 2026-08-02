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
    public void ResidualLambdaMethodName_RendersSanitizedNotRaw()
    {
        // The delegate target is an un-raised lambda body method group. The body
        // must degrade honestly (Partial) AND render a parseable fallback spelling
        // rather than leaking the raw <M>b__0_0 into the method group (#3129).
        var holder = TypeRef.Definition("Synthetic", "Samples", "ClosureHolder");
        var lambda = new MethodRef(holder, "<M>b__0_0", Void, [], HasThis: false);
        var body = Container(
            new ExpressionStatement(new DelegateCreation(Action, lambda, isVirtual: false, new Constant(null, Object))),
            new Return(null));

        var function = Function([], body);
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.DoesNotContain("<M>b__0_0", output);
        Assert.DoesNotContain('<', output);
        Assert.Contains("__M_b__0_0", output);
    }

    [Fact]
    public void RaisedObjectInitializerUnspellableMember_RendersSanitizedNotRaw()
    {
        // A residual state-machine hoisted-parameter field (<>3__first) used as an
        // object-initializer member must render a parseable fallback spelling, not
        // leak the raw <>3__first (which parses as CS1001) (#3129).
        var initializer = new ObjectInitializerExpression(
            NewTarget(),
            isCollection: false,
            [new InitializerEntry("<>3__first", [new Constant(1, Int32)])]);
        var function = Function(Target, [], [], Container(new Return(initializer)));
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.DoesNotContain("<>3__first", output);
        Assert.DoesNotContain('<', output);
        Assert.Contains("___3__first = 1", output);
    }

    [Fact]
    public void ConstructorDelegateTarget_DegradesToPartial()
    {
        // A delegate over an instance constructor (ldftn .ctor) has no C#
        // method-group spelling. The name sanitizer renders a legal __ctor
        // fallback identifier, so fidelity must still degrade to Partial —
        // otherwise the fabricated name is presented as Full and, if a real
        // __ctor member exists, silently binds an unrelated method. The shared
        // spellability check exempts .ctor for the constructor-CALL position
        // (base(...)/this(...)); a method-group target must not inherit that
        // exemption (#3129 adversarial-review finding).
        var ctor = new MethodRef(Target, ".ctor", Void, [], HasThis: true);
        var body = Container(
            new ExpressionStatement(new DelegateCreation(Action, ctor, isVirtual: false, new LoadLocal(0, Target))),
            new Return(null));

        var function = Function([Target], body);
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.DoesNotContain(".ctor", output);
        Assert.Contains("__ctor", output);
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
        using var source = MetadataSource.Open(
            typeof(object).Assembly.Location,
            null,
            TestAssemblyReferenceResolvers.SingleAssembly(typeof(CfgSampleClass).Assembly.Location));
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
        using var source = MetadataSource.Open(
            typeof(object).Assembly.Location,
            null,
            TestAssemblyReferenceResolvers.SingleAssembly(typeof(CfgSampleClass).Assembly.Location));
        var declaringType = TypeRef.Definition(
            typeof(CfgSampleClass).Assembly.GetName().Name!,
            typeof(CfgSampleClass).Namespace!,
            nameof(CfgSampleClass));
        var backing = new FieldRef(declaringType, "<Missing>k__BackingField", Int32);

        var upgraded = source.CrossAssembly.Upgrade(backing);

        Assert.Null(upgraded.BackingPropertyName);
    }

    [Fact]
    public void CrossAssemblyFacts_UseDescriptorStreamWhenPathIsInformational()
    {
        using var source = MetadataSource.Open(
            typeof(object).Assembly.Location,
            null,
            new StreamOnlyResolver(typeof(CfgSampleClass).Assembly.Location));
        var declaringType = TypeRef.Definition(
            typeof(CfgSampleClass).Assembly.GetName().Name!,
            typeof(CfgSampleClass).Namespace!,
            nameof(CfgSampleClass));
        var backing = new FieldRef(
            declaringType,
            "<CompoundProperty>k__BackingField",
            Int32);

        FieldRef upgraded = source.CrossAssembly.Upgrade(backing);

        Assert.Equal("CompoundProperty", upgraded.BackingPropertyName);
    }

    [Fact]
    public void CrossAssemblyInterfaceWalk_FollowsLocalTypeDefinitions()
    {
        using var source = MetadataSource.Open(
            typeof(object).Assembly.Location,
            null,
            TestAssemblyReferenceResolvers.SingleAssembly(
                typeof(CrossAssemblyLocalCollectionDerived).Assembly.Location));
        TypeRef type = TypeRef.Definition(
            typeof(CrossAssemblyLocalCollectionDerived).Assembly.GetName().Name!,
            typeof(CrossAssemblyLocalCollectionDerived).Namespace!,
            nameof(CrossAssemblyLocalCollectionDerived));

        Assert.Equal(
            MetadataFactState.Yes,
            source.SupportsCollectionInitializer(type));
    }

    [Fact]
    public void CrossAssemblyInterfaceCache_IncludesGenericArguments()
    {
        using var source = MetadataSource.Open(
            typeof(object).Assembly.Location,
            null,
            TestAssemblyReferenceResolvers.SingleAssembly(
                typeof(CrossAssemblyGenericEquatable<>).Assembly.Location));
        TypeRef definition = TypeRef.Definition(
            typeof(CrossAssemblyGenericEquatable<>).Assembly.GetName().Name!,
            typeof(CrossAssemblyGenericEquatable<>).Namespace!,
            "CrossAssemblyGenericEquatable`1");
        TypeRef stringType = TypeRef.CoreLib("System", "String");
        TypeRef intInstance = TypeRef.GenericInstance(definition, [Int32]);
        TypeRef equatableDefinition =
            TypeRef.CoreLib("System", "IEquatable`1");
        TypeRef equatableInt =
            TypeRef.GenericInstance(equatableDefinition, [Int32]);
        TypeRef equatableString =
            TypeRef.GenericInstance(equatableDefinition, [stringType]);

        Assert.Equal(
            MetadataFactState.Unknown,
            source.CrossAssembly.Implements(intInstance, equatableString));
        Assert.Equal(
            MetadataFactState.Yes,
            source.CrossAssembly.Implements(intInstance, equatableInt));
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
        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("value is { bad-name: int V_0 }", output);
        Assert.DoesNotContain("{ _bad_name:", output);
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

    [Fact]
    public void RaisedLocalFunctionInvocationUnspellableName_DegradesToPartial()
    {
        // bad-name(); — the raised invocation carries the demangled name after the
        // original Call that MethodReason would have flagged was replaced.
        var invocation = new LocalFunctionInvocation("bad-name", Void, []);
        var function = Function([], Container(new ExpressionStatement(invocation), new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedLocalFunctionKeywordName_StaysFull()
    {
        // A keyword local-function name escapes to @return (valid C#) per #1465, so
        // the keyword-tolerant predicate must NOT degrade it.
        var invocation = new LocalFunctionInvocation("return", Void, []);
        var function = Function([], Container(new ExpressionStatement(invocation), new Return(null)));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void RaisedLocalFunctionStatementUnspellableName_DegradesToPartial()
    {
        var statement = new LocalFunctionStatement(
            "bad-name", Void, [], isStatic: true, [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, Container(new Return(null)));
        var function = Function([], Container(statement, new Return(null)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void RaisedLocalFunctionStatementUsableName_StaysFull()
    {
        var statement = new LocalFunctionStatement(
            "Local", Void, [], isStatic: true, [], [],
            usesUpdatedMemorySafetyRules: false, skipLocalsInit: false, Container(new Return(null)));
        var function = Function([], Container(statement, new Return(null)));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    sealed class StreamOnlyResolver : IAssemblyReferenceResolver
    {
        readonly byte[] _image;
        readonly AssemblyReferenceIdentity _identity;

        public StreamOnlyResolver(string path)
        {
            _image = File.ReadAllBytes(path);
            _identity = ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("StreamOnlyTestIdentity"))
                .Identity;
        }

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope) =>
            identity.Name == _identity.Name
                ? ResolvedAssemblyReference.Create(
                    _identity,
                    "/informational/not-the-assembly.dll",
                    () => new MemoryStream(_image, writable: false),
                    AssemblyResolutionProvenance.Local("StreamOnlyTest"))
                : null;
    }
}

public class CrossAssemblyLocalCollectionBase : System.Collections.IEnumerable
{
    public System.Collections.IEnumerator GetEnumerator() =>
        Array.Empty<object>().GetEnumerator();
}

public sealed class CrossAssemblyLocalCollectionDerived :
    CrossAssemblyLocalCollectionBase;

public sealed class CrossAssemblyGenericEquatable<T> : IEquatable<T>
{
    public bool Equals(T? other) => false;
}
