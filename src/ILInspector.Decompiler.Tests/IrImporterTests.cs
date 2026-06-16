using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

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

        // The join-type diagnostic records the disagreement; the only
        // consumer of the unknown value was a pop of a pure load, which
        // elides — so no unknown-typed expression survives into the tree
        // and fidelity stays Full while the diagnostic preserves the trace.
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Contains("(join-type)", diagnostic.Message);
        Assert.DoesNotContain(function.Descendants.OfType<LoadStackSlot>(),
            l => l.Parent is ExpressionStatement);
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
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
    public void GenericInstanceNullCheck_RendersIsNull()
    {
        // A brtrue/brfalse operand can never be a struct value, so a generic
        // instance (List<int>) is soundly a reference type: null-test it,
        // never the uncompilable !items.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CountOrZero), source);

        Assert.Contains("items is null", output);
        Assert.DoesNotContain("!items", output);
    }

    [Fact]
    public void SameAssemblyReferenceNullCheck_RendersIsNull()
    {
        // CfgNullableTarget is a non-generic reference type defined in this
        // assembly; same-assembly shape resolution proves it a reference, so
        // the guard null-tests rather than printing the uncompilable !gate.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GateOrZero), source);

        Assert.Contains("gate is null", output);
        Assert.DoesNotContain("!gate", output);
    }

    [Fact]
    public void NestedGenericType_RendersInnermostName_NotOuter()
    {
        // List<T>.GetEnumerator returns the nested List`1+Enumerator. The old
        // StripArity-at-first-backtick bug rendered it new List<T>(this); the
        // correct innermost-only spelling is new Enumerator(this) — Enumerator
        // is non-generic (the T belongs to the elided outer List), so
        // Enumerator<T> would be CS0308.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        Assert.Equal("return new Enumerator(this);",
            PrintWithPasses("System.Collections.Generic.List`1", "GetEnumerator", source));
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
    public void Inlining_ArgumentRead_NotPureWhenAddressEscapes()
    {
        // V_0 = x; M(ref x, V_0): inlining the copy would read x AFTER the
        // ref call may have mutated it. The copy must stay.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, intType, new LoadArgument(0, "x", intType)));
        var callee = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "M",
            TypeRef.CoreLib("System", "Void"), [TypeRef.ByRef(intType), intType], HasThis: false);
        block.Add(new ExpressionStatement(new Call(callee, isVirtual: false,
            [new LoadArgumentAddress(0, "x", intType), new LoadLocal(0, intType)])));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("F", TypeRef.CoreLib("Synthetic", "T"), signature, [intType], container);

        new ExpressionInliningPass().Run(function);

        Assert.Single(function.Descendants.OfType<StoreLocal>());
        function.CheckInvariant();
    }

    [Fact]
    public void Truthiness_UnknownDefinition_DoesNotGuessNull()
    {
        // A bare definition could be a struct or an enum; '!= null' would be
        // a guess that might not compile. The raw value prints instead.
        var unknownType = TypeRef.Definition("Some.Assembly", "Some", "Widget");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ConditionalBranch(new LoadArgument(0, "w", unknownType), 4));
        var target = new Block(4);
        container.Add(target);
        target.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("w", unknownType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("!= null", output);
        Assert.Contains("if (w) goto IL_0004;", output);
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

        Assert.Contains("if (s is not null) goto IL_0004;", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void FloatUnorderedOrdering_PrintsNegatedOrderedDual()
    {
        // 'a >= b unordered' over doubles: C#'s >= is ordered, so the honest
        // spelling is !(a < b) — NaN inputs take the same path as the IL.
        var doubleType = TypeRef.CoreLib("System", "Double");
        var comparison = new Comparison(ComparisonKind.GreaterThanOrEqual, isUnsigned: true,
            new LoadArgument(0, "a", doubleType), new LoadArgument(1, "b", doubleType));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(comparison));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Boolean"),
            [new Parameter("a", doubleType), new Parameter("b", doubleType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return !(a < b);", CSharpPrinter.Print(function).Output!.Trim());
    }

    [Fact]
    public void BooleanFolding_GuardReturn_FoldsToSourceForm()
    {
        // The corpus front door, character-identical to the dotnet/runtime
        // source: return value == null || value.Length == 0; (is-form per
        // the taste doc).
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        string output = PrintWithPasses("System.String", "IsNullOrEmpty", source);

        Assert.Equal("return value is null || value.Length == 0;\n", output + "\n");
    }

    [Fact]
    public void BooleanFolding_GuardAndBoolComparison_FoldToAndChain()
    {
        // The && lowering plus the ceq-with-zero value form, folded back to
        // the exact dotnet/runtime source: _size != 0 && IndexOf(item) >= 0.
        // (Release-compiled CoreLib: the Debug result-local pattern with a
        // shared failure tail is a later structuring slice.)
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        string output = PrintWithPasses("System.Collections.Generic.List`1", "Contains", source);

        Assert.Equal("return _size != 0 && IndexOf(item) >= 0;", output);
    }

    [Fact]
    public void BooleanFolding_TernarySource_PerConfigShape()
    {
        // Debug lowers the ternary through a stack-slot diamond, which folds
        // back to the ternary (with the double-negative unwrapped and arms
        // swapped). Release lowers it as dual returns, which stay in
        // statement form — the same negation-shaped rule the baseline
        // applies (old pipeline PR #421).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Pick), source);

#if DEBUG
        Assert.Equal("return c ? a : b;", output);
#else
        Assert.Equal("""
            if (!c)
            {
                return b;
            }
            return a;
            """.ReplaceLineEndings("\n"), output);
#endif
    }

    [Fact]
    public void Structuring_NestedGuards_NestAndDropGotos()
    {
        using var fixtureSource = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.AbsShort), fixtureSource);

        Assert.DoesNotContain("goto", output);
        Assert.Contains("if (", output);
        // The inner overflow guard nests inside the outer negative guard.
        Assert.Contains("    if (", output);
    }

    [Fact]
    public void Structuring_GuardedWhile_RaisesToForLoop()
    {
        // The full composition: guard, for-recognition with the declaration
        // in the initializer, increment sugar, indexer — character-identical
        // to the current emitter's rendering of this method.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        string output = PrintWithPasses("System.String", "IsNullOrWhiteSpace", source);

        Assert.Equal("""
            if (value is null)
            {
                return true;
            }
            for (int V_0 = 0; V_0 < value.Length; V_0++)
            {
                if (!char.IsWhiteSpace(value[V_0]))
                {
                    return false;
                }
            }
            return true;
            """.ReplaceLineEndings("\n"), output);
    }

    [Fact]
    public void Structuring_BottomTestedLoop_RaisesToDoWhile()
    {
        // The bottom-tested back edge raises to a do-while: no goto, no label.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.DoWhileSum), source);

        Assert.Contains("do", output);
        Assert.Contains("while (", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void DoWhileWithBreak_RaisesBreak()
    {
        // The conditional exit out of the loop raises to `if (...) break;`
        // inside a structured do-while — no goto, no label.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.DoWhileWithBreak), source);

        Assert.Contains("do", output);
        Assert.Contains("while (", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void TopTestedLoopWithBreak_RaisesBreak()
    {
        // A forward exit out of a top-tested (while/for) loop body raises to a
        // structured `if (...) break;` — the whole method de-gotos, the for
        // loop composes on top.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = PrintWithPasses(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.LoopWithBreak), source);

        Assert.Contains("for (", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void TypeOfAndOperatorSugar_PrintSourceForms()
    {
        // typeof folding plus op_Equality spelling: the generic-dispatch
        // idiom prints as the source writes it.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Numerics.Vector", "AllWhereAllBitsSet");
        Assert.NotNull(function);
        string output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Contains("typeof(T) == typeof(float)", output);
        Assert.DoesNotContain("GetTypeFromHandle", output);
        Assert.DoesNotContain("op_Equality", output);
    }

    [Fact]
    public void DefaultInitialization_MergesIntoDeclaration()
    {
        // initobj over a local address: 'CancellationToken V_0 = default;',
        // not '*(ref V_0) = default(...)' nor a separate declaration.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Threading.CancellationToken", "get_None");
        Assert.NotNull(function);
        string output = CSharpPrinter.PrintRaised(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Equal("CancellationToken V_0 = default;\nreturn V_0;", output);
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

/// <summary>
/// The EH structuring slice: flat regions raise to TryCatch/TryFinally with
/// consumed regions, entry stores fold into clause variables, tail leaves
/// trim to fallthrough — and out-of-slice shapes (filters) keep the flat
/// form with regions intact.
/// </summary>
public class EhStructuringTests
{
    static (IrFunction Function, string Output) RaiseFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return (function, result.Output!.ReplaceLineEndings("\n").TrimEnd());
    }

    [Fact]
    public void TryFinally_RaisesToStructuredForm()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.TryFinallyAdd));

        Assert.Empty(function.Regions);
        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("finally", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("endfinally", output);
    }

    [Fact]
    public void Catch_EntryStore_FoldsIntoClauseVariable()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.CatchLogs));

#if DEBUG
        // Debug stores the catch variable at handler entry; the store folds
        // into the clause header.
        var clause = Assert.Single(function.Descendants.OfType<CatchClause>());
        Assert.NotNull(clause.VariableIndex);
        Assert.Matches(@"catch \(FormatException V_\d+\)", output);
        Assert.DoesNotContain("__exception", output);
        // The clause owns the declaration — nothing declares the local up front.
        Assert.DoesNotMatch(@"FormatException V_\d+;", output);
#else
        // Release consumes the exception inline (callvirt get_Message on the
        // raw stack value, no store) — outside the slice, honestly flat.
        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
#endif
        // ldc.i4.m1 must print -1, not the ushort-wrapped 65535.
        Assert.Contains("-1;", output);
        Assert.DoesNotContain("65535", output);
    }

    [Fact]
    public void Catch_DiscardedException_PrintsBareType()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.CatchDiscards));

        var clause = Assert.Single(function.Descendants.OfType<CatchClause>());
        Assert.Null(clause.VariableIndex);
        Assert.Matches(@"(?m)^catch \(FormatException\)$", output);
    }

    [Fact]
    public void CatchAll_PrintsBareCatch()
    {
        var (_, output) = RaiseFixture(nameof(CfgSampleClass.CatchEverything));

        Assert.Matches(@"(?m)^catch$", output);
    }

    [Fact]
    public void Rethrow_PrintsBareThrow()
    {
        var (_, output) = RaiseFixture(nameof(CfgSampleClass.LogAndRethrow));

        Assert.Contains("throw;", output);
        Assert.DoesNotContain("__exception", output);
    }

    [Fact]
    public void MultiCatch_PreservesClauseOrder()
    {
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.TwoCatches));

        var tryCatch = Assert.Single(function.Descendants.OfType<TryCatch>());
        Assert.Equal(2, tryCatch.Clauses.Count);
        Assert.True(output.IndexOf("FormatException", StringComparison.Ordinal)
            < output.IndexOf("OverflowException", StringComparison.Ordinal));
    }

    [Fact]
    public void TryCatchFinally_TailLeaves_TrimThroughNestedConstructs()
    {
        // The arms of the inner try/catch leave straight past the outer
        // finally; in tail position that is plain fallthrough, so no goto
        // and no label survive.
        var (function, output) = RaiseFixture(nameof(CfgSampleClass.ParseWithCleanup));

        Assert.Single(function.Descendants.OfType<TryFinally>());
        Assert.Single(function.Descendants.OfType<TryCatch>());
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void Filter_StaysFlatWithRegionsIntact()
    {
        var (function, _) = RaiseFixture(nameof(CfgSampleClass.FilteredLength));

        Assert.NotEmpty(function.Regions);
        Assert.Empty(function.Descendants.OfType<TryCatch>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());
    }
}

