using System.Collections.Immutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one request-shaped Library inspection through an exact Library
/// lease.
/// </summary>
public static class LibraryInspectionOperation
{
    private const string SharePath = "library-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            LibraryTypeDeclarationInventoryInspectionOutcome outcome =
                LibraryTypeDeclarationInventoryInspection.Execute(
                    new(
                        request.Library,
                        InventoryBounds(request.Plan.Bounds)),
                    lease,
                    cancellationToken);
            return Project(outcome, request, cancellationToken);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Project(
        LibraryTypeDeclarationInventoryInspectionOutcome outcome,
        LibraryInspectionRequest request,
        CancellationToken cancellationToken) =>
        outcome switch
        {
            LibraryTypeDeclarationInventoryInspectionOutcome.Completed
                completed =>
                Project(
                    completed.Correspondence,
                    request,
                    cancellationToken),
            LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete
                incomplete =>
                Project(incomplete, request),
            LibraryTypeDeclarationInventoryInspectionOutcome.Rejected
                rejected =>
                Rejected(rejected.Kind),
            LibraryTypeDeclarationInventoryInspectionOutcome.Failed failed =>
                Failed(failed.Kind),
            _ => throw new InvalidOperationException(
                "Unknown Library declaration inventory outcome."),
        };

    private static InspectionEnvelope<LibraryInspectionOutcome> Project(
        LibraryTypeDeclarationInventoryCorrespondence correspondence,
        LibraryInspectionRequest request,
        CancellationToken cancellationToken)
    {
        (
            LibraryTypePopulationCountOutcome count,
            ImmutableArray<InspectionDiagnostic> diagnostics) =
            Count(
                correspondence.Inventory,
                request.Plan.Bounds,
                cancellationToken);
        return Available(
            correspondence.Subject,
            request,
            count,
            correspondence.MetadataRows,
            correspondence.DeclarationCount,
            correspondence.RetainedTextCharacters,
            diagnostics);
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Project(
        LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete incomplete,
        LibraryInspectionRequest request)
    {
        LibraryTypeDeclarationInventorySubject subject =
            incomplete.Subject
            ?? throw new InvalidOperationException(
                "Library inspection does not impose an assembly-byte bound.");
        (
            LibraryTypePopulationCountBound bound,
            long limit,
            long measured) = incomplete.Bound switch
            {
                LibraryTypeDeclarationInventoryInspectionBound.MetadataRows =>
                    (
                        LibraryTypePopulationCountBound.MetadataRows,
                        request.Plan.Bounds.MaxMetadataRows,
                        incomplete.MeasuredMetadataRows
                            ?? throw new InvalidOperationException(
                                "Metadata-row exhaustion omitted its measurement.")),
                LibraryTypeDeclarationInventoryInspectionBound
                        .RetainedDeclarations =>
                    (
                        LibraryTypePopulationCountBound.RetainedDeclarations,
                        MaximumRetainedDeclarations(request.Plan.Bounds),
                        incomplete.MeasuredDeclarations
                            ?? throw new InvalidOperationException(
                                "Declaration retention exhaustion omitted its measurement.")),
                LibraryTypeDeclarationInventoryInspectionBound
                        .RetainedTextCharacters =>
                    (
                        LibraryTypePopulationCountBound
                            .RetainedTextCharacters,
                        request.Plan.Bounds.MaxRetainedTextCharacters,
                        incomplete.MeasuredRetainedTextCharacters
                            ?? throw new InvalidOperationException(
                                "Text retention exhaustion omitted its measurement.")),
                LibraryTypeDeclarationInventoryInspectionBound.AssemblyBytes =>
                    throw new InvalidOperationException(
                        "Library inspection does not impose an assembly-byte bound."),
                _ => throw new InvalidOperationException(
                    "Unknown Library declaration inventory bound."),
            };
        var count = new LibraryTypePopulationCountOutcome.Incomplete(
            bound,
            limit,
            measured);
        return Available(
            subject,
            request,
            count,
            incomplete.MeasuredMetadataRows ?? 0,
            incomplete.MeasuredDeclarations ?? 0,
            incomplete.MeasuredRetainedTextCharacters ?? 0,
            [
                new(
                    Code(bound),
                    InspectionDiagnosticSeverity.Warning,
                    Message(bound)),
            ]);
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Available(
        LibraryTypeDeclarationInventorySubject subject,
        LibraryInspectionRequest request,
        LibraryTypePopulationCountOutcome count,
        long metadataRows,
        long retainedDeclarations,
        long retainedTextCharacters,
        ImmutableArray<InspectionDiagnostic> diagnostics)
    {
        AssemblyReferenceIdentity identity = subject.AssemblyIdentity;
        var portableIdentity = new LibraryAssemblyIdentity(
            new InertString(TextPolicy.Field, identity.Name),
            identity.Version
                ?? throw new InvalidOperationException(
                    "The inspected Library assembly identity has no version."),
            identity.Culture is null
                ? null
                : new InertString(TextPolicy.Field, identity.Culture),
            identity.PublicKeyToken is null
                ? null
                : new InertString(
                    TextPolicy.Field,
                    identity.PublicKeyToken));
        var binding = new LibraryTypePopulationBinding(
            subject.ModuleVersionId,
            LibraryTypeAccessibility.Public);
        var document = new LibraryDocument(
            portableIdentity,
            subject.ModuleVersionId,
            new(binding, count),
            new(
                subject.AssemblyBytes,
                metadataRows,
                retainedDeclarations,
                retainedTextCharacters),
            request.Plan.Bounds);
        return new(
            new LibraryInspectionOutcome.Available(document),
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            diagnostics);
    }

    private static (
        LibraryTypePopulationCountOutcome Count,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        Count(
            AssemblyTypeDeclarationInventory inventory,
            ApiSurfaceExtractionBounds bounds,
            CancellationToken cancellationToken)
    {
        int forwarders = 0;
        int classes = 0;
        int structs = 0;
        int interfaces = 0;
        int enums = 0;
        int delegates = 0;
        foreach (
            AssemblyTypeDeclaration declaration
            in inventory.GetDeclarations())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (declaration.Kind)
            {
                case AssemblyTypeDeclarationKind.Definition:
                    switch (
                        declaration.DefinitionKind
                        ?? throw new InvalidOperationException(
                            "A Type definition declaration omitted its kind."))
                    {
                        case AssemblyTypeDefinitionKind.Class:
                            classes++;
                            break;
                        case AssemblyTypeDefinitionKind.ValueType:
                            structs++;
                            break;
                        case AssemblyTypeDefinitionKind.Interface:
                            interfaces++;
                            break;
                        case AssemblyTypeDefinitionKind.Enum:
                            enums++;
                            break;
                        case AssemblyTypeDefinitionKind.Delegate:
                            delegates++;
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unknown Type definition kind.");
                    }
                    break;
                case AssemblyTypeDeclarationKind.Forwarder:
                    forwarders++;
                    break;
                case AssemblyTypeDeclarationKind.ModuleExport:
                    {
                        const LibraryTypePopulationCountUnavailableReason
                            reason =
                                LibraryTypePopulationCountUnavailableReason
                                    .UnsupportedModuleExport;
                        return (
                            new LibraryTypePopulationCountOutcome.Unavailable(
                                reason),
                            [
                                new(
                                    Code(reason),
                                    InspectionDiagnosticSeverity.Error,
                                    Message(reason)),
                            ]);
                    }
                default:
                    throw new InvalidOperationException(
                        "Unknown Type declaration kind.");
            }
        }

        int definitions =
            checked(classes + structs + interfaces + enums + delegates);
        if (definitions > bounds.MaxTypes)
        {
            return Incomplete(
                LibraryTypePopulationCountBound.Definitions,
                bounds.MaxTypes,
                definitions);
        }
        if (forwarders > bounds.MaxTypeForwarders)
        {
            return Incomplete(
                LibraryTypePopulationCountBound.Forwarders,
                bounds.MaxTypeForwarders,
                forwarders);
        }

        return (
            new LibraryTypePopulationCountOutcome.Counted(
                forwarders,
                classes,
                structs,
                interfaces,
                enums,
                delegates),
            []);
    }

    private static (
        LibraryTypePopulationCountOutcome Count,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        Incomplete(
            LibraryTypePopulationCountBound bound,
            long limit,
            long measured) =>
        (
            new LibraryTypePopulationCountOutcome.Incomplete(
                bound,
                limit,
                measured),
            [
                new(
                    Code(bound),
                    InspectionDiagnosticSeverity.Warning,
                    Message(bound)),
            ]);

    private static LibraryTypeDeclarationInventoryInspectionBounds
        InventoryBounds(ApiSurfaceExtractionBounds bounds) =>
        new(
            maximumAssemblyBytes: int.MaxValue,
            maximumRetainedDeclarations:
                MaximumRetainedDeclarations(bounds),
            maximumMetadataRows: bounds.MaxMetadataRows,
            maximumRetainedTextCharacters:
                bounds.MaxRetainedTextCharacters);

    private static int MaximumRetainedDeclarations(
        ApiSurfaceExtractionBounds bounds) =>
        (int)Math.Min(
            (long)bounds.MaxTypes + bounds.MaxTypeForwarders,
            int.MaxValue);

    private static InspectionEnvelope<LibraryInspectionOutcome> Rejected(
        LibraryTypeDeclarationInventoryInspectionRejectionKind rejection)
    {
        LibraryInspectionRejection reason = rejection switch
        {
            LibraryTypeDeclarationInventoryInspectionRejectionKind
                    .LeaseReferenceMismatch =>
                LibraryInspectionRejection.LeaseReferenceMismatch,
            LibraryTypeDeclarationInventoryInspectionRejectionKind
                    .AssemblyIdentityMismatch =>
                LibraryInspectionRejection.AssemblyIdentityMismatch,
            _ => throw new InvalidOperationException(
                "Unknown Library declaration inventory rejection."),
        };
        string message = reason switch
        {
            LibraryInspectionRejection.LeaseReferenceMismatch =>
                "The Library lease does not belong to the requested Library.",
            LibraryInspectionRejection.AssemblyIdentityMismatch =>
                "The realized API assembly identity does not match the Library reference.",
            _ => throw new InvalidOperationException(
                "Unknown Library inspection rejection."),
        };
        return new(
            new LibraryInspectionOutcome.Rejected(reason),
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            [
                new(
                    Code(reason),
                    InspectionDiagnosticSeverity.Error,
                    message),
            ]);
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Failed(
        LibraryTypeDeclarationInventoryInspectionFailureKind failure)
    {
        LibraryInspectionFailure reason = failure switch
        {
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .NotManagedAssembly =>
                LibraryInspectionFailure.NotManagedAssembly,
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .ManagedModule =>
                LibraryInspectionFailure.ManagedModule,
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .UnsupportedWindowsMetadata =>
                LibraryInspectionFailure.UnsupportedWindowsMetadata,
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .MalformedMetadata =>
                LibraryInspectionFailure.MalformedMetadata,
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .EmptyModuleVersionId =>
                LibraryInspectionFailure.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown Library declaration inventory failure."),
        };
        string message = reason switch
        {
            LibraryInspectionFailure.NotManagedAssembly =>
                "The Library API content is not a managed assembly.",
            LibraryInspectionFailure.ManagedModule =>
                "The Library API content is a managed module, not an assembly.",
            LibraryInspectionFailure.UnsupportedWindowsMetadata =>
                "Windows Metadata is not a supported Library input.",
            LibraryInspectionFailure.MalformedMetadata =>
                "The Library API content has malformed Metadata.",
            LibraryInspectionFailure.EmptyModuleVersionId =>
                "The Library API assembly has no MVID.",
            _ => throw new InvalidOperationException(
                "Unknown Library inspection failure."),
        };
        return new(
            new LibraryInspectionOutcome.Failed(reason),
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            [
                new(
                    Code(reason),
                    InspectionDiagnosticSeverity.Error,
                    message),
            ]);
    }

    private static string Code(
        LibraryTypePopulationCountUnavailableReason reason) =>
        reason switch
        {
            LibraryTypePopulationCountUnavailableReason
                    .UnsupportedModuleExport =>
                "library-inspection.types.count.unavailable.unsupported-module-export",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count unavailability."),
        };

    private static string Message(
        LibraryTypePopulationCountUnavailableReason reason) =>
        reason switch
        {
            LibraryTypePopulationCountUnavailableReason
                    .UnsupportedModuleExport =>
                "The public Type Count encountered a module-export declaration that is not an admitted definition or forwarder.",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count unavailability."),
        };

    private static string Code(LibraryTypePopulationCountBound bound) =>
        bound switch
        {
            LibraryTypePopulationCountBound.MetadataRows =>
                "library-inspection.types.count.incomplete.metadata-rows",
            LibraryTypePopulationCountBound.RetainedDeclarations =>
                "library-inspection.types.count.incomplete.retained-declarations",
            LibraryTypePopulationCountBound.RetainedTextCharacters =>
                "library-inspection.types.count.incomplete.retained-text-characters",
            LibraryTypePopulationCountBound.Definitions =>
                "library-inspection.types.count.incomplete.definitions",
            LibraryTypePopulationCountBound.Forwarders =>
                "library-inspection.types.count.incomplete.forwarders",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count bound."),
        };

    private static string Message(LibraryTypePopulationCountBound bound) =>
        bound switch
        {
            LibraryTypePopulationCountBound.MetadataRows =>
                "The public Type Count exceeded its Metadata-row bound.",
            LibraryTypePopulationCountBound.RetainedDeclarations =>
                "The public Type Count exceeded its retained-declaration bound.",
            LibraryTypePopulationCountBound.RetainedTextCharacters =>
                "The public Type Count exceeded its retained-text bound.",
            LibraryTypePopulationCountBound.Definitions =>
                "The public Type Count exceeded its definition bound.",
            LibraryTypePopulationCountBound.Forwarders =>
                "The public Type Count exceeded its forwarder bound.",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count bound."),
        };

    private static string Code(LibraryInspectionRejection reason) =>
        reason switch
        {
            LibraryInspectionRejection.LeaseReferenceMismatch =>
                "library-inspection.rejected.lease-reference-mismatch",
            LibraryInspectionRejection.AssemblyIdentityMismatch =>
                "library-inspection.rejected.assembly-identity-mismatch",
            _ => throw new InvalidOperationException(
                "Unknown Library inspection rejection."),
        };

    private static string Code(LibraryInspectionFailure reason) =>
        reason switch
        {
            LibraryInspectionFailure.NotManagedAssembly =>
                "library-inspection.failed.not-managed-assembly",
            LibraryInspectionFailure.ManagedModule =>
                "library-inspection.failed.managed-module",
            LibraryInspectionFailure.UnsupportedWindowsMetadata =>
                "library-inspection.failed.unsupported-windows-metadata",
            LibraryInspectionFailure.MalformedMetadata =>
                "library-inspection.failed.malformed-metadata",
            LibraryInspectionFailure.EmptyModuleVersionId =>
                "library-inspection.failed.empty-module-version-id",
            _ => throw new InvalidOperationException(
                "Unknown Library inspection failure."),
        };
}
