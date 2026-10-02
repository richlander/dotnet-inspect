#:project ../src/DotnetInspector.Cache/DotnetInspector.Cache.csproj
#:project ../src/DotnetInspector.Ecosystems/DotnetInspector.Ecosystems.csproj
#:project ../src/DotnetInspector.Networking/DotnetInspector.Networking.csproj
#:project ../src/DotnetInspector.Packages/DotnetInspector.Packages.csproj
#:project ../src/DotnetInspector.Queries/DotnetInspector.Queries.csproj
#:project ../src/DotnetInspect.Web.Interop.Package/DotnetInspect.Web.Interop.Package.csproj

using System.Text;
using System.Text.Json;
using DotnetInspect.Web.Interop.Package;
using DotnetInspector.Cache;
using DotnetInspector.Ecosystems;
using DotnetInspector.Networking;
using DotnetInspector.Packages;
using DotnetInspector.Queries;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    const string usage =
        "Usage: dotnet run -c Release eng/measure-inspect-web-workspace-budget.cs -- "
        + "(--check | --refresh) <output.tsv>";
    if (args.Length != 2
        || args[0] is not ("--check" or "--refresh"))
    {
        Console.WriteLine(usage);
        return 2;
    }

    string mode = args[0];
    string path = args[1];
    bool refresh = mode == "--refresh";
    string outputPath = Path.GetFullPath(path);
    IReadOnlyDictionary<string, string>? pins = refresh
        ? null
        : ReadPins(outputPath);

    string cachePath = Path.Combine(
        Environment.CurrentDirectory,
        "artifacts",
        "inspect-web-workspace-budget-cache");
    Directory.CreateDirectory(cachePath);
    PersistentCache.Initialize("inspect-web-workspace-budget-census", cachePath);

    BrowserPackageCacheStats limits = ReadLimits();
    ValidatePackageEntryEnvelope(limits);
    PackageSetDescriptor packageSet = PackageSetCatalog.Lookup(
        PackageSetIds.MicrosoftExtensions) switch
    {
        PackageSetLookupResult.Known known => known.Descriptor,
        PackageSetLookupResult.Unknown => throw new InvalidOperationException(
            "The Microsoft.Extensions package set is not registered."),
        _ => throw new InvalidOperationException(
            "The Microsoft.Extensions package-set lookup returned an unknown result."),
    };
    if (pins is not null)
        ValidatePins(packageSet, pins);

    var extractions = new List<PackageExtractionResult>();
    try
    {
        List<PackageDemand> packages = await MeasurePackagesAsync(
            packageSet,
            pins,
            refresh,
            extractions);
        WorkspaceDemand demand = await RealizeWorkspaceAsync(packages, limits);
        string tsv = Serialize(packages);

        if (refresh)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath)
                    ?? throw new InvalidOperationException(
                        "The output path has no parent directory."));
            File.WriteAllText(outputPath, tsv, new UTF8Encoding(false));
        }
        else
        {
            byte[] expected = File.ReadAllBytes(outputPath);
            byte[] actual = Encoding.UTF8.GetBytes(tsv);
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Console.WriteLine(
                    $"{outputPath} does not match the immutable package measurements. "
                    + "Run with --refresh only when intentionally updating the census.");
                return 1;
            }
        }

        PrintSummary(packageSet, demand, limits, outputPath, refresh);
        return 0;
    }
    finally
    {
        foreach (PackageExtractionResult extraction in extractions)
            PackageExtractor.Cleanup(extraction.TempDir);
    }
}

static BrowserPackageCacheStats ReadLimits()
{
#pragma warning disable CA1416 // This export only projects owner-issued scalar limits.
    string json = PackageExports.PackageCacheStats();
#pragma warning restore CA1416
    using JsonDocument document = JsonDocument.Parse(json);
    JsonElement root = document.RootElement;
    return new(
        root.GetProperty("packages").GetInt32(),
        root.GetProperty("resident").GetInt32(),
        root.GetProperty("maxPackageEntries").GetInt32(),
        root.GetProperty("workspaces").GetInt32(),
        root.GetProperty("maxWorkspaces").GetInt32(),
        root.GetProperty("maxWorkspaceAssembliesPerRole").GetInt32(),
        root.GetProperty("residentBytes").GetInt64(),
        root.GetProperty("maxResidentBytes").GetInt64(),
        root.GetProperty("maxWorkspaceRetainedImageBytes").GetInt64());
}

