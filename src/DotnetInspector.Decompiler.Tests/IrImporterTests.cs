using DotnetInspector.Decompiler.Pipeline;

namespace DotnetInspector.Decompiler.Tests;

public class IrImporterTests
{
    static IrFunction ImportFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function;
    }

    static Block SingleBlock(IrFunction function)
        => (Block)Assert.Single(function.Body.Children);

    [Fact]
    public void Add_BuildsTypedExpressionTree()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));

        var ret = Assert.IsType<Return>(Assert.Single(SingleBlock(function).Children));
        var binary = Assert.IsType<Binary>(ret.Value);
        Assert.Equal(BinaryKind.Add, binary.Kind);
        Assert.Equal("int", binary.ResultType?.ToDisplayString());
        Assert.IsType<LoadArgument>(binary.Left);
        Assert.IsType<LoadArgument>(binary.Right);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Empty(function.Diagnostics);
        function.CheckInvariant();
    }

    [Fact]
    public void ImportedFunction_SurvivesSourceDisposal()
    {
        IrFunction function;
        using (var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location))
        {
            function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Add))!;
        }

        string dump = IrPrinter.Dump(function);
        Assert.Contains("Binary.Add", dump);
        Assert.Contains("LoadArgument", dump);
        Assert.Contains("fidelity: Full", dump);
    }

    [Fact]
    public void ReplaceWith_RewiresParentAndSlot()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));
        var binary = (Binary)((Return)SingleBlock(function).Children[0]).Value!;
        var left = binary.Left;

        var constant = new Constant(42, TypeRef.CoreLib("System", "Int32"));
        left.ReplaceWith(constant);

        Assert.Same(constant, binary.Left);
        Assert.Same(binary, constant.Parent);
        Assert.Equal(0, constant.ChildIndex);
        Assert.Null(left.Parent);
        Assert.Equal(-1, left.ChildIndex);
        function.CheckInvariant();
    }

    [Fact]
    public void Adoption_RejectsNodesThatAlreadyHaveParents()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));
        var binary = (Binary)((Return)SingleBlock(function).Children[0]).Value!;

        // Re-using an attached node without detaching it would silently
        // corrupt the tree; the IR refuses at the rewrite site.
        Assert.Throws<InvalidOperationException>(
            () => new ExpressionStatement(binary.Left));
    }

    [Fact]
    public void ExceptionRegions_ImportFlat_WithTypedHandlerEntry()
    {
        var function = ImportFixture(nameof(CfgSampleClass.ChecksThenTry));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var region = Assert.Single(function.Regions);
        Assert.Equal(HandlerKind.Catch, region.Kind);
        // Region boundaries are block leaders in the flat container.
        Assert.True(function.Body.IndexOfOffset(region.TryOffset) >= 0);
        Assert.True(function.Body.IndexOfOffset(region.HandlerOffset) >= 0);
        // The handler's first block consumes the CLR-pushed exception.
        var caught = function.Descendants.OfType<CaughtException>().First();
        Assert.Equal(region.CatchType, caught.Type);
        Assert.NotEmpty(function.Descendants.OfType<Leave>());
        function.CheckInvariant();
    }

    [Fact]
    public void BranchingMethod_BuildsBlocks()
    {
        var function = ImportFixture(nameof(CfgSampleClass.AbsShort));

        Assert.True(function.Body.Blocks.Count > 1);
        Assert.NotEmpty(function.Descendants.OfType<ConditionalBranch>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        function.CheckInvariant();
    }

    [Fact]
    public void Convert_UnsignedNarrowing_MapsToCorrectTargetUnchecked()
    {
        // conv.u1 and conv.u2 live far from the main conv.* opcode range;
        // a range-based importer mapped them to UIntPtr and marked them
        // checked (mid-point review catch).
        var function = ImportFixture(nameof(CfgSampleClass.ToByte));

        var convert = Assert.Single(function.Descendants.OfType<Pipeline.Convert>());
        Assert.Equal("byte", convert.Target.ToDisplayString());
        Assert.False(convert.IsChecked);
        Assert.False(convert.IsUnsigned);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }

    [Fact]
    public void Fidelity_ScansSignatureAndNonExpressionTypes()
    {
        // An unsupported type anywhere — signature, locals, store targets —
        // must cap fidelity, not only expression result types.
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(null));
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "Void"),
            [new Parameter("p", TypeRef.Unsupported("function pointer"))],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("System", "Object"), signature, [], container);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void CoreLib_IsNullOrEmpty_ImportsAtFullFidelity()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.String", "IsNullOrEmpty");

        Assert.NotNull(function);
        Assert.Equal(3, function.Body.Blocks.Count);
        var conditional = Assert.Single(function.Descendants.OfType<ConditionalBranch>());
        Assert.IsType<LogicalNot>(conditional.Condition);
        Assert.Single(function.Descendants.OfType<Comparison>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(function.Body.Blocks[2].StartOffset, conditional.TargetOffset);
    }

    [Fact]
    public void CoreLib_Ternary_ImportsViaStackSlots()
    {
        // 'TargetFrameworkName ??= ...' carries a value across block
        // boundaries — the canonical stack-carrying edge, materialized
        // through position-indexed slots so every predecessor of the join
        // stores to the same slot.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.AppContext", "get_TargetFrameworkName");

        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.NotEmpty(function.Descendants.OfType<StoreStackSlot>());
        Assert.NotEmpty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant();
    }

    [Fact]
    public void CoreLib_GenericMethodCall_ResolvesMethodSpecification()
    {
        // Array.get_Length calls Unsafe.As<...> — a MethodSpecification.
        // Unresolved, its arity-0 fallback mis-popped the stack and poisoned
        // everything downstream (2,484 false stops in the CoreLib sweep).
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Array", "get_Length");

        Assert.NotNull(function);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var genericCall = function.Descendants.OfType<Call>().First(c => !c.Callee.TypeArguments.IsEmpty);
        Assert.NotEqual("?", genericCall.Callee.Name);
        // The call site reports instantiated types, not the callee's formal
        // !!N parameters (second-review fix).
        Assert.False(ContainsGenericParameter(genericCall.Callee.ReturnType));
        Assert.All(genericCall.Callee.ParameterTypes, p => Assert.False(ContainsGenericParameter(p)));
    }

    static bool ContainsGenericParameter(TypeRef type)
        => type.Kind is TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter
            || (type.ElementType is { } element && ContainsGenericParameter(element))
            || type.TypeArguments.Any(ContainsGenericParameter);

    [Fact]
    public void AddressOf_OutArgument_ImportsAsLocalAddress()
    {
        var function = ImportFixture(nameof(CfgSampleClass.ParseOrZero));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var address = function.Descendants.OfType<LoadLocalAddress>().First();
        Assert.Equal("ref int", address.ResultType?.ToDisplayString());
        var call = function.Descendants.OfType<Call>().First(c => c.Callee.Name == "TryParse");
        Assert.Contains(call.Arguments, a => a is LoadLocalAddress);
    }

    [Fact]
    public void ElementAccess_ImportsTypedLoadAndStore()
    {
        var load = ImportFixture(nameof(CfgSampleClass.FirstElement));
        var store = ImportFixture(nameof(CfgSampleClass.SetFirstElement));

        Assert.Equal(DecompilationFidelity.Full, load.Fidelity);
        Assert.Equal("int", Assert.Single(load.Descendants.OfType<LoadElement>()).ResultType?.ToDisplayString());
        Assert.Equal(DecompilationFidelity.Full, store.Fidelity);
        Assert.Single(store.Descendants.OfType<StoreElement>());
    }

    [Fact]
    public void Switch_ImportsWithTargetsAsLeaders()
    {
        var function = ImportFixture(nameof(CfgSampleClass.PowerOfTwo));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        var switchBranch = Assert.Single(function.Descendants.OfType<SwitchBranch>());
        Assert.True(switchBranch.TargetOffsets.Length >= 4);
        // Every switch target starts a block.
        Assert.All(switchBranch.TargetOffsets,
            target => Assert.True(function.Body.IndexOfOffset(target) >= 0));
        function.CheckInvariant();
    }

    [Fact]
    public void TryFinally_ImportsWithEndFinally()
    {
        var function = ImportFixture(nameof(CfgSampleClass.TryFinallyAdd));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(HandlerKind.Finally, Assert.Single(function.Regions).Kind);
        Assert.Single(function.Descendants.OfType<EndFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void ExceptionFilter_ImportsWithEndFilter()
    {
        var function = ImportFixture(nameof(CfgSampleClass.FilteredLength));

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(HandlerKind.Filter, Assert.Single(function.Regions).Kind);
        Assert.Single(function.Descendants.OfType<EndFilter>());
        Assert.NotEmpty(function.Descendants.OfType<CaughtException>());
        function.CheckInvariant();
    }

    [Fact]
    public void CoreLib_SimpleCorpusMethods_ImportAtFullFidelity()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        (string Type, string Method)[] corpus =
        [
            ("System.String", "IsNullOrEmpty"),
            ("System.Math", "Max"),
            ("System.Math", "Clamp"),
            ("System.Text.StringBuilder", "Clear"),
            ("System.Collections.Generic.HashSet`1", "Contains"),
        ];

        foreach (var (type, method) in corpus)
        {
            var function = IrImporter.Import(source, type, method);
            Assert.NotNull(function);
            Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
            function.CheckInvariant();
        }
    }

    [Fact]
    public void CoreLib_StraightLineMethod_ImportsCallsAndFields()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        // get_Count: ldarg.0; ldfld _size; ret
        var function = IrImporter.Import(source, "System.Collections.Generic.List`1", "get_Count");

        Assert.NotNull(function);
        var block = (Block)Assert.Single(function.Body.Children);
        var ret = Assert.IsType<Return>(Assert.Single(block.Children));
        var field = Assert.IsType<LoadField>(ret.Value);
        Assert.Equal("_size", field.Field.Name);
        Assert.Equal("int", field.ResultType?.ToDisplayString());
        Assert.IsType<LoadArgument>(field.Instance);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
    }
}

