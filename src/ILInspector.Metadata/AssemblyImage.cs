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
    bool _disposed;

    internal PEReader PEReader { get; }

    AssemblyImage(Stream? stream, PEReader peReader)
    {
        _stream = stream;
        PEReader = peReader;
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
    /// A borrow does not control its reader's lifetime. <see cref="Dispose"/> releases only the
    /// borrow, and using a borrow after the opener disposed it throws
    /// <see cref="ObjectDisposedException"/> from the underlying reader, exactly as using an owned
    /// image after its own disposal does.
    /// </summary>
    internal static AssemblyImage Borrow(PEReader peReader)
        => new(stream: null, peReader);

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
        => ObjectDisposedException.ThrowIf(_disposed, this);

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
