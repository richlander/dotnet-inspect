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
            LibraryApiTypeInventoryCountInspectionOutcome outcome =
                LibraryApiTypeInventoryCountInspection.Execute(
                    new(request.Library),
                    lease,
                    cancellationToken);
            return Project(
                outcome,
                request,
                lease,
                cancellationToken);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Project(
        LibraryApiTypeInventoryCountInspectionOutcome outcome,
        LibraryInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken) =>
        outcome switch
        {
            LibraryApiTypeInventoryCountInspectionOutcome.Completed completed =>
                Project(
                    completed.Correspondence,
                    request,
                    lease,
                    cancellationToken),
            LibraryApiTypeInventoryCountInspectionOutcome.Rejected rejected =>
                Rejected(rejected.Kind),
            LibraryApiTypeInventoryCountInspectionOutcome.Failed failed =>
                Failed(failed.Kind),
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count inspection outcome."),
        };

    private static InspectionEnvelope<LibraryInspectionOutcome> Project(
        LibraryApiTypeInventoryCountCorrespondence correspondence,
        LibraryInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken)
    {
        AssemblyReferenceIdentity identity =
            correspondence.AssemblyIdentity;
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
            correspondence.ModuleVersionId,
            LibraryTypeAccessibility.Public);

        LibraryTypePopulationCountOutcome count;
        ImmutableArray<InspectionDiagnostic> diagnostics;
        if (correspondence.Count
            is ApiTypeInventoryCountResult.Counted counted)
        {
            count = new LibraryTypePopulationCountOutcome.Counted(
                counted.Count.Classes,
                counted.Count.Structs,
                counted.Count.Interfaces,
                counted.Count.Enums,
                counted.Count.Delegates);
            diagnostics = [];
        }
        else
        {
            var declined =
                (ApiTypeInventoryCountResult.Declined)correspondence.Count;
            if (declined.Reason
                == ApiTypeInventoryCountDeclineReason.TypeForwarders)
            {
                (count, diagnostics) = Fallback(
                    request,
                    lease,
                    correspondence.ModuleVersionId,
                    cancellationToken);
            }
            else
            {
                LibraryTypePopulationCountUnavailableReason reason =
                    Map(declined.Reason);
                count =
                    new LibraryTypePopulationCountOutcome.Unavailable(reason);
                diagnostics =
                [
                    new(
                        Code(reason),
                        InspectionDiagnosticSeverity.Error,
                        Message(reason)),
                ];
            }
        }

        var document = new LibraryDocument(
            portableIdentity,
            correspondence.ModuleVersionId,
            new(binding, count),
            new(correspondence.AssemblyBytes),
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
        Fallback(
            LibraryInspectionRequest request,
            LibraryOperationLease lease,
            Guid moduleVersionId,
            CancellationToken cancellationToken)
    {
        LibraryApiSurfaceInspectionOutcome fallback =
            LibraryApiSurfaceInspection.Execute(
                new(
                    request.Library,
                    ApiSurfaceExtractionScope.Public,
                    request.Plan.Bounds,
                    typesOnly: true),
                lease,
                cancellationToken);
        return fallback switch
        {
            LibraryApiSurfaceInspectionOutcome.Completed completed =>
                Count(completed.Correspondence, moduleVersionId),
            LibraryApiSurfaceInspectionOutcome.Incomplete incomplete =>
                (
                    new LibraryTypePopulationCountOutcome.Incomplete(
                        incomplete.Bound),
                    [
                        new(
                            "library-inspection.types.count.incomplete.extraction-bound",
                            InspectionDiagnosticSeverity.Warning,
                            "The public Type Count exceeded its extraction bounds."),
                    ]),
            LibraryApiSurfaceInspectionOutcome.Rejected rejected =>
                throw new InvalidOperationException(
                    $"A compact-count fallback rejected the already-attested Library ({rejected.Kind})."),
            LibraryApiSurfaceInspectionOutcome.Failed failed =>
                throw new InvalidOperationException(
                    $"A compact-count fallback failed for the already-attested Library ({failed.Kind})."),
            _ => throw new InvalidOperationException(
                "Unknown Library API-surface fallback outcome."),
        };
    }

    private static (
        LibraryTypePopulationCountOutcome Count,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        Count(
            LibraryApiSurfaceCorrespondence correspondence,
            Guid moduleVersionId)
    {
        if (correspondence.ModuleVersionId != moduleVersionId)
        {
            throw new InvalidOperationException(
                "Compact and fallback Library Type Counts observed different MVIDs.");
        }

        ApiSurface surface = correspondence.Surface;
        if (surface.InspectionFailures.Count > 0)
        {
            return (
                new LibraryTypePopulationCountOutcome.Unavailable(
                    LibraryTypePopulationCountUnavailableReason
                        .TypeForwardersRequireResolution),
                [
                    new(
                        "library-inspection.types.count.unavailable.type-forwarders-require-resolution",
                        InspectionDiagnosticSeverity.Error,
                        "The public Type Count could not retain exact Type-forwarder evidence."),
                ]);
        }

        int classes = 0;
        int structs = 0;
        int interfaces = 0;
        int enums = 0;
        int delegates = 0;
        foreach (ApiType type in surface.Types)
        {
            switch (type.Kind)
            {
                case "class":
                    classes++;
                    break;
                case "struct":
                    structs++;
                    break;
                case "interface":
                    interfaces++;
                    break;
                case "enum":
                    enums++;
                    break;
                case "delegate":
                    delegates++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown API Type kind '{type.Kind}'.");
            }
        }

        var count = new ApiTypeInventoryCount(
            moduleVersionId,
            classes,
            structs,
            interfaces,
            enums,
            delegates);
        if (count.Total != surface.PublicTypeCount)
        {
            throw new InvalidOperationException(
                "Fallback public Type-kind Counts do not equal the API-surface Type Count.");
        }

        return (
            new LibraryTypePopulationCountOutcome.Counted(
                count.Classes,
                count.Structs,
                count.Interfaces,
                count.Enums,
                count.Delegates),
            []);
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Rejected(
        LibraryApiTypeInventoryCountInspectionRejectionKind rejection)
    {
        LibraryInspectionRejection reason = rejection switch
        {
            LibraryApiTypeInventoryCountInspectionRejectionKind
                    .LeaseReferenceMismatch =>
                LibraryInspectionRejection.LeaseReferenceMismatch,
            LibraryApiTypeInventoryCountInspectionRejectionKind
                    .AssemblyIdentityMismatch =>
                LibraryInspectionRejection.AssemblyIdentityMismatch,
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count rejection."),
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
        LibraryApiTypeInventoryCountInspectionFailureKind failure)
    {
        LibraryInspectionFailure reason = failure switch
        {
            LibraryApiTypeInventoryCountInspectionFailureKind
                    .NotManagedAssembly =>
                LibraryInspectionFailure.NotManagedAssembly,
            LibraryApiTypeInventoryCountInspectionFailureKind.ManagedModule =>
                LibraryInspectionFailure.ManagedModule,
            LibraryApiTypeInventoryCountInspectionFailureKind
                    .UnsupportedWindowsMetadata =>
                LibraryInspectionFailure.UnsupportedWindowsMetadata,
            LibraryApiTypeInventoryCountInspectionFailureKind
                    .MalformedMetadata =>
                LibraryInspectionFailure.MalformedMetadata,
            LibraryApiTypeInventoryCountInspectionFailureKind
                    .EmptyModuleVersionId =>
                LibraryInspectionFailure.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count failure."),
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

    private static LibraryTypePopulationCountUnavailableReason Map(
        ApiTypeInventoryCountDeclineReason reason) =>
        reason switch
        {
            ApiTypeInventoryCountDeclineReason.MissingModuleVersionId =>
                throw new InvalidOperationException(
                    "Compact Type Count declined after a non-empty MVID was established."),
            ApiTypeInventoryCountDeclineReason.TypeForwarders =>
                LibraryTypePopulationCountUnavailableReason
                    .TypeForwardersRequireResolution,
            ApiTypeInventoryCountDeclineReason.MalformedExportedType =>
                LibraryTypePopulationCountUnavailableReason
                    .MalformedExportedType,
            ApiTypeInventoryCountDeclineReason.MalformedTypeIdentity =>
                LibraryTypePopulationCountUnavailableReason
                    .MalformedTypeIdentity,
            ApiTypeInventoryCountDeclineReason.MalformedTypeRow =>
                LibraryTypePopulationCountUnavailableReason.MalformedTypeRow,
            _ => throw new InvalidOperationException(
                "Unknown compact Type Count decline."),
        };

    private static string Code(
        LibraryTypePopulationCountUnavailableReason reason) =>
        reason switch
        {
            LibraryTypePopulationCountUnavailableReason
                    .TypeForwardersRequireResolution =>
                "library-inspection.types.count.unavailable.type-forwarders-require-resolution",
            LibraryTypePopulationCountUnavailableReason
                    .MalformedExportedType =>
                "library-inspection.types.count.unavailable.malformed-exported-type",
            LibraryTypePopulationCountUnavailableReason
                    .MalformedTypeIdentity =>
                "library-inspection.types.count.unavailable.malformed-type-identity",
            LibraryTypePopulationCountUnavailableReason.MalformedTypeRow =>
                "library-inspection.types.count.unavailable.malformed-type-row",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count unavailability."),
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

    private static string Message(
        LibraryTypePopulationCountUnavailableReason reason) =>
        reason switch
        {
            LibraryTypePopulationCountUnavailableReason
                    .TypeForwardersRequireResolution =>
                "The public Type Count requires resolution of Type forwarders.",
            LibraryTypePopulationCountUnavailableReason
                    .MalformedExportedType =>
                "The public Type Count encountered malformed exported-Type Metadata.",
            LibraryTypePopulationCountUnavailableReason
                    .MalformedTypeIdentity =>
                "The public Type Count encountered a malformed Type identity.",
            LibraryTypePopulationCountUnavailableReason.MalformedTypeRow =>
                "The public Type Count encountered a malformed Type row.",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count unavailability."),
        };

}
