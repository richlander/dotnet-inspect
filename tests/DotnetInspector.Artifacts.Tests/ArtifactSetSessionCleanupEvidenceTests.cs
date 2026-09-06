using DotnetInspector.Artifacts.Workspaces;

namespace DotnetInspector.Artifacts.Tests;

/// <summary>
/// Materialization cleanup evidence stays strictly secondary to the condition
/// the session is already reporting.
/// </summary>
public sealed class ArtifactSetSessionCleanupEvidenceTests
{
    [Fact]
    public async Task DisposalFailureDoesNotReplaceCancelledRead()
    {
        using var cancellation = new CancellationTokenSource();
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution artifact = scope.Register(
                    new CleanupProvenance("cancelled-read"),
                    _ => new HookedStream(
                        new byte[64],
                        failDispose: true,
                        onRead: cancellation.Cancel));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [artifact],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: TestContext.Current.CancellationToken);

        OperationCanceledException cancelled =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await session.SealAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Exception disposal = Assert.Single(
            ArtifactSetSession.GetCleanupFailures(cancelled));
        Assert.Equal(
            "synthetic disposal failure",
            Assert.IsType<IOException>(disposal).Message);
    }

    [Fact]
    public async Task DisposalFailureDoesNotReplaceReadFailure()
    {
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution artifact = scope.Register(
                    new CleanupProvenance("failed-read"),
                    _ => new HookedStream(
                        new byte[64],
                        failDispose: true,
                        failRead: () => new IOException("synthetic read failure")));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [artifact],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await session.SealAsync(
                    TestContext.Current.CancellationToken));

        // The original read failure classifies the outcome; disposal evidence
        // is retained separately rather than replacing it.
        Assert.Equal(
            "artifact.session.materialization-failed",
            Assert.Single(rejected.Failures).Diagnostic.Code);
        Exception disposal = Assert.Single(session.CleanupFailures);
        Assert.Equal(
            "synthetic disposal failure",
            Assert.IsType<IOException>(disposal).Message);
    }

    [Fact]
    public void AttachedCleanupEvidenceMergesRatherThanReplaces()
    {
        var primary = new InvalidOperationException("primary");
        var first = new IOException("first cleanup failure");
        var second = new IOException("second cleanup failure");

        ArtifactSetSession.AttachCleanupFailures(primary, [first]);
        // A later release sweep over the same failure must add to the earlier
        // evidence, not replace it; the Workspace root realizer attaches this
        // way after transfer fails.
        ArtifactSetSession.AttachCleanupFailures(primary, [second]);

        Assert.Equal(
            [first, second],
            ArtifactSetSession.GetCleanupFailures(primary));

        ArtifactSetSession.AttachCleanupFailures(primary, []);
        Assert.Equal(
            [first, second],
            ArtifactSetSession.GetCleanupFailures(primary));

        Assert.Empty(
            ArtifactSetSession.GetCleanupFailures(
                new InvalidOperationException("unrelated")));
    }

    [Fact]
    public async Task DisposalFailureOnSuccessfulReadStillPropagates()
    {
        await using var session = new ArtifactSetSession();
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution artifact = scope.Register(
                    new CleanupProvenance("successful-read"),
                    _ => new HookedStream(new byte[64], failDispose: true));
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [artifact],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var rejected =
            Assert.IsType<ArtifactSetPublicationOutcome.NotPublished>(
                await session.SealAsync(
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            "artifact.session.materialization-failed",
            Assert.Single(rejected.Failures).Diagnostic.Code);
    }

    private sealed record CleanupProvenance(string Source) :
        IArtifactProvenance;

    private sealed class HookedStream(
        byte[] content,
        bool failDispose,
        Func<Exception>? failRead = null,
        Action? onRead = null) : Stream
    {
        readonly MemoryStream _source = new(content, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _source.Length;

        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            onRead?.Invoke();
            if (failRead is not null)
                throw failRead();
            return _source.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (failRead is not null)
                throw failRead();
            return ValueTask.FromResult(_source.Read(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            _source.Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _source.Dispose();
                if (failDispose)
                    throw new IOException("synthetic disposal failure");
            }

            base.Dispose(disposing);
        }
    }
}
