using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class DelegateConstructionPassTests
{
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef IntPtr = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Owner = TypeRef.Definition("Test", "Adversarial", "C");
    static readonly TypeRef DelegateType = TypeRef.Definition("Test", "Adversarial", "D");
    static readonly TypeRef NotADelegate = TypeRef.Definition("Test", "Adversarial", "NotADelegate");
    static readonly MethodRef Target = new(Owner, "Target", Void, [], HasThis: false);

    [Fact]
    public void DelegateConstructor_WithDelegateTypeFact_Raises()
    {
        var function = FunctionWith(NewObject(MetadataFactState.Yes));

        new DelegateConstructionPass().Run(function, PassContext.None);

        var creation = Assert.Single(function.Descendants.OfType<DelegateCreation>());
        Assert.Equal(DelegateType, creation.DelegateType);
        Assert.Equal(Target, creation.Method);
        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.Empty(function.Descendants.OfType<LoadFunctionPointer>());
    }

    [Theory]
    [InlineData(MetadataFactState.No)]
    [InlineData(MetadataFactState.Unknown)]
    public void ObjectIntPtrConstructor_WithoutDelegateTypeFact_StaysNewObject(MetadataFactState delegateFact)
    {
        var function = FunctionWith(NewObject(delegateFact));

        new DelegateConstructionPass().Run(function, PassContext.None);

        var newObject = Assert.Single(function.Descendants.OfType<NewObject>());
        Assert.Equal(NotADelegate, newObject.Constructor.DeclaringType);
        Assert.Single(function.Descendants.OfType<LoadFunctionPointer>());
        Assert.Empty(function.Descendants.OfType<DelegateCreation>());
    }

    static NewObject NewObject(MetadataFactState delegateFact)
    {
        var declaringType = delegateFact == MetadataFactState.Yes ? DelegateType : NotADelegate;
        var constructor = new MethodRef(
            declaringType,
            ".ctor",
            Void,
            ImmutableArray.Create(Object, IntPtr),
            HasThis: true)
        {
            DeclaringTypeIsDelegate = delegateFact,
        };
        return new NewObject(constructor, [new Constant(null, Object), new LoadFunctionPointer(Target, isVirtual: false, instance: null)]);
    }

    static IrFunction FunctionWith(IrExpression expression)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new Return(expression));
        body.Add(block);

        var signature = new MethodSignature(
            expression.ResultType ?? Object,
            [],
            HasThis: false,
            GenericParameterCount: 0);

        return new IrFunction("M", Owner, signature, [], body);
    }
}
