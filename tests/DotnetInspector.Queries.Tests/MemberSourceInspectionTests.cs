using System.Collections.Immutable;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        MemberDecompilationInspection_PathlessTargetUsesOnlySuppliedEvidence(
            bool supplyPdb)
    {
        TestAssembly assembly =
            TestAssembly.Create(
                fixture:
                    FixtureCatalog.DecompilerUnsafeLegacy);
        var (type, property) =
            assembly.MemberTarget(
                "Count",
                "SelectedAutoPropertySamples");
        ApiMember getter = Assert.Single(
            ApiMemberAccessors.Create(property, type),
            candidate => candidate.Name == "get_Count");
        AssemblyContextLibraryPortablePdb? portablePdb =
            supplyPdb
                ? new(
                    ImmutableArray.CreateRange(
                        File.ReadAllBytes(
                            assembly.PdbPath)),
                    new AssemblySourcePdbProvenance(
                        assembly.Assembly.Registration,
                        Identity: null,
                        Location: assembly.PdbPath,
                        Path: assembly.PdbPath,
                        SymbolServer: null))
                : null;
        using var host = QueryHost.WithoutPdb();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyMemberDecompilationEntry>
            inspection =
                await MemberSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    AssemblyMemberSourceRequest.From(
                        type,
                        getter),
                    host.Context,
                    portablePdb,
                    TestContext.Current.CancellationToken);

        var settled =
            Assert.IsType<
                AssemblyMemberDecompilationEntry.Settled>(
                    inspection.Content);
        Assert.Equal(
            CSharpDecompilationStatus.Available,
            settled.Attempt.Status);
        Assert.Equal(
            supplyPdb,
            settled.Attempt.PdbSupplied);
        CSharpBodyProjection body = Assert.Single(
            settled.Attempt.BodyProjections,
            projection =>
                projection.Address.Token
                    == getter.MetadataToken);
        Assert.True(body.ContributesToOutput);
        Assert.NotNull(body.PropertySource);
        Assert.NotNull(body.Projection.Output);
        var house =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                    settled.HouseOutcome);
        Assert.Same(settled.Attempt, house.Attempt);
        Assert.Equal(
            SourceHouseLibraryLeaseConsumer.SourceHouse,
            house.LeaseSettlement.Consumer);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(
            "member-decompilation/share",
            Assert.IsType<InspectionShare.NonProjectable>(
                inspection.Share).Path);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task
        MemberDecompilationInspection_InvalidSuppliedPdbIsTerminal()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(
                nameof(SourceFixture.Describe));
        var portablePdb =
            new AssemblyContextLibraryPortablePdb(
                [1, 2, 3, 4],
                new AssemblySourcePdbProvenance(
                    assembly.Assembly.Registration,
                    Identity: null,
                    Location: "invalid.pdb",
                    Path: null,
                    SymbolServer: null));
        using var host = QueryHost.WithoutPdb();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyMemberDecompilationEntry>
            inspection =
                await MemberSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    request,
                    host.Context,
                    portablePdb,
                    TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                AssemblyMemberDecompilationEntry.Unavailable>(
                    inspection.Content);
        Assert.Equal(
            AssemblySourceFailureKind.InspectionFailed,
            unavailable.Failure.Kind);
        Assert.IsType<
            AssemblyContextLibraryAdapterResult.PortablePdbRejected>(
                unavailable.LibraryFailure);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task
        MemberDecompilationInspection_TerminalLibraryAdmissionRetainsEvidence()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        AssemblyContextSourceQueryContext context =
            TerminalLibraryAdmissionContext(host);
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyMemberDecompilationEntry>
            inspection =
                await MemberSourceInspection.DecompileAsync(
                    group,
                    assembly.Participant,
                    assembly.MemberRequest(
                        nameof(SourceFixture.Describe)),
                    context,
                    cancellationToken:
                        TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<
                AssemblyMemberDecompilationEntry.Unavailable>(
                    inspection.Content);
        Assert.Equal(
            AssemblySourceFailureKind.InspectionFailed,
            unavailable.Failure.Kind);
        var terminal = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Incomplete>(
                unavailable.LibraryFailure);
        Assert.Equal(1, terminal.MaxCapturedImageBytes);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task
        MemberDecompilationInspection_CancellationPropagates()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        using var cancellation = new CancellationTokenSource();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MemberSourceInspection.DecompileAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(nameof(SourceFixture.Describe)),
                host.Context,
                cancellationToken: cancellation.Token));
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task MemberSourceInspection_SelectedAutoGetterUsesSharedStorageComposition()
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.DecompilerUnsafeLegacy);
        var (type, property) = assembly.MemberTarget("Count", "SelectedAutoPropertySamples");
        var getter = Assert.Single(ApiMemberAccessors.Create(property, type));
        var request = AssemblyMemberSourceRequest.From(type, getter);
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var envelope = await MemberSourceInspection.ExecuteAsync(
            group, assembly.Participant, request,
            host.Context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(envelope.Content);
        var source = Assert.IsType<AssemblyMemberSource.Decompiled>(available.Source);
        Assert.Contains("public int Count { get; }", source.Text);
        Assert.DoesNotContain("get_Count()", source.Text);
        Assert.DoesNotContain("this.Count", source.Text);
        SourceHouseDecompilationOutcome.Completed house =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationHouseOutcome);
        Assert.Equal(
            getter.MetadataToken,
            Assert.IsType<SourceHouseTarget.MemberTarget>(
                house.Request.Target)
                .MetadataToken);
        Assert.Same(source.Decompilation, house.Attempt);
        Assert.Empty(envelope.Diagnostics);
    }

    [Theory]
    [InlineData("SelectedFieldPropertySamples", "field + 1")]
    [InlineData("FieldKeywordGetterSamples", "global::ILInspector.Decompiler.Fixtures.FieldKeyword.field.Keep(field)")]
    public async Task MemberSourceInspection_SelectedFieldGetterUsesSharedStorageComposition(
        string typeName, string expression)
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.DecompilerUnsafeLegacy);
        var (type, property) = assembly.MemberTarget("Count", typeName);
        var getter = Assert.Single(ApiMemberAccessors.Create(property, type));
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);
        var envelope = await MemberSourceInspection.ExecuteAsync(
            group, assembly.Participant, AssemblyMemberSourceRequest.From(type, getter),
            host.Context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(envelope.Content);
        var source = Assert.IsType<AssemblyMemberSource.Decompiled>(available.Source);
        Assert.Contains($"public int Count => {expression};", source.Text);
        Assert.DoesNotContain("get_Count()", source.Text);
        Assert.DoesNotContain("this.Count", source.Text);
        SourceHouseDecompilationOutcome.Completed house =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationHouseOutcome);
        Assert.Equal(
            getter.MetadataToken,
            Assert.IsType<SourceHouseTarget.MemberTarget>(
                house.Request.Target)
                .MetadataToken);
        Assert.Same(source.Decompilation, house.Attempt);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task MemberSourceInspection_SelectedInitializerCarriesItsScope()
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.DecompilerUnsafeLegacy);
        var (type, property) = assembly.MemberTarget("Value", "ConstructorGetterComputed");
        var getter = Assert.Single(ApiMemberAccessors.Create(property, type));
        using var host = QueryHost.WithoutPdb();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);
        var envelope = await MemberSourceInspection.ExecuteAsync(
            group, assembly.Participant, AssemblyMemberSourceRequest.From(type, getter),
            host.Context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(envelope.Content);
        var source = Assert.IsType<AssemblyMemberSource.Decompiled>(available.Source);
        Assert.Contains("struct ConstructorGetterComputed(int value)", source.Text);
        Assert.Contains("get => field + 1;", source.Text);
        Assert.Contains("} = value;", source.Text);
        var body = Assert.Single(source.Decompilation.BodyProjections, projection => projection.ContributesToOutput);
        Assert.Equal(getter.MetadataToken, body.Address.Token);
        Assert.Equal(2, source.Decompilation.BodyProjectionsAttempted);
        Assert.Empty(envelope.Diagnostics);
    }

    // These member-level production outcomes are PR-fast.
    [Fact]
    public async Task MemberSourceInspection_RealRepositoryAuthoredResultIsDetached()
    {
        string path = typeof(CSharpText.MemberSlicing.MemberTextSlicer).Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly = TestAssembly.CreatePackage(File.ReadAllBytes(path), pdbPath);
        using var host = QueryHost.WithPdb(pdbPath, File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs")),
            maxDecompilerBodyProjections: 0);
        InspectionEnvelope<AssemblyMemberSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([assembly.Participant]);
            inspection = await MemberSourceInspection.ExecuteAsync(
                group, assembly.Participant,
                assembly.MemberRequest("ExtractMemberText", "MemberTextSlicer"),
                host.Context, TestContext.Current.CancellationToken);
        }

        var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(inspection.Content);
        var pdb = Assert.IsType<AssemblyMemberSource.Pdb>(available.Source);
        var house = Assert.IsType<SourceHouseOutcome.Available>(available.HouseOutcome);
        Assert.StartsWith("public static string? ExtractMemberText(", pdb.Text.TrimStart());
        Assert.Equal(pdb.Text, house.Source.Text);
        Assert.Null(pdb.MemberDocument);
        Assert.Null(house.Source.MemberDocument);
        Assert.Equal(SourceChecksumVerification.Exact, pdb.Inspection.ChecksumVerification);
        Assert.Equal(SourceHouseLibraryLeaseConsumer.SourceHouse, house.Receipt.LeaseSettlement.Consumer);
        Assert.Equal(assembly.Assembly.Registration, available.Subject.Registration);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.Equal("member-source/share",
            Assert.IsType<InspectionShare.NonProjectable>(inspection.Share).Path);
        Assert.Empty(inspection.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberSourceInspection_ChecksumFailureRetainsSymbolsForFallback(bool embedded)
    {
        TestAssembly assembly = embedded
            ? TestAssembly.Create(File.ReadAllBytes(typeof(EmbeddedSourceFixture).Assembly.Location))
            : TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = embedded
            ? QueryHost.WithSource("not the checksum-verified declaration"u8.ToArray())
            : QueryHost.WithPdb(assembly.PdbPath, "not the checksum-verified declaration"u8.ToArray());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var envelope = await MemberSourceInspection.ExecuteAsync(
            group, assembly.Participant,
            embedded ? assembly.MemberRequest("Echo", "EmbeddedSourceFixture")
                : assembly.MemberRequest("Value", "Counter"),
            host.Context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(envelope.Content);
        var source = Assert.IsType<AssemblyMemberSource.Decompiled>(available.Source);
        Assert.Contains(embedded ? "Echo" : "Value", source.Text);
        Assert.True(source.Decompilation.PdbSupplied);
        Assert.Equal(PdbMemberSourceOutcome.ChecksumMismatch, source.PdbAttempt.Outcome);
        Assert.IsType<FindingInspection<string>.Failed>(source.PdbAttempt.Lines.Value);
        Assert.IsType<SourceHouseOutcome.Failed>(available.HouseOutcome);
        SourceHouseDecompilationOutcome.Completed decompiled =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationHouseOutcome);
        Assert.Same(source.Decompilation, decompiled.Attempt);
        Assert.Equal(
            embedded
                ? SourceHousePdbContributionKind.Embedded
                : SourceHousePdbContributionKind.SuppliedCompanion,
            decompiled.PdbContribution.Kind);
        if (embedded)
            Assert.Empty(host.SymbolRequests);
        else
            Assert.Single(host.SymbolRequests, uri => uri.AbsolutePath.EndsWith(".snupkg"));
        Assert.Single(host.SourceRequests);
    }

    [Fact]
    public async Task MemberSourceInspection_AuthoredOnlyDeclarationDoesNotDecompile()
    {
        TestAssembly assembly =
            TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            "not the checksum-verified declaration"u8.ToArray());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([assembly.Participant]);
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest("Value", "Counter")
                .WithoutDecompiledFallback();

        InspectionEnvelope<AssemblyMemberSourceEntry> inspection =
            await MemberSourceInspection.ExecuteAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
                inspection.Content);
        Assert.False(unavailable.Request.IncludeAuthoredParts);
        Assert.False(unavailable.Request.AllowDecompiledFallback);
        Assert.Equal(
            AssemblySourceFailureKind.AuthoredMemberUnavailable,
            unavailable.Failure.Kind);
        Assert.Null(unavailable.DecompiledAttempt);
        Assert.Null(unavailable.DecompilationHouseOutcome);
        Assert.Equal(
            PdbMemberSourceOutcome.ChecksumMismatch,
            unavailable.PdbAttempt!.Outcome);
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("source-bytes")]
    [InlineData("assembly")]
    public async Task MemberSourceInspection_BoundsRetainFailureAndFallback(string boundary)
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(assembly.PdbPath, SourcePairBytes(FixtureCatalog.SourceDiffV1));
        SourceHouseLimits defaults = host.Context.MemberSourceLimits;
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            MemberSourceTimeout = boundary == "deadline" ? TimeSpan.FromTicks(1) : TimeSpan.FromMinutes(5),
            MemberSourceLimits = new(
                boundary == "assembly" ? 1 : defaults.MaximumAssemblyBytes,
                boundary == "assembly" ? 1 : defaults.MaximumPortablePdbBytes,
                defaults.TargetBounds, defaults.SourceLinkReadLimits,
                defaults.MaximumDocuments, defaults.MaximumTargetMappings, defaults.MaximumCandidateAttempts,
                boundary == "source-bytes" ? 1 : defaults.MaximumSourceBytes,
                defaults.MaximumSourceTextCharacters),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);

        var envelope = await MemberSourceInspection.ExecuteAsync(
            group, assembly.Participant, assembly.MemberRequest("Value", "Counter"),
            context, TestContext.Current.CancellationToken);

        var available = Assert.IsType<AssemblyMemberSourceEntry.Available>(envelope.Content);
        var source = Assert.IsType<AssemblyMemberSource.Decompiled>(available.Source);
        Assert.True(source.Decompilation.PdbSupplied);
        Assert.Equal(boundary == "deadline"
            ? PdbMemberSourceOutcome.SourceDeadlineExceeded
            : PdbMemberSourceOutcome.SourceLimitExceeded, source.PdbAttempt.Outcome);
        Assert.IsType<FindingInspection<string>.Failed>(source.PdbAttempt.Lines.Value);
        if (boundary == "assembly")
        {
            Assert.Null(available.LibraryFailure);
            Assert.Equal(
                SourceHouseIncompleteBoundary.AssemblyBytes,
                Assert.IsType<SourceHouseOutcome.Incomplete>(
                    available.HouseOutcome).Boundary);
        }
        else
        {
            Assert.Equal(boundary == "deadline"
                ? SourceHouseIncompleteBoundary.Deadline
                : SourceHouseIncompleteBoundary.SourceBytes,
                Assert.IsType<SourceHouseOutcome.Incomplete>(available.HouseOutcome).Boundary);
        }
        SourceHouseDecompilationOutcome.Completed decompiled =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                available.DecompilationHouseOutcome);
        Assert.Same(
            source.Decompilation,
            decompiled.Attempt);
    }

    [Fact]
    public async Task
        MemberSourceInspection_TerminalLibraryAdmissionRetainsEvidence()
    {
        string path =
            typeof(CSharpText.MemberSlicing.MemberTextSlicer)
                .Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly =
            TestAssembly.CreatePackage(
                File.ReadAllBytes(path),
                pdbPath);
        using var host = QueryHost.WithPdb(
            pdbPath,
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "LibraryAdapter",
                    "MemberTextSlicer.cs")));
        AssemblyContextSourceQueryContext context =
            TerminalLibraryAdmissionContext(host);
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(
                "ExtractMemberText",
                "MemberTextSlicer");
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyMemberSourceEntry> inspection =
            await MemberSourceInspection.ExecuteAsync(
                group,
                assembly.Participant,
                request,
                context,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
                inspection.Content);
        Assert.Equal(
            AssemblySourceFailureKind.InspectionFailed,
            unavailable.Failure.Kind);
        Assert.Null(unavailable.Failure.Error);
        Assert.Contains(
            "terminal Library admission",
            unavailable.Failure.Detail,
            StringComparison.Ordinal);
        var terminal = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Incomplete>(
                unavailable.LibraryFailure);
        Assert.Equal(1, terminal.MaxCapturedImageBytes);
        Assert.Equal(
            PdbMemberSourceOutcome.SourceLimitExceeded,
            unavailable.PdbAttempt!.Outcome);
        Assert.IsType<FindingInspection<string>.Failed>(
            unavailable.PdbAttempt.Lines.Value);
        Assert.Null(unavailable.HouseOutcome);
        Assert.Null(unavailable.DecompiledAttempt);
        Assert.Null(unavailable.DecompilationHouseOutcome);
        Assert.Empty(inspection.Diagnostics);

        InspectionEnvelope<AssemblyMemberSourceEntry> authoredOnly =
            await MemberSourceInspection.ExecuteAsync(
                group,
                assembly.Participant,
                request.WithoutDecompiledFallback(),
                context,
                TestContext.Current.CancellationToken);
        var authoredUnavailable =
            Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
                authoredOnly.Content);
        Assert.IsType<
            AssemblyContextLibraryAdapterResult.Incomplete>(
                authoredUnavailable.LibraryFailure);
        Assert.Equal(
            PdbMemberSourceOutcome.SourceLimitExceeded,
            authoredUnavailable.PdbAttempt!.Outcome);
        Assert.Null(authoredUnavailable.DecompiledAttempt);
        Assert.Null(
            authoredUnavailable.DecompilationHouseOutcome);
    }

    [Fact]
    public async Task
        MemberSourceComparison_TerminalLibraryAdmissionRetainsEvidence()
    {
        string path =
            typeof(CSharpText.MemberSlicing.MemberTextSlicer)
                .Assembly.Location;
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        TestAssembly assembly =
            TestAssembly.CreatePackage(
                File.ReadAllBytes(path),
                pdbPath);
        using var host = QueryHost.WithPdb(
            pdbPath,
            File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "LibraryAdapter",
                    "MemberTextSlicer.cs")));
        AssemblyContextSourceQueryContext context =
            TerminalLibraryAdmissionContext(host);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<AssemblyMemberSourceComparisonEntry>
            inspection =
                await MemberSourceInspection.CompareAsync(
                    group,
                    assembly.Participant,
                    assembly.MemberRequest(
                        "ExtractMemberText",
                        "MemberTextSlicer"),
                    context,
                    TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<
                AssemblyMemberSourceComparisonEntry.Failed>(
                    inspection.Content);
        Assert.Equal(
            AssemblySourceFailureKind.InspectionFailed,
            failed.Failure.Kind);
        Assert.Null(failed.Failure.Error);
        Assert.Contains(
            "terminal Library admission",
            failed.Failure.Detail,
            StringComparison.Ordinal);
        var pdbAttempt =
            Assert.IsType<
                AssemblyMemberPdbSourceAttempt.Unavailable>(
                    failed.PdbAttempt);
        var terminal = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Incomplete>(
                pdbAttempt.LibraryFailure);
        Assert.Equal(1, terminal.MaxCapturedImageBytes);
        Assert.Equal(
            PdbMemberSourceOutcome.SourceLimitExceeded,
            pdbAttempt.Inspection.Outcome);
        Assert.IsType<FindingInspection<string>.Failed>(
            pdbAttempt.Inspection.Lines.Value);
        Assert.Null(pdbAttempt.HouseOutcome);
        Assert.Empty(inspection.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberSourceInspection_MemberAndPairBoundsAreIndependent(bool boundPair)
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);
        var context = new AssemblyContextSourceQueryContext(
            host.Context.SymbolClient, host.Context.PdbStore,
            host.Context.PackageSourceAuthorization, host.Context.SourceFetch)
        {
            MemberSourceTimeout = boundPair ? TimeSpan.FromMinutes(5) : TimeSpan.FromTicks(1),
            MemberSourcePairTimeout = boundPair ? TimeSpan.FromTicks(1) : TimeSpan.FromMinutes(5),
        };
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([before.Participant]);
        var member = await MemberSourceInspection.ExecuteAsync(
            group, before.Participant, before.MemberRequest("Value", "Counter"),
            context, TestContext.Current.CancellationToken);
        var pair = await ExecuteSourcePairAsync(
            before, after, "Value", host, sourceContext: context);
        var type = await AssemblyContextSourceQuery.ExecuteTypeAsync(
            group, before.Participant, before.TypeRequest("Counter"),
            context, TestContext.Current.CancellationToken);

        Assert.Equal(boundPair,
            Assert.IsType<AssemblyMemberSourceEntry.Available>(member.Content).Source is AssemblyMemberSource.Pdb);
        Assert.Equal(boundPair ? AssemblyMemberSourcePairStatus.Unavailable : AssemblyMemberSourcePairStatus.Compared,
            pair.Status);
        Assert.IsType<AssemblyTypeSource.Pdb>(
            Assert.IsType<AssemblyTypeSourceEntry.Available>(type).Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberSourceComparisonInspection_IsDetachedAndReusesPdb(bool failSource)
    {
        TestAssembly assembly = TestAssembly.Create(fixture: FixtureCatalog.SourceDiffV1);
        using var host = QueryHost.WithPdb(assembly.PdbPath,
            failSource ? "different declaration"u8.ToArray() : SourcePairBytes(FixtureCatalog.SourceDiffV1));
        InspectionEnvelope<AssemblyMemberSourceComparisonEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([assembly.Participant]);
            inspection = await MemberSourceInspection.CompareAsync(
                group, assembly.Participant, assembly.MemberRequest("Value", "Counter"),
                host.Context, TestContext.Current.CancellationToken);
        }

        var available = Assert.IsType<AssemblyMemberSourceComparisonEntry.Available>(inspection.Content);
        var decompiled = Assert.IsType<AssemblyMemberDecompiledSourceAttempt.Available>(available.Decompiled);
        Assert.True(decompiled.Result.PdbSupplied);
        SourceHouseDecompilationOutcome.Completed decompiledHouse =
            Assert.IsType<
                SourceHouseDecompilationOutcome.Completed>(
                decompiled.HouseOutcome);
        Assert.Same(decompiled.Result, decompiledHouse.Attempt);
        Assert.Equal(
            SourceHousePdbContributionKind.SuppliedCompanion,
            decompiledHouse.PdbContribution.Kind);
        if (failSource)
        {
            var pdb = Assert.IsType<AssemblyMemberPdbSourceAttempt.Unavailable>(available.Pdb);
            Assert.Equal(PdbMemberSourceOutcome.ChecksumMismatch, pdb.Inspection.Outcome);
            Assert.IsType<SourceHouseOutcome.Failed>(pdb.HouseOutcome);
        }
        else
        {
            var pdb = Assert.IsType<AssemblyMemberPdbSourceAttempt.Available>(available.Pdb);
            Assert.Equal(pdb.Inspection.Text,
                Assert.IsType<SourceHouseOutcome.Available>(pdb.HouseOutcome).Source.Text);
        }
        Assert.Single(host.SymbolRequests, uri => uri.AbsolutePath.EndsWith(".snupkg"));
        Assert.Single(host.SourceRequests);
        Assert.Equal("member-source-comparison/share",
            Assert.IsType<InspectionShare.NonProjectable>(inspection.Share).Path);
        Assert.Empty(inspection.Diagnostics);
    }

    private static AssemblyContextSourceQueryContext
        TerminalLibraryAdmissionContext(QueryHost host)
    {
        SourceHouseLimits authored =
            host.Context.MemberSourceLimits;
        SourceHouseDecompilationLimits decompiled =
            host.Context.MemberDecompilationLimits;
        return new(
            host.Context.SymbolClient,
            host.Context.PdbStore,
            host.Context.PackageSourceAuthorization,
            host.Context.SourceFetch)
        {
            MemberSourceLimits = new(
                maximumAssemblyBytes: 1,
                maximumPortablePdbBytes: 1,
                authored.TargetBounds,
                authored.SourceLinkReadLimits,
                authored.MaximumDocuments,
                authored.MaximumTargetMappings,
                authored.MaximumCandidateAttempts,
                authored.MaximumSourceBytes,
                authored.MaximumSourceTextCharacters),
            MemberDecompilationLimits = new(
                maximumAssemblyBytes: 1,
                maximumPortablePdbBytes: 1,
                decompiled.TargetBounds,
                decompiled.EmbeddedPdbReadLimits),
        };
    }

}
