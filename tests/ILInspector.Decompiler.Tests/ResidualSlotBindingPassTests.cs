using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates for value-typed-emission.md "Residual storage binding": the printer's
/// frozen residual policy now runs as a pass, every bound local carries
/// provenance, excluded and untyped webs fail visibly, the coercion discharge
/// reaches a fixpoint, and no stack-slot node reaches the printer.
/// </summary>
[Trait("Area", "Pass")]
public class ResidualSlotBindingPassTests
{
    static readonly TypeRef Holder = TypeRef.Definition("Synthetic", "Samples", "Holder", ValueTypeHint.ReferenceType);
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int16 = TypeRef.CoreLib("System", "Int16");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef Char = TypeRef.CoreLib("System", "Char");

    [Fact]
    public void PipelinePlacesBindingAfterInsertionAndBeforeSelfUpdate()
    {
        var passes = IrPasses.Default;
        int insertion = passes.IndexOf(passes.OfType<CoercionInsertionPass>().Single());
        int binding = passes.IndexOf(passes.OfType<ResidualSlotBindingPass>().Single());
        int selfUpdate = passes.IndexOf(passes.OfType<ScalarSelfUpdatePass>().Single());

        Assert.Equal(insertion + 1, binding);
        Assert.Equal(binding + 1, selfUpdate);
        Assert.Contains(IrPasses.Lowered, static pass => pass is ResidualSlotBindingPass);
        Assert.Contains(IrPasses.CapturingLambdaCompletion, static pass => pass is ResidualSlotBindingPass);
        Assert.DoesNotContain(IrPasses.ForReconstruction<IteratorReconstructionPass>(), static pass => pass is ResidualSlotBindingPass);
    }

