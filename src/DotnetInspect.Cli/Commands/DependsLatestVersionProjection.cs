using System.Collections.Immutable;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

public partial class DependsCommand
{
    private static async Task<DependsLatestVersionProjectionResult>
        AcquireLatestVersionProjectionAsync(
            DependsOptions options,
            DependsAssetRequestPlan plan,
            PackageDependencyEvidenceOutcome evidence,
            IReadOnlyList<int> admittedIndexes,
            DesktopPackageSourceComposition composition,
            CommandContext context,
            CancellationToken cancellationToken)
    {
        if (!plan.LatestVersions)
        {
            return new DependsLatestVersionProjectionResult(
                [],
                DependencyInspectionLatestVersionSummary.NotRequested,
                []);
        }

        var declarations = new List<PendingLatestVersionDeclaration>();
        for (int inputIndex = 0;
             inputIndex < evidence.Roots.Length;
             inputIndex++)
        {
            PackageDependencyEvidenceRoot root = evidence.Roots[inputIndex];
            if (root.Declaration
                is not PackageDependencyEvidenceDeclarationResult.Available
                    available)
            {
                continue;
            }

            bool hasSelectedGroup =
                root.Selection.Status
                    == PackageDependencyEvidenceSelectionStatus.Selected
                && root.Selection.SelectedGroup is not null;
            IEnumerable<PackageDependencyEvidenceGroup> groups =
                hasSelectedGroup
                    ? available.Groups.Where(group =>
                        group.Identity == root.Selection.SelectedGroup)
                    : available.Groups;
            foreach (PackageDependencyEvidenceDeclaration declaration
                     in groups.SelectMany(static group => group.Declarations))
            {
                (NuGetVersion? current,
                    DependencyInspectionCurrentVersionBasis? basis) =
                    FindAssociatedResolution(root, declaration)
                        is { } resolved
                        ? (NuGetVersion.Parse(resolved.Version),
                            DependencyInspectionCurrentVersionBasis.Resolved)
                        : PackageVersionComparison.InclusiveMinimum(
                                declaration.CanonicalVersionConstraint)
                            is { } minimum
                            ? (minimum,
                                DependencyInspectionCurrentVersionBasis
                                    .LowerBound)
                            : ((NuGetVersion?)null,
                                (DependencyInspectionCurrentVersionBasis?)null);
                declarations.Add(
                    new PendingLatestVersionDeclaration(
                        admittedIndexes[inputIndex],
                        root,
                        declaration,
                        current,
                        basis));
            }
        }

        // One discovery per distinct package identity and prerelease policy.
        var discoveries =
            new Dictionary<(string PackageId, bool IncludePrerelease),
                LatestVersionDiscovery>();
        foreach (PendingLatestVersionDeclaration declaration in declarations)
        {
            if (declaration.Current is not { } current)
                continue;

            var key = (
                declaration.Declaration.CanonicalPackageId
                    .ToLowerInvariant(),
                current.IsPrerelease);
            if (!discoveries.ContainsKey(key))
            {
                discoveries[key] = await DiscoverLatestAsync(
                    key.Item1,
                    key.Item2,
                    options,
                    composition,
                    context,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        // One House compatibility question per distinct Latest coordinate.
        string compatibilityTarget =
            (options.Tfm is { } framework
                ? CreateTraversalTargetPolicy(framework)
                : TraversalTargetFrameworkPolicy.ProductDefault)
            .TargetFramework;
        var compatibility =
            new Dictionary<PackageSourceCoordinate, LatestCompatibilityAnswer>();
        using var stores = new SearchPackageStores("inspect-depends");
        foreach (LatestVersionDiscovery discovery in discoveries.Values)
        {
            if (discovery.Coordinate is not { } latest
                || compatibility.ContainsKey(latest))
            {
                continue;
            }

            compatibility[latest] = await EvaluateCompatibilityAsync(
                latest,
                compatibilityTarget,
                options,
                composition,
                stores,
                context,
                cancellationToken).ConfigureAwait(false);
        }

        ImmutableArray<DependencyInspectionLatestVersion> rows =
        [
            .. declarations.Select(declaration =>
                CreateLatestVersionRow(
                    declaration,
                    compatibilityTarget,
                    discoveries,
                    compatibility)),
        ];
        int failed = rows.Count(static row =>
            row.State
                == DependencyInspectionLatestVersionState.DiscoveryFailed);
        int compatibilityUnavailable = rows.Count(static row =>
            row.LatestCompatible
                == DependencyInspectionLatestCompatibility.Unavailable);
        var summary = new DependencyInspectionLatestVersionSummary(
            failed > 0 || compatibilityUnavailable > 0
                ? DependencyInspectionLatestVersionCompletion.Partial
                : DependencyInspectionLatestVersionCompletion.Complete,
            rows.Length,
            discoveries.Count,
            rows.Count(static row =>
                row.State == DependencyInspectionLatestVersionState.Compared),
            rows.Count(static row =>
                row.Behind is { } behind
                && behind != PackageVersionLag.None),
            rows.Count(static row =>
                row.State
                    == DependencyInspectionLatestVersionState
                        .NoCurrentVersion),
            rows.Count(static row =>
                row.State
                    == DependencyInspectionLatestVersionState
                        .NoListedVersion),
            failed,
            rows.Count(static row =>
                row.LatestCompatible
                    == DependencyInspectionLatestCompatibility.No),
            compatibilityUnavailable);
        ImmutableArray<DependencyInspectionFailure> failures =
        [
            .. discoveries
                .Where(static pair => pair.Value.Coordinate is null
                    && !pair.Value.NoListedVersion)
                .Select(static pair =>
                    new DependencyInspectionFailure.Evidence(
                        new DependencyEvidenceFailureRow(
                            DependencyEvidenceFailurePhase.LatestVersion,
                            pair.Value.FailureKind!,
                            SourceKind: null,
                            RootIndex: null,
                            RootIdentity: null,
                            Group: null,
                            GroupIndex: null,
                            Source: null,
                            Subject: null,
                            pair.Key.PackageId,
                            PackageVersion: null,
                            SourceLabel: null,
                            pair.Value.FailureMessage!.Value,
                            Occurrences: 1))),
            .. compatibility
                .Where(static pair => pair.Value.FailureKind is not null)
                .Select(static pair =>
                    new DependencyInspectionFailure.Evidence(
                        new DependencyEvidenceFailureRow(
                            DependencyEvidenceFailurePhase.LatestVersion,
                            pair.Value.FailureKind!,
                            SourceKind: null,
                            RootIndex: null,
                            RootIdentity: null,
                            Group: null,
                            GroupIndex: null,
                            Source: null,
                            Subject: null,
                            pair.Key.PackageId,
                            pair.Key.Version,
                            SourceLabel: null,
                            pair.Value.FailureMessage!.Value,
                            Occurrences: 1))),
        ];
        return new DependsLatestVersionProjectionResult(
            rows,
            summary,
            failures);
    }

    private static PackageSourceCoordinate? FindAssociatedResolution(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration)
    {
        if (root.Relationships
            is not PackageDependencyEvidenceRelationshipResult.Available
                relationships)
        {
            return null;
        }

        PackageSourceCoordinate[] resolved =
        [
            .. relationships.Relationships
                .Where(relationship =>
                    relationship.DeclarationAssociation
                        == declaration.Identity
                    && relationship.ResolvedCoordinate.PackageId.Equals(
                        declaration.CanonicalPackageId,
                        StringComparison.OrdinalIgnoreCase))
                .Select(static relationship =>
                    relationship.ResolvedCoordinate)
                .Distinct(),
        ];
        return resolved.Length == 1 ? resolved[0] : null;
    }

    private static async Task<LatestVersionDiscovery> DiscoverLatestAsync(
        string packageId,
        bool includePrerelease,
        DependsOptions options,
        DesktopPackageSourceComposition composition,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        PackageHouse house = composition.CreateSettlementHouse(
            packageId,
            options.SourceOptions,
            context.Logger.Log);
        using PackageSourceOperationLease lease =
            composition.IssueSettlementOperation(cancellationToken);
        var operation = PackageHouseOperation.Create(
            PackageHouseOperationProfile.Settle,
            lease.RequestTimeout,
            lease.OperationTimeout);
        PackageHouseSettlement settlement = await house.ExecuteAsync(
            new PackageHouseRequest(
                new PackageHouseDemand.Selecting(
                    new PackageVersionSelectionRequest.AlwaysLatest(
                        packageId,
                        includePrerelease)),
                operation),
            lease).ConfigureAwait(false);
        PackageHouseResult result = settlement.Result;
        switch (result.Decision?.VersionResolution)
        {
            case PackageVersionResolutionReceipt.Resolved resolved:
                return new LatestVersionDiscovery(
                    resolved.Coordinate,
                    PackageVersionSelectionResolver.AdmittedPopulation(
                        (PackageVersionSelectionRequest.AlwaysLatest)
                            resolved.Request,
                        resolved.Discovery),
                    NoListedVersion: false,
                    FailureKind: null,
                    FailureMessage: null);

            case PackageVersionResolutionReceipt.NoMatch:
                return new LatestVersionDiscovery(
                    Coordinate: null,
                    Population: null,
                    NoListedVersion: true,
                    FailureKind: null,
                    FailureMessage: null);

            case { } receipt:
                return Failed(
                    receipt.GetType().Name,
                    ResultReason(result));

            default:
                // A House operation timeout may stop before issuing a receipt.
                return Failed(
                    result.Evidence.HasOperationTimeout
                        ? "OperationTimedOut"
                        : result.GetType().Name,
                    ResultReason(result));
        }

        static LatestVersionDiscovery Failed(string kind, InertString reason) =>
            new(
                Coordinate: null,
                Population: null,
                NoListedVersion: false,
                kind,
                reason);
    }

    private static async Task<LatestCompatibilityAnswer>
        EvaluateCompatibilityAsync(
            PackageSourceCoordinate latest,
            string targetFramework,
            DependsOptions options,
            DesktopPackageSourceComposition composition,
            SearchPackageStores stores,
            CommandContext context,
            CancellationToken cancellationToken)
    {
        PackageHouseTargetContext target;
        try
        {
            target = PackageHouseTargetContext.Exact(targetFramework);
        }
        catch (ArgumentException exception)
        {
            return LatestCompatibilityAnswer.Failed(
                "InvalidTarget",
                new InertString(TextPolicy.Prose, exception.Message));
        }

        PackageHouseSettlement settlement =
            await composition.AcquireContentAsync(
                latest,
                PackageHouseContentQuery.TfmFileList(target),
                stores.GetStore,
                options.SourceOptions,
                context.Logger.Log,
                cancellationToken).ConfigureAwait(false);
        PackageHouseResult result = settlement.Result;
        // The House's compile selection decides; this only labels it.
        return result switch
        {
            PackageHouseResult.Settled =>
                new(DependencyInspectionLatestCompatibility.Yes, null, null),
            PackageHouseResult.NoMatch
            {
                Evidence.ContentNarrowing.TargetSelection.Selection.Status:
                    PackageCompileAssetSelectionStatus
                        .NoMatchingTargetFramework,
            } => new(DependencyInspectionLatestCompatibility.No, null, null),
            PackageHouseResult.NoMatch
            {
                Evidence.ContentNarrowing.TargetSelection.Selection.Status:
                    PackageCompileAssetSelectionStatus.NoCompileAssets,
            } => new(
                DependencyInspectionLatestCompatibility.NoAssets,
                null,
                null),
            _ => LatestCompatibilityAnswer.Failed(
                result.GetType().Name,
                ResultReason(result)),
        };
    }

    private static InertString ResultReason(PackageHouseResult result) =>
        result switch
        {
            PackageHouseResult.NotFound value => value.Reason,
            PackageHouseResult.NoMatch value => value.Reason,
            PackageHouseResult.Ambiguous value => value.Reason,
            PackageHouseResult.Rejected value => value.Reason,
            PackageHouseResult.Unavailable value => value.Reason,
            PackageHouseResult.Incomplete value => value.Reason,
            PackageHouseResult.Failed value => value.Reason,
            _ => new InertString(
                TextPolicy.Prose,
                "Version discovery did not issue a resolution receipt."),
        };

    private static DependencyInspectionLatestVersion CreateLatestVersionRow(
        PendingLatestVersionDeclaration item,
        string compatibilityTarget,
        IReadOnlyDictionary<(string PackageId, bool IncludePrerelease),
            LatestVersionDiscovery> discoveries,
        IReadOnlyDictionary<PackageSourceCoordinate, LatestCompatibilityAnswer>
            compatibility)
    {
        PackageDependencyEvidenceDeclaration declaration = item.Declaration;
        DependencyInspectionLatestVersion Row(
            DependencyInspectionLatestVersionState state,
            string? latestInMajor = null,
            string? latest = null,
            PackageVersionLag? behind = null,
            int? majorsBehind = null,
            DependencyInspectionLatestCompatibility? latestCompatible = null,
            string? failureKind = null,
            InertString? failureMessage = null) =>
            new(
                item.RootOccurrence,
                item.Root.Identity,
                item.Root.Display,
                declaration.Identity,
                declaration.CanonicalPackageId,
                declaration.CanonicalVersionConstraint,
                item.Current?.ToNormalizedString(),
                item.Basis,
                latestInMajor,
                latest,
                behind,
                majorsBehind,
                compatibilityTarget,
                latestCompatible,
                state,
                failureKind,
                failureMessage);

        if (item.Current is not { } current)
            return Row(DependencyInspectionLatestVersionState.NoCurrentVersion);

        LatestVersionDiscovery discovery = discoveries[(
            declaration.CanonicalPackageId.ToLowerInvariant(),
            current.IsPrerelease)];
        if (discovery.NoListedVersion)
            return Row(DependencyInspectionLatestVersionState.NoListedVersion);

        if (discovery is not
            {
                Coordinate: { } latestCoordinate,
                Population: { } population,
            })
        {
            return Row(
                DependencyInspectionLatestVersionState.DiscoveryFailed,
                failureKind: discovery.FailureKind,
                failureMessage: discovery.FailureMessage);
        }

        NuGetVersion latestVersion = NuGetVersion.Parse(latestCoordinate.Version);
        PackageVersionComparison comparison =
            PackageVersionComparison.Create(current, latestVersion, population);
        return Row(
            DependencyInspectionLatestVersionState.Compared,
            comparison.LatestInMajor,
            latestVersion.ToNormalizedString(),
            comparison.Behind,
            comparison.MajorsBehind,
            compatibility[latestCoordinate].Compatibility);
    }

    private sealed record PendingLatestVersionDeclaration(
        int RootOccurrence,
        PackageDependencyEvidenceRoot Root,
        PackageDependencyEvidenceDeclaration Declaration,
        NuGetVersion? Current,
        DependencyInspectionCurrentVersionBasis? Basis);

    private sealed record LatestVersionDiscovery(
        PackageSourceCoordinate? Coordinate,
        PackageVersionVector? Population,
        bool NoListedVersion,
        string? FailureKind,
        InertString? FailureMessage);

    private sealed record LatestCompatibilityAnswer(
        DependencyInspectionLatestCompatibility Compatibility,
        string? FailureKind,
        InertString? FailureMessage)
    {
        internal static LatestCompatibilityAnswer Failed(
            string kind,
            InertString reason) =>
            new(DependencyInspectionLatestCompatibility.Unavailable, kind, reason);
    }

    private sealed record DependsLatestVersionProjectionResult(
        ImmutableArray<DependencyInspectionLatestVersion> Rows,
        DependencyInspectionLatestVersionSummary Summary,
        ImmutableArray<DependencyInspectionFailure> Failures);
}
