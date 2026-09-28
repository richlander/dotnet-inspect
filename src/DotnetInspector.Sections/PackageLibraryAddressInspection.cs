using System.Runtime.ExceptionServices;

using DotnetInspector.Libraries;
using DotnetInspector.Packages;

namespace DotnetInspector.Sections;

/// <summary>
/// Finite materialization and Address limits for one selected package Library.
/// </summary>
public sealed record PackageLibraryAddressInspectionLimits
{
    public static PackageLibraryAddressInspectionLimits Default { get; } =
        new();

    public PackageHouseLibraryMaterializationLimits Materialization
    {
        get;
        init;
    } = new();

    public LibraryAddressInspectionLimits Address { get; init; } = new();

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Materialization);
        ArgumentNullException.ThrowIfNull(Address);
    }
}

/// <summary>
/// Composes one exact PackageHouse compile handoff through Library
/// materialization and the shared Library Address operation.
/// </summary>
public static class PackageLibraryAddressInspection
{
    private const string CleanupEvidenceKey =
        "package-library-address.cleanup";

    public static async ValueTask<
        InspectionEnvelope<LibraryAddressInspectionOutcome>>
        ExecuteAsync(
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff,
            LibraryAddressIntent intent,
            PackageLibraryAddressInspectionLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(intent);
        limits ??= PackageLibraryAddressInspectionLimits.Default;
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        PackageHouseLibraryOptionalArtifacts optionalArtifacts =
            LibraryAddressInspectionOperation.RequiresSource(intent)
                ? PackageHouseLibraryOptionalArtifacts
                    .ImplementationPortablePdb
                : PackageHouseLibraryOptionalArtifacts.None;
        PackageHouseLibraryMaterializationOutcome materialization =
            await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    optionalArtifacts,
                    limits.Materialization,
                    cancellationToken)
                .ConfigureAwait(false);
        if (materialization
            is PackageHouseLibraryMaterializationOutcome.Terminal terminal)
        {
            return MaterializationFailure(terminal.Evidence);
        }

