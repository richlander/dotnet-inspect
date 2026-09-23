using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

/// <summary>
/// A compact public Type-inventory Count request for one exact realized
/// Library.
/// </summary>
public sealed class LibraryApiTypeInventoryCountInspectionRequest
{
    public LibraryApiTypeInventoryCountInspectionRequest(
        LibraryReference library)
    {
        ArgumentNullException.ThrowIfNull(library);
        Library = library;
    }

    public LibraryReference Library { get; }
}

/// <summary>
/// Detached correspondence between exact Library API content and one compact
/// public Type-inventory Count attempt.
/// </summary>
public sealed class LibraryApiTypeInventoryCountCorrespondence
{
    internal LibraryApiTypeInventoryCountCorrespondence(
        LibraryContentReference apiContent,
        AssemblyReferenceIdentity assemblyIdentity,
        Guid moduleVersionId,
        ApiTypeInventoryCountResult count,
        int assemblyBytes)
    {
        ApiContent = apiContent;
        AssemblyIdentity = assemblyIdentity;
        ModuleVersionId = moduleVersionId;
        Count = count;
        AssemblyBytes = assemblyBytes;
    }

    public LibraryReference Library => ApiContent.Library;
    public LibraryContentReference ApiContent { get; }
    public AssemblyReferenceIdentity AssemblyIdentity { get; }
    public Guid ModuleVersionId { get; }
    public ApiTypeInventoryCountResult Count { get; }
    public int AssemblyBytes { get; }
}

public enum LibraryApiTypeInventoryCountInspectionRejectionKind
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryApiTypeInventoryCountInspectionFailureKind
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

/// <summary>
/// The closed result of one Library-bound compact public Type Count.
/// </summary>
public abstract record LibraryApiTypeInventoryCountInspectionOutcome
{
    private protected LibraryApiTypeInventoryCountInspectionOutcome()
    {
    }

    public sealed record Completed(
        LibraryApiTypeInventoryCountCorrespondence Correspondence)
        : LibraryApiTypeInventoryCountInspectionOutcome;

    public sealed record Rejected(
        LibraryApiTypeInventoryCountInspectionRejectionKind Kind)
        : LibraryApiTypeInventoryCountInspectionOutcome;

    public sealed record Failed(
        LibraryApiTypeInventoryCountInspectionFailureKind Kind)
        : LibraryApiTypeInventoryCountInspectionOutcome;
}

/// <summary>
/// Counts public Types while exact Library API content remains owner-attested
/// and issues only detached correspondence.
/// </summary>
public static class LibraryApiTypeInventoryCountInspection
{
    public static LibraryApiTypeInventoryCountInspectionOutcome Execute(
        LibraryApiTypeInventoryCountInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryApiTypeInventoryCountInspectionOutcome.Rejected(
                LibraryApiTypeInventoryCountInspectionRejectionKind
                    .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    private static LibraryApiTypeInventoryCountInspectionOutcome Inspect(
        scoped LibraryContentView view,
        LibraryApiTypeInventoryCountInspectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int assemblyBytes = view.Content.Length;
        if (view.Content.IsEmpty)
        {
            return Failed(
                LibraryApiTypeInventoryCountInspectionFailureKind
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

    private static LibraryApiTypeInventoryCountInspectionOutcome Inspect(
        Stream content,
        LibraryContentReference reference,
        LibraryApiTypeInventoryCountInspectionRequest request,
        int assemblyBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var peReader = new PEReader(
                content,
                PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata)
            {
                return Failed(
                    LibraryApiTypeInventoryCountInspectionFailureKind
                        .NotManagedAssembly);
            }

            cancellationToken.ThrowIfCancellationRequested();
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(peReader);
            if (!reader.IsAssembly)
            {
                return Failed(
                    LibraryApiTypeInventoryCountInspectionFailureKind
                        .ManagedModule);
            }

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !identity.IsEquivalentTo(expectedIdentity.Identity))
            {
                return new LibraryApiTypeInventoryCountInspectionOutcome
                    .Rejected(
                        LibraryApiTypeInventoryCountInspectionRejectionKind
                            .AssemblyIdentityMismatch);
            }

            Guid moduleVersionId = reader.GetGuid(
                reader.GetModuleDefinition().Mvid);
            if (moduleVersionId == Guid.Empty)
            {
                return Failed(
                    LibraryApiTypeInventoryCountInspectionFailureKind
                        .EmptyModuleVersionId);
            }

            ApiTypeInventoryCountResult count =
                ApiSurfaceExtractor.CountSummaryTypes(peReader);
            cancellationToken.ThrowIfCancellationRequested();
            if (count is ApiTypeInventoryCountResult.Counted counted
                && counted.Count.ModuleVersionId != moduleVersionId)
            {
                throw new InvalidOperationException(
                    "Compact Type Count returned a different MVID from the inspected Library image.");
            }

            return new LibraryApiTypeInventoryCountInspectionOutcome.Completed(
                new LibraryApiTypeInventoryCountCorrespondence(
                    reference,
                    identity,
                    moduleVersionId,
                    count,
                    assemblyBytes));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                LibraryApiTypeInventoryCountInspectionFailureKind
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                LibraryApiTypeInventoryCountInspectionFailureKind
                    .MalformedMetadata);
        }
    }

    private static LibraryApiTypeInventoryCountInspectionOutcome.Failed Failed(
        LibraryApiTypeInventoryCountInspectionFailureKind kind) =>
        new(kind);
}
