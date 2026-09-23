using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

/// <summary>
/// Finite limits for one Library-bound declaration inventory.
/// </summary>
public sealed class LibraryTypeDeclarationInventoryInspectionBounds
{
    public LibraryTypeDeclarationInventoryInspectionBounds(
        int maximumAssemblyBytes,
        int maximumRetainedDeclarations,
        int maximumMetadataRows,
        int maximumRetainedTextCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumAssemblyBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedDeclarations);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumMetadataRows);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedTextCharacters);

        MaximumAssemblyBytes = maximumAssemblyBytes;
        MaximumRetainedDeclarations = maximumRetainedDeclarations;
        MaximumMetadataRows = maximumMetadataRows;
        MaximumRetainedTextCharacters =
            maximumRetainedTextCharacters;
    }

    public int MaximumAssemblyBytes { get; }
    public int MaximumRetainedDeclarations { get; }
    public int MaximumMetadataRows { get; }
    public int MaximumRetainedTextCharacters { get; }
}

/// <summary>
/// A bounded request for the Metadata declaration inventory of one exact
/// realized Library.
/// </summary>
public sealed class LibraryTypeDeclarationInventoryInspectionRequest
{
    public LibraryTypeDeclarationInventoryInspectionRequest(
        LibraryReference library,
        LibraryTypeDeclarationInventoryInspectionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(bounds);

        Library = library;
        Bounds = bounds;
    }

    public LibraryReference Library { get; }
    public LibraryTypeDeclarationInventoryInspectionBounds Bounds { get; }
}

/// <summary>
/// Resource-free correspondence between one exact Library API content and its
/// complete Metadata declaration inventory.
/// </summary>
public sealed class LibraryTypeDeclarationInventoryCorrespondence
{
    internal LibraryTypeDeclarationInventoryCorrespondence(
        LibraryTypeDeclarationInventorySubject subject,
        AssemblyTypeDeclarationInventory inventory,
        long metadataRows,
        int declarationCount)
    {
        Subject = subject;
        Inventory = inventory;
        MetadataRows = metadataRows;
        DeclarationCount = declarationCount;
    }

    public LibraryTypeDeclarationInventorySubject Subject { get; }
    public LibraryReference Library => Subject.Library;
    public LibraryContentReference ApiContent => Subject.ApiContent;
    public AssemblyReferenceIdentity AssemblyIdentity =>
        Subject.AssemblyIdentity;
    public Guid ModuleVersionId => Subject.ModuleVersionId;
    public AssemblyTypeDeclarationInventory Inventory { get; }
    public int AssemblyBytes => Subject.AssemblyBytes;
    public long MetadataRows { get; }
    public int DeclarationCount { get; }
    public long RetainedTextCharacters =>
        Inventory.RetainedTextCharacters;
}

/// <summary>
/// Resource-free subject facts established before declaration inventory work.
/// </summary>
public sealed class LibraryTypeDeclarationInventorySubject
{
    internal LibraryTypeDeclarationInventorySubject(
        LibraryContentReference apiContent,
        AssemblyReferenceIdentity assemblyIdentity,
        Guid moduleVersionId,
        int assemblyBytes)
    {
        ApiContent = apiContent;
        AssemblyIdentity = assemblyIdentity;
        ModuleVersionId = moduleVersionId;
        AssemblyBytes = assemblyBytes;
    }

    public LibraryReference Library => ApiContent.Library;
    public LibraryContentReference ApiContent { get; }
    public AssemblyReferenceIdentity AssemblyIdentity { get; }
    public Guid ModuleVersionId { get; }
    public int AssemblyBytes { get; }
}

public enum LibraryTypeDeclarationInventoryInspectionBound
{
    AssemblyBytes,
    MetadataRows,
    RetainedDeclarations,
    RetainedTextCharacters,
}

public enum LibraryTypeDeclarationInventoryInspectionRejectionKind
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryTypeDeclarationInventoryInspectionFailureKind
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

/// <summary>
/// The closed result of one Library-bound declaration inventory inspection.
/// </summary>
public abstract record LibraryTypeDeclarationInventoryInspectionOutcome
{
    private protected LibraryTypeDeclarationInventoryInspectionOutcome()
    {
    }

    public sealed record Completed(
        LibraryTypeDeclarationInventoryCorrespondence Correspondence)
        : LibraryTypeDeclarationInventoryInspectionOutcome;

    public sealed record Incomplete(
        LibraryTypeDeclarationInventorySubject? Subject,
        LibraryTypeDeclarationInventoryInspectionBound Bound,
        int MeasuredAssemblyBytes,
        long? MeasuredMetadataRows,
        long? MeasuredDeclarations,
        long? MeasuredRetainedTextCharacters)
        : LibraryTypeDeclarationInventoryInspectionOutcome;

    public sealed record Rejected(
        LibraryTypeDeclarationInventoryInspectionRejectionKind Kind)
        : LibraryTypeDeclarationInventoryInspectionOutcome;

    public sealed record Failed(
        LibraryTypeDeclarationInventoryInspectionFailureKind Kind)
        : LibraryTypeDeclarationInventoryInspectionOutcome;
}