/// <summary>
/// Constructor-chain rendering: base/this calls print as body statements, the
/// spilled-this receiver (control-flow argument shapes) canonicalizes back to
/// <c>this</c>, and the implicit parameterless base call is suppressed.
/// </summary>
public class ConstructorChainTests
{
    static string RaiseCtor(int overloadIndex)
    {
        using var source = MetadataSource.Open(typeof(CtorChainSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CtorChainSamples).FullName!, ".ctor", overloadIndex);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n").TrimEnd();
    }

    [Fact]
    public void ImplicitParameterlessBase_IsSuppressed()
    {
        // ctor#0: public CtorChainSamples() { } — base() is implicit.
        Assert.Equal("", RaiseCtor(0));
    }

    [Fact]
    public void BaseCall_RendersBaseWithArguments()
    {
        // ctor#1: : base(message)
        Assert.Equal("base(message);", RaiseCtor(1));
    }

    [Fact]
    public void SpilledThis_CoalesceArgument_CanonicalizesToBase()
    {
        // ctor#3: : base(message ?? "default") — the ?? forces a this spill
        // the inliner cannot dissolve; the chain pass renames it to this.
        string output = RaiseCtor(3);

        Assert.Contains("base(", output);
        Assert.DoesNotContain("..ctor", output);   // never the invalid S_0..ctor form
        Assert.DoesNotContain("= this;", output);   // the dead spill is gone
    }

