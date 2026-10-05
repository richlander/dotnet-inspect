using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public sealed record LibraryTypeOverviewDocumentInspectionRequest
{
    public LibraryTypeOverviewDocumentInspectionRequest(
        LibraryReference library,
        MetadataTypeOverviewDocumentInspectionRequest document,
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
    public MetadataTypeOverviewDocumentInspectionRequest Document { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
}

public sealed class LibraryTypeOverviewDocumentSubjectCorrespondence
{
    internal LibraryTypeOverviewDocumentSubjectCorrespondence(
        LibraryReference requestedLibrary,
        LibraryContentReference definingApiContent)
    {
        RequestedLibrary = requestedLibrary
            ?? throw new ArgumentNullException(
                nameof(requestedLibrary));
        DefiningApiContent = definingApiContent
            ?? throw new ArgumentNullException(
                nameof(definingApiContent));
        if (!ReferenceEquals(
                definingApiContent.Library.ApiAssembly,
                definingApiContent)
            || !definingApiContent.HasRole(
                LibraryContentRole.ApiAssembly))
        {
            throw new ArgumentException(
                "The defining content must be its Library's API assembly.",
                nameof(definingApiContent));
        }
    }

    public LibraryReference RequestedLibrary { get; }
    public LibraryReference DefiningLibrary =>
        DefiningApiContent.Library;
    public LibraryContentReference DefiningApiContent { get; }
}

public sealed record LibraryTypeOverviewDocumentCorrespondence(
    LibraryTypeOverviewDocumentSubjectCorrespondence Subject,
    AssemblyReferenceIdentity AssemblyIdentity,
    int AssemblyBytes,
    MetadataTypeOverviewDocumentInspectionOutcome Document);

public enum LibraryTypeOverviewDocumentInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryTypeOverviewDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public enum LibraryTypeOverviewDocumentInspectionBound
{
    MetadataRows,
}

public abstract record LibraryTypeOverviewDocumentInspectionOutcome
{
    private protected LibraryTypeOverviewDocumentInspectionOutcome()
    {
    }

    public sealed record Completed(
        LibraryTypeOverviewDocumentCorrespondence Correspondence)
        : LibraryTypeOverviewDocumentInspectionOutcome;

    public sealed record Rejected(
        LibraryTypeOverviewDocumentInspectionRejection Reason)
        : LibraryTypeOverviewDocumentInspectionOutcome;

    public sealed record Incomplete(
        LibraryTypeOverviewDocumentInspectionBound Bound,
        long Measured)
        : LibraryTypeOverviewDocumentInspectionOutcome;

    public sealed record Failed(
        LibraryTypeOverviewDocumentInspectionFailure Reason)
        : LibraryTypeOverviewDocumentInspectionOutcome;
}

/// <summary>
/// Inspects one exact Type declaration and its optional declared
/// Member-group population while owner-backed Library content is borrowed.
/// </summary>
public static class LibraryTypeOverviewDocumentInspection
{
    public static LibraryTypeOverviewDocumentInspectionOutcome Execute(
        LibraryTypeOverviewDocumentInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryTypeOverviewDocumentInspectionOutcome.Rejected(
                LibraryTypeOverviewDocumentInspectionRejection
                    .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    private static LibraryTypeOverviewDocumentInspectionOutcome Inspect(
        scoped LibraryContentView view,
        LibraryTypeOverviewDocumentInspectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
        {
            return Failed(
                LibraryTypeOverviewDocumentInspectionFailure.MalformedMetadata);
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

    private static LibraryTypeOverviewDocumentInspectionOutcome Inspect(
        Stream content,
        LibraryContentReference reference,
        LibraryTypeOverviewDocumentInspectionRequest request,
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
                    LibraryTypeOverviewDocumentInspectionFailure
                        .NotManagedAssembly);
            }
            if (!session.IsAssembly)
            {
                return Failed(
                    LibraryTypeOverviewDocumentInspectionFailure.ManagedModule);
            }

            AssemblyReferenceIdentity identity =
                session.AssemblyIdentity();
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !identity.IsEquivalentTo(expectedIdentity.Identity))
            {
                return new LibraryTypeOverviewDocumentInspectionOutcome.Rejected(
                    LibraryTypeOverviewDocumentInspectionRejection
                        .AssemblyIdentityMismatch);
            }

            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
            {
                return Failed(
                    LibraryTypeOverviewDocumentInspectionFailure
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
                return new LibraryTypeOverviewDocumentInspectionOutcome.Incomplete(
                    LibraryTypeOverviewDocumentInspectionBound.MetadataRows,
                    rejection.Failure.ImageMetadataRows);
            }

            cancellationToken.ThrowIfCancellationRequested();
            MetadataTypeOverviewDocumentInspectionOutcome document =
                declaration.InspectTypeOverviewDocument(
                    request.Document,
                    request.Bounds,
                    cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new LibraryTypeOverviewDocumentInspectionOutcome.Completed(
                new(
                    new(
                        request.Library,
                        reference),
                    identity,
                    assemblyBytes,
                    document));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                LibraryTypeOverviewDocumentInspectionFailure
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                LibraryTypeOverviewDocumentInspectionFailure.MalformedMetadata);
        }
    }

    private static LibraryTypeOverviewDocumentInspectionOutcome.Failed Failed(
        LibraryTypeOverviewDocumentInspectionFailure reason) =>
        new(reason);
}
