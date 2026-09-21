using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Queries;

public static partial class PackageQuery
{
    private sealed record PackageQueryDependencyTraversalEvaluation(
        bool IsMatch,
        ImmutableArray<PackageQueryTermResult> TermResults,
        string? FailureMessage)
    {
        internal static PackageQueryDependencyTraversalEvaluation NoMatch { get; } =
            new(false, [], null);

        internal static PackageQueryDependencyTraversalEvaluation Failed(
            string message) =>
            new(false, [], message);

        internal static PackageQueryDependencyTraversalEvaluation Matched(
            ImmutableArray<PackageQueryTermResult> termResults) =>
            new(true, termResults, null);
    }

    private sealed record PackageQueryTransitiveDependencyMatch(
        int EdgeIndex,
        int Distance,
        string Path);

    private static async ValueTask<PackageQueryDependencyTraversalEvaluation>
        EvaluateDependencyTraversalAsync(
            PackageQueryPlan plan,
            PackageQueryPackage package,
            PackageQueryDependencyTraversalServices services,
            CancellationToken cancellationToken)
    {
        string requestedFramework =
            plan.DependencyTarget.RequestedTargetFramework
            ?? throw new InvalidOperationException(
                "Transitive dependency traversal requires an exact target framework.");
        var traversalTargetPolicy =
            new TraversalTargetFrameworkPolicy(requestedFramework);
        int maximumDepth = plan.DependencyDepth
            ?? throw new InvalidOperationException(
                "Transitive dependency traversal requires a maximum depth.");

        PackageDependencyEvidenceInput.Package input =
            PackageDependencyEvidenceQuery.CreatePackageInput(
                package.RequiredManifest,
                PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                requestedFramework,
                source: package.Source,
                allowCompatibleFallbackForRequestedTfm: true);
        PackageDependencyEvidenceOutcome evidence =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([input]));
        if (!evidence.FailedRoots.IsEmpty || evidence.Roots.Length != 1)
        {
            return PackageQueryDependencyTraversalEvaluation.Failed(
                "Dependency traversal could not project the package manifest.");
        }

        PackageDependencyEvidenceRoot root = evidence.Roots[0];
        if (root.Selection.Status
            == PackageDependencyEvidenceSelectionStatus.NoMatchingTargetFramework)
        {
            return PackageQueryDependencyTraversalEvaluation.NoMatch;
        }
        if (root.Declaration
                is not PackageDependencyEvidenceDeclarationResult.Available
                    available
            || !available.IsComplete)
        {
            return PackageQueryDependencyTraversalEvaluation.Failed(
                "Dependency traversal could not completely project the selected declarations.");
        }

        PackageDependencyTraversalOutcome traversal =
            await PackageDependencyTraversalQuery.ExecuteAsync(
                new PackageDependencyTraversalRequest(
                    [
                        new PackageDependencyTraversalRootOccurrence(
                            root,
                            PackageDependencyTraversalExpansionAuthority
                                .RecursiveSources),
                    ],
                    traversalTargetPolicy,
                    services.CandidateResolver,
                    services.ManifestAcquirer,
                    new PackageDependencyTraversalWorkBudget(
                        MaximumDependencyTraversalManifestProjections,
                        MaximumDependencyTraversalDeclarationResolutions),
                    maximumDepth),
                cancellationToken).ConfigureAwait(false);
        if (!traversal.IsSuccessful)
        {
            return PackageQueryDependencyTraversalEvaluation.Failed(
                "Dependency traversal could not completely evaluate the requested depth within its work bounds.");
        }

        var results = ImmutableArray.CreateBuilder<PackageQueryTermResult>();
        foreach (BoundPackageQueryTerm term in plan.BoundTerms
            .Where(term =>
                term.Predicate.Kind
                    == PackageQueryPredicateKind.DependsTransitive)
            .Reverse())
        {
            ImmutableArray<PackageQueryTransitiveDependencyMatch> matches =
                FindTransitiveDependencyMatches(
                    traversal,
                    term.Predicate.Text
                    ?? throw new InvalidOperationException(
                        "A transitive dependency term requires a package ID."));
            if (matches.IsEmpty)
                return PackageQueryDependencyTraversalEvaluation.NoMatch;

            PackageQueryEvidenceSummary summary = SummarizeItems(
                matches.Select(match => match.Path),
                StringComparer.Ordinal) with
            {
                Count = matches.Length,
            };
            results.Add(new PackageQueryTermResult(
                new PackageQueryAnswer(
                    term.Descriptor.Key,
                    new InertString(TextPolicy.Field, term.Predicate.Text))
                {
                    Term = term.Term,
                },
                new PackageQueryEvidence(term.Descriptor.Key)
                {
                    Scope = PackageQueryEvidenceScope.Package,
                    Summary = summary,
                    Properties =
                    [
                        Property("minimum-depth", "2"),
                        Property(
                            "maximum-depth",
                            maximumDepth.ToString(CultureInfo.InvariantCulture)),
                    ],
                    Term = term.Term,
                }));
        }

