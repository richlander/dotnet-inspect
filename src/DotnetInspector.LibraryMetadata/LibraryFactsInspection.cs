using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

/// <summary>
/// The outcome of reading one Library's identity and Image and Description
/// facts from its API assembly. Failure and rejection reuse the image-opening
/// kinds of the declaration inventory, since both open the same content.
/// </summary>
public abstract record LibraryFactsInspectionOutcome
{
    private LibraryFactsInspectionOutcome()
    {
    }

    public sealed record Available(
        AssemblyReferenceIdentity AssemblyIdentity,
        Guid ModuleVersionId,
        int AssemblyBytes,
        AssemblyLibraryFactsObservation Facts)
        : LibraryFactsInspectionOutcome;

    public sealed record Rejected(
        LibraryTypeDeclarationInventoryInspectionRejectionKind Kind)
        : LibraryFactsInspectionOutcome;

    public sealed record Failed(
        LibraryTypeDeclarationInventoryInspectionFailureKind Kind)
        : LibraryFactsInspectionOutcome;
}

/// <summary>
/// Reads Library facts from the API assembly without building any Type
/// population (<c>docs/design/library-inspection-document.md#library-facts</c>).
/// </summary>
public static class LibraryFactsInspection
{
    public static LibraryFactsInspectionOutcome Execute(
        LibraryReference library,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(library, lease.Reference))
        {
            return new LibraryFactsInspectionOutcome.Rejected(
                LibraryTypeDeclarationInventoryInspectionRejectionKind.LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            library.ApiAssembly,
            static (view, token) => Inspect(view, token),
            cancellationToken);
    }

    private static LibraryFactsInspectionOutcome Inspect(
        scoped LibraryContentView view,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
            return Failed(LibraryTypeDeclarationInventoryInspectionFailureKind.MalformedMetadata);

        int assemblyBytes = view.Content.Length;
        LibraryContentReference reference = view.Reference;
        return view.UseReadStream(content => Inspect(content, reference, assemblyBytes));
    }

    private static LibraryFactsInspectionOutcome Inspect(
        Stream content,
        LibraryContentReference reference,
        int assemblyBytes)
    {
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.OpenPrefetched(content);
            if (!session.HasMetadata)
                return Failed(LibraryTypeDeclarationInventoryInspectionFailureKind.NotManagedAssembly);
            if (!session.IsAssembly)
                return Failed(LibraryTypeDeclarationInventoryInspectionFailureKind.ManagedModule);

            AssemblyReferenceIdentity identity = session.AssemblyIdentity();
            if (reference.AssemblyIdentity is not { } expected
                || !identity.IsEquivalentTo(expected.Identity))
            {
                return new LibraryFactsInspectionOutcome.Rejected(
                    LibraryTypeDeclarationInventoryInspectionRejectionKind.AssemblyIdentityMismatch);
            }

            Guid moduleVersionId = session.ModuleVersionId();
            if (moduleVersionId == Guid.Empty)
                return Failed(LibraryTypeDeclarationInventoryInspectionFailureKind.EmptyModuleVersionId);

            return new LibraryFactsInspectionOutcome.Available(
                identity,
                moduleVersionId,
                assemblyBytes,
                session.LibraryFacts());
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(LibraryTypeDeclarationInventoryInspectionFailureKind.UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(LibraryTypeDeclarationInventoryInspectionFailureKind.MalformedMetadata);
        }
    }

    private static LibraryFactsInspectionOutcome Failed(
        LibraryTypeDeclarationInventoryInspectionFailureKind kind) =>
        new LibraryFactsInspectionOutcome.Failed(kind);
}
