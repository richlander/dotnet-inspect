using System.Reflection;

namespace DotnetInspector.Artifacts.Tests;

public sealed class ArtifactContractTests
{
    [Fact]
    public void IdentityAndRegistration_AreScopedToOwningGeneration()
    {
        var firstOwner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization firstAuthorization =
            firstOwner.CreateAdmissionAuthorization();
        ArtifactContribution first;
        ArtifactContribution second;
        using (ArtifactContributionScope scope =
            firstOwner.BeginContribution(firstAuthorization))
        {
            first = scope.Register(
                new Provenance("first"),
                _ => StreamFor([1]));
            second = scope.Register(
                new Provenance("second"),
                _ => StreamFor([2]));
        }

        var secondOwner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization secondAuthorization =
            secondOwner.CreateAdmissionAuthorization();
        ArtifactContribution foreign;
        using (ArtifactContributionScope scope =
            secondOwner.BeginContribution(secondAuthorization))
        {
            foreign = scope.Register(
                new Provenance("foreign"),
                _ => StreamFor([3]));
        }

        Assert.NotSame(first.Descriptor.Identity, second.Descriptor.Identity);
        Assert.NotSame(first.Descriptor.Identity, foreign.Descriptor.Identity);
        Assert.Same(
            first.Descriptor.Identity,
            first.Registration.Artifact);
        Assert.Same(
            firstOwner.Generation,
            first.Descriptor.Identity.Generation);
        Assert.Equal(0, first.Descriptor.Identity.Ordinal);
        Assert.Equal(1, second.Descriptor.Identity.Ordinal);
        Assert.Equal(0, foreign.Descriptor.Identity.Ordinal);
        Assert.Throws<ArgumentException>(
            () => secondOwner.CreateRetainedContent(
                first.Registration,
                _ => StreamFor([4])));
    }

