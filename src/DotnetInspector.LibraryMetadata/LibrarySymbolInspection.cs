using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public sealed record LibrarySymbolInspectionRequest
{
    public LibrarySymbolInspectionRequest(
        MetadataLibrarySymbolAdmission admission,
        ApiSurfaceExtractionBounds bounds)
    {
        if (!Enum.IsDefined(admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        Admission = admission;
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
    }

    public MetadataLibrarySymbolAdmission Admission { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
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
        MetadataLibrarySymbolScan scan)
    {
        AssemblyIdentity = assemblyIdentity;
        ModuleVersionId = moduleVersionId;
        Scan = scan;
    }

    public AssemblyReferenceIdentity AssemblyIdentity { get; }
    public Guid ModuleVersionId { get; }
    public MetadataLibrarySymbolScan Scan { get; }
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
/// (docs/design/library-symbol-document.md#work-and-bounds). The lease names
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
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.OpenPrefetched(content);
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
            return new LibrarySymbolInspectionOutcome<TResult>.Completed(
                consume(
                    new LibrarySymbolInspectionContext(identity, moduleVersionId, scan),
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

    private static LibrarySymbolInspectionOutcome<TResult>.Failed Failed<TResult>(
        LibrarySymbolInspectionFailure reason) =>
        new(reason);
}
