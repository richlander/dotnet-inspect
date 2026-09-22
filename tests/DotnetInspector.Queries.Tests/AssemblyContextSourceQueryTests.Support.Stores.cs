using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;

using DotnetInspector.Packages;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Pipeline = ILInspector.Decompiler.Pipeline;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
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

    sealed class StateChangingPdbStore(
        Action? afterLocalPath,
        ManualResetEventSlim? disposeEntered = null,
        ManualResetEventSlim? disposeRelease = null,
        Exception? disposeFailure = null,
        Exception? positionResetFailure = null,
        int disposeFailureAt = 1,
        Exception? prefetchReadFailure = null)
        : IPdbStore
    {
        readonly InMemoryPdbStore _inner = new();
        bool _wrapNextOpen;

        internal Stream? AuthoritativeStream { get; private set; }

        public async ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            Stream? stream =
                await _inner.TryOpenAsync(
                    key,
                    cancellationToken);
            if (stream is null || !_wrapNextOpen)
                return stream;

            _wrapNextOpen = false;
            AuthoritativeStream =
                new BlockingDisposeStream(
                    stream,
                    disposeEntered,
                    disposeRelease,
                    disposeFailure,
                    positionResetFailure,
                    disposeFailureAt,
                    prefetchReadFailure);
            return AuthoritativeStream;
        }

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default) =>
            _inner.PutAsync(
                key,
                content,
                cancellationToken);

        public string? TryGetLocalPath(string key)
        {
            _wrapNextOpen = true;
            afterLocalPath?.Invoke();
            return null;
        }
    }

    sealed class BlockingDisposeStream(
        Stream inner,
        ManualResetEventSlim? entered,
        ManualResetEventSlim? release,
        Exception? disposeFailure,
        Exception? positionResetFailure = null,
        int disposeFailureAt = 1,
        Exception? prefetchReadFailure = null)
        : Stream
    {
        bool _headerReset;

        internal int DisposeCount { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set
            {
                bool headerReset =
                    value == 0
                    && inner.Position != 0;
                if (headerReset
                    && positionResetFailure is not null)
                {
                    throw positionResetFailure;
                }
                inner.Position = value;
                _headerReset |= headerReset;
            }
        }

        public override void Flush() =>
            inner.Flush();

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            ThrowPrefetchReadFailure();
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            ThrowPrefetchReadFailure();
            return inner.Read(buffer);
        }

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
            if (disposing)
            {
                DisposeCount++;
                if (DisposeCount == disposeFailureAt
                    && entered is not null)
                {
                    entered.Set();
                    Assert.True(
                        release!.Wait(
                            TimeSpan.FromSeconds(10)),
                        "Timed out waiting for PDB disposal release.");
                }
                if (DisposeCount == disposeFailureAt
                    && disposeFailure is not null)
                    throw disposeFailure;
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        void ThrowPrefetchReadFailure()
        {
            if (_headerReset
                && prefetchReadFailure is not null)
            {
                throw prefetchReadFailure;
            }
        }
    }

    sealed class CancellationOnReadStream(byte[] bytes)
        : MemoryStream(bytes, writable: false)
    {
        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new OperationCanceledException(
                "Synthetic selected-descriptor read cancellation.");

        public override int Read(Span<byte> buffer) =>
            throw new OperationCanceledException(
                "Synthetic selected-descriptor read cancellation.");
    }

    sealed class CancellationOnCanReadStream : Stream
    {
        public override bool CanRead =>
            throw new OperationCanceledException(
                "Synthetic selected-descriptor capability cancellation.");
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            0;

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            0;

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    sealed class PrimaryAndCleanupFailureStream(
        byte[] bytes,
        Exception primaryFailure,
        Exception cleanupFailure)
        : MemoryStream(bytes, writable: false)
    {
        internal int DisposeCount { get; private set; }

        public override long Length =>
            throw primaryFailure;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                base.Dispose(disposing);
                throw cleanupFailure;
            }
            base.Dispose(disposing);
        }
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

    internal sealed class FrameworkBindingPolicy
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
            AssemblyBindingSelection?>? SelectOverride
        { get; set; }
        internal AssemblyBindingPolicyVersion? SnapshotVersion { get; set; }

        internal void ChangeVersion() =>
            Version = new AssemblyBindingPolicyVersion();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                SnapshotVersion ?? Version,
                SelectCore());

            AssemblyBindingSelection SelectCore()
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
    }

    sealed class NullSnapshotPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            null!;
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                 new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(
                Path.Combine(
                    directory.FullName,
                    "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}
