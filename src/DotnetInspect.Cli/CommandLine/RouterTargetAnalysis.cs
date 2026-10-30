using System.Runtime.CompilerServices;
using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Sections;
using DotnetInspector.Services;
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

internal static class RouterTargetAnalysis
{
    private static readonly ConditionalWeakTable<ParseResult, RouterResolvedTarget>
        ResolvedTargets = new();

    internal static RouterTargetDomain Classify(string target) =>
        EcosystemPackCatalog.ShouldSearchPlatform(target)
            ? RouterTargetDomain.Platform
            : RouterTargetDomain.Package;

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

        PlatformTypeLookupOutcome lookup =
            PlatformResolver.LookupTypeInFramework(target, framework);
        string? member = null;
        if (lookup is PlatformTypeLookupOutcome.Missing)
        {
            foreach (ImpliedMemberTypeCandidate candidate in
                ApiTypeLookupService.GetImpliedMemberTypeCandidates(target))
            {
                cancellationToken.ThrowIfCancellationRequested();
                lookup = PlatformResolver.LookupTypeInFramework(
                    candidate.TypeName, framework);
                if (lookup is PlatformTypeLookupOutcome.Missing)
                    continue;
                member = candidate.MemberSelector;
                break;
            }
        }

        return lookup switch
        {
            PlatformTypeLookupOutcome.Resolved resolved =>
                (RouterResolvedTarget.From(resolved.Candidate, framework, member), null),
            PlatformTypeLookupOutcome.Ambiguous ambiguous =>
                (null, $"Type '{target}' matched {ambiguous.Candidates.Length} types in framework '{framework}'."),
            PlatformTypeLookupOutcome.Rejected rejected =>
                (null, $"Platform type lookup in framework '{framework}' failed ({rejected.Failure.Kind}): {rejected.Failure.Detail}"),
            _ => (null, $"Type or member '{target}' was not found in framework '{framework}'."),
        };
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
