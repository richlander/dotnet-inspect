using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public static class PackagePairDirectUseClusterInspection
{
    public static async Task<InspectionEnvelope<
        PackagePairDirectUseClusterOperationOutcome>> ExecuteAsync(
        PackagePairDirectUseClusterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PackagePairDirectUseClusterOperationOutcome content =
            await PackagePairDirectUseClusterOperation.ExecuteAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "package-pair/direct-use-clusters",
                "Package-pair Direct Use Clusters do not yet have a "
                    + "canonical Workspace Share projection."),
            Diagnostics(content));
    }

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        PackagePairDirectUseClusterOperationOutcome content)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        switch (content)
        {
            case PackagePairDirectUseClusterOperationOutcome
                .WorkspaceNotCommitted notCommitted:
                diagnostics.Add(
                    new(
                        "package-pair.workspace-not-committed",
                        InspectionDiagnosticSeverity.Error,
                        notCommitted.Detail));
                break;
            case PackagePairDirectUseClusterOperationOutcome.Failed failed:
                diagnostics.Add(
                    new(
                        "package-pair.cleanup-failed",
                        InspectionDiagnosticSeverity.Error,
                        failed.Detail,
                        failed.Reason.ToString()));
                break;
            case PackagePairDirectUseClusterOperationOutcome
                .Completed completed:
                AddQueryDiagnostics(diagnostics, completed.Content);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Package-pair inspection outcome.");
        }
        return diagnostics.ToImmutable();
    }

    static void AddQueryDiagnostics(
        ImmutableArray<InspectionDiagnostic>.Builder diagnostics,
        PackagePairDirectUseClusterOutcome content)
    {
        switch (content)
        {
            case PackagePairDirectUseClusterOutcome.EndpointUnavailable
                unavailable:
                diagnostics.Add(
                    new(
                        "package-pair.endpoint-unavailable",
                        InspectionDiagnosticSeverity.Error,
                        "No implementation Libraries were admitted for: "
                            + string.Join(
                                ", ",
                                unavailable.Packages.Select(
                                    static package =>
                                        $"{package.PackageId}@{package.PackageVersion}"))));
                break;
            case PackagePairDirectUseClusterOutcome.PairPopulationRejected
                rejected:
                diagnostics.Add(
                    new(
                        "package-pair.population-rejected",
                        InspectionDiagnosticSeverity.Error,
                        $"The {rejected.FirstLibraryCount} x "
                            + $"{rejected.SecondLibraryCount} Library matrix "
                            + $"requires {rejected.RequiredLibraryPairs} pairs; "
                            + $"the limit is {rejected.MaximumLibraryPairs}."));
                break;
            case PackagePairDirectUseClusterOutcome.Available available:
                foreach (PackagePairLibraryPair pair
                    in available.Document.LibraryPairs)
                {
                    foreach (PackagePairLibraryFailure failure
                        in pair.Failures)
                    {
                        diagnostics.Add(
                            new(
                                "package-pair.library-pair-incomplete",
                                InspectionDiagnosticSeverity.Error,
                                failure.Message,
                                failure.Library?.AssemblyIdentity.Name));
                    }
                    if (pair.Diagnostics.IsIncomplete)
                    {
                        diagnostics.Add(
                            new(
                                "package-pair.correspondence-incomplete",
                                InspectionDiagnosticSeverity.Warning,
                                $"Library pair {pair.Ordinal}: "
                                    + $"{pair.Diagnostics.UnresolvedCandidateCallCount} "
                                    + "candidate calls could not be matched."));
                    }
                }
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Package-pair query outcome.");
        }
    }
}

public sealed record PackagePairDirectUseClusterRow(
    int Cluster,
    int LibraryPair,
    string SourcePackage,
    string SourceLibrary,
    Guid SourceModuleVersionId,
    int AnchorSourceMethodToken,
    string TargetPackage,
    string TargetLibrary,
    Guid TargetModuleVersionId,
    int AnchorTargetMethodToken,
    int SourceMembers,
    int ProviderTypes,
    int TargetMembers,
    int ExtensionMethods,
    int CallSites);

