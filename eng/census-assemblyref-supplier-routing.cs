#:project ../src/DotnetInspector.Services/DotnetInspector.Services.csproj

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;

const string TargetFramework = "net11.0";
const string RuntimeFamily = "Microsoft.NETCore.App";
const string AspNetCoreFamily = "Microsoft.AspNetCore.App";

if (args.Length < 1
    || !TryPositiveInt(args.ElementAtOrDefault(1), 1, out int startRank)
    || !TryPositiveInt(args.ElementAtOrDefault(2), 100, out int packageCount))
{
    Console.Error.WriteLine(
        "Usage: dotnet run eng/census-assemblyref-supplier-routing.cs -- "
        + "<output.json> [start-rank] [package-count] "
        + "[package@version[/target-framework] ...]");
    return 2;
}

PackageCoordinate[] supplementalPackages;
try
{
    supplementalPackages = args
        .Skip(3)
        .Select(ParseCoordinate)
        .ToArray();
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

VerifyRootTargetEvidence();

string repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory())
    ?? throw new InvalidOperationException(
        "Run the AssemblyRef supplier census from inside the repository.");
string outputPath = Path.GetFullPath(args[0]);
string packageListPath = Path.Combine(
    repositoryRoot,
    "docs",
    "data",
    "nuget-top-packages.json");
string packagePinPath = Path.Combine(
    repositoryRoot,
    "docs",
    "data",
    "nuget-top-packages.lock.json");

List<PackageListEntry> packageList = JsonSerializer.Deserialize(
        File.ReadAllBytes(packageListPath),
        AssemblyRefSupplierCensusJsonContext.Default.ListPackageListEntry)
    ?? throw new InvalidDataException($"'{packageListPath}' is empty.");
PackagePinFile pinFile = JsonSerializer.Deserialize(
        File.ReadAllBytes(packagePinPath),
        AssemblyRefSupplierCensusJsonContext.Default.PackagePinFile)
    ?? throw new InvalidDataException($"'{packagePinPath}' is empty.");
if (pinFile.SchemaVersion != 1)
    throw new InvalidDataException("The package pin schema is unsupported.");

Dictionary<string, PackagePin> pins = pinFile.Packages.ToDictionary(
    pin => pin.Package,
    StringComparer.OrdinalIgnoreCase);
PackageListEntry[] selected = packageList
    .Where(entry => entry.Rank >= startRank)
    .OrderBy(entry => entry.Rank)
    .Take(packageCount)
    .ToArray();
if (selected.Length != packageCount)
{
    throw new InvalidDataException(
        $"Ranks {startRank}-{startRank + packageCount - 1} are not complete.");
}
selected =
[
    .. selected,
    .. supplementalPackages.Select(
        (coordinate, index) =>
            new PackageListEntry(
                Rank: 1001 + index,
                coordinate.Id,
                Downloads: 0)),
];
foreach (PackageCoordinate coordinate in supplementalPackages)
{
    if (!pins.TryAdd(
            coordinate.Id,
            new PackagePin(
                coordinate.Id,
                coordinate.Version,
                Tfm: null,
                Status: "pinned",
                Detail: "Supplemental scenario")))
    {
        throw new InvalidDataException(
            $"Supplemental package '{coordinate.Id}' duplicates the pinned corpus.");
    }
}

string packagePinSha256 = Convert.ToHexStringLower(
    SHA256.HashData(File.ReadAllBytes(packagePinPath)));