/// <summary>
/// Reads declarations while exact Library API content remains owner-attested
/// and issues only detached correspondence.
/// </summary>
public static class LibraryTypeDeclarationInventoryInspection
{
    public static LibraryTypeDeclarationInventoryInspectionOutcome Execute(
        LibraryTypeDeclarationInventoryInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryTypeDeclarationInventoryInspectionOutcome.Rejected(
                LibraryTypeDeclarationInventoryInspectionRejectionKind
                    .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    private static LibraryTypeDeclarationInventoryInspectionOutcome Inspect(
        scoped LibraryContentView view,
        LibraryTypeDeclarationInventoryInspectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int assemblyBytes = view.Content.Length;
        if (assemblyBytes > request.Bounds.MaximumAssemblyBytes)
        {
            return new LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete(
                Subject: null,
                LibraryTypeDeclarationInventoryInspectionBound.AssemblyBytes,
                assemblyBytes,
                MeasuredMetadataRows: null,
                MeasuredDeclarations: null,
                MeasuredRetainedTextCharacters: null);
        }

        if (view.Content.IsEmpty)
        {
            return Failed(
                LibraryTypeDeclarationInventoryInspectionFailureKind
                    .MalformedMetadata);
        }

        LibraryContentReference reference = view.Reference;
        return view.UseReadStream(
            content => Inspect(
                content,
                reference,
                request,
                assemblyBytes,
                cancellationToken));
    }

    private static LibraryTypeDeclarationInventoryInspectionOutcome Inspect(
        Stream content,
        LibraryContentReference reference,
        LibraryTypeDeclarationInventoryInspectionRequest request,
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
                    LibraryTypeDeclarationInventoryInspectionFailureKind
                        .NotManagedAssembly);
            }

            if (!session.IsAssembly)
            {
                return Failed(
                    LibraryTypeDeclarationInventoryInspectionFailureKind
                        .ManagedModule);
            }

            AssemblyReferenceIdentity identity = session.AssemblyIdentity();
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !identity.IsEquivalentTo(expectedIdentity.Identity))
            {
                return new LibraryTypeDeclarationInventoryInspectionOutcome.Rejected(
                    LibraryTypeDeclarationInventoryInspectionRejectionKind
                        .AssemblyIdentityMismatch);
            }

            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
            {
                return Failed(
                    LibraryTypeDeclarationInventoryInspectionFailureKind
                        .EmptyModuleVersionId);
            }

            var subject = new LibraryTypeDeclarationInventorySubject(
                reference,
                identity,
                moduleVersionId,
                assemblyBytes);
            using var metadataOperation = new MetadataOperationContext(
                new MetadataOperationPolicy(
                    request.Bounds.MaximumMetadataRows));
            using MetadataDeclarationSession declarationSession =
                session.CreateDeclarationSession(metadataOperation);
            if (declarationSession.ImageAdmission
                is MetadataImageAdmissionResult.Rejected rowRejection)
            {
                return new LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete(
                    subject,
                    LibraryTypeDeclarationInventoryInspectionBound.MetadataRows,
                    assemblyBytes,
                    rowRejection.Failure.ImageMetadataRows,
                    MeasuredDeclarations: null,
                    MeasuredRetainedTextCharacters: null);
            }
            long metadataRows =
                ((MetadataImageAdmissionResult.Admitted)
                    declarationSession.ImageAdmission).ImageMetadataRows;

            cancellationToken.ThrowIfCancellationRequested();
            AssemblyTypeDeclarationInventoryOutcome inventoryOutcome =
                session.TypeDeclarations(
                    request.Bounds.MaximumRetainedDeclarations,
                    request.Bounds.MaximumRetainedTextCharacters);
            cancellationToken.ThrowIfCancellationRequested();
            if (inventoryOutcome
                is AssemblyTypeDeclarationInventoryOutcome.Incomplete
                    incomplete)
            {
                return new LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete(
                    subject,
                    incomplete.Bound switch
                    {
                        AssemblyTypeDeclarationInventoryBound
                                .RetainedDeclarations =>
                            LibraryTypeDeclarationInventoryInspectionBound
                                .RetainedDeclarations,
                        AssemblyTypeDeclarationInventoryBound
                                .RetainedTextCharacters =>
                            LibraryTypeDeclarationInventoryInspectionBound
                                .RetainedTextCharacters,
                        _ => throw new InvalidOperationException(
                            "Unknown declaration inventory bound."),
                    },
                    assemblyBytes,
                    metadataRows,
                    incomplete.MeasuredDeclarations,
                    incomplete.MeasuredRetainedTextCharacters);
            }
            if (inventoryOutcome
                is AssemblyTypeDeclarationInventoryOutcome.Rejected rejected)
            {
                return rejected.Failure.Kind switch
                {
                    CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                        Failed(
                            LibraryTypeDeclarationInventoryInspectionFailureKind
                                .UnsupportedWindowsMetadata),
                    CandidateOpenFailureKind.InvalidImage =>
                        Failed(
                            LibraryTypeDeclarationInventoryInspectionFailureKind
                                .MalformedMetadata),
                    _ => throw new InvalidOperationException(
                        "Owner-attested Library content became unreadable during synchronous Metadata inspection."),
                };
            }

            AssemblyTypeDeclarationInventory inventory =
                ((AssemblyTypeDeclarationInventoryOutcome.Read)
                    inventoryOutcome).Inventory;
            if (!inventory.Identity.IsEquivalentTo(identity))
            {
                throw new InvalidOperationException(
                    "Declaration inventory returned a different assembly identity from the inspected image.");
            }

            int declarationCount = inventory.Declarations.Length;
            return new LibraryTypeDeclarationInventoryInspectionOutcome.Completed(
                new LibraryTypeDeclarationInventoryCorrespondence(
                    subject,
                    inventory,
                    metadataRows,
                    declarationCount));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                LibraryTypeDeclarationInventoryInspectionFailureKind
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                LibraryTypeDeclarationInventoryInspectionFailureKind
                    .MalformedMetadata);
        }
    }

    private static LibraryTypeDeclarationInventoryInspectionOutcome.Failed
        Failed(
            LibraryTypeDeclarationInventoryInspectionFailureKind kind) =>
            new(kind);
}
