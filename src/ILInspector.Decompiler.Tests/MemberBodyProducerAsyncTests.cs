using System.Reflection;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class MemberBodyProducerAsyncTests
{
    [Fact]
    public void ClassicAsyncStageFailureFlowsToBodyFailure()
    {
        var block = new Block(0);
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "C"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            ClassicAsyncStageResult =
                new ClassicAsyncStageResult.Failed(
                    ClassicAsyncStage.Raised,
                    new ClassicAsyncFailure(
                        DiagnosticIds.InternalError,
                        "classic planning failed")),
        };

        DecompilerResult result =
            CSharpPrinter.Print(function, out _);

        Assert.Null(result.Output);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Id == DiagnosticIds.InternalError
                && diagnostic.Message
                    == "classic planning failed");

        foreach (bool legacyErrorAsSource in
            new[] { false, true })
        {
            DecompilerResult projection =
                MemberBodyProducer.CompleteTypeProjection(
                    new(
                        Text: null,
                        new MemberBodyProducer
                            .ClassicAsyncBodyFailureException(
                                result)),
                    "Synthetic.C",
                    legacyErrorAsSource);
            Assert.False(projection.Succeeded);
            Assert.Contains(
                projection.Diagnostics,
                static diagnostic =>
                    diagnostic.Id
                        == DiagnosticIds.InternalError
                    && diagnostic.Message
                        == "classic planning failed");
        }
    }

    [Fact]
    public void ClassicAsyncStageFailureStopsWholeTypeComposition()
    {
        using var source = MetadataSource.Open(
            ClassicFixturePath());
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures",
                "AwaitVoid"));
        ClassicAsyncRelationshipEvidence evidence =
            Assert.IsType<ClassicAsyncRelationshipEvidence>(
                function.ClassicAsyncRelationship);
        function.ClassicAsyncRelationship =
            evidence with
            {
                PlanningSession =
                    new FailedPlanningSession(),
            };
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(
            typeof(MemberBodyProducer).GetMethod(
                "DecompileFunction",
                BindingFlags.Static | BindingFlags.NonPublic));
        object?[] arguments =
        [
            source,
            function,
            new SortedSet<string>(),
            null,
            false,
            ClassicAsyncDeclarationDisposition.NoOpinion,
            false,
            false,
            null,
            false,
        ];

        TargetInvocationException exception =
            Assert.Throws<TargetInvocationException>(
                () => method.Invoke(null, arguments));
        var failure = Assert.IsType<MemberBodyProducer
            .ClassicAsyncBodyFailureException>(
                exception.InnerException);

        Assert.False(failure.Result.Succeeded);
        Assert.Contains(
            failure.Result.Diagnostics,
            static diagnostic =>
                diagnostic.Id == DiagnosticIds.InternalError
                && diagnostic.Message
                    == "classic planning failed");
    }

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

    sealed class FailedPlanningSession
        : IClassicAsyncPlanningSession
    {
        public ClassicAsyncPreparationResult Prepare(
            ClassicAsyncRelationshipEvidence evidence)
            => new ClassicAsyncPreparationResult.PlanningFailed(
                new(
                    DiagnosticIds.InternalError,
                    "classic planning failed"));
    }
}
