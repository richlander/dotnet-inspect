using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// Owns a single opened PE image (backing stream + <see cref="PEReader"/>) so that an inspection
/// parses the file once and shares the reader across scanners, instead of every caller opening its
/// own <see cref="PEReader"/>.
///
/// The reader is metadata-internal: it is not exposed to callers outside this assembly. Consumers
/// go through <see cref="AssemblyInspectionSession"/>, which produces facets over the shared image.
/// See <c>docs/design/assembly-inspection-query.md</c>.
/// </summary>
public sealed class AssemblyImage : IDisposable
{
    readonly Stream? _stream;
    readonly Action? _ensureLenderAlive;
    bool _disposed;

    internal PEReader PEReader { get; }

    AssemblyImage(Stream? stream, PEReader peReader, Action? ensureLenderAlive = null)
    {
        _stream = stream;
        PEReader = peReader;
        _ensureLenderAlive = ensureLenderAlive;
    }

    /// <summary>Whether the image contains managed metadata (false for a native binary).</summary>
    public bool HasMetadata => PEReader.HasMetadata;

    /// <summary>Opens an image from a file path.</summary>
    public static AssemblyImage Open(string path) => FromStream(File.OpenRead(path));

    /// <summary>
    /// Wraps an image another component already opened, without taking ownership of it. Use this
    /// to give the facet surface a second reader over the <em>same bytes</em> rather than a second
    /// open of the same path: two opens of one path are two different files whenever the path is
    /// retargeted between them, and the result mixes two assemblies while exiting zero.
    ///
    /// <paramref name="ensureLenderAlive"/> is the lender's liveness check, and it is load-bearing
    /// rather than defensive. A borrow does not control its reader's lifetime, and its own
    /// <c>_disposed</c> flag says nothing about the lender's. Most facets happen to fail loudly
    /// anyway because the disposed <see cref="PEReader"/> throws, but a
    /// <see cref="MethodBodySource"/> handed out <em>before</em> the lender was disposed holds
    /// pointers into the unmapped image and reads freed memory: an
    /// <see cref="AccessViolationException"/> that kills the process rather than an exception a
    /// caller can map. Checking the lender is what turns that into
    /// <see cref="ObjectDisposedException"/>.
    ///
    /// <see cref="Dispose"/> releases only the borrow.
    ///
    /// Gate: <c>BorrowedSession_FailsLoudlyAfterTheLenderIsDisposed</c>.
    /// </summary>
    internal static AssemblyImage Borrow(PEReader peReader, Action ensureLenderAlive)
        => new(stream: null, peReader, ensureLenderAlive);

    /// <summary>
    /// Opens an image from a resolved assembly reference, using its stream opener. This is the
    /// descriptor-based entry point that keeps callers off bare paths.
    /// </summary>
    public static AssemblyImage Open(ResolvedAssemblyReference reference) => FromStream(reference.OpenRead());

    static AssemblyImage FromStream(Stream stream)
    {
        try
        {
            // LeaveOpen: this AssemblyImage is the sole owner of the stream and disposes it
            // explicitly in Dispose(), so the PEReader must not also take ownership.
            return new AssemblyImage(stream, new PEReader(stream, PEStreamOptions.LeaveOpen));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal MetadataReader GetMetadataReader() => PEReader.GetMetadataReader();

    internal void EnsureAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A borrow's own flag says nothing about whether the lender still holds the image open.
        _ensureLenderAlive?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // A borrowed image owns neither the reader nor a stream; the opener disposes both.
        if (_stream is null)
            return;

        PEReader.Dispose();
        _stream.Dispose();
    }
}
