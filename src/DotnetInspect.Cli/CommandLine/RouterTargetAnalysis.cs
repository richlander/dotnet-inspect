using System.Runtime.CompilerServices;
using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.CommandLine;

internal enum RouterTargetDomain
{
    Package,
    Platform,
}

internal sealed record RouterResolvedTarget(
    MetadataTypeDefinitionName Type,
    AssemblyReferenceIdentity Assembly,
    string Framework,
    MemberTargetSelector? Member,
    PlatformTypeCatalogRouteOutcome.Resolved? Route = null)
{
    internal string TypeName =>
        MetadataTypeNameFormatter.FormatGenericTypeName(
            Type.ToMetadataFullName());

    internal static RouterResolvedTarget From(
        PlatformTypeCatalogRouteOutcome.Resolved route) =>
        new(
            route.Candidate.Type,
            route.Candidate.Assembly.Identity,
            route.Target.Family switch
            {
                PlatformFamily.DotNetRuntime => $"runtime@{route.Target.Version}",
                PlatformFamily.AspNetCore => $"aspnetcore@{route.Target.Version}",
                _ => throw new InvalidOperationException("Unknown Platform family."),
            },
            route.Request.MemberSelector is { } selector
                ? MemberTargetSelector.Parse(selector)
                : null,
            route);

    internal static RouterResolvedTarget From(
        PlatformTypeLookupCandidate candidate,
        string framework,
        string? memberSelector) =>
        new(
            candidate.Type,
            candidate.Assembly.Identity,
            candidate.Assembly.Provenance is AssemblyResolutionProvenance.PlatformAsset
            { FrameworkVersion: { } version } platform
                ? $"{platform.Framework}@{version}"
                : framework,
            memberSelector is null
                ? null
                : MemberTargetSelector.Parse(memberSelector));
}

/// <summary>
/// The Router's completed whole-name Platform Library selection: one Library
/// in one selected family at one exact realized version. Unlike a
/// user-authored framework selector, the version never matches by prefix.
/// </summary>
internal sealed record RouterResolvedLibrary(
    string Name,
    PlatformFamily Family,
    string Version)
{
    internal string FrameworkName => Family switch
    {
        PlatformFamily.DotNetRuntime => "runtime",
        PlatformFamily.AspNetCore => "aspnetcore",
        _ => throw new InvalidOperationException("Unknown Platform family."),
    };
}

internal static class RouterTargetAnalysis
{
    private static readonly ConditionalWeakTable<ParseResult, RouterResolvedLibrary>
        ResolvedLibraries = new();

    internal static void BindLibrary(ParseResult parseResult, RouterResolvedLibrary library) =>
        ResolvedLibraries.Add(parseResult, library);

    internal static RouterResolvedLibrary? GetLibrary(ParseResult parseResult) =>
        ResolvedLibraries.TryGetValue(parseResult, out RouterResolvedLibrary? library)
            ? library
            : null;

    private static readonly ConditionalWeakTable<ParseResult, RouterResolvedTarget>
        ResolvedTargets = new();

    // Platform is the default only for a Platform-search-eligible claimant
    // that declares its own Platform population. An eligible claimant without
    // one (Microsoft.Extensions) defaults to Package, like an unclaimed name.
    internal static RouterTargetDomain Classify(string target) =>
        EcosystemPackCatalog.FindPlatformSearchPopulation(target) is not null
            ? RouterTargetDomain.Platform
            : RouterTargetDomain.Package;

    // The selected Platform family is the Ecosystem claimant's own declared
    // Platform population. Without one (an explicit --platform override of an
    // unclaimed or Package-default name), the Runtime default applies. Only
    // that one family is realized.
    internal static PlatformLibraryPopulationDeclaration SelectPlatformPopulation(
        string target) =>
        EcosystemPackCatalog.FindPlatformSearchPopulation(target)
            ?? new PlatformLibraryPopulationDeclaration(PlatformFamily.DotNetRuntime);

    // The framework family spelling of the selected Platform population, for
    // lookups that take a framework family rather than a population.
    internal static string SelectPlatformFrameworkFamily(string target) =>
        SelectPlatformPopulation(target).Family switch
        {
            PlatformFamily.DotNetRuntime => "runtime",
            PlatformFamily.AspNetCore => "aspnetcore",
            _ => throw new InvalidOperationException("Unknown Platform family."),
        };

