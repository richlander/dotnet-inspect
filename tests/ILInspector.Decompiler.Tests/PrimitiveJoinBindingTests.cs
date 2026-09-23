using System.Collections.Immutable;
using System.Security.Cryptography;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class PrimitiveJoinBindingTests
{
    static readonly TypeRef BoolType = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef ByteType = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef IntType = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef UIntType = TypeRef.CoreLib("System", "UInt32");
    static readonly TypeRef LongType = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef ULongType = TypeRef.CoreLib("System", "UInt64");

    [Fact]
    public void ConditionalAndSwitchShareAcceptedTargetRelation()
    {
        var conditional = Conditional(
            new LoadArgument(1, "left", IntType),
            new LoadArgument(2, "right", IntType),
            IntType);
        var switchExpression = new SwitchExpression(
            new LoadArgument(0, "value", IntType),
            [
                new SwitchExpressionArm([0], false, new Constant(0, IntType)),
                new SwitchExpressionArm([], true, new LoadArgument(1, "right", IntType)),
            ]);
        var function = Function(UIntType,
            new StoreLocal(0, UIntType, conditional),
            new Return(switchExpression));

        new PrimitiveJoinBindingPass().Run(function, PassContext.None);

        Assert.True(conditional.CanRenderPrimitiveJoinAt(UIntType));
        Assert.Equal(IntType, conditional.PrimitiveJoinArmSource(UIntType));
        Assert.True(conditional.CanRenderPrimitiveJoinAt(IntType));
        Assert.Null(conditional.PrimitiveJoinArmSource(IntType));
        Assert.True(switchExpression.CanRenderPrimitiveJoinAt(UIntType));
        Assert.Equal(IntType, switchExpression.PrimitiveJoinArmSource(UIntType));
    }

    [Fact]
    public void CoalesceKeepsWholeJoinAndRightArmTestimonyDistinct()
    {
        var nullableInt = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Nullable`1"),
            [IntType]);
        var coalesce = new Coalesce(
            new LoadArgument(0, "value", nullableInt),
            new LoadArgument(1, "fallback", IntType));
        var function = Function(UIntType, new Return(coalesce));

        new PrimitiveJoinBindingPass().Run(function, PassContext.None);

        Assert.False(coalesce.CanRenderPrimitiveJoinAt(UIntType));
        Assert.Equal(IntType, coalesce.PrimitiveJoinArmSource(UIntType));
    }

    [Fact]
    public void BooleanArmIsAcceptedButDifferingWidthAndMissingTypesDecline()
    {
        var booleanArm = Conditional(
            new LoadArgument(1, "value", BoolType),
            new LoadArgument(2, "integer", IntType),
            IntType);
        Assert.True(booleanArm.CanRenderPrimitiveJoinAt(UIntType));

        var differingWidth = Conditional(
            new LoadArgument(1, "left", LongType),
            new LoadArgument(2, "right", LongType),
            LongType);
        Assert.False(differingWidth.CanRenderPrimitiveJoinAt(UIntType));

        var missingType = Conditional(
            new LoadStackSlot(0, type: null),
            new LoadArgument(1, "right", IntType),
            IntType);
        Assert.False(missingType.CanRenderPrimitiveJoinAt(UIntType));
    }

    [Fact]
    public void CloneRetainsIssuedTestimonyAndFinalBindingRefreshesChangedArms()
    {
        var conditional = Conditional(
            new LoadArgument(1, "left", IntType),
            new LoadArgument(2, "right", IntType),
            IntType);
        var function = Function(UIntType, new Return(conditional));
        new PrimitiveJoinBindingPass().Run(function, PassContext.None);
        var clone = Assert.IsType<Conditional>(conditional.Clone());

        Assert.Equal(IntType, clone.PrimitiveJoinArmSource(UIntType));
        conditional.WhenFalse.ReplaceWith(new LoadStackSlot(0, type: null));
        Assert.Equal(IntType, conditional.PrimitiveJoinArmSource(UIntType));

        new PrimitiveJoinBindingPass().Run(function, PassContext.None);

        Assert.Null(conditional.PrimitiveJoinArmSource(UIntType));
        Assert.False(conditional.CanRenderPrimitiveJoinAt(UIntType));
        Assert.Equal(IntType, clone.PrimitiveJoinArmSource(UIntType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerProducedJoinsCarryIssuedTestimony(bool lowered)
    {
        var conditional = Raise(
            nameof(PrimitiveJoinBindingSamples.Conditional),
            lowered);
        var conditionalJoin = Assert.Single(
            conditional.Descendants.OfType<Conditional>());
        Assert.True(conditionalJoin.CanRenderPrimitiveJoinAt(ByteType));
        Assert.NotNull(conditionalJoin.PrimitiveJoinArmSource(ByteType));
        Assert.Contains("(byte)17", CSharpPrinter.Print(conditional).Output);

        var switchFunction = Raise(
            nameof(PrimitiveJoinBindingSamples.SwitchExpression),
            lowered);
        var switchJoin = Assert.Single(
            switchFunction.Descendants.OfType<SwitchExpression>());
        Assert.True(switchJoin.CanRenderPrimitiveJoinAt(UIntType));
        Assert.NotNull(switchJoin.PrimitiveJoinArmSource(UIntType));

        var coalesceFunction = Raise(
            nameof(PrimitiveJoinBindingSamples.Coalesce),
            lowered);
        var coalesceJoin = Assert.Single(
            coalesceFunction.Descendants.OfType<Coalesce>());
        Assert.Null(coalesceJoin.PrimitiveJoinArmSource(UIntType));
        Assert.Contains(
            "unchecked((uint)(-1))",
            CSharpPrinter.Print(coalesceFunction).Output);
    }

    [Fact]
    public void FinalBindingDoesNotChangeStorageAdmission()
    {
        var conditional = Conditional(
            new LoadArgument(1, "left", IntType),
            new LoadArgument(2, "right", IntType),
            IntType);
        var function = Function(
            UIntType,
            new StoreStackSlot(0, conditional),
            new Return(new LoadStackSlot(0, UIntType)));
        var before = Assert.Single(SlotMaterializationPass.Analyze(function));

        new PrimitiveJoinBindingPass().Run(function, PassContext.None);

        var after = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(before.WillMaterialize, after.WillMaterialize);
        Assert.Equal(before.Vetoes, after.Vetoes);
        Assert.Equal(before.Type, after.Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CharacterJoinKeepsDedicatedLiteralRendering(bool lowered)
    {
        var function = Raise(
            nameof(PrimitiveJoinBindingSamples.Character),
            lowered);
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("'a'", output);
        Assert.Contains("'b'", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedBitVectorWitnessKeepsSameWidthArmCast(bool lowered)
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
            "Microsoft.CodeAnalysis.BitVector",
            "get_Item");
        Assert.NotNull(function);
        IrPasses.Run(
            function,
            lowered ? IrPasses.Lowered : IrPasses.Default,
            PassContext.ForImport(
                reference => IrImporter.Import(source, reference),
                source.AreProvablyDisjoint));

        var witness = Assert.Single(
            function.Descendants.OfType<Conditional>(),
            conditional =>
                conditional.PrimitiveJoinArmSource(ULongType)?.Equals(LongType)
                == true);
        Assert.True(witness.CanRenderPrimitiveJoinAt(ULongType));
        Assert.Contains(
            " : (ulong)_bits[",
            CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    static Conditional Conditional(
        IrExpression whenTrue,
        IrExpression whenFalse,
        TypeRef mergedType)
        => new(
            new LoadArgument(0, "choose", BoolType),
            whenTrue,
            whenFalse)
        {
            MergedType = mergedType,
        };

    static IrFunction Function(TypeRef returnType, params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(
                returnType,
                ImmutableArray<Parameter>.Empty,
                HasThis: false,
                GenericParameterCount: 0),
            [UIntType],
            body);
    }

    static IrFunction Raise(string method, bool lowered)
    {
        using var source = MetadataSource.Open(
            typeof(PrimitiveJoinBindingSamples).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(PrimitiveJoinBindingSamples).FullName!,
            method);
        Assert.NotNull(function);
        IrPasses.Run(
            function,
            lowered ? IrPasses.Lowered : IrPasses.Default,
            PassContext.ForImport(
                reference => IrImporter.Import(source, reference),
                source.AreProvablyDisjoint));
        return function;
    }
}