    [Fact]
    public void UnifiedWebBecomesOneLocalWithProvenance()
    {
        // Loads typed int and a store typed int: one candidate, one local.
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, Int32)));
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(Int32, block, [new Parameter("value", Int32)]);

        new ResidualSlotBindingPass().Run(function, PassContext.None);

        var (index, binding) = Assert.Single(function.ResidualSlotBindings);
        Assert.Equal(ResidualSlotBindingKind.Unified, binding.Kind);
        Assert.Equal(0, binding.Slot);
        Assert.Equal(Int32, function.Locals[index]);
        Assert.Equal("S_0", function.SynthesizedLocalNames[index]);
        Assert.DoesNotContain(function.Descendants, static node => node is StoreStackSlot or LoadStackSlot);
        Assert.Contains("return S_0;", DecidedPrint.Print(function).Output);
    }

    [Fact]
    public void SplitWebBecomesOneLocalPerRenderedTypeInFirstOccurrenceOrder()
    {
        // long and int ranges of one slot: no candidate satisfies both loads,
        // so the printer's split rule yields S_0 (long, first) and S_0_1 (int).
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "l", Int64)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, Int64)));
        block.Add(new StoreStackSlot(0, new LoadArgument(1, "i", Int32)));
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(Int32, block, [new Parameter("l", Int64), new Parameter("i", Int32)]);

        new ResidualSlotBindingPass().Run(function, PassContext.None);

        var bindings = function.ResidualSlotBindings.OrderBy(static entry => entry.Key).ToList();
        Assert.Equal(2, bindings.Count);
        Assert.All(bindings, entry => Assert.Equal(ResidualSlotBindingKind.Split, entry.Value.Kind));
        Assert.Equal(Int64, function.Locals[bindings[0].Key]);
        Assert.Equal("S_0", function.SynthesizedLocalNames[bindings[0].Key]);
        Assert.Equal(Int32, function.Locals[bindings[1].Key]);
        Assert.Equal("S_0_1", function.SynthesizedLocalNames[bindings[1].Key]);
        string output = DecidedPrint.Print(function).Output!;
        Assert.Contains("S_0 = l;", output);
        Assert.Contains("S_0_1 = i;", output);
        Assert.Contains("return S_0_1;", output);
    }

    [Fact]
    public void ProvenanceCarriesMaterializationVetoesAtThePassPosition()
    {
        // Conflicting load testimony (int and long reads of one store).
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, Int32)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, Int64)));
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("value", Int32)]);
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.Vetoes.HasFlag(SlotMaterializationVeto.ConflictingTypeTestimony), decision.Vetoes.ToString());

        new ResidualSlotBindingPass().Run(function, PassContext.None);

        Assert.All(
            function.ResidualSlotBindings.Values,
            binding => Assert.True(binding.Vetoes.HasFlag(SlotMaterializationVeto.ConflictingTypeTestimony), binding.Vetoes.ToString()));
    }

    [Theory]
    [InlineData("missing-load", SlotMaterializationVeto.MissingLoad)]
    [InlineData("missing-store", SlotMaterializationVeto.MissingStore)]
    [InlineData("underivable", SlotMaterializationVeto.UnderivableTypeTestimony)]
    [InlineData("conflicting", SlotMaterializationVeto.ConflictingTypeTestimony)]
    [InlineData("unrenderable-store", SlotMaterializationVeto.UnrenderableStoreType)]
    [InlineData("incomplete-copy", SlotMaterializationVeto.IncompleteCopyComponent)]
    [InlineData("outside-domain", SlotMaterializationVeto.OutsideCoercionDomain)]
    [InlineData("boolean-sink", SlotMaterializationVeto.BooleanSinkIdentityRecovery)]
    public void EachReachableVetoClassBindsWithItsFlag(string shape, SlotMaterializationVeto expected)
    {
        var function = VetoFixture(shape);
        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.Contains(decisions, decision => decision.Vetoes.HasFlag(expected));

        new ResidualSlotBindingPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants, static node => node is StoreStackSlot or LoadStackSlot);
        Assert.Contains(function.ResidualSlotBindings.Values, binding => binding.Vetoes.HasFlag(expected));
        Assert.NotNull(DecidedPrint.Print(function).Output);
    }

    [Fact]
    public void UntypedWebFailsVisibly()
    {
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", null!)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, null)));
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("value", Object)]);

        var ex = Assert.Throws<InvalidOperationException>(() => new ResidualSlotBindingPass().Run(function, PassContext.None));
        Assert.Contains("no decided type", ex.Message);
    }

    [Fact]
    public void UntypedPieceOfSplitWebFailsVisibly()
    {
        // The typed store would unify alone; the untyped store splits off an
        // "<unknown>" piece the printer used to declare as `var S_0_1;`.
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", String)));
        block.Add(new StoreStackSlot(0, new LoadArgument(1, "other", null!)));
        block.Add(new Return(new LoadStackSlot(0, String)));
        var function = Function(String, block, [new Parameter("value", String), new Parameter("other", Object)]);

        var ex = Assert.Throws<InvalidOperationException>(() => new ResidualSlotBindingPass().Run(function, PassContext.None));
        Assert.Contains("S_0_1", ex.Message);
    }

    [Theory]
    [InlineData("store-only")]
    [InlineData("load-only")]
    [InlineData("mixed")]
    public void ManagedReferenceWebsAreExcludedAndFailVisibly(string shape)
    {
        var refInt = TypeRef.ByRef(Int32);
        var refByte = TypeRef.ByRef(TypeRef.CoreLib("System", "Byte"));
        var block = new Block(0);
        switch (shape)
        {
            case "store-only":
                block.Add(new StoreStackSlot(0, new LoadArgumentAddress(0, "a", Int32)));
                block.Add(new StoreStackSlot(0, new LoadArgumentAddress(1, "b", TypeRef.CoreLib("System", "Byte"))));
                break;
            case "load-only":
                block.Add(new ExpressionStatement(new LoadStackSlot(0, refInt)));
                break;
            default:
                block.Add(new StoreStackSlot(0, new LoadArgumentAddress(0, "a", Int32)));
                block.Add(new ExpressionStatement(new LoadStackSlot(0, refByte)));
                break;
        }
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("a", Int32), new Parameter("b", TypeRef.CoreLib("System", "Byte"))]);

        var ex = Assert.Throws<InvalidOperationException>(() => new ResidualSlotBindingPass().Run(function, PassContext.None));
        Assert.Contains("managed-reference stack slot 0", ex.Message);
    }

    [Fact]
    public void PrinterRejectsAnySurvivingStackSlot()
    {
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(Int32, block, [new Parameter("value", Int32)]);

        var result = DecidedPrint.Print(function);

        Assert.Null(result.Output);
        Assert.Contains("reached C# emission without residual storage binding", result.Diagnostics.Single().Message);
    }

    [Fact]
    public void DischargeWrapsBoundLoadsAtNarrowerSinks()
    {
        // The printer rendered a slot load transparently at a short field; the
        // bound local goes through the shared decision and gains its cast.
        var field = new FieldRef(Holder, "f", Int16);
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(1, "value", Int32)));
        block.Add(new StoreField(field, new LoadArgument(0, "this", Holder), new LoadStackSlot(0, Int32)));
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("value", Int32)], hasThis: true);
        new CoercionInsertionPass().Run(function, PassContext.None);
        Assert.Empty(function.Descendants.OfType<Coerce>());

        new ResidualSlotBindingPass().Run(function, PassContext.None);

        var coerce = Assert.Single(function.Descendants.OfType<Coerce>());
        Assert.Equal(Int16, coerce.Target);
        Assert.Empty(CoercionInvariant.Check(function));
        Assert.Contains("f = (short)S_0;", DecidedPrint.Print(function).Output);
    }

    [Fact]
    public void DischargeReachesFixpointWhenBindingRetypesAnAncestor()
    {
        // Loads typed short and int unify to short (the int load reads a short
        // as assignable); the Binary parent of the int load then retypes to
        // short against its int field sink, so the parent needs a wrapper the
        // first insertion run never saw.
        var field = new FieldRef(Holder, "sum", Int32);
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(1, "value", Int16)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, Int16)));
        block.Add(new StoreField(
            field,
            new LoadArgument(0, "this", Holder),
            new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadStackSlot(0, Int32), new Constant(1, Int32))));
        block.Add(new Return(null));
        var function = Function(Void, block, [new Parameter("value", Int16)], hasThis: true);
        new CoercionInsertionPass().Run(function, PassContext.None);

        new ResidualSlotBindingPass().Run(function, PassContext.None);

        Assert.Empty(CoercionInvariant.Check(function));
        var binding = Assert.Single(function.ResidualSlotBindings);
        Assert.Equal(Int16, function.Locals[binding.Key]);
    }

    [Fact]
    public void SplitPieceReadWithoutReachingStoreDeclaresBare()
    {
        // Store long, load int: no candidate qualifies, so the web splits and
        // the int piece is read on a path no store of that piece reaches. The
        // policy proves no reaching store, so the piece must not be
        // zero-initialised: `= default` would compile and silently drop the
        // term, while the bare declaration keeps the gap CS0165-visible.
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "l", Int64)));
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(Int32, block, [new Parameter("l", Int64)]);

        new ResidualSlotBindingPass().Run(function, PassContext.None);
        string output = DecidedPrint.Print(function).Output!;

        Assert.Equal(2, function.ResidualSlotBindings.Count);
        Assert.All(function.ResidualSlotBindings.Values, static binding => Assert.Equal(ResidualSlotBindingKind.Split, binding.Kind));
        // The stored piece takes the plan's merged initializer; the unreached
        // piece is bare.
        Assert.Contains("long S_0 = l;", output);
        Assert.Contains("int S_0_1;", output);
        Assert.Contains("return S_0_1;", output);
        Assert.DoesNotContain("= default", output);
    }

    [Fact]
    public void NestedSplitPieceReadWithoutReachingStoreDeclaresBare()
    {
        // The same gap inside a local function and a lambda: the raised node
        // carries the body's provenance, the printer restores it when it
        // re-enters the body, and the bare-declaration rule applies there too.
        var localBody = new BlockContainer();
        var localBlock = new Block(0);
        localBlock.Add(new StoreStackSlot(0, new LoadArgument(0, "l", Int64)));
        localBlock.Add(new Return(new LoadStackSlot(0, Int32)));
        localBody.Add(localBlock);
        var localFunction = new LocalFunctionStatement(
            "Inner",
            Int32,
            [new Parameter("l", Int64)],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);

        var lambdaBody = new BlockContainer();
        var lambdaBlock = new Block(0);
        lambdaBlock.Add(new StoreStackSlot(0, new LoadArgument(0, "l", Int64)));
        lambdaBlock.Add(new Return(new LoadStackSlot(0, Int32)));
        lambdaBody.Add(lambdaBlock);
        var func = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Func`2"), [Int64, Int32]);
        var lambda = new Lambda(
            func,
            [new Parameter("l", Int64)],
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            lambdaBody);

        var block = new Block(0);
        block.Add(localFunction);
        block.Add(new StoreLocal(0, func, lambda));
        block.Add(new Return(new Constant(0, Int32)));
        var function = Function(Int32, block, [], locals: [func]).BindResidualSlots();

        var boundLocalFunction = Assert.Single(function.Descendants.OfType<LocalFunctionStatement>());
        var boundLambda = Assert.Single(function.Descendants.OfType<Lambda>());
        Assert.Equal(2, boundLocalFunction.ResidualSlotBindings.Count);
        Assert.Equal(2, boundLambda.ResidualSlotBindings.Count);
        Assert.Empty(function.ResidualSlotBindings);

        string output = DecidedPrint.Print(function).Output!;

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(output, @"\bint S_0_1;").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(output, @"\blong S_0 = l;").Count);
        Assert.DoesNotContain("= default", output);
    }

    [Fact]
    public void RealSplitPieceReadWithoutReachingStoreDeclaresBare()
    {
        // Microsoft.CodeAnalysis 5.0.0, ForEachLoopOperation.get_ChildOperationsCount
        // (0x060029CB): slot 256 splits into an int piece and a Boolean piece
        // the return reads with no reaching store. The bare declaration keeps
        // the gap visible instead of compiling to a dropped term.
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "PrimitiveJoin", "Microsoft.CodeAnalysis.dll");
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, "Microsoft.CodeAnalysis.Operations.ForEachLoopOperation", "get_ChildOperationsCount");
        Assert.NotNull(function);
        Assert.Equal(0x060029CB, function.MetadataToken);

        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        var pieces = function.ResidualSlotBindings.Where(static entry => entry.Value.Slot == 256).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, static entry => Assert.Equal(ResidualSlotBindingKind.Split, entry.Value.Kind));
        string output = DecidedPrint.Print(function).Output!;

        Assert.Contains("bool S_256_1;", output);
        Assert.Contains("return (S_256_1 ? 1 : 0)", output);
        Assert.DoesNotContain("S_256_1 = default", output);
        Assert.DoesNotContain("= default", output);
    }

    [Fact]
    public void RealNestedBodyWebIsDecidedByItsArgumentSink()
    {
        // Microsoft.CodeAnalysis.CSharp, NullableWalker.VisitUnaryOperator: the
        // local function adjustForLifting's slot-1 web was the corpus's one
        // nested residual-bound web until the argument-sink testimony slice
        // (#9371) let TypeWithState.Create's NullableFlowState parameter decide
        // it; the nested body now materializes it inside its own pipeline run,
        // so the raised node carries no residual provenance and the printed body
        // still declares the local. Provenance carrying for a nested web that
        // does stay residual is gated synthetically by
        // NestedSplitPieceReadWithoutReachingStoreDeclaresBare and by
        // NestedSlotMaterializationTests' CreateQueue binding.
        string path = typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly.Location;
        using var source = MetadataSource.Open(path);
        var function = IrImporter.Import(source, "Microsoft.CodeAnalysis.CSharp.NullableWalker", "VisitUnaryOperator");
        Assert.NotNull(function);

        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        var adjustForLifting = Assert.Single(function.Descendants.OfType<LocalFunctionStatement>(), static local => local.Name == "adjustForLifting");
        Assert.Empty(adjustForLifting.ResidualSlotBindings);
        Assert.Empty(function.ResidualSlotBindings);

        string output = DecidedPrint.Print(function).Output!;
        Assert.Contains("TypeWithState adjustForLifting(TypeWithState argumentResult)", output);
        Assert.Contains("NullableFlowState S_1 = ", output);
        Assert.DoesNotContain("S_1 = default", output);
    }

    [Fact]
    public void BoundLocalSelfUpdateIsOwnedByScalarSelfUpdatePass()
    {
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
        block.Add(new StoreStackSlot(0, new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, new LoadStackSlot(0, Int32), new Constant(1, Int32))));
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(Int32, block, [new Parameter("value", Int32)]);

        new ResidualSlotBindingPass().Run(function, PassContext.None);
        new ScalarSelfUpdatePass().Run(function, PassContext.None);

        Assert.Contains("S_0++;", DecidedPrint.Print(function).Output);
    }

    [Theory]
    [InlineData("existing-local", "fresh local")]
    [InlineData("extra-local", "one-to-one")]
    [InlineData("wrong-type", "local table type")]
    [InlineData("clone-producer", "ordered node identity")]
    public void CorrespondenceInvariantRejectsFaultyBindings(string fault, string diagnostic)
    {
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "l", Int64)));
        block.Add(new ExpressionStatement(new LoadStackSlot(0, Int64)));
        block.Add(new StoreStackSlot(0, new LoadArgument(1, "i", Int32)));
        block.Add(new Return(new LoadStackSlot(0, Int32)));
        var function = Function(Int32, block, [new Parameter("l", Int64), new Parameter("i", Int32)], locals: [Int32]);
        var plan = new ResidualSlotPolicy(function).Plan(CoercionSinks.ScopeNodes(function.Body).ToList());
        var invariant = ResidualSlotBindingInvariant.Capture(function, plan);
        var originalStore = function.Descendants.OfType<StoreStackSlot>().First();

        new ResidualSlotBindingPass().Run(function, PassContext.None);
        var longRead = function.Descendants.OfType<LoadLocal>().First(load => load.Type.Equals(Int64));
        var intRead = function.Descendants.OfType<LoadLocal>().First(load => load.Type.Equals(Int32));
        switch (fault)
        {
            case "existing-local":
                longRead.ReplaceWith(new LoadLocal(0, Int64));
                break;
            case "extra-local":
                function.AddSynthesizedLocal(Int64, "extra");
                break;
            case "wrong-type":
                longRead.ReplaceWith(new LoadLocal(longRead.Index, Int32));
                break;
            default:
                var store = function.Descendants.OfType<StoreLocal>().First();
                store.Value.ReplaceWith((IrExpression)store.Value.Clone());
                break;
        }

        var ex = Assert.Throws<InvalidOperationException>(invariant.Check);
        Assert.Contains(diagnostic, ex.Message);
        _ = originalStore;
    }

    static IrFunction VetoFixture(string shape)
    {
        var block = new Block(0);
        IReadOnlyList<Parameter> parameters = [new Parameter("value", Int32), new Parameter("text", String), new Parameter("flag", Bool)];
        IReadOnlyList<TypeRef> locals = [];
        TypeRef returnType = Void;
        switch (shape)
        {
            case "missing-load":
                block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
                break;
            case "missing-store":
                block.Add(new ExpressionStatement(new LoadStackSlot(0, Int32)));
                break;
            case "underivable":
                block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
                block.Add(new ExpressionStatement(new LoadStackSlot(0, null)));
                break;
            case "conflicting":
                block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
                block.Add(new ExpressionStatement(new LoadStackSlot(0, Int32)));
                block.Add(new ExpressionStatement(new LoadStackSlot(0, Int64)));
                break;
            case "unrenderable-store":
                block.Add(new StoreStackSlot(0, new LoadArgument(1, "text", String)));
                block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
                block.Add(new ExpressionStatement(new LoadStackSlot(0, Int32)));
                break;
            case "incomplete-copy":
                block.Add(new StoreStackSlot(256, new LoadArgument(0, "value", Int32)));
                block.Add(new StoreStackSlot(0, new LoadStackSlot(256, Int32)));
                block.Add(new ExpressionStatement(new LoadStackSlot(0, Int32)));
                block.Add(new ExpressionStatement(new LoadStackSlot(0, null)));
                break;
            case "outside-domain":
                var opaque = TypeRef.Definition("Synthetic", "Samples", "Opaque", ValueTypeHint.Unknown);
                block.Add(new StoreStackSlot(0, new LoadArgument(3, "opaque", opaque)));
                block.Add(new ExpressionStatement(new LoadStackSlot(0, opaque)));
                parameters = [.. parameters, new Parameter("opaque", opaque)];
                break;
            case "boolean-sink":
                block.Add(new StoreStackSlot(0, new LoadArgument(0, "value", Int32)));
                block.Add(new StoreLocal(0, Bool, new LoadStackSlot(0, Int32)));
                locals = [Bool];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
        block.Add(new Return(null));
        return Function(returnType, block, parameters, locals: locals);
    }

    static Block BlockOf(IrNode statement)
    {
        var block = new Block(0);
        block.Add(statement);
        return block;
    }

    static IrFunction Function(TypeRef returnType, Block block, IReadOnlyList<Parameter> parameters, bool hasThis = false, IReadOnlyList<TypeRef>? locals = null)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(returnType, [.. parameters], HasThis: hasThis, GenericParameterCount: 0),
            [.. locals ?? []],
            body);
    }
}
