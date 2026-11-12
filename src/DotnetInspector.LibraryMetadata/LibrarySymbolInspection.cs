using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public sealed record LibrarySymbolInspectionRequest
{
    /// <param name="openPortablePdb">
    /// Opens the Portable PDB the host settled for the Library's API
    /// assembly, or <c>null</c> when the consumer needs no debug information.
    /// The inspection owns each stream it opens.
    /// </param>
    public LibrarySymbolInspectionRequest(
        MetadataLibrarySymbolAdmission admission,
        ApiSurfaceExtractionBounds bounds,
        Func<Stream>? openPortablePdb = null)
    {
        if (!Enum.IsDefined(admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        Admission = admission;
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        OpenPortablePdb = openPortablePdb;
    }

    public MetadataLibrarySymbolAdmission Admission { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public Func<Stream>? OpenPortablePdb { get; }
}

/// <summary>
/// The debug information a symbol scan's consumer may read beside it: the
/// host's Portable PDB loaded into the same image the scan reads, or why it
/// is not.
/// </summary>
public abstract record LibrarySymbolDebugInformation
{
    private protected LibrarySymbolDebugInformation()
    {
    }

    /// <summary>The request supplied no Portable PDB.</summary>
    public sealed record NotSupplied : LibrarySymbolDebugInformation
    {
        internal static NotSupplied Instance { get; } = new();

        NotSupplied()
        {
        }
    }

    /// <summary>
    /// The Portable PDB matched the image's CodeView identity and is loaded
    /// into <paramref name="Context"/>, which lives only while the consumer
    /// runs.
    /// </summary>
    public sealed record Loaded(PdbContext Context) : LibrarySymbolDebugInformation;

    /// <summary>The supplied Portable PDB could not be opened or did not match the image.</summary>
    public sealed record Rejected(string Detail) : LibrarySymbolDebugInformation;
}

/// <summary>
/// The admitted image a symbol scan reads, available to the consumer only
/// while the Library's API assembly content is borrowed.
/// </summary>
public sealed class LibrarySymbolInspectionContext
{
    internal LibrarySymbolInspectionContext(
        AssemblyReferenceIdentity assemblyIdentity,
        Guid moduleVersionId,
        MetadataLibrarySymbolScan scan,
        LibrarySymbolDebugInformation debugInformation)
    {
        AssemblyIdentity = assemblyIdentity;
        ModuleVersionId = moduleVersionId;
        Scan = scan;
        DebugInformation = debugInformation;
    }

    public AssemblyReferenceIdentity AssemblyIdentity { get; }
    public Guid ModuleVersionId { get; }
    public MetadataLibrarySymbolScan Scan { get; }
    public LibrarySymbolDebugInformation DebugInformation { get; }
}

public enum LibrarySymbolInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public enum LibrarySymbolInspectionBound
{
    MetadataRows,
}

public abstract record LibrarySymbolInspectionOutcome<TResult>
{
    private protected LibrarySymbolInspectionOutcome()
    {
    }

    public sealed record Completed(TResult Result)
        : LibrarySymbolInspectionOutcome<TResult>;

    public sealed record Incomplete(
        LibrarySymbolInspectionBound Bound,
        long Measured)
        : LibrarySymbolInspectionOutcome<TResult>;

    public sealed record Failed(LibrarySymbolInspectionFailure Reason)
        : LibrarySymbolInspectionOutcome<TResult>;
}

/// <summary>
/// Opens one metadata session over a Library's API assembly and hands a
/// whole-Library symbol scan to a consumer while the content is borrowed
/// (docs/design/library-api-reference.md#work-and-bounds). The lease names
/// the Library, and the identity the consumer sees is read from the bytes it
/// opens, so there is no second handle to reconcile (#11057).
/// </summary>
public static class LibrarySymbolInspection
{
    public static LibrarySymbolInspectionOutcome<TResult> Execute<TResult>(
        LibrarySymbolInspectionRequest request,
        LibraryOperationLease lease,
        Func<LibrarySymbolInspectionContext, CancellationToken, TResult> consume,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(consume);
        cancellationToken.ThrowIfCancellationRequested();

        return lease.Snapshot(
            lease.Reference.ApiAssembly,
            (request, consume),
            static (view, state, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (view.Content.IsEmpty)
                {
                    return new LibrarySymbolInspectionOutcome<TResult>.Failed(
                        LibrarySymbolInspectionFailure.MalformedMetadata);
                }
                return view.UseReadStream(
                    content => Inspect(
                        content,
                        state.request,
                        state.consume,
                        token));
            },
            cancellationToken);
    }

    private static LibrarySymbolInspectionOutcome<TResult> Inspect<TResult>(
        Stream content,
        LibrarySymbolInspectionRequest request,
        Func<LibrarySymbolInspectionContext, CancellationToken, TResult> consume,
        CancellationToken cancellationToken)
    {
        try
        {
            // With a Portable PDB the image is opened once, as a PDB context
            // the scan's session borrows, so the PDB is checked against the
            // very bytes the scan reads.
            using PdbContext? debugImage = request.OpenPortablePdb is null
                ? null
                : PdbContext.OpenPrefetchedMetadataOnly(content, "Library");
            using AssemblyInspectionSession session = debugImage is null
                ? AssemblyInspectionSession.OpenPrefetched(content)
                : AssemblyInspectionSession.Borrow(debugImage);
            if (!session.HasMetadata)
                return Failed<TResult>(LibrarySymbolInspectionFailure.NotManagedAssembly);
            if (!session.IsAssembly)
                return Failed<TResult>(LibrarySymbolInspectionFailure.ManagedModule);

            AssemblyReferenceIdentity identity = session.AssemblyIdentity();

            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
                return Failed<TResult>(LibrarySymbolInspectionFailure.EmptyModuleVersionId);

            using var metadataOperation = new MetadataOperationContext(
                new MetadataOperationPolicy(request.Bounds.MaxMetadataRows));
            using MetadataDeclarationSession declaration =
                session.CreateDeclarationSession(metadataOperation);
            if (declaration.ImageAdmission
                is MetadataImageAdmissionResult.Rejected rejection)
            {
                return new LibrarySymbolInspectionOutcome<TResult>.Incomplete(
                    LibrarySymbolInspectionBound.MetadataRows,
                    rejection.Failure.ImageMetadataRows);
            }

            MetadataLibrarySymbolScan scan =
                declaration.OpenLibrarySymbolScan(request.Admission, request.Bounds)
                ?? throw new InvalidOperationException(
                    "An admitted image did not open a symbol scan.");
            cancellationToken.ThrowIfCancellationRequested();
            LibrarySymbolDebugInformation debugInformation = debugImage is null
                ? LibrarySymbolDebugInformation.NotSupplied.Instance
                : LoadPortablePdb(debugImage, request.OpenPortablePdb!);
            cancellationToken.ThrowIfCancellationRequested();
            return new LibrarySymbolInspectionOutcome<TResult>.Completed(
                consume(
                    new LibrarySymbolInspectionContext(
                        identity,
                        moduleVersionId,
                        scan,
                        debugInformation),
                    cancellationToken));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed<TResult>(LibrarySymbolInspectionFailure.UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException)
        {
            return Failed<TResult>(LibrarySymbolInspectionFailure.MalformedMetadata);
        }
    }

    private static LibrarySymbolDebugInformation LoadPortablePdb(
        PdbContext image,
        Func<Stream> openPortablePdb)
    {
        try
        {
            image.LoadPdbFromStream(
                openPortablePdb(),
                "Settlement",
                throwOnReadFailure: true);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or BadImageFormatException)
        {
            return new LibrarySymbolDebugInformation.Rejected(exception.Message);
        }
        return image.HasPdb
            ? new LibrarySymbolDebugInformation.Loaded(image)
            : new LibrarySymbolDebugInformation.Rejected(
                image.LastPdbLoadError
                ?? "The Portable PDB does not match the image.");
    }

    private static LibrarySymbolInspectionOutcome<TResult>.Failed Failed<TResult>(
        LibrarySymbolInspectionFailure reason) =>
        new(reason);
}
