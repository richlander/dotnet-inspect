using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.LibraryMetadata;

/// <summary>
/// A bounded request for the Metadata API surface of one exact realized
/// Library.
/// </summary>
public sealed class LibraryApiSurfaceInspectionRequest
{
    public LibraryApiSurfaceInspectionRequest(
        LibraryReference library,
        ApiSurfaceExtractionScope scope,
        ApiSurfaceExtractionBounds bounds,
        bool typesOnly = false,
        bool includeCompilerGenerated = false)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(bounds);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        Library = library;
        Scope = scope;
        Bounds = bounds;
        TypesOnly = typesOnly;
        IncludeCompilerGenerated = includeCompilerGenerated;
    }

    public LibraryReference Library { get; }
    public ApiSurfaceExtractionScope Scope { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public bool TypesOnly { get; }
    public bool IncludeCompilerGenerated { get; }
}

/// <summary>
/// Resource-free evidence that one exact Metadata surface was extracted from
/// one exact realized Library API content.
/// </summary>
public sealed class LibraryApiSurfaceCorrespondence
{
    internal LibraryApiSurfaceCorrespondence(
        LibraryContentReference apiContent,
        Guid moduleVersionId,
        ApiSurface surface,
        ApiSurfaceExtractionScope scope,
        bool typesOnly,
        bool includeCompilerGenerated,
        int metadataRows,
        int retainedTextCharacters)
    {
        ApiContent = apiContent;
        ModuleVersionId = moduleVersionId;
        Surface = surface;
        Scope = scope;
        TypesOnly = typesOnly;
        IncludeCompilerGenerated = includeCompilerGenerated;
        MetadataRows = metadataRows;
        RetainedTextCharacters = retainedTextCharacters;
    }

    public LibraryReference Library => ApiContent.Library;
    public LibraryContentReference ApiContent { get; }
    public Guid ModuleVersionId { get; }
    public ApiSurface Surface { get; }
    public ApiSurfaceExtractionScope Scope { get; }
    public bool TypesOnly { get; }
    public bool IncludeCompilerGenerated { get; }
    public int MetadataRows { get; }
    public int RetainedTextCharacters { get; }
}

public enum LibraryApiSurfaceInspectionRejectionKind
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public enum LibraryApiSurfaceInspectionFailureKind
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

/// <summary>The closed result of one Library-bound API-surface inspection.</summary>
public abstract record LibraryApiSurfaceInspectionOutcome
{
    private protected LibraryApiSurfaceInspectionOutcome()
    {
    }

    public sealed record Completed(
        LibraryApiSurfaceCorrespondence Correspondence)
        : LibraryApiSurfaceInspectionOutcome;

    public sealed record Incomplete(ApiSurfaceExtractionBound Bound)
        : LibraryApiSurfaceInspectionOutcome;

    public sealed record Rejected(
        LibraryApiSurfaceInspectionRejectionKind Kind)
        : LibraryApiSurfaceInspectionOutcome;

    public sealed record Failed(
        LibraryApiSurfaceInspectionFailureKind Kind)
        : LibraryApiSurfaceInspectionOutcome;
}

/// <summary>
/// Extracts Metadata while exact Library content remains owner-attested and
/// issues only detached correspondence.
/// </summary>
public static class LibraryApiSurfaceInspection
{
    public static LibraryApiSurfaceInspectionOutcome Execute(
        LibraryApiSurfaceInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReferenceEquals(request.Library, lease.Reference))
        {
            return new LibraryApiSurfaceInspectionOutcome.Rejected(
                LibraryApiSurfaceInspectionRejectionKind
                    .LeaseReferenceMismatch);
        }

        return lease.Snapshot(
            request.Library.ApiAssembly,
            request,
            static (view, state, token) =>
                Inspect(view, state, token),
            cancellationToken);
    }

    private static LibraryApiSurfaceInspectionOutcome Inspect(
        scoped LibraryContentView view,
        LibraryApiSurfaceInspectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view.Content.IsEmpty)
            return Failed(LibraryApiSurfaceInspectionFailureKind.MalformedMetadata);

        using Stream content = view.OpenRead();
        using var peReader = new PEReader(content);
        try
        {
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                return Failed(
                    LibraryApiSurfaceInspectionFailureKind
                        .NotManagedAssembly);
            }

            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(peReader);
            if (!reader.IsAssembly)
            {
                return Failed(
                    LibraryApiSurfaceInspectionFailureKind.ManagedModule);
            }

            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            ManagedMetadataIdentity.Assembly? expectedIdentity =
                view.Reference.AssemblyIdentity;
            if (expectedIdentity is null
                || !identity.IsEquivalentTo(expectedIdentity.Identity))
            {
                return new LibraryApiSurfaceInspectionOutcome.Rejected(
                    LibraryApiSurfaceInspectionRejectionKind
                        .AssemblyIdentityMismatch);
            }

            Guid moduleVersionId =
                reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (moduleVersionId == Guid.Empty)
            {
                return Failed(
                    LibraryApiSurfaceInspectionFailureKind
                        .EmptyModuleVersionId);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ApiSurfaceExtractionResult extraction =
                ApiSurfaceExtractor.ExtractBounded(
                    peReader,
                    request.Scope,
                    request.Bounds,
                    request.TypesOnly,
                    request.IncludeCompilerGenerated);
            cancellationToken.ThrowIfCancellationRequested();

            if (extraction
                is ApiSurfaceExtractionResult.Exceeded exceeded)
            {
                return new LibraryApiSurfaceInspectionOutcome.Incomplete(
                    exceeded.Bound);
            }

            ApiSurfaceExtractionResult.Extracted extracted =
                (ApiSurfaceExtractionResult.Extracted)extraction;
            return new LibraryApiSurfaceInspectionOutcome.Completed(
                new LibraryApiSurfaceCorrespondence(
                    view.Reference,
                    moduleVersionId,
                    extracted.Surface,
                    request.Scope,
                    request.TypesOnly,
                    request.IncludeCompilerGenerated,
                    extracted.MetadataRows,
                    extracted.RetainedTextCharacters));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                LibraryApiSurfaceInspectionFailureKind
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or OverflowException)
        {
            return Failed(
                LibraryApiSurfaceInspectionFailureKind.MalformedMetadata);
        }
    }

    private static LibraryApiSurfaceInspectionOutcome.Failed Failed(
        LibraryApiSurfaceInspectionFailureKind kind) => new(kind);
}
