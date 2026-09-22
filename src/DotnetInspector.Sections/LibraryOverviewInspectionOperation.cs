using System.Collections.Immutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one bounded overview over an exact realized Library.
/// </summary>
public static class LibraryOverviewInspectionOperation
{
    private const string SharePath = "library-overview/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<LibraryOverviewOutcome> Execute(
        LibraryOverviewRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        LibraryOverviewQueryPlan plan;
        try
        {
            plan = LibraryOverviewQuery.CreatePlan(
                QuerySpaceTerminalRequirement.Rows);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        return Execute(
            request,
            plan,
            lease,
            cancellationToken);
    }

    public static InspectionEnvelope<LibraryOverviewOutcome> Execute(
        LibraryOverviewRequest request,
        LibraryOverviewQueryPlan plan,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.Terminal != QuerySpaceTerminalRequirement.Rows)
            {
                throw new ArgumentException(
                    "Library overview Execute requires a Rows QuerySpace plan.",
                    nameof(plan));
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        InspectionEnvelope<LibraryOverviewOutcome> envelope =
            ExecuteCore(request, lease, cancellationToken);
        if (envelope.Content
            is not LibraryOverviewOutcome.Available available)
        {
            return envelope;
        }

        SectionRowsOutcome<string, LibraryOverviewDocument> rows =
            LibraryOverviewQuery.ApplyRows(
                plan,
                available.Document);
        if (!rows.IsSuccess)
        {
            throw new InvalidOperationException(
                "The owner-issued Library overview Rows plan failed.");
        }

        return new InspectionEnvelope<LibraryOverviewOutcome>(
            new LibraryOverviewOutcome.Available(
                rows.Rebind(available.Document)),
            envelope.Share,
            envelope.Diagnostics);
    }

