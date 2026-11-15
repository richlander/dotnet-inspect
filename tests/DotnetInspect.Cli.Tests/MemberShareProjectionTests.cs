using System.Diagnostics.CodeAnalysis;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Gates the bounded compile-asset read that Member Share performs over retained
/// package content, whose entries may be forward-only pull streams.
/// </summary>
public sealed class MemberShareProjectionTests
{
    const string AssetPath = "lib/net10.0/Library.dll";

    static readonly byte[] Image =
        File.ReadAllBytes(typeof(IPackageContent).Assembly.Location);

    [Fact]
    public void ForwardOnlyEntryOverTheBoundIsRefusedWithoutBufferingIt()
    {
        long bound = Image.Length / 4;
        var content = new Content(declaresLength: false);

        ApiSurface? surface = MemberShareProjection.ReadBrowserTypeSurface(
            content, AssetPath, bound, out string? refusal);

        Assert.Null(surface);
        Assert.Contains("assembly-entry limit", refusal);
        Assert.Equal(bound, content.RequestedMaxBytes);
        Assert.True(content.Opened!.BytesRead < Image.Length);
        Assert.True(content.Opened.Disposed);
    }

    [Fact]
    public void DeclaredLengthOverTheBoundIsRefusedBeforeOpening()
    {
        var content = new Content(declaresLength: true);

        ApiSurface? surface = MemberShareProjection.ReadBrowserTypeSurface(
            content, AssetPath, Image.Length - 1, out string? refusal);

        Assert.Null(surface);
        Assert.Contains("assembly-entry limit", refusal);
        Assert.Null(content.Opened);
    }

    [Fact]
    public void ForwardOnlyEntryWithinTheBoundReadsTypes()
    {
        var content = new Content(declaresLength: false);

        ApiSurface? surface = MemberShareProjection.ReadBrowserTypeSurface(
            content, AssetPath, Image.Length, out string? refusal);

        Assert.NotNull(surface);
        Assert.Null(refusal);
        Assert.Contains(
            surface.Types,
            type => type.FullName == typeof(IPackageContent).FullName);
    }

    [Fact]
    public void DefaultBoundIsTheRealizationAssemblyEntryLimit()
    {
        Assert.Equal(
            new DotnetInspector.Queries.PackageAssemblyContextRealizationOptions()
                .MaxAssemblyEntryBytes,
            MemberShareProjection.MaxCompileAssetBytes);
    }

    sealed class Content(bool declaresLength) :
        IPackageContent,
        IPackageContentEntryManifest
    {
        public ForwardOnlyStream? Opened { get; private set; }

        public long? RequestedMaxBytes { get; private set; }

        public string? RootPath => null;

        public string? NupkgPath => null;

        public bool FromCache => true;

        public string ProducerKey => "test";

        public bool RequiresArchiveTreeMatch => false;

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream) =>
            TryOpenEntry(relativePath, long.MaxValue, out stream);

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            RequestedMaxBytes = maxExpandedBytes;
            stream = Opened = new ForwardOnlyStream(Image);
            return relativePath == AssetPath;
        }

        public IEnumerable<string> EnumerateEntries() => [AssetPath];

        public bool TryGetEntryLength(string relativePath, out long length)
        {
            length = Image.Length;
            return declaresLength && relativePath == AssetPath;
        }

        public PackageContentEntryScanner CreateEntryScanner() =>
            throw new NotSupportedException();
    }

    sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        readonly MemoryStream _inner = new(bytes, writable: false);

        public long BytesRead { get; private set; }

        public bool Disposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
