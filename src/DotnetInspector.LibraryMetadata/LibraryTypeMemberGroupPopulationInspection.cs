using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public sealed record LibraryTypeMemberGroupPopulationInspectionRequest
{
    public LibraryTypeMemberGroupPopulationInspectionRequest(
        LibraryReference library,
        MetadataTypeMemberGroupPopulationRequest population,
        ApiSurfaceExtractionBounds bounds,
        Guid? expectedModuleVersionId = null)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Population = population
            ?? throw new ArgumentNullException(nameof(population));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        if (expectedModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An expected module version identifier cannot be empty.",
                nameof(expectedModuleVersionId));
        }

        ExpectedModuleVersionId = expectedModuleVersionId;
    }

    public LibraryReference Library { get; }
    public MetadataTypeMemberGroupPopulationRequest Population { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public Guid? ExpectedModuleVersionId { get; }
}

public sealed record LibraryTypeMemberGroupPopulationCorrespondence(
    LibraryContentReference ApiContent,
    AssemblyReferenceIdentity AssemblyIdentity,
    Guid ModuleVersionId,
    int AssemblyBytes,
    MetadataTypeMemberGroupPopulationOutcome Population);

public enum LibraryTypeMemberGroupPopulationInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    StaleContinuation,
}

public enum LibraryTypeMemberGroupPopulationInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public enum LibraryTypeMemberGroupPopulationInspectionBound
{
    MetadataRows,
}

public abstract record LibraryTypeMemberGroupPopulationInspectionOutcome
{
    private protected LibraryTypeMemberGroupPopulationInspectionOutcome()
    {
    }

    public sealed record Completed(
        LibraryTypeMemberGroupPopulationCorrespondence Correspondence)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;

    public sealed record Rejected(
        LibraryTypeMemberGroupPopulationInspectionRejection Reason)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;

    public sealed record Incomplete(
        LibraryTypeMemberGroupPopulationInspectionBound Bound,
        long Measured)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;

    public sealed record Failed(
        LibraryTypeMemberGroupPopulationInspectionFailure Reason)
        : LibraryTypeMemberGroupPopulationInspectionOutcome;
}

/// <summary>
/// Inspects one exact realized Library's compact declared Type Member-group
/// population while its owner-backed content remains borrowed.
/// </summary>
public static class LibraryTypeMemberGroupPopulationInspection
{
    public static LibraryTypeMemberGroupPopulationInspectionOutcome Execute(
        LibraryTypeMemberGroupPopulationInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryTypeMemberGroupPopulationInspectionOutcome
                .Rejected(
                    LibraryTypeMemberGroupPopulationInspectionRejection
                        .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    private static LibraryTypeMemberGroupPopulationInspectionOutcome Inspect(
        scoped LibraryContentView view,
        LibraryTypeMemberGroupPopulationInspectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
        {
            return Failed(
                LibraryTypeMemberGroupPopulationInspectionFailure
                    .MalformedMetadata);
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

    private static LibraryTypeMemberGroupPopulationInspectionOutcome Inspect(
        Stream content,
        LibraryContentReference reference,
        LibraryTypeMemberGroupPopulationInspectionRequest request,
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
                    LibraryTypeMemberGroupPopulationInspectionFailure
                        .NotManagedAssembly);
            }
            if (!session.IsAssembly)
            {
                return Failed(
                    LibraryTypeMemberGroupPopulationInspectionFailure
                        .ManagedModule);
            }

            AssemblyReferenceIdentity identity =
                session.AssemblyIdentity();
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !identity.IsEquivalentTo(expectedIdentity.Identity))
            {
                return new LibraryTypeMemberGroupPopulationInspectionOutcome
                    .Rejected(
                        LibraryTypeMemberGroupPopulationInspectionRejection
                            .AssemblyIdentityMismatch);
            }

            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
            {
                return Failed(
                    LibraryTypeMemberGroupPopulationInspectionFailure
                        .EmptyModuleVersionId);
            }
            if (request.ExpectedModuleVersionId is { } expected
                && expected != moduleVersionId)
            {
                return new LibraryTypeMemberGroupPopulationInspectionOutcome
                    .Rejected(
                        LibraryTypeMemberGroupPopulationInspectionRejection
                            .StaleContinuation);
            }

            using var metadataOperation = new MetadataOperationContext(
                new MetadataOperationPolicy(
                    request.Bounds.MaxMetadataRows));
            using MetadataDeclarationSession declaration =
                session.CreateDeclarationSession(metadataOperation);
            if (declaration.ImageAdmission
                is MetadataImageAdmissionResult.Rejected rejection)
            {
                return new LibraryTypeMemberGroupPopulationInspectionOutcome
                    .Incomplete(
                        LibraryTypeMemberGroupPopulationInspectionBound
                            .MetadataRows,
                        rejection.Failure.ImageMetadataRows);
            }

            cancellationToken.ThrowIfCancellationRequested();
            MetadataTypeMemberGroupPopulationOutcome population =
                declaration.InspectTypeMemberGroups(
                    request.Population,
                    request.Bounds,
                    cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new LibraryTypeMemberGroupPopulationInspectionOutcome
                .Completed(
                    new(
                        reference,
                        identity,
                        moduleVersionId,
                        assemblyBytes,
                        population));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                LibraryTypeMemberGroupPopulationInspectionFailure
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                LibraryTypeMemberGroupPopulationInspectionFailure
                    .MalformedMetadata);
        }
    }

    private static LibraryTypeMemberGroupPopulationInspectionOutcome.Failed
        Failed(
            LibraryTypeMemberGroupPopulationInspectionFailure reason) =>
        new(reason);
}
