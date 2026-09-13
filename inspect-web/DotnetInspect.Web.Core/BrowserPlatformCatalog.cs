using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGet.Frameworks;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Web;

internal sealed record BrowserPlatformCatalogResult(
    string Tfm,
    string Version,
    BrowserPlatformCatalogRow[] Rows);

internal sealed record BrowserPlatformCatalogRow(
    string Tfm,
    string Pack,
    string Assembly,
    string File,
    string Kind,
    string? ForwardsTo,
    string Version,
    int PublicTypes,
    bool InReferencePack,
    bool HasImplementation,
    string PackVersion);

[SupportedOSPlatform("browser")]
internal static class BrowserPlatformCatalog
{
    static readonly Family[] Families =
    [
        new("netcore.app", "Microsoft.NETCore.App.Ref",
            "Microsoft.NETCore.App.Runtime.linux-x64"),
        new("aspnetcore.app", "Microsoft.AspNetCore.App.Ref",
            "Microsoft.AspNetCore.App.Runtime.linux-x64"),
    ];

    internal static Task<string[]> GetVersionsAsync(
        string targetFramework,
        CancellationToken cancellationToken = default) =>
        GetVersionsAsync(targetFramework, BrowserPackageWorkspace.Gallery,
            BrowserPackageWorkspace.PackageOperationTimeout, cancellationToken);