public class JoinTypeConflictTests : IDisposable
{
    readonly Stack<IDisposable> _disposables = new();

    public void Dispose()
    {
        while (_disposables.Count > 0)
            _disposables.Pop().Dispose();
    }

    IrFunction BuildSynthetic(byte[] il)
    {
        var source = MetadataSource.Open(typeof(object).Assembly.Location);
        _disposables.Push(source);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var method = new ImportedMethod(
            TypeRef.CoreLib("Synthetic", "T"), "M", signature,
            new MethodBody([.. il], MaxStack: 8, Locals: [], LocalNames: [], Handlers: []));
        return IrImporter.Build(source, method, GenericScope.Empty);
    }

    [Fact]
    public void TrailingLabeledReturn_IsNotTrimmedToADanglingLabel()
    {
        // br.s to the final 'return;' — the return is a branch target's only
        // statement, so trimming it would strand the label as invalid C#.
        var function = BuildSynthetic([0x2B, 0x00, 0x2A]);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("IL_0002:\nreturn;", output);
    }

    [Fact]
    public void JoinTypeConflict_BeforeJoinIsBuilt_MergesToHonestUnknown()
    {
        // ldc.i4.1; brtrue.s L1; ldc.i4.0; br.s J; L1: ldnull; J: pop; ret
        // Two forward edges carry int and object into J: pre-build conflict
        // merges to null (honest unknown), never a guessed type.
        var function = BuildSynthetic([0x17, 0x2D, 0x03, 0x16, 0x2B, 0x01, 0x14, 0x26, 0x2A]);

        var load = Assert.Single(function.Descendants.OfType<LoadStackSlot>(),
            l => l.Parent is ExpressionStatement);
        Assert.Null(load.Type);
        // An unknown type anywhere is a fidelity signal: the merged-null
        // slot caps the function at Partial, with a join-type diagnostic
        // saying which types disagreed.
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("(join-type)", diagnostic.Message);
        function.CheckInvariant();
    }

