using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspect.Web.Interop.Package;

[SupportedOSPlatform("browser")]
public static partial class PackageExports
{
    /// <summary>
    /// Runs the same rooted package traversal as the CLI hierarchy section.
    /// The Browser supplies its own authorized source and operation deadline.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryPackageHierarchy(
        string packageId,
        string version,
        string targetFramework,
        string? traversalTargetFramework,
        string? workspaceRealizationId,
        string inventoryJson)
    {
        BrowserEcosystemPackageInventory? inventory =
            JsonSerializer.Deserialize(
                inventoryJson,
                BrowserPackageJsonContext.Default.BrowserEcosystemPackageInventory);
        BrowserPackageHierarchy hierarchy =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                async deadline =>
                {
                    WorkspacePlan plan = await HierarchyWorkspacePlanAsync(
                        workspaceRealizationId,
                        traversalTargetFramework,
                        deadline.Token).ConfigureAwait(false);
                    PlatformPruneInventory[] inventories =
                        PlatformSupplyInventories(inventory);
                    BrowserPackageCoordinate coordinate =
                        await BrowserPackageWorkspace.ResolveAsync(
                            packageId,
                            version,
                            targetFramework,
                            deadline.Token).ConfigureAwait(false);
                    PackageDependencyEvidenceRoot root =
                        await PackageDependencyEvidenceAsync(coordinate)
                            .ConfigureAwait(false);
                    IPackageSourceClient source =
                        BrowserPackageWorkspace.Gallery;
                    await using PackageSourceSettlementLease sourceLease =
                        PackageSourceSettlementService.IssueLease(
                            authority => ReferenceEquals(
                                authority.Association,
                                source.Source.Association)
                                ? source
                                : throw new InvalidOperationException(
                                    "The Package hierarchy requested an "
                                        + "unauthorized package source."));
                    TimeSpan operationTimeout =
                        BrowserPackageWorkspace
                            .SourceSettlementOperationTimeout(
                                deadline.Remaining);
                    using PackageSourceOperationLease traversalOperation =
                        sourceLease.IssueOperationLease(
                            deadline.Token,
                            operationTimeout,
                            operationTimeout,
                            PackageDependencyTraversalQuery.DefaultMaximumConcurrentSteps);
                    PackageDependencyTraversalOutcome traversal =
                        await PackageDependencyTraversalQuery.ExecuteAsync(
                            new PackageDependencyTraversalRequest(
                                [new PackageDependencyTraversalRootOccurrence(
                                    root,
                                    PackageDependencyTraversalExpansionAuthority
                                        .RecursiveSources,
                                    PackageDependencyTraversalRootRecurrenceAuthority
                                        .ExactCoordinate)],
                                plan.TraversalTargetPolicy,
                                new PackageDependencyTraversalCandidateAdapter(
                                    BrowserPackageWorkspace
                                        .PackageSourceAuthorization),
                                new AuthorizedPackageDependencyManifestSource(),
                                new PackageDependencyTraversalWorkBudget(
                                    1_024, 16_384),
                                platformPruning: new(
                                    plan, inventories,
                                    inventory?.Supplies is null
                                        ? "The matching Platform catalog supply inventory is unavailable."
                                        : null)),
                            traversalOperation).ConfigureAwait(false);
                    DependencyHierarchyDocument document =
                        DependencyHierarchyDocument.Create(
                            PackageTraversalGraphProjection.Project(
                                traversal, [1]));
                    Dictionary<int, DependencyGraphNode> nodes =
                        document.BackingGraph.Nodes.ToDictionary(
                            static node => node.Id);
                    Dictionary<int, DependencyGraphEdge> edges =
                        document.BackingGraph.Edges.ToDictionary(
                            static edge => edge.Id);
                    BrowserPackageHierarchyRow[] rows =
                    [
                        .. document.Roots.Select(rootOccurrence =>
                            new BrowserPackageHierarchyRow(
                                rootOccurrence.Identity.Value,
                                ParentId: null,
                                rootOccurrence.Depth,
                                nodes[rootOccurrence.NodeId].Label.ToString(),
                                "Root",
                                "Resolved")),
                        .. document.Occurrences.Select(occurrence =>
                            new BrowserPackageHierarchyRow(
                                occurrence.Identity.Value,
                                occurrence.ParentIdentity.Value,
                                occurrence.Depth,
                                nodes[occurrence.TargetNodeId].Label.ToString(),
                                occurrence.Disposition.ToString(),
                                edges[occurrence.IncomingEdgeId]
                                    .Resolution.ToString())),
                    ];
                    return new BrowserPackageHierarchy(
                        coordinate.PackageId,
                        coordinate.Version,
                        coordinate.Framework,
                        plan.TraversalTargetPolicy.TargetFramework,
                        traversal.IsComplete,
                        traversal.Roots[0].Completion.ToString(),
                        rows,
                        [.. traversal.Failures.Select(failure =>
                            DescribeHierarchyFailure(traversal, failure))])
                    {
                        Pruning = DependencyHierarchyPruning.FromTraversal(traversal),
                    };
                },
                BrowserPackageWorkspace.PackageOperationTimeout)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            hierarchy,
            BrowserPackageJsonContext.Default.BrowserPackageHierarchy);
    }

    private static async Task<WorkspacePlan> HierarchyWorkspacePlanAsync(
        string? realizationId,
        string? fallbackFramework,
        CancellationToken cancellationToken)
    {
        if (realizationId is null)
        {
            WorkspacePlan product = BrowserPackageWorkspace.ProductWorkspacePlan;
            return new(
                fallbackFramework is null
                    ? TraversalTargetFrameworkPolicy.ProductDefault
                    : new TraversalTargetFrameworkPolicy(fallbackFramework),
                product.Registrations,
                product.Contexts);
        }

        BrowserRetainedWorkspaceActivationOwner owner =
            BrowserRetainedWorkspaceActivationRegistry.Owner;
        BrowserRetainedWorkspacePosting posting = owner.Active
            is { } active && active.RealizationId == realizationId
                ? active
                : throw new InvalidOperationException(
                    "The Hierarchy Workspace realization is no longer active.");
        WorkspaceRealizationOperationAdmission admission =
            await owner.EnterOperationAsync(
                posting.RetainedDefinitionId, cancellationToken)
                .ConfigureAwait(false);
        if (admission is not WorkspaceRealizationOperationAdmission.Admitted admitted)
            throw new InvalidOperationException(
                "The Hierarchy Workspace realization is unavailable.");
        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        if (!ReferenceEquals(operation.Realization, posting.Realization))
            throw new InvalidOperationException(
                "The Hierarchy Workspace realization changed.");
        WorkspaceRegistrationReadResult registrations =
            operation.Workspace.GetRegistrationSnapshot();
        if (registrations is not WorkspaceRegistrationReadResult.Available available)
            throw new InvalidOperationException(
                "The Hierarchy Workspace registration revision is unavailable.");
        WorkspacePlan plan = available.Revision.Plan;
        if (fallbackFramework is not null
            && !string.Equals(
                plan.TraversalTargetPolicy.TargetFramework,
                fallbackFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Hierarchy target does not match its Workspace.");
        }
        return plan;
    }

    private static string DescribeHierarchyFailure(
        PackageDependencyTraversalOutcome traversal,
        PackageDependencyTraversalFailure failure)
    {
        PackageSourceCoordinate coordinate =
            traversal.Nodes[failure.NodeIndex].Coordinate;
        string subject = $"{coordinate.PackageId} {coordinate.Version}";
        return failure.Detail switch
        {
            PackageDependencyTraversalManifestFailureDetail.Acquisition =>
                $"Could not acquire the dependency manifest for {subject}.",
            PackageDependencyTraversalManifestFailureDetail.IncompleteAcquisition =>
                $"Timed out before dependency traversal could finish for {subject}.",
            PackageDependencyTraversalManifestFailureDetail.Identity =>
                $"The dependency manifest for {subject} is invalid.",
            PackageDependencyTraversalManifestFailureDetail.Declaration =>
                $"The dependency declaration in {subject} is invalid.",
            PackageDependencyTraversalManifestFailureDetail
                .ManifestProjectionBudgetExhausted =>
                $"The dependency traversal budget was reached at {subject}.",
            _ => "The dependency traversal could not finish.",
        };
    }
}