static void ValidatePackageEntryEnvelope(BrowserPackageCacheStats limits)
{
    int required = checked(
        WorkspaceScopeLimits.DefaultMaxPackages * limits.MaxWorkspaces);
    if (limits.MaxPackageEntries != required)
    {
        throw new InvalidOperationException(
            $"The Browser package-entry budget is {limits.MaxPackageEntries}; "
            + $"the {WorkspaceScopeLimits.DefaultMaxPackages}-package logical limit "
            + $"across {limits.MaxWorkspaces} charged realizations requires exactly "
            + $"{required} entries.");
    }
}

static IReadOnlyDictionary<string, string> ReadPins(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException(
            "The checked census does not exist. Run --refresh to create it.",
            path);
    }

    string[] lines = File.ReadAllLines(path);
    if (lines.Length == 0
        || !lines[0].Equals(PackageDemand.Header, StringComparison.Ordinal))
    {
        throw new InvalidDataException(
            $"The census must start with the exact header '{PackageDemand.Header}'.");
    }

    var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int index = 1; index < lines.Length; index++)
    {
        string[] columns = lines[index].Split('\t');
        if (columns.Length != PackageDemand.ColumnCount
            || string.IsNullOrWhiteSpace(columns[0])
            || string.IsNullOrWhiteSpace(columns[1]))
        {
            throw new InvalidDataException(
                $"The census row at line {index + 1} is malformed.");
        }
        if (!pins.TryAdd(columns[0], columns[1]))
        {
            throw new InvalidDataException(
                $"The census repeats package '{columns[0]}'.");
        }
    }
    return pins;
}

static void ValidatePins(
    PackageSetDescriptor packageSet,
    IReadOnlyDictionary<string, string> pins)
{
    string[] expected =
    [
        .. packageSet.Members.Select(member => member.PackageId),
    ];
    string[] missing =
    [
        .. expected.Where(packageId => !pins.ContainsKey(packageId)),
    ];
    string[] extra =
    [
        .. pins.Keys.Where(packageId => !expected.Contains(
            packageId,
            StringComparer.OrdinalIgnoreCase)),
    ];
    if (missing.Length > 0 || extra.Length > 0)
    {
        throw new InvalidDataException(
            "The census package membership differs from the shipped package set. "
            + $"Missing: {Display(missing)}. Extra: {Display(extra)}.");
    }
}

static async Task<List<PackageDemand>> MeasurePackagesAsync(
    PackageSetDescriptor packageSet,
    IReadOnlyDictionary<string, string>? pins,
    bool refresh,
    List<PackageExtractionResult> extractions)
{
    var packages = new List<PackageDemand>(packageSet.Members.Length);
    foreach (PackageCoordinate coordinate in packageSet.Members)
    {
        string packageId = coordinate.PackageId;
        string? pinnedVersion = pins is null ? null : pins[packageId];
        PackageExtractionOutcome outcome = await PackageExtractor.ExtractPackageAsync(
            HttpClientFactory.Shared,
            packageId,
            log: null,
            tempDirPrefix: "inspect-web-workspace-budget",
            version: pinnedVersion,
            forceLatest: refresh);
        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException(
                $"{packageId}: {outcome.ErrorMessage}");
        }

        PackageExtractionResult result = outcome.Result!;
        extractions.Add(result);
        string version = result.Version
            ?? throw new InvalidOperationException(
                $"{packageId}: package acquisition did not select a version.");
        if (refresh && !version.StartsWith("10.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{packageId}: the package set requires the stable major-10 product line; "
                + $"latest stable resolution selected {version}.");
        }

        IPackageContent content = result.AcquiredPayload?.Content
            ?? new FileSystemPackageContent(
                result.ExtractPath,
                result.NupkgPath,
                result.FromCache,
                result.ProducerKey
                    ?? throw new InvalidOperationException(
                        $"{packageId}: package acquisition did not retain producer identity."));
        PackageCompileAssetSelection selection =
            PackageCompileAssetSelector.Select(content, packageId);
        if (!selection.IsSelected)
        {
            throw new InvalidOperationException(
                $"{packageId}@{version}: compile-asset selection returned "
                + $"{selection.Status}. {selection.Message}");
        }

        long archiveBytes = ArchiveLength(packageId, content);
        var manifest = content as IPackageContentEntryManifest
            ?? throw new InvalidOperationException(
                $"{packageId}@{version}: admitted package content has no entry manifest.");
        packages.Add(new(
            packageId,
            version,
            archiveBytes,
            selection.Status.ToString(),
            selection.TargetFramework!,
            selection.Assets.Count,
            Sum(selection.Assets, manifest),
            selection.ImplementationAssets.Count,
            Sum(selection.ImplementationAssets, manifest),
            new PackageRootRealization(content, packageId, version)));
    }
    return packages;
}

