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
        string? traversalTargetFramework)
    {
        TraversalTargetFrameworkPolicy traversalTargetPolicy =
            traversalTargetFramework is null
                ? TraversalTargetFrameworkPolicy.ProductDefault
                : new TraversalTargetFrameworkPolicy(traversalTargetFramework);
        BrowserPackageHierarchy hierarchy =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                async deadline =>
                {
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
                                traversalTargetPolicy,
                                new PackageDependencyTraversalCandidateAdapter(
                                    BrowserPackageWorkspace
                                        .PackageSourceAuthorization),
                                new AuthorizedPackageDependencyManifestSource(),
                                new PackageDependencyTraversalWorkBudget(
                                    1_024, 16_384)),
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
                        traversalTargetPolicy.TargetFramework,
                        traversal.IsComplete,
                        traversal.Roots[0].Completion.ToString(),
                        rows,
                        [.. traversal.Failures.Select(failure =>
                            DescribeHierarchyFailure(traversal, failure))]);
                },
                BrowserPackageWorkspace.PackageOperationTimeout)
                .ConfigureAwait(false);
        return JsonSerializer.Serialize(
            hierarchy,
            BrowserPackageJsonContext.Default.BrowserPackageHierarchy);
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