        var completed =
            (PackageHouseLibraryMaterializationOutcome.Completed)
                materialization;
        InspectionEnvelope<LibraryAddressInspectionOutcome>? inspection =
            null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            inspection = completed.Owner.IssueOperationLease(
                completed.Receipt.Library) switch
            {
                LibraryOperationLeaseIssueOutcome.Issued issued =>
                    ApplyMaterializationEvidence(
                        LibraryAddressInspectionOperation.Execute(
                            new(
                                completed.Receipt.Library,
                                intent,
                                limits.Address),
                            issued.Lease,
                            cancellationToken),
                        completed.Receipt),
                LibraryOperationLeaseIssueOutcome outcome =>
                    OperationLeaseFailure(outcome),
            };
        }
        catch (Exception failure)
        {
            primary = ExceptionDispatchInfo.Capture(failure);
        }

        IReadOnlyList<CleanupFailure> cleanup =
            await RetireAsync(completed).ConfigureAwait(false);
        if (primary is not null)
        {
            AttachCleanup(primary.SourceException, cleanup);
            primary.Throw();
        }

        if (inspection is null)
        {
            throw new InvalidOperationException(
                "Package Library Address inspection completed without an outcome.");
        }
        return cleanup.Count == 0
            ? inspection
            : CleanupFailureEnvelope(inspection, cleanup);
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        MaterializationFailure(
            PackageHouseLibraryMaterializationFailure failure)
    {
        string detail =
            "The selected PackageHouse Library could not be materialized: "
            + string.Join(", ", failure.Failures);
        return LibraryAddressInspectionOperation.Envelope(
            new LibraryAddressInspectionOutcome.Failed(
                LibraryAddressInspectionFailure.Inspection,
                detail),
            failure.Failures.Select(
                kind => new InspectionDiagnostic(
                    MaterializationCode(kind),
                    InspectionDiagnosticSeverity.Error,
                    MaterializationMessage(kind))));
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        OperationLeaseFailure(
            LibraryOperationLeaseIssueOutcome outcome)
    {
        (string code, string detail) = outcome switch
        {
            LibraryOperationLeaseIssueOutcome.OwnerRetiring =>
                (
                    "package-library-address.operation-owner-retiring",
                    "The materialized Library owner began retirement before Address inspection."),
            LibraryOperationLeaseIssueOutcome.OwnerReleased released =>
                (
                    "package-library-address.operation-owner-released",
                    $"The materialized Library owner was already {released.State}."),
            LibraryOperationLeaseIssueOutcome.ReferenceMismatch =>
                (
                    "package-library-address.operation-reference-mismatch",
                    "The materialized Library owner rejected its own exact Library reference."),
            _ =>
                (
                    "package-library-address.operation-unavailable",
                    "The materialized Library owner did not issue an Address operation lease."),
        };
        return LibraryAddressInspectionOperation.Envelope(
            new LibraryAddressInspectionOutcome.Failed(
                LibraryAddressInspectionFailure.Inspection,
                detail),
            [
                new(
                    code,
                    InspectionDiagnosticSeverity.Error,
                    detail),
            ]);
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        ApplyMaterializationEvidence(
            InspectionEnvelope<LibraryAddressInspectionOutcome> inspection,
            PackageHouseLibraryMaterializationReceipt receipt)
    {
        if (receipt.ImplementationPortablePdbOmission is not { } omission)
            return inspection;

        string code;
        string summary;
        switch (omission)
        {
            case PackageHouseLibraryOptionalArtifactOmissionKind
                .ContentByteLimit:
                code =
                    "package-library-address.portable-pdb.content-byte-limit";
                summary =
                    "The selected implementation Portable PDB exceeded its materialization entry-byte limit.";
                break;
            case PackageHouseLibraryOptionalArtifactOmissionKind
                .RetainedByteLimit:
                code =
                    "package-library-address.portable-pdb.retained-byte-limit";
                summary =
                    "The selected implementation Portable PDB exceeded the materialized Library retained-byte limit.";
                break;
            case PackageHouseLibraryOptionalArtifactOmissionKind.Unreadable:
                code =
                    "package-library-address.portable-pdb.unreadable";
                summary =
                    "The selected implementation Portable PDB could not be read during Library materialization.";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(omission));
        }

        return new(
            inspection.Content,
            inspection.Share,
            inspection.Diagnostics.Add(
                new(
                    code,
                    InspectionDiagnosticSeverity.Warning,
                    summary)));
    }

    private static async ValueTask<IReadOnlyList<CleanupFailure>>
        RetireAsync(
            PackageHouseLibraryMaterializationOutcome.Completed completed)
    {
        var failures = new List<CleanupFailure>(2);
        try
        {
            await completed.Owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(new("library-owner", failure));
        }

        try
        {
            await completed.Artifacts.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(new("artifact-session", failure));
        }
        return failures;
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        CleanupFailureEnvelope(
            InspectionEnvelope<LibraryAddressInspectionOutcome> inspection,
            IReadOnlyList<CleanupFailure> cleanup)
    {
        string detail =
            "Package Library Address inspection could not retire all "
            + "transferred resources: "
            + string.Join(
                "; ",
                cleanup.Select(
                    failure =>
                        $"{failure.Stage}: "
                        + $"{failure.Failure.GetType().Name}: "
                        + failure.Failure.Message));
        return new(
            new LibraryAddressInspectionOutcome.Failed(
                LibraryAddressInspectionFailure.ResourceDisposal,
                detail),
            inspection.Share,
            inspection.Diagnostics.Concat(
                cleanup.Select(
                    failure => new InspectionDiagnostic(
                        $"package-library-address.cleanup.{failure.Stage}",
                        InspectionDiagnosticSeverity.Error,
                        $"{failure.Failure.GetType().Name}: "
                            + failure.Failure.Message))));
    }

    private static void AttachCleanup(
        Exception primary,
        IReadOnlyList<CleanupFailure> cleanup)
    {
        if (cleanup.Count == 0)
            return;
        primary.Data[CleanupEvidenceKey] =
            new AggregateException(
                cleanup.Select(static failure => failure.Failure));
    }

    private static string MaterializationCode(
        PackageHouseLibraryMaterializationFailureKind kind) =>
        kind switch
        {
            PackageHouseLibraryMaterializationFailureKind.InvalidSettlement =>
                "package-library-address.materialization.invalid-settlement",
            PackageHouseLibraryMaterializationFailureKind.InvalidHandoff =>
                "package-library-address.materialization.invalid-handoff",
            PackageHouseLibraryMaterializationFailureKind.MissingPackageEntry =>
                "package-library-address.materialization.missing-entry",
            PackageHouseLibraryMaterializationFailureKind.ContentByteLimit =>
                "package-library-address.materialization.content-byte-limit",
            PackageHouseLibraryMaterializationFailureKind.RetainedByteLimit =>
                "package-library-address.materialization.retained-byte-limit",
            PackageHouseLibraryMaterializationFailureKind
                .ArtifactPublication =>
                "package-library-address.materialization.artifact-publication",
            PackageHouseLibraryMaterializationFailureKind.MetadataProjection =>
                "package-library-address.materialization.metadata-projection",
            PackageHouseLibraryMaterializationFailureKind
                .AssemblyIdentityMismatch =>
                "package-library-address.materialization.identity-mismatch",
            PackageHouseLibraryMaterializationFailureKind.ArtifactRetirement =>
                "package-library-address.materialization.artifact-retirement",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string MaterializationMessage(
        PackageHouseLibraryMaterializationFailureKind kind) =>
        kind switch
        {
            PackageHouseLibraryMaterializationFailureKind.InvalidSettlement =>
                "The selected Library handoff does not belong to the acquired PackageHouse settlement.",
            PackageHouseLibraryMaterializationFailureKind.InvalidHandoff =>
                "The acquired PackageHouse settlement did not issue the selected compile handoff.",
            PackageHouseLibraryMaterializationFailureKind.MissingPackageEntry =>
                "The selected package Library entry was unavailable during materialization.",
            PackageHouseLibraryMaterializationFailureKind.ContentByteLimit =>
                "One selected package Library entry exceeded its materialization byte limit.",
            PackageHouseLibraryMaterializationFailureKind.RetainedByteLimit =>
                "The selected package Library exceeded its aggregate retained-byte limit.",
            PackageHouseLibraryMaterializationFailureKind
                .ArtifactPublication =>
                "The selected package Library could not be published as Artifacts.",
            PackageHouseLibraryMaterializationFailureKind.MetadataProjection =>
                "Metadata could not project one selected package Library assembly.",
            PackageHouseLibraryMaterializationFailureKind
                .AssemblyIdentityMismatch =>
                "The selected API and implementation assemblies did not retain one managed identity.",
            PackageHouseLibraryMaterializationFailureKind.ArtifactRetirement =>
                "A failed package Library materialization also failed Artifact retirement.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private sealed record CleanupFailure(
        string Stage,
        Exception Failure);
}