static async Task<WorkspaceDemand> RealizeWorkspaceAsync(
    IReadOnlyList<PackageDemand> packages,
    BrowserPackageCacheStats limits)
{
    await using var workspace = new InspectionWorkspace();
    using PackageAssemblyContextRealization roles =
        workspace.RealizePackageAssemblyContextRoles(
            packages.Select(package => package.Root),
            new()
            {
                MaxAssembliesPerRole =
                    limits.MaxWorkspaceAssembliesPerRole,
                MaxAggregateRetainedImageBytes =
                    limits.MaxWorkspaceRetainedImageBytes,
                MaxAssemblyEntryBytes =
                    limits.MaxWorkspaceRetainedImageBytes,
                RequireDeclaredEntryLengths = true,
            });
    return new(
        packages.Count,
        packages.Sum(package => package.ArchiveBytes),
        packages.Sum(package => package.SurfaceAssemblies),
        packages.Sum(package => package.SurfaceBytes),
        packages.Sum(package => package.ImplementationAssemblies),
        packages.Sum(package => package.ImplementationBytes),
        roles.SurfaceParticipants.Length,
        roles.ImplementationParticipants.Length,
        roles.SharesGroup);
}

static long ArchiveLength(string packageId, IPackageContent content)
{
    if (!content.TryOpenArchive(out Stream? archive))
    {
        throw new InvalidOperationException(
            $"{packageId}: admitted package content did not retain an archive.");
    }
    using (archive)
        return archive.Length;
}

static long Sum(
    IReadOnlyList<PackageCompileAsset> assets,
    IPackageContentEntryManifest manifest)
{
    long total = 0;
    foreach (PackageCompileAsset asset in assets)
    {
        if (!manifest.TryGetEntryLength(asset.Path, out long length))
        {
            throw new InvalidOperationException(
                $"The admitted manifest has no declared length for '{asset.Path}'.");
        }
        total = checked(total + length);
    }
    return total;
}

static string Serialize(IEnumerable<PackageDemand> packages)
{
    var text = new StringBuilder();
    text.Append(PackageDemand.Header).Append('\n');
    foreach (PackageDemand package in packages)
    {
        text.Append(package.PackageId).Append('\t')
            .Append(package.Version).Append('\t')
            .Append(package.ArchiveBytes).Append('\t')
            .Append(package.SelectionStatus).Append('\t')
            .Append(package.SelectedTargetFramework).Append('\t')
            .Append(package.SurfaceAssemblies).Append('\t')
            .Append(package.SurfaceBytes).Append('\t')
            .Append(package.ImplementationAssemblies).Append('\t')
            .Append(package.ImplementationBytes).Append('\n');
    }
    return text.ToString();
}

