using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ClassicAsyncReconstructionPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "Outer+<Fake>d__0");
    static readonly TypeRef Builder = TypeRef.Definition("Synthetic", "Samples", "BuilderLike");

    [Fact]
    public void SupportMethodLookalikeWithoutCompilerGeneratedMetadata_IsNotAcknowledged()
    {
        var function = BuildMoveNext(MetadataFactState.No);

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "<>t__builder");
        Assert.Single(function.Descendants.OfType<Call>(), call => call.Callee.Name == "SideEffect");
        Assert.False(IsAcknowledgedEmptyReturn(function));
    }

    [Fact]
    public void CompilerGeneratedSupportMethod_IsAcknowledged()
    {
        var function = BuildMoveNext(MetadataFactState.Yes);

        new ClassicAsyncReconstructionPass().Run(function, PassContext.None);

        Assert.True(IsAcknowledgedEmptyReturn(function));
        Assert.Empty(function.Descendants.OfType<LoadField>());
        Assert.Empty(function.Descendants.OfType<Call>());
    }

    static IrFunction BuildMoveNext(MetadataFactState declaringTypeGenerated)
    {
        var block = new Block(0);
        block.Add(new ExpressionStatement(new LoadField(
            new FieldRef(Owner, "<>t__builder", Builder),
            new LoadArgument(0, "this", Owner))));
        block.Add(new ExpressionStatement(new Call(
            new MethodRef(Owner, "SideEffect", Void, [], HasThis: false),
            isVirtual: false,
            [])));
        block.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction(
            "MoveNext",
            Owner,
            new MethodSignature(Void, [], HasThis: true, GenericParameterCount: 0),
            [],
            body)
        {
            DeclaringTypeCompilerGenerated = declaringTypeGenerated,
        };
    }

    static bool IsAcknowledgedEmptyReturn(IrFunction function)
        => function.Body.Blocks is [var block]
            && block.Children is [Return { Value: null }];
}
