using System.Collections.Immutable;
using System.Security.Cryptography;
using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Fixtures.StructuredTypes;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ValueTypeReceiverAliasPassTests
{
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef ValueType = TypeRef.CoreLib("System", "ValueType");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Owner = TypeRef.Definition(
        "Synthetic",
        "Samples",
        "Receiver");

    [Fact]
    public void ExactValueTypeReceiverAliasIsForwardedAcrossInterveningCall()
    {
        var function = Function(ValueType);
        var block = Assert.Single(function.Body.Blocks);
        var field = new FieldRef(Owner, "Value", Int32);
        var touch = new MethodRef(Owner, "Touch", Void, [], HasThis: false);
        block.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, function.ReceiverParameter!)));
        block.Add(new ExpressionStatement(new Call(touch, isVirtual: false, [])));
        block.Add(new StoreField(
            field,
            new LoadStackSlot(0, Owner),
            new Constant(1, Int32)));
        block.Add(new Return(null));

        new ValueTypeReceiverAliasPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        var receiver = Assert.IsType<LoadArgument>(
            Assert.Single(function.Descendants.OfType<StoreField>()).Instance);
        Assert.Same(function.ReceiverParameter, receiver.Parameter);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void ReceiverAliasCopyChainIsForwardedToTheSameBinder()
    {
        var function = Function(ValueType);
        var block = Assert.Single(function.Body.Blocks);
        block.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, function.ReceiverParameter!)));
        block.Add(new StoreStackSlot(1, new LoadStackSlot(0, Owner)));
        block.Add(new Return(new LoadStackSlot(1, Owner)));

        new ValueTypeReceiverAliasPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        var receiver = Assert.IsType<LoadArgument>(
            Assert.Single(function.Descendants.OfType<Return>()).Value);
        Assert.Same(function.ReceiverParameter, receiver.Parameter);
    }

    [Fact]
    public void MixedProducerSlotIsNotAnExactReceiverAlias()
    {
        var function = Function(ValueType);
        var block = Assert.Single(function.Body.Blocks);
        block.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, function.ReceiverParameter!)));
        block.Add(new StoreStackSlot(0, new DefaultValue(Owner)));
        block.Add(new Return(new LoadStackSlot(0, Owner)));

        new ValueTypeReceiverAliasPass().Run(function, PassContext.None);

        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.Single(function.Descendants.OfType<LoadStackSlot>());
    }

    [Theory]
    [InlineData("store")]
    [InlineData("address")]
    [InlineData("increment")]
    [InlineData("deconstruction")]
    public void MutableReceiverBindingDeclinesAliasForwarding(string mutation)
    {
        var function = Function(ValueType);
        var block = Assert.Single(function.Body.Blocks);
        block.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, function.ReceiverParameter!)));
        block.Add(mutation switch
        {
            "store" => new StoreArgument(
                0,
                function.ReceiverParameter!,
                new DefaultValue(Owner)),
            "address" => new ExpressionStatement(new LoadArgumentAddress(
                0,
                function.ReceiverParameter!)),
            "increment" => new ExpressionStatement(new IncrementDecrement(
                new LoadArgument(0, function.ReceiverParameter!),
                isIncrement: true,
                isPrefix: true)),
            "deconstruction" => new DeconstructionAssignment(
                [DeconstructionTarget.Argument(
                    0,
                    function.ReceiverParameter!)],
                new DefaultValue(Owner)),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        });
        block.Add(new Return(new LoadStackSlot(0, Owner)));

        new ValueTypeReceiverAliasPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Single(function.Descendants.OfType<LoadStackSlot>());
    }

    [Fact]
    public void ReferenceTypeReceiverDeclinesAliasForwarding()
    {
        var function = Function(Object);
        var block = Assert.Single(function.Body.Blocks);
        block.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, function.ReceiverParameter!)));
        block.Add(new Return(new LoadStackSlot(0, Owner)));

        new ValueTypeReceiverAliasPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Single(function.Descendants.OfType<LoadStackSlot>());
    }

    [Fact]
    public void UnboundArgumentZeroIsNotTheReceiverBinder()
    {
        var function = Function(ValueType);
        var block = Assert.Single(function.Body.Blocks);
        block.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, "this", Owner)));
        block.Add(new Return(new LoadStackSlot(0, Owner)));

        new ValueTypeReceiverAliasPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Single(function.Descendants.OfType<LoadStackSlot>());
    }

    [Fact]
    public void NestedSlotNamespaceIsNotRewrittenByRootReceiverAlias()
    {
        var function = Function(ValueType);
        var block = Assert.Single(function.Body.Blocks);
        block.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, function.ReceiverParameter!)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, Owner)));

        var nestedBody = new BlockContainer();
        var nestedBlock = new Block(0);
        nestedBody.Add(nestedBlock);
        nestedBlock.Add(new StoreStackSlot(
            0,
            new LoadArgument(0, function.ReceiverParameter!)));
        nestedBlock.Add(new Return(new LoadStackSlot(0, Owner)));
        block.Add(new ExpressionStatement(new Lambda(
            TypeRef.Definition("Synthetic", "System", "Func"),
            [],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            nestedBody)));
        block.Add(new Return(null));

        new ValueTypeReceiverAliasPass().Run(function, PassContext.None);

        Assert.Single(CoercionSinks.ScopeNodes(function.Body).OfType<LoadArgument>(),
            load => ReferenceEquals(load.Parameter, function.ReceiverParameter));
        Assert.Single(nestedBody.Descendants.OfType<StoreStackSlot>());
        Assert.Single(nestedBody.Descendants.OfType<LoadStackSlot>());
    }

    [Fact]
    public void CompilerProducedConstructorWritesTheReceiverNotAValueCopy()
    {
        using var source = MetadataSource.Open(
            typeof(ValueTypeReceiverAlias).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(ValueTypeReceiverAlias).FullName!,
            ".ctor");
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(
            function!,
            method => IrImporter.Import(source, method));

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.NotNull(result.Output);
        Assert.DoesNotContain("ValueTypeReceiverAlias S_", result.Output);
        Assert.Contains("this.Path =", result.Output);
    }

    [Fact]
    public void RoslynFileLinePositionSpanConstructorWritesTheReceiver()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PrimitiveJoin",
            "Microsoft.CodeAnalysis.dll");
        Assert.Equal(
            "10F489DB67B8AC7489E58D392166C928302BA5698506DD652311DA5D89F0A0F8",
            System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = IrImporter.Import(
            source,
            "Microsoft.CodeAnalysis.FileLinePositionSpan",
            ".ctor",
            overloadIndex: 1);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(
            function!,
            method => IrImporter.Import(source, method));

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.NotNull(result.Output);
        Assert.DoesNotContain("FileLinePositionSpan S_", result.Output);
        Assert.Contains("this.Path =", result.Output);
    }

    static IrFunction Function(TypeRef baseType)
    {
        var body = new BlockContainer();
        body.Add(new Block(0));
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                Void,
                [],
                HasThis: true,
                GenericParameterCount: 0),
            [],
            body)
        {
            BaseType = baseType,
        };
    }
}