    IrFunction BuildSyntheticWithRegion(byte[] il, HandlerRegion region)
    {
        var source = MetadataSource.Open(typeof(object).Assembly.Location);
        _disposables.Push(source);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var method = new ImportedMethod(
            TypeRef.CoreLib("Synthetic", "T"), "M", signature,
            new MethodBody([.. il], MaxStack: 8, Locals: [], LocalNames: [], Handlers: [region]));
        return IrImporter.Build(source, method, GenericScope.Empty);
    }

    [Fact]
    public void Endfinally_NonEmptyStack_IsMalformed_StopsHonestly()
    {
        // try { leave } finally { ldc.i4.1; endfinally }  — ECMA requires an
        // empty stack at endfinally; the stray value must not import as Full.
        var function = BuildSyntheticWithRegion(
            [0xDE, 0x02, 0x17, 0xDC, 0x2A],
            new HandlerRegion(HandlerKind.Finally, 0, 2, 2, 2, 0, null));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("endfinally", diagnostic.Message);
        function.CheckInvariant();
    }

    [Fact]
    public void Endfilter_MoreThanVerdict_IsMalformed_StopsHonestly()
    {
        // Filter code that never consumes the CLR-pushed exception: after
        // popping the verdict the exception remains — malformed per ECMA.
        var function = BuildSyntheticWithRegion(
            [0xDE, 0x06, 0x17, 0xFE, 0x11, 0x26, 0xDE, 0x00, 0x2A],
            new HandlerRegion(HandlerKind.Filter, 0, 2, 5, 3, 2, null));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("filter verdict", diagnostic.Message);
        function.CheckInvariant();
    }

