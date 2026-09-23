using System.Collections.Immutable;

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
        LibraryTypeDeclarationInventoryInspectionBounds bounds,
        LibraryTypeDeclarationRowsInspectionRequest? rows = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(bounds);

        Library = library;
        Bounds = bounds;
        Rows = rows;
    }

    public LibraryReference Library { get; }
    public LibraryTypeDeclarationInventoryInspectionBounds Bounds { get; }
    public LibraryTypeDeclarationRowsInspectionRequest? Rows { get; }
}

/// <summary>
/// One bounded Metadata-order segment requested from a declaration inventory.
/// </summary>
public sealed record LibraryTypeDeclarationRowsInspectionRequest
{
    public LibraryTypeDeclarationRowsInspectionRequest(
        int startOrdinal,
        int maximumRows,
        bool includeMemberCount,
        Guid? expectedModuleVersionId,
        bool includeDefinitions = true,
        bool includeForwarders = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        if (!includeDefinitions && !includeForwarders)
        {
            throw new ArgumentException(
                "A declaration Rows request must include definitions, forwarders, or both.");
        }
        if (expectedModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A continuation MVID cannot be empty.",
                nameof(expectedModuleVersionId));
        }

        StartOrdinal = startOrdinal;
        MaximumRows = maximumRows;
        IncludeMemberCount = includeMemberCount;
        IncludeDefinitions = includeDefinitions;
        IncludeForwarders = includeForwarders;
        ExpectedModuleVersionId = expectedModuleVersionId;
    }

    public int StartOrdinal { get; }
    public int MaximumRows { get; }
    public bool IncludeMemberCount { get; }
    public bool IncludeDefinitions { get; }
    public bool IncludeForwarders { get; }
    public Guid? ExpectedModuleVersionId { get; }
}

public enum LibraryTypeDeclarationRowsInspectionUnavailableReason
{
    UnsupportedModuleExport,
}

public enum LibraryTypeDeclarationRowsInspectionRejectionKind
{
    StaleContinuation,
    ContinuationOutOfRange,
}

public enum LibraryTypeDeclarationRowsInspectionBound
{
    RetainedTextCharacters,
}

public enum LibraryTypeDeclarationRowsInspectionFailureKind
{
    MalformedMetadata,
}

/// <summary>
/// The closed source result for one requested declaration-row segment.
/// </summary>
public abstract record LibraryTypeDeclarationRowsInspectionOutcome
{
    private protected LibraryTypeDeclarationRowsInspectionOutcome()
    {
    }

    public sealed record Read(
        ImmutableArray<AssemblyTypeDeclarationRow> Rows,
        int? NextOrdinal,
        long RetainedTextCharacters)
        : LibraryTypeDeclarationRowsInspectionOutcome;

    public sealed record Unavailable(
        LibraryTypeDeclarationRowsInspectionUnavailableReason Reason)
        : LibraryTypeDeclarationRowsInspectionOutcome;

    public sealed record Rejected(
        LibraryTypeDeclarationRowsInspectionRejectionKind Kind)
        : LibraryTypeDeclarationRowsInspectionOutcome;

    public sealed record Incomplete(
        LibraryTypeDeclarationRowsInspectionBound Bound,
        long MeasuredRetainedTextCharacters)
        : LibraryTypeDeclarationRowsInspectionOutcome;

