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
    readonly bool _hasMetadata;
    readonly bool _ownsReader;
    bool _disposed;

    internal PEReader PEReader { get; }

    AssemblyImage(
        Stream? stream,
        PEReader peReader,
        bool ownsReader,
        Action? ensureLenderAlive = null,
        bool? admittedHasMetadata = null)
    {
        bool hasMetadata;
        if (admittedHasMetadata is bool retainedAdmission)
        {
            hasMetadata = retainedAdmission;
        }
        else
        {
            try
            {
                hasMetadata =
                    MetadataFormatAdmission.AdmitImage(peReader);
            }
            catch (Exception ex)
            {
                if (ownsReader)
                    OwnedResourceCleanup.DisposeAfterFailure(peReader, ex);
                throw;
            }
        }

        _stream = stream;
        PEReader = peReader;
        _hasMetadata = hasMetadata;
        _ownsReader = ownsReader;
        _ensureLenderAlive = ensureLenderAlive;
    }

    /// <summary>Whether the image contains managed metadata (false for a native binary).</summary>
    public bool HasMetadata
    {
        get
        {
            EnsureAlive();
            return _hasMetadata;
        }
    }

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
        => new(stream: null, peReader, ownsReader: false, ensureLenderAlive);

    /// <summary>
    /// Opens an image from a resolved assembly reference, using its stream opener. This is the
    /// descriptor-based entry point that keeps callers off bare paths.
    /// </summary>
    public static AssemblyImage Open(ResolvedAssemblyReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        AssemblyImage image = FromStream(reference.OpenRead());
        try
        {
            reference.ValidateArtifactContent(image.PEReader);
            return image;
        }
        catch (Exception ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(image, ex);
            throw;
        }
    }

    internal static AssemblyImage Open(AssemblyImageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new AssemblyImage(
            stream: null,
            new PEReader(snapshot.Content),
            ownsReader: true);
    }

    internal static AssemblyImage OpenPrefetched(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Stream? ownedStream = stream;
        PEReader? peReader = null;
        bool? hasMetadata = null;
        try
        {
            peReader = new PEReader(
                ownedStream,
                PEStreamOptions.PrefetchEntireImage | PEStreamOptions.LeaveOpen);
            hasMetadata =
                MetadataFormatAdmission.AdmitImage(peReader);

            Stream streamToDispose = ownedStream;
            ownedStream = null;
            if (hasMetadata.Value)
                streamToDispose.Dispose();
            else
                OwnedResourceCleanup.DisposeWithoutReplacingOutcome(
                    streamToDispose);

            var image = new AssemblyImage(
                stream: null,
                peReader,
                ownsReader: true,
                admittedHasMetadata: hasMetadata.Value);
            peReader = null;
            return image;
        }
        catch (Exception ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(peReader, ex);
            OwnedResourceCleanup.DisposeAfterFailure(ownedStream, ex);
            throw;
        }
    }

    static AssemblyImage FromStream(Stream stream)
    {
        try
        {
            // LeaveOpen: this AssemblyImage is the sole owner of the stream and disposes it
            // explicitly in Dispose(), so the PEReader must not also take ownership.
            return new AssemblyImage(
                stream,
                new PEReader(stream, PEStreamOptions.LeaveOpen),
                ownsReader: true);
        }
        catch (Exception ex)
        {
            OwnedResourceCleanup.DisposeAfterFailure(stream, ex);
            throw;
        }
    }

    internal MetadataReader GetMetadataReader() =>
        MetadataFormatAdmission.GetMetadataReader(PEReader);

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

        if (_hasMetadata)
        {
            if (_ownsReader)
                PEReader.Dispose();
            _stream?.Dispose();
            return;
        }

        if (_ownsReader)
            OwnedResourceCleanup.DisposeWithoutReplacingOutcome(
                PEReader);
        OwnedResourceCleanup.DisposeWithoutReplacingOutcome(_stream);
    }
}
