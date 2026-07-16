using System.Reflection;
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
}