    internal static Task<string[]> GetVersionsAsync(
        string targetFramework,
        IPackageSourceClient source,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        NuGetFramework framework = Framework(targetFramework);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            async deadline =>
            {
                HashSet<NuGetVersion>? common = null;
                foreach (string package in Families.SelectMany(
                    family => new[] { family.ReferencePackage, family.RuntimePackage }))
                {
                    string[] candidates = await BrowserPackageWorkspace.GetVersionsAsync(
                        package, source, deadline.Remaining, deadline.Token).ConfigureAwait(false);
                    var versions = new HashSet<NuGetVersion>(
                        candidates.Select(NuGetVersion.Parse).Where(version =>
                            version.Major == framework.Version.Major
                            && version.Minor == framework.Version.Minor),
                        VersionComparer.VersionRelease);
                    if (common is null)
                        common = versions;
                    else
                        common.IntersectWith(versions);
                }

                if (common is null || common.Count == 0)
                    throw new InvalidOperationException(
                        $"No common reference/runtime pack version is available for {targetFramework}.");
                return common.OrderByDescending(version => version, VersionComparer.VersionRelease)
                    .Select(version => version.ToNormalizedString()).ToArray();
            },
            timeout,
            cancellationToken);
    }

    internal static Task PrefetchAsync(
        string targetFramework,
        string platformVersion,
        CancellationToken cancellationToken = default) =>
        PrefetchAsync(targetFramework, platformVersion, BrowserPackageWorkspace.Gallery,
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);

    internal static Task PrefetchAsync(
        string targetFramework,
        string platformVersion,
        IPackageSourceClient source,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        string version = Version(Framework(targetFramework), platformVersion);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            async deadline =>
            {
                using var leases = new BrowserPackageWorkspace.PackageLeaseSet();
                foreach (Family family in Families)
                {
                    BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(
                        family.RuntimePackage, version, source,
                        deadline.Remaining, deadline.Token,
                        epochWork: null).ConfigureAwait(false);
                    leases.Lease(package.CacheKey);
                }
                return true;
            },
            timeout,
            cancellationToken);
    }

    internal static Task<BrowserPackage> AcquireRuntimeAsync(
        string targetFramework,
        string family,
        string version,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        AcquireRuntimeAsync(targetFramework, family, version,
            BrowserPackageWorkspace.Gallery,
            timeout, cancellationToken);

    internal static Task<BrowserPackage> AcquireRuntimeAsync(
        string targetFramework,
        string family,
        string version,
        IPackageSourceClient source,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        NuGetFramework framework = NuGetFramework.ParseFolder(targetFramework);
        if (framework.IsUnsupported
            || !framework.Framework.Equals(
                FrameworkConstants.FrameworkIdentifiers.NetCoreApp, StringComparison.Ordinal))
            throw new ArgumentException("The platform target framework is unsupported.", nameof(targetFramework));
        version = Version(framework, version);
        string package = family switch
        {
            "runtime" => Families[0].RuntimePackage,
            "aspnetcore" => Families[1].RuntimePackage,
            _ => throw new ArgumentException("Unknown platform family.", nameof(family)),
        };
        return BrowserPackageWorkspace.AcquireAsync(package, version,
            source,
            timeout, cancellationToken,
            epochWork: null);
    }

    internal static Task<BrowserPlatformCatalogResult> GetCatalogAsync(
        string targetFramework,
        string platformVersion,
        CancellationToken cancellationToken = default) =>
        GetCatalogAsync(targetFramework, platformVersion, BrowserPackageWorkspace.Gallery,
            BrowserPackageWorkspace.PackageOperationTimeout,
            cancellationToken);

    internal static Task<BrowserPlatformCatalogResult> GetCatalogAsync(
        string targetFramework,
        string platformVersion,
        IPackageSourceClient source,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        NuGetFramework framework = Framework(targetFramework);
        string tfm = framework.GetShortFolderName();
        string version = Version(framework, platformVersion);
        return BrowserPackageWorkspace.RunPackageOperationAsync(
            async deadline =>
            {
                await using ScopeReservation reservation =
                    await BrowserPackageWorkspace.ReserveScopeAsync(deadline.Token).ConfigureAwait(false);
                var rows = new List<BrowserPlatformCatalogRow>();
                foreach (Family family in Families)
                {
                    Dictionary<string, Library> reference = await ReadPackAsync(
                        family.ReferencePackage, runtime: false).ConfigureAwait(false);
                    Dictionary<string, Library> runtime = await ReadPackAsync(
                        family.RuntimePackage, runtime: true).ConfigureAwait(false);
                    foreach (string name in reference.Keys.Union(runtime.Keys, StringComparer.OrdinalIgnoreCase))
                    {
                        reference.TryGetValue(name, out Library? contract);
                        runtime.TryGetValue(name, out Library? implementation);
                        Library physical = implementation ?? contract
                            ?? throw new InvalidOperationException("The catalog library has no metadata.");
                        bool facade = implementation?.Metadata.Classification.Kind == AssemblySurfaceKind.Facade;
                        string? forwardsTo = facade
                            ? physical.Metadata.Forwarders
                                .GroupBy(forwarder => forwarder.TargetAssembly, StringComparer.Ordinal)
                                .OrderByDescending(group => group.Count())
                                .ThenBy(group => group.Key, StringComparer.Ordinal)
                                .Select(group => group.Key).FirstOrDefault()
                            : null;
                        rows.Add(new(tfm, family.Pack, physical.Name, physical.File,
                            facade ? "facade" : implementation is null ? "ref" : "impl",
                            forwardsTo, physical.Version,
                            (contract ?? physical).Metadata.Classification.MeaningfulPublicTypeCount,
                            contract is not null, implementation is not null, version));
                    }
                }

                return new BrowserPlatformCatalogResult(tfm, version,
                    [.. rows.OrderBy(row => row.Pack, StringComparer.Ordinal)
                        .ThenBy(row => row.Assembly, StringComparer.OrdinalIgnoreCase)]);

                async Task<Dictionary<string, Library>> ReadPackAsync(string packageId, bool runtime)
                {
                    BrowserPackage package = await BrowserPackageWorkspace.AcquireAsync(
                        packageId, version, source,
                        deadline.Remaining, deadline.Token,
                        epochWork: null).ConfigureAwait(false);
                    using var leases = new BrowserPackageWorkspace.PackageLeaseSet();
                    leases.Lease(package.CacheKey);
                    string[] entries = runtime
                        ? RuntimeEntries(package, tfm)
                        : [.. package.Content.EnumerateEntries()
                            .Where(path => path.StartsWith($"ref/{tfm}/", StringComparison.OrdinalIgnoreCase)
                                && !path[$"ref/{tfm}/".Length..].Contains('/')
                                && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            .Order(StringComparer.Ordinal)];
                    BrowserPlatformWorkspace.EnsureAssemblyCapacity(entries.Length);
                    PackageInspectionInput input = package.CreateInspectionInput();
                    var libraries = new Dictionary<string, Library>(StringComparer.OrdinalIgnoreCase);
                    foreach (string path in entries)
                    {
                        deadline.ThrowIfExpired();
                        // One image at a time keeps catalog work within the same reserved
                        // workspace allowance even for a large runtime pack.
                        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
                        using PackageInspectionAssemblyContext context =
                            await workspace.RealizePackageInspectionAsync(
                                input.SelectAssemblies([new(path, tfm)]),
                                options: new()
                                {
                                    MaxAssembliesPerRole = 1,
                                    MaxAssemblyEntryBytes = BrowserInspectionScope.MaxRetainedImageBytes,
                                    MaxAggregateRetainedImageBytes = BrowserInspectionScope.MaxRetainedImageBytes,
                                    RequireDeclaredEntryLengths = true,
                                },
                                cancellationToken: deadline.Token).ConfigureAwait(false);
                        PackageInspectionAssemblyOutcome outcome = context.Assemblies.Single();
                        if (runtime && outcome is PackageInspectionAssemblyOutcome.WithoutAssembly
                            {
                                Projection: ArtifactAssemblyProjectionOutcome.NotAssembly
                                {
                                    Kind: ArtifactNonAssemblyKind.NativeImage,
                                },
                            })
                            continue;
                        if (outcome is not PackageInspectionAssemblyOutcome.Available available)
                            throw new InvalidOperationException(
                                $"Platform catalog cannot inspect {packageId}@{version}/{path}: {outcome}.");
                        AssemblyCatalogMetadata metadata =
                            AssemblyContextCatalogMetadataQuery.ExecuteParticipant(
                                available.Group, available.Participant) switch
                            {
                                AssemblyContextEntry<AssemblyCatalogMetadata>.Available result => result.Value,
                                AssemblyContextEntry<AssemblyCatalogMetadata>.Rejected rejected =>
                                    throw new InvalidOperationException(
                                        $"{packageId}/{path}: {rejected.Failure.Kind}: {rejected.Failure.Detail}"),
                                AssemblyContextEntry<AssemblyCatalogMetadata>.Failed failed =>
                                    throw new InvalidOperationException(
                                        $"{packageId}/{path}: {failed.Error.Message}", failed.Error),
                                _ => throw new InvalidOperationException("Unknown catalog metadata result."),
                            };
                        AssemblyReferenceIdentity identity = available.Participant.Assembly.Identity;
                        if (!libraries.TryAdd(identity.Name,
                                new(identity.Name, Path.GetFileName(path),
                                    identity.Version?.ToString()
                                        ?? throw new InvalidOperationException("The platform assembly has no version."),
                                    metadata)))
                            throw new InvalidOperationException(
                                $"Platform pack {packageId}@{version} contains duplicate assembly {identity.Name}.");
                    }
                    if (libraries.Count == 0)
                        throw new InvalidOperationException(
                            $"Platform pack {packageId}@{version} contains no managed libraries for {tfm}.");
                    return libraries;
                }
            },
            timeout,
            cancellationToken);
    }

    static string[] RuntimeEntries(BrowserPackage package, string tfm) =>
        PackageAssetSelector.SelectPlatformPack(package.Content, tfm, "linux-x64") switch
        {
            PackageAssetSelection.Selected selected =>
                [.. selected.Universe.Assets.Select(asset => asset.EntryPath)],
            PackageAssetSelection.Invalid invalid => throw new InvalidOperationException(invalid.Message),
            PackageAssetSelection.NoMatch missing => throw new InvalidOperationException(missing.Message),
            PackageAssetSelection.Ambiguous ambiguous => throw new InvalidOperationException(ambiguous.Message),
            _ => throw new InvalidOperationException("Unknown platform pack selection outcome."),
        };

    static NuGetFramework Framework(string targetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        NuGetFramework framework = NuGetFramework.ParseFolder(targetFramework);
        if (framework.IsUnsupported || framework.HasPlatform
            || !framework.Framework.Equals(FrameworkConstants.FrameworkIdentifiers.NetCoreApp, StringComparison.Ordinal)
            || framework.Version.Major < 5)
            throw new ArgumentException(
                "A platform catalog requires an unqualified .NET target framework (for example net11.0).",
                nameof(targetFramework));
        return framework;
    }

    static string Version(NuGetFramework framework, string platformVersion)
    {
        if (!NuGetVersion.TryParse(platformVersion, out NuGetVersion? version)
            || version.Major != framework.Version.Major || version.Minor != framework.Version.Minor)
            throw new ArgumentException(
                "The exact platform version must match the target framework release line.",
                nameof(platformVersion));
        return version.ToNormalizedString();
    }

    sealed record Family(string Pack, string ReferencePackage, string RuntimePackage);
    sealed record Library(string Name, string File, string Version, AssemblyCatalogMetadata Metadata);
}
