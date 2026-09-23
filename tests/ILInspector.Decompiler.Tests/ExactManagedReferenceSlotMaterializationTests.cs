using System.Collections.Immutable;
using System.Security.Cryptography;
using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ExactManagedReferenceSlotMaterializationTests
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef RefInt32 = TypeRef.ByRef(Int32);

    [Fact]
    public void ExactManagedReferenceMaterializesAsRefLocal()
    {
        var value = new LoadLocalAddress(0, Int32);
        var function = Function(
            RefInt32,
            [Int32],
            new StoreStackSlot(0, value),
            new Return(new LoadStackSlot(0, RefInt32)));
        var invariant = SlotMaterializationInvariant.Capture(function);

        Assert.False(CoercionDomain.InDomain(RefInt32, function.TypeShapes));
        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());

        new SlotMaterializationPass().Run(function, PassContext.None);
        invariant.Check();

        Assert.Equal([Int32, RefInt32], function.Locals);
        Assert.Same(value, Assert.Single(
            function.Descendants.OfType<StoreLocal>(),
            store => store.Index == 1).Value);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("ref int S_0 = ref V_0;", output);
        Assert.Contains("return ref S_0;", output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void CrossBlockRebindingKeepsUpFrontRefLocal()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(
            new LoadArgument(0, "choose", Boolean),
            4));
        entry.Add(new Branch(8));
        var first = new Block(4);
        first.Add(new StoreStackSlot(0, new LoadLocalAddress(0, Int32)));
        first.Add(new Branch(12));
        var second = new Block(8);
        second.Add(new StoreStackSlot(0, new LoadLocalAddress(1, Int32)));
        second.Add(new Branch(12));
        var join = new Block(12);
        join.Add(new Return(new LoadStackSlot(0, RefInt32)));
        foreach (var block in (Block[])[entry, first, second, join])
            body.Add(block);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                RefInt32,
                [new Parameter("choose", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32, Int32],
            body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
        new SlotMaterializationPass().Run(function, PassContext.None);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains(
            "ref int S_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();",
            output);
        Assert.Contains("S_0 = ref V_0;", output);
        Assert.Contains("S_0 = ref V_1;", output);
        Assert.Contains("return ref S_0;", output);
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void UpFrontRefLocalKeepsItsStackSlotDeclarationOrder()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new ExpressionStatement(
            new LoadStackSlot(0, Int32)));
        entry.Add(new ConditionalBranch(
            new LoadArgument(0, "choose", Boolean),
            4));
        entry.Add(new Branch(8));
        var first = new Block(4);
        first.Add(new StoreStackSlot(
            1,
            new LoadLocalAddress(0, Int32)));
        first.Add(new Branch(12));
        var second = new Block(8);
        second.Add(new StoreStackSlot(
            1,
            new LoadLocalAddress(1, Int32)));
        second.Add(new Branch(12));
        var join = new Block(12);
        join.Add(new Return(
            new LoadStackSlot(1, RefInt32)));
        body.Add(entry);
        body.Add(first);
        body.Add(second);
        body.Add(join);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                RefInt32,
                [new Parameter("choose", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32, Int32],
            body);

        new SlotMaterializationPass().Run(function, PassContext.None);
        string output = CSharpPrinter.Print(function).Output!;

        int residualDeclaration = output.IndexOf(
            "int S_0;",
            StringComparison.Ordinal);
        int materializedDeclaration = output.IndexOf(
            "ref int S_1 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();",
            StringComparison.Ordinal);
        Assert.True(residualDeclaration >= 0);
        Assert.True(
            materializedDeclaration > residualDeclaration,
            output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void RefLocalReceiverDoesNotEnableNewCompoundAssignment()
    {
        var holder = TypeRef.Definition(
            "Samples",
            "Samples",
            "Holder");
        var refHolder = TypeRef.ByRef(holder);
        var field = new FieldRef(holder, "Flags", Int32);
        var receiver = new LoadStackSlot(0, refHolder);
        var store = new StoreField(
            field,
            receiver,
            new Binary(
                BinaryKind.Or,
                isChecked: false,
                isUnsigned: false,
                new LoadField(
                    field,
                    new LoadStackSlot(0, refHolder)),
                new Constant(1, Int32)));
        var function = Function(
            TypeRef.CoreLib("System", "Void"),
            [holder],
            new StoreStackSlot(
                0,
                new LoadLocalAddress(0, holder)),
            store,
            new Return(null));

        new SlotMaterializationPass().Run(function, PassContext.None);
        new ScalarSelfUpdatePass().Run(
            function,
            PassContext.None);

        Assert.Null(store.UpdateKind);
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains(
            "S_0.Flags = S_0.Flags | 1;",
            output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagedReferenceCopyComponentsRemainAtomic(bool incomplete)
    {
        var body = new BlockContainer();
        var block = new Block(0);
        block.Add(new StoreStackSlot(0, new LoadLocalAddress(0, Int32)));
        block.Add(new StoreStackSlot(1, new LoadStackSlot(0, RefInt32)));
        if (incomplete)
            block.Add(new ExpressionStatement(new LoadStackSlot(1, type: null)));
        else
            block.Add(new Return(new LoadStackSlot(1, RefInt32)));
        body.Add(block);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                incomplete ? TypeRef.CoreLib("System", "Void") : RefInt32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32],
            body);
        var invariant = SlotMaterializationInvariant.Capture(function);
        var decisions = SlotMaterializationPass.Analyze(function);

        Assert.Equal(2, decisions.Count);
        if (incomplete)
        {
            Assert.All(decisions, decision => Assert.True(
                decision.Vetoes.HasFlag(
                    SlotMaterializationVeto.IncompleteCopyComponent)));
            new SlotMaterializationPass().Run(function, PassContext.None);
            Assert.Equal([Int32], function.Locals);
            Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
        }
        else
        {
            Assert.All(decisions, decision =>
                Assert.True(decision.WillMaterialize, decision.Vetoes.ToString()));
            new SlotMaterializationPass().Run(function, PassContext.None);
            Assert.Equal([Int32, RefInt32, RefInt32], function.Locals);
            Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
            Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
            function.CheckInvariant(includeSemantics: true);
        }
        invariant.Check();
    }

    [Theory]
    [InlineData("nested-byref")]
    [InlineData("pinned-element")]
    [InlineData("unsupported-element")]
    public void MalformedAndUnsupportedManagedReferencesRemainDeferred(
        string shape)
    {
        var type = shape switch
        {
            "nested-byref" => TypeRef.ByRef(RefInt32),
            "pinned-element" => TypeRef.ByRef(TypeRef.Pinned(Int32)),
            "unsupported-element" => TypeRef.ByRef(
                TypeRef.Unsupported("synthetic unsupported element")),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var function = Function(
            type,
            [],
            new StoreStackSlot(0, RefProducer(type)),
            new Return(new LoadStackSlot(0, type)));

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.Vetoes.HasFlag(
            SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetainsSlots(function);
        Assert.False(CSharpPrinter.Print(function).Succeeded);
    }

    [Fact]
    public void CompilerGeneratedNameDoesNotVetoExactManagedReferenceStorage()
    {
        var generated = TypeRef.Definition(
            "Samples",
            "Samples",
            "<Value>j__TPar");
        var type = TypeRef.ByRef(generated);
        var function = Function(
            type,
            [],
            new StoreStackSlot(0, RefProducer(type)),
            new Return(new LoadStackSlot(0, type)));

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Equal(type, Assert.Single(function.Locals));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        Assert.Contains(
            "ref __Value_j__TPar S_0 = ref GetRef();",
            result.Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void EveryProducerMustHaveTheExactManagedReferenceType()
    {
        var function = Function(
            RefInt32,
            [Int32, Byte],
            new StoreStackSlot(0, new LoadLocalAddress(0, Int32)),
            new StoreStackSlot(0, new LoadLocalAddress(1, Byte)),
            new Return(new LoadStackSlot(0, RefInt32)));

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.Vetoes.HasFlag(
            SlotMaterializationVeto.OutsideCoercionDomain));
        AssertRetainsSlots(function);
    }

    [Fact]
    public void StoreOnlyManagedReferenceUsesUnanimousProducerTestimony()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new Branch(4));
        var storeBlock = new Block(4);
        storeBlock.Add(new StoreStackSlot(
            0,
            new LoadLocalAddress(0, Int32)));
        storeBlock.Add(new Return(null));
        body.Add(entry);
        body.Add(storeBlock);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32],
            body);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(RefInt32, decision.Type);
        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        Assert.Contains(
            "ref int S_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();",
            result.Output);
        Assert.Contains("S_0 = ref V_0;", result.Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void ProducerOnlyEntryStoreRetainsInlineDeclaration()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new StoreStackSlot(
            0,
            new LoadLocalAddress(0, Int32)));
        entry.Add(new Branch(8));
        var exit = new Block(4);
        exit.Add(new Return(null));
        var diagnosed = new Block(8);
        diagnosed.Add(new ExpressionStatement(
            new Call(
                new MethodRef(
                    Int32,
                    ".ctor",
                    TypeRef.CoreLib("System", "Void"),
                    [],
                    HasThis: true),
                isVirtual: false,
                [new LoadStackSlot(0, RefInt32)])));
        diagnosed.Add(new Branch(4));
        body.Add(entry);
        body.Add(exit);
        body.Add(diagnosed);
        var function = new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32],
            body);

        new ConstructorCallDiagnosticsPass().Run(
            function,
            PassContext.None);
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        var decision = Assert.Single(
            SlotMaterializationPass.Analyze(function));
        Assert.True(decision.WillMaterialize, decision.Vetoes.ToString());
        new SlotMaterializationPass().Run(
            function,
            PassContext.None);

        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        Assert.Contains("ref int S_0 = ref V_0;", result.Output);
        Assert.DoesNotContain("Unsafe.NullRef", result.Output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void StoreOnlyManagedReferenceRequiresUnanimousProducers()
    {
        var function = Function(
            TypeRef.CoreLib("System", "Void"),
            [Int32, Byte],
            new StoreStackSlot(0, new LoadLocalAddress(0, Int32)),
            new StoreStackSlot(0, new LoadLocalAddress(1, Byte)),
            new Return(null));

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Null(decision.Type);
        Assert.Equal(
            SlotMaterializationVeto.MissingLoad,
            decision.Vetoes);
        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Equal(2, function.Descendants.OfType<StoreStackSlot>().Count());
        Assert.False(CSharpPrinter.Print(function).Succeeded);
        function.CheckInvariant();
    }

    [Fact]
    public void LoadOnlyManagedReferenceFailsVisiblyAtPrinterBoundary()
    {
        var function = Function(
            RefInt32,
            [Int32],
            new Return(new LoadStackSlot(0, RefInt32)));

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.True(decision.Vetoes.HasFlag(
            SlotMaterializationVeto.MissingStore));
        new SlotMaterializationPass().Run(function, PassContext.None);

        var result = CSharpPrinter.Print(function);
        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticIds.InternalError, diagnostic.Id);
        Assert.Equal(
            "InvalidOperationException: Managed-reference stack slot 0 reached C# emission after slot materialization.",
            diagnostic.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedRoslynHandlerReferenceMaterializesWithoutOutputChange(
        bool lowered)
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
            "Microsoft.CodeAnalysis.Operation",
            "GetDebuggerDisplay");
        Assert.NotNull(function);
        var context = PassContext.ForImport(
            reference => IrImporter.Import(source, reference),
            source.AreProvablyDisjoint);
        var passes = lowered ? IrPasses.Lowered : IrPasses.Default;
        int materializationIndex = passes
            .Select((pass, index) => (pass, index))
            .Single(item => item.pass is SlotMaterializationPass)
            .index;
        foreach (var pass in passes.Take(materializationIndex))
            pass.Run(function, context);

        var decisions = SlotMaterializationPass.Analyze(function);
        var managedReference = Assert.Single(
            decisions,
            decision => decision.Type?.Kind == TypeRefKind.ByRef);
        Assert.True(
            managedReference.WillMaterialize,
            managedReference.Vetoes.ToString());
        var neighboringResidual = Assert.Single(
            decisions,
            decision => decision.Slot == 1);
        Assert.Equal(
            SlotMaterializationVeto.UnderivableTypeTestimony,
            neighboringResidual.Vetoes);

        foreach (var pass in passes.Skip(materializationIndex))
            pass.Run(function, context);

        Assert.DoesNotContain(
            function.DescendantsOutsideNestedFunctions,
            node => node is StoreStackSlot
                {
                    Value.ResultType.Kind: TypeRefKind.ByRef,
                }
                or LoadStackSlot
                {
                    Type.Kind: TypeRefKind.ByRef,
                });
        Assert.Contains(
            function.DescendantsOutsideNestedFunctions,
            node => node is StoreStackSlot { Slot: 1 }
                or LoadStackSlot { Slot: 1 });
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains(
            "ref DefaultInterpolatedStringHandler S_0 = ref V_0;",
            output);
        Assert.Contains(
            "ITypeSymbol S_1 = Type is not null ? Type : \"null\";",
            output);
        Assert.Contains("S_0.AppendFormatted(S_1, 0, null);", output);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void NestedLocalFunctionKeepsMaterializedSlotProvenance()
    {
        var nested = NestedRefFunction();
        var nestedBody = nested.Body;
        nestedBody.Detach();
        var localFunction = new LocalFunctionStatement(
            "Nested",
            RefInt32,
            nested.Signature.Parameters,
            isStatic: true,
            nested.Locals,
            nested.LocalNames,
            nested.UsesUpdatedMemorySafetyRules,
            nested.SkipLocalsInit,
            nestedBody)
        {
            SynthesizedLocalNames =
                nested.SynthesizedLocalNames,
            MaterializedStackSlotLocals =
                nested.MaterializedStackSlotLocals,
        };
        var function = Function(
            TypeRef.CoreLib("System", "Void"),
            [],
            localFunction,
            new Return(null));

        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        Assert.Contains(
            "ref int S_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();",
            result.Output);
        Assert.Contains("S_0 = ref V_0;", result.Output);
        Assert.Contains("S_0 = ref V_1;", result.Output);
    }

    [Fact]
    public void NestedLambdaKeepsMaterializedSlotProvenance()
    {
        var nested = NestedRefFunction();
        var nestedBody = nested.Body;
        nestedBody.Detach();
        var delegateType = TypeRef.Definition(
            "Samples",
            "Samples",
            "RefSelector");
        var lambda = new Lambda(
            delegateType,
            nested.Signature.Parameters,
            nested.Locals,
            nested.LocalNames,
            nested.UsesUpdatedMemorySafetyRules,
            nested.SkipLocalsInit,
            nestedBody)
        {
            SynthesizedLocalNames =
                nested.SynthesizedLocalNames,
            MaterializedStackSlotLocals =
                nested.MaterializedStackSlotLocals,
        };
        var function = Function(
            delegateType,
            [],
            new Return(lambda));

        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        Assert.Contains(
            "ref int S_0 = ref System.Runtime.CompilerServices.Unsafe.NullRef<int>();",
            result.Output);
        Assert.Contains("S_0 = ref V_0;", result.Output);
        Assert.Contains("S_0 = ref V_1;", result.Output);
    }

    static IrFunction NestedRefFunction()
    {
        var body = new BlockContainer();
        var entry = new Block(0);
        entry.Add(new StoreStackSlot(
            0,
            new LoadLocalAddress(0, Int32)));
        entry.Add(new ConditionalBranch(
            new LoadArgument(0, "choose", Boolean),
            4));
        entry.Add(new Branch(8));
        var rebind = new Block(4);
        rebind.Add(new StoreStackSlot(
            0,
            new LoadLocalAddress(1, Int32)));
        rebind.Add(new Branch(8));
        var join = new Block(8);
        join.Add(new Return(
            new LoadStackSlot(0, RefInt32)));
        body.Add(entry);
        body.Add(rebind);
        body.Add(join);
        var function = new IrFunction(
            "Nested",
            Owner,
            new MethodSignature(
                RefInt32,
                [new Parameter("choose", Boolean)],
                HasThis: false,
                GenericParameterCount: 0),
            [Int32, Int32],
            body);
        new SlotMaterializationPass().Run(
            function,
            PassContext.None);
        return function;
    }

    static IrFunction Function(
        TypeRef returnType,
        ImmutableArray<TypeRef> locals,
        params IrNode[] statements)
    {
        var block = new Block(0);
        foreach (var statement in statements)
            block.Add(statement);
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(
                returnType,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            locals,
            body);
    }

    static IrExpression RefProducer(TypeRef type)
        => new Call(
            new MethodRef(
                Owner,
                "GetRef",
                type,
                [],
                HasThis: false),
            isVirtual: false,
            []);

    static void AssertRetainsSlots(IrFunction function)
    {
        var nodes = function.Descendants
            .Where(node => node is StoreStackSlot or LoadStackSlot)
            .ToArray();
        int localCount = function.Locals.Length;

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Equal(localCount, function.Locals.Length);
        Assert.All(nodes, node => Assert.Contains(node, function.Descendants));
        function.CheckInvariant();
    }
}
