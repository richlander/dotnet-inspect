using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

public class ILOffsetProjectionProducerTests
{
    [Fact]
    public void ProjectILOffset_ComposesMemberAndInstructionContexts()
    {
        var method = typeof(ILOffsetProjectionProducerTests).GetMethod(
            nameof(AddOne),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        using var source = SourceLinkService.Open(typeof(ILOffsetProjectionProducerTests).Assembly.Location);

        var outcome = ResearchViews.ProjectILOffset(new ILOffsetProjectionRequest(
            source,
            method.MetadataToken,
            ILOffset: 0,
            ILOffsetProjectionCapabilities.InstructionContext));

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Failure);
        Assert.Equal($"0x{method.MetadataToken:X}", outcome.Projection!.Token);
        Assert.EndsWith($".{nameof(AddOne)}", outcome.Projection.MemberContext!.Member);
        Assert.Equal("ldarg.0", outcome.Projection.InstructionContext!.Opcode);
    }

    static int AddOne(int value) => value + 1;

    static object Allocate() => new();

    static int Stackalloc()
    {
        Span<int> values = stackalloc int[1];
        values[0] = 1;
        return values[0];
    }

    [Fact]
    public void
        ProjectILOffset_NonBoundaryToleranceRejectsOffsetOutsideMethodExtent()
    {
        var method = typeof(ILOffsetProjectionProducerTests).GetMethod(
            nameof(AddOne),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        using var source = SourceLinkService.Open(
            typeof(ILOffsetProjectionProducerTests).Assembly.Location);

        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    method.MetadataToken,
                    ILOffset: int.MaxValue,
                    ILOffsetProjectionCapabilities.InstructionContext,
                    AllowNonBoundaryContextAbsence: true));

        Assert.False(outcome.Succeeded);
        Assert.Equal(
            ILOffsetProjectionFailureKind.InstructionUnavailable,
            outcome.Failure!.Kind);
        Assert.Contains(
            "outside the decoded method body",
            outcome.Failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ProjectILOffset_CostContextUsesPhysicalAsyncBody()
    {
        MethodInfo sourceMethod =
            typeof(ILOffsetProjectionProducerTests).GetMethod(
                nameof(CallVirtualAsync),
                BindingFlags.Static
                    | BindingFlags.NonPublic)!;
        string path =
            typeof(ILOffsetProjectionProducerTests)
                .Assembly.Location;
        LibraryBodyAnalysisExecution execution =
            AnalyzePath(path);
        DirectCall call = Assert.Single(
            execution.CallGraph.DirectCalls,
            call => call.Caller.MetadataToken
                    == sourceMethod.MetadataToken
                && call.EvidenceMethod != call.Caller
                && call.Callee.Name
                    == nameof(OffsetVirtualTarget.Compute));
        using var source = SourceLinkService.OpenPrefetched(path);

        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    call.EvidenceMethod.MetadataToken,
                    call.ILOffset,
                    ILOffsetProjectionCapabilities
                        .CostContext,
                    Analysis: AnalysisInput(execution)));