    [Fact]
    public void JoinTypeConflict_AfterJoinIsBuilt_StopsHonestly()
    {
        // ldc.i4.0; br.s J; J: pop; ret; (unreachable) ldnull; br.s J
        // The join is built with int before the object-carrying edge arrives;
        // already-emitted loads cannot be retyped, so the import stops.
        var function = BuildSynthetic([0x16, 0x2B, 0x00, 0x26, 0x2A, 0x14, 0x2B, 0xFB]);

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedConstruct, diagnostic.Id);
        Assert.Contains("types disagree", diagnostic.Message);
        function.CheckInvariant();
    }
}

public class CSharpPrinterTests
{
    static string PrintFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n");
    }

    [Fact]
    public void StraightLine_PrintsCurrentStyle()
    {
        Assert.Equal("return a + b;\n", PrintFixture(nameof(CfgSampleClass.Add)));
    }

    [Fact]
    public void Branches_PrintAsHonestLabelsAndGotos()
    {
        string output = PrintFixture(nameof(CfgSampleClass.AbsShort));

        Assert.Contains("goto IL_", output);
        Assert.Contains(":", output);
        Assert.DoesNotContain("/* ", output);  // every node has a rendering
    }

    [Fact]
    public void UnsignedConversion_CastsSignedSource()
    {
        // conv.r.un on a signed int: the source reads as unsigned, so the
        // C# spelling needs the (uint) cast or the value is wrong for
        // negative inputs.
        var convert = new Pipeline.Convert(
            TypeRef.CoreLib("System", "Double"), isChecked: false, isUnsigned: true,
            new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32")));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(convert));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Double"),
            [new Parameter("a", TypeRef.CoreLib("System", "Int32"))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (double)(uint)a;", CSharpPrinter.Print(function).Output!.Trim());
    }

    [Fact]
    public void TypedConstants_BoxedAndElementConstants_Retype()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var boxed = new Box(boolType, new Constant(1, TypeRef.CoreLib("System", "Int32")));
        block.Add(new ExpressionStatement(boxed));
        block.Add(new StoreElement(boolType,
            new LoadArgument(0, "flags", TypeRef.SzArray(boolType)),
            new Constant(0, TypeRef.CoreLib("System", "Int32")),
            new Constant(1, TypeRef.CoreLib("System", "Int32"))));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("flags", TypeRef.SzArray(boolType))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        new TypedConstantsPass().Run(function);

        Assert.Equal(true, ((Constant)boxed.Operand).Value);
        var store = function.Descendants.OfType<StoreElement>().Single();
        Assert.Equal(true, ((Constant)store.Value).Value);
        // The index constant stays int — element typing applies to the value.
        Assert.Equal(0, ((Constant)store.Index).Value);
        function.CheckInvariant();
    }

    [Fact]
    public void UnsignedOperations_CastSignedOperands_PlainWhenAlreadyUnsigned()
    {
        var signedInt = new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32"));
        var signedInt2 = new LoadArgument(1, "b", TypeRef.CoreLib("System", "Int32"));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new Binary(BinaryKind.Divide, isChecked: false, isUnsigned: true, signedInt, signedInt2)));
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "UInt32"),
            [new Parameter("a", TypeRef.CoreLib("System", "Int32")), new Parameter("b", TypeRef.CoreLib("System", "Int32"))],
            HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (uint)a / (uint)b;", CSharpPrinter.Print(function).Output!.Trim());

        // Already-unsigned operands print plain — div.un's semantics are
        // already conveyed by the types.
        var unsignedArg = new LoadArgument(0, "a", TypeRef.CoreLib("System", "UInt32"));
        var unsignedArg2 = new LoadArgument(1, "b", TypeRef.CoreLib("System", "UInt32"));
        var container2 = new BlockContainer();
        var block2 = new Block(0);
        container2.Add(block2);
        block2.Add(new Return(new Binary(BinaryKind.Divide, isChecked: false, isUnsigned: true, unsignedArg, unsignedArg2)));
        var function2 = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container2);

        Assert.Equal("return a / b;", CSharpPrinter.Print(function2).Output!.Trim());
    }

    [Fact]
    public void Constructor_ImplicitBaseCall_IsSuppressed()
    {
        // A no-argument base-constructor call is implicit in C#; only the
        // field initializer remains in the body. (Argumentful base(...)
        // still prints until constructor initializers are modeled.)
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, ".ctor");

        Assert.NotNull(function);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("base(", output);
        Assert.DoesNotContain(".ctor", output);
        Assert.Contains("_shadowed = 1;", output);
    }

    [Fact]
    public void UnsignedComparison_InverseCondition_KeepsUnsignedCasts()
    {
        // brfalse over an unsigned comparison folds to the inverse operator;
        // the unsigned operand casts must survive the fold.
        var intType = TypeRef.CoreLib("System", "Int32");
        var comparison = new Comparison(ComparisonKind.LessThan, isUnsigned: true,
            new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ConditionalBranch(new LogicalNot(comparison), 4));
        var target = new Block(4);
        container.Add(target);
        target.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("if ((uint)a >= (uint)b) goto IL_0004;", output);
    }

    [Fact]
    public void Parity_StraightLineCoreLibMethod_MatchesCurrentEmitter()
    {
        // The first parity class: methods needing no raising at all.
        using var stream = File.OpenRead(typeof(object).Assembly.Location);
        using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);

        var context = MethodBodyContext.Create(peReader, "System.Collections.Generic.List`1", "get_Count");
        var function = IrImporter.Import(source, "System.Collections.Generic.List`1", "get_Count");
        Assert.NotNull(context);
        Assert.NotNull(function);

        string baseline = CSharpEmitter.Emit(context).ReplaceLineEndings("\n").TrimEnd();
        string candidate = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Equal(baseline, candidate);
        Assert.Equal("return _size;", candidate);
    }
}

