using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates for value-typed-emission.md, Instance 2's one slot-evidence rule as
/// extended for the #9248 remainder (#9371 slice 1): an untyped slot load
/// takes its type from a call or object-creation argument's closed declared
/// parameter type or from the other operand of a comparison, and a coalesce
/// of two disjoint proven references stored to an <c>object</c>-observed
/// carrier receives the one reference-conversion witness C# needs so it
/// materializes as <c>object S = ((object)left) ?? right</c>.
/// </summary>
[Trait("Area", "Pass")]
public class ArgumentSinkTestimonyTests
{
    static readonly TypeRef Holder = TypeRef.Definition("Synthetic", "Samples", "Holder", ValueTypeHint.ReferenceType);
    static readonly TypeRef Alpha = TypeRef.Definition("Synthetic", "Samples", "Alpha", ValueTypeHint.ReferenceType);
    static readonly TypeRef Beta = TypeRef.Definition("Synthetic", "Samples", "Beta", ValueTypeHint.ReferenceType);
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Exception = TypeRef.CoreLib("System", "Exception");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");

    [Fact]
    public void ArgumentParameterTypeTestifiesForAnUntypedLoad()
    {
        // Two unrelated reference stores into a slot read only as an `object`
        // argument: the parameter type is the load's sink target, so the slot
        // testifies object and materializes under the reference-to-object rule.
        var consume = new MethodRef(Holder, "Consume", Void, [Object], HasThis: false);
        var block = new Block(0);
        block.Add(new IfStatement(
            new LoadArgument(0, "choose", Bool),
            BlockOf(new StoreStackSlot(0, new LoadArgument(1, "alpha", Alpha))),
            BlockOf(new StoreStackSlot(0, new LoadArgument(2, "beta", Beta)))));
        block.Add(new ExpressionStatement(new Call(consume, isVirtual: false, [new LoadStackSlot(0, type: null)])));
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("choose", Bool), new Parameter("alpha", Alpha), new Parameter("beta", Beta)]);

        var testimony = CoercionSinks.AnalyzeSlotTypeTestimony(function.Body, function.Signature.ReturnType, function.TypeShapes);
        Assert.Equal(CoercionSinks.SlotTypeTestimonyStatus.Decided, testimony[0].Status);
        Assert.Equal(Object, testimony[0].Type);

        string output = RunTail(function);
        Assert.Empty(function.ResidualSlotBindings);
        Assert.Contains("object S_0;", output);
        Assert.Contains("Consume(S_0);", output);
    }

    [Fact]
    public void OpenGenericParameterDoesNotTestify()
    {
        // The same shape through a generic method whose parameter is still the
        // open `T`: no closed sink type exists, so the load stays underivable
        // and the web fails visibly instead of guessing.
        var consume = new MethodRef(Holder, "Consume", Void, [TypeRef.MethodGenericParameter(0, "T")], HasThis: false);
        var block = new Block(0);
        block.Add(new IfStatement(
            new LoadArgument(0, "choose", Bool),
            BlockOf(new StoreStackSlot(0, new LoadArgument(1, "alpha", Alpha))),
            BlockOf(new StoreStackSlot(0, new LoadArgument(2, "beta", Beta)))));
        block.Add(new ExpressionStatement(new Call(consume, isVirtual: false, [new LoadStackSlot(0, type: null)])));
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("choose", Bool), new Parameter("alpha", Alpha), new Parameter("beta", Beta)]);

        var testimony = CoercionSinks.AnalyzeSlotTypeTestimony(function.Body, function.Signature.ReturnType, function.TypeShapes);
        Assert.Equal(CoercionSinks.SlotTypeTestimonyStatus.Underivable, testimony[0].Status);
        var failure = Assert.Throws<InvalidOperationException>(() => RunTail(function));
        Assert.Contains("has no decided type at residual storage binding", failure.Message);
    }

    [Fact]
    public void ComparisonSiblingTestifiesForAnUntypedLoad()
    {
        // An int constant and a long argument share a slot that is read only
        // against a long: the sibling operand is the sink target.
        var block = new Block(0);
        block.Add(new IfStatement(
            new LoadArgument(0, "choose", Bool),
            BlockOf(new StoreStackSlot(0, new Constant(0, Int32))),
            BlockOf(new StoreStackSlot(0, new LoadArgument(1, "value", Int64)))));
        block.Add(new Return(new Comparison(ComparisonKind.NotEqual, isUnsigned: false, new LoadStackSlot(0, type: null), new LoadArgument(2, "other", Int64))));
        var function = Function(Bool, block, [new Parameter("choose", Bool), new Parameter("value", Int64), new Parameter("other", Int64)]);

        var testimony = CoercionSinks.AnalyzeSlotTypeTestimony(function.Body, function.Signature.ReturnType, function.TypeShapes);
        Assert.Equal(CoercionSinks.SlotTypeTestimonyStatus.Decided, testimony[0].Status);
        Assert.Equal(Int64, testimony[0].Type);

        string output = RunTail(function);
        Assert.Contains("long S_0", output);
        Assert.Contains("S_0 != other", output);
    }

    [Fact]
    public void TwoReferenceCoalesceAtAnObjectCarrierReceivesTheLeftWitness()
    {
        // `alpha ?? beta` with unrelated reference operands has no assignment
        // type; stored to a slot read only as `object`, the slot-target pass
        // issues `(object)alpha ?? beta` and the web materializes as object.
        var consume = new MethodRef(Holder, "Consume", Void, [Object], HasThis: false);
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Coalesce(new LoadArgument(0, "alpha", Alpha), new LoadArgument(1, "beta", Beta))));
        block.Add(new ExpressionStatement(new Call(consume, isVirtual: false, [new LoadStackSlot(0, type: null)])));
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("alpha", Alpha), new Parameter("beta", Beta)]);

        new ReferenceSlotTargetBindingPass().Run(function, PassContext.None);
        var coalesce = Assert.Single(function.Descendants.OfType<Coalesce>());
        var witness = Assert.IsType<Coerce>(coalesce.Left);
        Assert.Equal(CoercionKind.ReferenceWitness, witness.Kind);
        Assert.Equal(Object, witness.Target);
        Assert.Equal(Object, coalesce.AssignmentType);

        string output = RunTail(function, slotTargetBindingDone: true);
        Assert.Empty(function.ResidualSlotBindings);
        Assert.Contains("object S_0 = ((object)alpha) ?? beta;", output);
        Assert.Contains("Consume(S_0);", output);
    }

    [Fact]
    public void TwoReferenceCoalesceAtANarrowerCarrierStaysUndecided()
    {
        // The same coalesce read as a `string` argument: object is not the
        // carrier type, so no witness is issued and the coalesce keeps no
        // assignment type; the web still fails visibly rather than guessing.
        var consume = new MethodRef(Holder, "Consume", Void, [String], HasThis: false);
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new Coalesce(new LoadArgument(0, "alpha", Alpha), new LoadArgument(1, "beta", Beta))));
        block.Add(new ExpressionStatement(new Call(consume, isVirtual: false, [new LoadStackSlot(0, type: null)])));
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("alpha", Alpha), new Parameter("beta", Beta)]);

        new ReferenceSlotTargetBindingPass().Run(function, PassContext.None);
        var coalesce = Assert.Single(function.Descendants.OfType<Coalesce>());
        Assert.IsType<LoadArgument>(coalesce.Left);
        Assert.Null(coalesce.AssignmentType);
        Assert.Throws<InvalidOperationException>(() => RunTail(function, slotTargetBindingDone: true));
    }

    [Theory]
    [InlineData("Microsoft.CodeAnalysis.CodeGen.SynthesizedStaticField", "ToString", "object S_1 = ((object)S_256) ?? _type;")]
    [InlineData("Microsoft.CodeAnalysis.CodeGen.DataSectionStringType.DataSectionStringField", "ToString", "object S_1 = ((object)S_256) ?? base.ContainingTypeDefinition;")]
    [InlineData("Microsoft.CodeAnalysis.MarshalAsAttributeDecoder`4", "DecodeMarshalAsCustom", "SetMarshalAsCustom(S_1, ")]
    public void RealObjectCarriersMaterializeInsteadOfFailing(string typeName, string methodName, string expected)
    {
        // Microsoft.CodeAnalysis 5.0.0: three of the #9248 witnesses, each a
        // reference join spilled to a slot that only an `object` argument
        // reads. Before this slice they failed visibly at residual binding.
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "PrimitiveJoin", "Microsoft.CodeAnalysis.dll");
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        var result = CSharpPrinter.Print(function);

        Assert.Contains(expected, result.Output);
        Assert.Contains("object S_1", result.Output);
        Assert.DoesNotContain("var S_1", result.Output);
        Assert.DoesNotContain(function.ResidualSlotBindings.Values, static binding => binding.Slot == 1);
    }

    [Fact]
    public void RealEnumCarrierBindsInsteadOfFailing()
    {
        // Microsoft.CodeAnalysis.CSharp, SpillSequenceSpiller.Spill: a RefKind
        // argument types a slot whose stores are the enum and an integer
        // constant. The constant store keeps the carrier out of materialization
        // (UnrenderableStoreType), so the frozen residual policy binds it as the
        // enum; before this slice the load had no testimony and the web failed
        // visibly. (The comparison-sibling witness in the 5.0.0 corpus,
        // MetadataDecoder.SubstituteNoPiaLocalType, has no residual web in the
        // referenced 5.9.0 build, so the comparison rule is gated synthetically.)
        const string typeName = "Microsoft.CodeAnalysis.CSharp.SpillSequenceSpiller";
        const string methodName = "Spill";
        const string expected = "RefKind S_5";
        string path = typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly.Location;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        var result = CSharpPrinter.Print(function);

        Assert.Contains(expected, result.Output);
        Assert.Contains(function.ResidualSlotBindings.Values, static binding => binding.Kind == ResidualSlotBindingKind.Unified);
    }

    static string RunTail(IrFunction function, bool slotTargetBindingDone = false)
    {
        if (!slotTargetBindingDone)
            new ReferenceSlotTargetBindingPass().Run(function, PassContext.None);
        new SlotMaterializationPass().Run(function, PassContext.None);
        new CoercionInsertionPass().Run(function, PassContext.None);
        new ResidualSlotBindingPass().Run(function, PassContext.None);
        return CSharpPrinter.Print(function).Output!;
    }

    static Block BlockOf(IrNode statement)
    {
        var block = new Block(0);
        block.Add(statement);
        return block;
    }

    static IrFunction Function(TypeRef returnType, Block block, IReadOnlyList<Parameter> parameters)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
