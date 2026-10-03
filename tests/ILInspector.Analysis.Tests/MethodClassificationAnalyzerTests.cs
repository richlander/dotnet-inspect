using ILInspector.Metadata.LegacyOracles;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Classification;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Release gates for the method classification analyzers
/// (docs/design/method-classification-analyzers.md). All PR-fast: fixtures
/// are built in memory.
/// </summary>
public sealed class MethodClassificationAnalyzerTests
{
    const MethodImplAttributes RuntimeAsync = (MethodImplAttributes)0x2000;
    const MethodAttributes PublicStatic = MethodAttributes.Public | MethodAttributes.Static;

    [Fact]
    public void Analyzers_AsyncAndPointerMethodHasTwoRowsAndPInvokeHasOne()
    {
        GateFixtureImage builder = new();
        builder.Type("N", "Mixed")
            .Method("AsyncPointer", PointerParameter(), implAttributes: RuntimeAsync)
            .Method("Imported", PointerParameter(), PublicStatic | MethodAttributes.PinvokeImpl, MethodImplAttributes.PreserveSig)
            .Method("get_Accessor", PointerParameter())
            .Method("Hidden", PointerParameter(), MethodAttributes.Private | MethodAttributes.Static);
        builder.Type("N", "<Generated>").Method("Skipped", PointerParameter());
        ImmutableArray<byte> image = builder.Build();

        (ImmutableArray<ClassifiedMethodRow> pinvoke, ImmutableArray<ClassifiedMethodRow> async, ImmutableArray<ClassifiedMethodRow> pointer) =
            RowsOfAll(image);

        Assert.Equal(["Imported"], pinvoke.Select(static row => row.MethodName.ToString()));
        Assert.Equal(["AsyncPointer"], async.Select(static row => row.MethodName.ToString()));
        Assert.Equal(MethodClassification.RuntimeAsync, Assert.Single(async).Classification);
        Assert.Equal(["AsyncPointer"], pointer.Select(static row => row.MethodName.ToString()));
        Assert.Equal("native.dll", Assert.Single(pinvoke).ModuleName?.ToString());

        using var peReader = new PEReader(image);
        List<ClassifiedMethodInfo> legacy = LegacyMethodClassificationScanner.Scan(peReader);
        Assert.Equal(
            legacy.Select(static row => (row.MethodName, row.Classification)),
            pinvoke.Concat(async).Concat(pointer)
                .OrderBy(static row => (uint)row.Token)
                .ThenBy(static row => row.Classification switch
                {
                    MethodClassification.PInvoke => 0,
                    MethodClassification.Unsafe => 2,
                    _ => 1,
                })
                .Select(static row => (row.MethodName.ToString(), row.Classification)));
    }

    [Fact]
    public void Async_RuntimeAndCompilerAsyncAreDisjointAndEqualLegacy()
    {
        GateFixtureImage builder = new();
        TypeReferenceHandle asyncAttribute = builder.TypeRef(
            "System.Runtime.CompilerServices", "AsyncStateMachineAttribute");
        TypeReferenceHandle iteratorAttribute = builder.TypeRef(
            "System.Runtime.CompilerServices", "AsyncIteratorStateMachineAttribute");
        TypeReferenceHandle otherAttribute = builder.TypeRef(
            "System.Runtime.CompilerServices", "IteratorStateMachineAttribute");
        builder.Type("N", "Mixed")
            .Method("Runtime", implAttributes: RuntimeAsync)
            .Method("Both", implAttributes: RuntimeAsync, attributeConstructors: [builder.AttributeConstructor(asyncAttribute)])
            .Method("Compiler", attributeConstructors: [builder.AttributeConstructor(asyncAttribute)])
            .Method("Iterator", attributeConstructors: [builder.AttributeConstructor(iteratorAttribute)])
            .Method("SyncIterator", attributeConstructors: [builder.AttributeConstructor(otherAttribute)])
            .Method("Plain");
        ImmutableArray<byte> image = builder.Build();

        ImmutableArray<ClassifiedMethodRow> runtime = Rows(image, RuntimeAsyncAnalyzer.Instance);
        ImmutableArray<ClassifiedMethodRow> compiler = Rows(image, CompilerAsyncAnalyzer.Instance);

        Assert.Equal(["Runtime", "Both"], runtime.Select(static row => row.MethodName.ToString()));
        Assert.All(runtime, static row => Assert.Equal(MethodClassification.RuntimeAsync, row.Classification));
        Assert.Equal(["Compiler", "Iterator"], compiler.Select(static row => row.MethodName.ToString()));
        Assert.All(compiler, static row => Assert.Equal(MethodClassification.StateMachineAsync, row.Classification));
        Assert.Empty(runtime.Select(static row => row.Token).Intersect(compiler.Select(static row => row.Token)));

        using var peReader = new PEReader(image);
        Assert.Equal(
            LegacyMethodClassificationScanner.Scan(peReader)
                .Where(static row => row.Classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync)
                .Select(static row => (row.MethodName, row.Classification)),
            runtime.Concat(compiler)
                .OrderBy(static row => row.Ordinal)
                .Select(static row => (row.MethodName.ToString(), row.Classification)));
    }

