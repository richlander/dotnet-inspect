using System.Collections.Immutable;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// Request for exact namespace discovery in one retained package realization.
/// </summary>
public sealed record PackageNamespaceDiscoveryRequest
{
    public PackageNamespaceDiscoveryRequest(
        string @namespace,
        ApiSurfaceExtractionBounds bounds,
        AssemblyContextLibraryMaterializationLimits materializationLimits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        Namespace = @namespace;
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        MaterializationLimits =
            materializationLimits
            ?? throw new ArgumentNullException(
                nameof(materializationLimits));
    }

    public string Namespace { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public AssemblyContextLibraryMaterializationLimits MaterializationLimits
    {
        get;
    }
}

/// <summary>
/// One exact package Library observation and its public namespace declarations.
/// </summary>
public sealed record PackageNamespaceDiscoveryHit(
    string PackageId,
    string PackageVersion,
    string AssetPath,
    string Library,
    string Namespace,
    ImmutableArray<LibraryTypeShape> Declarations);

/// <summary>
/// Detached result of inspecting every namesake Library in one package Root.
/// </summary>
public sealed record PackageNamespaceDiscoveryOutcome(
    PackageNamespaceDiscoveryRequest Request,
    ImmutableArray<PackageNamespaceDiscoveryHit> Hits,
    bool IsComplete);

/// <summary>
/// Drains the existing Library Type population for every namesake assembly in
/// one retained package surface realization.
/// </summary>
public static class PackageNamespaceDiscoveryInspection
{
    public static async ValueTask<
        InspectionEnvelope<PackageNamespaceDiscoveryOutcome>> ExecuteAsync(
        PackageAssemblyContextRealization realization,
        PackageNamespaceDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(request);

        ImmutableArray<string> namesakeLibraries =
            LibraryNamespaceDiscovery.NamesakeLibraryCandidates(
                request.Namespace);
        var namesakeRanks = new Dictionary<string, int>(
            namesakeLibraries.Length,
            StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < namesakeLibraries.Length; index++)
            namesakeRanks.Add(namesakeLibraries[index], index);

        if (!realization.HasAssemblyContexts
            || namesakeLibraries.IsDefaultOrEmpty)
        {
            return Envelope(request, [], isComplete: true, []);
        }

        PackageAssemblyRoleParticipant[] candidates =
        [
            .. realization.SurfaceParticipants
                .Where(participant =>
                    namesakeRanks.ContainsKey(
                        participant.Participant.Assembly.Identity.Name))
                .OrderBy(participant =>
                    namesakeRanks[
                        participant.Participant.Assembly.Identity.Name]),
        ];
        var hits =
            ImmutableArray.CreateBuilder<PackageNamespaceDiscoveryHit>();
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        bool isComplete = true;

        foreach (PackageAssemblyRoleParticipant candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextLibraryAdapterResult.Completed? completed = null;
            try
            {
                AssemblyContextLibraryAdapterResult materialization =
                    await AssemblyContextLibraryAdapter.MaterializeAsync(
                            realization.SurfaceGroup,
                            candidate.Participant,
                            AssemblyContextLibraryRole.ApiOnly,
                            request.MaterializationLimits,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (materialization
                    is not AssemblyContextLibraryAdapterResult.Completed
                        available)
                {
                    isComplete = false;
                    diagnostics.Add(
                        Diagnostic(
                            "package-namespace-discovery.materialization",
                            candidate,
                            Describe(materialization)));
                    if (materialization
                        is AssemblyContextLibraryAdapterResult.Terminal
                        {
                            CleanupFailures.Count: > 0,
                        })
                    {
                        diagnostics.Add(
                            Diagnostic(
                                "package-namespace-discovery.materialization-cleanup",
                                candidate,
                                "The failed Library materialization reported cleanup failures."));
                    }
                    continue;
                }

                completed = available;
                LibraryOperationLeaseIssueOutcome leaseIssue =
                    available.Owner.IssueOperationLease(
                        available.Reference);
                if (leaseIssue
                    is not LibraryOperationLeaseIssueOutcome.Issued issued)
                {
                    isComplete = false;
                    diagnostics.Add(
                        Diagnostic(
                            "package-namespace-discovery.lease",
                            candidate,
                            "The exact Library owner could not issue an inspection lease."));
                    continue;
                }

                using LibraryOperationLease lease = issued.Lease;
                InspectionEnvelope<LibraryInspectionOutcome> inspection =
                    LibraryInspectionOperation.Execute(
                        new(
                            available.Reference,
                            new(
                                new(
                                    LibraryTypeAccessibility.Public,
                                    count: null,
                                    rows: new(maximumRows: int.MaxValue),
                                    LibraryTypeDeclarationSelection
                                        .DefinitionsAndForwarders,
                                    ApiTypeInventoryKinds.All,
                                    request.Namespace,
                                    MetadataNamespaceMatch.Exact),
                                request.Bounds)),
                        lease,
                        cancellationToken);
                foreach (InspectionDiagnostic diagnostic
                    in inspection.Diagnostics)
                {
                    if (diagnostic.Severity
                        is InspectionDiagnosticSeverity.Error)
                    {
                        isComplete = false;
                    }
                    diagnostics.Add(
                        new(
                            diagnostic.Code,
                            diagnostic.Severity,
                            diagnostic.Summary,
                            new(
                                TextPolicy.Field,
                                Correspondence(candidate))));
                }

                if (inspection.Content
                    is not LibraryInspectionOutcome.Available
                        {
                            Document.Types.Rows:
                                LibraryTypePopulationRowsOutcome.Read rows,
                        })
                {
                    isComplete = false;
                    diagnostics.Add(
                        Diagnostic(
                            "package-namespace-discovery.inspection",
                            candidate,
                            Describe(inspection.Content)));
                    continue;
                }

                if (!rows.IsComplete)
                {
                    isComplete = false;
                    diagnostics.Add(
                        Diagnostic(
                            "package-namespace-discovery.rows",
                            candidate,
                            "The exact namespace Type rows were not fully drained."));
                    continue;
                }
                if (rows.Items.IsEmpty)
                    continue;

                hits.Add(
                    new(
                        candidate.Package.PackageId,
                        candidate.Package.PackageVersion,
                        candidate.Asset.Path,
                        candidate.Participant.Assembly.Identity.Name,
                        request.Namespace,
                        rows.Items));
            }
            finally
            {
                if (completed is not null)
                {
                    isComplete &=
                        await RetireAsync(
                                completed,
                                candidate,
                                diagnostics)
                            .ConfigureAwait(false);
                }
            }
        }

        return Envelope(
            request,
            hits.ToImmutable(),
            isComplete,
            diagnostics.ToImmutable());
    }

    private static InspectionEnvelope<PackageNamespaceDiscoveryOutcome>
        Envelope(
            PackageNamespaceDiscoveryRequest request,
            ImmutableArray<PackageNamespaceDiscoveryHit> hits,
            bool isComplete,
            ImmutableArray<InspectionDiagnostic> diagnostics) =>
        new(
            new(request, hits, isComplete),
            new InspectionShare.NonProjectable(
                "package-namespace-discovery/share",
                "Package namespace discovery does not yet have a canonical Workspace Share projection."),
            diagnostics);

    private static InspectionDiagnostic Diagnostic(
        string code,
        PackageAssemblyRoleParticipant candidate,
        string summary) =>
        new(
            code,
            InspectionDiagnosticSeverity.Error,
            summary,
            Correspondence(candidate));

    private static string Correspondence(
        PackageAssemblyRoleParticipant candidate) =>
        $"{candidate.Package.PackageId}@{candidate.Package.PackageVersion}/"
            + candidate.Asset.Path;

    private static string Describe(
        AssemblyContextLibraryAdapterResult result) =>
        result switch
        {
            AssemblyContextLibraryAdapterResult.SnapshotRejected rejected =>
                "The selected Library image could not be captured "
                    + $"({rejected.Failure.Kind}).",
            AssemblyContextLibraryAdapterResult.Incomplete incomplete =>
                "The selected Library image exceeds the inspection limit of "
                    + $"{incomplete.MaxCapturedImageBytes} bytes.",
            AssemblyContextLibraryAdapterResult.ArtifactNotPublished =>
                "The selected Library image could not be published.",
            AssemblyContextLibraryAdapterResult.MetadataNotProjected =>
                "The selected Library image could not be projected as managed Metadata.",
            AssemblyContextLibraryAdapterResult.PortablePdbRejected =>
                "The selected Library rejected an unexpected Portable PDB companion.",
            _ => throw new InvalidOperationException(
                "Unknown package Library materialization result."),
        };

    private static string Describe(LibraryInspectionOutcome outcome) =>
        outcome switch
        {
            LibraryInspectionOutcome.Rejected rejected =>
                $"The exact Library inspection was rejected ({rejected.Reason}).",
            LibraryInspectionOutcome.Failed failed =>
                $"The exact Library inspection failed ({failed.Reason}).",
            LibraryInspectionOutcome.Available
                {
                    Document.Types.Rows:
                        LibraryTypePopulationRowsOutcome.Unavailable rows,
                } =>
                    $"The exact namespace rows are unavailable ({rows.Reason}).",
            LibraryInspectionOutcome.Available
                {
                    Document.Types.Rows:
                        LibraryTypePopulationRowsOutcome.Rejected rows,
                } =>
                    $"The exact namespace rows were rejected ({rows.Reason}).",
            LibraryInspectionOutcome.Available
                {
                    Document.Types.Rows:
                        LibraryTypePopulationRowsOutcome.Incomplete rows,
                } =>
                    $"The exact namespace rows exceeded {rows.Bound} "
                        + $"({rows.Measured} > {rows.Limit}).",
            LibraryInspectionOutcome.Available
                {
                    Document.Types.Rows:
                        LibraryTypePopulationRowsOutcome.Failed rows,
                } =>
                    $"The exact namespace rows failed ({rows.Reason}).",
            _ => "The exact namespace inspection returned no readable rows.",
        };

    private static async ValueTask<bool> RetireAsync(
        AssemblyContextLibraryAdapterResult.Completed completed,
        PackageAssemblyRoleParticipant candidate,
        ImmutableArray<InspectionDiagnostic>.Builder diagnostics)
    {
        bool complete = true;
        try
        {
            await completed.Owner.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            complete = false;
            diagnostics.Add(
                Diagnostic(
                    "package-namespace-discovery.library-cleanup",
                    candidate,
                    "The exact Library owner could not retire."));
        }
        if (completed.Owner.CleanupFailures.Count > 0
            || completed.Owner.ReleaseFailures.Count > 0)
        {
            complete = false;
            diagnostics.Add(
                Diagnostic(
                    "package-namespace-discovery.library-cleanup",
                    candidate,
                    "The exact Library owner reported content release failures."));
        }

        try
        {
            await completed.Artifacts.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            complete = false;
            diagnostics.Add(
                Diagnostic(
                    "package-namespace-discovery.artifact-cleanup",
                    candidate,
                    "The adjacent Artifact session could not retire."));
        }
        if (completed.Artifacts.CleanupFailures.Count > 0)
        {
            complete = false;
            diagnostics.Add(
                Diagnostic(
                    "package-namespace-discovery.artifact-cleanup",
                    candidate,
                    "The adjacent Artifact session reported cleanup failures."));
        }

        return complete;
    }
}