string temporaryRoot = Path.Combine(
    Path.GetTempPath(),
    $"dotnet-inspect-assemblyref-routing-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

var failures = new ConcurrentBag<CensusFailure>();
var rootResults = new ConcurrentBag<RootCensusResult>();
var platformCatalogs =
    new ConcurrentDictionary<string, Lazy<PlatformCatalog>>(
        StringComparer.OrdinalIgnoreCase);
Dictionary<string, string> supplementalTargetFrameworks = supplementalPackages
    .ToDictionary(
        coordinate => coordinate.Id,
        coordinate => coordinate.TargetFramework,
        StringComparer.OrdinalIgnoreCase);

try
{
    await Parallel.ForEachAsync(
        selected,
        new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount),
        },
        async (entry, cancellationToken) =>
        {
            if (!pins.TryGetValue(entry.Package, out PackagePin? pin))
            {
                failures.Add(
                    new CensusFailure(
                        entry.Rank,
                        entry.Package,
                        "pin-missing",
                        "The ranked package has no pinned corpus outcome."));
                return;
            }

            string rootTargetFramework =
                supplementalTargetFrameworks.GetValueOrDefault(
                    entry.Package,
                    TargetFramework);

            if (pin.Status.Equals(
                    "no-library",
                    StringComparison.OrdinalIgnoreCase))
            {
                rootResults.Add(
                    RootCensusResult.NoLibrary(
                        entry,
                        pin,
                        rootTargetFramework,
                        pin.Detail ?? "Pinned as no-library."));
                return;
            }

            if (!pin.Status.Equals(
                    "pinned",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(pin.Version))
            {
                failures.Add(
                    new CensusFailure(
                        entry.Rank,
                        entry.Package,
                        "pin-invalid",
                        $"Unsupported pin status '{pin.Status}'."));
                return;
            }

            string rootDirectory = Path.Combine(
                temporaryRoot,
                entry.Rank.ToString("D3"));
            Directory.CreateDirectory(rootDirectory);

            try
            {
                RootCensusResult result = await AnalyzeRootAsync(
                    entry,
                    pin,
                    rootTargetFramework,
                    rootDirectory,
                    platformCatalogs,
                    cancellationToken);
                rootResults.Add(result);
                Console.Error.WriteLine(
                    $"rank {entry.Rank}: {entry.Package}@{pin.Version}: "
                    + $"{result.Assemblies.Count} assemblies, "
                    + $"{result.Observations.Count} AssemblyRefs");
            }
            catch (Exception exception) when (
                exception is IOException
                    or InvalidDataException
                    or BadImageFormatException
                    or JsonException
                    or UnauthorizedAccessException)
            {
                failures.Add(
                    new CensusFailure(
                        entry.Rank,
                        entry.Package,
                        "analysis-failed",
                        OneLine(exception.Message)));
            }
        });
}
finally
{
    Directory.Delete(temporaryRoot, recursive: true);
}

RootCensusResult[] orderedRoots = rootResults
    .OrderBy(result => result.Rank)
    .ToArray();
CensusFailure[] orderedFailures = failures
    .OrderBy(failure => failure.Rank)
    .ThenBy(failure => failure.Kind, StringComparer.Ordinal)
    .ToArray();
AssemblyRefObservation[] observations = orderedRoots
    .SelectMany(result => result.Observations)
    .OrderBy(observation => observation.RootRank)
    .ThenBy(observation => observation.SourceAssembly, StringComparer.Ordinal)
    .ThenBy(observation => observation.ReferenceName, StringComparer.Ordinal)
    .ThenBy(observation => observation.ReferenceVersion, StringComparer.Ordinal)
    .ToArray();

var summary = new CensusSummary(
    RequestedRoots: selected.Length,
    RestoredRoots: orderedRoots.Count(result => result.Status == "analyzed"),
    NoLibraryRoots: orderedRoots.Count(result => result.Status == "no-library"),
    NoCompileAssetRoots: orderedRoots.Count(
        result => result.Status == "no-compile-assets"),
    FailedRoots: orderedFailures
        .Select(failure => failure.Rank)
        .Distinct()
        .Count(),
    RootAssemblies: orderedRoots.Sum(result => result.Assemblies.Count),
    SelectedCompileAssets: orderedRoots.Sum(
        result => result.SelectedCompileAssetCount),
    ReachablePackages: orderedRoots
        .Where(result => result.Status == "analyzed")
        .Sum(result => result.ReachablePackageCount),
    MaximumReachablePackages: orderedRoots
        .Where(result => result.Status == "analyzed")
        .Select(result => result.ReachablePackageCount)
        .DefaultIfEmpty()
        .Max(),
    AverageReachablePackages: Math.Round(
        orderedRoots
            .Where(result => result.Status == "analyzed")
            .Select(result => result.ReachablePackageCount)
            .DefaultIfEmpty()
            .Average(),
        2),
    MaximumSelectedCompileAssets: orderedRoots
        .Where(result => result.Status == "analyzed")
        .Select(result => result.SelectedCompileAssetCount)
        .DefaultIfEmpty()
        .Max(),
    AverageSelectedCompileAssets: Math.Round(
        orderedRoots
            .Where(result => result.Status == "analyzed")
            .Select(result => result.SelectedCompileAssetCount)
            .DefaultIfEmpty()
            .Average(),
        2),
    AssetDecodeFailures: orderedRoots.Sum(
        result => result.AssetFailures.Count),
    AssemblyReferences: observations.Length,
    Classifications: observations
        .GroupBy(observation => observation.Classification)
        .OrderBy(group => group.Key)
        .ToDictionary(group => group.Key, group => group.Count()),
    ExactNamesakeWithCompetingPackage: observations.Count(
        observation =>
            observation.Classification
                == AssemblyRefClassification.ExactNamesakePackage
            && observation.MatchingPackageIds.Count > 1),
    PackageWithPlatformOverlap: observations.Count(
        observation =>
            observation.MatchingPackageIds.Count > 0
            && observation.MatchingPlatformFamilies.Count > 0),
    FilenameOnlyPackage: observations.Count(
        observation =>
            observation.Classification
                == AssemblyRefClassification.FilenameOnlyPackage),
    IdentityOnlyPackage: observations.Count(
        observation =>
            observation.Classification
                == AssemblyRefClassification.IdentityOnlyPackage),
    ExactNamesakeCandidateMisses: observations.Count(
        observation => observation.ExactNamesakeCandidateMiss),
    PrefixCandidateMisses: observations.Count(
        observation => observation.PrefixCandidateMiss),
    FilenameCandidateIdentityMismatches: observations.Count(
        observation => observation.FilenameCandidateIdentityMismatch));