    public static LibraryOverviewCountResult ExecuteCount(
        LibraryOverviewRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        LibraryOverviewQueryPlan plan;
        try
        {
            plan = LibraryOverviewQuery.CreatePlan(
                QuerySpaceTerminalRequirement.Count);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        return ExecuteCount(
            request,
            plan,
            lease,
            cancellationToken);
    }

    public static LibraryOverviewCountResult ExecuteCount(
        LibraryOverviewRequest request,
        LibraryOverviewQueryPlan plan,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.Terminal != QuerySpaceTerminalRequirement.Count)
            {
                throw new ArgumentException(
                    "Library overview Count requires a Count QuerySpace plan.",
                    nameof(plan));
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        InspectionEnvelope<LibraryOverviewOutcome> overview =
            ExecuteCore(request, lease, cancellationToken);
        if (overview.Content
            is not LibraryOverviewOutcome.Available available)
        {
            return new LibraryOverviewCountResult.NotAvailable(overview);
        }

        SectionCountOutcome<string, string> count =
            LibraryOverviewQuery.ApplyCount(
                plan,
                available.Document);
        var completed =
            count as SectionCountOutcome<string, string>.Completed
            ?? throw new InvalidOperationException(
                "The owner-issued Library overview Count plan failed.");
        SectionCountEntry<string> entry =
            completed.Counts.Single();
        if (!string.Equals(
                entry.Identity,
                LibraryOverviewQuery.OverviewRowSet,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Library overview Count result named an unknown row set.");
        }

        return new LibraryOverviewCountResult.Completed(
            new InspectionEnvelope<int>(
                entry.Value,
                overview.Share,
                overview.Diagnostics));
    }

    private static InspectionEnvelope<LibraryOverviewOutcome> ExecuteCore(
        LibraryOverviewRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            LibraryApiSurfaceInspectionOutcome inspection =
                LibraryApiSurfaceInspection.Execute(
                    new LibraryApiSurfaceInspectionRequest(
                        request.Library,
                        ApiSurfaceExtractionScope.Public,
                        request.Bounds),
                    lease,
                    cancellationToken);
            LibraryOverviewOutcome outcome =
                Project(inspection, request.Bounds);
            return new InspectionEnvelope<LibraryOverviewOutcome>(
                outcome,
                new InspectionShare.NonProjectable(
                    SharePath,
                    ShareReason),
                Diagnostics(outcome));
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static LibraryOverviewOutcome Project(
        LibraryApiSurfaceInspectionOutcome inspection,
        ApiSurfaceExtractionBounds bounds) =>
        inspection switch
        {
            LibraryApiSurfaceInspectionOutcome.Completed completed =>
                Project(completed.Correspondence, bounds),
            LibraryApiSurfaceInspectionOutcome.Incomplete incomplete =>
                new LibraryOverviewOutcome.Incomplete(
                    new LibraryOverviewIncompleteReason.ExtractionBound(
                        incomplete.Bound)),
            LibraryApiSurfaceInspectionOutcome.Rejected rejected =>
                new LibraryOverviewOutcome.Rejected(
                    rejected.Kind switch
                    {
                        LibraryApiSurfaceInspectionRejectionKind
                            .LeaseReferenceMismatch =>
                            LibraryOverviewRejection
                                .LeaseReferenceMismatch,
                        LibraryApiSurfaceInspectionRejectionKind
                            .AssemblyIdentityMismatch =>
                            LibraryOverviewRejection
                                .AssemblyIdentityMismatch,
                        _ => throw new InvalidOperationException(
                            "Unknown Library API-surface rejection."),
                    }),
            LibraryApiSurfaceInspectionOutcome.Failed failed =>
                new LibraryOverviewOutcome.Failed(
                    failed.Kind switch
                    {
                        LibraryApiSurfaceInspectionFailureKind
                            .NotManagedAssembly =>
                            LibraryOverviewFailure.NotManagedAssembly,
                        LibraryApiSurfaceInspectionFailureKind
                            .ManagedModule =>
                            LibraryOverviewFailure.ManagedModule,
                        LibraryApiSurfaceInspectionFailureKind
                            .UnsupportedWindowsMetadata =>
                            LibraryOverviewFailure
                                .UnsupportedWindowsMetadata,
                        LibraryApiSurfaceInspectionFailureKind
                            .MalformedMetadata =>
                            LibraryOverviewFailure.MalformedMetadata,
                        LibraryApiSurfaceInspectionFailureKind
                            .EmptyModuleVersionId =>
                            LibraryOverviewFailure.EmptyModuleVersionId,
                        _ => throw new InvalidOperationException(
                            "Unknown Library API-surface failure."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown Library API-surface inspection outcome."),
        };

    private static LibraryOverviewOutcome Project(
        LibraryApiSurfaceCorrespondence correspondence,
        ApiSurfaceExtractionBounds bounds)
    {
        ApiSurface surface = correspondence.Surface;
        if (surface.InspectionFailures.Count > 0)
        {
            return new LibraryOverviewOutcome.Incomplete(
                new LibraryOverviewIncompleteReason
                    .MetadataInspectionFailures(
                        surface.InspectionFailures.Count));
        }

        ApiAssemblyIdentity identity =
            surface.AssemblyIdentity
            ?? throw new InvalidOperationException(
                "Completed Library API surface has no assembly identity.");
        var document = new LibraryOverviewDocument(
            new LibraryOverviewAssemblyIdentity(
                new InertString(TextPolicy.Field, identity.Name),
                identity.Version!,
                Contained(identity.Culture),
                Contained(identity.PublicKeyToken)),
            correspondence.ModuleVersionId,
            surface.PublicTypeCount,
            surface.PublicMethodCount,
            surface.PublicPropertyCount,
            surface.PublicEventCount,
            surface.PublicFieldCount,
            checked(
                (long)surface.PublicMethodCount
                + surface.PublicPropertyCount
                + surface.PublicEventCount
                + surface.PublicFieldCount),
            correspondence.MetadataRows,
            correspondence.RetainedTextCharacters,
            bounds);
        return new LibraryOverviewOutcome.Available(document);
    }

    private static InertString? Contained(string? value) =>
        value is null
            ? null
            : new InertString(TextPolicy.Field, value);

    private static ImmutableArray<InspectionDiagnostic> Diagnostics(
        LibraryOverviewOutcome outcome)
    {
        InspectionDiagnostic? diagnostic = outcome switch
        {
            LibraryOverviewOutcome.Available => null,
            LibraryOverviewOutcome.Incomplete
            {
                Reason:
                    LibraryOverviewIncompleteReason.ExtractionBound,
            } => new(
                "library-overview.incomplete.extraction-bound",
                InspectionDiagnosticSeverity.Warning,
                "The Library overview exceeded its extraction bounds."),
            LibraryOverviewOutcome.Incomplete
            {
                Reason:
                    LibraryOverviewIncompleteReason
                        .MetadataInspectionFailures,
            } => new(
                "library-overview.incomplete.metadata-inspection-failures",
                InspectionDiagnosticSeverity.Warning,
                "The Library overview omitted one or more declarations."),
            LibraryOverviewOutcome.Rejected
            {
                Reason:
                    LibraryOverviewRejection.LeaseReferenceMismatch,
            } => new(
                "library-overview.rejected.lease-reference-mismatch",
                InspectionDiagnosticSeverity.Error,
                "The transferred Library operation lease does not match the requested Library."),
            LibraryOverviewOutcome.Rejected
            {
                Reason:
                    LibraryOverviewRejection.AssemblyIdentityMismatch,
            } => new(
                "library-overview.rejected.assembly-identity-mismatch",
                InspectionDiagnosticSeverity.Error,
                "The owner-attested API assembly does not match the Library identity."),
            LibraryOverviewOutcome.Failed failed =>
                FailureDiagnostic(failed.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Library overview outcome."),
        };
        return diagnostic is null ? [] : [diagnostic];
    }

    private static InspectionDiagnostic FailureDiagnostic(
        LibraryOverviewFailure failure) =>
        failure switch
        {
            LibraryOverviewFailure.NotManagedAssembly =>
                new(
                    "library-overview.failed.not-managed-assembly",
                    InspectionDiagnosticSeverity.Error,
                    "The Library API content is not a managed assembly."),
            LibraryOverviewFailure.ManagedModule =>
                new(
                    "library-overview.failed.managed-module",
                    InspectionDiagnosticSeverity.Error,
                    "The Library API content is a managed module, not an assembly."),
            LibraryOverviewFailure.UnsupportedWindowsMetadata =>
                new(
                    "library-overview.failed.unsupported-windows-metadata",
                    InspectionDiagnosticSeverity.Error,
                    "Windows Metadata is not a supported Library input."),
            LibraryOverviewFailure.MalformedMetadata =>
                new(
                    "library-overview.failed.malformed-metadata",
                    InspectionDiagnosticSeverity.Error,
                    "The Library API content contains malformed Metadata."),
            LibraryOverviewFailure.EmptyModuleVersionId =>
                new(
                    "library-overview.failed.empty-module-version-id",
                    InspectionDiagnosticSeverity.Error,
                    "The Library API assembly has an empty module-version identity."),
            _ => throw new InvalidOperationException(
                "Unknown Library overview failure."),
        };
}