        Assert.True(outcome.Succeeded);
        ILOffsetCostContext cost = Assert.Single(
            outcome.Projection!.CostContext!);
        Assert.Equal("virtual dispatch", cost.CostKind);
        Assert.Contains(
            nameof(OffsetVirtualTarget.Compute),
            cost.Operation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ProjectILOffset_DescriptorSemanticEvidenceDoesNotReopenPath()
    {
        MethodInfo sourceMethod =
            typeof(ILOffsetProjectionProducerTests).GetMethod(
                nameof(CallVirtualAsync),
                BindingFlags.Static
                    | BindingFlags.NonPublic)!;
        string sourcePath =
            typeof(ILOffsetProjectionProducerTests)
                .Assembly.Location;
        byte[] retainedImage = File.ReadAllBytes(sourcePath);
        LibraryBodyAnalysisExecution baseline =
            LibraryBodyAnalysisService.ExecuteImage(
                sourcePath,
                [.. retainedImage],
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));
        DirectCall call = Assert.Single(
            baseline.CallGraph.DirectCalls,
            call => call.Caller.MetadataToken
                    == sourceMethod.MetadataToken
                && call.EvidenceMethod != call.Caller
                && call.Callee.Name
                    == nameof(OffsetVirtualTarget.Compute));
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, retainedImage);
        try
        {
            var assembly = ResolvedAssemblyReference.Create(
                ReadIdentity(retainedImage),
                path,
                () => new MemoryStream(
                    retainedImage,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "IL-offset acquisition-continuity test"));
            using var source =
                SourceLinkService.OpenPrefetched(assembly);

            File.WriteAllBytes(path, [0x01, 0x02, 0x03]);

            ILOffsetProjectionOutcome outcome =
                ResearchViews.ProjectILOffset(
                    new ILOffsetProjectionRequest(
                        source,
                        call.EvidenceMethod.MetadataToken,
                        call.ILOffset,
                        ILOffsetProjectionCapabilities
                            .CostContext,
                        Analysis: AnalysisInput(baseline)));

            Assert.True(outcome.Succeeded);
            ILOffsetCostContext cost = Assert.Single(
                outcome.Projection!.CostContext!);
            Assert.Equal("virtual dispatch", cost.CostKind);
            Assert.Contains(
                nameof(OffsetVirtualTarget.Compute),
                cost.Operation,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void
        ProjectILOffset_RejectsMismatchedSourceAndAnalysisGenerations()
    {
        MethodInfo sourceMethod =
            typeof(ILOffsetProjectionProducerTests).GetMethod(
                nameof(CallVirtualAsync),
                BindingFlags.Static
                    | BindingFlags.NonPublic)!;
        ResolvedAssemblyReference sourceAssembly =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(ILOffsetProjectionProducerTests)
                    .Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "IL-offset source-generation test"));
        LibraryBodyAnalysisExecution analysis =
            AnalyzePath(
                typeof(LibraryBodyAnalysisExecution)
                    .Assembly.Location);
        using var source =
            SourceLinkService.OpenPrefetched(sourceAssembly);

        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    sourceMethod.MetadataToken,
                    ILOffset: 0,
                    ILOffsetProjectionCapabilities.CostContext,
                    Analysis: AnalysisInput(analysis)));

        Assert.False(outcome.Succeeded);
        Assert.Equal(
            ILOffsetProjectionFailureKind.CostAnalysisUnavailable,
            outcome.Failure!.Kind);
        Assert.Contains(
            "different module generations",
            outcome.Failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ProjectILOffset_MissingFocusedAnalysisIsVisible()
    {
        MethodInfo method =
            typeof(ILOffsetProjectionProducerTests).GetMethod(
                nameof(AddOne),
                BindingFlags.Static | BindingFlags.NonPublic)!;
        using var source = SourceLinkService.OpenPrefetched(
            typeof(ILOffsetProjectionProducerTests)
                .Assembly.Location);

        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    method.MetadataToken,
                    ILOffset: 0,
                    ILOffsetProjectionCapabilities.CostContext));

        Assert.False(outcome.Succeeded);
        Assert.Equal(
            ILOffsetProjectionFailureKind.CostAnalysisUnavailable,
            outcome.Failure!.Kind);
        Assert.Contains(
            "no focused Analysis input",
            outcome.Failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ProjectILOffset_ExactAllocationUsesOneDeduplicatedMethodRequest()
    {
        MethodInfo method =
            typeof(ILOffsetProjectionProducerTests).GetMethod(
                nameof(Allocate),
                BindingFlags.Static | BindingFlags.NonPublic)!;
        string path =
            typeof(ILOffsetProjectionProducerTests).Assembly.Location;
        using var source = SourceLinkService.OpenPrefetched(path);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(source.Context);

        ILOffsetAllocationAnalysisInput allocation =
            ILOffsetAllocationAnalysis.Execute(
                session,
                "IL-offset exact allocation test",
                source.Context.ModuleVersionId(),
                [method.MetadataToken, method.MetadataToken],
                TestContext.Current.CancellationToken);
        AllocationOccurrence occurrence = Assert.Single(
            allocation.Occurrences[method.MetadataToken]);
        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    method.MetadataToken,
                    occurrence.ILOffset,
                    ILOffsetProjectionCapabilities.AllocationContext,
                    AllocationAnalysis: allocation));

        Assert.True(outcome.Succeeded);
        Assert.Single(outcome.Projection!.AllocationContext!);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.ExactMethods,
            allocation.SourceReceipt.Breadth.Kind);
        Assert.Single(allocation.SourceReceipt.Breadth.Methods);
        Assert.Equal(
            session.ImageLength,
            allocation.SourceReceipt.TerminalWorkLimits
                .MaximumEncodedIlBytes);
        Assert.Equal(1, allocation.SourceReceipt.DefinitionsVisited);
        Assert.Equal(1, allocation.SourceReceipt.BodiesAcquired);
        Assert.Equal(0, allocation.SourceReceipt.ModuleLookups);
        Assert.Single(allocation.WorkReceipt.Producers);
    }

    [Fact]
    public void
        ProjectILOffset_ExactSafetyUsesOneDeduplicatedMethodRequest()
    {
        MethodInfo method =
            typeof(ILOffsetProjectionProducerTests).GetMethod(
                nameof(Stackalloc),
                BindingFlags.Static | BindingFlags.NonPublic)!;
        string path =
            typeof(ILOffsetProjectionProducerTests).Assembly.Location;
        LibraryBodyAnalysisExecution legacy = AnalyzePath(path);
        int ilOffset = Assert.Single(
            legacy.Safety.Occurrences[method.MetadataToken],
            occurrence => occurrence.Kind == UnsafetyKind.StackAlloc).ILOffset;
        using var source = SourceLinkService.OpenPrefetched(path);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(source.Context);

        ILOffsetSafetyAnalysisInput safety =
            ILOffsetSafetyAnalysis.Execute(
                session,
                "IL-offset exact safety test",
                source.Context.ModuleVersionId(),
                [method.MetadataToken, method.MetadataToken],
                TestContext.Current.CancellationToken);
        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    method.MetadataToken,
                    ilOffset,
                    ILOffsetProjectionCapabilities.SafetyContext,
                    SafetyAnalysis: safety));

        Assert.True(outcome.Succeeded);
        ILOffsetSafetyContext context = Assert.Single(
            outcome.Projection!.SafetyContext!);
        Assert.Equal("stackalloc", context.SafetyKind);
        Assert.Equal(
            MethodDefinitionSourceBreadthKind.ExactMethods,
            safety.SourceReceipt.Breadth.Kind);
        Assert.Single(safety.SourceReceipt.Breadth.Methods);
        Assert.Equal(
            session.ImageLength,
            safety.SourceReceipt.TerminalWorkLimits
                .MaximumEncodedIlBytes);
        Assert.Equal(1, safety.SourceReceipt.DefinitionsVisited);
        Assert.Equal(1, safety.SourceReceipt.BodiesAcquired);
        Assert.Equal(0, safety.SourceReceipt.ModuleLookups);
        Assert.Single(safety.WorkReceipt.Producers);
    }

    [Fact]
    public void
        ProjectILOffset_RejectsMismatchedExactSafetyGeneration()
    {
        MethodInfo method =
            typeof(ILOffsetProjectionProducerTests).GetMethod(
                nameof(AddOne),
                BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo foreignMethod =
            typeof(LibraryBodyAnalysisExecution)
                .GetProperty(
                    nameof(LibraryBodyAnalysisExecution.Receipt))!
                .GetMethod!;
        string foreignPath =
            typeof(LibraryBodyAnalysisExecution).Assembly.Location;
        using var foreignSource =
            SourceLinkService.OpenPrefetched(foreignPath);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(foreignSource.Context);
        ILOffsetSafetyAnalysisInput safety =
            ILOffsetSafetyAnalysis.Execute(
                session,
                "IL-offset mismatched safety test",
                foreignSource.Context.ModuleVersionId(),
                [foreignMethod.MetadataToken],
                TestContext.Current.CancellationToken);
        using var source = SourceLinkService.OpenPrefetched(
            typeof(ILOffsetProjectionProducerTests)
                .Assembly.Location);

        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    method.MetadataToken,
                    ILOffset: 0,
                    ILOffsetProjectionCapabilities.SafetyContext,
                    SafetyAnalysis: safety));

        Assert.False(outcome.Succeeded);
        Assert.Equal(
            ILOffsetProjectionFailureKind.SafetyAnalysisUnavailable,
            outcome.Failure!.Kind);
        Assert.Contains(
            "different module generations",
            outcome.Failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        ILOffsetAnalysisInput_RejectsMixedExecutionReceipts()
    {
        LibraryBodyAnalysisExecution first =
            AnalyzePath(
                typeof(ILOffsetProjectionProducerTests)
                    .Assembly.Location);
        LibraryBodyAnalysisExecution second =
            AnalyzePath(
                typeof(LibraryBodyAnalysisExecution)
                    .Assembly.Location);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new ILOffsetAnalysisInput(
                first.Allocations,
                second.Safety,
                first.CallGraph));

        Assert.Contains(
            "one execution receipt",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        ILOffsetProjectionCapabilities.AllocationContext,
        ILOffsetProjectionFailureKind
            .AllocationAnalysisUnavailable)]
    [InlineData(
        ILOffsetProjectionCapabilities.SafetyContext,
        ILOffsetProjectionFailureKind
            .SafetyAnalysisUnavailable)]
    [InlineData(
        ILOffsetProjectionCapabilities.CostContext,
        ILOffsetProjectionFailureKind
            .CostAnalysisUnavailable)]
    public void
        ProjectILOffset_UnrequestedFocusedResultIsVisible(
            ILOffsetProjectionCapabilities capability,
            ILOffsetProjectionFailureKind expectedFailure)
    {
        MethodInfo method =
            typeof(ILOffsetProjectionProducerTests).GetMethod(
                nameof(AddOne),
                BindingFlags.Static | BindingFlags.NonPublic)!;
        string path =
            typeof(ILOffsetProjectionProducerTests)
                .Assembly.Location;
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.None));
        using var source =
            SourceLinkService.OpenPrefetched(path);

        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    method.MetadataToken,
                    ILOffset: 0,
                    capability,
                    Analysis: AnalysisInput(execution)));

        Assert.False(outcome.Succeeded);
        Assert.Equal(expectedFailure, outcome.Failure!.Kind);
        Assert.Contains(
            "was not requested",
            outcome.Failure.Message,
            StringComparison.Ordinal);
    }

    static async Task<int> CallVirtualAsync(
        OffsetVirtualTarget target)
    {
        await Task.Yield();
        return target.Compute();
    }

    class OffsetVirtualTarget
    {
        public virtual int Compute() => 1;
    }

    static LibraryBodyAnalysisExecution AnalyzePath(string path) =>
        LibraryBodyAnalysisService.ExecutePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence));

    static ILOffsetAnalysisInput AnalysisInput(
        LibraryBodyAnalysisExecution execution) =>
        new(
            execution.Allocations,
            execution.Safety,
            execution.CallGraph);

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader metadata = peReader.GetMetadataReader();
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            metadata);
    }
}
