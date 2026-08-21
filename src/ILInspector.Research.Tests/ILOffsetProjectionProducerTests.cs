using System.Reflection;
using ILInspector.Analysis;
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
}