public class RaisingPassTests
{
    static string PrintWithPasses(string typeName, string methodName, MetadataSource source)
    {
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n").TrimEnd();
    }

    [Fact]
    public void TypedConstants_BoolReturn_PrintsFalse()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        Assert.Equal("return false;", PrintWithPasses("System.Array", "get_IsReadOnly", source));
    }

    [Fact]
    public void PropertySugar_GetterCall_PrintsPropertyAccess()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        Assert.Equal("return s.Length;",
            PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.LengthOf), source));
    }

    [Fact]
    public void ExpressionInlining_SingleUseTemp_Collapses()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        Assert.Equal("return x + x;",
            PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Twice), source));
    }

    [Fact]
    public void MultiUseLocal_DeclaresAtItsEntryBlockStore()
    {
        // Two loads: no inlining; the declaration merges into the store,
        // current-style. Debug uses a local (V_0), Release a dup slot
        // (S_256) — the merged-declaration shape must hold for both.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Reused), source);

        Assert.Matches(@"int (V_0|S_\d+) = x \+ 1;", output);
        Assert.DoesNotMatch(@"int (V_0|S_\d+);", output);
        Assert.Matches(@"return (V_0|S_\d+) \* \1;", output);
    }

    [Fact]
    public void TypedConstants_RunAgainAfterInlining_CatchExposedPositions()
    {
        // A slot constant only reaches its typed position (the bool return)
        // after inlining — the pass list runs typed constants twice.
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, boolType, new Constant(0, TypeRef.CoreLib("System", "Int32"))));
        block.Add(new Return(new LoadLocal(0, boolType)));
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return false;", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }

    [Fact]
    public void Inlining_DoesNotCrossExceptionRegionBoundaries()
    {
        // The handler block is physically next but not normal fallthrough;
        // moving the computation would change what the try protects.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var tryBlock = new Block(0);
        container.Add(tryBlock);
        tryBlock.Add(new StoreLocal(0, intType, new Constant(7, intType)));
        var handlerBlock = new Block(4);
        container.Add(handlerBlock);
        handlerBlock.Add(new Return(new LoadLocal(0, intType)));
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container)
        {
            Regions = [new HandlerRegion(HandlerKind.Catch, 0, 4, 4, 4, 0, null)],
        };

        new ExpressionInliningPass().Run(function);

        Assert.Single(function.Descendants.OfType<StoreLocal>());
        Assert.Single(function.Descendants.OfType<LoadLocal>());
        function.CheckInvariant();
    }

    [Fact]
    public void NonBoolBranchOperands_SpellTheComparison()
    {
        // brtrue over a reference must not print 'if (s)' — that is not C#.
        var stringType = TypeRef.CoreLib("System", "String");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ConditionalBranch(new LoadArgument(0, "s", stringType), 4));
        var target = new Block(4);
        container.Add(target);
        target.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("s", stringType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Contains("if (s != null) goto IL_0004;", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void Passes_PreserveInvariants_AcrossCoreLibSample()
    {
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        foreach (var (type, method) in new[]
        {
            ("System.String", "IsNullOrEmpty"),
            ("System.Collections.Generic.Dictionary`2", "ContainsValue"),
            ("System.Text.StringBuilder", "Clear"),
        })
        {
            var function = IrImporter.Import(source, type, method);
            Assert.NotNull(function);
            IrPasses.Run(function);  // CheckInvariant runs after every pass in debug
            Assert.True(CSharpPrinter.Print(function).Succeeded);
        }
    }
}
