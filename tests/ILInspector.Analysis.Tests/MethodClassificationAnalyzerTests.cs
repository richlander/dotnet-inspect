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
        List<ClassifiedMethodInfo> legacy = MethodClassificationScanner.Scan(peReader);
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
        ClassifiedMethodInfo legacy = Assert.Single(MethodClassificationScanner.Scan(peReader));
        Assert.Equal(legacy.Signature, row.Signature.ToString());
        Assert.Equal("M(...)", row.Signature.ToString());
        Assert.Null(row.Anchor);
        Assert.Null(legacy.Anchor);
    }

    [Theory]
    [InlineData(ProducerTerminal.All)]
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

        foreach (ProducerTerminal terminal in new[] { ProducerTerminal.All, ProducerTerminal.Exists })
        {
            MethodDefinitionExecution execution = Execute(
                image,
                new ProducerRequest(PInvokeAnalyzer.Instance, terminal),
                new ProducerRequest(AsyncAnalyzer.Instance, terminal),
                new ProducerRequest(PointerSignatureAnalyzer.Instance, terminal));

            Assert.False(execution.Receipt.IdentityBudgetArmed);
            Assert.Equal(0, execution.Receipt.IdentityWorkCharged);
            Assert.False(execution.ResultOf(PointerSignatureAnalyzer.Instance).Value!.HasRows);
        }

        MethodDefinitionExecution counted = Execute(image, new ProducerRequest(PointerSignatureAnalyzer.Instance));
        Assert.Equal(2, counted.ResultOf(PointerSignatureAnalyzer.Instance).Value!.Count);
    }

    [Fact]
    public void Analyzers_RowsAndCountForOneAnalyzerRunRowsOnce()
    {
        GateFixtureImage builder = new();
        builder.Type("N", "Plain")
            .Method("A", PointerParameter(), implAttributes: RuntimeAsync)
            .Method("B", implAttributes: RuntimeAsync)
            .Method("C");
        ImmutableArray<byte> image = builder.Build();

        WorkDescription description = Plan(
            new ProducerRequest(AsyncAnalyzer.Instance, ProducerTerminal.All),
            new ProducerRequest(AsyncAnalyzer.Instance, ProducerTerminal.Rows));

        Assert.Equal(ProducerTerminal.Rows, description.TerminalOf(AsyncAnalyzer.Instance));
        using var peReader = new PEReader(image);
        MethodDefinitionExecution execution = MethodDefinitionExecution.Execute(description, "Fixture.dll", peReader);
        ClosedQueryResult<ClassifiedMethodRow> result = execution.ResultOf(AsyncAnalyzer.Instance).Value!;
        Assert.Equal(2, result.Count);
        Assert.Equal(result.Count, result.Rows.Length);
        Assert.True(execution.Receipt.IdentityBudgetArmed);
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

        foreach (ProducerTerminal terminal in new[] { ProducerTerminal.All, ProducerTerminal.Exists, ProducerTerminal.Rows })
        {
            // Alone, each analyzer runs as a kernel; beside an independent
            // producer, the reference executor interprets it.
            foreach (ProducerDeclaration<ClosedQueryResult<ClassifiedMethodRow>> analyzer in Analyzers)
            {
                MethodDefinitionExecution kernel = Execute(image, new ProducerRequest(analyzer, terminal));
                MethodDefinitionExecution interpreted = Execute(
                    image,
                    new ProducerRequest(analyzer, terminal),
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

    // ---- Helpers ----

    static readonly ProducerDeclaration<ClosedQueryResult<ClassifiedMethodRow>>[] Analyzers =
        [PInvokeAnalyzer.Instance, AsyncAnalyzer.Instance, PointerSignatureAnalyzer.Instance];

    static BlobBuilder PointerParameter() =>
        GateFixtureImage.VoidSignature(static t => t.Pointer().Int32());

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
            new ProducerRequest(AsyncAnalyzer.Instance, ProducerTerminal.Rows),
            new ProducerRequest(PointerSignatureAnalyzer.Instance, ProducerTerminal.Rows));
        return (
            execution.ResultOf(PInvokeAnalyzer.Instance).Value!.Rows,
            execution.ResultOf(AsyncAnalyzer.Instance).Value!.Rows,
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
}
