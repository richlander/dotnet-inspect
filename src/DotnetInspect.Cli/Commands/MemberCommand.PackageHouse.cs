using System.Diagnostics.CodeAnalysis;

using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Services;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using static DotnetInspect.Cli.Commands.WorkspaceTypeInspectionTarget;

namespace DotnetInspect.Cli.Commands;

public static partial class MemberCommand
{
    internal static bool TryCreateSharedDefaultMemberRequest(
        MemberOptions options,
        [NotNullWhen(true)] out ExactTypeInspectionRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(options.PackagePath)
            || File.Exists(options.PackagePath)
            || options.PackagePath.Contains("::", StringComparison.Ordinal)
            || options.PackageRangeAddress is not null
            || options.AssemblyPath is not null
            || options.PlatformAssembly is not null
            || options.PlatformFramework is not null
            || options.ProjectPath is not null
            || options.ProjectAssetsPath is not null
            || options.WorkspacePacket is not null
            || string.IsNullOrWhiteSpace(options.Tfm)
            || options.Tfm.Equals("all", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(options.TypeName)
            || options.TypeName.IndexOfAny(['*', '?']) >= 0
            || options.RouterDeferredTypeOrMember
            || options.MemberFilter.Count > 1
            || options.MemberSectionsPreResolved
            || options.ImplicitIncludeSections.Count > 0
            || options.IncludeSections is not null
            || options.Select is not null
            || options.SelectDefault
            || options.Discover is not null
            || options.HasExplicitOutputIntent
            || options.Verbosity is not (
                Verbosity.Quiet or Verbosity.Minimal)
            || options.VerbosityExplicitlySet
            || options.IncludeAll
            || options.DocsExplicitlySet
            || options.ShowSamples
            || options.PreferRenderedUrls
            || options.Explanation is not null
            || options.CompanionOutput != CompanionOutput.None
            || options.ShareFormat is not null
            || options.SourceParts
            || options.SourcePart is not null
            || options.CtorOnly
            || options.KindFilter.Count > 0
            || options.UnsafeOnly
            || options.MemberGenericArity.HasValue
            || options.Focus is not null
            || options.RequestAllTaste
            || options.RequestReadableLocalNames
            || options.SourceRepositories.Length > 0
            || options.DllPath is not null
            || options.PdbPath is not null
            || options.HasCallerScope
            || options.PerformanceTriage.HasFilters
            || options.BodyKindQuery.HasFilter
            || options.CloneCandidateQuery.HasPredicates)
        {
            return false;
        }

        (string packageId, string? version) =
            PackageExtractor.ParsePackageReference(options.PackagePath);
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        try
        {
            request = new ExactTypeInspectionRequest(
                packageId,
                version,
                options.Tfm,
                options.TypeName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static async Task<int?> ExecuteSharedDefaultMemberAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        ExactTypeInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var logger = new VerboseLogger(options.Verbose);
        await using var composition =
            new DesktopPackageSourceComposition(
                HttpClientFactory.Shared.Timeout);
        using var stores =
            new SearchPackageStores("inspect-member");
        PackageHouse house =
            composition.CreateDependencyContentQueryHouse(
                stores.GetLegacyCompatibleStore,
                options.SourceOptions,
                options.Verbose ? logger.Log : null);
        using PackageSourceOperationLease sourceOperation =
            composition.IssueSettlementOperation(cancellationToken);
        return await ExecuteSharedDefaultMemberAsync(
                options,
                plan,
                request,
                house,
                sourceOperation,
                cancellationToken)
            .ConfigureAwait(false);
    }

    static async Task<int?> ExecuteSharedDefaultMemberAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        ExactTypeInspectionRequest request,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExactTypeInspectionOperation
                .ExecuteWithLiveTargetAsync<int?>(
                    request,
                    house,
                    sourceOperation,
                    ApiSurfaceScope.Public,
                    async (envelope, target, _) =>
                    {
                        if (!envelope.Content.IsAvailable
                            || target is null)
                        {
                            if (envelope.Content.Outcome
                                    == ExactTypeInspectionOutcome.NotFound
                                && options.MemberFilter.Count == 0
                                && HasPotentialImpliedMember(
                                    options.TypeName!))
                            {
                                return null;
                            }

                            TypeCommand.WriteExactTypeNonSuccess(
                                envelope.Content,
                                envelope.Diagnostics);
                            return 1;
                        }

                        TypeCommand.WriteInspectionDiagnostics(
                            envelope.Diagnostics);
                        int exitCode =
                            await WorkspaceTypeInspectionTarget.ExecuteAsync(
                                    options,
                                    ExactTypeRenderSource.From(request) with
                                    {
                                        SelectedLibraryPath =
                                            target.PackageAssetPath,
                                    },
                                    target,
                                    (source, loaded) =>
                                        ExecuteWithShareAsync(
                                            options,
                                            plan,
                                            source,
                                            loaded))
                                .ConfigureAwait(false);
                        return exitCode == 0
                            && envelope.Content.IsComplete
                                ? 0
                                : 1;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            CommandError.Write(failure);
            return 1;
        }
    }
}