    [Fact]
    public void ThisDelegation_RendersThis()
    {
        // ctor#4: : this(value.ToString())
        string output = RaiseCtor(4);

        Assert.StartsWith("this(", output);
        Assert.DoesNotContain("base(", output);
        Assert.DoesNotContain("..ctor", output);
    }
}

public class IdentityConvertTests
{
    [Fact]
    public void ArrayLengthConversion_IsElided()
    {
        // ldlen yields the int-typed ArrayLength; the trailing conv.i4 is an
        // identity conversion and must not print as a cast.
        var intType = TypeRef.CoreLib("System", "Int32");
        var arrayType = TypeRef.SzArray(intType);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var length = new ArrayLength(new LoadArgument(0, "a", arrayType));
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: false, isUnsigned: false, length)));
        var signature = new MethodSignature(intType, [new Parameter("a", arrayType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return a.Length;", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }

    [Fact]
    public void GenuineNarrowing_IsKept()
    {
        // conv.i4 of a long is a real narrowing — the cast stays.
        var intType = TypeRef.CoreLib("System", "Int32");
        var longType = TypeRef.CoreLib("System", "Int64");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: false, isUnsigned: false, new LoadArgument(0, "x", longType))));
        var signature = new MethodSignature(intType, [new Parameter("x", longType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (int)x;", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }

    [Fact]
    public void CheckedUnsignedConversion_AtEqualType_IsKept()
    {
        // conv.ovf.i4.un of an int is Int32 -> Int32, but it reinterprets the
        // source as unsigned and throws for negative bit patterns — eliding it
        // would drop the overflow check. The cast must survive equal types.
        var intType = TypeRef.CoreLib("System", "Int32");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new ILInspector.Decompiler.Pipeline.Convert(intType, isChecked: true, isUnsigned: true, new LoadArgument(0, "x", intType))));
        var signature = new MethodSignature(intType, [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return checked((int)(uint)x);", CSharpPrinter.PrintRaised(function).Output!.Trim());
    }
}

/// <summary>
/// The lock-sugar pass: the csc Monitor lockTaken lowering raises to a
/// lock (obj) { ... } statement, the synthetic V_object/V_taken locals
/// disappear, and the lock object is the original expression.
/// </summary>
public class LockSugarTests
{
    static string RaiseLock(string methodName)
    {
        using var source = MetadataSource.Open(typeof(LockFixtureSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LockFixtureSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n").TrimEnd();
    }

    [Fact]
    public void VoidLock_RaisesToLockStatement()
    {
        var (function, output) = (IrImportFor(nameof(LockFixtureSamples.IncrementUnderLock)), RaiseLock(nameof(LockFixtureSamples.IncrementUnderLock)));

        Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());            // the try/finally is consumed
        Assert.DoesNotContain("Monitor", output);                          // no Monitor.Enter/Exit left
        Assert.Matches(@"lock \(_root\)", output);
        Assert.DoesNotContain("bool V_", output);                          // the lockTaken local is gone
    }

    [Fact]
    public void LockOnParameter_UsesParameterExpression()
    {
        Assert.Contains("lock (gate)", RaiseLock(nameof(LockFixtureSamples.LockOnParameter)));
    }

    [Fact]
    public void LockBody_IsStillRaised()
    {
        // The body inside the lock continues through later passes.
        string output = RaiseLock(nameof(LockFixtureSamples.ReadUnderLock));
        Assert.Contains("lock (_root)", output);
        Assert.DoesNotContain("Monitor", output);
    }

    static IrFunction IrImportFor(string methodName)
    {
        using var source = MetadataSource.Open(typeof(LockFixtureSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(LockFixtureSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        return function;
    }
}

/// <summary>
/// Soundness of the lock-sugar match, on shapes the C# compiler never emits
/// but hand-written or obfuscated IL could: a lockTaken local read after the
/// try/finally, and a same-named Monitor from a non-BCL assembly. Both must
/// leave the construct flat. Built directly in the post-structuring shape and
/// run through LockSugarPass alone.
/// </summary>
public class LockSugarSoundnessTests
{
    static IrFunction BuildLock(string monitorAssembly, bool strayTakenRef, bool malformedEnterSignature = false)
    {
        var voidType = TypeRef.CoreLib("System", "Void");
        var objType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var monitor = monitorAssembly == TypeRef.CoreLibrary
            ? TypeRef.CoreLib("System.Threading", "Monitor")
            : TypeRef.Definition(monitorAssembly, "System.Threading", "Monitor");
        // A malformed Enter returns object instead of void — same name, type,
        // and argument node shapes, wrong signature.
        var enterReturn = malformedEnterSignature ? objType : voidType;
        var enterRef = new MethodRef(monitor, "Enter", enterReturn, [objType, TypeRef.ByRef(boolType)], HasThis: false);
        var exitRef = new MethodRef(monitor, "Exit", voidType, [objType], HasThis: false);

        var tryBlock = new Block(0);
        tryBlock.Add(new ExpressionStatement(new Call(enterRef, false,
            [new LoadLocal(0, objType), new LoadLocalAddress(1, boolType)])));
        var tryBody = new BlockContainer();
        tryBody.Add(tryBlock);

        var thenBlock = new Block(0);
        thenBlock.Add(new ExpressionStatement(new Call(exitRef, false, [new LoadLocal(0, objType)])));
        var finallyBlock = new Block(0);
        finallyBlock.Add(new IfStatement(new LoadLocal(1, boolType), thenBlock, null));
        var finallyBody = new BlockContainer();
        finallyBody.Add(finallyBlock);

        var entry = new Block(0);
        entry.Add(new StoreLocal(0, objType, new Constant(null, objType)));
        entry.Add(new StoreLocal(1, boolType, new Constant(0, boolType)));
        entry.Add(new TryFinally(tryBody, finallyBody));
        if (strayTakenRef)
            entry.Add(new ExpressionStatement(new LoadLocal(1, boolType)));   // reads V_1 after the lock
        var body = new BlockContainer();
        body.Add(entry);

        var signature = new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [objType, boolType], body);
    }

    [Fact]
    public void CleanShape_Raises()   // positive control: the synthetic shape is well-formed
    {
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false);
        new LockSugarPass().Run(function);

        Assert.Single(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Empty(function.Descendants.OfType<TryFinally>());
        function.CheckInvariant();
    }

    [Fact]
    public void LockTakenReadAfterTryFinally_StaysFlat()
    {
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: true);
        new LockSugarPass().Run(function);

        // Detaching the stores would strand the later read of V_1.
        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void MonitorFromOtherAssembly_StaysFlat()
    {
        var function = BuildLock("SomeUserAssembly", strayTakenRef: false);
        new LockSugarPass().Run(function);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }

    [Fact]
    public void WrongMonitorSignature_StaysFlat()
    {
        // Right name, type, and argument shapes — but Enter returns object,
        // not void. The signature check rejects it.
        var function = BuildLock(TypeRef.CoreLibrary, strayTakenRef: false, malformedEnterSignature: true);
        new LockSugarPass().Run(function);

        Assert.Empty(function.Descendants.OfType<Pipeline.Lock>());
        Assert.Single(function.Descendants.OfType<TryFinally>());
    }
}

/// <summary>
/// The printer's shape-driven truthiness: given a resolved TypeShape for a
/// non-generic definition branch operand, an enum zero-tests and a reference
/// null-tests. Built directly with a TypeShapes map so the rendering is tested
/// independent of csc codegen (enum brfalse is Release-only).
/// </summary>
public class TypeShapeTruthinessTests
{
    static string PrintConditionOn(TypeRef conditionType, TypeShape shape)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var then = new Block(0);
        then.Add(new Return(new Constant(1, intType)));
        var entry = new Block(0);
        entry.Add(new IfStatement(new LoadLocal(0, conditionType), then, null));
        entry.Add(new Return(new Constant(0, intType)));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [conditionType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [conditionType] = shape },
        };
        return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
    }

    [Fact]
    public void EnumShape_ZeroTests()
    {
        var enumType = TypeRef.Definition("asm", "NS", "MyEnum");
        Assert.Contains("if (V_0 != 0)", PrintConditionOn(enumType, TypeShape.Enum));
    }

    [Fact]
    public void ReferenceShape_NullTests()
    {
        var classType = TypeRef.Definition("asm", "NS", "MyClass");
        Assert.Contains("if (V_0 is not null)", PrintConditionOn(classType, TypeShape.Reference));
    }

    [Fact]
    public void UnknownShape_StaysRaw()
    {
        // A cross-assembly definition resolves to Unknown — print raw, no guess.
        var classType = TypeRef.Definition("other-asm", "NS", "Mystery");
        string output = PrintConditionOn(classType, TypeShape.Unknown);

        Assert.DoesNotContain("is null", output);
        Assert.DoesNotContain("!= 0", output);
        Assert.Contains("V_0", output);   // still references the operand, raw
    }
}

/// <summary>
/// Enum-constant naming: an integer flowing into an enum position retypes to
/// the enum and prints as EnumType.Member from the resolved same-assembly
/// member map. Exact matches only; composite/unnamed values stay raw.
/// </summary>
public class EnumConstantTests
{
    [Fact]
    public void EnumArgument_RendersMemberName()
    {
        // TakesPriority(CfgPriority.High) — the ldc.i4.2 names as High, not 2.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CallWithHighPriority), source);

        Assert.Contains("CfgPriority.High", output);
        Assert.DoesNotContain("TakesPriority(2)", output);
    }

    [Fact]
    public void HighBitUnsignedEnumMember_Names()
    {
        // CfgFlags.Top = 0x80000000 (uint) emits as int -2147483648. The
        // member-map key must reinterpret the uint as a signed int to match,
        // or this falls back to the raw -2147483648.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CallWithTopFlag), source);

        Assert.Contains("CfgFlags.Top", output);
        Assert.DoesNotContain("-2147483648", output);
    }

    [Fact]
    public void ExactMember_Names_UnmatchedValue_StaysRaw()
    {
        var enumType = TypeRef.Definition("asm", "NS", "Color");
        var members = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
        {
            [enumType] = new Dictionary<long, string> { [1] = "Red", [2] = "Green" },
        };

        Assert.Equal("Color.Red", PrintEnumConstant(1, enumType, members));
        Assert.Equal("Color.Green", PrintEnumConstant(2, enumType, members));
        // 3 names no member (would be a composite/cast) — raw, never guessed.
        Assert.Equal("3", PrintEnumConstant(3, enumType, members));
    }

    [Fact]
    public void EnumWithNoResolvedMembers_StaysRaw()
    {
        // A cross-assembly enum is absent from the map → raw integer.
        var enumType = TypeRef.Definition("other", "NS", "Mystery");
        Assert.Equal("5", PrintEnumConstant(5, enumType, new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>()));
    }

    static string PrintEnumConstant(int value, TypeRef enumType, IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>> members)
    {
        var block = new Block(0);
        block.Add(new Return(new Constant(value, enumType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container)
        {
            EnumMembers = members,
        };
        // Strip the leading "return " and trailing ";" to get the operand text.
        string output = CSharpPrinter.Print(function).Output!.Trim();
        return output["return ".Length..].TrimEnd(';');
    }

    sealed class RaisingPassTestsAccessor
    {
        public string Print(string typeName, string methodName, MetadataSource source)
        {
            var function = IrImporter.Import(source, typeName, methodName);
            Assert.NotNull(function);
            IrPasses.Run(function);
            return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        }
    }
}

/// <summary>
/// String and char literal escaping: control characters, quotes, and
/// backslashes are escaped so the rendered literal always compiles. A raw
/// newline or tab in the output would be invalid C#.
/// </summary>
public class StringEscapingTests
{
    static string PrintStringConstant(string value)
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var block = new Block(0);
        block.Add(new Return(new Constant(value, stringType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(stringType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    [Fact]
    public void ControlCharacters_QuotesAndBackslashes_AreEscaped()
    {
        Assert.Equal("return \"a\\nb\";", PrintStringConstant("a\nb"));
        Assert.Equal("return \"\\t\\r\\0\";", PrintStringConstant("\t\r\0"));
        Assert.Equal("return \"say \\\"hi\\\"\";", PrintStringConstant("say \"hi\""));
        Assert.Equal("return \"c:\\\\tmp\";", PrintStringConstant("c:\\tmp"));
    }

    [Fact]
    public void OtherControlChar_UsesUnicodeEscape()
    {
        //  has no recognized short escape.
        Assert.Equal("return \"x\\u0001y\";", PrintStringConstant("xy"));
    }
}
