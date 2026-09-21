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
        int maximumRetainedDeclarations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumAssemblyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumRetainedDeclarations);

        MaximumAssemblyBytes = maximumAssemblyBytes;
        MaximumRetainedDeclarations = maximumRetainedDeclarations;
    }

    public int MaximumAssemblyBytes { get; }
    public int MaximumRetainedDeclarations { get; }
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
        LibraryContentReference apiContent,
        Guid moduleVersionId,
        AssemblyTypeDeclarationInventory inventory,
        int assemblyBytes,
        int declarationCount)
    {
        ApiContent = apiContent;
        ModuleVersionId = moduleVersionId;
        Inventory = inventory;
        AssemblyBytes = assemblyBytes;
        DeclarationCount = declarationCount;
    }

    public LibraryReference Library => ApiContent.Library;
    public LibraryContentReference ApiContent { get; }
    public Guid ModuleVersionId { get; }
    public AssemblyTypeDeclarationInventory Inventory { get; }
    public int AssemblyBytes { get; }
    public int DeclarationCount { get; }
}

public enum LibraryTypeDeclarationInventoryInspectionBound
{
    AssemblyBytes,
    RetainedDeclarations,
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
        LibraryTypeDeclarationInventoryInspectionBound Bound,
        int MeasuredAssemblyBytes,
        int? MeasuredDeclarations)
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
                LibraryTypeDeclarationInventoryInspectionBound.AssemblyBytes,
                assemblyBytes,
                MeasuredDeclarations: null);
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

            if (session.AssemblyInfo().AssemblyName is null)
            {
                return Failed(
                    LibraryTypeDeclarationInventoryInspectionFailureKind
                        .ManagedModule);
            }

            cancellationToken.ThrowIfCancellationRequested();
            AssemblyTypeDeclarationInventoryOutcome inventoryOutcome =
                session.TypeDeclarations();
            cancellationToken.ThrowIfCancellationRequested();
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
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !inventory.Identity.IsEquivalentTo(
                    expectedIdentity.Identity))
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

            int declarationCount = inventory.Declarations.Length;
            if (declarationCount
                > request.Bounds.MaximumRetainedDeclarations)
            {
                return new LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete(
                    LibraryTypeDeclarationInventoryInspectionBound
                        .RetainedDeclarations,
                    assemblyBytes,
                    declarationCount);
            }

            return new LibraryTypeDeclarationInventoryInspectionOutcome.Completed(
                new LibraryTypeDeclarationInventoryCorrespondence(
                    reference,
                    moduleVersionId,
                    inventory,
                    assemblyBytes,
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