var report = new AssemblyRefSupplierCensusReport(
    SchemaVersion: 2,
    CorpusTargetFramework: TargetFramework,
    PackageList: Path.GetRelativePath(repositoryRoot, packageListPath),
    PackagePin: Path.GetRelativePath(repositoryRoot, packagePinPath),
    PackagePinSha256: packagePinSha256,
    SupplementalPackages: supplementalPackages
        .Select(coordinate =>
            $"{coordinate.Id}@{coordinate.Version}/{coordinate.TargetFramework}")
        .ToArray(),
    PlatformFamilies: platformCatalogs.Values
        .Where(catalog => catalog.IsValueCreated)
        .SelectMany(catalog => catalog.Value.Families)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(family => family, StringComparer.Ordinal)
        .ToArray(),
    Summary: summary,
    Roots: orderedRoots,
    Failures: orderedFailures);

Directory.CreateDirectory(
    Path.GetDirectoryName(outputPath)
        ?? throw new InvalidOperationException("The output path has no directory."));
await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(
        report,
        AssemblyRefSupplierCensusJsonContext.Default
            .AssemblyRefSupplierCensusReport)
        + Environment.NewLine,
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

Console.WriteLine(
    $"Roots: {summary.RestoredRoots} analyzed, "
    + $"{summary.NoLibraryRoots} no-library, "
    + $"{summary.NoCompileAssetRoots} no-compile-assets, "
    + $"{summary.FailedRoots} failed");
Console.WriteLine(
    $"Root assemblies: {summary.RootAssemblies}; "
    + $"selected closure assets: {summary.SelectedCompileAssets}; "
    + $"AssemblyRefs: {summary.AssemblyReferences}");
Console.WriteLine(
    $"Reachable packages per analyzed root: "
    + $"average {summary.AverageReachablePackages:F2}, "
    + $"maximum {summary.MaximumReachablePackages}");
Console.WriteLine(
    $"Selected compile assets per analyzed root: "
    + $"average {summary.AverageSelectedCompileAssets:F2}, "
    + $"maximum {summary.MaximumSelectedCompileAssets}");
foreach ((AssemblyRefClassification classification, int count)
    in summary.Classifications)
{
    Console.WriteLine($"{classification}: {count}");
}
Console.WriteLine(
    $"Exact namesake with competing Package supplier: "
    + $"{summary.ExactNamesakeWithCompetingPackage}");
Console.WriteLine(
    $"Package and Platform overlap: {summary.PackageWithPlatformOverlap}");
Console.WriteLine(
    $"Filename-only Package suppliers: {summary.FilenameOnlyPackage}");
Console.WriteLine(
    $"Identity-only Package suppliers: {summary.IdentityOnlyPackage}");
Console.WriteLine(
    $"Exact-namesake candidate misses: "
    + $"{summary.ExactNamesakeCandidateMisses}");
Console.WriteLine(
    $"Prefix candidate misses: {summary.PrefixCandidateMisses}");
Console.WriteLine(
    $"Filename candidate identity mismatches: "
    + $"{summary.FilenameCandidateIdentityMismatches}");
Console.WriteLine($"Asset decode failures: {summary.AssetDecodeFailures}");
Console.WriteLine($"Report: {outputPath}");

return orderedFailures.Length == 0
    && summary.AssetDecodeFailures == 0
        ? 0
        : 1;