    [Fact]
    public void Async_OnePassEqualsTheUnionAndSkipsTheAttributeTestForRuntimeAsync()
    {
        GateFixtureImage builder = new();
        TypeReferenceHandle asyncAttribute = builder.TypeRef(
            "System.Runtime.CompilerServices", "AsyncStateMachineAttribute");
        // An attribute type nested beyond the chain bound: matching it aborts.
        TypeReferenceHandle deep = builder.TypeRef("System.Runtime", "CompilerServices");
        for (int i = 0; i < MetadataSafetyPolicy.MaxRelationshipNodes; i++)
            deep = builder.TypeRef("", "AsyncStateMachineAttribute", deep);
        builder.Type("N", "Mixed")
            .Method("RuntimeWithHostileAttribute", implAttributes: RuntimeAsync, attributeConstructors: [builder.AttributeConstructor(deep)])
            .Method("Compiler", attributeConstructors: [builder.AttributeConstructor(asyncAttribute)])
            .Method("Plain");
        ImmutableArray<byte> image = builder.Build();

        ImmutableArray<ClassifiedMethodRow> async = Rows(image, AsyncAnalyzer.Instance);
        Assert.Equal(
            [("RuntimeWithHostileAttribute", MethodClassification.RuntimeAsync), ("Compiler", MethodClassification.StateMachineAsync)],
            async.Select(static row => (row.MethodName.ToString(), row.Classification)));
        Assert.Equal(
            2,
            Execute(image, new ProducerRequest(AsyncAnalyzer.Instance)).ResultOf(AsyncAnalyzer.Instance).Value!.Count);
        Assert.Equal(
            MethodDefinitionLayers.Flags | MethodDefinitionLayers.AttributeTypeMatch | MethodDefinitionLayers.Declaration,
            AsyncAnalyzer.Instance.Layers & ~MethodDefinitionLayers.IdentityText);

        // Neither the one pass nor compiler async alone reads the runtime-async
        // method's attribute: reading it would abort.
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>> compiler =
            Execute(image, new ProducerRequest(CompilerAsyncAnalyzer.Instance)).ResultOf(CompilerAsyncAnalyzer.Instance);
        Assert.Equal(1, compiler.Value!.Count);
    }

    [Theory]
    [InlineData(ProducerTerminal.Count)]
    [InlineData(ProducerTerminal.Exists)]
    [InlineData(ProducerTerminal.Rows)]
    public void Async_CompilerAsyncDeclaresNoRelationshipAndCountEqualsRows(ProducerTerminal terminal)
    {
        GateFixtureImage builder = new();
        TypeReferenceHandle asyncAttribute = builder.TypeRef(
            "System.Runtime.CompilerServices", "AsyncStateMachineAttribute");
        builder.Type("N", "T")
            .Method("A", attributeConstructors: [builder.AttributeConstructor(asyncAttribute)])
            .Method("B", attributeConstructors: [builder.AttributeConstructor(asyncAttribute)])
            .Method("C");
        ImmutableArray<byte> image = builder.Build();

        MethodDefinitionExecution execution = Execute(image, new ProducerRequest(CompilerAsyncAnalyzer.Instance, terminal));
        ClosedQueryResult<ClassifiedMethodRow> value = execution.ResultOf(CompilerAsyncAnalyzer.Instance).Value!;

        Assert.Equal(
            MethodDefinitionLayers.Flags | MethodDefinitionLayers.AttributeTypeMatch | MethodDefinitionLayers.Declaration,
            CompilerAsyncAnalyzer.Instance.Layers & ~MethodDefinitionLayers.IdentityText);
        switch (terminal)
        {
            case ProducerTerminal.Rows:
                Assert.Equal(2, value.Rows.Length);
                break;
            case ProducerTerminal.Count:
                Assert.Equal(2, value.Count);
                Assert.False(execution.Receipt.IdentityBudgetArmed);
                break;
            default:
                Assert.True(value.Exists);
                break;
        }
    }

