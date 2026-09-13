using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

[Collection(AnalysisIndexCacheCollection.Name)]
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
        LibraryBodyIndex index =
            LibraryBodyIndex.Open(path);
        DirectCall call = Assert.Single(
            index.DirectCalls,
            call => call.Caller.MetadataToken
                    == sourceMethod.MetadataToken
                && call.EvidenceMethod != call.Caller
                && call.Callee.Name
                    == nameof(OffsetVirtualTarget.Compute));
        using var source = SourceLinkService.Open(path);

        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    call.EvidenceMethod.MetadataToken,
                    call.ILOffset,
                    ILOffsetProjectionCapabilities
                        .CostContext));

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
        LibraryBodyIndex baseline =
            LibraryBodyIndex.Open(sourcePath);
        DirectCall call = Assert.Single(
            baseline.DirectCalls,
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
            using var source = SourceLinkService.Open(assembly);

            File.WriteAllBytes(path, [0x01, 0x02, 0x03]);

            ILOffsetProjectionOutcome outcome =
                ResearchViews.ProjectILOffset(
                    new ILOffsetProjectionRequest(
                        source,
                        call.EvidenceMethod.MetadataToken,
                        call.ILOffset,
                        ILOffsetProjectionCapabilities
                            .CostContext,
                        Assembly: assembly));

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
        ResolvedAssemblyReference analysisAssembly =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(LibraryBodyIndex).Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "IL-offset analysis-generation test"));
        using var source = SourceLinkService.Open(sourceAssembly);

        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new ILOffsetProjectionRequest(
                    source,
                    sourceMethod.MetadataToken,
                    ILOffset: 0,
                    ILOffsetProjectionCapabilities.CostContext,
                    Assembly: analysisAssembly));

        Assert.False(outcome.Succeeded);
        Assert.Equal(
            ILOffsetProjectionFailureKind.CostAnalysisUnavailable,
            outcome.Failure!.Kind);
        Assert.Contains(
            "different module generations",
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

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader metadata = peReader.GetMetadataReader();
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            metadata);
    }
}