        BoundPackageQueryTerm depthTerm = plan.BoundTerms.Single(term =>
            term.Predicate.Kind == PackageQueryPredicateKind.DependencyDepth);
        results.Add(new PackageQueryTermResult(
            new PackageQueryAnswer(
                depthTerm.Descriptor.Key,
                new InertString(
                    TextPolicy.Field,
                    maximumDepth.ToString(CultureInfo.InvariantCulture)))
            {
                Term = depthTerm.Term,
            },
            new PackageQueryEvidence(depthTerm.Descriptor.Key)
            {
                Scope = PackageQueryEvidenceScope.Package,
                Number = maximumDepth,
                Term = depthTerm.Term,
            }));

        return PackageQueryDependencyTraversalEvaluation.Matched(
            results.ToImmutable());
    }

    private static ImmutableArray<PackageQueryTransitiveDependencyMatch>
        FindTransitiveDependencyMatches(
            PackageDependencyTraversalOutcome traversal,
            string requestedPackageId)
    {
        PackageDependencyTraversalReachability reachability =
            traversal.RootReachability[0];
        var matches =
            ImmutableArray.CreateBuilder<PackageQueryTransitiveDependencyMatch>();
        foreach ((int edgeIndex, int distance) in reachability.EdgeDistances)
        {
            if (distance < 2
                || traversal.Edges[edgeIndex].Target
                    is not PackageDependencyTraversalEdgeTarget.Node target
                || !traversal.Nodes[target.NodeIndex].Coordinate.PackageId.Equals(
                    requestedPackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matches.Add(new(
                edgeIndex,
                distance,
                DescribeTransitiveDependencyPath(
                    traversal,
                    reachability,
                    edgeIndex,
                    distance)));
        }

        return
        [
            .. matches
                .OrderBy(match => match.Distance)
                .ThenBy(match => match.Path, StringComparer.Ordinal)
                .ThenBy(match => match.EdgeIndex),
        ];
    }

    private static string DescribeTransitiveDependencyPath(
        PackageDependencyTraversalOutcome traversal,
        PackageDependencyTraversalReachability reachability,
        int terminalEdgeIndex,
        int terminalDistance)
    {
        var path = new List<int> { terminalEdgeIndex };
        int sourceProjectionIndex =
            traversal.Edges[terminalEdgeIndex].SourceProjectionIndex;
        for (int distance = terminalDistance - 1; distance > 0; distance--)
        {
            int predecessor = reachability.EdgeDistances
                .Where(pair => pair.Value == distance)
                .Select(pair => pair.Key)
                .Where(edgeIndex =>
                    traversal.Edges[edgeIndex].Target
                        is PackageDependencyTraversalEdgeTarget.Node target
                    && target.ProjectionIndex == sourceProjectionIndex)
                .Order()
                .First();
            path.Add(predecessor);
            sourceProjectionIndex =
                traversal.Edges[predecessor].SourceProjectionIndex;
        }
        path.Reverse();

        var text = new StringBuilder();
        AppendCoordinate(text, traversal.Edges[path[0]].SourceCoordinate);
        foreach (int edgeIndex in path)
        {
            PackageDependencyTraversalEdge edge = traversal.Edges[edgeIndex];
            var target =
                (PackageDependencyTraversalEdgeTarget.Node)edge.Target;
            text.Append(" --");
            text.Append(edge.Declaration.CanonicalVersionConstraint);
            text.Append("--> ");
            AppendCoordinate(text, traversal.Nodes[target.NodeIndex].Coordinate);
        }
        return text.ToString();
    }

    private static void AppendCoordinate(
        StringBuilder text,
        NuGetFetch.PackageSourceCoordinate coordinate)
    {
        text.Append(coordinate.PackageId);
        text.Append('@');
        text.Append(coordinate.Version);
    }
}