    [Fact]
    public void Analyzers_IdentityProjectionFailureKeepsTheRow()
    {
        // One public P/Invoke method whose identity cannot be decoded: its
        // parameter names a TypeRef row that does not exist. As in legacy,
        // the row is kept with the fallback signature and no anchor.
        GateFixtureImage builder = new();
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x12); // CLASS
        signature.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(MetadataTokens.TypeReferenceHandle(99)));

        builder.Type("N", "Hostile").Method("M", signature, PublicStatic | MethodAttributes.PinvokeImpl, MethodImplAttributes.PreserveSig);
        ImmutableArray<byte> image = builder.Build();

        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>> result =
            Execute(image, new ProducerRequest(PInvokeAnalyzer.Instance, ProducerTerminal.Rows)).ResultOf(PInvokeAnalyzer.Instance);
        Assert.True(result.HasValue, $"{result.Outcome} {result.Failure} {result.Critical}");
        ClassifiedMethodRow row = Assert.Single(result.Value!.Rows);

        using var peReader = new PEReader(image);
        ClassifiedMethodInfo legacy = Assert.Single(LegacyMethodClassificationScanner.Scan(peReader));
        Assert.Equal(legacy.Signature, row.Signature.ToString());
        Assert.Equal("M(...)", row.Signature.ToString());
        Assert.Null(row.Anchor);
        Assert.Null(legacy.Anchor);
    }

    [Theory]
    [InlineData(ProducerTerminal.Count)]
    [InlineData(ProducerTerminal.Exists)]
    public void Analyzers_MalformedPointerSignatureFailsNamingTheMethod(ProducerTerminal terminal)
    {
        GateFixtureImage builder = new();
        builder.Type("N", "Bad")
            .Method("Fine", PointerParameter())
            .Method("Truncated", Bytes(0x00, 0x01, 0x01));
        ImmutableArray<byte> image = builder.Build();

        MethodDefinitionExecution execution = Execute(image, new ProducerRequest(PointerSignatureAnalyzer.Instance, terminal));
        ProducerResult<ClosedQueryResult<ClassifiedMethodRow>> result = execution.ResultOf(PointerSignatureAnalyzer.Instance);

        if (terminal == ProducerTerminal.Exists)
        {
            // Exists settles at "Fine" before reaching the malformed row.
            Assert.Equal(ProducerOutcome.Stopped, result.Outcome);
            return;
        }

        Assert.Equal(ProducerOutcome.Failed, result.Outcome);
        Assert.StartsWith("MethodDef 0x", result.Failure!.Unit);
    }

    [Fact]
    public void Analyzers_CountAndExistsDeclareNoIdentityText()
    {
        GateFixtureImage builder = new();
        builder.Type("N", "Plain")
            .Method("A", PointerParameter())
            .Method("B", PointerParameter(), implAttributes: RuntimeAsync);
        ImmutableArray<byte> image = builder.Build();

        foreach (ProducerTerminal terminal in new[] { ProducerTerminal.Count, ProducerTerminal.Exists })
        {
            MethodDefinitionExecution execution = Execute(
                image,
                new ProducerRequest(PInvokeAnalyzer.Instance, terminal),
                new ProducerRequest(RuntimeAsyncAnalyzer.Instance, terminal),
                new ProducerRequest(PointerSignatureAnalyzer.Instance, terminal));

            Assert.False(execution.Receipt.IdentityBudgetArmed);
            Assert.Equal(0, execution.Receipt.IdentityWorkCharged);
            Assert.False(execution.ResultOf(PointerSignatureAnalyzer.Instance).Value!.HasRows);
        }

        MethodDefinitionExecution counted = Execute(image, new ProducerRequest(PointerSignatureAnalyzer.Instance));
        Assert.Equal(2, counted.ResultOf(PointerSignatureAnalyzer.Instance).Value!.Count);
    }

    [Fact]
    public void Analyzers_RowsAndCountForOneAnalyzerExecuteIndependently()
    {
        GateFixtureImage builder = new();
        builder.Type("N", "Plain")
            .Method("A", PointerParameter(), implAttributes: RuntimeAsync)
            .Method("B", implAttributes: RuntimeAsync)
            .Method("C");
        ImmutableArray<byte> image = builder.Build();

        // Each closing is its own request in its own work description:
        // nothing derives Count from Rows.
        MethodDefinitionExecution rows = Execute(image, new ProducerRequest(RuntimeAsyncAnalyzer.Instance, ProducerTerminal.Rows));
        MethodDefinitionExecution count = Execute(image, new ProducerRequest(RuntimeAsyncAnalyzer.Instance, ProducerTerminal.Count));

        ClosedQueryResult<ClassifiedMethodRow> listed = rows.ResultOf(RuntimeAsyncAnalyzer.Instance).Value!;
        ClosedQueryResult<ClassifiedMethodRow> counted = count.ResultOf(RuntimeAsyncAnalyzer.Instance).Value!;
        Assert.Equal(2, listed.Rows.Length);
        Assert.Equal(listed.Rows.Length, counted.Count);
        Assert.False(counted.HasRows);
        Assert.True(rows.Receipt.IdentityBudgetArmed);
        Assert.False(count.Receipt.IdentityBudgetArmed);
        Assert.Equal(0, count.Receipt.IdentityWorkCharged);
    }

    [Theory]
    [InlineData(ProducerTerminal.Exists)]
    [InlineData(ProducerTerminal.Count)]
    [InlineData(ProducerTerminal.Rows)]
    public void GateCache_TypedKernelTestsInlineAndInterpretedPassResolvesOnce(ProducerTerminal terminal)
    {
        GateFixtureImage builder = new();
        builder.Type("N", "First").Method("A").Method("B", PointerParameter()).Method("C");
        builder.Type("N", "Second").Method("D", PointerParameter()).Method("get_E").Method("F");
        builder.Type("N", "<Generated>").Method("G", PointerParameter());
        ImmutableArray<byte> image = builder.Build();

        // Alone, the analyzer's pass runs as its kernel, specialized to the
        // scope's struct classification: it tests the gate inline and never
        // looks up the classifier's cache.
        MethodDefinitionExecution kernel = Execute(image, new ProducerRequest(PointerSignatureAnalyzer.Instance, terminal));
        Assert.Equal(0, kernel.GateCacheLookups);

        // Beside an independent producer, the pass is interpreted.
        MethodDefinitionExecution interpreted = Execute(
            image,
            new ProducerRequest(PointerSignatureAnalyzer.Instance, terminal),
            new ProducerRequest(Independent.Instance));
        Assert.Equal(1, interpreted.GateCacheLookups);

        // Both pass shapes answer as before.
        ClosedQueryResult<ClassifiedMethodRow> alone = kernel.ResultOf(PointerSignatureAnalyzer.Instance).Value!;
        ClosedQueryResult<ClassifiedMethodRow> fused = interpreted.ResultOf(PointerSignatureAnalyzer.Instance).Value!;
        Assert.Equal(terminal == ProducerTerminal.Exists ? 1 : 2, alone.Count);
        Assert.Equal(alone.Count, fused.Count);
    }

    [Theory]
    [InlineData(ProducerTerminal.Rows, ProducerTerminal.Count)]
    [InlineData(ProducerTerminal.Count, ProducerTerminal.Rows)]
    [InlineData(ProducerTerminal.Rows, ProducerTerminal.Exists)]
    [InlineData(ProducerTerminal.Exists, ProducerTerminal.Rows)]
    [InlineData(ProducerTerminal.Count, ProducerTerminal.Exists)]
    [InlineData(ProducerTerminal.Exists, ProducerTerminal.Count)]
    public void Planner_DistinctClosingsForOneProducerAreAContractError(ProducerTerminal first, ProducerTerminal second)
    {
        // Planning never ranks or merges closings, so no order yields a lossy plan.
        ProducerContractException error = Assert.Throws<ProducerContractException>(() => ProducerPlanner.Plan(
        [
            new ProducerRequest(RuntimeAsyncAnalyzer.Instance, first),
            new ProducerRequest(RuntimeAsyncAnalyzer.Instance, second),
        ]));
        Assert.Contains(RuntimeAsyncAnalyzer.Instance.Identity, error.Message, StringComparison.Ordinal);
        Assert.Contains(first.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains(second.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProducerTerminal.Rows)]
    [InlineData(ProducerTerminal.Count)]
    [InlineData(ProducerTerminal.Exists)]
    public void Planner_IdenticalDuplicateRequestIsAccepted(ProducerTerminal terminal)
    {
        WorkDescription description = Plan(
            new ProducerRequest(RuntimeAsyncAnalyzer.Instance, terminal),
            new ProducerRequest(RuntimeAsyncAnalyzer.Instance, terminal));

        Assert.Equal(terminal, description.TerminalOf(RuntimeAsyncAnalyzer.Instance));
        Assert.Equal([RuntimeAsyncAnalyzer.Instance], description.Producers);
    }

    [Fact]
    public void Planner_IdenticalHeadIsAcceptedAndDistinctLimitsConflict()
    {
        ProducerRequest head = ProducerRequest.Head(
            RuntimeAsyncAnalyzer.Instance,
            2);
        WorkDescription description = Plan(head, head);

        Assert.Equal(
            ProducerTerminal.Rows,
            description.TerminalOf(RuntimeAsyncAnalyzer.Instance));
        Assert.Equal(
            2,
            description.RowLimitOf(RuntimeAsyncAnalyzer.Instance));

        ProducerContractException error =
            Assert.Throws<ProducerContractException>(() => ProducerPlanner.Plan(
            [
                ProducerRequest.Head(RuntimeAsyncAnalyzer.Instance, 2),
                ProducerRequest.Head(RuntimeAsyncAnalyzer.Instance, 3),
            ]));
        Assert.Contains("Head(2)", error.Message, StringComparison.Ordinal);
        Assert.Contains("Head(3)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_HeadRequiresAPositiveRowLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProducerRequest.Head(RuntimeAsyncAnalyzer.Instance, 0));
        Assert.Throws<ArgumentException>(() => new ProducerRequest(
            RuntimeAsyncAnalyzer.Instance,
            ProducerTerminal.Complete,
            rowLimit: 1));
    }

    [Fact]
    public void Analyzers_KernelEqualsTheInterpretedExecutor()
    {
        GateFixtureImage builder = new();
        builder.Type("N", "K")
            .Method("A", PointerParameter())
            .Method("B", PointerParameter(), PublicStatic | MethodAttributes.PinvokeImpl, MethodImplAttributes.PreserveSig)
            .Method("C", implAttributes: RuntimeAsync)
            .Method("D");
        ImmutableArray<byte> image = builder.Build();

        ProducerRequest[] requests =
        [
            new(RuntimeAsyncAnalyzer.Instance, ProducerTerminal.Count),
            new(RuntimeAsyncAnalyzer.Instance, ProducerTerminal.Exists),
            new(RuntimeAsyncAnalyzer.Instance, ProducerTerminal.Rows),
            ProducerRequest.Head(RuntimeAsyncAnalyzer.Instance, 1),
        ];
        foreach (ProducerRequest request in requests)
        {
            // Alone, each analyzer runs as a kernel; beside an independent
            // producer, the reference executor interprets it.
            foreach (ProducerDeclaration<ClosedQueryResult<ClassifiedMethodRow>> analyzer in Analyzers)
            {
                ProducerRequest analyzerRequest = request.RowLimit is int count
                    ? ProducerRequest.Head(analyzer, count)
                    : new ProducerRequest(analyzer, request.Terminal);
                MethodDefinitionExecution kernel = Execute(image, analyzerRequest);
                MethodDefinitionExecution interpreted = Execute(
                    image,
                    analyzerRequest,
                    new ProducerRequest(Independent.Instance));

                ProducerResult<ClosedQueryResult<ClassifiedMethodRow>> k = kernel.ResultOf(analyzer);
                ProducerResult<ClosedQueryResult<ClassifiedMethodRow>> i = interpreted.ResultOf(analyzer);
                Assert.Equal(i.Outcome, k.Outcome);
                Assert.Equal(i.Value?.Count, k.Value?.Count);
                Assert.Equal(
                    i.Value is { HasRows: true } ir ? ir.Rows.Select(static row => row.Token) : [],
                    k.Value is { HasRows: true } kr ? kr.Rows.Select(static row => row.Token) : []);
                Assert.Equal(
                    interpreted.Receipt.For(analyzer).UnitsAttempted,
                    kernel.Receipt.For(analyzer).UnitsAttempted);
            }
        }
    }

    [Theory]
    [InlineData(ProducerTerminal.Count)]
    [InlineData(ProducerTerminal.Exists)]
    [InlineData(ProducerTerminal.Rows)]
    public void Analyzers_AbortedKernelReceiptEqualsTheInterpretedExecutor(
        ProducerTerminal terminal)
    {
        GateFixtureImage builder = new();
        builder.Type("N", "Order")
            .Method("Ordinary")
            .Method("OverCap", WideSignature());
        ImmutableArray<byte> image = builder.Build();

        MethodDefinitionExecution kernel = Execute(
            image,
            new ProducerRequest(PointerSignatureAnalyzer.Instance, terminal));
        MethodDefinitionExecution interpreted = Execute(
            image,
            new ProducerRequest(PointerSignatureAnalyzer.Instance, terminal),
            new ProducerRequest(Independent.Instance));

        Assert.NotNull(kernel.Receipt.Critical);
        Assert.NotNull(interpreted.Receipt.Critical);
        Assert.Equal(interpreted.Receipt.UnitsVisited, kernel.Receipt.UnitsVisited);
        Assert.Equal(2, kernel.Receipt.UnitsVisited);
        ProducerParticipation kp = kernel.Receipt.For(PointerSignatureAnalyzer.Instance);
        ProducerParticipation ip = interpreted.Receipt.For(PointerSignatureAnalyzer.Instance);
        Assert.Equal(
            (ip.Outcome, ip.UnitsAttempted, ip.UnitsCompleted, ip.UnitsFailed),
            (kp.Outcome, kp.UnitsAttempted, kp.UnitsCompleted, kp.UnitsFailed));
        Assert.Equal(
            (ProducerOutcome.Aborted, 2, 1, 0),
            (kp.Outcome, kp.UnitsAttempted, kp.UnitsCompleted, kp.UnitsFailed));
    }

    // ---- Helpers ----

    static readonly ProducerDeclaration<ClosedQueryResult<ClassifiedMethodRow>>[] Analyzers =
        [PInvokeAnalyzer.Instance, AsyncAnalyzer.Instance, RuntimeAsyncAnalyzer.Instance, CompilerAsyncAnalyzer.Instance, PointerSignatureAnalyzer.Instance];

    static BlobBuilder PointerParameter() =>
        GateFixtureImage.VoidSignature(static t => t.Pointer().Int32());

    static BlobBuilder WideSignature()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        int parameters = MetadataSafetyPolicy.MaxSignatureTypeNodes + 16;
        signature.WriteCompressedInteger(parameters);
        signature.WriteByte(0x01);
        for (int i = 0; i < parameters; i++)
            signature.WriteByte(0x08);
        return signature;
    }

    static BlobBuilder Bytes(params byte[] bytes)
    {
        var blob = new BlobBuilder();
        blob.WriteBytes(bytes);
        return blob;
    }

    static (ImmutableArray<ClassifiedMethodRow>, ImmutableArray<ClassifiedMethodRow>, ImmutableArray<ClassifiedMethodRow>) RowsOfAll(
        ImmutableArray<byte> image)
    {
        MethodDefinitionExecution execution = Execute(
            image,
            new ProducerRequest(PInvokeAnalyzer.Instance, ProducerTerminal.Rows),
            new ProducerRequest(RuntimeAsyncAnalyzer.Instance, ProducerTerminal.Rows),
            new ProducerRequest(CompilerAsyncAnalyzer.Instance, ProducerTerminal.Rows),
            new ProducerRequest(PointerSignatureAnalyzer.Instance, ProducerTerminal.Rows));
        return (
            execution.ResultOf(PInvokeAnalyzer.Instance).Value!.Rows,
            [.. execution.ResultOf(RuntimeAsyncAnalyzer.Instance).Value!.Rows,
                .. execution.ResultOf(CompilerAsyncAnalyzer.Instance).Value!.Rows],
            execution.ResultOf(PointerSignatureAnalyzer.Instance).Value!.Rows);
    }

    static ImmutableArray<ClassifiedMethodRow> Rows(
        ImmutableArray<byte> image,
        ProducerDeclaration<ClosedQueryResult<ClassifiedMethodRow>> analyzer) =>
        Execute(image, new ProducerRequest(analyzer, ProducerTerminal.Rows)).ResultOf(analyzer).Value!.Rows;

    static WorkDescription Plan(params ProducerRequest[] requests) =>
        Assert.IsType<ProducerPlanResult.Accepted>(ProducerPlanner.Plan(requests)).Description;

    static MethodDefinitionExecution Execute(ImmutableArray<byte> image, params ProducerRequest[] requests)
    {
        using var peReader = new PEReader(image);
        return MethodDefinitionExecution.Execute(Plan(requests), "Fixture.dll", peReader);
    }

    /// <summary>An independent flags-only producer, so its pass is interpreted.</summary>
    sealed class Independent()
        : MethodDefinitionPredicateProducer<AlwaysTrue>("Independent", version: 1, tier: 0, MethodDefinitionLayers.Flags)
    {
        public static Independent Instance { get; } = new();
    }

    struct AlwaysTrue : IMethodDefinitionPredicate
    {
        public readonly bool Test(scoped MethodDefinitionView view) => true;
    }

    [Fact]
    public void ExtensionAnalyzer_CountsPublicStaticExtensionMethodsOnStaticExtensionTypes()
    {
        GateFixtureImage builder = new();
        MemberReferenceHandle extension = builder.AttributeConstructor(builder.TypeRef(
            "System.Runtime.CompilerServices", "ExtensionAttribute"));
        MemberReferenceHandle editorBrowsable = builder.AttributeConstructor(
            builder.TypeRef("System.ComponentModel", "EditorBrowsableAttribute"),
            static t => t.Int32());
        MemberReferenceHandle obsolete = builder.AttributeConstructor(
            builder.TypeRef("System", "ObsoleteAttribute"),
            static t => t.String());
        MemberReferenceHandle compilerFeatureRequired = builder.AttributeConstructor(
            builder.TypeRef("System.Runtime.CompilerServices", "CompilerFeatureRequiredAttribute"),
            static t => t.String());
        builder.Type("N", "Extensions")
            .Attributes(extension)
            .Method("Visible", PointerParameter(), PublicStatic, attributeConstructors: extension)
            .Method("NotExtension", PointerParameter(), PublicStatic)
            .Method("Instance", PointerParameter(), MethodAttributes.Public, attributeConstructors: extension)
            .Method("Internal", PointerParameter(), MethodAttributes.Assembly | MethodAttributes.Static, attributeConstructors: extension)
            .Method("HiddenMethod", PointerParameter(), PublicStatic, attributeConstructors: extension)
            .MethodAttribute(editorBrowsable, GateFixtureImage.AttributeValue(1))
            .Method("BrowsableAdvanced", PointerParameter(), PublicStatic, attributeConstructors: extension)
            .MethodAttribute(editorBrowsable, GateFixtureImage.AttributeValue(2))
            .Method("Deprecated", PointerParameter(), PublicStatic, attributeConstructors: extension)
            .MethodAttribute(obsolete, GateFixtureImage.AttributeValue("Use Visible."))
            .Method("CompilerCompatibility", PointerParameter(), PublicStatic, attributeConstructors: extension)
            .MethodAttribute(obsolete, GateFixtureImage.AttributeValue(
                "Types with embedded references are not supported in this version of your compiler."))
            .MethodAttribute(compilerFeatureRequired, GateFixtureImage.AttributeValue("RefStructs"))
            .Method("CompilerCompatibilityWithDiagnosticId", PointerParameter(), PublicStatic, attributeConstructors: extension)
            .MethodAttribute(obsolete, ObsoleteWithDiagnosticId(
                "Types with embedded references are not supported in this version of your compiler.",
                "CS9999"))
            .MethodAttribute(compilerFeatureRequired, GateFixtureImage.AttributeValue("RefStructs"))
            .Method("ManyCompatibilityMarkers", PointerParameter(), PublicStatic, attributeConstructors: extension);
        for (int marker = 0; marker < 2048; marker++)
        {
            builder.Types[^1]
                .MethodAttribute(obsolete, GateFixtureImage.AttributeValue(
                    "Constructors of types with required members are not supported in this version of your compiler."))
                .MethodAttribute(compilerFeatureRequired, GateFixtureImage.AttributeValue("RequiredMembers"));
        }
        builder.Type("N", "NotExtensionType")
            .Method("Stray", PointerParameter(), PublicStatic, attributeConstructors: extension);
        builder.Type("N", "HiddenExtensions")
            .Attributes(extension)
            .Attribute(editorBrowsable, GateFixtureImage.AttributeValue(1))
            .Method("OnHiddenType", PointerParameter(), PublicStatic, attributeConstructors: extension);
        builder.Type("N", "InstanceExtensions")
            .WithShape(TypeAttributes.Public)
            .Attributes(extension)
            .Method("OnInstanceType", PointerParameter(), PublicStatic, attributeConstructors: extension);
        ImmutableArray<byte> image = builder.Build();

        MethodDefinitionExecution count = Execute(image, new ProducerRequest(ExtensionMethodAnalyzer.Instance, ProducerTerminal.Count));
        ImmutableArray<ClassifiedMethodRow> rows = Rows(image, ExtensionMethodAnalyzer.Instance);

        // Legacy compares a compatibility message only within sixteen bytes
        // of its length, so the DiagnosticId variant is an ordinary
        // deprecation (hidden) for both; the 2,048 repeated markers are read
        // once per row by the gate where legacy rescans at every marker.
        Assert.Equal(4, count.ResultOf(ExtensionMethodAnalyzer.Instance).Value!.Count);
        Assert.Equal(
            ["Visible", "BrowsableAdvanced", "CompilerCompatibility", "ManyCompatibilityMarkers"],
            rows.Select(static row => row.MethodName.ToString()));
        Assert.All(rows, static row => Assert.Equal(MethodClassification.Extension, row.Classification));
        Assert.Equal(4, LegacyExtensionMethodCount(image));
    }

    /// <summary>An <c>[Obsolete(message, DiagnosticId = id)]</c> value: one fixed string and one named property.</summary>
    static BlobBuilder ObsoleteWithDiagnosticId(string message, string diagnosticId)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteSerializedString(message);
        blob.WriteUInt16(1);
        blob.WriteByte(0x54); // PROPERTY
        blob.WriteByte(0x0e); // ELEMENT_TYPE_STRING
        blob.WriteSerializedString("DiagnosticId");
        blob.WriteSerializedString(diagnosticId);
        return blob;
    }

    [Fact]
    public void ExtensionAnalyzer_CountsAReceiverNestedBeyondTheLegacySignatureGuard()
    {
        // Roslyn compiles `void M(this int[][]...[] value)` with 513 array
        // levels, and likewise a shallow receiver with a deep second
        // parameter. Legacy decodes every candidate's signature through the
        // signature guard, whose depth bound is 512, and drops both methods;
        // the analyzer decodes nothing and counts them. Both are extension
        // methods, so the analyzer's answer is the faithful one.
        GateFixtureImage builder = new();
        MemberReferenceHandle extension = builder.AttributeConstructor(builder.TypeRef(
            "System.Runtime.CompilerServices", "ExtensionAttribute"));
        builder.Type("N", "Extensions")
            .Attributes(extension)
            .Method(
                "Deep",
                GateFixtureImage.VoidSignature(static t =>
                {
                    SignatureTypeEncoder element = t;
                    for (int level = 0; level < 513; level++)
                        element = element.SZArray();
                    element.Int32();
                }),
                PublicStatic,
                attributeConstructors: extension)
            .Method(
                "DeepParameter",
                GateFixtureImage.VoidSignature(
                    static t => t.Int32(),
                    static t =>
                    {
                        SignatureTypeEncoder element = t;
                        for (int level = 0; level < 513; level++)
                            element = element.SZArray();
                        element.Int32();
                    }),
                PublicStatic,
                attributeConstructors: extension);
        ImmutableArray<byte> image = builder.Build();

        MethodDefinitionExecution count = Execute(image, new ProducerRequest(ExtensionMethodAnalyzer.Instance, ProducerTerminal.Count));

        Assert.Equal(2, count.ResultOf(ExtensionMethodAnalyzer.Instance).Value!.Count);
        Assert.Equal(0, LegacyExtensionMethodCount(image));
    }

    static int LegacyExtensionMethodCount(ImmutableArray<byte> image)
    {
        using var reader = new PEReader(image);
        return ExtensionMethodScanner.FindAllExtensions(reader, includeAll: false)
            .Count(static member => member.Kind == "method");
    }

    [Theory]
    [InlineData(typeof(System.Text.Json.JsonSerializer))]
    [InlineData(typeof(Enumerable))]
    public void ExtensionAnalyzer_CountEqualsTheLegacyScanOnARealAssembly(Type anchor)
    {
        ImmutableArray<byte> image = [.. File.ReadAllBytes(anchor.Assembly.Location)];
        int legacy = LegacyExtensionMethodCount(image);

        MethodDefinitionExecution count = Execute(image, new ProducerRequest(ExtensionMethodAnalyzer.Instance, ProducerTerminal.Count));

        Assert.True(legacy > 0);
        Assert.Equal(legacy, count.ResultOf(ExtensionMethodAnalyzer.Instance).Value!.Count);
    }
}