    public sealed record Failed(
        LibraryTypeDeclarationRowsInspectionFailureKind Kind)
        : LibraryTypeDeclarationRowsInspectionOutcome;
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
        int declarationCount,
        LibraryTypeDeclarationRowsInspectionOutcome? rows)
    {
        Subject = subject;
        Inventory = inventory;
        MetadataRows = metadataRows;
        DeclarationCount = declarationCount;
        Rows = rows;
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
    public LibraryTypeDeclarationRowsInspectionOutcome? Rows { get; }
    public long RetainedTextCharacters =>
        Rows switch
        {
            LibraryTypeDeclarationRowsInspectionOutcome.Read read =>
                read.RetainedTextCharacters,
            LibraryTypeDeclarationRowsInspectionOutcome.Incomplete
                incomplete =>
                incomplete.MeasuredRetainedTextCharacters,
            _ => Inventory.RetainedTextCharacters,
        };
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
            LibraryTypeDeclarationRowsInspectionOutcome? rows =
                request.Rows is null
                    ? null
                    : InspectRows(
                        session,
                        inventory,
                        request.Rows,
                        moduleVersionId,
                        request.Bounds.MaximumRetainedTextCharacters,
                        cancellationToken);
            return new LibraryTypeDeclarationInventoryInspectionOutcome.Completed(
                new LibraryTypeDeclarationInventoryCorrespondence(
                    subject,
                    inventory,
                    metadataRows,
                    declarationCount,
                    rows));
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

    private static LibraryTypeDeclarationRowsInspectionOutcome InspectRows(
        AssemblyInspectionSession session,
        AssemblyTypeDeclarationInventory inventory,
        LibraryTypeDeclarationRowsInspectionRequest request,
        Guid moduleVersionId,
        int maximumRetainedTextCharacters,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedModuleVersionId is { } expected
            && expected != moduleVersionId)
        {
            return new LibraryTypeDeclarationRowsInspectionOutcome.Rejected(
                LibraryTypeDeclarationRowsInspectionRejectionKind
                    .StaleContinuation);
        }

        ImmutableArray<AssemblyTypeDeclaration> allDeclarations =
            [.. inventory.GetDeclarations()];
        if (allDeclarations.Any(
                static declaration =>
                    declaration.Kind
                        == AssemblyTypeDeclarationKind.ModuleExport))
        {
            return new LibraryTypeDeclarationRowsInspectionOutcome.Unavailable(
                LibraryTypeDeclarationRowsInspectionUnavailableReason
                    .UnsupportedModuleExport);
        }
        ImmutableArray<AssemblyTypeDeclaration> declarations =
            [
                .. allDeclarations.Where(
                    declaration =>
                        declaration.Kind switch
                        {
                            AssemblyTypeDeclarationKind.Definition =>
                                request.IncludeDefinitions,
                            AssemblyTypeDeclarationKind.Forwarder =>
                                request.IncludeForwarders,
                            _ => false,
                        })
            ];

        if (request.StartOrdinal > declarations.Length
            || (request.StartOrdinal == declarations.Length
                && declarations.Length != 0))
        {
            return new LibraryTypeDeclarationRowsInspectionOutcome.Rejected(
                LibraryTypeDeclarationRowsInspectionRejectionKind
                    .ContinuationOutOfRange);
        }

        int rowCount = Math.Min(
            request.MaximumRows,
            declarations.Length - request.StartOrdinal);
        ImmutableArray<AssemblyTypeDeclaration> selected =
            declarations
                .AsSpan(request.StartOrdinal, rowCount)
                .ToArray()
                .ToImmutableArray();
        int nextOrdinal = checked(request.StartOrdinal + rowCount);
        int remainingText = checked(
            maximumRetainedTextCharacters
                - (int)inventory.RetainedTextCharacters);
        AssemblyTypeDeclarationRowsOutcome outcome =
            session.TypeDeclarationRows(
                inventory,
                selected,
                request.IncludeMemberCount,
                remainingText,
                cancellationToken);
        return outcome switch
        {
            AssemblyTypeDeclarationRowsOutcome.Read read =>
                new LibraryTypeDeclarationRowsInspectionOutcome.Read(
                    read.Rows,
                    nextOrdinal < declarations.Length
                        ? nextOrdinal
                        : null,
                    checked(
                        inventory.RetainedTextCharacters
                            + read.RetainedTextCharacters)),
            AssemblyTypeDeclarationRowsOutcome.Incomplete incomplete =>
                new LibraryTypeDeclarationRowsInspectionOutcome.Incomplete(
                    LibraryTypeDeclarationRowsInspectionBound
                        .RetainedTextCharacters,
                    checked(
                        inventory.RetainedTextCharacters
                            + incomplete
                                .MeasuredRetainedTextCharacters)),
            AssemblyTypeDeclarationRowsOutcome.Rejected =>
                new LibraryTypeDeclarationRowsInspectionOutcome.Failed(
                    LibraryTypeDeclarationRowsInspectionFailureKind
                        .MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown declaration Rows outcome."),
        };
    }
}
