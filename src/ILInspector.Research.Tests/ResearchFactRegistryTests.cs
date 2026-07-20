using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

public class ResearchFactRegistryTests
{
    [Fact]
    public void Registry_OrdersProducersAfterTheirDependencies()
    {
        var registry = new ResearchFactRegistry(
            new TestProducer("consumer", ["source"]),
            new TestProducer("source"));

        Assert.Equal(["source", "consumer"], registry.ProducerNames);
    }

    [Fact]
    public void MemberProjection_PreservesConstructorChainMetadata()
    {
        using var source = MetadataSource.Open(typeof(ResearchConstructorFixture).Assembly.Location);

        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchConstructorFixture).FullName!,
            ".ctor",
            AnnotatedSource: true,
            CostOverlay: true,
            SemanticsOverlay: true));

        var results = new[]
        {
            Assert.IsType<DecompilerResult>(projection.AnnotatedSource),
            Assert.IsType<DecompilerResult>(projection.CostOverlay?.Body),
            Assert.IsType<DecompilerResult>(projection.SemanticsOverlay)
        };
        Assert.All(results, result =>
        {
            Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.StartsWith("this(", result.ConstructorChain);
            Assert.DoesNotContain(": this(", result.Output);
        });
    }

    [Fact]
    public void MemberProjection_TokenAddressingMatchesNameAddressing()
    {
        string path = typeof(ResearchConstructorFixture).Assembly.Location;
        using var metadata = AssemblyInspectionSession.Open(path);
        var selection = metadata.MethodBodies.ResolveMethod(
            typeof(ResearchConstructorFixture).FullName!,
            ".ctor",
            overloadIndex: 0,
            publicOnly: false);
        Assert.NotNull(selection);

        using var source = MetadataSource.Open(path);
        var byName = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchConstructorFixture).FullName!,
            ".ctor",
            AnnotatedSource: true));
        var byToken = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchConstructorFixture).FullName!,
            ".ctor",
            AnnotatedSource: true,
            MethodToken: selection.MetadataToken));

        Assert.Equal(byName.AnnotatedSource?.Output, byToken.AnnotatedSource?.Output);
        Assert.Equal(
            byName.AnnotatedSource?.ConstructorChain,
            byToken.AnnotatedSource?.ConstructorChain);
        Assert.Equal(
            byName.AnnotatedSource?.Diagnostics,
            byToken.AnnotatedSource?.Diagnostics);
    }

    [Fact]
    public void MemberProjection_PreservesInitializerOnlyConstructorMetadata()
    {
        using var source = MetadataSource.Open(typeof(ResearchInitializerOnlyFixture).Assembly.Location);

        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchInitializerOnlyFixture).FullName!,
            ".ctor",
            AnnotatedSource: true,
            CostOverlay: true,
            SemanticsOverlay: true));

        var results = new[]
        {
            Assert.IsType<DecompilerResult>(projection.AnnotatedSource),
            Assert.IsType<DecompilerResult>(projection.CostOverlay?.Body),
            Assert.IsType<DecompilerResult>(projection.SemanticsOverlay)
        };
        Assert.All(results, result =>
        {
            Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
            Assert.Contains((nameof(ResearchInitializerOnlyFixture.Value), "42"), result.FieldInitializers);
            Assert.DoesNotContain(nameof(ResearchInitializerOnlyFixture.Value), result.Output);
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.EmptyOutput);
        });
    }

    [Fact]
    public void RunProjection_PreservesFailureDiagnostics()
    {
        var failure = DecompilerResult.Failure(
            DiagnosticIds.ContextUnavailable,
            "overlay context unavailable");

        var result = ResearchViews.RunProjection(
            () => failure,
            emptyOutputIsFailure: true);

        Assert.Same(failure, result);
        Assert.Equal(DiagnosticIds.ContextUnavailable, Assert.Single(result.Diagnostics).Id);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.EmptyOutput);
    }

    [Fact]
    public void RunProjection_AllowsEmptyBodyOnlyCSharp()
    {
        var result = ResearchViews.RunProjection(
            () => DecompilerResult.Success(""),
            emptyOutputIsFailure: false);

        Assert.True(result.Succeeded);
        Assert.Equal("", result.Output);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EmptyRegistry_ProducesNoOverlayFacts()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var registry = new ResearchFactRegistry();

        var facts = ResearchViews.CollectFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry: registry);
        var annotated = RenderAnnotatedSource(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry).Output;
        var imported = IrImporter.Import(source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt))
            ?? throw new InvalidOperationException("fixture method has no IL body");
        var headerFacts = registry.CollectHeaderFacts(new ResearchFactContext(source, imported));

        Assert.Empty(facts);
        Assert.Empty(headerFacts);
        Assert.DoesNotContain("alloc.", annotated);
    }

    [Fact]
    public void RenderedOverlayFacts_ComeFromRegisteredProducers()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var registry = new ResearchFactRegistry(new TestProducer(
            "registered-allocation",
            produces: ["alloc.box"],
            facts: [new Annotation(
                new AnnotationDescriptor("alloc.box", AnnotationCategory.Allocation, "test allocation"),
                SourceOffset: 0,
                Detail: "registered")]));

        var facts = ResearchViews.CollectFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry: registry);
        var annotated = RenderAnnotatedSource(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.BoxInt), registry).Output;

        var fact = Assert.Single(facts);
        Assert.Equal("alloc.box", fact.Descriptor.Id);
        Assert.Contains("alloc.box(registered)", annotated);
    }

    [Fact]
    public void MemberProjection_CollectsFactsOnceForRequestedViews()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);
        var producer = new CountingProducer();
        var registry = new ResearchFactRegistry(producer);

        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt),
            AnnotatedSource: true,
            CostOverlay: true,
            SemanticsOverlay: true,
            FactRows: true,
            Registry: registry));

        Assert.Equal(1, producer.FactCollectCount);
        Assert.Equal(1, producer.HeaderFactCollectCount);
        Assert.NotNull(producer.AssemblyContext);
        Assert.Same(producer.AssemblyContext, producer.HeaderAssemblyContext);
        Assert.Contains("cost.test", projection.AnnotatedSource!.Output);
        Assert.Contains("cost.test", projection.CostOverlay!.Body.Output);
        Assert.Contains("semantics.test", projection.SemanticsOverlay!.Output);
        Assert.Contains(projection.Facts!, row => row.Id == "cost.header.test");
    }

    [Fact]
    public void DefaultAllocationProducer_ProjectsAnalysisFindingCensus()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var facts = ResearchViews.CollectFacts(
            source,
            typeof(ResearchFixture).FullName!,
            nameof(ResearchFixture.BoxInt));

        var allocation = Assert.Single(facts, fact => fact.Descriptor.Id == "alloc.box");
        Assert.True(allocation.SourceOffset >= 0);
        Assert.Contains("System.Int32", allocation.Detail);
    }

    [Fact]
    public void CostOverlay_AnnotatesHighValueCalleeAtCallSite()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderCostOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsAllocInLoopCallee)).Output;

        Assert.Contains("AllocInLoopCallee", overlay);
        Assert.Contains("cost.callee", overlay);
        Assert.Contains("alloc-loop", overlay);
    }

    [Fact]
    public void CostOverlay_DoesNotAnnotateLowSignalCallee()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderCostOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsLowSignalCallee)).Output;

        Assert.Contains("LowSignalCallee", overlay);
        Assert.DoesNotContain("cost.callee", overlay);
    }

    [Fact]
    public void CostOverlay_DoesNotAnnotateExceptionOnlyCallee()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderCostOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsExceptionOnlyCallee)).Output;

        Assert.Contains("ExceptionOnlyCallee", overlay);
        Assert.DoesNotContain("cost.callee", overlay);
        Assert.DoesNotContain("FormatException", overlay);
    }

    [Fact]
    public void CostOverlay_RendersMethodHeaderLeverage()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderCostOverlayWithHeaderFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.HighLoopLeverageCallee));

        var fact = Assert.Single(overlay.HeaderFacts);
        Assert.Equal("cost.method", fact.Descriptor.Id);
        Assert.Contains("direct-callers 20", fact.Detail);
        Assert.DoesNotContain("cost.method", overlay.Body.Output);
    }

    [Fact]
    public void CostOverlay_KeepsMethodHeaderLeverageOutOfBodyText()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderCostOverlayWithHeaderFacts(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.HighLoopLeverageCallee));

        Assert.NotEmpty(overlay.HeaderFacts);
        Assert.DoesNotContain("cost.method", overlay.Body.Output);
        Assert.StartsWith("return value + 1;", overlay.Body.Output);
    }

    [Fact]
    public void CostOverlay_DoesNotAnnotateDirectCallerOnlyCallee()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderCostOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.LoopCaller01)).Output;

        Assert.Contains("HighLoopLeverageCallee", overlay);
        Assert.DoesNotContain("cost.callee", overlay);
        Assert.DoesNotContain("root-reach 1", overlay);
        Assert.DoesNotContain("direct-callers 1", overlay);
    }

    [Fact]
    public void SemanticsOverlay_AnnotatesExceptionCalleeAtCallSite()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderSemanticsOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsExceptionOnlyCallee)).Output;

        Assert.Contains("ExceptionOnlyCallee", overlay);
        Assert.Contains("semantics.callee", overlay);
        Assert.Contains("may-throw FormatException", overlay);
        Assert.DoesNotContain("cost.callee", overlay);
    }

    [Fact]
    public void SemanticsOverlay_SuppressesArgumentValidationOnlyCallee()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderSemanticsOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsArgumentValidationOnlyCallee)).Output;

        Assert.Contains("ArgumentValidationOnlyCallee", overlay);
        Assert.DoesNotContain("semantics.callee", overlay);
        Assert.DoesNotContain("ArgumentNullException", overlay);
    }

    [Fact]
    public void SemanticsOverlay_PrefersDomainExceptionOverArgumentValidation()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderSemanticsOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsMixedExceptionCallee)).Output;

        Assert.Contains("MixedExceptionCallee", overlay);
        Assert.Contains("semantics.callee", overlay);
        Assert.Contains("may-throw FormatException", overlay);
        Assert.DoesNotContain("ArgumentNullException", overlay);
    }

    [Fact]
    public void SemanticsOverlay_AnnotatesUnsafeCalleeAtCallSite()
    {
        using var source = MetadataSource.Open(typeof(ResearchFixture).Assembly.Location);

        var overlay = RenderSemanticsOverlay(
            source, typeof(ResearchFixture).FullName!, nameof(ResearchFixture.CallsStackallocCallee)).Output;

        Assert.Contains("StackallocCallee", overlay);
        Assert.Contains("safety.callee", overlay);
        Assert.Contains("unsafe", overlay);
        Assert.Contains("stackalloc", overlay);
    }

    static DecompilerResult RenderAnnotatedSource(
        MetadataSource source, string type, string method, ResearchFactRegistry? registry = null)
        => ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source, type, method, AnnotatedSource: true, Registry: registry)).AnnotatedSource!;

    static DecompilerResult RenderCostOverlay(MetadataSource source, string type, string method)
        => RenderCostOverlayWithHeaderFacts(source, type, method).Body;

    static ResearchViews.CostOverlayResult RenderCostOverlayWithHeaderFacts(
        MetadataSource source, string type, string method)
        => ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source, type, method, CostOverlay: true)).CostOverlay!;

    static DecompilerResult RenderSemanticsOverlay(MetadataSource source, string type, string method)
        => ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source, type, method, SemanticsOverlay: true)).SemanticsOverlay!;

    sealed class TestProducer(
        string name,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<string>? produces = null,
        IReadOnlyList<IAnnotation>? facts = null) : IResearchFactProducer
    {
        public string Name => name;
        public IReadOnlyList<string> Produces { get; } = produces ?? [];
        public IReadOnlyList<string> DependsOn { get; } = dependsOn ?? [];
        public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context) => facts ?? [];
    }

    sealed class CountingProducer : IResearchFactProducer
    {
        public int FactCollectCount { get; private set; }
        public int HeaderFactCollectCount { get; private set; }
        public ResearchAssemblyContext? AssemblyContext { get; private set; }
        public ResearchAssemblyContext? HeaderAssemblyContext { get; private set; }
        public string Name => "counting";
        public IReadOnlyList<string> Produces { get; } = ["cost.test", "semantics.test", "cost.header.test"];
        public IReadOnlyList<string> DependsOn => [];

        public IReadOnlyList<IAnnotation> Produce(ResearchFactContext context)
        {
            FactCollectCount++;
            AssemblyContext = context.Assembly;
            return
            [
                new Annotation(
                    new AnnotationDescriptor("cost.test", AnnotationCategory.Cost, "test cost"),
                    SourceOffset: 0,
                    Detail: "shared"),
                new Annotation(
                    new AnnotationDescriptor("semantics.test", AnnotationCategory.Semantics, "test semantics"),
                    SourceOffset: 0,
                    Detail: "shared")
            ];
        }

        public IReadOnlyList<ResearchHeaderFact> ProduceHeaderFacts(ResearchFactContext context)
        {
            HeaderFactCollectCount++;
            HeaderAssemblyContext = context.Assembly;
            return
            [
                new ResearchHeaderFact(
                    new AnnotationDescriptor("cost.header.test", AnnotationCategory.Cost, "test header"),
                    "shared")
            ];
        }
    }
}

