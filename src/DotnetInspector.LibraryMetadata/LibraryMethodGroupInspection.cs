using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

public sealed record LibraryMethodGroupInspectionRequest
{
    public LibraryMethodGroupInspectionRequest(
        LibraryReference library,
        MetadataTypeDefinitionName declaringType,
        string methodName,
        int startOrdinal,
        int maximumRows,
        bool materializeRows,
        ApiSurfaceExtractionBounds bounds,
        Guid? expectedModuleVersionId = null)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        DeclaringType = declaringType
            ?? throw new ArgumentNullException(nameof(declaringType));
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        if (expectedModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An expected module version identifier cannot be empty.",
                nameof(expectedModuleVersionId));
        }

        MethodName = methodName;
        StartOrdinal = startOrdinal;
        MaximumRows = maximumRows;
        MaterializeRows = materializeRows;
        ExpectedModuleVersionId = expectedModuleVersionId;
    }

    public LibraryReference Library { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public string MethodName { get; }
    public int StartOrdinal { get; }
    public int MaximumRows { get; }
    public bool MaterializeRows { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public Guid? ExpectedModuleVersionId { get; }
}

public sealed record LibraryMethodGroupCorrespondence(
    LibraryContentReference ApiContent,
    AssemblyReferenceIdentity AssemblyIdentity,
    Guid ModuleVersionId,
    int AssemblyBytes,
    MetadataMethodGroupInspectionOutcome Group,
    bool StaleContinuation);

public enum LibraryMethodGroupInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryMethodGroupInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public enum LibraryMethodGroupInspectionBound
{
    MetadataRows,
}

public abstract record LibraryMethodGroupInspectionOutcome
{
    private protected LibraryMethodGroupInspectionOutcome()
    {
    }

    public sealed record Completed(
        LibraryMethodGroupCorrespondence Correspondence)
        : LibraryMethodGroupInspectionOutcome;

    public sealed record Rejected(
        LibraryMethodGroupInspectionRejection Reason)
        : LibraryMethodGroupInspectionOutcome;

    public sealed record Incomplete(
        LibraryMethodGroupInspectionBound Bound,
        long Measured)
        : LibraryMethodGroupInspectionOutcome;

    public sealed record Failed(
        LibraryMethodGroupInspectionFailure Reason)
        : LibraryMethodGroupInspectionOutcome;
}

public static class LibraryMethodGroupInspection
{
    public static LibraryMethodGroupInspectionOutcome Execute(
        LibraryMethodGroupInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryMethodGroupInspectionOutcome.Rejected(
                LibraryMethodGroupInspectionRejection
                    .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    private static LibraryMethodGroupInspectionOutcome Inspect(
        scoped LibraryContentView view,
        LibraryMethodGroupInspectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
        {
            return Failed(
                LibraryMethodGroupInspectionFailure.MalformedMetadata);
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

    private static LibraryMethodGroupInspectionOutcome Inspect(
        Stream content,
        LibraryContentReference reference,
        LibraryMethodGroupInspectionRequest request,
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
                    LibraryMethodGroupInspectionFailure
                        .NotManagedAssembly);
            }
            if (!session.IsAssembly)
            {
                return Failed(
                    LibraryMethodGroupInspectionFailure.ManagedModule);
            }

            AssemblyReferenceIdentity identity =
                session.AssemblyIdentity();
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !identity.IsEquivalentTo(expectedIdentity.Identity))
            {
                return new LibraryMethodGroupInspectionOutcome.Rejected(
                    LibraryMethodGroupInspectionRejection
                        .AssemblyIdentityMismatch);
            }

            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
            {
                return Failed(
                    LibraryMethodGroupInspectionFailure
                        .EmptyModuleVersionId);
            }
            bool staleContinuation =
                request.ExpectedModuleVersionId is { } expected
                && expected != moduleVersionId;

            using var metadataOperation = new MetadataOperationContext(
                new MetadataOperationPolicy(
                    request.Bounds.MaxMetadataRows));
            using MetadataDeclarationSession declarationSession =
                session.CreateDeclarationSession(metadataOperation);
            if (declarationSession.ImageAdmission
                is MetadataImageAdmissionResult.Rejected rejection)
            {
                return new LibraryMethodGroupInspectionOutcome.Incomplete(
                    LibraryMethodGroupInspectionBound.MetadataRows,
                    rejection.Failure.ImageMetadataRows);
            }

            cancellationToken.ThrowIfCancellationRequested();
            MetadataMethodGroupInspectionOutcome group =
                session.MethodGroup(
                    request.DeclaringType,
                    request.MethodName,
                    staleContinuation
                        ? 0
                        : request.StartOrdinal,
                    request.MaximumRows,
                    request.MaterializeRows
                        && !staleContinuation,
                    request.Bounds.MaxMembers,
                    request.Bounds.MaxRetainedTextCharacters);
            cancellationToken.ThrowIfCancellationRequested();
            return new LibraryMethodGroupInspectionOutcome.Completed(
                new(
                    reference,
                    identity,
                    moduleVersionId,
                    assemblyBytes,
                    group,
                    staleContinuation));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                LibraryMethodGroupInspectionFailure
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                LibraryMethodGroupInspectionFailure.MalformedMetadata);
        }
    }

    private static LibraryMethodGroupInspectionOutcome.Failed Failed(
        LibraryMethodGroupInspectionFailure reason) =>
        new(reason);
}
