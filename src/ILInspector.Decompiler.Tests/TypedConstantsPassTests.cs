using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using Convert = ILInspector.Decompiler.Pipeline.Convert;

namespace ILInspector.Decompiler.Tests;

public class TypedConstantsPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    [Fact]
    public void BoolStoredThroughByRef_RetypesConstantToBool()
    {
        // `out bool flag = true` lowers to `stind.i1` over a `ref bool`, whose opcode
        // width is sbyte. Retyping by the address's pointee type (bool), not the
        // opcode type, recovers the boolean literal — otherwise the output is
        // `flag = 1;`, which is not valid C# (CS0029).
        var function = Raised(nameof(CfgSampleClass.SetFlag));
        var store = Assert.Single(function.Descendants.OfType<StoreIndirect>());

        var constant = Assert.IsType<Constant>(store.Value);
        Assert.IsType<bool>(constant.Value);
        Assert.Equal(true, constant.Value);
    }

    [Fact]
    public void BoolStoredThroughByRef_RendersBooleanLiteral()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.SetFlag))).Output;

        Assert.NotNull(output);
        Assert.Contains("flag = true;", output);
        Assert.DoesNotContain("flag = 1;", output);
    }

    [Fact]
    public void BoolArrayElementTraffic_RendersAsBool()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.BoolArrayVisited))).Output;

        Assert.NotNull(output);
        Assert.Contains("[index] = true;", output);
        Assert.Contains("return S_256[index] ? 1 : 0;", output);
        Assert.DoesNotContain("visited[index] = 1;", output);
        Assert.DoesNotContain("visited[index] == 0", output);
    }

    // --- Slice 2 (value-typed-emission.md): Convert folding, semantic element
    // targets, either-side comparisons, and late-created sinks. ---

    static readonly TypeRef LongEnum = TypeRef.Definition("synthetic", "", "LEnum");
    static readonly TypeRef Int32Type = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64Type = TypeRef.CoreLib("System", "Int64");

    static IrFunction EnumFunction(BlockContainer body, TypeRef returnType, params Parameter[] parameters)
        => new("M", TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(returnType, [.. parameters], HasThis: false, GenericParameterCount: 0), [], body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [LongEnum] = TypeShape.Enum },
            EnumUnderlyingTypes = new Dictionary<TypeRef, TypeRef> { [LongEnum] = Int64Type },
            EnumMembers = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
            {
                [LongEnum] = new Dictionary<long, string> { [2] = "High" },
            },
        };

    static BlockContainer SingleStatement(IrNode statement)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(statement);
        container.Add(block);
        return container;
    }

    [Fact]
    public void WideningConvertConstant_AtEnumReturn_FoldsAndNames()
    {
        // A long-backed enum constant lowers `ldc.i4 2; conv.i8`; the fold turns
        // the Convert into a typed constant, and the printer names it.
        var body = SingleStatement(new Return(
            new Convert(Int64Type, isChecked: false, isUnsigned: false, new Constant(2, Int32Type))));
        var function = EnumFunction(body, LongEnum);

        new TypedConstantsPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(Assert.Single(function.Descendants.OfType<Return>()).Value);
        Assert.Equal(2L, constant.Value);
        Assert.Equal(LongEnum, constant.Type);
        Assert.Contains("return LEnum.High;", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void UnsignedWideningConvertConstant_ZeroExtendsPayload()
    {
        // conv.u8 zero-extends a 32-bit payload: -1 carries 4294967295, not -1
        // (keyed off the convert target, mirroring TryEnumCastLiteral).
        var body = SingleStatement(new Return(
            new Convert(TypeRef.CoreLib("System", "UInt64"), isChecked: false, isUnsigned: false, new Constant(-1, Int32Type))));
        var function = EnumFunction(body, LongEnum);

        new TypedConstantsPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(Assert.Single(function.Descendants.OfType<Return>()).Value);
        Assert.Equal(4294967295L, constant.Value);
    }

    [Fact]
    public void CheckedOrNarrowingConvertConstant_DoesNotFold()
    {
        // conv.ovf traps on out-of-range values and a narrowing truncates —
        // neither is a pure retype, so both keep their Convert.
        var check = SingleStatement(new Return(
            new Convert(Int64Type, isChecked: true, isUnsigned: false, new Constant(-1, Int32Type))));
        var checkedFunction = EnumFunction(check, LongEnum);
        new TypedConstantsPass().Run(checkedFunction, PassContext.None);
        Assert.IsType<Convert>(Assert.Single(checkedFunction.Descendants.OfType<Return>()).Value);

        var narrow = SingleStatement(new Return(
            new Convert(TypeRef.CoreLib("System", "UInt32"), isChecked: false, isUnsigned: false, new Constant(300, Int32Type))));
        var narrowFunction = EnumFunction(narrow, LongEnum);
        new TypedConstantsPass().Run(narrowFunction, PassContext.None);
        Assert.IsType<Convert>(Assert.Single(narrowFunction.Descendants.OfType<Return>()).Value);
    }

    [Fact]
    public void StoreElement_PrefersSemanticElementType()
    {
        // stelem.i8 into LEnum[] carries ElementType Int64 (the storage width);
        // the array's element type is the semantic target, so the Convert-wrapped
        // payload folds to the enum.
        var array = new LoadArgument(0, "values", TypeRef.SzArray(LongEnum));
        var store = new StoreElement(
            Int64Type,
            array,
            new Constant(0, Int32Type),
            new Convert(Int64Type, isChecked: false, isUnsigned: false, new Constant(2, Int32Type)));
        var function = EnumFunction(SingleStatement(store), TypeRef.CoreLib("System", "Void"),
            new Parameter("values", TypeRef.SzArray(LongEnum)));

        new TypedConstantsPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(Assert.Single(function.Descendants.OfType<StoreElement>()).Value);
        Assert.Equal(LongEnum, constant.Type);
    }

    [Fact]
    public void ComparisonConstantOnLeft_RetypesAgainstRightSide()
    {
        var comparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new Constant(2, Int32Type),
            new LoadArgument(0, "e", LongEnum));
        var function = EnumFunction(
            SingleStatement(new Return(comparison)),
            TypeRef.CoreLib("System", "Boolean"),
            new Parameter("e", LongEnum));

        new TypedConstantsPass().Run(function, PassContext.None);

        var retyped = Assert.IsType<Constant>(Assert.Single(function.Descendants.OfType<Comparison>()).Left);
        Assert.Equal(LongEnum, retyped.Type);
    }

    [Fact]
    public void EnumMergedConditionalArm_RetypesConstantArm()
    {
        // The join sinks only exist after structuring/diamond folds, which is
        // why the pipeline gained a late run; enum-merged only (bool joins
        // belong to BooleanFoldingPass).
        var conditional = new Conditional(
            new LoadArgument(0, "c", TypeRef.CoreLib("System", "Boolean")),
            new Constant(2, Int32Type),
            new LoadArgument(1, "e", LongEnum))
        {
            MergedType = LongEnum,
        };
        var function = EnumFunction(
            SingleStatement(new Return(conditional)),
            LongEnum,
            new Parameter("c", TypeRef.CoreLib("System", "Boolean")),
            new Parameter("e", LongEnum));

        new TypedConstantsPass().Run(function, PassContext.None);

        var arm = Assert.IsType<Constant>(Assert.Single(function.Descendants.OfType<Conditional>()).WhenTrue);
        Assert.Equal(LongEnum, arm.Type);
        Assert.Contains("LEnum.High", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void CoalescingAssignment_RetypesValue()
    {
        var assign = new NullCoalescingAssignment(0, LongEnum, new Constant(2, Int32Type));
        var function = EnumFunction(SingleStatement(assign), TypeRef.CoreLib("System", "Void"));

        new TypedConstantsPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(Assert.Single(function.Descendants.OfType<NullCoalescingAssignment>()).Value);
        Assert.Equal(LongEnum, constant.Type);
    }

    // --- Slice 5a: slot-store reconciliation against the slot's unique load
    // type (the phantom-split fix: `E S_3; int S_3_1;` where `S_3_1 = 0` was
    // written but the load read `S_3`). ---

    static BlockContainer SlotDiamond(IrExpression storeA, IrExpression storeB, TypeRef? loadType, TypeRef returnType)
    {
        // Two stores to slot 3 (the diamond arms) and one load (the join read),
        // flattened — the pass only needs the access population, not the CFG.
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(3, storeA));
        block.Add(new StoreStackSlot(3, storeB));
        block.Add(new Return(new LoadStackSlot(3, loadType)));
        container.Add(block);
        return container;
    }

    [Fact]
    public void SlotConstantStore_ReconcilesToUniqueLoadType_AndCollapsesNaming()
    {
        // The enum-diamond slot: one arm stores the enum, the other `0`; the
        // load carries the joined enum type. The constant store retypes, and
        // the printer's type-keyed naming collapses to ONE declaration whose
        // constant arm renders by member name — no phantom `int S_3_1`.
        var body = SlotDiamond(
            new LoadArgument(0, "e", LongEnum),
            new Constant(0, Int32Type),
            LongEnum,
            LongEnum);
        var function = EnumFunction(body, LongEnum, new Parameter("e", LongEnum));

        new TypedConstantsPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(
            function.Descendants.OfType<StoreStackSlot>().Last().Value);
        Assert.Equal(LongEnum, constant.Type);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("S_3_1", output);
        Assert.DoesNotContain("int S_3", output);
    }

    [Fact]
    public void SlotWithDisagreeingLoadTypes_IsNotReconciled()
    {
        // Ambiguity is not an invitation to guess: two typed loads that
        // disagree veto reconciliation and the constant keeps its type.
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(3, new Constant(0, Int32Type)));
        block.Add(new StoreLocal(0, LongEnum, new LoadStackSlot(3, LongEnum)));
        block.Add(new Return(new LoadStackSlot(3, Int32Type)));
        container.Add(block);
        var function = EnumFunction(container, Int32Type);

        new TypedConstantsPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(
            Assert.Single(function.Descendants.OfType<StoreStackSlot>()).Value);
        Assert.Equal(Int32Type, constant.Type);
    }

    [Fact]
    public void UntypedLoadWithConflictingSink_VetoesReconciliation()
    {
        // Slice-5a second round (GPT-5.5): an untyped load is not silence — it
        // testifies through its consuming sink. Here the untyped load feeds an
        // int local while the typed load says enum: reconciliation must veto,
        // or slot reuse smuggles the enum type onto the int range's store.
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(3, new Constant(0, Int32Type)));
        block.Add(new StoreLocal(0, Int32Type, new LoadStackSlot(3, null)));
        block.Add(new Return(new LoadStackSlot(3, LongEnum)));
        container.Add(block);
        var function = EnumFunction(container, LongEnum);

        new TypedConstantsPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(
            Assert.Single(function.Descendants.OfType<StoreStackSlot>()).Value);
        Assert.Equal(Int32Type, constant.Type);
    }

    [Fact]
    public void NestedLambdaSlots_ReconcilePerScope()
    {
        // Slice-5a second round (GPT-5.5): slot ids are per-body. The outer
        // scope's slot 3 (int) and the lambda's slot 3 (enum) are different
        // variables; each reconciles in its own scope only.
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new StoreStackSlot(3, new Constant(2, Int32Type)));
        lambdaBlock.Add(new StoreLocal(0, LongEnum, new LoadStackSlot(3, LongEnum)));
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            TypeRef.Definition("System.Private.CoreLib", "System", "Action"),
            [], [], [], usesUpdatedMemorySafetyRules: false, skipLocalsInit: false,
            lambdaBody);

        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(3, new Constant(0, Int32Type)));
        block.Add(new StoreLocal(1, Int32Type, new LoadStackSlot(3, Int32Type)));
        block.Add(new StoreLocal(2, TypeRef.Definition("System.Private.CoreLib", "System", "Action"), lambda));
        container.Add(block);
        var function = EnumFunction(container, Int32Type);

        new TypedConstantsPass().Run(function, PassContext.None);

        var stores = function.Descendants.OfType<StoreStackSlot>().ToList();
        var outer = Assert.IsType<Constant>(stores.Single(s => ((Constant)s.Value).Value is 0).Value);
        Assert.Equal(Int32Type, outer.Type);
        var nested = Assert.IsType<Constant>(stores.Single(s => ((Constant)s.Value).Value is not 0).Value);
        Assert.Equal(LongEnum, nested.Type);
    }

    [Fact]
    public void LambdaReturn_IsNotRetypedToOuterSignature()
    {
        // The pass's own latent CoercionSinks-class bug, fixed by the scope
        // walk: a constant returned from a lambda body must not be retyped
        // against the OUTER method's return type.
        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new Return(new Constant(1, Int32Type)));
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(
            TypeRef.Definition("System.Private.CoreLib", "System", "Func`1"),
            [], [], [], usesUpdatedMemorySafetyRules: false, skipLocalsInit: false,
            lambdaBody);
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreLocal(0, TypeRef.Definition("System.Private.CoreLib", "System", "Func`1"), lambda));
        block.Add(new Return(new Constant(2, Int32Type)));
        container.Add(block);
        var function = EnumFunction(container, LongEnum);

        new TypedConstantsPass().Run(function, PassContext.None);

        var lambdaReturn = Assert.IsType<Constant>(
            function.Descendants.OfType<Lambda>().Single()
                .Descendants.OfType<Return>().Single().Value);
        Assert.Equal(Int32Type, lambdaReturn.Type);
    }

    [Fact]
    public void UntypedLoads_DoNotVetoReconciliation()
    {
        // An untyped load testifies through its consuming sink; when that sink
        // AGREES with the typed evidence, the slot still reconciles (the
        // sink-evidence rule from the slice-5a second round).
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(3, new Constant(2, Int32Type)));
        block.Add(new StoreLocal(0, LongEnum, new LoadStackSlot(3, null)));
        block.Add(new Return(new LoadStackSlot(3, LongEnum)));
        container.Add(block);
        var function = EnumFunction(container, LongEnum);

        new TypedConstantsPass().Run(function, PassContext.None);

        var constant = Assert.IsType<Constant>(
            Assert.Single(function.Descendants.OfType<StoreStackSlot>()).Value);
        Assert.Equal(LongEnum, constant.Type);
    }

    [Fact]
    public void LoneSurrogateCharConstant_RendersAsUnicodeEscape()
    {
        // A lone surrogate emitted raw cannot survive a UTF-8 encode (writers
        // substitute U+FFFD, corrupting the literal — char.IsHighSurrogate's
        // own bounds rendered as replacement characters). It must escape.
        var charType = TypeRef.CoreLib("System", "Char");
        var container = new BlockContainer();
        var block = new Block(0);
        block.Add(new Return(new Constant('\ud800', charType)));
        container.Add(block);
        var function = EnumFunction(container, charType);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("'\\ud800'", output);
        Assert.DoesNotContain('\ud800'.ToString(), output);
    }

    [Fact]
    public void ConstantOnlyBoolSpill_RendersAsBool()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreStackSlot(0, new Constant(1, intType)));
        block.Add(new StoreLocal(0, boolType, new LoadStackSlot(0, intType)));
        block.Add(new Return(new LoadLocal(0, boolType)));
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [boolType], container);

        new BooleanFoldingPass().Run(function, PassContext.None);

        var slotStore = Assert.Single(function.Descendants.OfType<StoreStackSlot>());
        Assert.Equal(true, Assert.IsType<Constant>(slotStore.Value).Value);
        var localStore = Assert.Single(function.Descendants.OfType<StoreLocal>());
        var slotLoad = Assert.IsType<LoadStackSlot>(localStore.Value);
        Assert.NotNull(slotLoad.Type);
        Assert.Equal("Boolean", slotLoad.Type.Name);
    }
}
