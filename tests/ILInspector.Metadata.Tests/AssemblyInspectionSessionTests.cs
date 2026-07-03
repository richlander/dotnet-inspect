using System.Linq;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for the assembly inspection substrate (<see cref="AssemblyImage"/> /
/// <see cref="AssemblyInspectionSession"/>): the shared-open hub that produces assembly facets
/// over a single <c>PEReader</c>. See <c>docs/design/assembly-inspection-query.md</c>.
/// </summary>
public class AssemblyInspectionSessionTests
{
    static string SelfPath => typeof(AssemblyInspectionSessionTests).Assembly.Location;
    const string SelfName = "ILInspector.Metadata.Tests";

    [Fact]
    public void Open_FromPath_HasMetadata()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        Assert.True(session.HasMetadata);
    }

    [Fact]
    public void AssemblyInfo_ReturnsAssemblyName()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        Assert.Equal(SelfName, session.AssemblyInfo().AssemblyName);
    }

    [Fact]
    public void Facets_ProduceOverSharedReader_WithoutReopening()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);

        // Many facets, one open — none should throw, and the surface should be populated.
        Assert.NotEmpty(session.ApiSurface(includeAll: true).Types);
        Assert.NotNull(session.Resources());
        Assert.NotNull(session.CustomAttributes());
        Assert.NotNull(session.ClassifiedMethods());
        Assert.NotNull(session.TypeForwarders());
        Assert.NotNull(session.UnionTypes());
        Assert.NotNull(session.Switches());
        Assert.NotNull(session.OpenTelemetrySignals());
        Assert.NotNull(session.EcosystemIntegrations());
        Assert.NotNull(session.AuditMetadata());
        Assert.NotNull(session.ExtensionMethods().ToList());
    }

    [Fact]
    public void Open_FromResolvedReference_MatchesPathOpen()
    {
        var reference = new ResolvedAssemblyReference(
            new AssemblyReferenceIdentity(SelfName, Version: null, Culture: null, PublicKeyToken: null),
            SelfPath,
            () => File.OpenRead(SelfPath));

        using var fromRef = AssemblyInspectionSession.Open(reference);
        using var fromPath = AssemblyInspectionSession.Open(SelfPath);

        Assert.True(fromRef.HasMetadata);
        Assert.Equal(fromPath.AssemblyInfo().AssemblyName, fromRef.AssemblyInfo().AssemblyName);
        Assert.Equal(
            fromPath.ApiSurface(includeAll: true).Types.Count,
            fromRef.ApiSurface(includeAll: true).Types.Count);
    }

    [Fact]
    public void AssemblyImage_Open_HasMetadata()
    {
        using var image = AssemblyImage.Open(SelfPath);
        Assert.True(image.HasMetadata);
    }

    [Fact]
    public void AssemblyImage_DisposesBackingStreamExactlyOnce()
    {
        var stream = new DisposeCountingStream(File.OpenRead(SelfPath));
        var reference = new ResolvedAssemblyReference(
            new AssemblyReferenceIdentity(SelfName, Version: null, Culture: null, PublicKeyToken: null),
            SelfPath,
            () => stream);

        var image = AssemblyImage.Open(reference);
        try
        {
            Assert.True(image.HasMetadata);
            Assert.Equal(0, stream.DisposeCount);
        }
        finally
        {
            image.Dispose();
        }

        Assert.Equal(1, stream.DisposeCount);
    }

    sealed class DisposeCountingStream(Stream inner) : Stream
    {
        bool _innerDisposed;

        public int DisposeCount { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                if (!_innerDisposed)
                {
                    inner.Dispose();
                    _innerDisposed = true;
                }
            }

            base.Dispose(disposing);
        }
    }
}
