using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;

using DotnetInspector.Packages;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextSourceQueryTests
{
    static readonly Guid SourceLinkKind =
        new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    [Fact]
    public void RequestFromLegacyApiType_RequiresUnambiguousMetadataName()
    {
        var simple = new ApiType
        {
            Namespace = "Sample",
            Name = "Widget",
            MetadataName = "Widget",
        };
        var ambiguous = new ApiType
        {
            Namespace = "Sample",
            Name = "Inner",
            MetadataName = "Outer+Inner",
        };

        AssemblyTypeSourceRequest request =
            AssemblyTypeSourceRequest.From(simple);

        Assert.Equal(
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Widget"]))
                .Name,
            request.Type);
        Assert.Throws<ArgumentException>(
            () => AssemblyTypeSourceRequest.From(ambiguous));
    }

    [Fact]
    public async Task PathlessMember_AcquiresVerifiedAuthoredSource()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var authored =
            Assert.IsType<AssemblyMemberSource.Authored>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            authored.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            authored.Inspection.ChecksumVerification);
        Assert.NotNull(authored.Inspection.Mapping);
        Assert.NotNull(authored.Inspection.Document);
        Assert.Null(assembly.Assembly.Path);
        Assert.NotEmpty(host.SymbolRequests);
        Assert.NotEmpty(host.SourceRequests);
        Assert.IsType<
            AssemblyImageAccessResult<int>.Available>(
                group.UseAssemblySession(
                    assembly.Assembly,
                    static session =>
                        session.ApiSurface().Types.Count));
    }

    [Fact]
    public async Task LocalPdbSource_DoesNotRequireSourceLinkMap()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            RemoveSourceLinkCustomDebugInformation(
                assembly.PdbPath);
        using var host =
            QueryHost.WithPdb(
                Path.GetFileName(assembly.PdbPath),
                pdbBytes,
                sourceBytes: [],
                allowLocalSourceReads: true);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry memberResult =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);
        AssemblyTypeSourceEntry typeResult =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);

        var member =
            Assert.IsType<AssemblyMemberSource.Authored>(
                Assert.IsType<
                    AssemblyMemberSourceEntry.Available>(
                        memberResult)
                    .Source);
        var type =
            Assert.IsType<AssemblyTypeSource.Authored>(
                Assert.IsType<
                    AssemblyTypeSourceEntry.Available>(
                        typeResult)
                    .Source);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            member.Inspection.ChecksumVerification);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            type.Inspection.ChecksumVerification);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task MissingAuthoredSource_FallsBackToDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            decompiled.Text,
            StringComparison.Ordinal);
        Assert.IsType<FindingInspection<string>.Absent>(
            decompiled.AuthoredAttempt.Lines.Value);
        Assert.Empty(host.SourceRequests);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Fact]
    public async Task AuthoredIntegrityFailure_IsPreservedBesideDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            "not the compiled source"u8.ToArray());
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                available.Source);
        Assert.IsType<FindingInspection<string>.Failed>(
            decompiled.AuthoredAttempt.Lines.Value);
        Assert.Equal(
            SourceChecksumVerification.Mismatch,
            decompiled.AuthoredAttempt.ChecksumVerification);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            decompiled.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathlessType_AcquiresVerifiedAuthoredDocument()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceFixture).Name);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes());
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var authored =
            Assert.IsType<AssemblyTypeSource.Authored>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture),
            authored.Text,
            StringComparison.Ordinal);
        Assert.Equal(
            SourceChecksumVerification.Exact,
            authored.Inspection.ChecksumVerification);
    }

    [Fact]
    public async Task MissingAuthoredType_FallsBackToDecompiler()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceFixture).Name);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                available.Source);
        Assert.Contains(
            nameof(SourceFixture),
            decompiled.Text,
            StringComparison.Ordinal);
        Assert.True(decompiled.Decompilation.Succeeded);
        Assert.IsType<FindingInspection<string>.Absent>(
            decompiled.AuthoredAttempt.Lines.Value);
    }

    [Fact]
    public async Task PreCanceledQueries_StopBeforeSnapshotAndDecompilerFallback()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                cancellation.Token));

        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Fact]
    public async Task SameDescriptorForeignParticipant_IsRejectedBeforeCancellation()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        var foreignPolicy =
            new FrameworkBindingPolicy();
        var foreign =
            new AssemblyContextParticipant(
                assembly.Assembly,
                foreignPolicy);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        using var cancellation = new CancellationTokenSource();

        foreach (bool canceled in new[] { false, true })
        {
            if (canceled)
                cancellation.Cancel();

            AssemblyMemberSourceEntry member =
                await AssemblyContextSourceQuery
                    .ExecuteMemberAsync(
                        group,
                        foreign,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        cancellation.Token);
            AssemblyTypeSourceEntry type =
                await AssemblyContextSourceQuery
                    .ExecuteTypeAsync(
                        group,
                        foreign,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        cancellation.Token);

            Assert.IsType<ArgumentException>(
                Assert.IsType<
                    AssemblyMemberSourceEntry.Unavailable>(
                        member)
                    .Failure.Error);
            Assert.IsType<ArgumentException>(
                Assert.IsType<
                    AssemblyTypeSourceEntry.Unavailable>(
                        type)
                    .Failure.Error);
        }

        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.Equal(0, foreignPolicy.SelectionCount);
    }

    [Fact]
    public async Task ChangedBindingPolicySnapshot_IsRejectedBeforeCancellation()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        assembly.Policy.ChangeVersion();
        using var cancellation = new CancellationTokenSource();

        foreach (bool canceled in new[] { false, true })
        {
            if (canceled)
                cancellation.Cancel();

            AssemblyMemberSourceEntry member =
                await AssemblyContextSourceQuery
                    .ExecuteMemberAsync(
                        group,
                        assembly.Participant,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        cancellation.Token);
            AssemblyTypeSourceEntry type =
                await AssemblyContextSourceQuery
                    .ExecuteTypeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        cancellation.Token);

            Assert.IsType<InvalidOperationException>(
                Assert.IsType<
                    AssemblyMemberSourceEntry.Unavailable>(
                        member)
                    .Failure.Error);
            Assert.IsType<InvalidOperationException>(
                Assert.IsType<
                    AssemblyTypeSourceEntry.Unavailable>(
                        type)
                    .Failure.Error);
        }

        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Fact]
    public async Task BindingPolicyCancellation_PropagatesFromDecompilerFallback()
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);
        assembly.Policy.CancelSelection = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken));

        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingPolicyVersionChangeDuringPdbAcquisition_IsRejected(
        bool typeQuery)
    {
        TestAssembly assembly = TestAssembly.Create();
        var pdbStore =
            new ThrowingPdbStore(
                assembly.Policy.ChangeVersion);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Exception error;
        if (typeQuery)
        {
            var result =
                Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
                    await AssemblyContextSourceQuery.ExecuteTypeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        TestContext.Current.CancellationToken));
            error = result.Failure.Error!;
        }
        else
        {
            var result =
                Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
                    await AssemblyContextSourceQuery.ExecuteMemberAsync(
                        group,
                        assembly.Participant,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        TestContext.Current.CancellationToken));
            error = result.Failure.Error!;
        }

        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(1, pdbStore.ReadAttempts);
        Assert.Equal(0, assembly.Policy.SelectionCount);
        Assert.Empty(host.SourceRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingPolicyVersionChangeDuringFallback_IsRejected(
        bool typeQuery)
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        assembly.Policy.BeforeSelection =
            assembly.Policy.ChangeVersion;
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        Exception error;
        if (typeQuery)
        {
            var result =
                Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
                    await AssemblyContextSourceQuery.ExecuteTypeAsync(
                        group,
                        assembly.Participant,
                        assembly.TypeRequest(
                            typeof(SourceFixture).Name),
                        host.Context,
                        TestContext.Current.CancellationToken));
            error = result.Failure.Error!;
        }
        else
        {
            var result =
                Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
                    await AssemblyContextSourceQuery.ExecuteMemberAsync(
                        group,
                        assembly.Participant,
                        assembly.MemberRequest(
                            nameof(SourceFixture.Describe)),
                        host.Context,
                        TestContext.Current.CancellationToken));
            error = result.Failure.Error!;
        }

        Assert.IsType<InvalidOperationException>(error);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedDescriptorCancellation_PropagatesFromFallback(
        bool typeQuery)
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        AssemblyReferenceIdentity coreLibraryIdentity =
            ReadIdentity(
                File.ReadAllBytes(
                    typeof(object).Assembly.Location));
        int opens = 0;
        assembly.Policy.SelectOverride =
            request =>
                AssemblyBindingSelection.Found(
                    ResolvedAssemblyReference.Create(
                        request.Target
                            is AssemblyBindingTarget.AssemblyReference reference
                            ? reference.Identity
                            : coreLibraryIdentity,
                        path: null,
                        () =>
                        {
                            Interlocked.Increment(ref opens);
                            throw new OperationCanceledException(
                                "Synthetic selected-descriptor cancellation.");
                        },
                        AssemblyResolutionProvenance.Local(
                            "source query cancellation test")));
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        try
        {
            if (typeQuery)
            {
                await AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    TestContext.Current.CancellationToken);
            }
            else
            {
                await AssemblyContextSourceQuery.ExecuteMemberAsync(
                    group,
                    assembly.Participant,
                    assembly.MemberRequest(
                        nameof(SourceFixture.Describe)),
                    host.Context,
                    TestContext.Current.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Assert.True(opens > 0);
            Assert.True(assembly.Policy.SelectionCount > 0);
            return;
        }

        Assert.Fail(
            $"Expected selected-descriptor cancellation; opens={opens}, selections={assembly.Policy.SelectionCount}.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingPolicyRequestedTokenCancellation_StopsFallback(
        bool typeQuery)
    {
        byte[] bytes =
            WithoutDebugDirectory(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var cancellation = new CancellationTokenSource();
        assembly.Policy.BeforeSelection =
            cancellation.Cancel;
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        if (typeQuery)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest(
                        typeof(SourceFixture).Name),
                    host.Context,
                    cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => AssemblyContextSourceQuery.ExecuteMemberAsync(
                    group,
                    assembly.Participant,
                    assembly.MemberRequest(
                        nameof(SourceFixture.Describe)),
                    host.Context,
                    cancellation.Token));
        }

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(assembly.Policy.SelectionCount > 0);
    }

    [Fact]
    public async Task SourceStoreFailure_FallsBackRepeatablyWithoutPublishingMemoryEntry()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceFixture).Name);
        var store = new ThrowingSourceContentStore();
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            store);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            AssemblyTypeSourceEntry result =
                await AssemblyContextSourceQuery.ExecuteTypeAsync(
                    group,
                    assembly.Participant,
                    request,
                    host.Context,
                    TestContext.Current.CancellationToken);

            var available =
                Assert.IsType<AssemblyTypeSourceEntry.Available>(
                    result);
            var decompiled =
                Assert.IsType<AssemblyTypeSource.Decompiled>(
                    available.Source);
            var failed =
                Assert.IsType<FindingInspection<string>.Failed>(
                    decompiled.AuthoredAttempt.Lines.Value);
            Assert.Contains(
                "source-content store failed",
                failed.Error.Reason,
                StringComparison.Ordinal);
            Assert.Contains(
                nameof(SourceFixture),
                decompiled.Text,
                StringComparison.Ordinal);
        }

        Assert.Equal(2, store.StoreAttempts);
        Assert.Equal(2, host.SourceRequests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourceStoreOperationalFailure_PreservesAuthoredFailureAndFallback(
        bool failRead)
    {
        TestAssembly assembly = TestAssembly.Create();
        var store =
            new OperationalFailureSourceContentStore(
                failRead);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            store);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);

        var decompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                Assert.IsType<
                    AssemblyTypeSourceEntry.Available>(
                        result)
                    .Source);
        var failed =
            Assert.IsType<FindingInspection<string>.Failed>(
                decompiled.AuthoredAttempt.Lines.Value);
        Assert.Contains(
            "source-content store failed",
            failed.Error.Reason,
            StringComparison.Ordinal);
        Assert.Equal(1, store.ReadAttempts);
        Assert.Equal(failRead ? 0 : 1, store.StoreAttempts);
        Assert.Equal(failRead ? 0 : 1, host.SourceRequests.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourceStoreCancellation_Propagates(
        bool cancelRead)
    {
        TestAssembly assembly = TestAssembly.Create();
        using var cancellation = new CancellationTokenSource();
        var store =
            new CancelingSourceContentStore(
                cancellation,
                cancelRead);
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            store);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, store.ReadAttempts);
        Assert.Equal(cancelRead ? 0 : 1, store.StoreAttempts);
        Assert.Equal(cancelRead ? 0 : 1, host.SourceRequests.Count);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task SourceStoreSuccessfulCancellation_PropagatesBeforeAuthoredSuccess(
        bool member,
        bool cancelRead)
    {
        TestAssembly assembly = TestAssembly.Create();
        var store =
            new SuccessfulCancelingSourceContentStore(
                cancelRead,
                SourceFileBytes());
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            store);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var cancellation =
                new CancellationTokenSource();
            store.Arm(cancellation);
            if (member)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => AssemblyContextSourceQuery
                        .ExecuteMemberAsync(
                            group,
                            assembly.Participant,
                            assembly.MemberRequest(
                                nameof(SourceFixture.Describe)),
                            host.Context,
                            cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => AssemblyContextSourceQuery
                        .ExecuteTypeAsync(
                            group,
                            assembly.Participant,
                            assembly.TypeRequest(
                                typeof(SourceFixture).Name),
                            host.Context,
                            cancellation.Token));
            }

            Assert.True(
                cancellation.IsCancellationRequested);
        }

        Assert.Equal(2, store.ReadAttempts);
        Assert.Equal(cancelRead ? 0 : 2, store.StoreAttempts);
        Assert.Equal(cancelRead ? 0 : 2, host.SourceRequests.Count);
        Assert.Equal(0, assembly.Policy.SelectionCount);
    }

    [Fact]
    public async Task PdbStoreFailure_PreservesAuthoredFailureAndFallsBackForMemberAndType()
    {
        TestAssembly assembly = TestAssembly.Create();
        var pdbStore = new ThrowingPdbStore();
        using var host = QueryHost.WithPdb(
            assembly.PdbPath,
            SourceFileBytes(),
            pdbStore: pdbStore);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry memberResult =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(SourceFixture.Describe)),
                host.Context,
                TestContext.Current.CancellationToken);
        var memberAvailable =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                memberResult);
        var memberDecompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                memberAvailable.Source);
        var memberFailure =
            Assert.IsType<FindingInspection<string>.Failed>(
                memberDecompiled.AuthoredAttempt.Lines.Value);
        Assert.Contains(
            "Portable PDB acquisition failed",
            memberFailure.Error.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SourceFixture.Describe),
            memberDecompiled.Text,
            StringComparison.Ordinal);

        AssemblyTypeSourceEntry typeResult =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);
        var typeAvailable =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                typeResult);
        var typeDecompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                typeAvailable.Source);
        var typeFailure =
            Assert.IsType<FindingInspection<string>.Failed>(
                typeDecompiled.AuthoredAttempt.Lines.Value);
        Assert.Contains(
            "Portable PDB acquisition failed",
            typeFailure.Error.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SourceFixture),
            typeDecompiled.Text,
            StringComparison.Ordinal);

        Assert.Equal(2, pdbStore.ReadAttempts);
        Assert.True(assembly.Policy.SelectionCount > 0);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task CorruptEmbeddedPdb_PreservesAuthoredFailureAndFallsBackForMemberAndType()
    {
        byte[] bytes =
            CorruptEmbeddedPdb(
                File.ReadAllBytes(
                    typeof(EmbeddedSourceFixture)
                        .Assembly.Location));

        TestAssembly assembly =
            TestAssembly.Create(bytes);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry memberResult =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                assembly.MemberRequest(
                    nameof(EmbeddedSourceFixture.Echo),
                    typeof(EmbeddedSourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);
        var memberAvailable =
            Assert.IsType<AssemblyMemberSourceEntry.Available>(
                memberResult);
        var memberDecompiled =
            Assert.IsType<AssemblyMemberSource.Decompiled>(
                memberAvailable.Source);
        Assert.IsType<FindingInspection<string>.Failed>(
            memberDecompiled.AuthoredAttempt.Lines.Value);
        Assert.Contains(
            nameof(EmbeddedSourceFixture.Echo),
            memberDecompiled.Text,
            StringComparison.Ordinal);

        AssemblyTypeSourceEntry typeResult =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(EmbeddedSourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);
        var typeAvailable =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                typeResult);
        var typeDecompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                typeAvailable.Source);
        Assert.IsType<FindingInspection<string>.Failed>(
            typeDecompiled.AuthoredAttempt.Lines.Value);
        Assert.Contains(
            nameof(EmbeddedSourceFixture),
            typeDecompiled.Text,
            StringComparison.Ordinal);

        Assert.True(assembly.Policy.SelectionCount > 0);
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public void PdbContextOpenFailure_DisposesAuthoritativeStream()
    {
        byte[] bytes =
            CorruptEmbeddedPdb(
                File.ReadAllBytes(
                    typeof(EmbeddedSourceFixture)
                        .Assembly.Location));
        using var stream =
            new DisposeCountingStream(
                new MemoryStream(
                    bytes,
                    writable: false));
        var descriptor =
            ResolvedAssemblyReference.Create(
                ReadIdentity(bytes),
                path: null,
                () => stream,
                AssemblyResolutionProvenance.Local(
                    "corrupt embedded PDB fixture"));

        Assert.Throws<BadImageFormatException>(
            () => PdbContext.OpenMetadataOnly(
                descriptor));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task PdbAcquisitionCancellation_DisposesOpenedSourceLinkService()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] bytes =
            File.ReadAllBytes(
                typeof(AssemblyContextSourceQueryTests)
                    .Assembly.Location);
        using var stream =
            new DisposeCountingStream(
                new MemoryStream(
                    bytes,
                    writable: false));
        var descriptor =
            ResolvedAssemblyReference.Create(
                assembly.Assembly.Identity,
                path: null,
                () => stream,
                AssemblyResolutionProvenance.Package(
                    "Example.Source",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var host =
            QueryHost.WithPdb(
                assembly.PdbPath,
                SourceFileBytes(),
                pdbStore: new CancelingPdbStore());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => AssemblyContextSourceQuery
                .OpenSourceLinkAsync(
                    descriptor,
                    host.Context,
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task MalformedPdbDocument_PreservesAuthoredFailureAndFallsBackForType()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            CorruptDocumentName(
                assembly.PdbPath,
                Path.GetFileName(
                    SourceFileBytesPath()),
                corruptTarget: false);

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            pdbBytes);
    }

    [Fact]
    public async Task MalformedTargetPdbDocument_ProducesFailedAuthoredEvidenceBeforeTypeFallback()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            CorruptDocumentName(
                assembly.PdbPath,
                Path.GetFileName(
                    SourceFileBytesPath()),
                corruptTarget: true);

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            pdbBytes);
    }

    [Fact]
    public async Task EmptyTargetPdbDocument_ProducesFailedAuthoredEvidenceBeforeTypeFallback()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            EmptyDocumentName(
                assembly.PdbPath,
                Path.GetFileName(
                    SourceFileBytesPath()));

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            pdbBytes);
    }

    [Fact]
    public async Task MalformedTargetSequencePoints_ProduceFailedAuthoredEvidenceBeforeTypeFallback()
    {
        TestAssembly assembly = TestAssembly.Create();
        byte[] pdbBytes =
            CorruptMethodSequencePoints(
                assembly.PdbPath,
                typeof(SourceFixture)
                    .GetMethod(
                        nameof(SourceFixture.Describe))!
                    .MetadataToken);

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            pdbBytes);
    }

    [Fact]
    public async Task RejectedUnrelatedTypeName_ProducesFailedAuthoredEvidenceBeforeTypeFallback()
    {
        TestAssembly original = TestAssembly.Create();
        byte[] bytes =
            RejectUnrelatedTypeName(
                File.ReadAllBytes(
                    typeof(AssemblyContextSourceQueryTests)
                        .Assembly.Location));
        TestAssembly assembly =
            TestAssembly.CreatePackage(
                bytes,
                original.PdbPath);

        await AssertMalformedPdbTypeFallsBackAsync(
            assembly,
            File.ReadAllBytes(original.PdbPath));
    }

    [Fact]
    public async Task NeitherSourceAvailable_ReturnsTypedFailure()
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest(typeof(SourceDelegate).Name);
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<AssemblyTypeSourceEntry.Unavailable>(
                result);
        Assert.Equal(
            AssemblySourceFailureKind
                .AuthoredAndDecompiledUnavailable,
            unavailable.Failure.Kind);
        Assert.NotNull(unavailable.AuthoredAttempt);
        Assert.NotNull(unavailable.DecompiledAttempt);
        Assert.False(unavailable.DecompiledAttempt!.Succeeded);
    }

    [Fact]
    public async Task RejectedParticipant_ReturnsAcquisitionFailure()
    {
        TestAssembly assembly =
            TestAssembly.Create(selectedName: "Different.Identity");
        AssemblyMemberSourceRequest request =
            assembly.MemberRequest(nameof(SourceFixture.Describe));
        using var host = QueryHost.WithoutPdb();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyMemberSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteMemberAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<AssemblyMemberSourceEntry.Rejected>(
                result);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    static byte[] SourceFileBytes(
        [CallerFilePath] string path = "") =>
        File.ReadAllBytes(path);

    static string SourceFileBytesPath(
        [CallerFilePath] string path = "") =>
        path;

    static byte[] CorruptEmbeddedPdb(
        byte[] original)
    {
        byte[] bytes = (byte[])original.Clone();
        using var stream =
            new MemoryStream(bytes, writable: false);
        using var reader = new PEReader(stream);
        DebugDirectoryEntry embedded =
            Assert.Single(
                reader.ReadDebugDirectory(),
                static entry =>
                    entry.Type
                    == DebugDirectoryEntryType
                        .EmbeddedPortablePdb);
        bytes[embedded.DataPointer] ^= 0xff;
        return bytes;
    }

    static byte[] WithoutDebugDirectory(
        byte[] original)
    {
        byte[] bytes = (byte[])original.Clone();
        using (var reader =
               new PEReader(
                   new MemoryStream(
                       bytes,
                       writable: false)))
        {
            PEHeader header =
                Assert.IsType<PEHeader>(
                    reader.PEHeaders.PEHeader);
            int directoryBase =
                reader.PEHeaders.PEHeaderStartOffset
                + (header.Magic == PEMagic.PE32Plus
                    ? 112
                    : 96);
            Array.Clear(
                bytes,
                directoryBase + (6 * 8),
                8);
        }

        using var mutated =
            new PEReader(
                new MemoryStream(
                    bytes,
                    writable: false));
        Assert.Empty(mutated.ReadDebugDirectory());
        return bytes;
    }

    static byte[] RemoveSourceLinkCustomDebugInformation(
        string pdbPath)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        int kindOffset;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            CustomDebugInformationHandle informationHandle =
                Assert.Single(
                    reader.GetCustomDebugInformation(
                        EntityHandle.ModuleDefinition),
                    handle =>
                        reader.GetGuid(
                            reader
                                .GetCustomDebugInformation(
                                    handle)
                                .Kind)
                        == SourceLinkKind);
            CustomDebugInformation information =
                reader.GetCustomDebugInformation(
                    informationHandle);
            kindOffset =
                MetadataTokens.GetHeapOffset(
                    information.Kind);
        }

        int guidOffset =
            FindMetadataStreamOffset(bytes, "#GUID");
        bytes[
            checked(
                guidOffset
                + ((kindOffset - 1) * 16))] ^= 0xff;

        using var mutatedProvider =
            MetadataReaderProvider.FromPortablePdbStream(
                new MemoryStream(
                    bytes,
                    writable: false),
                MetadataStreamOptions.PrefetchMetadata);
        MetadataReader mutatedReader =
            mutatedProvider.GetMetadataReader();
        Assert.DoesNotContain(
            mutatedReader.GetCustomDebugInformation(
                EntityHandle.ModuleDefinition),
            handle =>
                mutatedReader.GetGuid(
                    mutatedReader
                        .GetCustomDebugInformation(
                            handle)
                        .Kind)
                == SourceLinkKind);
        return bytes;
    }

    static AssemblyReferenceIdentity ReadIdentity(
        byte[] bytes)
    {
        using var stream =
            new MemoryStream(bytes, writable: false);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity
            .FromAssemblyDefinition(
                reader.GetMetadataReader());
    }

    static async Task AssertMalformedPdbTypeFallsBackAsync(
        TestAssembly assembly,
        byte[] pdbBytes)
    {
        using var host =
            QueryHost.WithPdb(
                Path.GetFileName(assembly.PdbPath),
                pdbBytes,
                SourceFileBytes());
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeSourceEntry result =
            await AssemblyContextSourceQuery.ExecuteTypeAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest(
                    typeof(SourceFixture).Name),
                host.Context,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<AssemblyTypeSourceEntry.Available>(
                result);
        var decompiled =
            Assert.IsType<AssemblyTypeSource.Decompiled>(
                available.Source);
        var failed =
            Assert.IsType<FindingInspection<string>.Failed>(
                decompiled.AuthoredAttempt.Lines.Value);
        Assert.Contains(
            "Portable PDB type source mapping failed",
            failed.Error.Reason,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(SourceFixture),
            decompiled.Text,
            StringComparison.Ordinal);
        Assert.True(assembly.Policy.SelectionCount > 0);
        Assert.NotEmpty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    static byte[] CorruptDocumentName(
        string pdbPath,
        string targetFileName,
        bool corruptTarget)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        int documentNameOffset = -1;
        int documentNameLength = 0;
        int corruptedDocumentRow = 0;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            foreach (DocumentHandle handle in reader.Documents)
            {
                Document document =
                    reader.GetDocument(handle);
                string name =
                    reader.GetString(document.Name);
                bool isTarget =
                    name.EndsWith(
                        targetFileName,
                        StringComparison.Ordinal);
                if (isTarget != corruptTarget)
                {
                    continue;
                }

                var nameBlob =
                    (BlobHandle)document.Name;
                corruptedDocumentRow =
                    MetadataTokens.GetRowNumber(handle);
                documentNameOffset =
                    MetadataTokens.GetHeapOffset(nameBlob);
                documentNameLength =
                    reader.GetBlobBytes(nameBlob).Length;
                break;
            }
        }

        Assert.True(documentNameOffset >= 0);
        Assert.True(documentNameLength > 1);
        int blobOffset =
            FindMetadataStreamOffset(bytes, "#Blob");
        int blobEntryOffset =
            checked(blobOffset + documentNameOffset);
        int payloadOffset =
            checked(
                blobEntryOffset
                + CompressedIntegerPrefixSize(
                    bytes[blobEntryOffset]));
        bytes[payloadOffset + 1] = 0xe0;

        int malformedDocuments = 0;
        bool targetDocumentReadable = false;
        bool targetDocumentMalformed = false;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            foreach (DocumentHandle handle in reader.Documents)
            {
                try
                {
                    string name =
                        reader.GetString(
                            reader.GetDocument(handle).Name);
                    targetDocumentReadable |=
                        name.EndsWith(
                            targetFileName,
                            StringComparison.Ordinal);
                }
                catch (BadImageFormatException)
                {
                    malformedDocuments++;
                    targetDocumentMalformed |=
                        MetadataTokens.GetRowNumber(handle)
                        == corruptedDocumentRow
                        && corruptTarget;
                }
            }
        }

        Assert.Equal(1, malformedDocuments);
        Assert.Equal(
            !corruptTarget,
            targetDocumentReadable);
        Assert.Equal(
            corruptTarget,
            targetDocumentMalformed);
        return bytes;
    }

    static byte[] EmptyDocumentName(
        string pdbPath,
        string targetFileName)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        int documentNameOffset = -1;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            foreach (DocumentHandle handle in reader.Documents)
            {
                Document document =
                    reader.GetDocument(handle);
                string name =
                    reader.GetString(document.Name);
                if (!name.EndsWith(
                    targetFileName,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                documentNameOffset =
                    MetadataTokens.GetHeapOffset(
                        (BlobHandle)document.Name);
                break;
            }
        }

        Assert.True(documentNameOffset >= 0);
        int blobOffset =
            FindMetadataStreamOffset(bytes, "#Blob");
        int blobEntryOffset =
            checked(blobOffset + documentNameOffset);
        Assert.Equal(
            1,
            CompressedIntegerPrefixSize(
                bytes[blobEntryOffset]));
        bytes[blobEntryOffset] = 1;

        using var corruptedProvider =
            MetadataReaderProvider.FromPortablePdbStream(
                new MemoryStream(
                    bytes,
                    writable: false),
                MetadataStreamOptions.PrefetchMetadata);
        MetadataReader corruptedReader =
            corruptedProvider.GetMetadataReader();
        Assert.Single(
            corruptedReader.Documents,
            handle =>
                corruptedReader.GetString(
                    corruptedReader
                        .GetDocument(handle).Name)
                    .Length == 0);
        return bytes;
    }

    static byte[] RejectUnrelatedTypeName(
        byte[] original)
    {
        byte[] bytes = (byte[])original.Clone();
        int metadataOffset;
        int typeNameOffset = -1;
        using (var stream =
               new MemoryStream(
                   bytes,
                   writable: false))
        using (var reader = new PEReader(stream))
        {
            metadataOffset =
                reader.PEHeaders.MetadataStartOffset;
            MetadataReader metadata =
                reader.GetMetadataReader();
            foreach (TypeDefinitionHandle handle
                in metadata.TypeDefinitions)
            {
                TypeDefinition type =
                    metadata.GetTypeDefinition(handle);
                string name =
                    metadata.GetString(type.Name);
                string typeNamespace =
                    metadata.GetString(type.Namespace);
                if (name is "<Module>"
                    or nameof(SourceFixture)
                    || typeNamespace.Length == 0
                    || !type.GetDeclaringType().IsNil)
                {
                    continue;
                }

                typeNameOffset =
                    MetadataTokens.GetHeapOffset(
                        type.Name);
                break;
            }
        }

        Assert.True(typeNameOffset > 0);
        int stringsOffset =
            checked(
                metadataOffset
                + FindMetadataStreamOffset(
                    bytes,
                    "#Strings",
                    metadataOffset));
        bytes[stringsOffset + typeNameOffset] = 0;

        using var corruptedStream =
            new MemoryStream(
                bytes,
                writable: false);
        using var corruptedReader =
            new PEReader(corruptedStream);
        MetadataReader corruptedMetadata =
            corruptedReader.GetMetadataReader();
        Assert.Contains(
            corruptedMetadata.TypeDefinitions,
            handle =>
                corruptedMetadata.GetString(
                    corruptedMetadata
                        .GetTypeDefinition(handle).Name)
                    .Length == 0);
        return bytes;
    }

    static byte[] CorruptMethodSequencePoints(
        string pdbPath,
        int metadataToken)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        var methodHandle =
            MetadataTokens.MethodDefinitionHandle(
                metadataToken & 0x00ff_ffff);
        int sequencePointsOffset;
        using (var provider =
               MetadataReaderProvider.FromPortablePdbStream(
                   new MemoryStream(
                       bytes,
                       writable: false),
                   MetadataStreamOptions.PrefetchMetadata))
        {
            MetadataReader reader =
                provider.GetMetadataReader();
            MethodDebugInformation debugInfo =
                reader.GetMethodDebugInformation(
                    methodHandle
                        .ToDebugInformationHandle());
            Assert.False(
                debugInfo.SequencePointsBlob.IsNil);
            sequencePointsOffset =
                MetadataTokens.GetHeapOffset(
                    debugInfo.SequencePointsBlob);
        }

        int blobOffset =
            FindMetadataStreamOffset(bytes, "#Blob");
        int blobEntryOffset =
            checked(blobOffset + sequencePointsOffset);
        int payloadOffset =
            checked(
                blobEntryOffset
                + CompressedIntegerPrefixSize(
                    bytes[blobEntryOffset]));
        bytes[payloadOffset] = 0xff;

        using var corruptedProvider =
            MetadataReaderProvider.FromPortablePdbStream(
                new MemoryStream(
                    bytes,
                    writable: false),
                MetadataStreamOptions.PrefetchMetadata);
        MetadataReader corruptedReader =
            corruptedProvider.GetMetadataReader();
        Assert.Throws<BadImageFormatException>(
            () =>
            {
                foreach (SequencePoint _ in
                    corruptedReader
                        .GetMethodDebugInformation(
                            methodHandle
                                .ToDebugInformationHandle())
                        .GetSequencePoints())
                {
                }
            });
        return bytes;
    }

    static int FindMetadataStreamOffset(
        byte[] metadata,
        string requestedName,
        int metadataOffset = 0)
    {
        int versionLength =
            BinaryPrimitives.ReadInt32LittleEndian(
                metadata.AsSpan(
                    metadataOffset + 12,
                    sizeof(int)));
        int cursor =
            checked(
                metadataOffset
                + 16
                + versionLength
                + 2);
        ushort streamCount =
            BinaryPrimitives.ReadUInt16LittleEndian(
                metadata.AsSpan(cursor, sizeof(ushort)));
        cursor += sizeof(ushort);
        for (int i = 0; i < streamCount; i++)
        {
            int offset =
                BinaryPrimitives.ReadInt32LittleEndian(
                    metadata.AsSpan(cursor, sizeof(int)));
            cursor += 2 * sizeof(int);
            int nameStart = cursor;
            while (metadata[cursor] != 0)
                cursor++;
            string name =
                Encoding.ASCII.GetString(
                    metadata,
                    nameStart,
                    cursor - nameStart);
            cursor =
                checked((cursor + 4) & ~3);
            if (name == requestedName)
                return offset;
        }

        throw new InvalidOperationException(
            $"Metadata stream '{requestedName}' is unavailable.");
    }

    static int CompressedIntegerPrefixSize(byte first)
        => first switch
        {
            < 0x80 => 1,
            _ when ((first & 0xc0) == 0x80) => 2,
            _ when ((first & 0xe0) == 0xc0) => 4,
            _ => throw new BadImageFormatException(
                "Invalid compressed integer."),
        };

    sealed class TestAssembly
    {
        readonly ApiSurface _surface;

        TestAssembly(
            ResolvedAssemblyReference assembly,
            AssemblyContextParticipant participant,
            string pdbPath,
            ApiSurface surface,
            FrameworkBindingPolicy policy)
        {
            Assembly = assembly;
            Participant = participant;
            PdbPath = pdbPath;
            _surface = surface;
            Policy = policy;
        }

        internal ResolvedAssemblyReference Assembly { get; }
        internal AssemblyContextParticipant Participant { get; }
        internal string PdbPath { get; }
        internal FrameworkBindingPolicy Policy { get; }

        internal static TestAssembly Create(
            string? selectedName = null)
        {
            string path =
                typeof(AssemblyContextSourceQueryTests)
                    .Assembly.Location;
            byte[] bytes = File.ReadAllBytes(path);
            AssemblyReferenceIdentity identity =
                ReadIdentity(bytes);
            if (selectedName is not null)
            {
                identity = identity with
                {
                    Name = selectedName,
                };
            }

            var assembly =
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () => new MemoryStream(
                        bytes,
                        writable: false),
                    AssemblyResolutionProvenance.Package(
                        "Example.Source",
                        "1.0.0",
                        "net10.0",
                        rid: null));
            var policy = new FrameworkBindingPolicy();
            var participant =
                new AssemblyContextParticipant(
                    assembly,
                    policy);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(
                    ResolvedAssemblyReference.Create(
                        ReadIdentity(bytes),
                        path: null,
                        () => new MemoryStream(
                            bytes,
                            writable: false),
                        AssemblyResolutionProvenance.Local(
                            "source query target")));
            return new TestAssembly(
                assembly,
                participant,
                Path.ChangeExtension(path, ".pdb"),
                session.ApiSurface(includeAll: true),
                policy);
        }

        internal static TestAssembly Create(
            byte[] bytes)
        {
            AssemblyReferenceIdentity identity =
                ReadIdentity(bytes);
            var assembly =
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () => new MemoryStream(
                        bytes,
                        writable: false),
                    AssemblyResolutionProvenance.Local(
                        "embedded source query fixture"));
            var policy = new FrameworkBindingPolicy();
            var participant =
                new AssemblyContextParticipant(
                    assembly,
                    policy);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(assembly);
            return new TestAssembly(
                assembly,
                participant,
                pdbPath: "",
                session.ApiSurface(includeAll: true),
                policy);
        }

        internal static TestAssembly CreatePackage(
            byte[] bytes,
            string pdbPath)
        {
            AssemblyReferenceIdentity identity =
                ReadIdentity(bytes);
            var assembly =
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () => new MemoryStream(
                        bytes,
                        writable: false),
                    AssemblyResolutionProvenance.Package(
                        "Example.Source",
                        "1.0.0",
                        "net10.0",
                        rid: null));
            var policy = new FrameworkBindingPolicy();
            var participant =
                new AssemblyContextParticipant(
                    assembly,
                    policy);
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(
                    assembly);
            return new TestAssembly(
                assembly,
                participant,
                pdbPath,
                session.ApiSurface(includeAll: true),
                policy);
        }

        internal AssemblyTypeSourceRequest TypeRequest(
            string typeName)
        {
            ApiType type = Assert.Single(
                _surface.Types,
                candidate =>
                    candidate.DefinitionName?.Segments[^1]
                    == typeName);
            return AssemblyTypeSourceRequest.From(type);
        }

        internal AssemblyMemberSourceRequest MemberRequest(
            string memberName,
            string? typeName = null)
        {
            ApiType type = Assert.Single(
                _surface.Types,
                candidate =>
                    candidate.DefinitionName?.Segments[^1]
                    == (typeName
                        ?? typeof(SourceFixture).Name));
            ApiMember member = Assert.Single(
                type.Members,
                candidate => candidate.Name == memberName);
            return AssemblyMemberSourceRequest.From(
                type,
                member);
        }

        static AssemblyReferenceIdentity ReadIdentity(
            byte[] bytes)
        {
            using var stream =
                new MemoryStream(bytes, writable: false);
            using var reader = new PEReader(stream);
            return AssemblyReferenceIdentity
                .FromAssemblyDefinition(
                    reader.GetMetadataReader());
        }
    }

    sealed class QueryHost : IDisposable
    {
        readonly HttpClient _symbolClient;
        readonly HttpClient _sourceClient;

        QueryHost(
            SymbolPackageHandler symbolHandler,
            SourceHandler sourceHandler,
            ISourceContentStore? sourceContentStore = null,
            IPdbStore? pdbStore = null,
            bool allowLocalSourceReads = false)
        {
            _symbolClient = new HttpClient(symbolHandler);
            _sourceClient = new HttpClient(sourceHandler);
            Context = new AssemblyContextSourceQueryContext(
                _symbolClient,
                pdbStore
                    ?? new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                new SourceFetcher(
                    _sourceClient,
                    sourceContentStore
                        ?? new InMemorySourceContentStore()))
            {
                AllowLocalSourceReads =
                    allowLocalSourceReads,
            };
            SymbolRequests = symbolHandler.RequestUris;
            SourceRequests = sourceHandler.RequestUris;
        }

        internal AssemblyContextSourceQueryContext Context
        {
            get;
        }
        internal List<Uri> SymbolRequests { get; }
        internal List<Uri> SourceRequests { get; }

        internal static QueryHost WithPdb(
            string pdbPath,
            byte[] sourceBytes,
            ISourceContentStore? sourceContentStore = null,
            IPdbStore? pdbStore = null)
        {
            Assert.True(
                File.Exists(pdbPath),
                $"Expected test PDB at {pdbPath}");
            return new QueryHost(
                new SymbolPackageHandler(
                    BuildSnupkg(
                        Path.GetFileName(pdbPath),
                        File.ReadAllBytes(pdbPath))),
                new SourceHandler(sourceBytes),
                sourceContentStore,
                pdbStore);
        }

        internal static QueryHost WithPdb(
            string pdbFileName,
            byte[] pdbBytes,
            byte[] sourceBytes,
            bool allowLocalSourceReads = false)
            => new(
                new SymbolPackageHandler(
                    BuildSnupkg(
                        pdbFileName,
                        pdbBytes)),
                new SourceHandler(sourceBytes),
                allowLocalSourceReads:
                    allowLocalSourceReads);

        internal static QueryHost WithoutPdb()
            => new(
                new SymbolPackageHandler(snupkg: null),
                new SourceHandler(content: null));

        public void Dispose()
        {
            _sourceClient.Dispose();
            _symbolClient.Dispose();
        }

        static byte[] BuildSnupkg(
            string pdbFileName,
            byte[] pdbBytes)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(
                       buffer,
                       ZipArchiveMode.Create,
                       leaveOpen: true))
            {
                ZipArchiveEntry entry =
                    archive.CreateEntry(
                        $"lib/net10.0/{pdbFileName}");
                using Stream stream = entry.Open();
                stream.Write(pdbBytes);
            }

            return buffer.ToArray();
        }
    }

    sealed class SymbolPackageHandler(byte[]? snupkg)
        : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (snupkg is not null
                && request.RequestUri!.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(snupkg),
                        RequestMessage = request,
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
        }
    }

    sealed class SourceHandler(byte[]? content)
        : HttpMessageHandler
    {
        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(
                new HttpResponseMessage(
                    content is null
                        ? HttpStatusCode.NotFound
                        : HttpStatusCode.OK)
                {
                    Content = content is null
                        ? null
                        : new ByteArrayContent(content),
                    RequestMessage = request,
                });
        }
    }

    sealed class ThrowingSourceContentStore
        : ISourceContentStore
    {
        int _storeAttempts;

        internal int StoreAttempts =>
            Volatile.Read(ref _storeAttempts);

        public ValueTask<byte[]?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<byte[]?>(null);
        }

        public ValueTask StoreAsync(
            string key,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _storeAttempts);
            throw new IOException(
                "Synthetic source-content store failure.");
        }
    }

    sealed class OperationalFailureSourceContentStore(
        bool failRead)
        : ISourceContentStore
    {
        int _readAttempts;
        int _storeAttempts;

        internal int ReadAttempts =>
            Volatile.Read(ref _readAttempts);
        internal int StoreAttempts =>
            Volatile.Read(ref _storeAttempts);

        public ValueTask<byte[]?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readAttempts);
            if (failRead)
            {
                throw new InvalidOperationException(
                    "Synthetic source-content store read failure.");
            }

            return ValueTask.FromResult<byte[]?>(null);
        }

        public ValueTask StoreAsync(
            string key,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _storeAttempts);
            throw new InvalidOperationException(
                "Synthetic source-content store write failure.");
        }
    }

    sealed class CancelingSourceContentStore(
        CancellationTokenSource source,
        bool cancelRead)
        : ISourceContentStore
    {
        int _readAttempts;
        int _storeAttempts;

        internal int ReadAttempts =>
            Volatile.Read(ref _readAttempts);
        internal int StoreAttempts =>
            Volatile.Read(ref _storeAttempts);

        public ValueTask<byte[]?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readAttempts);
            if (cancelRead)
            {
                source.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromResult<byte[]?>(null);
        }

        public ValueTask StoreAsync(
            string key,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _storeAttempts);
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    sealed class SuccessfulCancelingSourceContentStore(
        bool cancelRead,
        byte[] content)
        : ISourceContentStore
    {
        int _readAttempts;
        int _storeAttempts;
        CancellationTokenSource? _source;

        internal int ReadAttempts =>
            Volatile.Read(ref _readAttempts);
        internal int StoreAttempts =>
            Volatile.Read(ref _storeAttempts);

        internal void Arm(
            CancellationTokenSource source) =>
            _source = source;

        public ValueTask<byte[]?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readAttempts);
            if (!cancelRead)
                return ValueTask.FromResult<byte[]?>(null);

            _source!.Cancel();
            return ValueTask.FromResult<byte[]?>(
                content.ToArray());
        }

        public ValueTask StoreAsync(
            string key,
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _storeAttempts);
            _source!.Cancel();
            return ValueTask.CompletedTask;
        }
    }

    sealed class ThrowingPdbStore(
        Action? beforeFailure = null)
        : IPdbStore
    {
        int _readAttempts;

        internal int ReadAttempts =>
            Volatile.Read(ref _readAttempts);

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readAttempts);
            beforeFailure?.Invoke();
            throw new HttpRequestException(
                "Synthetic PDB store failure.");
        }

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The failing read must prevent a store write.");

        public string? TryGetLocalPath(string key) =>
            null;
    }

    sealed class CancelingPdbStore
        : IPdbStore
    {
        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException(
                "Synthetic PDB-store cancellation.");

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Cancellation must prevent a store write.");

        public string? TryGetLocalPath(string key) =>
            null;
    }

    sealed class DisposeCountingStream(Stream inner)
        : Stream
    {
        bool _disposed;

        internal int DisposeCount { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() =>
            inner.Flush();

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Read(buffer, offset, count);

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            inner.SetLength(value);

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                DisposeCount++;
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    sealed class FrameworkBindingPolicy
        : IAssemblyBindingPolicy
    {
        int _selectionCount;

        readonly ResolvedAssemblyReference _coreLibrary =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(object).Assembly.Location,
                AssemblyResolutionProvenance.Platform(
                    "Microsoft.NETCore.App",
                    frameworkVersion: null,
                    "source query test"));

        public AssemblyBindingPolicyVersion Version { get; private set; } =
            new();
        internal int SelectionCount =>
            Volatile.Read(ref _selectionCount);
        internal bool CancelSelection { get; set; }
        internal Action? BeforeSelection { get; set; }
        internal Func<
            AssemblyBindingRequest,
            AssemblyBindingSelection?>? SelectOverride { get; set; }

        internal void ChangeVersion() =>
            Version = new AssemblyBindingPolicyVersion();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            Interlocked.Increment(ref _selectionCount);
            BeforeSelection?.Invoke();
            if (CancelSelection)
            {
                throw new OperationCanceledException(
                    "Synthetic binding-policy cancellation.");
            }
            if (SelectOverride?.Invoke(request)
                is { } overridden)
            {
                return overridden;
            }

            return request.Target
                is AssemblyBindingTarget.AssemblyReference reference
                && reference.Identity.Name
                    == _coreLibrary.Identity.Name
                    ? AssemblyBindingSelection.Found(
                        _coreLibrary)
                    : AssemblyBindingSelection.NotFound();
        }
    }

    public static class SourceFixture
    {
        public static string Describe(int value)
            => $"value={value}";

        public static int Increment(int value)
            => value + 1;
    }

    public delegate int SourceDelegate(int value);
}