    internal static void Bind(ParseResult parseResult, RouterResolvedTarget target) =>
        ResolvedTargets.Add(parseResult, target);

    internal static RouterResolvedTarget? Get(ParseResult parseResult) =>
        ResolvedTargets.TryGetValue(parseResult, out RouterResolvedTarget? target)
            ? target
            : null;

    internal static async ValueTask<(RouterResolvedTarget? Target, string? Error)>
        ResolveFrameworkAsync(
            string target,
            string framework,
            NuGetSourceOptions sourceOptions,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new CommandContext(verbose: false);
        var requests = PlatformPackService.GetMissingPackRequests([framework]);
        await foreach (var _ in PlatformPackService.EnsurePacksAsync(
            requests,
            context.HttpClient,
            context.Logger.Log,
            sourceOptions: sourceOptions).WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Only candidates whose identity satisfies the Router's exact-subject
        // rule count; a dotted suffix never selects a Type.
        var (lookup, accepted) = Lookup(target);
        string? member = null;
        if (lookup is not PlatformTypeLookupOutcome.Rejected && accepted.Length == 0)
        {
            foreach (TypeMemberSelectionCandidate candidate in
                ExactTypeInspectionOperation.GetTypeMemberCandidates(target))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = Lookup(candidate.TypeName);
                if (next.Lookup is not PlatformTypeLookupOutcome.Rejected
                    && next.Accepted.Length == 0)
                {
                    continue;
                }
                (lookup, accepted) = next;
                member = candidate.MemberSelector;
                break;
            }
        }

        if (lookup is PlatformTypeLookupOutcome.Rejected rejected)
            return (null, $"Platform type lookup in framework '{framework}' failed ({rejected.Failure.Kind}): {rejected.Failure.Detail}");
        return accepted.Length switch
        {
            1 => (RouterResolvedTarget.From(accepted[0], framework, member), null),
            0 => (null, $"Type or member '{target}' was not found in framework '{framework}'."),
            _ => (null, $"Type '{target}' matched {accepted.Length} types in framework '{framework}'."),
        };

        (PlatformTypeLookupOutcome Lookup, PlatformTypeLookupCandidate[] Accepted)
            Lookup(string pattern)
        {
            PlatformTypeLookupOutcome outcome =
                PlatformResolver.LookupTypeInFramework(pattern, framework);
            PlatformTypeLookupCandidate[] candidates = outcome switch
            {
                PlatformTypeLookupOutcome.Resolved resolved => [resolved.Candidate],
                PlatformTypeLookupOutcome.Ambiguous ambiguous => [.. ambiguous.Candidates],
                _ => [],
            };
            return (
                outcome,
                [
                    .. candidates.Where(candidate =>
                        PlatformTypeLocatorRouting.IsRouterIdentityMatch(
                            candidate.Type, pattern)),
                ]);
        }
    }

    internal static async ValueTask<(
        CliPlatformTypeLocatorOutcome Locator,
        InspectionEnvelope<PlatformTypeCatalogRouteOutcome>? Route)>
        ResolvePlatformAsync(
            string target,
            NuGetSourceOptions sourceOptions,
            CancellationToken cancellationToken)
    {
        var locator = await PlatformTypeLocatorRouting.LocateAsync(
            new CommandContext(verbose: false),
            sourceOptions,
            target,
            SelectPlatformPopulation(target),
            cancellationToken);
        return (
            locator,
            locator is CliPlatformTypeLocatorOutcome.Completed completed
                ? PlatformTypeLocatorRouting.ResolveType(
                    completed,
                    cancellationToken)
                : null);
    }

    internal static string? Failure(
        CliPlatformTypeLocatorOutcome locator,
        PlatformTypeCatalogRouteOutcome? route,
        string target)
    {
        if (locator is CliPlatformTypeLocatorOutcome.NotCompleted failure)
            return $"Platform type locator routing failed ({failure.Kind}).";

        return route switch
        {
            PlatformTypeCatalogRouteOutcome.Ambiguous ambiguous =>
                $"Type '{ambiguous.Request.TypePattern}' matched multiple platform types. The target-bound catalog contains {ambiguous.Candidates.Length} equally preferred candidates.",
            PlatformTypeCatalogRouteOutcome.Rejected rejected =>
                $"Platform type lookup failed ({rejected.Rejection}).",
            PlatformTypeCatalogRouteOutcome.Missing =>
                $"Type or member '{target}' was not found in the selected Platform domain.",
            _ => null,
        };
    }
}
