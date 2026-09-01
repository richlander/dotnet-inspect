using ILInspector.Metadata;
using System.Reflection;

namespace DotnetInspector.Queries.Tests;

public sealed class InspectionWorkspaceTests
{
    [Fact]
    public void GroupAccess_IsLazyAndReusesOneImmutableSnapshot()
    {
        TestAssembly source = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);

        Assert.Equal(0, source.OpenCount);
        AssemblyImageAccessResult<int> first =
            group.UseAssemblyImage(
                source.Assembly,
                static image =>
                {
                    Assert.Equal((byte)'M', image.Content[0]);
                    Assert.Equal((byte)'Z', image.Content[1]);
                    return image.Content.Length;
                });
        AssemblyImageAccessResult<int> second =
            group.UseAssemblyImage(
                source.Assembly,
                static image => image.Content.Length);

        Assert.Equal(
            source.Bytes.Length,
            Assert.IsType<
                AssemblyImageAccessResult<int>.Available>(first).Value);
        Assert.Equal(
            source.Bytes.Length,
            Assert.IsType<
                AssemblyImageAccessResult<int>.Available>(second).Value);
        Assert.Equal(1, source.OpenCount);
        Assert.Equal(source.Bytes.Length, group.RetainedImageBytes);
    }

    [Fact]
    public void ReturnedSpan_RemainsSafeAfterWorkspaceDisposal()
    {
        TestAssembly source = TestAssembly.Create();
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);

        AssemblyImageSpanResult result =
            group.GetAssemblyImageSpan(source.Assembly);
        Assert.True(result.IsAvailable);
        byte first = result.Content[0];

        workspace.Dispose();

        Assert.Equal(first, result.Content[0]);
        Assert.Equal(0, group.RetainedImageBytes);
        Assert.Throws<ObjectDisposedException>(
            () => group.UseAssemblyImage(
                source.Assembly,
                static image => image.Content.Length));
    }

    [Fact]
    public void Snapshot_IsIsolatedFromTheMutableSource()
    {
        TestAssembly source = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);

        AssemblyImageSpanResult image =
            group.GetAssemblyImageSpan(source.Assembly);
        Assert.True(image.IsAvailable);
        byte first = image.Content[0];

        source.Bytes[0] ^= 0xff;

        Assert.Equal(first, image.Content[0]);
        Assert.Equal(
            first,
            group.GetAssemblyImageSpan(source.Assembly).Content[0]);
    }

    [Fact]
    public void RetainedReference_RemainsSnapshotBackedAfterWorkspaceDisposal()
    {
        TestAssembly source = TestAssembly.Create();
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);

        var retained = Assert.IsType<
            AssemblyImageAccessResult<
                ResolvedAssemblyReference>.Available>(
                    group.RetainAssemblyReference(source.Assembly)).Value;
        byte first = source.Bytes[0];

        source.Bytes[0] ^= 0xff;
        workspace.Dispose();

        Assert.Same(source.Assembly.Registration, retained.Registration);
        Assert.Same(source.Assembly.Provenance, retained.Provenance);
        Assert.Equal(source.Assembly.LastWriteTimeUtc, retained.LastWriteTimeUtc);
        using Stream stream = retained.OpenRead();
        Assert.Equal(first, stream.ReadByte());
        Assert.Equal(1, source.OpenCount);
    }

    [Fact]
    public void DisposalInsideCallback_DoesNotRevokeActiveView()
    {
        TestAssembly source = TestAssembly.Create();
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);

        AssemblyImageAccessResult<byte> result =
            group.UseAssemblyImage(
                source.Assembly,
                image =>
                {
                    byte first = image.Content[0];
                    workspace.Dispose();
                    Assert.Equal(
                        source.Bytes.Length,
                        group.RetainedImageBytes);
                    return first;
                });

        Assert.Equal(
            source.Bytes[0],
            Assert.IsType<
                AssemblyImageAccessResult<byte>.Available>(result).Value);
        Assert.Equal(0, group.RetainedImageBytes);
    }

    [Fact]
    public async Task ConcurrentDisposal_DoesNotRevokeActiveView()
    {
        TestAssembly source = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;

        Task<AssemblyImageAccessResult<byte>> access = StartConcurrent(
            () => group.UseAssemblyImage(
                source.Assembly,
                image =>
                {
                    byte first = image.Content[0];
                    entered.Set();
                    resume.Wait(cancellationToken);
                    Assert.Equal(first, image.Content[0]);
                    return first;
                }));

        Assert.True(
            entered.Wait(
                TimeSpan.FromSeconds(10),
                cancellationToken));
        try
        {
            workspace.Dispose();
            Assert.Equal(
                source.Bytes.Length,
                group.RetainedImageBytes);
        }
        finally
        {
            resume.Set();
        }

        AssemblyImageAccessResult<byte> result = await access;
        Assert.Equal(
            source.Bytes[0],
            Assert.IsType<
                AssemblyImageAccessResult<byte>.Available>(result).Value);
        Assert.Equal(0, group.RetainedImageBytes);
        Assert.Throws<ObjectDisposedException>(
            () => group.GetAssemblyImageSpan(source.Assembly));
    }

    [Fact]
    public async Task BlockedParticipant_DoesNotBlockAnotherParticipant()
    {
        TestAssembly blocked = TestAssembly.Create();
        TestAssembly available = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [blocked.Participant, available.Participant]);
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        blocked.BeforeOpen = () =>
        {
            entered.Set();
            resume.Wait(cancellationToken);
        };

        Task<AssemblyImageAccessResult<int>> first = StartConcurrent(
            () => group.UseAssemblyImage(
                blocked.Assembly,
                static image => image.Content.Length));

        Assert.True(
            entered.Wait(
                TimeSpan.FromSeconds(10),
                cancellationToken));
        Task<AssemblyImageAccessResult<int>> second = StartConcurrent(
            () => group.UseAssemblyImage(
                available.Assembly,
                static image => image.Content.Length));
        try
        {
            await second.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        finally
        {
            resume.Set();
        }

        Assert.IsType<
            AssemblyImageAccessResult<int>.Available>(await first);
        Assert.IsType<
            AssemblyImageAccessResult<int>.Available>(await second);
    }

    [Fact]
    public void ImageBudgetFailure_IsTypedAndCached()
    {
        TestAssembly source = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = source.Bytes.Length - 1,
                });
        bool called = false;

        AssemblyImageAccessResult<int> first =
            group.UseAssemblyImage(
                source.Assembly,
                image =>
                {
                    called = true;
                    return image.Content.Length;
                });
        AssemblyImageSpanResult second =
            group.GetAssemblyImageSpan(source.Assembly);

        var rejected = Assert.IsType<
            AssemblyImageAccessResult<int>.Rejected>(first);
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            rejected.Failure.Kind);
        Assert.False(second.IsAvailable);
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            second.Failure?.Kind);
        Assert.False(called);
        Assert.Equal(1, source.OpenCount);
        Assert.Equal(0, group.RetainedImageBytes);
    }

    [Fact]
    public void NullStream_IsReportedAsUnreadable()
    {
        TestAssembly source = TestAssembly.Create();
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                source.Assembly.Identity,
                path: null,
                static () => null!,
                AssemblyResolutionProvenance.Local(
                    "workspace null-stream test"));
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        assembly,
                        MissingBindingPolicy.Instance),
                ]);

        AssemblyImageSpanResult image =
            group.GetAssemblyImageSpan(assembly);

        Assert.False(image.IsAvailable);
        Assert.Equal(
            CandidateOpenFailureKind.Unreadable,
            image.Failure?.Kind);
    }

    [Fact]
    public void DescriptorIdentityMismatch_IsReportedAsInvalidImage()
    {
        TestAssembly source = TestAssembly.Create();
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                source.Assembly.Identity with
                {
                    Name = source.Assembly.Identity.Name + ".Other",
                },
                path: null,
                () => new MemoryStream(
                    source.Bytes,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "workspace identity-mismatch test"));
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        assembly,
                        MissingBindingPolicy.Instance),
                ]);

        AssemblyImageSpanResult image =
            group.GetAssemblyImageSpan(assembly);

        Assert.False(image.IsAvailable);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            image.Failure?.Kind);
    }

    [Fact]
    public void RejectedAcquisition_ReleasesReservedBudget()
    {
        TestAssembly invalidSource = TestAssembly.Create();
        TestAssembly validSource = TestAssembly.Create();
        ResolvedAssemblyReference invalidAssembly =
            ResolvedAssemblyReference.Create(
                invalidSource.Assembly.Identity with
                {
                    Name =
                        invalidSource.Assembly.Identity.Name
                        + ".Other",
                },
                path: null,
                () => new MemoryStream(
                    invalidSource.Bytes,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "workspace reservation-release test"));
        var invalidParticipant =
            new AssemblyContextParticipant(
                invalidAssembly,
                MissingBindingPolicy.Instance);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [invalidParticipant, validSource.Participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes =
                        validSource.Bytes.Length,
                });

        AssemblyImageSpanResult rejected =
            group.GetAssemblyImageSpan(invalidAssembly);

        Assert.False(rejected.IsAvailable);
        Assert.Equal(0, group.RetainedImageBytes);
        Assert.True(
            group.GetAssemblyImageSpan(
                validSource.Assembly).IsAvailable);
        Assert.Equal(
            validSource.Bytes.Length,
            group.RetainedImageBytes);
    }

    [Fact]
    public void StreamDisposalFailure_ReleasesReservedBudget()
    {
        TestAssembly failingSource = TestAssembly.Create();
        TestAssembly validSource = TestAssembly.Create();
        ResolvedAssemblyReference failingAssembly =
            ResolvedAssemblyReference.Create(
                failingSource.Assembly.Identity,
                path: null,
                () => new ThrowingDisposeMemoryStream(
                    failingSource.Bytes),
                AssemblyResolutionProvenance.Local(
                    "workspace disposal-failure test"));
        var failingParticipant =
            new AssemblyContextParticipant(
                failingAssembly,
                MissingBindingPolicy.Instance);
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [failingParticipant, validSource.Participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes =
                        validSource.Bytes.Length,
                });

        Assert.Throws<InvalidOperationException>(
            () => group.GetAssemblyImageSpan(failingAssembly));
        Assert.Equal(0, group.RetainedImageBytes);
        Assert.True(
            group.GetAssemblyImageSpan(
                validSource.Assembly).IsAvailable);
    }

    [Fact]
    public void ImageBudget_IsCumulativeAcrossParticipants()
    {
        TestAssembly first = TestAssembly.Create();
        TestAssembly second = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [first.Participant, second.Participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes =
                        first.Bytes.Length + second.Bytes.Length - 1,
                });

        Assert.True(
            group.GetAssemblyImageSpan(first.Assembly).IsAvailable);
        AssemblyImageSpanResult rejected =
            group.GetAssemblyImageSpan(second.Assembly);

        Assert.False(rejected.IsAvailable);
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            rejected.Failure?.Kind);
        Assert.Equal(
            first.Bytes.Length,
            group.RetainedImageBytes);
    }

    [Fact]
    public async Task ConcurrentAcquisition_RespectsCumulativeBudget()
    {
        TestAssembly first = TestAssembly.Create();
        TestAssembly second = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [first.Participant, second.Participant],
                new AssemblyContextGroupOptions
                {
                    MaxRetainedImageBytes = first.Bytes.Length,
                });
        using var entered = new CountdownEvent(2);
        using var resume = new ManualResetEventSlim();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        first.BeforeOpen = WaitForBoth;
        second.BeforeOpen = WaitForBoth;

        Task<AssemblyImageAccessResult<int>> firstAccess =
            StartConcurrent(() => Access(first));
        Task<AssemblyImageAccessResult<int>> secondAccess =
            StartConcurrent(() => Access(second));
        Assert.True(
            entered.Wait(
                TimeSpan.FromSeconds(10),
                cancellationToken));
        resume.Set();

        AssemblyImageAccessResult<int>[] results =
            await Task.WhenAll(firstAccess, secondAccess);

        Assert.Single(
            results.OfType<
                AssemblyImageAccessResult<int>.Available>());
        var rejected = Assert.Single(
            results.OfType<
                AssemblyImageAccessResult<int>.Rejected>());
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            rejected.Failure.Kind);
        Assert.Equal(first.Bytes.Length, group.RetainedImageBytes);

        void WaitForBoth()
        {
            entered.Signal();
            resume.Wait(cancellationToken);
        }

        AssemblyImageAccessResult<int> Access(TestAssembly source) =>
            group.UseAssemblyImage(
                source.Assembly,
                static image => image.Content.Length);
    }

    [Fact]
    public void GroupRejectsAssemblyOutsideItsParticipantSet()
    {
        TestAssembly source = TestAssembly.Create();
        TestAssembly other = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);

        Assert.Throws<ArgumentException>(
            () => group.UseAssemblyImage(
                other.Assembly,
                static image => image.Content.Length));
        Assert.Equal(0, source.OpenCount);
        Assert.Equal(0, other.OpenCount);
    }

    [Fact]
    public void DisposalDuringParticipantEnumeration_PreventsGroupPublication()
    {
        TestAssembly source = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();

        IEnumerable<AssemblyContextParticipant> Participants()
        {
            workspace.Dispose();
            yield return source.Participant;
        }

        Assert.Throws<ObjectDisposedException>(
            () => workspace.CreateAssemblyContextGroup(
                Participants()));
    }

    [Fact]
    public void GroupRejectsMixedBindingPolicySnapshots()
    {
        TestAssembly first = TestAssembly.Create();
        TestAssembly second = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();

        Assert.Throws<ArgumentException>(
            () => workspace.CreateAssemblyContextGroup(
                [
                    first.Participant,
                    new AssemblyContextParticipant(
                        second.Assembly,
                        new MissingBindingPolicy()),
                ]));
    }

    [Fact]
    public void OwnedResourceFailure_DoesNotRetainSnapshots()
    {
        TestAssembly source = TestAssembly.Create();
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);
        Assert.True(
            group.GetAssemblyImageSpan(source.Assembly).IsAvailable);
        group.RegisterOwnedResource(new ThrowingResource());

        Assert.Throws<AggregateException>(workspace.Dispose);

        Assert.Equal(0, group.RetainedImageBytes);
    }

    [Fact]
    public void OwnedResources_AreDisposedBeforeSnapshots()
    {
        TestAssembly source = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);
        Assert.True(
            group.GetAssemblyImageSpan(source.Assembly).IsAvailable);
        var resource = new RetainedImageAssertingResource(group);
        group.RegisterOwnedResource(resource);

        workspace.Dispose();

        Assert.True(resource.IsDisposed);
        Assert.Equal(0, group.RetainedImageBytes);
    }

    [Fact]
    public async Task AsyncParticipantRelease_PreservesOwnedResourceDisposalOrder()
    {
        TestAssembly source = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);
        var resource = new RetainedImageAssertingResource(group);
        group.RegisterOwnedResource(resource);

        AssemblyImageAccessResult<int> result =
            await group.UseAndReleaseAssemblySessionAsync(
                source.Assembly,
                (_, _) =>
                {
                    workspace.Dispose();
                    return Task.FromResult(1);
                });

        Assert.IsType<AssemblyImageAccessResult<int>.Available>(result);
        Assert.True(resource.IsDisposed);
        Assert.Equal(0, group.RetainedImageBytes);
    }

    [Fact]
    public async Task WorkspaceClose_RejectsAdmissionAndRoutesLateGroupToRelease()
    {
        TestAssembly source = TestAssembly.Create();
        InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        using var enumerationEntered = new ManualResetEventSlim();
        using var enumerationResume = new ManualResetEventSlim();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;

        IEnumerable<AssemblyContextParticipant> Participants()
        {
            enumerationEntered.Set();
            enumerationResume.Wait(cancellationToken);
            yield return source.Participant;
        }

        Task<AssemblyContextGroup> construction = StartConcurrent(
            () => workspace.CreateAssemblyContextGroup(
                Participants()));
        Assert.True(
            enumerationEntered.Wait(
                TimeSpan.FromSeconds(10),
                cancellationToken));

        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        Assert.Throws<ObjectDisposedException>(
            () => workspace.CreateAssemblyContextGroup(
                [source.Participant]));

        enumerationResume.Set();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await construction);
        InspectionWorkspaceCloseReport report = await close;

        InspectionWorkspaceDirectGroupCloseResult result =
            Assert.IsType<InspectionWorkspaceDirectGroupCloseResult>(
                Assert.Single(report.Groups));
        Assert.Equal(0, result.RegistrationIndex);
        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task WorkspaceClose_NoGroupFailureSettlesAdmissionWithoutCleanupEntry()
    {
        InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        using var enumerationEntered = new ManualResetEventSlim();
        using var enumerationResume = new ManualResetEventSlim();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;

        IEnumerable<AssemblyContextParticipant> Participants()
        {
            enumerationEntered.Set();
            enumerationResume.Wait(cancellationToken);
            yield return ThrowConstructionFailure();
        }

        Task<AssemblyContextGroup> construction = StartConcurrent(
            () => workspace.CreateAssemblyContextGroup(
                Participants()));
        Assert.True(
            enumerationEntered.Wait(
                TimeSpan.FromSeconds(10),
                cancellationToken));

        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        enumerationResume.Set();

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await construction);
        InspectionWorkspaceCloseReport report = await close;

        Assert.Equal(
            "Synthetic construction failure.",
            failure.Message);
        Assert.Empty(report.Groups);

        static AssemblyContextParticipant
            ThrowConstructionFailure() =>
            throw new InvalidOperationException(
                "Synthetic construction failure.");
    }

    [Fact]
    public async Task WorkspaceClose_AwaitsAllGroupCompletionsAndReportsEveryFailure()
    {
        TestAssembly first = TestAssembly.Create();
        TestAssembly second = TestAssembly.Create();
        InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        AssemblyContextGroup firstGroup =
            workspace.CreateAssemblyContextGroup(
                [first.Participant]);
        AssemblyContextGroup secondGroup =
            workspace.CreateAssemblyContextGroup(
                [second.Participant]);
        firstGroup.RegisterOwnedResource(new ThrowingResource());
        secondGroup.RegisterOwnedResource(new ThrowingResource());
        var callbackEntered =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackResume =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AssemblyImageAccessResult<int>> operation =
            firstGroup.UseAndReleaseAssemblySessionAsync(
                first.Assembly,
                async (_, _) =>
                {
                    callbackEntered.SetResult();
                    await callbackResume.Task;
                    return 1;
                });
        await callbackEntered.Task;

        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();
        Assert.False(close.IsCompleted);

        callbackResume.SetResult();
        Assert.IsType<AssemblyImageAccessResult<int>.Available>(
            await operation);
        InspectionWorkspaceCloseReport report = await close;

        Assert.Equal(2, report.Groups.Length);
        Assert.All(
            report.Groups,
            result =>
            {
                InspectionWorkspaceDirectGroupCloseResult direct =
                    Assert.IsType<
                        InspectionWorkspaceDirectGroupCloseResult>(
                            result);
                Assert.False(direct.Succeeded);
                AggregateException failure =
                    Assert.IsType<AggregateException>(
                        direct.Failure);
                Assert.Contains(
                    failure.InnerExceptions,
                    ex => ex is InvalidOperationException
                        && ex.Message
                            == "Synthetic owned-resource disposal failure.");
            });
        Assert.Equal(
            [0, 1],
            report.Groups
                .Select(result => result.RegistrationIndex)
                .ToArray());
    }

    [Fact]
    public async Task WorkspaceClose_ConcurrentCallersShareCompletionAndReportInstance()
    {
        TestAssembly firstSource = TestAssembly.Create();
        TestAssembly secondSource = TestAssembly.Create();
        InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        AssemblyContextGroup firstGroup =
            workspace.CreateAssemblyContextGroup(
                [firstSource.Participant]);
        AssemblyContextGroup secondGroup =
            workspace.CreateAssemblyContextGroup(
                [secondSource.Participant]);
        using var releaseEntered = new ManualResetEventSlim();
        using var releaseResume = new ManualResetEventSlim();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        firstGroup.RegisterOwnedResource(
            new BlockingResource(
                releaseEntered,
                releaseResume,
                cancellationToken));

        Task<Task<InspectionWorkspaceCloseReport>> firstCall =
            StartConcurrent(workspace.CloseAsync);
        Assert.True(
            releaseEntered.Wait(
                TimeSpan.FromSeconds(10),
                cancellationToken));
        Task<InspectionWorkspaceCloseReport> second =
            workspace.CloseAsync();
        Assert.Throws<ObjectDisposedException>(
            () => secondGroup.GetAssemblyImageSpan(
                secondSource.Assembly));
        Assert.False(second.IsCompleted);
        releaseResume.Set();
        Task<InspectionWorkspaceCloseReport> first =
            await firstCall;
        InspectionWorkspaceCloseReport firstReport = await first;
        InspectionWorkspaceCloseReport secondReport = await second;
        Task<InspectionWorkspaceCloseReport> third =
            workspace.CloseAsync();

        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.Same(firstReport, secondReport);
        Assert.Same(firstReport, await third);
        Assert.Same(firstReport, workspace.CloseReport);
    }

    [Fact]
    public async Task WorkspaceDispose_CompatibilityUsesSharedReleaseAuthority()
    {
        TestAssembly source = TestAssembly.Create();
        InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);
        group.RegisterOwnedResource(new ThrowingResource());

        Assert.Throws<InvalidOperationException>(workspace.Dispose);
        Assert.True(
            group.GetAssemblyImageSpan(source.Assembly).IsAvailable);
        group.Dispose();

        InspectionWorkspaceCloseReport report =
            await workspace.CloseAsync();

        Assert.False(
            Assert.IsType<
                InspectionWorkspaceDirectGroupCloseResult>(
                    Assert.Single(report.Groups))
                .Succeeded);

        TestAssembly synchronousSource = TestAssembly.Create();
        using var synchronousWorkspace = new InspectionWorkspace();
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = synchronousWorkspace.CloseAsync();
            });
        AssemblyContextGroup synchronousGroup =
            synchronousWorkspace.CreateAssemblyContextGroup(
                [synchronousSource.Participant]);
        synchronousGroup.RegisterOwnedResource(
            new ThrowingResource());
        Assert.Throws<AggregateException>(
            synchronousWorkspace.Dispose);

        TestAssembly asyncCompatibilitySource =
            TestAssembly.Create();
        var asyncCompatibilityWorkspace =
            new InspectionWorkspace();
        AssemblyContextGroup asyncCompatibilityGroup =
            asyncCompatibilityWorkspace.CreateAssemblyContextGroup(
                [asyncCompatibilitySource.Participant]);

        await asyncCompatibilityWorkspace.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(
            () => asyncCompatibilityGroup.GetAssemblyImageSpan(
                asyncCompatibilitySource.Assembly));
    }

    [Fact]
    public async Task WorkspaceClose_BrowserWasmUsesAwaitedProgressWithoutThreadBlocking()
    {
        TestAssembly source = TestAssembly.Create();
        var creationContext =
            new NonPumpingSynchronizationContext();
        SynchronizationContext? previousContext =
            SynchronizationContext.Current;
        InspectionWorkspace workspace;
        AssemblyContextGroup group;
        try
        {
            SynchronizationContext.SetSynchronizationContext(
                creationContext);
            workspace =
                InspectionWorkspace.CreateAsynchronous();
            group = workspace.CreateAssemblyContextGroup(
                [source.Participant]);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(
                previousContext);
        }
        var callbackEntered =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackResume =
            new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AssemblyImageAccessResult<int>> operation =
            group.UseAndReleaseAssemblySessionAsync(
                source.Assembly,
                async (_, _) =>
                {
                    callbackEntered.SetResult();
                    return await callbackResume.Task;
                });
        await callbackEntered.Task;

        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        Assert.Throws<ObjectDisposedException>(
            () => group.GetAssemblyImageSpan(source.Assembly));

        callbackResume.SetResult(42);
        var available = Assert.IsType<
            AssemblyImageAccessResult<int>.Available>(
                await operation);
        Assert.Equal(0, creationContext.PostCount);
        InspectionWorkspaceCloseReport report =
            await close.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.Equal(42, available.Value);
        Assert.True(
            Assert.IsType<
                InspectionWorkspaceDirectGroupCloseResult>(
                    Assert.Single(report.Groups))
                .Succeeded);
    }

    [Fact]
    public async Task WorkspaceClose_DirectAndCoordinatedGroupsReleaseExactlyOnce()
    {
        TestAssembly directSource = TestAssembly.Create();
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        AssemblyContextGroup direct =
            workspace.CreateAssemblyContextGroup(
                [directSource.Participant]);
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Workspace.Mixed.Release");
        PackageAssemblyContextCompletion completion =
            await ExecutePackageCompletionAsync(
                workspace,
                binding);
        var directResource = new CountingResource();
        var coordinatedResource = new CountingResource();
        direct.RegisterOwnedResource(directResource);
        completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(
            coordinatedResource);
        completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(
            new ThrowingResource());

        Task<PackageRoleCleanupReport> packageClose =
            completion.CloseAsync();
        Task<InspectionWorkspaceCloseReport> workspaceClose =
            workspace.CloseAsync();
        await Task.WhenAll(packageClose, workspaceClose);

        Assert.Equal(1, directResource.DisposeCount);
        Assert.Equal(1, coordinatedResource.DisposeCount);
        InspectionWorkspaceCloseReport workspaceReport =
            await workspaceClose;
        Assert.Equal(2, workspaceReport.Groups.Length);
        Assert.IsType<InspectionWorkspaceDirectGroupCloseResult>(
            workspaceReport.Groups[0]);
        var coordinated = Assert.IsType<
            InspectionWorkspaceCoordinatedGroupCloseResult<
                PackageRoleGroupCleanupRecord>>(
                    workspaceReport.Groups[1]);
        PackageRoleGroupCleanupRecord packageResult =
            Assert.Single((await packageClose).Groups);
        Assert.IsType<PackageRoleGroupCleanupRecord.Failed>(
            packageResult);
        Assert.Same(packageResult, coordinated.Result);
        Assert.Equal([0, 1], workspaceReport.Groups
            .Select(result => result.RegistrationIndex));
    }

    [Fact]
    public async Task WorkspaceClose_ExistingCoordinatedLeaseRemainsUsableUntilOwnerRelease()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Workspace.Existing.Lease");
        PackageAssemblyContextCompletion completion =
            await ExecutePackageCompletionAsync(
                workspace,
                binding);
        PackageAssemblyContextProjection projection =
            completion.CreateProjection([binding]);
        PackageAssemblyContextRoleProjection role =
            projection.SurfaceRole;
        var resource = new CountingResource();
        completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(
            resource);

        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();

        Assert.False(close.IsCompleted);
        Assert.Throws<ObjectDisposedException>(
            () => completion.CreateProjection([binding]));
        Assert.Single(
            AssemblyContextIntegrationsQuery.Execute(role)
                .Assemblies);
        Assert.Equal(0, resource.DisposeCount);

        await projection.ReturnAsync();
        InspectionWorkspaceCloseReport report = await close;
        Assert.Equal(1, resource.DisposeCount);
        PackageRoleCleanupReport packageReport =
            await completion.CloseAsync();
        var coordinated = Assert.IsType<
            InspectionWorkspaceCoordinatedGroupCloseResult<
                PackageRoleGroupCleanupRecord>>(
                    Assert.Single(report.Groups));
        Assert.Same(
            Assert.Single(packageReport.Groups),
            coordinated.Result);
    }

    [Fact]
    public async Task WorkspaceClose_OwnerFirstReleaseDeactivatesRegistrationAndRetainsReport()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Workspace.Owner.First");
        PackageAssemblyContextCompletion completion =
            await ExecutePackageCompletionAsync(
                workspace,
                binding);

        PackageRoleCleanupReport packageReport =
            await completion.CloseAsync();

        Assert.Throws<ObjectDisposedException>(
            () => completion.CreateProjection([binding]));
        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();
        InspectionWorkspaceCloseReport workspaceReport =
            await close;
        var coordinated = Assert.IsType<
            InspectionWorkspaceCoordinatedGroupCloseResult<
                PackageRoleGroupCleanupRecord>>(
                    Assert.Single(workspaceReport.Groups));
        Assert.Same(
            Assert.Single(packageReport.Groups),
            coordinated.Result);
        Assert.Equal(0, coordinated.RegistrationIndex);
        Assert.Same(close, workspace.CloseAsync());
        Assert.Same(workspaceReport, await workspace.CloseAsync());
    }

    [Fact]
    public async Task WorkspaceClose_CoordinatedLateGroupsCommitHistoryBeforeOwnerRelease()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SeparateBinding(
                "Workspace.Late.Coordinated");
        var entered =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var resume =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        int yields = 0;
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(
                [binding],
                options: null,
                () =>
                {
                    if (Interlocked.Increment(ref yields) == 1)
                        entered.SetResult();
                    return new ValueTask(resume.Task);
                });
        Task<PackageAssemblyContextCompletion> construction =
            operation.ExecuteAsync(operation.Identity);
        await entered.Task;

        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();

        Assert.False(close.IsCompleted);
        resume.SetResult();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await construction);
        InspectionWorkspaceCloseReport report = await close;

        Assert.Equal(2, report.Groups.Length);
        Assert.Equal(
            [0, 1],
            report.Groups
                .Select(result => result.RegistrationIndex));
        Assert.All(
            report.Groups,
            result =>
            {
                var coordinated = Assert.IsType<
                    InspectionWorkspaceCoordinatedGroupCloseResult<
                        PackageRoleGroupCleanupRecord>>(result);
                Assert.IsType<
                    PackageRoleGroupCleanupRecord.Released>(
                        coordinated.Result);
                Assert.Same(
                    operation.Identity,
                    coordinated.Result.Group.Operation);
            });
    }

    [Fact]
    public void ConcurrentDisposal_AfterAsyncCallbackEnds_PreservesOwnedResourceDisposalOrder()
    {
        TestAssembly source = TestAssembly.Create();
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);
        var resource = new RetainedImageAssertingResource(group);
        group.RegisterOwnedResource(resource);

        object participant =
            InvokeGroupMethod(
                group,
                "FindParticipant",
                source.Assembly)!;
        object imageLoadGate =
            participant.GetType().GetProperty(
                "ImageLoadGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(participant)!;
        using var callbackEntered = new ManualResetEventSlim();
        using var callbackResume = new ManualResetEventSlim();
        using var callbackResumed = new ManualResetEventSlim();
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Exception? operationFailure = null;
        var operation = new Thread(
            () =>
            {
                try
                {
                    AssemblyImageAccessResult<int> result =
                        group.UseAndReleaseAssemblySessionAsync(
                                source.Assembly,
                                (_, _) =>
                                {
                                    callbackEntered.Set();
                                    callbackResume.Wait(cancellationToken);
                                    callbackResumed.Set();
                                    return Task.FromResult(1);
                                })
                            .GetAwaiter()
                            .GetResult();
                    Assert.IsType<
                        AssemblyImageAccessResult<int>.Available>(result);
                }
                catch (Exception ex)
                {
                    operationFailure = ex;
                }
            });
        operation.IsBackground = true;
        operation.Start();
        Assert.True(
            callbackEntered.Wait(
                TimeSpan.FromSeconds(10),
                cancellationToken));

        lock (imageLoadGate)
        {
            callbackResume.Set();
            Assert.True(
                callbackResumed.Wait(
                    TimeSpan.FromSeconds(10),
                    cancellationToken));
            Assert.True(
                SpinWait.SpinUntil(
                    () => (operation.ThreadState & ThreadState.WaitSleepJoin)
                        != 0,
                    TimeSpan.FromSeconds(10)));

            workspace.Dispose();
            Assert.True(group.RetainedImageBytes > 0);
        }

        Assert.True(operation.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(operationFailure);
        Assert.True(resource.IsDisposed);
        Assert.Equal(0, group.RetainedImageBytes);
    }

    [Fact]
    public void WorkspaceDisposal_ContinuesAfterAGroupFails()
    {
        TestAssembly first = TestAssembly.Create();
        TestAssembly second = TestAssembly.Create();
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup failingGroup =
            workspace.CreateAssemblyContextGroup(
                [first.Participant]);
        AssemblyContextGroup laterGroup =
            workspace.CreateAssemblyContextGroup(
                [second.Participant]);
        Assert.True(
            failingGroup.GetAssemblyImageSpan(first.Assembly).IsAvailable);
        Assert.True(
            laterGroup.GetAssemblyImageSpan(second.Assembly).IsAvailable);
        failingGroup.RegisterOwnedResource(new ThrowingResource());

        Assert.Throws<AggregateException>(workspace.Dispose);

        Assert.Equal(0, failingGroup.RetainedImageBytes);
        Assert.Equal(0, laterGroup.RetainedImageBytes);
        Assert.Throws<ObjectDisposedException>(
            () => laterGroup.UseAssemblyImage(
                second.Assembly,
                static image => image.Content.Length));
    }

    [Fact]
    public void CallbackFailure_IsPreservedWhenDeferredDisposalAlsoFails()
    {
        TestAssembly source = TestAssembly.Create();
        using var workspace = new InspectionWorkspace();
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [source.Participant]);
        group.RegisterOwnedResource(new ThrowingResource());

        AggregateException failure = Assert.Throws<AggregateException>(
            () => group.UseContext<int>(
                () =>
                {
                    group.Dispose();
                    throw new NotSupportedException(
                        "Synthetic callback failure.");
                }));

        IReadOnlyCollection<Exception> failures =
            failure.Flatten().InnerExceptions;
        Assert.Contains(
            failures,
            ex => ex is NotSupportedException
                && ex.Message == "Synthetic callback failure.");
        Assert.Contains(
            failures,
            ex => ex is InvalidOperationException
                && ex.Message
                    == "Synthetic owned-resource disposal failure.");
    }

    [Fact]
    public void ImageViewAndSpanResult_AreStackOnly()
    {
        Assert.True(typeof(AssemblyImageView).IsByRefLike);
        Assert.True(typeof(AssemblyImageSpanResult).IsByRefLike);
    }

    static object? InvokeGroupMethod(
        AssemblyContextGroup group,
        string methodName,
        params object?[]? arguments)
    {
        MethodInfo method =
            typeof(AssemblyContextGroup).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"AssemblyContextGroup.{methodName} was not found.");
        return method.Invoke(group, arguments);
    }

    // These operations intentionally block on test gates. Dedicated workers
    // keep ThreadPool injection timing from consuming loaded-CI readiness budgets.
    static Task<T> StartConcurrent<T>(Func<T> action) =>
        Task.Factory.StartNew(
            action,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    static async Task<PackageAssemblyContextCompletion>
        ExecutePackageCompletionAsync(
            InspectionWorkspace workspace,
            PackageRootBinding binding)
    {
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion(
                [binding]);
        return await operation.ExecuteAsync(operation.Identity);
    }

    sealed class TestAssembly
    {
        int _openCount;

        TestAssembly(
            byte[] bytes,
            ResolvedAssemblyReference assembly)
        {
            Bytes = bytes;
            Assembly = assembly;
            Participant = new AssemblyContextParticipant(
                assembly,
                MissingBindingPolicy.Instance);
        }

        internal byte[] Bytes { get; }
        internal ResolvedAssemblyReference Assembly { get; }
        internal AssemblyContextParticipant Participant { get; }
        internal Action? BeforeOpen { get; set; }
        internal int OpenCount =>
            System.Threading.Volatile.Read(ref _openCount);

        internal static TestAssembly Create()
        {
            byte[] bytes = File.ReadAllBytes(
                typeof(InspectionWorkspaceTests).Assembly.Location);
            ResolvedAssemblyReference identity =
                ResolvedAssemblyReference.CreateFromPath(
                    typeof(InspectionWorkspaceTests).Assembly.Location,
                    AssemblyResolutionProvenance.Local(
                        "workspace lifetime test identity"));
            TestAssembly? source = null;
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    identity.Identity,
                    path: null,
                    () =>
                    {
                        Interlocked.Increment(
                            ref source!._openCount);
                        source.BeforeOpen?.Invoke();
                        return new MemoryStream(
                            source.Bytes,
                            writable: false);
                    },
                    AssemblyResolutionProvenance.Local(
                        "workspace lifetime test"));
            source = new TestAssembly(bytes, assembly);
            return source;
        }
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        internal static MissingBindingPolicy Instance { get; } =
            new();

        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
    }

    sealed class ThrowingDisposeMemoryStream(byte[] bytes)
        : MemoryStream(bytes, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                throw new InvalidOperationException(
                    "Synthetic disposal failure.");
            }
        }
    }

    sealed class ThrowingResource : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException(
                "Synthetic owned-resource disposal failure.");
    }

    sealed class CountingResource : IDisposable
    {
        int _disposeCount;

        internal int DisposeCount =>
            Volatile.Read(ref _disposeCount);

        public void Dispose() =>
            Interlocked.Increment(ref _disposeCount);
    }

    sealed class BlockingResource(
        ManualResetEventSlim entered,
        ManualResetEventSlim resume,
        CancellationToken cancellationToken)
        : IDisposable
    {
        public void Dispose()
        {
            entered.Set();
            resume.Wait(cancellationToken);
        }
    }

    sealed class NonPumpingSynchronizationContext :
        SynchronizationContext
    {
        int _postCount;

        internal int PostCount =>
            Volatile.Read(ref _postCount);

        public override void Post(
            SendOrPostCallback callback,
            object? state) =>
            Interlocked.Increment(ref _postCount);
    }

    sealed class RetainedImageAssertingResource(
        AssemblyContextGroup group)
        : IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public void Dispose()
        {
            Assert.True(group.RetainedImageBytes > 0);
            IsDisposed = true;
        }
    }
}
