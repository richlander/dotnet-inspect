using System.Diagnostics.CodeAnalysis;

using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using static DotnetInspect.Cli.Commands.WorkspaceTypeInspectionTarget;

namespace DotnetInspect.Cli.Commands;

public static partial class MemberCommand
{
    internal enum SharedMemberPackageDemand
    {
        Surface,
        DefiningImplementation,
    }

    static readonly HashSet<string> DefiningImplementationSections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            SectionNames.IL,
            SectionNames.CustomAttributes,
            SectionNames.DecompiledSource,
            SectionNames.ExceptionRegions,
        };

    internal static bool TryCreateSharedMemberRequest(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        [NotNullWhen(true)] out ExactTypeInspectionRequest? request,
        out SharedMemberPackageDemand demand)
    {
        request = null;
        demand = default;
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
            || options.HasFocus
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

        bool surfaceOnly =
            options.IncludeSections is null
            && options.Select is null
            && !options.SelectDefault;
        bool definingImplementation =
            !options.SelectDefault
            && (options.IncludeSections is { Count: > 0 }
                || options.Select is { Length: > 0 })
            && plan.Selection.UnresolvedSelectors.IsEmpty
            && !plan.Selection.ResolvedSections.IsEmpty
            && plan.Selection.ResolvedSections.All(
                DefiningImplementationSections.Contains)
            && (options.MemberFilter.Count == 1
                || HasPotentialImpliedMember(options.TypeName));
        if (!surfaceOnly && !definingImplementation)
            return false;

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
            demand = definingImplementation
                ? SharedMemberPackageDemand.DefiningImplementation
                : SharedMemberPackageDemand.Surface;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static async Task<int?> ExecuteSharedMemberAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        ExactTypeInspectionRequest request,
        SharedMemberPackageDemand demand,
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
        return await ExecuteSharedMemberAsync(
                options,
                plan,
                request,
                demand,
                house,
                composition.IssueSettlementOperation,
                cancellationToken)
            .ConfigureAwait(false);
    }

    static async Task<int?> ExecuteSharedMemberAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        ExactTypeInspectionRequest request,
        SharedMemberPackageDemand demand,
        PackageHouse house,
        Func<CancellationToken, PackageSourceOperationLease>
            issueSourceOperation,
        CancellationToken cancellationToken)
    {
        try
        {
            var requests =
                new List<ExactTypeInspectionRequest> { request };
            var impliedMembers =
                new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ImpliedMemberTypeCandidate candidate
                in ApiTypeLookupService.GetImpliedMemberTypeCandidates(
                    options.TypeName!))
            {
                var candidateRequest = new ExactTypeInspectionRequest(
                    request.PackageId,
                    request.Version,
                    request.TargetFramework,
                    candidate.TypeName);
                requests.Add(candidateRequest);
                impliedMembers[candidateRequest.Type] =
                    candidate.MemberSelector;
            }

            async ValueTask<int?> UseTargetAsync(
                InspectionEnvelope<ExactTypeInspectionResult> envelope,
                SelectedContextExactTypeLiveTarget? target,
                CancellationToken _)
            {
                if (!envelope.Content.IsAvailable
                    || target is null)
                {
                    TypeCommand.WriteExactTypeNonSuccess(
                        envelope.Content,
                        envelope.Diagnostics);
                    return 1;
                }

                if (demand
                        == SharedMemberPackageDemand.DefiningImplementation
                    && options.MemberFilter.Count == 0
                    && !impliedMembers.ContainsKey(
                        envelope.Content.RequestedType))
                {
                    return null;
                }

                MemberOptions effectiveOptions = options;
                ResolvedMemberInspectionPlan effectivePlan = plan;
                if (impliedMembers.TryGetValue(
                    envelope.Content.RequestedType,
                    out string? impliedMember))
                {
                    if (!TryMergeImpliedMemberOptions(
                        options,
                        impliedMember,
                        out effectiveOptions))
                        return 1;
                    effectivePlan =
                        ResolvedMemberInspectionPlan
                            .FromCompatibilityOptions(
                                effectiveOptions);
                    var (preamble, error) =
                        ApiCommand.RunPreamble(
                            effectiveOptions,
                            effectivePlan);
                    if (error.HasValue)
                        return error.Value;
                    effectiveOptions =
                        (MemberOptions)preamble.Options;
                }

                effectiveOptions =
                    effectiveOptions with { ShowDocs = false };
                ExactTypeInspectionRequest selectedRequest =
                    requests.First(candidate =>
                        candidate.Type.Equals(
                            envelope.Content.RequestedType,
                            StringComparison.Ordinal));
                TypeCommand.WriteInspectionDiagnostics(
                    envelope.Diagnostics);
                int exitCode =
                    await WorkspaceTypeInspectionTarget.ExecuteAsync(
                            effectiveOptions,
                            ExactTypeRenderSource.From(
                                selectedRequest) with
                            {
                                SelectedLibraryPath =
                                    target.PackageAssetPath,
                            },
                            target,
                            (source, loaded) =>
                                ExecuteWithShareAsync(
                                    effectiveOptions,
                                    effectivePlan,
                                    source,
                                    loaded,
                                    allowImplicitDocumentation:
                                        false),
                            requiresLocalAssemblyPath:
                                demand
                                != SharedMemberPackageDemand
                                    .DefiningImplementation)
                        .ConfigureAwait(false);
                return exitCode == 0
                    && envelope.Content.IsComplete
                        ? 0
                        : 1;
            }

            return await (demand
                == SharedMemberPackageDemand.DefiningImplementation
                ? ExactTypeInspectionOperation
                    .ExecuteFirstAvailableWithDefiningImplementationTargetAsync<
                        int?>(
                        requests,
                        house,
                        issueSourceOperation,
                        ApiSurfaceScope.Public,
                        UseTargetAsync,
                        cancellationToken)
                : ExactTypeInspectionOperation
                    .ExecuteFirstAvailableWithLiveTargetAsync<int?>(
                        requests,
                        house,
                        issueSourceOperation(cancellationToken),
                        ApiSurfaceScope.Public,
                        UseTargetAsync,
                        cancellationToken)
            ).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            CommandError.Write(failure);
            return 1;
        }
    }
}
