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
    public void ExceptionRegions_StopHonestly_WithUnsupportedNode()
    {
        var function = ImportFixture(nameof(CfgSampleClass.ChecksThenTry));

        var unsupported = Assert.Single(function.Descendants.OfType<UnsupportedNode>());
        Assert.Contains("exception regions", unsupported.Reason);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        var diagnostic = Assert.Single(function.Diagnostics);
        Assert.Equal(DiagnosticIds.UnsupportedConstruct, diagnostic.Id);
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
    public void JoinTypeConflict_BeforeJoinIsBuilt_MergesToHonestUnknown()
    {
        // ldc.i4.1; brtrue.s L1; ldc.i4.0; br.s J; L1: ldnull; J: pop; ret
        // Two forward edges carry int and object into J: pre-build conflict
        // merges to null (honest unknown), never a guessed type.
        var function = BuildSynthetic([0x17, 0x2D, 0x03, 0x16, 0x2B, 0x01, 0x14, 0x26, 0x2A]);

        Assert.Empty(function.Diagnostics);
        var load = Assert.Single(function.Descendants.OfType<LoadStackSlot>(),
            l => l.Parent is ExpressionStatement);
        Assert.Null(load.Type);
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