public static class ResearchFixture
{
    public static object BoxInt(int value) => value;

    public static int CallsAllocInLoopCallee(int count) => AllocInLoopCallee(count);

    public static int AllocInLoopCallee(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++)
            total += new object().GetHashCode();
        return total;
    }

    public static int CallsLowSignalCallee(int value) => LowSignalCallee(value);

    public static int LowSignalCallee(int value) => value + 1;

    public static int CallsExceptionOnlyCallee(string value) => ExceptionOnlyCallee(value);

    public static int ExceptionOnlyCallee(string value)
    {
        if (value.Length == 0)
            throw new FormatException();
        return value.Length;
    }

    public static int CallsArgumentValidationOnlyCallee(string? value) => ArgumentValidationOnlyCallee(value);

    public static int ArgumentValidationOnlyCallee(string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length;
    }

    public static int CallsMixedExceptionCallee(string? value) => MixedExceptionCallee(value);

    public static int MixedExceptionCallee(string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            throw new FormatException();
        return value.Length;
    }

    public static int CallsStackallocCallee(int value) => StackallocCallee(value);

    public static int StackallocCallee(int value)
    {
        Span<int> values = stackalloc int[1];
        values[0] = value;
        return values[0];
    }

    public static int SharedLeverageCallee(int value) => value + 1;

    public static int LeverageCallerA(int value) => SharedLeverageCallee(value);

    public static int LeverageCallerB(int value) => SharedLeverageCallee(value + 1);

    public static int HighLoopLeverageCallee(int value) => value + 1;

    public static int LoopCaller01(int count) { int total = 1; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller02(int count) { int total = 2; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller03(int count) { int total = 3; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller04(int count) { int total = 4; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller05(int count) { int total = 5; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller06(int count) { int total = 6; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller07(int count) { int total = 7; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller08(int count) { int total = 8; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller09(int count) { int total = 9; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller10(int count) { int total = 10; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller11(int count) { int total = 11; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller12(int count) { int total = 12; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller13(int count) { int total = 13; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller14(int count) { int total = 14; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller15(int count) { int total = 15; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller16(int count) { int total = 16; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller17(int count) { int total = 17; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller18(int count) { int total = 18; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller19(int count) { int total = 19; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
    public static int LoopCaller20(int count) { int total = 20; for (int i = 0; i < count; i++) total += HighLoopLeverageCallee(i); return total; }
}

public sealed class ResearchConstructorFixture
{
    public ResearchConstructorFixture()
        : this(42)
    {
    }

    public ResearchConstructorFixture(int value)
    {
        GC.KeepAlive(value);
    }
}

public sealed class ResearchInitializerOnlyFixture
{
    public int Value = 42;
}
