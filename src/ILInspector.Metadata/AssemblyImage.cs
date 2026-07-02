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
    readonly Stream _stream;

    internal PEReader PEReader { get; }

    AssemblyImage(Stream stream, PEReader peReader)
    {
        _stream = stream;
        PEReader = peReader;
    }

    /// <summary>Whether the image contains managed metadata (false for a native binary).</summary>
    public bool HasMetadata => PEReader.HasMetadata;

    /// <summary>Opens an image from a file path.</summary>
    public static AssemblyImage Open(string path) => FromStream(File.OpenRead(path));

    /// <summary>
    /// Opens an image from a resolved assembly reference, using its stream opener. This is the
    /// descriptor-based entry point that keeps callers off bare paths.
    /// </summary>
    public static AssemblyImage Open(ResolvedAssemblyReference reference) => FromStream(reference.OpenRead());

    static AssemblyImage FromStream(Stream stream)
    {
        try
        {
            return new AssemblyImage(stream, new PEReader(stream));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal MetadataReader GetMetadataReader() => PEReader.GetMetadataReader();

    public void Dispose()
    {
        PEReader.Dispose();
        _stream.Dispose();
    }
}
