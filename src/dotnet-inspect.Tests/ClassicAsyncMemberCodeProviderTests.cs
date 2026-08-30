using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using DotnetInspector.Views;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Fixtures.ClassicAsync;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class ClassicAsyncMemberCodeProviderTests
{
    [Theory]
    [InlineData("AwaitInLoopChecked", true)]
    [InlineData("AwaitWithGuardedThrow", false)]
    public void DecompiledSourceUsesClassicAsyncDeclarationDisposition(
        string methodName,
        bool expectedAsyncModifier)
    {
        string assemblyPath = typeof(AsyncFixtures).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(pe).Types,
            candidate => candidate.FullName == typeof(AsyncFixtures).FullName);
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name == methodName);
        var request = new MemberCodeProvider.Request(
            DecompiledSource: true,
            AnnotatedSource: false,
            CostOverlay: false,
            SemanticsOverlay: false,
            IL: false,
            Attributes: false,
            Calls: false,
            Callers: false,
            CallGraph: false,
            UnsafeOperations: false);

        var (_, code) = Assert.Single(MemberCodeProvider.Collect(
            type,
            [member],
            assemblyPath,
            overloadIndex: 0,
            request));

        Assert.NotNull(code.DecompiledResult?.Output);
        Assert.Equal(
            expectedAsyncModifier,
            code.RequiresAsyncBodyModifier);
    }

    [Fact]
    public void RejectedClassicClaimRendersAsNonAsyncTaskMethod()
    {
        string assemblyPath = typeof(AsyncFixtures).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(pe).Types,
            candidate => candidate.FullName
                == typeof(AsyncFixtures).FullName);
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name == "RejectedClassicClaim");
        var request = new MemberCodeProvider.Request(
            DecompiledSource: true,
            AnnotatedSource: false,
            CostOverlay: false,
            SemanticsOverlay: false,
            IL: false,
            Attributes: false,
            Calls: false,
            Callers: false,
            CallGraph: false,
            UnsafeOperations: false);

        var (_, code) = Assert.Single(MemberCodeProvider.Collect(
            type,
            [member],
            assemblyPath,
            overloadIndex: 0,
            request));
        DecompilerResult result = Assert.IsType<DecompilerResult>(
            code.DecompiledResult);
        var sections = new MemberCodeView();

        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            result.ClassicAsyncDeclarationDisposition);
        Assert.False(code.RequiresAsyncBodyModifier);
        Assert.True(ApiOutputFormatter.PopulateCSharpSections(
            sections,
            type,
            member,
            code));
        string rendered = sections.DecompiledSourceCode.Content;
        Assert.Contains(
            "Task RejectedClassicClaim()",
            rendered,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "async System.Threading.Tasks.Task RejectedClassicClaim()",
            rendered,
            StringComparison.Ordinal);
        Assert.Contains(
            "return Task.CompletedTask;",
            rendered,
            StringComparison.Ordinal);
    }
}
