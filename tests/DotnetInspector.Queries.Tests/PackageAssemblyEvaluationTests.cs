using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using ILInspector.Analysis;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageAssemblyEvaluationTests
{
    const string Framework = "net11.0";
    const string Marker = "shared-literal-use-marker";
    static byte[] Image => File.ReadAllBytes(FixtureCatalog.AnalysisStringLiterals.AssemblyPath());

    [Fact]
    public async Task PrimaryAssetIsSelectorIssuedAndAssetScoped()
    {
        byte[] image = Image;
        var content = Content(
            ("lib/net11.0/A.Primary.dll", image),
            ("lib/net11.0/Z.Other.dll", image));
        PackageRootBinding binding = Binding(content);
        PackageRootReacquisitionRequest opening = binding.CreateReacquisitionRequest();

        var result = Assert.IsType<PackageAssemblyEvaluationOutcome.Matched>(
            await Evaluate(binding));

        Assert.Equal(["lib/net11.0/A.Primary.dll"], content.OpenedEntries);
        Assert.Equal(opening, result.Subject.RootRequest);
        Assert.Same(binding.ContentGenerationIdentity, result.Subject.ContentGeneration);
        Assert.Same(binding.SelectionIdentity, result.Subject.Selection);
        Assert.NotNull(result.SelectedAsset);
        Assert.Equal("lib/net11.0/A.Primary.dll", result.SelectedAsset.Asset.Path.ToString());
        Assert.Equal(PackageAssemblyAssetSequence.Implementation, result.SelectedAsset.Occurrence.Sequence);
        Assert.Equal(0, result.SelectedAsset.Occurrence.Ordinal);
        Assert.Equal(1, result.SelectedAsset.UnevaluatedSiblings);
        Assert.Equal(2, result.Evidence.Occurrences.Length);
        Assert.True(PackageRootReacquisitionRequest.TryDecode(opening.Encode(), out var decoded));
        Assert.Equal(opening, decoded);
    }

    [Fact]
    public async Task ImplementationUsesExactSelectedCounterpart()
    {
        byte[] image = Image;
        var content = Content(
            ("ref/net11.0/A.Primary.dll", image),
            ("lib/net11.0/A.Primary.dll", image),
            ("runtimes/linux-x64/lib/net11.0/A.Primary.dll", image),
            ("lib/net11.0/Z.Other.dll", image));
        PackageRootBinding binding = Binding(content, runtime: "linux-x64");

        var result = Assert.IsType<PackageAssemblyEvaluationOutcome.Matched>(
            await Evaluate(binding));

        Assert.Equal(["runtimes/linux-x64/lib/net11.0/A.Primary.dll"], content.OpenedEntries);
        Assert.NotNull(result.SelectedAsset);
        Assert.Equal(1, result.SelectedAsset.UnevaluatedSiblings);
        Assert.Equal("linux-x64", result.Subject.RootRequest.SelectionRuntimeIdentifier);
    }

    [Theory]
    [InlineData("readme.txt", "net11.0", PackageAssemblyNotApplicableReason.NoCompileAssets)]
    [InlineData("lib/net11.0/A.Primary.dll", "net8.0", PackageAssemblyNotApplicableReason.NoMatchingTargetFramework)]
    [InlineData("ref/net11.0/_._", "net11.0", PackageAssemblyNotApplicableReason.EmptyCompileGroup)]
    [InlineData("ref/net11.0/A.Primary.dll", "net11.0", PackageAssemblyNotApplicableReason.NoImplementationCounterpart)]
    public async Task MissingRoleIsDistinctFromNoMatch(
        string entry, string framework, PackageAssemblyNotApplicableReason reason)
    {
        byte[] image = Image;
        var content = entry.EndsWith("/_._", StringComparison.Ordinal)
            ? Content((entry, []), ("lib/net11.0/A.Primary.dll", image))
            : Content((entry, image));
        PackageRootBinding binding = Binding(content, framework);

        var result = Assert.IsType<PackageAssemblyEvaluationOutcome.NotApplicable>(
            await Evaluate(binding));

        Assert.Equal(reason, result.Reason);
        Assert.Empty(content.OpenedEntries);
        Assert.Equal(binding.CreateReacquisitionRequest(), result.Subject.RootRequest);
        Assert.Equal(reason == PackageAssemblyNotApplicableReason.NoImplementationCounterpart,
            result.SelectedAsset is not null);
    }

    [Fact]
    public async Task SemanticNoMatchIsConfirmedRatherThanAHeapSearch()
    {
        var result = Assert.IsType<PackageAssemblyEvaluationOutcome.NoMatch>(
            await Evaluate(Binding(Content(("lib/net11.0/A.Primary.dll", Image))),
                "literal-marker-present-only-as-a-constant"));

        Assert.Equal(PackageAssemblyNoMatchKind.SemanticallyConfirmed, result.Kind);
        Assert.True(result.Receipt.MethodsVisited > 0);
        Assert.True(result.Receipt.MethodBodiesVisited > 0);
    }

    [Fact]
    public async Task ImageAdmissionFailureIsNotASemanticMiss()
    {
        var result = Assert.IsType<PackageAssemblyEvaluationOutcome.Failure>(
            await Evaluate(Binding(Content(("lib/net11.0/A.Primary.dll", [1, 2, 3])))));

        Assert.Equal(PackageAssemblyFailureStage.ImageAdmission, result.Reason.Stage);
        Assert.NotNull(result.SelectedAsset);
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task ExactSelectedEntryAndAggregateLimitsAreAdmitted()
    {
        byte[] image = Image;
        var content = Content(("lib/net11.0/A.Primary.dll", image));
        var budget = new PackageAssemblyEvaluationBudget(
            image.Length, image.Length * 2L,
            StringLiteralUsePatternBudget.Default, TimeSpan.FromMinutes(1));
        Assert.IsType<PackageAssemblyEvaluationOutcome.Matched>(
            await Evaluate(Binding(content), budget: budget));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectedEntryOrAggregateLimitRemainsTyped(bool entryLimit)
    {
        byte[] image = Image;
        var content = Content(("lib/net11.0/A.Primary.dll", image));
        var budget = new PackageAssemblyEvaluationBudget(
            image.Length - (entryLimit ? 1 : 0),
            image.Length * 2L - (entryLimit ? 0 : 1),
            StringLiteralUsePatternBudget.Default, TimeSpan.FromMinutes(1));

        var result = Assert.IsType<PackageAssemblyEvaluationOutcome.Failure>(
            await Evaluate(Binding(content), budget: budget));

        Assert.IsType<PackageAssemblyFailureReason.EntryByteLimit>(result.Reason);
        Assert.Empty(content.OpenedEntries);
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task MissingEntryRemainsTypedAndTheEmptyWorkspaceClosesNormally()
    {
        var content = Content(("lib/net11.0/A.Primary.dll", Image));
        PackageRootBinding binding = Binding(content);
        content.RefuseOpen = true;

        var result = Assert.IsType<PackageAssemblyEvaluationOutcome.Failure>(
            await Evaluate(binding));

        Assert.IsType<PackageAssemblyFailureReason.EntryUnavailable>(result.Reason);
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task SemanticWorkLimitDiscardsProvisionalMatches()
    {
        var semantic = new StringLiteralUsePatternBudget(
            50_000, 1_000_000, 16_000_000, 4_000_000, 4_000_000, 1);
        var budget = new PackageAssemblyEvaluationBudget(
            16_000_000, 32_000_000, semantic, TimeSpan.FromMinutes(1));
        var result = Assert.IsType<PackageAssemblyEvaluationOutcome.Failure>(
            await Evaluate(Binding(Content(("lib/net11.0/A.Primary.dll", Image))), budget: budget));

        var limit = Assert.IsType<PackageAssemblyFailureReason.SemanticWorkLimit>(result.Reason);
        Assert.Equal(StringLiteralUseLimitKind.Occurrences, limit.Limit);
        Assert.Equal(1, limit.Receipt.OccurrencesRetained);
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task CancellationDoesNotPublishAnOutcome()
    {
        var content = Content(("lib/net11.0/A.Primary.dll", Image));
        PackageRootBinding binding = Binding(content);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PackageAssemblyEvaluator.EvaluateAsync(
                binding, Pattern(Marker), PackageAssemblyEvaluationBudget.Default, cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.False(PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup(failure, out _));
        Assert.Empty(content.OpenedEntries);
    }

    [Fact]
    public async Task UnexpectedAcquisitionExceptionRemainsTheExactPrimaryException()
    {
        var content = Content(("lib/net11.0/A.Primary.dll", Image));
        PackageRootBinding binding = Binding(content);
        var expected = new ApplicationException("fixture-only primary condition");
        content.OpenFailure = expected;

        Exception actual = await Assert.ThrowsAsync<ApplicationException>(() => Evaluate(binding));

        Assert.Same(expected, actual);
        Assert.False(PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup(actual, out _));
    }

    [Fact]
    public void ResultClosureIsResourceFree()
    {
        var seen = new HashSet<Type>();
        foreach (Type root in new[]
        {
            typeof(PackageAssemblyPatternDescriptor),
            typeof(PackageAssemblyPatternRequest),
            typeof(PackageAssemblyEvaluationBudget),
            typeof(PackageAssemblyEvaluationOutcome),
            typeof(PackageAssemblyFailureReason),
            typeof(PackageAssemblyEvaluationCleanupEvidence),
        })
        {
            Visit(root);
        }
        MethodInfo accessor = typeof(PackageAssemblyEvaluationExceptionEvidence)
            .GetMethod(nameof(PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup))!;
        Visit(accessor.GetParameters()[1].ParameterType.GetElementType()!);

        void Visit(Type type)
        {
            if (!seen.Add(type))
                return;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)
                || type == typeof(Guid) || type == typeof(TimeSpan) || type == typeof(InertString))
                return;
            if (type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
                    || type.GetGenericTypeDefinition() == typeof(Nullable<>)))
            {
                Visit(type.GetGenericArguments()[0]);
                return;
            }

            Assert.False(type.IsArray || type.IsByRefLike, type.FullName);
            Assert.False(typeof(IDisposable).IsAssignableFrom(type), type.FullName);
            Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type), type.FullName);
            Assert.False(typeof(Delegate).IsAssignableFrom(type), type.FullName);
            Assert.Contains(type.Assembly, new[]
            {
                typeof(PackageAssemblyEvaluator).Assembly,
                typeof(PackageRootBinding).Assembly,
                typeof(PackageContentGenerationIdentity).Assembly,
                typeof(StringLiteralUsePatternResult).Assembly,
                typeof(ArtifactAssemblyProjectionFailure).Assembly,
            });
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
                Visit(nested);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Visit(property.PropertyType);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Visit(field.FieldType);
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.ReturnType != typeof(void))
                    Visit(method.ReturnType);
                foreach (ParameterInfo parameter in method.GetParameters().Where(parameter => parameter.IsOut))
                    Visit(parameter.ParameterType.GetElementType()!);
            }
        }
    }

    [Fact]
    public void EmptyCloseReportIsValidOnlyBeforeOwnershipTransfer()
    {
        var report = new InspectionWorkspaceCloseReport([], []);
        Assert.Empty(PackageAssemblyEvaluator.DescribeClose(report, transferred: false, closeFaulted: false));
        var failure = Assert.Single(
            PackageAssemblyEvaluator.DescribeClose(report, transferred: true, closeFaulted: false));
        Assert.Equal(PackageAssemblyCandidateCleanupStage.CloseReportContract, failure.Stage);
        Assert.Equal(1, failure.Count);
    }

    [Fact]
    public void CloseReportPreservesDistinctOwnerReleaseFailures()
    {
        var report = new InspectionWorkspaceCloseReport(
            [new InspectionWorkspaceDirectGroupCloseResult(0, new IOException("group fixture"))],
            [new IOException("artifact fixture")]);

        ImmutableArray<PackageAssemblyCandidateCleanupFailure> failures =
            PackageAssemblyEvaluator.DescribeClose(report, transferred: true, closeFaulted: true);

        Assert.Equal(
            [PackageAssemblyCandidateCleanupStage.GroupRelease,
             PackageAssemblyCandidateCleanupStage.ArtifactSessionRelease,
             PackageAssemblyCandidateCleanupStage.CloseOrchestration],
            failures.Select(failure => failure.Stage));
        Assert.All(failures, failure => Assert.Equal(1, failure.Count));
    }

    [Fact]
    public void UnexpectedPreTransferReportIsNotReinterpretedAsAnOwnedRelease()
    {
        var report = new InspectionWorkspaceCloseReport(
            [new InspectionWorkspaceDirectGroupCloseResult(0, null)],
            [new IOException("unexpected artifact fixture")]);
        var failure = Assert.Single(
            PackageAssemblyEvaluator.DescribeClose(report, transferred: false, closeFaulted: false));
        Assert.Equal(PackageAssemblyCandidateCleanupStage.CloseReportContract, failure.Stage);
        Assert.Equal(2, failure.Count);
    }

    [Fact]
    public void CleanupAccessorPreservesPrimaryExceptionAndOmitsEmptyAttachments()
    {
        var primary = new IOException("primary fixture");
        primary.Data["existing-fixture-data"] = 42;
        PackageAssemblyEvaluationExceptionEvidence.Attach(primary, new(null, []));
        Assert.False(PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup(primary, out _));

        var cleanup = new PackageAssemblyEvaluationCleanupEvidence(null,
            [new(PackageAssemblyCandidateCleanupStage.ArtifactSessionRelease, 1)]);
        PackageAssemblyEvaluationExceptionEvidence.Attach(primary, cleanup);

        Assert.True(PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup(primary, out var attached));
        Assert.Same(cleanup, attached);
        Assert.Equal(42, primary.Data["existing-fixture-data"]);
        Assert.Null(attached.ProjectionCleanup);
    }

    static PackageAssemblyPatternRequest Pattern(string operand) =>
        PackageAssemblyPatterns.CreateRequest(PackageAssemblyPatterns.StringLiteralContains, operand);

    static Task<PackageAssemblyEvaluationOutcome> Evaluate(
        PackageRootBinding binding, string operand = Marker,
        PackageAssemblyEvaluationBudget? budget = null) =>
        PackageAssemblyEvaluator.EvaluateAsync(binding, Pattern(operand),
            budget ?? PackageAssemblyEvaluationBudget.Default, TestContext.Current.CancellationToken);

    static PackageRootBinding Binding(
        TrackingContent content, string framework = Framework, string? runtime = null) =>
        PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create("Unrelated.Package", "1.0.0"),
                content, content.ProducerKey, PackagePayloadOrigin.Download),
            framework, runtime);

    static TrackingContent Content(params (string Path, byte[] Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] bytes) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(bytes);
            }
        }
        return new(new InMemoryPackageContent(buffer.ToArray(), false, "tests"));
    }

    sealed class TrackingContent(InMemoryPackageContent content) : IPackageContent, IPackageContentEntryManifest
    {
        public List<string> OpenedEntries { get; } = [];
        public bool RefuseOpen { get; set; }
        public Exception? OpenFailure { get; set; }
        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => content.FromCache;
        public string ProducerKey => content.ProducerKey;
        public PackageContentGenerationIdentity GenerationIdentity => content.GenerationIdentity;
        public bool RequiresArchiveTreeMatch => false;
        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream) => content.TryOpenArchive(out stream);
        public bool TryOpenEntry(string path, [NotNullWhen(true)] out Stream? stream) =>
            TryOpenEntry(path, long.MaxValue, out stream);
        public bool TryOpenEntry(string path, long limit, [NotNullWhen(true)] out Stream? stream)
        {
            OpenedEntries.Add(path);
            if (OpenFailure is { } failure)
                throw failure;
            if (RefuseOpen)
            {
                stream = null;
                return false;
            }
            return content.TryOpenEntry(path, limit, out stream);
        }
        public IEnumerable<string> EnumerateEntries() => content.EnumerateEntries();
        public bool TryGetEntryLength(string path, out long length) => content.TryGetEntryLength(path, out length);
        public IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths() => content.EnumerateEntriesWithLengths();
    }
}
