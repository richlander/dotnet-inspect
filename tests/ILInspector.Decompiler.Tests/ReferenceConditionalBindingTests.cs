using System.Security.Cryptography;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ReferenceConditionalBindingTests
{
    static readonly TypeRef StringType = TypeRef.CoreLib("System", "String");
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef BoolType = TypeRef.CoreLib("System", "Boolean");

    [Theory]
    [InlineData(nameof(ReferenceConditionalBindingSamples.SameType), false)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.SameType), true)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.NullTrue), false)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.NullTrue), true)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.NullFalse), false)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.NullFalse), true)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.Lambda), false)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.Lambda), true)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.LocalFunction), false)]
    [InlineData(nameof(ReferenceConditionalBindingSamples.LocalFunction), true)]
    public void CompilerProducedArmsCarryReferenceTargets(string method, bool lowered)
    {
        var function = Raise(method, lowered);
        var conditionals = function.Descendants.OfType<Conditional>().ToArray();
        Assert.NotEmpty(conditionals);
        Assert.All(conditionals, conditional =>
        {
            Assert.True(conditional.CanAssignReferenceArmsTo(StringType, function.TypeShapes));
            Assert.True(conditional.CanAssignReferenceArmsTo(ObjectType, function.TypeShapes));
        });
        string before = CSharpPrinter.Print(function).Output!;
        new ReferenceConditionalBindingPass().Run(function, PassContext.None);
        Assert.Equal(before, CSharpPrinter.Print(function).Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedNewtonsoftSetterKeepsOneAssignedLocal(bool lowered)
    {
        string path = Path.Combine(AppContext.BaseDirectory,
            "RealAssets", "ReferenceConditional", "Newtonsoft.Json.dll");
        Assert.Equal("A28C251DFE36D881E9E2462E171441B8B0EC156FE3F452602C9149B1B9EFE05B",
            System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = IrImporter.Import(source,
            "Newtonsoft.Json.Converters.IsoDateTimeConverter", "set_DateTimeFormat");
        Assert.NotNull(function);
        IrPasses.Run(function, lowered ? IrPasses.Lowered : IrPasses.Default,
            PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));

        var conditional = Assert.Single(function.Descendants.OfType<Conditional>());
        Assert.Equal(ObjectType, conditional.ResultType);
        Assert.True(conditional.CanAssignReferenceArmsTo(StringType, function.TypeShapes));
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("string S_1 = ", output);
        Assert.Contains("_dateTimeFormat = S_1;", output);
        Assert.DoesNotContain("S_1_1", output);
        Assert.DoesNotContain("object S_1", output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void NestedArmsRetainBothMergedAndArmReferenceEvidence()
    {
        var derived = TypeRef.Definition("Example", "Models", "Derived");
        var parent = TypeRef.Definition("Example", "Models", "Base");
        var inner = Choose(new Constant(null, ObjectType), new LoadArgument(1, "value", derived));
        inner.MergedType = parent;
        var outer = Choose(inner, new Constant(null, ObjectType));
        var function = Function(parent, new Return(outer));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape>
        {
            [derived] = TypeShape.Reference,
            [parent] = TypeShape.Reference,
        };

        new ReferenceConditionalBindingPass().Run(function, PassContext.None);

        Assert.True(outer.CanAssignReferenceArmsTo(derived, function.TypeShapes));
        Assert.True(outer.CanAssignReferenceArmsTo(parent, function.TypeShapes));
        Assert.True(outer.CanAssignReferenceArmsTo(ObjectType, function.TypeShapes));
        Assert.False(outer.CanAssignReferenceArmsTo(StringType, function.TypeShapes));
    }

    [Fact]
    public void AllNullArmsRequireAProvenReferenceTarget()
    {
        var conditional = Choose(new Constant(null, ObjectType), new Constant(null, ObjectType));
        var function = Function(ObjectType, new Return(conditional));
        var unknown = TypeRef.Definition("Example", "Models", "MaybeStruct");
        Assert.True(conditional.CanAssignReferenceArmsTo(StringType, function.TypeShapes));
        Assert.True(conditional.CanAssignReferenceArmsTo(TypeRef.SzArray(StringType), function.TypeShapes));
        Assert.False(conditional.CanAssignReferenceArmsTo(unknown, function.TypeShapes));
        Assert.False(conditional.CanAssignReferenceArmsTo(BoolType, function.TypeShapes));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [unknown] = TypeShape.Reference };
        new ReferenceConditionalBindingPass().Run(function, PassContext.None);
        Assert.True(conditional.CanAssignReferenceArmsTo(unknown, function.TypeShapes));
    }

    [Theory]
    [InlineData(TypeShape.Unknown)]
    [InlineData(TypeShape.ValueType)]
    [InlineData(TypeShape.Reference)]
    public void MetadataShapesBoundNarrowing(TypeShape shape)
    {
        var type = TypeRef.Definition("Example", "Models", "Value");
        var conditional = Choose(new Constant(null, ObjectType), new LoadArgument(1, "value", type));
        var function = Function(ObjectType, new Return(conditional));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [type] = shape };
        new ReferenceConditionalBindingPass().Run(function, PassContext.None);
        Assert.Equal(shape == TypeShape.Reference,
            conditional.CanAssignReferenceArmsTo(type, function.TypeShapes));
        Assert.Equal(shape != TypeShape.ValueType,
            conditional.CanAssignReferenceArmsTo(ObjectType, function.TypeShapes));
    }

    [Fact]
    public void CloneKeepsTestimonyAndFinalBindingRefreshesChangedArms()
    {
        var conditional = Choose(new Constant(null, ObjectType), new LoadArgument(1, "value", StringType));
        var function = Function(ObjectType, new Return(conditional));
        new ReferenceConditionalBindingPass().Run(function, PassContext.None);
        var clone = Assert.IsType<Conditional>(conditional.Clone());
        Assert.True(clone.CanAssignReferenceArmsTo(StringType, function.TypeShapes));

        conditional.WhenFalse.ReplaceWith(new LoadArgument(1, "value", ObjectType));
        // A query reads issued evidence, not the subsequently edited arms.
        Assert.True(conditional.CanAssignReferenceArmsTo(StringType, function.TypeShapes));
        new ReferenceConditionalBindingPass().Run(function, PassContext.None);
        Assert.False(conditional.CanAssignReferenceArmsTo(StringType, function.TypeShapes));
        Assert.True(conditional.CanAssignReferenceArmsTo(ObjectType, function.TypeShapes));
        Assert.True(clone.CanAssignReferenceArmsTo(StringType, function.TypeShapes));
    }

    [Fact]
    public void CoalesceArmUsesItsBoundAssignmentRatherThanItsLeftResultType()
    {
        var coalesce = new Coalesce(new LoadArgument(1, "value", StringType),
            new LoadArgument(2, "fallback", ObjectType));
        var conditional = Choose(coalesce, new Constant(null, ObjectType));
        var function = Function(ObjectType, new Return(conditional));
        new ReferenceCoalesceBindingPass().Run(function, PassContext.None);
        new ReferenceConditionalBindingPass().Run(function, PassContext.None);
        Assert.False(conditional.CanAssignReferenceArmsTo(StringType, function.TypeShapes));
        Assert.True(conditional.CanAssignReferenceArmsTo(ObjectType, function.TypeShapes));
    }

    [Fact]
    public void FinalBindingDoesNotChangeStorageAdmission()
    {
        var conditional = Choose(new Constant(null, ObjectType), new LoadArgument(1, "value", StringType));
        conditional.MergedType = ObjectType;
        var function = Function(StringType, new StoreStackSlot(0, conditional),
            new Return(new LoadStackSlot(0, StringType)));
        var before = Assert.Single(SlotMaterializationPass.Analyze(function));
        new ReferenceConditionalBindingPass().Run(function, PassContext.None);
        var after = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(before.WillMaterialize, after.WillMaterialize);
        Assert.Equal(before.Vetoes, after.Vetoes);
        Assert.Equal(before.Type, after.Type);
        Assert.Equal(ObjectType, conditional.AssignmentType);
        Assert.Contains("string S_0", CSharpPrinter.Print(function).Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CharacterConditionalKeepsValueRendering(bool lowered)
    {
        var function = Raise(nameof(ReferenceConditionalBindingSamples.Character), lowered);
        Assert.Contains("'a'", CSharpPrinter.Print(function).Output);
        Assert.Contains("'b'", CSharpPrinter.Print(function).Output);
        Assert.All(function.Descendants.OfType<Conditional>(),
            conditional => Assert.False(conditional.CanAssignReferenceArmsTo(ObjectType, function.TypeShapes)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitReturnBranchesRemainOutsideThisBindingSlice(bool lowered)
    {
        var function = Raise(nameof(ReferenceConditionalBindingSamples.Nested), lowered);
        Assert.Empty(function.Descendants.OfType<Conditional>());
        string before = CSharpPrinter.Print(function).Output!;
        new ReferenceConditionalBindingPass().Run(function, PassContext.None);
        Assert.Equal(before, CSharpPrinter.Print(function).Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task CompilerProducedDirectCasesKeepNativeFidelity()
    {
        string[] methods =
        [
            nameof(ReferenceConditionalBindingSamples.SameType),
            nameof(ReferenceConditionalBindingSamples.NullTrue),
            nameof(ReferenceConditionalBindingSamples.NullFalse),
            nameof(ReferenceConditionalBindingSamples.Character),
        ];
        var results = await ReturnToSender.CompileBackTargets(
            typeof(ReferenceConditionalBindingSamples).Assembly.Location,
            [.. methods.Select(method => new ReturnToSender.RequestedTarget(
                typeof(ReferenceConditionalBindingSamples).FullName!, method, 0)),
                new(typeof(MergedSlotProbe).FullName!, "set_MergedFormat", 0)],
            sourceIndex: null, applyCompileBackFloor: false);
        Assert.Equal(methods.Length + 1, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.UsedCompileBackFloor);
            Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
        });
    }

    static Conditional Choose(IrExpression whenTrue, IrExpression whenFalse)
        => new(new LoadArgument(0, "choose", BoolType), whenTrue, whenFalse);

    static IrFunction Raise(string method, bool lowered)
    {
        using var source = MetadataSource.Open(typeof(ReferenceConditionalBindingSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ReferenceConditionalBindingSamples).FullName!, method);
        Assert.NotNull(function);
        IrPasses.Run(function, lowered ? IrPasses.Lowered : IrPasses.Default,
            PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        return function;
    }
}