static async Task<RootCensusResult> AnalyzeRootAsync(
    PackageListEntry entry,
    PackagePin pin,
    string targetFramework,
    string rootDirectory,
    ConcurrentDictionary<string, Lazy<PlatformCatalog>> platformCatalogs,
    CancellationToken cancellationToken)
{
    string projectPath = Path.Combine(rootDirectory, "census.csproj");
    string packageId = SecurityElement.Escape(entry.Package)
        ?? throw new InvalidDataException("The package ID could not be escaped.");
    string packageVersion = SecurityElement.Escape(pin.Version!)
        ?? throw new InvalidDataException("The package version could not be escaped.");
    await File.WriteAllTextAsync(
        projectPath,
        $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{{targetFramework}}</TargetFramework>
            <DisableImplicitFrameworkReferences>false</DisableImplicitFrameworkReferences>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="{{packageId}}" Version="{{packageVersion}}" />
          </ItemGroup>
        </Project>
        """,
        cancellationToken);

    ProcessResult restore = await RunAsync(
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
        [
            "restore",
            projectPath,
            "--nologo",
            "--verbosity",
            "quiet",
        ],
        cancellationToken);
    if (restore.ExitCode != 0)
    {
        throw new InvalidDataException(
            $"Restore exited {restore.ExitCode}: {OneLine(restore.StandardError)}");
    }

    string assetsPath = Path.Combine(
        rootDirectory,
        "obj",
        "project.assets.json");
    RootTargetEvidence rootTarget = ReadRootTargetEvidence(
        assetsPath,
        targetFramework,
        entry.Package);
    ValidatePinnedRootVersion(
        entry.Package,
        pin.Version!,
        rootTarget.PackageVersion);
    List<(string Path, string PackageName, string Version)> selectedAssets =
        ProjectAssetsParser.Parse(
            assetsPath,
            targetFramework,
            log: null);
    if (selectedAssets.Count == 0)
    {
        return RootCensusResult.NoCompileAssets(
            entry,
            pin,
            targetFramework,
            "Restore selected no package compile assets.");
    }

    string[] eligiblePlatformFamilies = rootTarget.EligiblePlatformFamilies;
    string platformCatalogKey = targetFramework
        + "|aspnet="
        + eligiblePlatformFamilies.Contains(
            AspNetCoreFamily,
            StringComparer.OrdinalIgnoreCase);
    PlatformCatalog platformCatalog = platformCatalogs.GetOrAdd(
        platformCatalogKey,
        _ => new Lazy<PlatformCatalog>(
            () => LoadPlatformCatalog(
                targetFramework,
                assetsPath,
                eligiblePlatformFamilies),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    var assetFailures = new List<string>();
    var assets = new List<AssemblyAsset>(selectedAssets.Count);
    foreach (var selectedAsset in selectedAssets)
    {
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(selectedAsset.Path);
            if (!session.HasMetadata)
            {
                assetFailures.Add(
                    $"{selectedAsset.PackageName}/{Path.GetFileName(selectedAsset.Path)}: no metadata");
                continue;
            }

            assets.Add(
                new AssemblyAsset(
                    selectedAsset.PackageName,
                    selectedAsset.Version,
                    selectedAsset.Path,
                    session.AssemblyIdentity(),
                    session.AssemblyReferenceIdentities()));
        }
        catch (Exception exception) when (
            exception is IOException
                or BadImageFormatException
                or UnauthorizedAccessException)
        {
            assetFailures.Add(
                $"{selectedAsset.PackageName}/{Path.GetFileName(selectedAsset.Path)}: "
                + OneLine(exception.Message));
        }
    }

    AssemblyAsset[] rootAssets = assets
        .Where(asset => asset.PackageId.Equals(
            entry.Package,
            StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (rootAssets.Length == 0)
    {
        throw new InvalidDataException(
            "The selected compile closure contains no root-package assembly.");
    }

    var observations = new List<AssemblyRefObservation>();
    foreach (AssemblyAsset source in rootAssets)
    {
        foreach (AssemblyReferenceIdentity reference in source.References
            .Distinct(AssemblyReferenceIdentity.EquivalentComparer))
        {
            observations.Add(
                Classify(
                    entry,
                    source,
                    reference,
                    assets,
                    eligiblePlatformFamilies,
                    platformCatalog));
        }
    }

    return new RootCensusResult(
        entry.Rank,
        entry.Package,
        pin.Version,
        targetFramework,
        "analyzed",
        Detail: null,
        ReachablePackageCount: selectedAssets
            .Select(asset => asset.PackageName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(),
        SelectedCompileAssetCount: selectedAssets.Count,
        Assemblies: rootAssets
            .Select(asset => Path.GetFileName(asset.Path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray(),
        EligiblePlatformFamilies: eligiblePlatformFamilies,
        AssetFailures: assetFailures,
        Observations: observations);
}

static AssemblyRefObservation Classify(
    PackageListEntry root,
    AssemblyAsset source,
    AssemblyReferenceIdentity reference,
    IReadOnlyList<AssemblyAsset> assets,
    IReadOnlyList<string> eligiblePlatformFamilies,
    PlatformCatalog platformCatalog)
{
    AssemblyAsset[] contextMatches = assets
        .Where(asset =>
            asset.PackageId.Equals(
                source.PackageId,
                StringComparison.OrdinalIgnoreCase)
            && asset.Path != source.Path
            && reference.MatchesCandidate(
                asset.Identity,
                allowVersionRollForward: true))
        .ToArray();
    AssemblyAsset[] packageMatches = assets
        .Where(asset =>
            !asset.PackageId.Equals(
                source.PackageId,
                StringComparison.OrdinalIgnoreCase)
            && reference.MatchesCandidate(
                asset.Identity,
                allowVersionRollForward: true))
        .ToArray();
    PlatformAssembly[] eligiblePlatformMatches = platformCatalog.Assemblies
        .Where(asset =>
            eligiblePlatformFamilies.Contains(
                asset.Family,
                StringComparer.OrdinalIgnoreCase)
            && reference.MatchesCandidate(
                asset.Identity,
                allowVersionRollForward: true))
        .ToArray();
    PlatformAssembly[] ineligiblePlatformMatches = platformCatalog.Assemblies
        .Where(asset =>
            !eligiblePlatformFamilies.Contains(
                asset.Family,
                StringComparer.OrdinalIgnoreCase)
            && reference.MatchesCandidate(
                asset.Identity,
                allowVersionRollForward: true))
        .ToArray();

    bool exactNamesake = packageMatches.Any(
        asset =>
            asset.PackageId.Equals(
                reference.Name,
                StringComparison.OrdinalIgnoreCase)
            && HasNamesakeFilename(asset, reference));
    bool prefix = packageMatches.Any(
        asset =>
            IsBoundaryPrefix(asset.PackageId, reference.Name)
            && HasNamesakeFilename(asset, reference));
    bool filenameOnly = packageMatches.Any(
        asset =>
            !HasNameAffinity(asset.PackageId, reference.Name)
            && HasNamesakeFilename(asset, reference));
    bool identityOnly = packageMatches.Any(
        asset => !HasNamesakeFilename(asset, reference));

    AssemblyRefClassification classification =
        contextMatches.Length > 0
            ? AssemblyRefClassification.ContextPackageMember
            : exactNamesake
                ? AssemblyRefClassification.ExactNamesakePackage
                : prefix
                    ? AssemblyRefClassification.PrefixPackage
                    : filenameOnly
                        ? AssemblyRefClassification.FilenameOnlyPackage
                        : identityOnly
                            ? AssemblyRefClassification.IdentityOnlyPackage
                            : eligiblePlatformMatches.Length > 0
                                ? AssemblyRefClassification.Platform
                                : IsIntrinsicCoreLibrary(reference.Name)
                                    ? AssemblyRefClassification.IntrinsicCoreLibrary
                                    : ineligiblePlatformMatches.Length > 0
                                        ? AssemblyRefClassification.IneligiblePlatform
                                        : AssemblyRefClassification.Unresolved;

    string[] packageIds = packageMatches
        .Select(asset => asset.PackageId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(package => package, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    IGrouping<string, AssemblyAsset>[] dependencyPackages = assets
        .Where(asset => !asset.PackageId.Equals(
            source.PackageId,
            StringComparison.OrdinalIgnoreCase))
        .GroupBy(asset => asset.PackageId, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    bool exactNamesakeCandidateMiss = dependencyPackages.Any(group =>
            group.Key.Equals(
                reference.Name,
                StringComparison.OrdinalIgnoreCase)
            && !group.Any(asset => reference.MatchesCandidate(
                asset.Identity,
                allowVersionRollForward: true)));
    bool prefixCandidateMiss = dependencyPackages.Any(group =>
            !group.Key.Equals(
                reference.Name,
                StringComparison.OrdinalIgnoreCase)
            && IsBoundaryPrefix(group.Key, reference.Name)
            && !group.Any(asset => reference.MatchesCandidate(
                asset.Identity,
                allowVersionRollForward: true)));
    bool filenameCandidateIdentityMismatch = assets.Any(
        asset =>
            !asset.PackageId.Equals(
                source.PackageId,
                StringComparison.OrdinalIgnoreCase)
            && HasNamesakeFilename(asset, reference)
            && !reference.MatchesCandidate(
                asset.Identity,
                allowVersionRollForward: true));

    return new AssemblyRefObservation(
        RootRank: root.Rank,
        RootPackage: root.Package,
        SourceAssembly: Path.GetFileName(source.Path),
        ReferenceName: reference.Name,
        ReferenceVersion: reference.Version?.ToString(),
        Classification: classification,
        MatchingPackageIds: packageIds,
        MatchingPlatformFamilies: eligiblePlatformMatches
            .Select(asset => asset.Family)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family, StringComparer.Ordinal)
            .ToArray(),
        ExactNamesakeCandidateMiss: exactNamesakeCandidateMiss,
        PrefixCandidateMiss: prefixCandidateMiss,
        FilenameCandidateIdentityMismatch: filenameCandidateIdentityMismatch);
}

static PlatformCatalog LoadPlatformCatalog(
    string targetFramework,
    string assetsPath,
    IReadOnlyList<string> requiredFamilies)
{
    string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
    var runtimeVersionDirectory = new DirectoryInfo(runtimeDirectory);
    DirectoryInfo? dotnetRoot = runtimeVersionDirectory
        .Parent?
        .Parent?
        .Parent;
    if (dotnetRoot is null)
        throw new InvalidOperationException("The dotnet root could not be located.");

    var assemblies = new List<PlatformAssembly>();
    var families = new List<string>();
    AddFamily(RuntimeFamily);
    AddFamily(AspNetCoreFamily);
    string? missingFamily = requiredFamilies.FirstOrDefault(
        required => !families.Contains(
            required,
            StringComparer.OrdinalIgnoreCase));
    if (missingFamily is not null)
    {
        throw new InvalidDataException(
            $"Reference pack '{missingFamily}.Ref' has no '{targetFramework}' "
            + "reference directory.");
    }

    return new PlatformCatalog(families, assemblies);

    void AddFamily(string family)
    {
        string packName = $"{family}.Ref";
        string? refDirectory =
            ReadPackageFolders(assetsPath)
            .Select(folder => Path.Combine(
                folder,
                packName.ToLowerInvariant()))
            .Append(Path.Combine(
                dotnetRoot.FullName,
                "packs",
                packName))
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(path => (
                Path: path,
                Version: Version.TryParse(
                    Path.GetFileName(path).Split('-')[0],
                    out Version? version)
                        ? version
                        : null))
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(
                candidate => candidate.Path,
                StringComparer.OrdinalIgnoreCase)
            .Select(candidate => Path.Combine(
                candidate.Path,
                "ref",
                targetFramework))
            .FirstOrDefault(Directory.Exists);
        if (refDirectory is null)
            return;

        families.Add(family);
        foreach (string path in Directory.EnumerateFiles(
            refDirectory,
            "*.dll",
            SearchOption.TopDirectoryOnly))
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(path);
            if (!session.HasMetadata)
            {
                throw new InvalidDataException(
                    $"Reference-pack member '{path}' has no Metadata.");
            }

            assemblies.Add(
                new PlatformAssembly(
                    family,
                    path,
                    session.AssemblyIdentity()));
        }
    }
}

static string[] ReadPackageFolders(string assetsPath)
{
    using JsonDocument document = JsonDocument.Parse(
        File.ReadAllBytes(assetsPath));
    if (!document.RootElement.TryGetProperty(
            "packageFolders",
            out JsonElement packageFolders)
        || packageFolders.ValueKind != JsonValueKind.Object)
    {
        return [];
    }

    return packageFolders
        .EnumerateObject()
        .Select(folder => folder.Name)
        .ToArray();
}

static RootTargetEvidence ReadRootTargetEvidence(
    string assetsPath,
    string targetFramework,
    string rootPackageId)
{
    using JsonDocument document = JsonDocument.Parse(
        File.ReadAllBytes(assetsPath));
    return ProjectRootTargetEvidence(
        document.RootElement,
        targetFramework,
        rootPackageId);
}

static RootTargetEvidence ProjectRootTargetEvidence(
    JsonElement assets,
    string targetFramework,
    string rootPackageId)
{
    var families = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        RuntimeFamily,
    };
    if (!assets.TryGetProperty(
            "targets",
            out JsonElement targets))
    {
        throw new InvalidDataException(
            "The assets file has no targets.");
    }

    JsonProperty target = targets
        .EnumerateObject()
        .FirstOrDefault(candidate =>
            candidate.Name.Split('/')[0].Equals(
                targetFramework,
                StringComparison.OrdinalIgnoreCase));
    if (target.Value.ValueKind != JsonValueKind.Object)
    {
        throw new InvalidDataException(
            $"The assets file has no '{targetFramework}' target.");
    }

    string rootPrefix = rootPackageId + "/";
    JsonProperty rootLibrary = target.Value
        .EnumerateObject()
        .FirstOrDefault(library =>
            library.Name.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase));
    if (rootLibrary.Value.ValueKind != JsonValueKind.Object)
    {
        throw new InvalidDataException(
            $"The assets target has no root package '{rootPackageId}'.");
    }
    string packageVersion = rootLibrary.Name[rootPrefix.Length..];
    if (string.IsNullOrWhiteSpace(packageVersion))
    {
        throw new InvalidDataException(
            $"The assets target root '{rootLibrary.Name}' has no version.");
    }

    if (rootLibrary.Value.TryGetProperty(
            "frameworkReferences",
            out JsonElement references))
    {
        if (references.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement reference in references.EnumerateArray())
            {
                if (reference.GetString() is string family)
                    families.Add(family);
            }
        }
        else if (references.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty reference in references.EnumerateObject())
                families.Add(reference.Name);
        }
    }

    return new RootTargetEvidence(
        packageVersion,
        families.OrderBy(value => value, StringComparer.Ordinal).ToArray());
}

static void ValidatePinnedRootVersion(
    string packageId,
    string pinnedVersion,
    string selectedVersion)
{
    if (!NuGetVersion.TryParse(
            pinnedVersion,
            out NuGetVersion? parsedPinned)
        || !NuGetVersion.TryParse(
            selectedVersion,
            out NuGetVersion? parsedSelected))
    {
        throw new InvalidDataException(
            $"Package '{packageId}' has an invalid pinned or selected version.");
    }

    if (!parsedPinned.ToNormalizedString().Equals(
            parsedSelected.ToNormalizedString(),
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidDataException(
            $"Package '{packageId}' restored '{selectedVersion}' instead of "
            + $"the exact pin '{pinnedVersion}'.");
    }
}

static void VerifyRootTargetEvidence()
{
    using JsonDocument document = JsonDocument.Parse(
        """
        {
          "targets": {
            "net11.0": {
              "Root.Package/1.0.1": {},
              "Dependency.Package/1.0.0": {
                "frameworkReferences": [
                  "Microsoft.AspNetCore.App"
                ]
              }
            }
          }
        }
        """);
    RootTargetEvidence root = ProjectRootTargetEvidence(
        document.RootElement,
        "net11.0",
        "Root.Package");
    if (root.EligiblePlatformFamilies.Contains(
            AspNetCoreFamily,
            StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "A transitive framework reference admitted the root Platform route.");
    }

    RootTargetEvidence dependency = ProjectRootTargetEvidence(
        document.RootElement,
        "net11.0",
        "Dependency.Package");
    if (!dependency.EligiblePlatformFamilies.Contains(
            AspNetCoreFamily,
            StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "The selected root framework reference was not admitted.");
    }

    ValidatePinnedRootVersion(
        "Root.Package",
        "1.0.1",
        root.PackageVersion);
    try
    {
        ValidatePinnedRootVersion(
            "Root.Package",
            "1.0.0",
            root.PackageVersion);
        throw new InvalidOperationException(
            "An approximated root package version was accepted.");
    }
    catch (InvalidDataException)
    {
    }
}

static async Task<ProcessResult> RunAsync(
    string fileName,
    IReadOnlyList<string> arguments,
    CancellationToken cancellationToken)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    foreach (string argument in arguments)
        startInfo.ArgumentList.Add(argument);
    startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

    using var process = new Process { StartInfo = startInfo };
    if (!process.Start())
        throw new InvalidOperationException($"Could not start '{fileName}'.");
    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
        cancellationToken);
    Task<string> standardError = process.StandardError.ReadToEndAsync(
        cancellationToken);
    await process.WaitForExitAsync(cancellationToken);
    return new ProcessResult(
        process.ExitCode,
        await standardOutput,
        await standardError);
}

static bool TryPositiveInt(
    string? value,
    int defaultValue,
    out int result)
{
    if (value is null)
    {
        result = defaultValue;
        return true;
    }

    return int.TryParse(value, out result) && result > 0;
}

static PackageCoordinate ParseCoordinate(string value)
{
    int separator = value.LastIndexOf('@');
    if (separator <= 0 || separator == value.Length - 1)
    {
        throw new ArgumentException(
            $"Supplemental package '{value}' must use "
            + "package@version[/target-framework] syntax.");
    }

    string versionAndFramework = value[(separator + 1)..];
    int frameworkSeparator = versionAndFramework.LastIndexOf('/');
    string version = frameworkSeparator >= 0
        ? versionAndFramework[..frameworkSeparator]
        : versionAndFramework;
    string targetFramework = frameworkSeparator >= 0
        ? versionAndFramework[(frameworkSeparator + 1)..]
        : TargetFramework;
    if (string.IsNullOrWhiteSpace(version)
        || string.IsNullOrWhiteSpace(targetFramework))
    {
        throw new ArgumentException(
            $"Supplemental package '{value}' must use "
            + "package@version[/target-framework] syntax.");
    }

    return new PackageCoordinate(
        value[..separator],
        version,
        targetFramework);
}

static string? FindRepositoryRoot(string start)
{
    for (DirectoryInfo? directory = new(start);
        directory is not null;
        directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            return directory.FullName;
    }

    return null;
}

static string OneLine(string value)
    => string.Join(
        " ",
        value.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries));

static bool HasNamesakeFilename(
    AssemblyAsset asset,
    AssemblyReferenceIdentity reference)
    => Path.GetFileNameWithoutExtension(asset.Path).Equals(
        reference.Name,
        StringComparison.OrdinalIgnoreCase);

static bool HasNameAffinity(string packageId, string assemblyName)
    => packageId.Equals(
        assemblyName,
        StringComparison.OrdinalIgnoreCase)
        || IsBoundaryPrefix(packageId, assemblyName);

static bool IsBoundaryPrefix(string left, string right)
    => left.StartsWith(
        right + ".",
        StringComparison.OrdinalIgnoreCase)
        || right.StartsWith(
            left + ".",
            StringComparison.OrdinalIgnoreCase);

static bool IsIntrinsicCoreLibrary(string name)
    => name.Equals("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase)
        || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase);

sealed record PackageListEntry(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("package")] string Package,
    [property: JsonPropertyName("downloads")] long Downloads);

sealed record PackagePinFile(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("packages")] IReadOnlyList<PackagePin> Packages);

sealed record PackagePin(
    [property: JsonPropertyName("package")] string Package,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("tfm")] string? Tfm,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("detail")] string? Detail);

sealed record PackageCoordinate(
    string Id,
    string Version,
    string TargetFramework);

sealed record RootTargetEvidence(
    string PackageVersion,
    string[] EligiblePlatformFamilies);

sealed record AssemblyAsset(
    string PackageId,
    string PackageVersion,
    string Path,
    AssemblyReferenceIdentity Identity,
    IReadOnlyList<AssemblyReferenceIdentity> References);

sealed record PlatformAssembly(
    string Family,
    string Path,
    AssemblyReferenceIdentity Identity);

sealed record PlatformCatalog(
    IReadOnlyList<string> Families,
    IReadOnlyList<PlatformAssembly> Assemblies);

sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

enum AssemblyRefClassification
{
    ContextPackageMember,
    ExactNamesakePackage,
    PrefixPackage,
    FilenameOnlyPackage,
    IdentityOnlyPackage,
    Platform,
    IntrinsicCoreLibrary,
    IneligiblePlatform,
    Unresolved,
}

sealed record AssemblyRefObservation(
    int RootRank,
    string RootPackage,
    string SourceAssembly,
    string ReferenceName,
    string? ReferenceVersion,
    AssemblyRefClassification Classification,
    IReadOnlyList<string> MatchingPackageIds,
    IReadOnlyList<string> MatchingPlatformFamilies,
    bool ExactNamesakeCandidateMiss,
    bool PrefixCandidateMiss,
    bool FilenameCandidateIdentityMismatch);

sealed record RootCensusResult(
    int Rank,
    string Package,
    string? Version,
    string TargetFramework,
    string Status,
    string? Detail,
    int ReachablePackageCount,
    int SelectedCompileAssetCount,
    IReadOnlyList<string> Assemblies,
    IReadOnlyList<string> EligiblePlatformFamilies,
    IReadOnlyList<string> AssetFailures,
    IReadOnlyList<AssemblyRefObservation> Observations)
{
    public static RootCensusResult NoLibrary(
        PackageListEntry entry,
        PackagePin pin,
        string targetFramework,
        string detail)
        => new(
            entry.Rank,
            entry.Package,
            pin.Version,
            targetFramework,
            "no-library",
            detail,
            0,
            0,
            [],
            [],
            [],
            []);

    public static RootCensusResult NoCompileAssets(
        PackageListEntry entry,
        PackagePin pin,
        string targetFramework,
        string detail)
        => new(
            entry.Rank,
            entry.Package,
            pin.Version,
            targetFramework,
            "no-compile-assets",
            detail,
            0,
            0,
            [],
            [],
            [],
            []);
}

sealed record CensusFailure(
    int Rank,
    string Package,
    string Kind,
    string Detail);

sealed record CensusSummary(
    int RequestedRoots,
    int RestoredRoots,
    int NoLibraryRoots,
    int NoCompileAssetRoots,
    int FailedRoots,
    int RootAssemblies,
    int SelectedCompileAssets,
    int ReachablePackages,
    int MaximumReachablePackages,
    double AverageReachablePackages,
    int MaximumSelectedCompileAssets,
    double AverageSelectedCompileAssets,
    int AssetDecodeFailures,
    int AssemblyReferences,
    IReadOnlyDictionary<AssemblyRefClassification, int> Classifications,
    int ExactNamesakeWithCompetingPackage,
    int PackageWithPlatformOverlap,
    int FilenameOnlyPackage,
    int IdentityOnlyPackage,
    int ExactNamesakeCandidateMisses,
    int PrefixCandidateMisses,
    int FilenameCandidateIdentityMismatches);

sealed record AssemblyRefSupplierCensusReport(
    int SchemaVersion,
    string CorpusTargetFramework,
    string PackageList,
    string PackagePin,
    string PackagePinSha256,
    IReadOnlyList<string> SupplementalPackages,
    IReadOnlyList<string> PlatformFamilies,
    CensusSummary Summary,
    IReadOnlyList<RootCensusResult> Roots,
    IReadOnlyList<CensusFailure> Failures);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<PackageListEntry>))]
[JsonSerializable(typeof(PackagePinFile))]
[JsonSerializable(typeof(AssemblyRefSupplierCensusReport))]
sealed partial class AssemblyRefSupplierCensusJsonContext :
    JsonSerializerContext;
