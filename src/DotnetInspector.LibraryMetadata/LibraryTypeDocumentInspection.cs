using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public sealed record LibraryTypeDocumentInspectionRequest
{
    public LibraryTypeDocumentInspectionRequest(
        LibraryReference library,
        MetadataTypeDocumentInspectionRequest document,
        ApiSurfaceExtractionBounds bounds)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Document = document
            ?? throw new ArgumentNullException(nameof(document));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
    }

    public LibraryReference Library { get; }
    public MetadataTypeDocumentInspectionRequest Document { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
}

public sealed record LibraryTypeDocumentCorrespondence(
    LibraryContentReference ApiContent,
    AssemblyReferenceIdentity AssemblyIdentity,
    int AssemblyBytes,
    MetadataTypeDocumentInspectionOutcome Document);

public enum LibraryTypeDocumentInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryTypeDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public enum LibraryTypeDocumentInspectionBound
{
    MetadataRows,
}

public abstract record LibraryTypeDocumentInspectionOutcome
{
    private protected LibraryTypeDocumentInspectionOutcome()
    {
    }

    public sealed record Completed(
        LibraryTypeDocumentCorrespondence Correspondence)
        : LibraryTypeDocumentInspectionOutcome;

    public sealed record Rejected(
        LibraryTypeDocumentInspectionRejection Reason)
        : LibraryTypeDocumentInspectionOutcome;

    public sealed record Incomplete(
        LibraryTypeDocumentInspectionBound Bound,
        long Measured)
        : LibraryTypeDocumentInspectionOutcome;

    public sealed record Failed(
        LibraryTypeDocumentInspectionFailure Reason)
        : LibraryTypeDocumentInspectionOutcome;
}

/// <summary>
/// Inspects one exact Type declaration and its optional declared
/// Member-group population while owner-backed Library content is borrowed.
/// </summary>
public static class LibraryTypeDocumentInspection
{
    public static LibraryTypeDocumentInspectionOutcome Execute(
        LibraryTypeDocumentInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryTypeDocumentInspectionOutcome.Rejected(
                LibraryTypeDocumentInspectionRejection
                    .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    private static LibraryTypeDocumentInspectionOutcome Inspect(
        scoped LibraryContentView view,
        LibraryTypeDocumentInspectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
        {
            return Failed(
                LibraryTypeDocumentInspectionFailure.MalformedMetadata);
        }

        int assemblyBytes = view.Content.Length;
        LibraryContentReference reference = view.Reference;
        return view.UseReadStream(
            content => Inspect(
                content,
                reference,
                request,
                assemblyBytes,
                cancellationToken));
    }

    private static LibraryTypeDocumentInspectionOutcome Inspect(
        Stream content,
        LibraryContentReference reference,
        LibraryTypeDocumentInspectionRequest request,
        int assemblyBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.OpenPrefetched(content);
            if (!session.HasMetadata)
            {
                return Failed(
                    LibraryTypeDocumentInspectionFailure
                        .NotManagedAssembly);
            }
            if (!session.IsAssembly)
            {
                return Failed(
                    LibraryTypeDocumentInspectionFailure.ManagedModule);
            }

            AssemblyReferenceIdentity identity =
                session.AssemblyIdentity();
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !identity.IsEquivalentTo(expectedIdentity.Identity))
            {
                return new LibraryTypeDocumentInspectionOutcome.Rejected(
                    LibraryTypeDocumentInspectionRejection
                        .AssemblyIdentityMismatch);
            }

            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
            {
                return Failed(
                    LibraryTypeDocumentInspectionFailure
                        .EmptyModuleVersionId);
            }
            using var metadataOperation = new MetadataOperationContext(
                new MetadataOperationPolicy(
                    request.Bounds.MaxMetadataRows));
            using MetadataDeclarationSession declaration =
                session.CreateDeclarationSession(metadataOperation);
            if (declaration.ImageAdmission
                is MetadataImageAdmissionResult.Rejected rejection)
            {
                return new LibraryTypeDocumentInspectionOutcome.Incomplete(
                    LibraryTypeDocumentInspectionBound.MetadataRows,
                    rejection.Failure.ImageMetadataRows);
            }

            cancellationToken.ThrowIfCancellationRequested();
            MetadataTypeDocumentInspectionOutcome document =
                declaration.InspectTypeDocument(
                    request.Document,
                    request.Bounds,
                    cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new LibraryTypeDocumentInspectionOutcome.Completed(
                new(
                    reference,
                    identity,
                    assemblyBytes,
                    document));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                LibraryTypeDocumentInspectionFailure
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                LibraryTypeDocumentInspectionFailure.MalformedMetadata);
        }
    }

    private static LibraryTypeDocumentInspectionOutcome.Failed Failed(
        LibraryTypeDocumentInspectionFailure reason) =>
        new(reason);
}