static void PrintSummary(
    PackageSetDescriptor packageSet,
    WorkspaceDemand demand,
    BrowserPackageCacheStats limits,
    string path,
    bool refresh)
{
    int replacementEntries = checked(demand.PackageEntries * 2);
    long replacementArchiveBytes = checked(demand.ArchiveBytes * 2);
    int chargedEntries = checked(demand.PackageEntries * limits.MaxWorkspaces);
    long chargedArchiveBytes = checked(
        demand.ArchiveBytes * limits.MaxWorkspaces);

    Console.WriteLine(
        $"{(refresh ? "Refreshed" : "Verified")} {path}");
    Console.WriteLine($"Scenario: {packageSet.Id.Value}");
    Console.WriteLine(
        $"Package entries: {demand.PackageEntries} of {limits.MaxPackageEntries} "
        + $"({Verdict(demand.PackageEntries, limits.MaxPackageEntries)})");
    Console.WriteLine(
        $"Package-entry envelope: {WorkspaceScopeLimits.DefaultMaxPackages} packages x "
        + $"{limits.MaxWorkspaces} charged realizations = {limits.MaxPackageEntries}");
    Console.WriteLine(
        $"Atomic replacement witness: {replacementEntries} entries / "
        + $"{FormatMiB(replacementArchiveBytes)} "
        + $"({Verdict(replacementEntries, limits.MaxPackageEntries)} / "
        + $"{Verdict(replacementArchiveBytes, limits.MaxResidentBytes)})");
    Console.WriteLine(
        $"Four-charge witness: {chargedEntries} entries / "
        + $"{FormatMiB(chargedArchiveBytes)} "
        + $"({Verdict(chargedEntries, limits.MaxPackageEntries)} / "
        + $"{Verdict(chargedArchiveBytes, limits.MaxResidentBytes)})");
    Console.WriteLine(
        $"Archive bytes: {FormatMiB(demand.ArchiveBytes)} of "
        + $"{FormatMiB(limits.MaxResidentBytes)} "
        + $"({Verdict(demand.ArchiveBytes, limits.MaxResidentBytes)})");
    Console.WriteLine(
        $"Surface role: {demand.SurfaceAssemblies} assemblies / "
        + $"{FormatMiB(demand.SurfaceBytes)}; realized "
        + $"{demand.RealizedSurfaceAssemblies} of "
        + $"{limits.MaxWorkspaceAssembliesPerRole}");
    Console.WriteLine(
        $"Implementation role: {demand.ImplementationAssemblies} assemblies / "
        + $"{FormatMiB(demand.ImplementationBytes)}; realized "
        + $"{demand.RealizedImplementationAssemblies} of "
        + $"{limits.MaxWorkspaceAssembliesPerRole}");
    Console.WriteLine(
        $"Workspace image budget: {FormatMiB(limits.MaxWorkspaceRetainedImageBytes)}; "
        + $"roles share one group: {demand.RolesShareGroup.ToString().ToLowerInvariant()}");
    Console.WriteLine(
        $"Workspace slots: 1 of {limits.MaxWorkspaces} "
        + $"({Verdict(1, limits.MaxWorkspaces)} after package admission)");
}

static string Verdict(long demand, long limit) =>
    demand <= limit ? "admitted" : "rejected";

static string FormatMiB(long bytes) =>
    $"{bytes / (1024d * 1024d):0.00} MiB";

static string Display(IEnumerable<string> values)
{
    string[] materialized = [.. values];
    return materialized.Length == 0
        ? "none"
        : string.Join(", ", materialized);
}

sealed record PackageDemand(
    string PackageId,
    string Version,
    long ArchiveBytes,
    string SelectionStatus,
    string SelectedTargetFramework,
    int SurfaceAssemblies,
    long SurfaceBytes,
    int ImplementationAssemblies,
    long ImplementationBytes,
    PackageRootRealization Root)
{
    internal const int ColumnCount = 9;
    internal const string Header =
        "package\tversion\tarchive_bytes\tselection_status\tselected_tfm\t"
        + "surface_assemblies\tsurface_bytes\timplementation_assemblies\t"
        + "implementation_bytes";
}

sealed record WorkspaceDemand(
    int PackageEntries,
    long ArchiveBytes,
    int SurfaceAssemblies,
    long SurfaceBytes,
    int ImplementationAssemblies,
    long ImplementationBytes,
    int RealizedSurfaceAssemblies,
    int RealizedImplementationAssemblies,
    bool RolesShareGroup);
