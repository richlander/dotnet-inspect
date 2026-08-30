using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class MemberBodyProducerAsyncTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclinedClassicAsyncWithoutAwait_OmitsAsyncModifier(
        bool invalidateMetadataToken)
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        string path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicAsync",
            configuration,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.dll"));
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures");
        if (invalidateMetadataToken)
        {
            var member = Assert.Single(
                type.Members,
                candidate => candidate.Name == "NoAwait");
            member.MetadataToken = 0x02000001;
        }

        var source = MemberBodyProducer.Project(type, path, pdbPath: null).Output;

        Assert.NotNull(source);
        Assert.Contains(
            "public static Task NoAwait()",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public static async Task NoAwait()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClassicAsyncDeclarationDispositionFlowsThroughDecompilerBodyResults()
    {
        string path = ClassicFixturePath();
        using var source = MetadataSource.Open(path);

        MemberBodyProductionResult reconstructed = Produce(
            source,
            "AwaitVoid");
        MemberBodyProductionResult declined = Produce(
            source,
            "AwaitVoidThenReturn");
        MemberBodyProductionResult rejected = Produce(
            source,
            "RejectedClassicClaim");

        Assert.IsType<ClassicAsyncOutcome.Reconstructed>(
            reconstructed.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.IncludeAsync,
            reconstructed.ClassicAsyncDeclarationDisposition);
        Assert.True(reconstructed.Body?.RequiresAsyncModifier);
        var decline = Assert.IsType<ClassicAsyncOutcome.Declined>(
            declined.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclineReason.UnrecognizedAwaiterProtocol,
            decline.Reason);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            declined.ClassicAsyncDeclarationDisposition);
        Assert.False(declined.Body?.RequiresAsyncModifier);
        Assert.Contains(
            "unsupported classic async state machine",
            declined.Body?.Source,
            StringComparison.Ordinal);
        var rejectedOutcome =
            Assert.IsType<ClassicAsyncOutcome.Declined>(
                rejected.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclineReason.RejectedRelationship,
            rejectedOutcome.Reason);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.OmitAsync,
            rejected.ClassicAsyncDeclarationDisposition);
        Assert.False(rejected.Body?.RequiresAsyncModifier);
        Assert.Contains(
            "return Task.CompletedTask;",
            rejected.Body?.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WholeTypeUsesDecidedClassicAsyncDeclarationDisposition()
    {
        string path = ClassicFixturePath();
        using var pe = new PEReader(File.OpenRead(path));
        var surface = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName
                == "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures");

        string? output = MemberBodyProducer.Project(
            type,
            path,
            pdbPath: null).Output;

        Assert.NotNull(output);
        Assert.Contains(
            "public static async Task AwaitVoid(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static Task<int> AwaitVoidThenReturn(",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public static async Task<int> AwaitVoidThenReturn(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static Task RejectedClassicClaim()",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public static async Task RejectedClassicClaim()",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "return Task.CompletedTask;",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "unsupported classic async state machine",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncIteratorNoOpinion_DoesNotInventAsyncModifier()
    {
        using var source = MetadataSource.Open(ClassicFixturePath());

        MemberBodyProductionResult result = Produce(
            source,
            "AsyncSequence");

        Assert.Null(result.ClassicAsyncOutcome);
        Assert.Equal(
            ClassicAsyncDeclarationDisposition.NoOpinion,
            result.ClassicAsyncDeclarationDisposition);
        Assert.False(result.Body?.RequiresAsyncModifier);
    }

    static MemberBodyProductionResult Produce(
        MetadataSource source,
        string methodName)
    {
        IrFunction function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures",
            methodName));
        var evidence = Assert.IsType<ClassicAsyncRelationshipEvidence>(
            function.ClassicAsyncRelationship);
        return MemberBodyProducer.ProduceBody(
            source,
            evidence.RequestedHost);
    }

    static string ClassicFixturePath()
    {
        string configuration = new DirectoryInfo(
            AppContext.BaseDirectory).Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "ILInspector.Decompiler.Fixtures.ClassicAsync",
            configuration,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.dll"));
    }
}