    [Fact]
    public void ContributionScope_RejectsLateRegistrationAndOpen()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization authorization =
            owner.CreateAdmissionAuthorization();
        ArtifactContributionScope scope =
            owner.BeginContribution(authorization);
        ArtifactContribution contribution = scope.Register(
            new Provenance("local"),
            _ => StreamFor([1, 2, 3]));
        ArtifactAdmissionLease lease = owner.IssueLease(authorization);
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => scope.Register(
                new Provenance("late"),
                _ => StreamFor([4])));

        using Stream opened = contribution.OpenRead(lease);
        owner.CreateRetainedContent(
            contribution.Registration,
            _ => StreamFor([1, 2, 3]));
        owner.CompleteAdmission(authorization);

        Assert.Equal(1, opened.ReadByte());
        Assert.Throws<UnauthorizedAccessException>(
            () => contribution.OpenRead(lease));

        lease.Dispose();
    }

    [Fact]
    public void AdmissionCannotCompleteWhileContributionScopeIsActive()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization authorization =
            owner.CreateAdmissionAuthorization();
        using ArtifactContributionScope scope =
            owner.BeginContribution(authorization);

        Assert.Throws<InvalidOperationException>(
            () => owner.CompleteAdmission(authorization));
    }

    [Fact]
    public void AdmissionRequiresExactlyOneRetainedContentPerRegistration()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization authorization =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(authorization))
        {
            contribution = scope.Register(
                new Provenance("local"),
                _ => StreamFor([1]));
        }

        Assert.Throws<InvalidOperationException>(
            () => owner.CompleteAdmission(authorization));

        owner.CreateRetainedContent(
            contribution.Registration,
            _ => StreamFor([1]));
        Assert.Throws<InvalidOperationException>(
            () => owner.CreateRetainedContent(
                contribution.Registration,
                _ => StreamFor([2])));

        owner.CompleteAdmission(authorization);
    }

    [Fact]
    public async Task GenerationAuthority_ConcurrentScopesMintUniqueOrderedIdentities()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization authorization =
            owner.CreateAdmissionAuthorization();

        ArtifactContribution[] contributions =
            await Task.WhenAll(
                Enumerable.Range(0, 100)
                    .Select(index => Task.Run(() =>
                    {
                        using ArtifactContributionScope scope =
                            owner.BeginContribution(authorization);
                        return scope.Register(
                            new Provenance(index.ToString()),
                            _ => StreamFor([unchecked((byte)index)]));
                    })));

        Assert.Equal(
            Enumerable.Range(0, contributions.Length)
                .Select(index => (long)index),
            contributions
                .Select(contribution =>
                    contribution.Descriptor.Identity.Ordinal)
                .Order());
        Assert.Equal(
            contributions.Length,
            contributions
                .Select(contribution => contribution.Descriptor.Identity)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Count());

        foreach (ArtifactContribution contribution in contributions)
        {
            owner.CreateRetainedContent(
                contribution.Registration,
                _ => StreamFor([1]));
        }
        owner.CompleteAdmission(authorization);
    }

    [Fact]
    public void RetainedContent_RejectsRevokedOrForeignAuthorizationWithoutRevokingOpenStream()
    {
        byte[] snapshot = [10, 20, 30];
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("snapshot"),
                _ => StreamFor(snapshot));
        }

        RetainedArtifactContent retained =
            owner.CreateRetainedContent(
                contribution.Registration,
                _ => StreamFor(snapshot));
        owner.CompleteAdmission(admission);

        ArtifactQueryAuthorization query =
            owner.CreateQueryAuthorization();
        using ArtifactQueryLease queryLease = owner.IssueLease(query);
        using Stream opened = retained.OpenRead(queryLease);

        ArtifactQueryAuthorization replacement =
            owner.ReplaceQueryAuthorization(query);
        Assert.Equal(10, opened.ReadByte());
        Assert.Throws<UnauthorizedAccessException>(
            () => retained.OpenRead(queryLease));

        using ArtifactQueryLease replacementLease =
            owner.IssueLease(replacement);
        using Stream replacementOpen =
            retained.OpenRead(replacementLease);
        Assert.Equal(10, replacementOpen.ReadByte());

        var foreignOwner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization foreignAdmission =
            foreignOwner.CreateAdmissionAuthorization();
        foreignOwner.CompleteAdmission(foreignAdmission);
        ArtifactQueryAuthorization foreignQuery =
            foreignOwner.CreateQueryAuthorization();
        using ArtifactQueryLease foreignLease =
            foreignOwner.IssueLease(foreignQuery);
        Assert.Throws<UnauthorizedAccessException>(
            () => retained.OpenRead(foreignLease));

        owner.EndGeneration();
        Assert.Equal(20, replacementOpen.ReadByte());
        Assert.Throws<UnauthorizedAccessException>(
            () => retained.OpenRead(replacementLease));
        Assert.Throws<ObjectDisposedException>(
            owner.CreateQueryAuthorization);
    }

    [Fact]
    public async Task ArtifactAccess_OpenRegistrationIsAtomicWithGenerationEnd()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("blocking-opener"),
                cancellationToken =>
                {
                    entered.TrySetResult();
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();
                    return StreamFor([1]);
                });
        }
        using ArtifactAdmissionLease lease = owner.IssueLease(admission);

        Task<Stream> opening =
            Task.Run(() => contribution.OpenRead(lease));
        await entered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        Task ending = owner.EndGenerationAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await opening);
        await ending;
        Assert.Throws<UnauthorizedAccessException>(
            () => contribution.OpenRead(lease));
    }

    [Fact]
    public async Task ArtifactAccess_RetainedOpenerIsCancelledAfterGateAdmission()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("retained-opener"),
                _ => StreamFor([1]));
        }
        RetainedArtifactContent retained =
            owner.CreateRetainedContent(
                contribution.Registration,
                cancellationToken =>
                {
                    entered.TrySetResult();
                    cancellationToken.WaitHandle.WaitOne();
                    cancellationToken.ThrowIfCancellationRequested();
                    return StreamFor([1]);
                });
        owner.CompleteAdmission(admission);
        ArtifactQueryAuthorization query =
            owner.CreateQueryAuthorization();
        using ArtifactQueryLease lease = owner.IssueLease(query);

        Task<Stream> opening =
            Task.Run(() => retained.OpenRead(lease));
        await entered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        Task ending = owner.EndGenerationAsync().AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await opening);
        await ending;
    }

    [Fact]
    public async Task ArtifactAccess_AuthorizationReplacementIsAtomicWithOpenRegistration()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int openCount = 0;
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("replacement-race"),
                _ => StreamFor([1]));
        }
        RetainedArtifactContent retained =
            owner.CreateRetainedContent(
                contribution.Registration,
                _ =>
                {
                    Interlocked.Increment(ref openCount);
                    entered.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                    return StreamFor([1]);
                });
        owner.CompleteAdmission(admission);
        ArtifactQueryAuthorization query =
            owner.CreateQueryAuthorization();
        using ArtifactQueryLease lease = owner.IssueLease(query);

        Task<Stream> opening =
            Task.Run(() => retained.OpenRead(lease));
        await entered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        ArtifactQueryAuthorization replacement =
            owner.ReplaceQueryAuthorization(query);
        release.SetResult();

        using Stream opened = await opening;
        Assert.Equal(1, opened.ReadByte());
        Assert.Throws<UnauthorizedAccessException>(
            () => retained.OpenRead(lease));
        Assert.Equal(1, Volatile.Read(ref openCount));
        using ArtifactQueryLease replacementLease =
            owner.IssueLease(replacement);
    }

    [Fact]
    public async Task ArtifactAccess_MaterializationReadPreservesCallerCancellation()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("caller-cancelled-read"),
                _ => new CancellationCaptureReadStream(
                    entered,
                    cancellationObserved));
        }
        using ArtifactAdmissionLease lease = owner.IssueLease(admission);
        using Stream opened = contribution.OpenRead(lease);
        using var cancellation = new CancellationTokenSource();

        Task<int> read =
            opened.ReadAsync(
                    new byte[1],
                    cancellation.Token)
                .AsTask();
        await entered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await read);
        await cancellationObserved.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            cancellation.Token,
            exception.CancellationToken);
        using ArtifactContributionScope stillActive =
            owner.BeginContribution(admission);
    }

    [Fact]
    public async Task ArtifactAccess_ReturnedStreamKeepsGenerationAliveUntilDisposed()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("snapshot"),
                _ => StreamFor([1, 2]));
        }
        RetainedArtifactContent retained =
            owner.CreateRetainedContent(
                contribution.Registration,
                _ => StreamFor([1, 2]));
        owner.CompleteAdmission(admission);
        ArtifactQueryAuthorization query =
            owner.CreateQueryAuthorization();
        using ArtifactQueryLease lease = owner.IssueLease(query);
        Stream opened = retained.OpenRead(lease);

        Task ending = owner.EndGenerationAsync().AsTask();

        Assert.False(ending.IsCompleted);
        Assert.Equal(1, opened.ReadByte());
        Assert.Throws<UnauthorizedAccessException>(
            () => retained.OpenRead(lease));

        opened.Dispose();
        await ending;
    }

    [Fact]
    public void ArtifactAccess_RetainedOpenerCancellationEndsAtCallbackReturn()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("opener-scoped-cancellation"),
                _ => StreamFor([1]));
        }
        RetainedArtifactContent retained =
            owner.CreateRetainedContent(
                contribution.Registration,
                cancellationToken =>
                {
                    var stream = StreamFor([7, 8]);
                    _ = cancellationToken.Register(stream.Dispose);
                    return stream;
                });
        owner.CompleteAdmission(admission);
        ArtifactQueryAuthorization query =
            owner.CreateQueryAuthorization();
        using ArtifactQueryLease lease = owner.IssueLease(query);
        using Stream opened = retained.OpenRead(lease);

        Assert.Equal(7, opened.ReadByte());
        owner.EndGeneration();
        Assert.Equal(8, opened.ReadByte());
    }

    [Fact]
    public async Task ArtifactAccess_StreamDisposalFailureStillReportsQuiescence()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("throwing-dispose"),
                _ => StreamFor([1]));
        }
        RetainedArtifactContent retained =
            owner.CreateRetainedContent(
                contribution.Registration,
                _ => new ThrowingDisposeReadStream());
        owner.CompleteAdmission(admission);
        ArtifactQueryAuthorization query =
            owner.CreateQueryAuthorization();
        using ArtifactQueryLease lease = owner.IssueLease(query);
        Stream opened = retained.OpenRead(lease);
        Task ending = owner.EndGenerationAsync().AsTask();

        Assert.Throws<IOException>(opened.Dispose);
        await ending;
    }

    [Fact]
    public async Task ArtifactAccess_LeaseDisposalIsAtomicWithOpenRegistration()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("admitted-opener"),
                _ =>
                {
                    entered.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                    return StreamFor([1]);
                });
        }
        ArtifactAdmissionLease lease = owner.IssueLease(admission);

        Task<Stream> opening =
            Task.Run(() => contribution.OpenRead(lease));
        await entered.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        lease.Dispose();
        release.SetResult();

        using Stream opened = await opening;
        Assert.Equal(1, opened.ReadByte());
        Assert.Throws<ObjectDisposedException>(
            () => contribution.OpenRead(lease));
    }

    [Fact]
    public void DisposedLease_RejectsNewOpen()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("snapshot"),
                _ => StreamFor([1]));
        }

        RetainedArtifactContent retained =
            owner.CreateRetainedContent(
                contribution.Registration,
                _ => StreamFor([1]));
        owner.CompleteAdmission(admission);
        ArtifactQueryAuthorization query =
            owner.CreateQueryAuthorization();
        ArtifactQueryLease lease = owner.IssueLease(query);
        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => retained.OpenRead(lease));
    }

    [Fact]
    public void ArtifactDescriptor_ExposesNoUnguardedContentRoute()
    {
        PropertyInfo[] properties =
            typeof(ArtifactDescriptor).GetProperties();
        MethodInfo[] declaredMethods =
            typeof(ArtifactDescriptor).GetMethods(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);

        Assert.Equal(
            [
                nameof(ArtifactDescriptor.Identity),
                nameof(ArtifactDescriptor.Kind),
                nameof(ArtifactDescriptor.MediaType),
            ],
            properties.Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            properties,
            property =>
                typeof(Stream).IsAssignableFrom(property.PropertyType)
                || typeof(Delegate).IsAssignableFrom(property.PropertyType)
                || property.PropertyType == typeof(RetainedArtifactContent)
                || property.Name.Contains("Path", StringComparison.Ordinal));
        Assert.DoesNotContain(
            declaredMethods,
            method =>
                method.Name.Contains("Open", StringComparison.Ordinal)
                || typeof(Stream).IsAssignableFrom(method.ReturnType));
    }

    [Fact]
    public async Task AcquisitionOutcome_PreservesTypedEvidenceAndClosedArms()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(admission))
        {
            contribution = scope.Register(
                new Provenance("package"),
                _ => StreamFor([1]));
        }

        List<ArtifactContribution> source = [contribution];
        var acquired = new ArtifactAcquisitionOutcome.Acquired(
            source,
            ArtifactAcquisitionLeases.None);
        source.Clear();

        ArtifactContribution preserved = Assert.Single(acquired.Artifacts);
        Assert.IsNotType<ArtifactContribution[]>(acquired.Artifacts);
        Assert.IsType<Provenance>(
            preserved.Registration.Provenance);
        await acquired.Lease.DisposeAsync();

        var diagnostic = new Diagnostic("missing", "Artifact was not found.");
        Assert.Same(
            diagnostic,
            new ArtifactAcquisitionOutcome.Unavailable(diagnostic).Diagnostic);
        Assert.Same(
            diagnostic,
            new ArtifactAcquisitionOutcome.Rejected(diagnostic).Diagnostic);
        Assert.Same(
            diagnostic,
            new ArtifactAcquisitionOutcome.Failed(diagnostic).Diagnostic);

        Assert.Equal(
            ["Acquired", "Failed", "Rejected", "Unavailable"],
            typeof(ArtifactAcquisitionOutcome).Assembly
                .GetTypes()
                .Where(type =>
                    type != typeof(ArtifactAcquisitionOutcome)
                    && !type.IsAbstract
                    && typeof(ArtifactAcquisitionOutcome)
                        .IsAssignableFrom(type))
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal));

        ConstructorInfo constructor = Assert.Single(
            typeof(ArtifactAcquisitionOutcome).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(constructor.IsFamilyAndAssembly);
    }

    [Fact]
    public void GenerationAuthority_RetainsOnlyActiveAuthorizations()
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            owner.CreateAdmissionAuthorization();
        Assert.Equal(1, ActiveAuthorizationCount(owner));
        owner.CompleteAdmission(admission);
        Assert.Equal(0, ActiveAuthorizationCount(owner));

        ArtifactQueryAuthorization first =
            owner.CreateQueryAuthorization();
        ArtifactQueryAuthorization sibling =
            owner.CreateQueryAuthorization();
        Assert.Equal(2, ActiveAuthorizationCount(owner));

        ArtifactQueryAuthorization replacement =
            owner.ReplaceQueryAuthorization(first);
        Assert.Equal(2, ActiveAuthorizationCount(owner));
        owner.Revoke(sibling);
        Assert.Equal(1, ActiveAuthorizationCount(owner));

        owner.EndGeneration();
        Assert.Equal(0, ActiveAuthorizationCount(owner));
        Assert.Throws<ObjectDisposedException>(
            () => owner.IssueLease(replacement));
    }

    private static int ActiveAuthorizationCount(
        ArtifactGenerationAuthority owner)
    {
        FieldInfo field =
            typeof(ArtifactGenerationAuthority).GetField(
                "_authorizations",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
        object authorizations = field.GetValue(owner)!;
        return (int)authorizations.GetType()
            .GetProperty("Count")!
            .GetValue(authorizations)!;
    }

    private static MemoryStream StreamFor(byte[] content) =>
        new(content, writable: false);

    private sealed class ThrowingDisposeReadStream :
        MemoryStream
    {
        public ThrowingDisposeReadStream()
            : base([1], writable: false)
        {
        }

        protected override void Dispose(bool disposing)
        {
            throw new IOException("stream cleanup failed");
        }
    }

    private sealed class CancellationCaptureReadStream(
        TaskCompletionSource entered,
        TaskCompletionSource cancellationObserved) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length =>
            throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }

            throw new InvalidOperationException(
                "Caller cancellation was not observed.");
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed record Provenance(string Source) :
        IArtifactProvenance;

    private sealed record Diagnostic(
        string Code,
        string Summary) :
        IArtifactAcquisitionDiagnostic;
}