public sealed record PackagePairLibraryPairRow(
    int LibraryPair,
    string FirstPackage,
    string FirstLibrary,
    string SecondPackage,
    string SecondLibrary,
    int Clusters,
    int CallSites,
    bool Complete);

public sealed record PackagePairCallSiteRow(
    int LibraryPair,
    int Cluster,
    string SourcePackage,
    string SourceLibrary,
    Guid SourceModuleVersionId,
    Analysis.MethodIdentity SourceMethod,
    string TargetPackage,
    string TargetLibrary,
    Guid TargetModuleVersionId,
    Analysis.MethodIdentity TargetMethod,
    Analysis.DirectCall Call);

/// <summary>Shared complete-document row plans for Package-pair hosts.</summary>
public static class PackagePairDirectUseClusterRows
{
    public static ImmutableArray<PackagePairDirectUseClusterRow> Clusters(
        PackagePairDirectUseClusterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return
        [
            .. document.Clusters.Select(cluster =>
                new PackagePairDirectUseClusterRow(
                    cluster.Ordinal,
                    cluster.LibraryPairOrdinal,
                    Coordinate(cluster.Identity.Source.Package),
                    cluster.Identity.Source.AssemblyIdentity.Name,
                    cluster.Identity.Source.ModuleVersionId
                        ?? throw new InvalidOperationException(
                            "A Direct Use Cluster source requires an MVID."),
                    cluster.Identity.AnchorSourceMethodToken,
                    Coordinate(cluster.Identity.Target.Package),
                    cluster.Identity.Target.AssemblyIdentity.Name,
                    cluster.Identity.Target.ModuleVersionId
                        ?? throw new InvalidOperationException(
                            "A Direct Use Cluster target requires an MVID."),
                    cluster.Identity.AnchorTargetMethodToken,
                    cluster.SourceMethods.Length,
                    cluster.TargetTypes.Length,
                    cluster.TargetMethods.Length,
                    cluster.ExtensionMethodCount,
                    cluster.CallSiteCount)),
        ];
    }

    public static ImmutableArray<PackagePairLibraryPairRow> LibraryPairs(
        PackagePairDirectUseClusterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return
        [
            .. document.LibraryPairs.Select(pair =>
                new PackagePairLibraryPairRow(
                    pair.Ordinal,
                    Coordinate(pair.First.Package),
                    pair.First.AssemblyIdentity.Name,
                    Coordinate(pair.Second.Package),
                    pair.Second.AssemblyIdentity.Name,
                    pair.Clusters.Length,
                    pair.Occurrences.Length,
                    pair.IsComplete)),
        ];
    }

    public static ImmutableArray<PackagePairCallSiteRow> CallSites(
        PackagePairDirectUseClusterDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rows = ImmutableArray.CreateBuilder<PackagePairCallSiteRow>();
        foreach (PackagePairLibraryPair pair in document.LibraryPairs)
        {
            var clusterByOccurrence = new int[pair.Occurrences.Length];
            foreach (PackagePairDirectUseCluster cluster in pair.Clusters)
            {
                foreach (int occurrenceIndex
                    in cluster.OccurrenceIndexes)
                {
                    clusterByOccurrence[occurrenceIndex] =
                        cluster.Ordinal;
                }
            }
            for (int index = 0;
                index < pair.Occurrences.Length;
                index++)
            {
                PackagePairDirectCall call = pair.Occurrences[index];
                rows.Add(
                    new(
                        pair.Ordinal,
                        clusterByOccurrence[index],
                        Coordinate(call.Source.Package),
                        call.Source.AssemblyIdentity.Name,
                        call.Source.ModuleVersionId
                            ?? throw new InvalidOperationException(
                                "A direct call source requires an MVID."),
                        call.SourceMethod,
                        Coordinate(call.Target.Package),
                        call.Target.AssemblyIdentity.Name,
                        call.Target.ModuleVersionId
                            ?? throw new InvalidOperationException(
                                "A direct call target requires an MVID."),
                        call.TargetMethod,
                        call.Call));
            }
        }
        return rows.ToImmutable();
    }

    static string Coordinate(PackagePairPackageDescriptor package) =>
        $"{package.PackageId}@{package.PackageVersion}";
}
