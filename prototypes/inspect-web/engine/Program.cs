using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;
using Pipeline = ILInspector.Decompiler.Pipeline;
using Research = ILInspector.Research;

Console.WriteLine("dotnet-inspect browser engine ready");

public sealed record BrowserPackageSurface(
    string Package,
    string Version,
    string[] Frameworks,
    string ActiveFramework,
    BrowserAssemblySurface[] Assemblies,
    BrowserTypeSurface[] Types,
    int TotalMembers);

public sealed record BrowserAssemblySurface(
    string Name,
    string Asset,
    int PublicTypes,
    int PublicMembers);

public sealed record BrowserTypeSurface(
    string Id,
    string Name,
    string Namespace,
    string Kind,
    string Accessibility,
    string Assembly,
    int Members,
    string Signature,
    BrowserMemberSurface[] Api);

public sealed record BrowserMemberSurface(
    string Name,
    string Kind,
    string Signature,
    int? MetadataToken,
    string? ReturnType,
    BrowserParameterSurface[] Parameters,
    string? DocumentationId,
    string? Summary,
    string? Returns,
    BrowserExceptionSurface[] Exceptions,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature);

public sealed record BrowserParameterSurface(
    string Name,
    string Type,
    string? Modifier,
    bool HasDefault,
    string? DefaultValue,
    string? Description);

public sealed record BrowserExceptionSurface(
    string Type,
    string Description);

public sealed record BrowserMemberDocumentation(
    string? Summary,
    string? Returns,
    IReadOnlyDictionary<string, string> Parameters,
    BrowserExceptionSurface[] Exceptions);

public sealed record BrowserMemberSource(
    string Provider,
    string Text,
    string? Url,
    string Provenance);

public sealed record BrowserCallGraph(
    string Mermaid,
    BrowserCallGraphNode Callers,
    BrowserCallGraphNode Callees,
    BrowserCallGraphScope Scope);

public sealed record BrowserCallGraphNode(
    string Label,
    string Status,
    bool InLoop,
    string? Source,
    BrowserCallGraphNode[] Children,
    string Assembly,
    string TypeFullName,
    string MemberName,
    string ParamSig);

public sealed record BrowserCallGraphScope(
    int Packages,
    int Assemblies,
    int CallerAssemblies,
    string CalleeScope);

public sealed record BrowserWorkspacePackage(
    string Package,
    string Version,
    string Framework);

public sealed record BrowserTypeCandidate(
    string Key,
    string Name,
    string Full);

public sealed record BrowserTypeSearchHit(
    string Key,
    string Kind);

public sealed record BrowserMemberFacts(
    BrowserMethodSignals Signals,
    BrowserAllocationFact[] Allocations,
    BrowserCallFact[] Calls,
    BrowserSafetyFact[] Safety,
    BrowserExceptionRegion[] ExceptionRegions,
    BrowserPerformanceOpportunity[] PerformanceOpportunities);

public sealed record BrowserMethodSignals(
    int Allocations,
    int Copies,
    bool Unsafe,
    int Reflection,
    int Throws,
    int Catches,
    int Finallys,
    bool AllocatesInLoop,
    string[] EvidenceOffsets,
    string[] ExceptionTypes);

public sealed record BrowserAllocationFact(
    string Kind,
    string? Type,
    string Offset,
    string Frequency,
    string Multiplicity,
    string Path,
    string Escape,
    bool InLoop,
    int? EstimatedSizeBytes,
    string? Detail);

public sealed record BrowserCallFact(
    string Callee,
    string Offset,
    string Opcode,
    string Kind,
    string Multiplicity,
    bool InLoop,
    bool ExactTarget);

public sealed record BrowserSafetyFact(
    string Kind,
    string? Offset,
    string Detail);

public sealed record BrowserExceptionRegion(
    int Region,
    string Clause,
    string TryRange,
    string HandlerRange,
    string? FilterRange,
    string? CaughtType);

public sealed record BrowserPerformanceOpportunity(
    string Shape,
    string Evidence,
    string Fix,
    string Confidence,
    string? Offset,
    bool InLoop,
    string? Caveat,
    string? Finding,
    string Provenance);

public sealed record BrowserStyleOption(
    string Id,
    string Title,
    string Summary,
    string Tier,
    bool ByteDivergent,
    bool OracleEndorsed,
    string? ConflictGroup);

// Type-level metadata projection — a JSON mirror of ILInspector.Research
// ResearchViews.TypeProjectionResult, the presentation-neutral shared seam.
public sealed record BrowserTypeMetadata(
    string FullName,
    string? Namespace,
    string Name,
    string Kind,
    string[] Modifiers,
    string? Accessibility,
    string? Assembly,
    string? BaseType,
    string[] Interfaces,
    string[] DerivedTypes,
    BrowserTypeParameter[] TypeParameters,
    string[] Attributes,
    string? EnumUnderlyingType,
    BrowserTypeComposition? Composition,
    BrowserTypeGraphNode[] GraphNodes,
    BrowserTypeGraphEdge[] GraphEdges,
    string[] InspectionFailures);

public sealed record BrowserTypeParameter(string Name, string? Variance, string[] Constraints);

public sealed record BrowserTypeComposition(
    int Methods,
    int Properties,
    int Fields,
    int Events,
    int Constructors,
    int Operators,
    int ExplicitInterfaceImplementations,
    int ExtensionMethods,
    int Static,
    int Unsafe,
    int Async,
    int Virtual,
    int Abstract,
    int Override,
    int Extension,
    int Obsolete,
    int Total);

public sealed record BrowserTypeGraphNode(string Id, string DisplayName, string Role);

public sealed record BrowserTypeGraphEdge(string FromId, string ToId, string Kind);

public sealed record BrowserPackageDependencies(
    string Package,
    string Version,
    string ActiveFramework,
    BrowserPackageDependencyGroup[] DependencyGroups,
    BrowserAssemblyReference[] AssemblyReferences);

public sealed record BrowserPackageDependencyGroup(
    string Framework,
    bool IsActive,
    BrowserPackageDependency[] Dependencies);

public sealed record BrowserPackageDependency(string Id, string VersionRange);

public sealed record BrowserAssemblyReference(string Name, string Version);

public sealed record BrowserPackageIntegrations(
    string Package,
    string Version,
    string ActiveFramework,
    BrowserIntegrationCategory[] Categories,
    int TotalSignals,
    string? InspectionError);

public sealed record BrowserIntegrationCategory(
    string Integration,
    int TypeCount,
    int ApiCount,
    BrowserIntegrationSignal[] Signals);

public sealed record BrowserIntegrationSignal(string Kind, string Name, string Shape);

public sealed record BrowserPackageOpportunities(
    string Package,
    string Version,
    string ActiveFramework,
    BrowserOpportunityCategory[] Categories,
    int TotalOpportunities,
    string? InspectionError);

public sealed record BrowserOpportunityCategory(
    string Integration,
    BrowserOpportunityItem[] Items);

public sealed record BrowserOpportunityItem(string Api, string IntegrationType, string LookFor);

public sealed record BrowserPackagePerformance(
    string Package,
    string Version,
    string ActiveFramework,
    BrowserPerfMember[] Members,
    int TotalOpportunities,
    int NonPublicOpportunities,
    string? InspectionError);

public sealed record BrowserPerfMember(
    string Assembly,
    string TypeId,
    string MemberName,
    string MemberSignature,
    int MetadataToken,
    int OpportunityCount,
    int InLoopCount,
    string[] Shapes,
    string Confidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageSurface))]
[JsonSerializable(typeof(BrowserMemberSource))]
[JsonSerializable(typeof(BrowserCallGraph))]
[JsonSerializable(typeof(BrowserMemberDocumentation))]
[JsonSerializable(typeof(BrowserMemberFacts))]
[JsonSerializable(typeof(BrowserTypeMetadata))]
[JsonSerializable(typeof(BrowserPackageDependencies))]
[JsonSerializable(typeof(BrowserPackageIntegrations))]
[JsonSerializable(typeof(BrowserPackageOpportunities))]
[JsonSerializable(typeof(BrowserPackagePerformance))]
[JsonSerializable(typeof(BrowserWorkspacePackage[]))]
[JsonSerializable(typeof(BrowserTypeCandidate[]))]
[JsonSerializable(typeof(BrowserTypeSearchHit[]))]
[JsonSerializable(typeof(BrowserStyleOption[]))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class BrowserJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class BrowserInspectionEngine
{
    static readonly HttpClient Http = new();
    static readonly object PackageCacheLock = new();
    static readonly Dictionary<string, PackageCacheEntry> PackageCache = new(StringComparer.Ordinal);
    const int MaxCachedPackages = 6;
    const long MaxCachedPackageBytes = 64L * 1024 * 1024;
    static long _packageCacheClock;
    static readonly HashSet<string> DownloadedPackages = new(StringComparer.Ordinal);

    sealed record PackageCacheEntry(byte[] Bytes, long LastAccess);

    [JSExport]
    public static async Task<string> QueryPackage(string packageId, string version, string targetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var normalizedId = packageId.ToLowerInvariant();
        var resolvedVersion = await ResolvePackageVersionAsync(normalizedId, version);
        var normalizedVersion = resolvedVersion.ToLowerInvariant();
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);
        using var packageStream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        var candidates = archive.Entries
            .Select(entry => (Entry: entry, Asset: ParseCompileAsset(entry.FullName)))
            .Where(candidate => candidate.Asset is not null)
            .Select(candidate => (candidate.Entry, Asset: candidate.Asset!.Value))
            .ToArray();

        var frameworks = candidates
            .Select(candidate => candidate.Asset.Framework)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(FrameworkPriority)
            .ThenBy(framework => framework, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selectedFramework = string.IsNullOrWhiteSpace(targetFramework)
            ? frameworks.FirstOrDefault() ?? throw new InvalidOperationException("The package has no compile-time assemblies.")
            : frameworks.FirstOrDefault(framework =>
                framework.Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Framework '{targetFramework}' is not present. Available frameworks: {string.Join(", ", frameworks)}.");

        var frameworkCandidates = candidates
            .Where(candidate => candidate.Asset.Framework.Equals(selectedFramework, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var preferredRoot = frameworkCandidates.Any(candidate => candidate.Asset.Root == "ref") ? "ref" : "lib";
        var selectedAssets = frameworkCandidates
            .Where(candidate => candidate.Asset.Root == preferredRoot)
            .OrderBy(candidate => candidate.Entry.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var assemblies = new List<BrowserAssemblySurface>();
        var types = new List<BrowserTypeSurface>();

        foreach (var candidate in selectedAssets)
        {
            await using var entryStream = candidate.Entry.Open();
            using var assemblyStream = new MemoryStream();
            await entryStream.CopyToAsync(assemblyStream);
            var image = assemblyStream.ToArray();

            var reference = new ResolvedAssemblyReference(
                new AssemblyReferenceIdentity(candidate.Entry.Name, null, null, null),
                Path: null,
                OpenRead: () => new MemoryStream(image, writable: false),
                Provenance: candidate.Entry.FullName);

            using var inspection = AssemblyInspectionSession.Open(reference);
            if (!inspection.HasMetadata)
                continue;

            var publicTypes = inspection.ApiSurface().Types
                .Select(type => ToBrowserType(type, candidate.Entry.Name))
                .ToArray();

            // Non-public types (internal/private/protected/…) are excluded from the public
            // surface by design. Pull them in separately so the client can offer an
            // accessibility filter (public by default). Public types keep their public-only
            // member lists from the surface above; the includeAll surface would also expand
            // every public type's members to include private ones, so we take non-public
            // TYPES from it but not the public entries.
            var nonPublicTypes = inspection.ApiSurface(includeAll: true).Types
                .Where(type => !string.IsNullOrWhiteSpace(type.Accessibility))
                .Select(type => ToBrowserType(type, candidate.Entry.Name))
                .ToArray();

            var assemblyTypes = publicTypes
                .Concat(nonPublicTypes)
                .OrderBy(type => type.Namespace, StringComparer.Ordinal)
                .ThenBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();

            assemblies.Add(new BrowserAssemblySurface(
                candidate.Entry.Name,
                candidate.Entry.FullName,
                publicTypes.Length,
                publicTypes.Sum(type => type.Members)));
            types.AddRange(assemblyTypes);
        }

        var duplicateNames = types
            .GroupBy(type => type.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var identifiedTypes = types
            .Select(type => duplicateNames.Contains(type.Id)
                ? type with { Id = $"{type.Assembly}:{type.Id}" }
                : type)
            .OrderBy(type => type.Namespace, StringComparer.Ordinal)
            .ThenBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        var result = new BrowserPackageSurface(
            packageId,
            resolvedVersion,
            frameworks,
            selectedFramework,
            assemblies.ToArray(),
            identifiedTypes,
            identifiedTypes.Where(type => type.Accessibility == "public").Sum(type => type.Members));

        return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserPackageSurface);
    }

    /// <summary>
    /// Ranks loaded type candidates against an incremental query, mirroring the CLI
    /// <c>TypeSearchService</c> find pipeline: exact/namespace-suffix (<see cref="TypeMatcher.Matches"/>),
    /// then prefix and substring globs (<see cref="TypeMatcher.MatchesTypeFilter"/>), then a
    /// Levenshtein "did you mean" fallback (<see cref="TypeMatcher.FindClosest"/>). Highlight spans are
    /// intentionally left to the caller; this method owns ranking only.
    /// </summary>
    [JSExport]
    public static string SearchTypes(string query, string candidatesJson)
    {
        var candidates = JsonSerializer.Deserialize(
                candidatesJson,
                BrowserJsonContext.Default.BrowserTypeCandidateArray)
            ?? [];
        query = query?.Trim() ?? string.Empty;

        if (query.Length == 0)
        {
            var alphabetical = candidates
                .OrderBy(candidate => candidate.Name.Length)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .Select(candidate => new BrowserTypeSearchHit(candidate.Key, "all"))
                .ToArray();
            return JsonSerializer.Serialize(alphabetical, BrowserJsonContext.Default.BrowserTypeSearchHitArray);
        }

        var hits = new List<BrowserTypeSearchHit>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        void AddTier(string kind, Func<BrowserTypeCandidate, bool> predicate)
        {
            var matched = candidates
                .Where(candidate => !used.Contains(candidate.Key) && predicate(candidate))
                .OrderBy(candidate => candidate.Name.Length)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in matched)
            {
                if (used.Add(candidate.Key))
                    hits.Add(new BrowserTypeSearchHit(candidate.Key, kind));
            }
        }

        AddTier("exact", candidate => TypeMatcher.Matches(candidate.Full, query));
        AddTier("prefix", candidate => TypeMatcher.MatchesTypeFilter(candidate.Name, query + "*"));
        AddTier("substring", candidate => TypeMatcher.MatchesTypeFilter(candidate.Name, "*" + query + "*"));
        AddTier("path", candidate => TypeMatcher.MatchesTypeFilter(candidate.Full, "*" + query + "*"));

        var remaining = candidates.Where(candidate => !used.Contains(candidate.Key)).ToList();
        if (remaining.Count > 0)
        {
            var namesToKeys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var candidate in remaining)
            {
                if (!namesToKeys.TryGetValue(candidate.Full, out var keys))
                    namesToKeys[candidate.Full] = keys = new List<string>();
                keys.Add(candidate.Key);
            }

            foreach (var (name, _) in TypeMatcher.FindClosest(namesToKeys.Keys, query, minSimilarity: 0.5, maxResults: 8))
            {
                if (!namesToKeys.TryGetValue(name, out var keys))
                    continue;
                foreach (var key in keys)
                {
                    if (used.Add(key))
                        hits.Add(new BrowserTypeSearchHit(key, "fuzzy"));
                }
            }
        }

        var limited = hits.Take(40).ToArray();
        return JsonSerializer.Serialize(limited, BrowserJsonContext.Default.BrowserTypeSearchHitArray);
    }

    static async Task<string> ResolvePackageVersionAsync(string normalizedId, string? requestedVersion)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion)
            && !requestedVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return requestedVersion;
        }

        var indexUrl =
            $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(normalizedId)}/index.json";
        var indexBytes = await Http.GetByteArrayAsync(indexUrl);
        using var document = JsonDocument.Parse(indexBytes);
        var versions = document.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        return versions.LastOrDefault(candidate => !candidate.Contains('-'))
            ?? throw new InvalidOperationException(
                $"Package '{normalizedId}' has no stable published version. Specify a prerelease version explicitly.");
    }

    // The library-owned StyleOptionCatalog is the single source of truth for the decompiler
    // style knobs ("taste"). Projecting it verbatim keeps the UI data-driven: options added
    // to the catalog surface in the browser without any change here.
    [JSExport]
    public static string PackageCacheStats()
    {
        int packages;
        int resident;
        lock (PackageCacheLock)
        {
            packages = DownloadedPackages.Count;
            resident = PackageCache.Count;
        }

        return $"{{\"packages\":{packages},\"resident\":{resident}}}";
    }

    [JSExport]
    public static string ListStyleOptions()
    {
        var options = new List<BrowserStyleOption>();
        foreach (var descriptor in Pipeline.StyleOptionCatalog.Options)
        {
            // A knob is an axis of values; the default/off value is not selectable taste.
            // Boolean knobs expose one non-default value and keep the descriptor id so
            // stored selections stay stable; multi-value axes expose one option per value
            // and share a conflict group so the client single-selects within the axis.
            var choices = descriptor.Values
                .Where(value => !string.Equals(value.Token, descriptor.DefaultValue, StringComparison.Ordinal))
                .ToArray();
            var multiValue = choices.Length > 1;
            foreach (var value in choices)
            {
                options.Add(new BrowserStyleOption(
                    multiValue ? $"{descriptor.Id}:{value.Token}" : descriptor.Id,
                    multiValue ? $"{descriptor.Title} · {value.Title ?? value.Token}" : descriptor.Title,
                    descriptor.Summary,
                    descriptor.Tier.ToString(),
                    descriptor.ByteDivergent,
                    value.OracleEndorsed,
                    multiValue ? descriptor.Id : null));
            }
        }
        return JsonSerializer.Serialize(options.ToArray(), BrowserJsonContext.Default.BrowserStyleOptionArray);
    }

    // Builds PrinterOptions from a JSON array of enabled option ids by single-selecting the
    // chosen value on each descriptor's axis — no knob-specific code, so new taste options
    // flow through automatically.
    static Pipeline.PrinterOptions BuildPrinterOptions(string? styleOptionsJson)
    {
        var options = Pipeline.PrinterOptions.Default;
        if (string.IsNullOrWhiteSpace(styleOptionsJson))
            return options;

        string[]? ids;
        try { ids = JsonSerializer.Deserialize(styleOptionsJson, BrowserJsonContext.Default.StringArray); }
        catch (JsonException) { return options; }
        if (ids is not { Length: > 0 })
            return options;

        var enabled = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (var descriptor in Pipeline.StyleOptionCatalog.Options)
        {
            var choices = descriptor.Values
                .Where(value => !string.Equals(value.Token, descriptor.DefaultValue, StringComparison.Ordinal))
                .ToArray();
            var multiValue = choices.Length > 1;
            foreach (var value in choices)
            {
                var id = multiValue ? $"{descriptor.Id}:{value.Token}" : descriptor.Id;
                if (enabled.Contains(id))
                {
                    options = descriptor.WithValue(options, value.Token);
                    break;
                }
            }
        }
        return options;
    }

    [JSExport]
    public static async Task<string> QueryMemberSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string memberName,
        string memberSignature,
        string styleOptionsJson)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var implementationPath = await MaterializeImplementationAsync(
                normalizedId, normalizedVersion, targetFramework, assemblyName, tempRoot, allowRefFallback: true);

            var pdbPath = Path.ChangeExtension(implementationPath, ".pdb");
            var symbolPackageUrl =
                $"https://globalcdn.nuget.org/symbol-packages/" +
                $"{Uri.EscapeDataString(normalizedId)}.{Uri.EscapeDataString(normalizedVersion)}.snupkg";
            await TryAcquirePackagePdbAsync(symbolPackageUrl, targetFramework, assemblyName, pdbPath);

            using var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
                new AssemblyReferenceIdentity(assemblyName, null, null, null),
                implementationPath,
                () => File.OpenRead(implementationPath),
                Provenance: $"lib/{targetFramework}/{assemblyName}"));
            var type = inspection.ApiSurface().Types.FirstOrDefault(candidate =>
                candidate.FullName.Equals(typeId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Type '{typeId}' is not in the implementation assembly.");
            var member = type.Members.FirstOrDefault(candidate =>
                candidate.Name.Equals(memberName, StringComparison.Ordinal)
                && string.Equals(candidate.Signature, memberSignature, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"The selected overload '{memberSignature}' was not found.");

            if (File.Exists(pdbPath)
                && member.MetadataToken is int token
                && await TryGetAuthoredSourceAsync(
                    implementationPath,
                    type,
                    member,
                    token) is { } authored)
            {
                return JsonSerializer.Serialize(authored, BrowserJsonContext.Default.BrowserMemberSource);
            }

            var decompiled = MemberBodyProducer.ProduceMember(type, member, implementationPath, File.Exists(pdbPath) ? pdbPath : null, printerOptions: BuildPrinterOptions(styleOptionsJson));
            if (decompiled.Text is not { Length: > 0 } text)
                throw new InvalidOperationException("Authored source was unavailable and decompilation did not produce source.");

            return JsonSerializer.Serialize(
                new BrowserMemberSource(
                    "decompiled",
                    text,
                    null,
                    $"Decompiled by dotnet-inspect from lib/{targetFramework}/{assemblyName}"),
                BrowserJsonContext.Default.BrowserMemberSource);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    // The one IL-inclusive member view: the Research annotated projection raises the member
    // to C# with hidden-fact comments and interleaves the raw IL beneath each statement. This
    // is a separate pipeline from QueryMemberSource (which renders clean decompiled or authored
    // C#) and from QueryMemberFacts (which reads LibraryBodyIndex signals).
    [JSExport]
    public static async Task<string> QueryMemberAnnotatedSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string memberName,
        string memberSignature,
        string styleOptionsJson)
    {
        _ = styleOptionsJson;
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var implementationPath = await MaterializeImplementationAsync(
                normalizedId, normalizedVersion, targetFramework, assemblyName, tempRoot, allowRefFallback: false);

            var pdbPath = Path.ChangeExtension(implementationPath, ".pdb");
            var symbolPackageUrl =
                $"https://globalcdn.nuget.org/symbol-packages/" +
                $"{Uri.EscapeDataString(normalizedId)}.{Uri.EscapeDataString(normalizedVersion)}.snupkg";
            await TryAcquirePackagePdbAsync(symbolPackageUrl, targetFramework, assemblyName, pdbPath);

            using var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
                new AssemblyReferenceIdentity(assemblyName, null, null, null),
                implementationPath,
                () => File.OpenRead(implementationPath),
                Provenance: $"lib/{targetFramework}/{assemblyName}"));
            var type = inspection.ApiSurface().Types.FirstOrDefault(candidate =>
                candidate.FullName.Equals(typeId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Type '{typeId}' is not in the implementation assembly.");
            var member = type.Members.FirstOrDefault(candidate =>
                candidate.Name.Equals(memberName, StringComparison.Ordinal)
                && string.Equals(candidate.Signature, memberSignature, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"The selected overload '{memberSignature}' was not found.");
            if (member.MetadataToken is not int token)
                throw new InvalidOperationException("The selected member has no method body identity.");

            var resolver = Pipeline.MetadataSource.DefaultAssemblyReferenceResolver(implementationPath);
            using var source = Pipeline.MetadataSource.Open(
                implementationPath,
                File.Exists(pdbPath) ? pdbPath : null,
                resolver);

            var projection = Research.ResearchViews.ProjectMember(new Research.ResearchViews.MemberProjectionRequest(
                source,
                type.FullName,
                member.Name,
                PublicOnly: false,
                AnnotatedSource: true,
                MethodToken: token));

            var annotated = projection.AnnotatedSource;
            if (annotated?.Output is not { Length: > 0 } text)
            {
                var diagnostic = annotated?.Diagnostics is { Count: > 0 } diagnostics ? diagnostics[0] : (DecompilerDiagnostic?)null;
                throw new InvalidOperationException(diagnostic is { } d
                    ? $"Annotated source projection failed ({d.Id}): {d.Message}"
                    : "Annotated source projection produced no output.");
            }

            return JsonSerializer.Serialize(
                new BrowserMemberSource(
                    "annotated",
                    text,
                    null,
                    $"Annotated by dotnet-inspect from lib/{targetFramework}/{assemblyName}"),
                BrowserJsonContext.Default.BrowserMemberSource);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    // Projects type-level metadata (identity, shape, generic parameters, base/interfaces/
    // derived relationships, attributes, and aggregate member composition) through the shared
    // ILInspector.Research ProjectType seam — the same presentation-neutral view the CLI
    // consumes — so the web Metadata section never reimplements type-fact composition.
    [JSExport]
    public static async Task<string> QueryTypeProjection(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var implementationPath = await MaterializeImplementationAsync(
                normalizedId, normalizedVersion, targetFramework, assemblyName, tempRoot, allowRefFallback: false);

            var resolver = Pipeline.MetadataSource.DefaultAssemblyReferenceResolver(implementationPath);
            using var source = Pipeline.MetadataSource.Open(implementationPath, null, resolver);

            var projection = Research.ResearchViews.ProjectType(
                new Research.ResearchViews.TypeProjectionRequest(source, typeId));

            var result = new BrowserTypeMetadata(
                projection.Identity.FullName,
                projection.Identity.Namespace,
                projection.Identity.Name,
                projection.Identity.Kind,
                [.. projection.Identity.Modifiers],
                projection.Identity.Accessibility,
                projection.Identity.Assembly,
                projection.BaseType,
                [.. projection.Interfaces],
                [.. projection.DerivedTypes],
                projection.TypeParameters
                    .Select(parameter => new BrowserTypeParameter(
                        parameter.Name, parameter.Variance, [.. parameter.Constraints]))
                    .ToArray(),
                [.. projection.Attributes],
                projection.EnumUnderlyingType,
                projection.Composition is { } composition
                    ? new BrowserTypeComposition(
                        composition.Methods,
                        composition.Properties,
                        composition.Fields,
                        composition.Events,
                        composition.Constructors,
                        composition.Operators,
                        composition.ExplicitInterfaceImplementations,
                        composition.ExtensionMethods,
                        composition.Static,
                        composition.Unsafe,
                        composition.Async,
                        composition.Virtual,
                        composition.Abstract,
                        composition.Override,
                        composition.Extension,
                        composition.Obsolete,
                        composition.Total)
                    : null,
                projection.Graph?.Nodes
                    .Select(node => new BrowserTypeGraphNode(
                        node.Id, node.DisplayName, node.Role.ToString().ToLowerInvariant()))
                    .ToArray() ?? [],
                projection.Graph?.Edges
                    .Select(edge => new BrowserTypeGraphEdge(
                        edge.FromId, edge.ToId, edge.Kind.ToString().ToLowerInvariant()))
                    .ToArray() ?? [],
                projection.InspectionFailures
                    .Select(failure => $"{failure.Operation}: {failure.Detail}")
                    .ToArray());

            return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserTypeMetadata);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    // Projects package-scoped dependency evidence: the NuGet .nuspec dependency groups
    // (per target framework, kept as-declared so the reality of "no group for this exact
    // TFM" stays visible) plus the referenced assemblies read straight from the active
    // framework's implementation assembly. The .nuspec is untrusted feed content, so it is
    // parsed with DTD processing prohibited to block XXE and entity-expansion attacks.
    [JSExport]
    public static async Task<string> QueryPackageDependencies(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);

        var groups = new List<BrowserPackageDependencyGroup>();
        string packageName = packageId;
        string packageVersion = version;
        var assemblyReferences = new List<BrowserAssemblyReference>();

        using (var stream = new MemoryStream(packageBytes, writable: false))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            var nuspec = archive.Entries.FirstOrDefault(entry =>
                !entry.FullName.Contains('/') && entry.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspec is not null)
            {
                using var nuspecStream = nuspec.Open();
                var readerSettings = new System.Xml.XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 1024
                };
                using var xmlReader = System.Xml.XmlReader.Create(nuspecStream, readerSettings);
                var document = XDocument.Load(xmlReader, LoadOptions.None);

                var metadata = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
                packageName = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value ?? packageId;
                packageVersion = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "version")?.Value ?? version;

                var dependencies = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "dependencies");
                if (dependencies is not null)
                {
                    var groupElements = dependencies.Elements().Where(e => e.Name.LocalName == "group").ToList();
                    if (groupElements.Count > 0)
                    {
                        foreach (var group in groupElements)
                        {
                            var tfm = NormalizeFrameworkMoniker(group.Attribute("targetFramework")?.Value ?? "any");
                            groups.Add(new BrowserPackageDependencyGroup(
                                tfm,
                                string.Equals(tfm, targetFramework, StringComparison.OrdinalIgnoreCase),
                                ReadDependencies(group)));
                        }
                    }
                    else
                    {
                        var flat = ReadDependencies(dependencies);
                        if (flat.Length > 0)
                            groups.Add(new BrowserPackageDependencyGroup("any", true, flat));
                    }
                }
            }

            var implementation = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.Equals($"lib/{targetFramework}/{assemblyName}", StringComparison.OrdinalIgnoreCase));
            if (implementation is not null)
            {
                using var assemblyStream = implementation.Open();
                using var buffer = new MemoryStream();
                await assemblyStream.CopyToAsync(buffer);
                buffer.Position = 0;
                try
                {
                    using var peReader = new System.Reflection.PortableExecutable.PEReader(buffer);
                    if (peReader.HasMetadata)
                    {
                        var reader = peReader.GetMetadataReader();
                        foreach (var handle in reader.AssemblyReferences)
                        {
                            var reference = reader.GetAssemblyReference(handle);
                            var name = reader.GetString(reference.Name);
                            if (!string.IsNullOrEmpty(name))
                                assemblyReferences.Add(new BrowserAssemblyReference(name, reference.Version.ToString()));
                        }
                    }
                }
                catch
                {
                    // A malformed assembly should not sink the whole dependency view; the
                    // nuspec groups still render. Leave assemblyReferences empty.
                }
            }
        }

        var distinctReferences = assemblyReferences
            .GroupBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new BrowserPackageDependencies(
            packageName,
            packageVersion,
            targetFramework,
            [.. groups],
            distinctReferences);

        return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserPackageDependencies);
    }

    private static BrowserPackageDependency[] ReadDependencies(XElement container) =>
        container.Elements()
            .Where(e => e.Name.LocalName == "dependency")
            .Select(dependency => new BrowserPackageDependency(
                dependency.Attribute("id")?.Value ?? "",
                dependency.Attribute("version")?.Value ?? ""))
            .Where(dependency => dependency.Id.Length > 0)
            .ToArray();

    // Scans every implementation assembly under the active framework for ecosystem
    // integration signals (DI, logging, OpenTelemetry, ASP.NET Core, AI, Aspire, …) using
    // the shared SRM-only EcosystemIntegrationScanner, then groups the signals by
    // integration. A per-assembly decode failure is recorded in InspectionError rather than
    // silently dropped, so a partial or empty result stays visible as such.
    [JSExport]
    public static async Task<string> QueryPackageIntegrations(
        string packageId,
        string version,
        string targetFramework)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);

        var signals = new List<EcosystemIntegrationSignalInfo>();
        var failures = new List<string>();

        using (var stream = new MemoryStream(packageBytes, writable: false))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            var prefix = $"lib/{targetFramework}/";
            var assemblies = archive.Entries
                .Where(entry =>
                    entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var entry in assemblies)
            {
                try
                {
                    using var assemblyStream = entry.Open();
                    using var buffer = new MemoryStream();
                    await assemblyStream.CopyToAsync(buffer);
                    buffer.Position = 0;
                    using var peReader = new System.Reflection.PortableExecutable.PEReader(buffer);
                    signals.AddRange(EcosystemIntegrationScanner.Scan(peReader));
                }
                catch (Exception exception)
                {
                    failures.Add($"{entry.Name}: {exception.Message}");
                }
            }
        }

        var categories = signals
            .GroupBy(signal => signal.Integration, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var distinct = group
                    .GroupBy(signal => (signal.Shape, signal.Kind, signal.Name))
                    .Select(inner => inner.First())
                    .OrderBy(signal => signal.Shape, StringComparer.Ordinal)
                    .ThenBy(signal => signal.Name, StringComparer.Ordinal)
                    .Select(signal => new BrowserIntegrationSignal(signal.Kind, signal.Name, signal.Shape))
                    .ToArray();
                var typeCount = distinct.Count(signal =>
                    signal.Shape.Equals(IntegrationSignalShape.Type, StringComparison.Ordinal));
                return new BrowserIntegrationCategory(
                    group.Key,
                    typeCount,
                    distinct.Length - typeCount,
                    distinct);
            })
            .ToArray();

        var result = new BrowserPackageIntegrations(
            packageId,
            version,
            targetFramework,
            categories,
            categories.Sum(category => category.Signals.Length),
            failures.Count > 0 ? string.Join("; ", failures) : null);

        return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserPackageIntegrations);
    }

    // Integration opportunities are the complement of the Integrations lens: types on the
    // public surface that suggest an ecosystem area (auth, cloud clients, configuration,
    // database, AI clients) the package does not yet integrate with. The set of integrations
    // it already ships is computed first (union across all active-framework assemblies) so a
    // package that already covers an area is not flagged for it.
    [JSExport]
    public static async Task<string> QueryPackageOpportunities(
        string packageId,
        string version,
        string targetFramework)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);

        var opportunities = new Dictionary<string, IntegrationOpportunityInfo>(StringComparer.Ordinal);
        var failures = new List<string>();

        using (var stream = new MemoryStream(packageBytes, writable: false))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            var prefix = $"lib/{targetFramework}/";
            var assemblyBytes = new List<byte[]>();
            foreach (var entry in archive.Entries.Where(entry =>
                entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var assemblyStream = entry.Open();
                    using var buffer = new MemoryStream();
                    await assemblyStream.CopyToAsync(buffer);
                    assemblyBytes.Add(buffer.ToArray());
                }
                catch (Exception exception)
                {
                    failures.Add($"{entry.Name}: {exception.Message}");
                }
            }

            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bytes in assemblyBytes)
            {
                try
                {
                    using var peReader = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes, writable: false));
                    foreach (var signal in EcosystemIntegrationScanner.Scan(peReader))
                        existing.Add(signal.Integration);
                }
                catch (Exception exception)
                {
                    failures.Add(exception.Message);
                }
            }

            foreach (var bytes in assemblyBytes)
            {
                try
                {
                    using var peReader = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes, writable: false));
                    foreach (var opportunity in IntegrationOpportunityScanner.Scan(peReader, existing))
                    {
                        var key = $"{opportunity.Integration}|{opportunity.Api}|{opportunity.IntegrationType}";
                        opportunities.TryAdd(key, opportunity);
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception.Message);
                }
            }
        }

        var categories = opportunities.Values
            .GroupBy(opportunity => opportunity.Integration, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new BrowserOpportunityCategory(
                group.Key,
                group
                    .OrderBy(opportunity => opportunity.Api, StringComparer.Ordinal)
                    .Select(opportunity => new BrowserOpportunityItem(
                        opportunity.Api, opportunity.IntegrationType, opportunity.LookFor))
                    .ToArray()))
            .ToArray();

        var result = new BrowserPackageOpportunities(
            packageId,
            version,
            targetFramework,
            categories,
            categories.Sum(category => category.Items.Length),
            failures.Count > 0 ? string.Join("; ", failures) : null);

        return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserPackageOpportunities);
    }

    // Ranks the package's public members by the allocation/performance opportunities the
    // Analysis layer classifies over their method bodies. The whole-assembly LibraryBodyIndex
    // pass computes opportunities once; they are joined back to public API members by method
    // token so each ranked row drills to its member. Opportunities in non-public members are
    // counted (NonPublicOpportunities) rather than dropped, so their absence stays visible.
    [JSExport]
    public static async Task<string> QueryPackagePerformance(
        string packageId,
        string version,
        string targetFramework)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-perf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var members = new List<BrowserPerfMember>();
        var failures = new List<string>();
        var totalOpportunities = 0;
        var nonPublicOpportunities = 0;

        try
        {
            var assemblyNames = new List<string>();
            using (var stream = new MemoryStream(packageBytes, writable: false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries.Where(entry =>
                    entry.FullName.StartsWith($"lib/{targetFramework}/", StringComparison.OrdinalIgnoreCase) &&
                    entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    await WriteEntryAsync(entry, Path.Combine(tempRoot, entry.Name));
                    if (!assemblyNames.Contains(entry.Name))
                        assemblyNames.Add(entry.Name);
                }
            }

            foreach (var assemblyName in assemblyNames)
            {
                var assemblyPath = Path.Combine(tempRoot, assemblyName);
                try
                {
                    var tokenMap = new Dictionary<int, (string TypeId, string Name, string Signature)>();
                    using (var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
                        new AssemblyReferenceIdentity(assemblyName, null, null, null),
                        assemblyPath,
                        () => File.OpenRead(assemblyPath),
                        Provenance: $"lib/{targetFramework}/{assemblyName}")))
                    {
                        foreach (var type in inspection.ApiSurface().Types)
                        {
                            foreach (var member in type.Members)
                            {
                                if (member.MetadataToken is int memberToken)
                                    tokenMap[memberToken] = (type.FullName, member.Name, member.Signature ?? "");
                            }
                        }
                    }

                    var index = Analysis.LibraryBodyIndex.Open(
                        assemblyPath,
                        Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities);

                    foreach (var group in index.OptimizationOpportunities.GroupBy(opportunity => opportunity.Method.MetadataToken))
                    {
                        var count = group.Count();
                        totalOpportunities += count;
                        if (!tokenMap.TryGetValue(group.Key, out var target))
                        {
                            nonPublicOpportunities += count;
                            continue;
                        }

                        var shapes = group
                            .Select(opportunity => opportunity.Shape)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(shape => shape, StringComparer.Ordinal)
                            .ToArray();
                        var confidence = group
                            .Select(opportunity => opportunity.Confidence)
                            .OrderByDescending(RankConfidence)
                            .FirstOrDefault() ?? "";

                        members.Add(new BrowserPerfMember(
                            assemblyName,
                            target.TypeId,
                            target.Name,
                            target.Signature,
                            group.Key,
                            count,
                            group.Count(opportunity => opportunity.InLoop),
                            shapes,
                            confidence));
                    }
                }
                catch (Exception exception)
                {
                    failures.Add($"{assemblyName}: {exception.Message}");
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* Best-effort scratch cleanup. */ }
        }

        var ranked = members
            .OrderByDescending(member => member.InLoopCount)
            .ThenByDescending(member => member.OpportunityCount)
            .ThenBy(member => member.TypeId, StringComparer.Ordinal)
            .ThenBy(member => member.MemberSignature, StringComparer.Ordinal)
            .Take(200)
            .ToArray();

        var result = new BrowserPackagePerformance(
            packageId,
            version,
            targetFramework,
            ranked,
            totalOpportunities,
            nonPublicOpportunities,
            failures.Count > 0 ? string.Join("; ", failures) : null);

        return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserPackagePerformance);
    }

    private static int RankConfidence(string confidence) => confidence?.ToLowerInvariant() switch
    {
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };

    // Collapses long .NET framework monikers (".NETStandard,Version=v2.0") to the short
    // folder form the lib/ layout and the UI use ("netstandard2.0"); short forms pass
    // through unchanged.
    private static string NormalizeFrameworkMoniker(string moniker)
    {
        if (string.IsNullOrWhiteSpace(moniker) || !moniker.StartsWith('.'))
            return moniker;

        // Two long forms appear in nuspec groups: the comma form
        // (".NETFramework,Version=v4.6.2") and the compact form (".NETFramework4.6.2").
        string family;
        string version;
        var comma = moniker.IndexOf(',');
        if (comma >= 0)
        {
            family = moniker[..comma];
            var versionMarker = moniker.IndexOf("Version=v", comma, StringComparison.OrdinalIgnoreCase);
            version = versionMarker < 0 ? "" : moniker[(versionMarker + "Version=v".Length)..];
        }
        else
        {
            var firstDigit = -1;
            for (var index = 0; index < moniker.Length; index++)
            {
                if (char.IsDigit(moniker[index])) { firstDigit = index; break; }
            }
            if (firstDigit < 0)
                return moniker;
            family = moniker[..firstDigit];
            version = moniker[firstDigit..];
        }

        var prefix = family switch
        {
            ".NETStandard" => "netstandard",
            ".NETCoreApp" => "net",
            ".NETFramework" => "net",
            _ => null
        };
        if (prefix is null)
            return moniker;

        if (family == ".NETFramework")
            version = version.Replace(".", "");
        else if (!version.Contains('.'))
            version += ".0";

        return prefix + version;
    }

    // Decompiles a member the caller only knows by declaring type and name — used by the
    // call graph, whose compact node labels reach non-public members that never appear on
    // the public API surface. Resolution uses the full surface (includeAll) so private,
    // internal, and implementation-detail members of a loaded assembly are navigable, while
    // the public type list stays public-only.
    [JSExport]
    public static async Task<string> QueryTypeMemberSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeName,
        string memberName,
        string styleOptionsJson)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var implementationPath = await MaterializeImplementationAsync(
                normalizedId, normalizedVersion, targetFramework, assemblyName, tempRoot, allowRefFallback: true);

            var pdbPath = Path.ChangeExtension(implementationPath, ".pdb");
            var symbolPackageUrl =
                $"https://globalcdn.nuget.org/symbol-packages/" +
                $"{Uri.EscapeDataString(normalizedId)}.{Uri.EscapeDataString(normalizedVersion)}.snupkg";
            await TryAcquirePackagePdbAsync(symbolPackageUrl, targetFramework, assemblyName, pdbPath);

            using var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
                new AssemblyReferenceIdentity(assemblyName, null, null, null),
                implementationPath,
                () => File.OpenRead(implementationPath),
                Provenance: $"lib/{targetFramework}/{assemblyName}"));
            var surface = inspection.ApiSurface(includeAll: true);
            bool DeclaresMember(ApiType candidate) =>
                candidate.Members.Any(m => m.Name.Equals(memberName, StringComparison.Ordinal));
            // The compact call-graph label strips generic arity, so a caller may pass
            // "JsonTypeInfo" for a node whose real declaring type is the generic
            // "JsonTypeInfo`1"; that simple name also collides with a same-named
            // non-generic type. Match on the full name, the simple name, and their
            // arity-stripped forms, then prefer the candidate that actually declares the
            // member so the arity collision resolves to the type that owns it.
            var candidates = surface.Types.Where(candidate =>
                candidate.FullName.Equals(typeName, StringComparison.Ordinal)
                || candidate.Name.Equals(typeName, StringComparison.Ordinal)
                || StripGenericArity(candidate.FullName).Equals(typeName, StringComparison.Ordinal)
                || StripGenericArity(candidate.Name).Equals(typeName, StringComparison.Ordinal)).ToArray();
            var type = candidates.FirstOrDefault(DeclaresMember)
                ?? candidates.FirstOrDefault()
                ?? throw new InvalidOperationException($"Type '{typeName}' is not in {assemblyName}.");
            var member = type.Members.FirstOrDefault(candidate =>
                candidate.Name.Equals(memberName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Member '{memberName}' was not found on '{type.FullName}'.");

            if (File.Exists(pdbPath)
                && member.MetadataToken is int token
                && await TryGetAuthoredSourceAsync(implementationPath, type, member, token) is { } authored)
            {
                return JsonSerializer.Serialize(authored, BrowserJsonContext.Default.BrowserMemberSource);
            }

            var decompiled = MemberBodyProducer.ProduceMember(type, member, implementationPath, File.Exists(pdbPath) ? pdbPath : null, printerOptions: BuildPrinterOptions(styleOptionsJson));
            if (decompiled.Text is not { Length: > 0 } text)
                throw new InvalidOperationException("Decompilation did not produce source for the selected member.");

            return JsonSerializer.Serialize(
                new BrowserMemberSource(
                    "decompiled",
                    text,
                    null,
                    $"Decompiled by dotnet-inspect from lib/{targetFramework}/{assemblyName}"),
                BrowserJsonContext.Default.BrowserMemberSource);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    [JSExport]
    public static async Task<string> QueryTypeSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string styleOptionsJson)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var implementationPath = await MaterializeImplementationAsync(
                normalizedId, normalizedVersion, targetFramework, assemblyName, tempRoot, allowRefFallback: true);

            var pdbPath = Path.ChangeExtension(implementationPath, ".pdb");
            var symbolPackageUrl =
                $"https://globalcdn.nuget.org/symbol-packages/" +
                $"{Uri.EscapeDataString(normalizedId)}.{Uri.EscapeDataString(normalizedVersion)}.snupkg";
            await TryAcquirePackagePdbAsync(symbolPackageUrl, targetFramework, assemblyName, pdbPath);

            using var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
                new AssemblyReferenceIdentity(assemblyName, null, null, null),
                implementationPath,
                () => File.OpenRead(implementationPath),
                Provenance: $"lib/{targetFramework}/{assemblyName}"));
            var type = inspection.ApiSurface().Types.FirstOrDefault(candidate =>
                candidate.FullName.Equals(typeId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Type '{typeId}' is not in the implementation assembly.");

            var listing = MemberBodyProducer.Project(
                type,
                implementationPath,
                File.Exists(pdbPath) ? pdbPath : null,
                printerOptions: BuildPrinterOptions(styleOptionsJson));
            if (listing.Output is not { Length: > 0 } text)
                throw new InvalidOperationException("Whole-type decompilation did not produce source.");

            return JsonSerializer.Serialize(
                new BrowserMemberSource(
                    "decompiled",
                    text,
                    null,
                    $"Decompiled by dotnet-inspect from lib/{targetFramework}/{assemblyName}"),
                BrowserJsonContext.Default.BrowserMemberSource);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    [JSExport]
    public static async Task<string> QueryMemberCallGraph(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string memberName,
        string memberSignature,
        string workspaceJson)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var workspace = JsonSerializer.Deserialize(
                    workspaceJson,
                    BrowserJsonContext.Default.BrowserWorkspacePackageArray)
                ?? [];
            if (!workspace.Any(package =>
                package.Package.Equals(packageId, StringComparison.OrdinalIgnoreCase)
                && package.Version.Equals(version, StringComparison.OrdinalIgnoreCase)))
            {
                workspace =
                [
                    .. workspace,
                    new BrowserWorkspacePackage(packageId, version, targetFramework)
                ];
            }

            var workspaceAssemblies = new List<(string Package, string Version, string Path)>();
            string? implementationPath = null;
            for (int packageIndex = 0; packageIndex < workspace.Length; packageIndex++)
            {
                var package = workspace[packageIndex];
                var bytes = package.Package.Equals(packageId, StringComparison.OrdinalIgnoreCase)
                    && package.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
                    ? packageBytes
                    : await GetPackageBytesAsync(
                        package.Package.ToLowerInvariant(),
                        package.Version.ToLowerInvariant());
                var packageDirectory = Path.Combine(tempRoot, $"package-{packageIndex}");
                Directory.CreateDirectory(packageDirectory);
                using var packageStream = new MemoryStream(bytes, writable: false);
                using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries.Where(entry =>
                    entry.FullName.StartsWith($"lib/{package.Framework}/", StringComparison.OrdinalIgnoreCase)
                    && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    var path = Path.Combine(packageDirectory, entry.Name);
                    await WriteEntryAsync(entry, path);
                    workspaceAssemblies.Add((package.Package, package.Version, path));
                    if (package.Package.Equals(packageId, StringComparison.OrdinalIgnoreCase)
                        && package.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
                        && entry.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        implementationPath = path;
                    }
                }
            }

            if (implementationPath is null || !File.Exists(implementationPath))
                throw new InvalidOperationException($"No implementation asset for {assemblyName} at {targetFramework}.");

            using var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
                new AssemblyReferenceIdentity(assemblyName, null, null, null),
                implementationPath,
                () => File.OpenRead(implementationPath),
                Provenance: $"lib/{targetFramework}/{assemblyName}"));
            var type = inspection.ApiSurface().Types.FirstOrDefault(candidate =>
                candidate.FullName.Equals(typeId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Type '{typeId}' is not in the implementation assembly.");
            var member = type.Members.FirstOrDefault(candidate =>
                candidate.Name.Equals(memberName, StringComparison.Ordinal)
                && string.Equals(candidate.Signature, memberSignature, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"The selected overload '{memberSignature}' was not found.");
            if (member.MetadataToken is not int token)
                throw new InvalidOperationException("The selected member has no method body identity.");

            var index = Analysis.LibraryBodyIndex.Open(
                implementationPath,
                Analysis.LibraryBodyAnalysisFeatures.MethodEvidence);
            var callerScopes = workspaceAssemblies
                .Where(assembly => !Path.GetFullPath(assembly.Path)
                    .Equals(Path.GetFullPath(implementationPath), StringComparison.OrdinalIgnoreCase))
                .Select(assembly => Analysis.LibraryBodyIndex.Open(
                    assembly.Path,
                    Analysis.LibraryBodyAnalysisFeatures.MethodEvidence))
                .ToArray();
            var callers = index.BuildCallerTree(token, maxDepth: 2, maxNodes: 30);
            if (callerScopes.Length > 0)
                callers = index.BuildCallerTree(token, callerScopes, maxDepth: 2, maxNodes: 30);
            var callees = index.BuildCallTree(token, maxDepth: 2, maxNodes: 30);
            var result = new BrowserCallGraph(
                CallGraphMermaid.Render(
                    callers,
                    callees,
                    new CallGraphMermaid.Options(CompactLabels: true, RelationshipColors: true)),
                ToBrowserCallNode(callers),
                ToBrowserCallNode(callees),
                new BrowserCallGraphScope(
                    workspace.Length,
                    workspaceAssemblies.Count,
                    callerScopes.Length + 1,
                    assemblyName));
            return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserCallGraph);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    [JSExport]
    public static async Task<string> QueryMemberFacts(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string memberName,
        string memberSignature)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-facts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var implementationPath = await MaterializeImplementationAsync(
                normalizedId, normalizedVersion, targetFramework, assemblyName, tempRoot, allowRefFallback: false);

            using var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
                new AssemblyReferenceIdentity(assemblyName, null, null, null),
                implementationPath,
                () => File.OpenRead(implementationPath),
                Provenance: $"lib/{targetFramework}/{assemblyName}"));
            var type = inspection.ApiSurface().Types.FirstOrDefault(candidate =>
                candidate.FullName.Equals(typeId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Type '{typeId}' is not in the implementation assembly.");
            var member = type.Members.FirstOrDefault(candidate =>
                candidate.Name.Equals(memberName, StringComparison.Ordinal)
                && string.Equals(candidate.Signature, memberSignature, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"The selected overload '{memberSignature}' was not found.");
            if (member.MetadataToken is not int token)
                throw new InvalidOperationException("The selected member has no method body identity.");

            var index = Analysis.LibraryBodyIndex.Open(
                implementationPath,
                Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities,
                bodyScope: new HashSet<int> { token });
            var signals = index.GetMethodSignals().GetValueOrDefault(token, Analysis.MethodSignals.None);
            index.GetAllocationOccurrences().TryGetValue(token, out var allocations);
            index.GetDirectCallsByCaller().TryGetValue(token, out var calls);
            index.GetUnsafetyOccurrences().TryGetValue(token, out var unsafeOperations);
            index.GetUnsafeEvidenceByMember().TryGetValue(token, out var unsafeEvidence);

            using var context = PdbContext.Open(implementationPath);
            var regions = context.ResolveExceptionRegions(token, out var regionError);
            if (regionError is not null && regions.Count == 0
                && index.Methods.Any(method => method.MetadataToken == token))
            {
                throw new InvalidOperationException(regionError);
            }

            var result = new BrowserMemberFacts(
                new BrowserMethodSignals(
                    signals.Allocations,
                    signals.Copies,
                    signals.Unsafe,
                    signals.Reflection,
                    signals.Throws,
                    signals.Catches,
                    signals.Finallys,
                    signals.AllocInLoop,
                    signals.Evidence.Select(FormatOffset).ToArray(),
                    signals.ExceptionTypes.ToArray()),
                (allocations.IsDefault ? [] : allocations)
                    .Select(allocation => new BrowserAllocationFact(
                        allocation.Kind.ToString(),
                        allocation.AllocatedType?.ToDisplayString() ?? allocation.RuntimeAllocationType,
                        FormatOffset(allocation.ILOffset),
                        allocation.Frequency.ToString(),
                        allocation.Multiplicity.ToString(),
                        allocation.PathContext.ToString(),
                        allocation.EscapeKind != Analysis.AllocationEscapeKind.None
                            ? allocation.EscapeKind.ToString()
                            : allocation.Escape.ToString(),
                        allocation.InLoop,
                        allocation.EstimatedSizeBytes,
                        allocation.Detail))
                    .ToArray(),
                (calls.IsDefault ? [] : calls)
                    .Select(call => new BrowserCallFact(
                        $"{call.Callee.DeclaringType.ToDisplayString()}.{call.Callee.Name}" +
                        $"({string.Join(", ", call.Callee.ParameterTypes.Select(parameter => parameter.ToDisplayString()))})",
                        FormatOffset(call.ILOffset),
                        string.IsNullOrEmpty(call.Opcode) ? FormatCallKind(call.Kind) : call.Opcode,
                        call.Kind.ToString(),
                        call.Multiplicity.ToString(),
                        call.InLoop,
                        call.ExactTarget))
                    .ToArray(),
                [
                    .. (unsafeOperations.IsDefault ? [] : unsafeOperations)
                        .Select(operation => new BrowserSafetyFact(
                            operation.Kind.ToString(),
                            FormatOffset(operation.ILOffset),
                            operation.Detail ?? "Unsafe IL operation")),
                    .. (unsafeEvidence.IsDefault ? [] : unsafeEvidence)
                        .Select(evidence => new BrowserSafetyFact(
                            evidence.Kind,
                            evidence.ILOffset is int offset ? FormatOffset(offset) : null,
                            $"{evidence.Reason}: {evidence.Detail}"))
                ],
                regions.Select(region => new BrowserExceptionRegion(
                    region.Region,
                    region.Clause,
                    FormatRange(region.TryStart, region.TryEnd),
                    FormatRange(region.HandlerStart, region.HandlerEnd),
                    region.FilterStart is int filterStart && region.FilterEnd is int filterEnd
                        ? FormatRange(filterStart, filterEnd)
                        : null,
                    region.CaughtType)).ToArray(),
                index.OptimizationOpportunities
                    .Where(opportunity => opportunity.Method.MetadataToken == token)
                    .Select(opportunity => new BrowserPerformanceOpportunity(
                        opportunity.Shape,
                        opportunity.Evidence,
                        opportunity.SafeFixDirection,
                        opportunity.Confidence,
                        opportunity.ILOffset is int offset ? FormatOffset(offset) : null,
                        opportunity.InLoop,
                        opportunity.Caveat,
                        opportunity.SourceFinding,
                        opportunity.Provenance.ToString().ToLowerInvariant()))
                    .ToArray());

            return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserMemberFacts);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    static string FormatOffset(int offset) => $"IL_{offset:X4}";
    static string FormatRange(int start, int end) => $"{FormatOffset(start)}..{FormatOffset(end)}";
    static string FormatCallKind(Analysis.CallKind kind) => kind switch
    {
        Analysis.CallKind.Call => "call",
        Analysis.CallKind.CallVirtual => "callvirt",
        Analysis.CallKind.NewObject => "newobj",
        Analysis.CallKind.LoadFunction => "ldftn",
        Analysis.CallKind.LoadVirtualFunction => "ldvirtftn",
        _ => "calli",
    };

    static string StripGenericArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    static BrowserCallGraphNode ToBrowserCallNode(Analysis.CallTreeNode node)
    {
        var definition = RootDefinition(node.Member.DeclaringType);
        var typeFullName = definition.Namespace.Length == 0
            ? definition.Name
            : $"{definition.Namespace}.{definition.Name}";
        return new(
            $"{node.Member.DeclaringType.ToDisplayString()}.{node.Member.Name}" +
            $"({string.Join(", ", node.Member.ParameterTypes.Select(type => type.ToDisplayString()))})",
            node.Status.ToString(),
            node.Perf?.InLoop ?? false,
            node.Perf?.Source,
            node.Children.Select(ToBrowserCallNode).ToArray(),
            definition.Assembly,
            typeFullName,
            node.Member.Name,
            string.Join(", ", node.Member.ParameterTypes.Select(type => type.ToDisplayString())));
    }

    // The declaring type of a callee may be a constructed generic instance or an array/
    // by-ref wrapper; unwrap to the underlying named definition so identity fields carry
    // the assembly + metadata full name (namespace.Name`arity) a platform resolver keys on.
    static Analysis.TypeRef RootDefinition(Analysis.TypeRef type)
        => type.Kind == Analysis.TypeRefKind.Definition
            ? type
            : type.ElementType is { } element
                ? RootDefinition(element)
                : type;

    static async Task<BrowserMemberSource?> TryGetAuthoredSourceAsync(
        string assemblyPath,
        ApiType type,
        ApiMember member,
        int metadataToken)
    {
        try
        {
            using var source = SourceLinkService.Open(assemblyPath);
            if (!source.HasPdb)
                return null;

            var sameName = type.Members
                .Where(candidate => candidate.Name == member.Name && candidate.Kind == member.Kind)
                .ToArray();
            var overloadIndex = Array.IndexOf(sameName, member);
            var mapping = source.ResolveMethodSource(type.FullName, member.Name, Math.Max(0, overloadIndex), publicOnly: false);
            if (mapping?.SourceUrl is not { Length: > 0 } url)
                return null;

            var bytes = await Http.GetByteArrayAsync(url);
            if (!ChecksumMatches(mapping.ChecksumAlgorithm, mapping.Checksum, bytes))
                return null;

            var text = SourceLinkResolver.ExtractMethodBody(
                Encoding.UTF8.GetString(bytes),
                mapping.StartLine,
                mapping.EndLine,
                member.Name);
            return new BrowserMemberSource(
                "original",
                text,
                url,
                $"Checksum-verified SourceLink source for metadata token 0x{metadataToken:x8}");
        }
        catch
        {
            return null;
        }
    }

    static bool ChecksumMatches(string? algorithm, byte[]? expected, byte[] content)
    {
        if (expected is not { Length: > 0 })
            return false;
        byte[] actual = algorithm?.ToUpperInvariant() switch
        {
            "SHA1" or "SHA-1" => SHA1.HashData(content),
            "SHA256" or "SHA-256" => SHA256.HashData(content),
            _ => []
        };
        return actual.Length > 0 && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // WASM PDB-acquisition policy: NuGet .snupkg only.
    //
    // This runs inside the browser, so every fetch is subject to CORS. NuGet's
    // symbol-package endpoint (globalcdn.nuget.org/symbol-packages) is CORS-open
    // and works for packages that publish symbols. Packages that ship no snupkg
    // (e.g. Microsoft runtime libraries like System.Text.Json) simply 404 here and
    // we fall back to decompiling with pdb=null — that is expected, not an error.
    //
    // Do NOT add the Microsoft symbol server (MSDL) as a fallback in this engine.
    // MSDL answers with a cross-origin 302 to an Azure blob (SAS-signed, expiring,
    // non-guessable URL) and the 302 itself carries no Access-Control-Allow-Origin
    // header, so a browser fetch in cors mode aborts with "Failed to fetch" before
    // it ever reaches the blob (verified). MSDL-backed PDBs must instead be
    // precomputed at build/publish time and shipped as same-origin static assets.
    static async Task TryAcquirePackagePdbAsync(
        string symbolPackageUrl,
        string targetFramework,
        string assemblyName,
        string destination)
    {
        try
        {
            using var response = await Http.GetAsync(symbolPackageUrl);
            if (!response.IsSuccessStatusCode)
                return;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var pdbName = Path.ChangeExtension(assemblyName, ".pdb");
            var entry = archive.Entries.FirstOrDefault(candidate =>
                candidate.FullName.Equals($"lib/{targetFramework}/{pdbName}", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(candidate =>
                    candidate.Name.Equals(pdbName, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                await WriteEntryAsync(entry, destination);
        }
        catch
        {
        }
    }

    static async Task WriteEntryAsync(ZipArchiveEntry entry, string path)
    {
        await using var input = entry.Open();
        await using var output = File.Create(path);
        await input.CopyToAsync(output);
    }

    // Descends the call graph one hop into a platform (BCL) method by acquiring its
    // implementation assembly from the CoreCLR runtime pack. RID is irrelevant here — we
    // only read metadata/IL, never execute — so linux-x64 stands in for the eventual
    // CoreCLR-wasm pack (dotnet/runtime #131420). Bodies are what a ref pack lacks, so a
    // ref pack would leave every BCL call a dead leaf; the runtime pack carries IL.
    const string PlatformRuntimePackId = "microsoft.netcore.app.runtime.linux-x64";

    // Display id of the runtime pseudo-package the client adds to its workspace when the
    // user requests the platform pack from Spotlight. Its normalized form is the marker the
    // shared image resolver keys on to fetch from the runtime pack rather than a lib/ layout.
    const string RuntimePackDisplayId = "Microsoft.NETCore.App";
    const string RuntimePackPackageId = "microsoft.netcore.app";

    // The single assembly loaded eagerly when the runtime pack is requested: it carries the
    // overwhelming majority of BCL surface (String, TextWriter, collections, Volatile,
    // Unsafe, …). Sibling pack assemblies load lazily as navigation reaches them.
    const string RuntimeCoreAssembly = "System.Private.CoreLib.dll";

    // Session cache of runtime-pack file bytes keyed by "version/fileName" so repeat
    // navigation into the pack does not re-range-fetch the same assembly.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> RuntimeFileCache = new();

    // Eagerly loads the runtime pack's core assembly (System.Private.CoreLib) for the
    // workspace TFM and returns it as a package-shaped surface the client treats as a
    // resident package: its types become searchable in Spotlight and browsable in the type
    // nav, and per-type views resolve through the shared image seam. Latest pack version per
    // TFM major is resolved from the flat container.
    [JSExport]
    public static async Task<string> LoadRuntimePack(string targetFramework)
    {
        var major = ParseTfmMajor(targetFramework);
        var version = await ResolveRuntimePackVersionAsync(PlatformRuntimePackId, major);
        var bytes = await AcquireRuntimeFileAsync(version, RuntimeCoreAssembly)
            ?? throw new InvalidOperationException(
                $"Could not acquire {RuntimeCoreAssembly} from {PlatformRuntimePackId} {version}.");

        using var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
            new AssemblyReferenceIdentity(Path.GetFileNameWithoutExtension(RuntimeCoreAssembly), null, null, null),
            Path: null,
            OpenRead: () => new MemoryStream(bytes, writable: false),
            Provenance: $"runtime-pack/{PlatformRuntimePackId}/{RuntimeCoreAssembly}"));
        if (!inspection.HasMetadata)
            throw new InvalidOperationException($"{RuntimeCoreAssembly} has no metadata.");

        var assemblyTypes = inspection.ApiSurface().Types
            .Select(type => ToBrowserType(type, RuntimeCoreAssembly))
            .OrderBy(type => type.Namespace, StringComparer.Ordinal)
            .ThenBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

        var tfm = string.IsNullOrWhiteSpace(targetFramework) ? $"net{major}.0" : targetFramework;
        var result = new BrowserPackageSurface(
            RuntimePackDisplayId,
            version,
            [tfm],
            tfm,
            [
                new BrowserAssemblySurface(
                    RuntimeCoreAssembly,
                    $"runtimes/*/lib/{tfm}/{RuntimeCoreAssembly}",
                    assemblyTypes.Length,
                    assemblyTypes.Sum(type => type.Members)),
            ],
            assemblyTypes,
            assemblyTypes.Sum(type => type.Members));
        return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserPackageSurface);
    }

    // Materializes the implementation assembly for a per-type/member query into tempRoot and
    // returns its path. For the runtime pseudo-package it range-extracts from the CoreCLR
    // runtime pack's runtimes/ layout (session-cached); for ordinary packages it uses the
    // lib/{tfm}/{assembly} asset (with an optional ref/ fallback), copying sibling lib
    // assemblies alongside for the reference resolver.
    static async Task<string> MaterializeImplementationAsync(
        string normalizedId,
        string normalizedVersion,
        string targetFramework,
        string assemblyName,
        string tempRoot,
        bool allowRefFallback)
    {
        if (normalizedId.Equals(RuntimePackPackageId, StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await AcquireRuntimeFileAsync(normalizedVersion, assemblyName)
                ?? throw new InvalidOperationException(
                    $"No runtime-pack asset for {assemblyName} in {PlatformRuntimePackId} {normalizedVersion}.");
            var runtimePath = Path.Combine(tempRoot, assemblyName);
            await File.WriteAllBytesAsync(runtimePath, bytes);
            return runtimePath;
        }

        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var implementation = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.Equals($"lib/{targetFramework}/{assemblyName}", StringComparison.OrdinalIgnoreCase))
            ?? (allowRefFallback
                ? archive.Entries.FirstOrDefault(entry =>
                    entry.FullName.Equals($"ref/{targetFramework}/{assemblyName}", StringComparison.OrdinalIgnoreCase))
                : null)
            ?? throw new InvalidOperationException(
                $"No implementation asset for {assemblyName} at {targetFramework}.");

        foreach (var entry in archive.Entries.Where(entry =>
            entry.FullName.StartsWith($"lib/{targetFramework}/", StringComparison.OrdinalIgnoreCase)
            && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            await WriteEntryAsync(entry, Path.Combine(tempRoot, entry.Name));
        }

        var implementationPath = Path.Combine(tempRoot, implementation.Name);
        if (!File.Exists(implementationPath))
            await WriteEntryAsync(implementation, implementationPath);
        return implementationPath;
    }

    // Fetches one file from the CoreCLR runtime pack (runtimes/.../<file>), range-extracting
    // just that entry from the ~38 MB nupkg with a full-download fallback, and caches the
    // bytes for the session.
    static async Task<byte[]?> AcquireRuntimeFileAsync(string version, string fileName)
    {
        var cacheKey = $"{version}/{fileName}";
        if (RuntimeFileCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var nupkgUrl =
            $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(PlatformRuntimePackId)}/" +
            $"{Uri.EscapeDataString(version)}/" +
            $"{Uri.EscapeDataString(PlatformRuntimePackId)}.{Uri.EscapeDataString(version)}.nupkg";
        bool IsWanted(string entryName) =>
            entryName.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(entryName).Equals(fileName, StringComparison.OrdinalIgnoreCase);

        byte[]? bytes = null;
        try { bytes = await RangeExtractEntryAsync(nupkgUrl, IsWanted); }
        catch { bytes = null; }
        if (bytes is null)
        {
            var fullPack = await GetPackageBytesAsync(PlatformRuntimePackId, version);
            bytes = ExtractEntryFromArchive(fullPack, IsWanted);
        }
        if (bytes is not null)
            RuntimeFileCache[cacheKey] = bytes;
        return bytes;
    }

    [JSExport]
    public static async Task<string> ExpandPlatformCallGraph(
        string targetFramework,
        string assembly,
        string typeFullName,
        string memberName,
        string paramSig)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        var major = ParseTfmMajor(targetFramework);
        var version = await ResolveRuntimePackVersionAsync(PlatformRuntimePackId, major);
        // TypeRef.Assembly canonicalizes the corelib facades (System.Private.CoreLib,
        // System.Runtime, mscorlib, netstandard) to "corelib"; those all implement in
        // System.Private.CoreLib.dll in the runtime pack.
        var startFile = assembly is "corelib" or "" or null
            ? "System.Private.CoreLib.dll"
            : assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? assembly : assembly + ".dll";

        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-plat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var acquired = await AcquirePlatformAssemblyAsync(version, startFile, typeFullName)
                ?? throw new InvalidOperationException(
                    $"Could not acquire an implementation assembly for '{typeFullName}' from {PlatformRuntimePackId} {version}.");
            var path = Path.Combine(tempRoot, acquired.FileName);
            await File.WriteAllBytesAsync(path, acquired.Bytes);

            using var inspection = AssemblyInspectionSession.Open(new ResolvedAssemblyReference(
                new AssemblyReferenceIdentity(Path.GetFileNameWithoutExtension(acquired.FileName), null, null, null),
                path,
                () => File.OpenRead(path),
                Provenance: $"runtime-pack/{PlatformRuntimePackId}/{acquired.FileName}"));
            var type = inspection.ApiSurface().Types.FirstOrDefault(candidate =>
                candidate.FullName.Equals(typeFullName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Type '{typeFullName}' is not defined in {acquired.FileName}.");
            var member = SelectPlatformMember(type, memberName, paramSig)
                ?? throw new InvalidOperationException(
                    $"Member '{memberName}' was not found on '{typeFullName}'.");
            if (member.MetadataToken is not int token)
                throw new InvalidOperationException("The selected platform member has no method body identity.");

            var index = Analysis.LibraryBodyIndex.Open(
                path,
                Analysis.LibraryBodyAnalysisFeatures.MethodEvidence);
            var callees = index.BuildCallTree(token, maxDepth: 2, maxNodes: 30);
            var calleeNode = ToBrowserCallNode(callees);
            var result = new BrowserCallGraph(
                CallGraphMermaid.Render(
                    null,
                    callees,
                    new CallGraphMermaid.Options(CompactLabels: true, RelationshipColors: true)),
                calleeNode with { Children = [] },
                calleeNode,
                new BrowserCallGraphScope(0, 1, 0, acquired.FileName));
            return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserCallGraph);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    static int ParseTfmMajor(string? targetFramework)
    {
        if (string.IsNullOrEmpty(targetFramework))
            return 10;
        int start = 0;
        while (start < targetFramework.Length && !char.IsDigit(targetFramework[start]))
            start++;
        int end = start;
        while (end < targetFramework.Length && char.IsDigit(targetFramework[end]))
            end++;
        return start < end && int.TryParse(targetFramework[start..end], out var major) ? major : 10;
    }

    static async Task<string> ResolveRuntimePackVersionAsync(string packId, int major)
    {
        var indexUrl = $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(packId)}/index.json";
        var indexBytes = await Http.GetByteArrayAsync(indexUrl);
        using var document = JsonDocument.Parse(indexBytes);
        var versions = document.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        var prefix = $"{major}.";
        return versions.LastOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal) && !candidate.Contains('-'))
            ?? versions.LastOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal))
            ?? versions.LastOrDefault(candidate => !candidate.Contains('-'))
            ?? throw new InvalidOperationException($"Runtime pack '{packId}' has no published version.");
    }

    // Fetches a single implementation assembly from the runtime pack, following ECMA-335
    // type-forwards (a facade like System.Runtime.dll forwards its public surface to
    // System.Private.CoreLib.dll) up to a bounded number of hops.
    static async Task<(byte[] Bytes, string FileName)?> AcquirePlatformAssemblyAsync(
        string version,
        string startFile,
        string typeFullName)
    {
        var (ns, name) = SplitTypeName(typeFullName);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = startFile;
        for (int hop = 0; hop < 5 && visited.Add(current); hop++)
        {
            // Range-extract (and session-cache) just this assembly from the ~38 MB pack.
            var bytes = await AcquireRuntimeFileAsync(version, current);
            if (bytes is null)
                return null;

            var forward = FindForwardTarget(bytes, ns, name);
            if (forward is null)
                return (bytes, current);
            current = forward.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? forward : forward + ".dll";
        }
        return null;
    }

    static byte[]? ExtractEntryFromArchive(byte[] packBytes, Func<string, bool> match)
    {
        using var stream = new MemoryStream(packBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(candidate => match(candidate.FullName));
        if (entry is null)
            return null;
        using var entryStream = entry.Open();
        using var output = new MemoryStream();
        entryStream.CopyTo(output);
        return output.ToArray();
    }

    // Extracts one zip entry's bytes using HTTP range requests: a suffix range for the
    // tail (End Of Central Directory), then absolute ranges for the central directory and
    // the target entry's local header + compressed data. Only response bodies are read,
    // so the CORS expose-headers restriction on Content-Range does not matter. Returns
    // null (caller falls back to a full download) for zip64 or an unsupported method.
    static async Task<byte[]?> RangeExtractEntryAsync(string url, Func<string, bool> match)
    {
        var tail = await RangeGetAsync(url, suffix: 65536);
        int eocd = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
            {
                eocd = i;
                break;
            }
        }
        if (eocd < 0)
            return null;
        uint cdSize = BitConverter.ToUInt32(tail, eocd + 12);
        uint cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
        if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF)
            return null;

        var cd = await RangeGetAsync(url, from: cdOffset, length: cdSize);
        int p = 0;
        while (p + 46 <= cd.Length && BitConverter.ToUInt32(cd, p) == 0x02014b50)
        {
            ushort method = BitConverter.ToUInt16(cd, p + 10);
            uint compressedSize = BitConverter.ToUInt32(cd, p + 20);
            ushort nameLength = BitConverter.ToUInt16(cd, p + 28);
            ushort extraLength = BitConverter.ToUInt16(cd, p + 30);
            ushort commentLength = BitConverter.ToUInt16(cd, p + 32);
            uint localHeaderOffset = BitConverter.ToUInt32(cd, p + 42);
            string entryName = Encoding.UTF8.GetString(cd, p + 46, nameLength);
            if (match(entryName))
            {
                if (compressedSize == 0xFFFFFFFF || localHeaderOffset == 0xFFFFFFFF)
                    return null;
                var localHeader = await RangeGetAsync(url, from: localHeaderOffset, length: 30);
                ushort localNameLength = BitConverter.ToUInt16(localHeader, 26);
                ushort localExtraLength = BitConverter.ToUInt16(localHeader, 28);
                long dataStart = localHeaderOffset + 30L + localNameLength + localExtraLength;
                var data = await RangeGetAsync(url, from: dataStart, length: compressedSize);
                if (method == 0)
                    return data;
                if (method == 8)
                {
                    using var input = new MemoryStream(data, writable: false);
                    using var inflate = new DeflateStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    await inflate.CopyToAsync(output);
                    return output.ToArray();
                }
                return null;
            }
            p += 46 + nameLength + extraLength + commentLength;
        }
        return null;
    }

    static async Task<byte[]> RangeGetAsync(string url, long? from = null, long? length = null, long? suffix = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = suffix is { } tailLength
            ? new RangeHeaderValue(null, tailLength)
            : new RangeHeaderValue(from, from + length - 1);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    // Returns the target assembly simple name when the requested type is an ECMA-335
    // exported-type forward in this assembly; null when the type is defined here (a
    // TypeDef) or absent (the caller then validates against the acquired assembly).
    static string? FindForwardTarget(byte[] assemblyBytes, string ns, string name)
    {
        using var peReader = new PEReader(new MemoryStream(assemblyBytes, writable: false));
        if (!peReader.HasMetadata)
            return null;
        var reader = peReader.GetMetadataReader();
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (reader.GetString(definition.Name) == name && reader.GetString(definition.Namespace) == ns)
                return null;
        }
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            if (reader.GetString(exported.Name) != name || reader.GetString(exported.Namespace) != ns)
                continue;
            if (exported.Implementation.Kind == HandleKind.AssemblyReference)
            {
                var assemblyReference = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
                return reader.GetString(assemblyReference.Name);
            }
        }
        return null;
    }

    static (string Namespace, string Name) SplitTypeName(string fullName)
    {
        int dot = fullName.LastIndexOf('.');
        return dot < 0 ? ("", fullName) : (fullName[..dot], fullName[(dot + 1)..]);
    }

    static ApiMember? SelectPlatformMember(ApiType type, string memberName, string paramSig)
    {
        var named = type.Members
            .Where(candidate => string.Equals(candidate.Name, memberName, StringComparison.Ordinal))
            .ToArray();
        if (named.Length <= 1)
            return named.FirstOrDefault();
        int wantArity = string.IsNullOrEmpty(paramSig) ? 0 : paramSig.Split(',').Length;
        var byArity = named
            .Where(candidate => (candidate.SignatureModel?.Parameters.Count ?? -1) == wantArity)
            .ToArray();
        var pool = byArity.Length > 0 ? byArity : named;
        var wantKey = SimpleParamKey(paramSig);
        return pool.FirstOrDefault(candidate => MemberParamKey(candidate) == wantKey) ?? pool[0];
    }

    static string MemberParamKey(ApiMember member)
        => string.Join(",", (member.SignatureModel?.Parameters ?? [])
            .Select(parameter => SimpleTypeName(parameter.TypeWithModifier)));

    static string SimpleParamKey(string paramSig)
        => string.IsNullOrEmpty(paramSig)
            ? ""
            : string.Join(",", paramSig.Split(',').Select(part => SimpleTypeName(part.Trim())));

    static string SimpleTypeName(string type)
    {
        type = type.Trim();
        int generic = type.IndexOf('<');
        if (generic >= 0)
            type = type[..generic];
        int array = type.IndexOf('[');
        string suffix = array >= 0 ? type[array..] : "";
        if (array >= 0)
            type = type[..array];
        int dot = type.LastIndexOf('.');
        if (dot >= 0)
            type = type[(dot + 1)..];
        return (type + suffix).ToLowerInvariant();
    }


    static async Task<byte[]> GetPackageBytesAsync(string normalizedId, string normalizedVersion)
    {
        var key = $"{normalizedId}@{normalizedVersion}";
        lock (PackageCacheLock)
        {
            if (PackageCache.TryGetValue(key, out var cached))
            {
                PackageCache[key] = cached with { LastAccess = ++_packageCacheClock };
                return cached.Bytes;
            }
        }

        var packageUrl =
            $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(normalizedId)}/" +
            $"{Uri.EscapeDataString(normalizedVersion)}/" +
            $"{Uri.EscapeDataString(normalizedId)}.{Uri.EscapeDataString(normalizedVersion)}.nupkg";
        var bytes = await Http.GetByteArrayAsync(packageUrl);
        lock (PackageCacheLock)
        {
            DownloadedPackages.Add(key);
        }
        if (bytes.LongLength > MaxCachedPackageBytes)
            return bytes;

        lock (PackageCacheLock)
        {
            while (PackageCache.Count >= MaxCachedPackages
                || PackageCache.Values.Sum(entry => entry.Bytes.LongLength) + bytes.LongLength
                    > MaxCachedPackageBytes)
            {
                var oldestKey = PackageCache
                    .OrderBy(entry => entry.Value.LastAccess)
                    .Select(entry => entry.Key)
                    .FirstOrDefault();
                if (oldestKey is null)
                    break;
                PackageCache.Remove(oldestKey);
            }
            PackageCache[key] = new PackageCacheEntry(bytes, ++_packageCacheClock);
        }
        return bytes;
    }

    static BrowserTypeSurface ToBrowserType(ApiType type, string assembly)
    {
        var accessibility = string.IsNullOrWhiteSpace(type.Accessibility) ? "public" : type.Accessibility;
        var modifiers = new List<string> { accessibility };
        if (type.IsStatic)
            modifiers.Add("static");
        else
        {
            if (type.IsAbstract && type.Kind == "class")
                modifiers.Add("abstract");
            if (type.IsSealed && type.Kind == "class")
                modifiers.Add("sealed");
            if (type.IsReadOnly && type.Kind == "struct")
                modifiers.Add("readonly");
            if (type.IsByRefLike && type.Kind == "struct")
                modifiers.Add("ref");
        }
        modifiers.Add(type.Kind);
        modifiers.Add(type.Name);

        var members = type.Members.Select(member =>
        {
            var documentationId = GetDocumentationId(type, member);
            var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
            return new BrowserMemberSurface(
                member.Name,
                member.Kind,
                member.Signature ?? member.Name,
                member.MetadataToken,
                member.SignatureModel?.ReturnType ?? member.ReturnType,
                member.SignatureModel?.Parameters.Select(parameter => new BrowserParameterSurface(
                    parameter.Name,
                    parameter.Type,
                    parameter.Modifier,
                    parameter.HasDefault,
                    parameter.DefaultValueText,
                    null)).ToArray() ?? [],
                documentationId,
                null,
                null,
                [],
                anchor.StableSelector,
                anchor.Fingerprint,
                anchor.CanonicalSignature);
        }).ToArray();

        return new BrowserTypeSurface(
            type.FullName,
            type.Name,
            type.Namespace ?? "",
            string.Join(' ', modifiers.Skip(1).SkipLast(1)),
            accessibility,
            assembly,
            members.Length,
            string.Join(' ', modifiers),
            members);
    }

    [JSExport]
    public static async Task<string> QueryMemberDocumentation(
        string packageId,
        string version,
        string framework,
        string assemblyName,
        string documentationId)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        var bytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var documentation = LoadMemberDocumentation(
            archive,
            framework,
            Path.GetFileNameWithoutExtension(assemblyName),
            documentationId)
            ?? new BrowserMemberDocumentation(null, null, new Dictionary<string, string>(), []);
        return JsonSerializer.Serialize(
            documentation,
            BrowserJsonContext.Default.BrowserMemberDocumentation);
    }

    static BrowserMemberDocumentation? LoadMemberDocumentation(
        ZipArchive archive,
        string framework,
        string assemblyName,
        string documentationId)
    {
        var fileName = $"{assemblyName}.xml";
        var entry = archive.Entries.FirstOrDefault(candidate =>
            candidate.FullName.Equals($"lib/{framework}/{fileName}", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(candidate =>
                candidate.FullName.Equals($"ref/{framework}/{fileName}", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;

        try
        {
            using var stream = entry.Open();
            var document = XDocument.Load(stream, LoadOptions.None);
            var element = document.Descendants("member").FirstOrDefault(candidate =>
                candidate.Attribute("name")?.Value == documentationId);
            return element is null
                ? null
                : new BrowserMemberDocumentation(
                    FormatDocElement(element.Element("summary")),
                    FormatDocElement(element.Element("returns")),
                    element.Elements("param")
                        .Where(parameter => parameter.Attribute("name") is not null)
                        .ToDictionary(
                            parameter => parameter.Attribute("name")!.Value,
                            parameter => FormatDocElement(parameter) ?? "",
                            StringComparer.Ordinal),
                    element.Elements("exception")
                        .Select(exception => new BrowserExceptionSurface(
                            NormalizeDocReference(exception.Attribute("cref")?.Value),
                            FormatDocElement(exception) ?? ""))
                        .ToArray());
        }
        catch
        {
            return null;
        }
    }

    static string? GetDocumentationId(ApiType type, ApiMember member)
    {
        if (!ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out var identity))
            return null;
        var key = identity.LookupKey;
        if (identity.NormalizedParameters.Count > 0)
            key += $"({string.Join(",", identity.NormalizedParameters)})";
        if (identity.NormalizedReturnType is { Length: > 0 } returnType)
            key += $"~{returnType}";
        return key;
    }

    static string? FormatDocElement(XElement? element)
    {
        if (element is null)
            return null;
        var builder = new StringBuilder();
        foreach (var node in element.Nodes())
            AppendDocNode(builder, node);
        return string.Join(
            " ",
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    static void AppendDocNode(StringBuilder builder, XNode node)
    {
        if (node is XText text)
        {
            builder.Append(text.Value);
            return;
        }
        if (node is not XElement element)
            return;
        builder.Append(element.Name.LocalName switch
        {
            "see" => element.Attribute("langword")?.Value
                ?? NormalizeDocReference(element.Attribute("cref")?.Value),
            "paramref" or "typeparamref" => element.Attribute("name")?.Value,
            _ => null
        });
        foreach (var child in element.Nodes())
            AppendDocNode(builder, child);
    }

    static string NormalizeDocReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return "";
        var value = reference.Length > 2 && reference[1] == ':' ? reference[2..] : reference;
        return value.Replace('#', '.');
    }

    static (string Root, string Framework)? ParseCompileAsset(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || (parts[0] != "ref" && parts[0] != "lib")
            || !parts[2].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || parts[2].EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return (parts[0], parts[1]);
    }

    static int FrameworkPriority(string framework)
    {
        var moniker = framework.ToLowerInvariant();

        // Family tiers dominate version so a modern .NET moniker always outranks an older
        // .NET Framework one, even though "net462" carries larger raw digits than "net10.0".
        // Modern .NET is spelled "net{major}.{minor}" (with a dot); .NET Framework is
        // "net{digits}" (no dot, e.g. net462, net48).
        int familyBase;
        int version;
        if (moniker.StartsWith("netcoreapp"))
        {
            familyBase = 300_000;
            version = DottedVersion(moniker);
        }
        else if (moniker.StartsWith("netstandard"))
        {
            familyBase = 200_000;
            version = DottedVersion(moniker);
        }
        else if (moniker.StartsWith("net") && moniker.Length > 3 && char.IsDigit(moniker[3]))
        {
            if (moniker.Contains('.'))
            {
                familyBase = 400_000;
                version = DottedVersion(moniker);
            }
            else
            {
                familyBase = 100_000;
                version = int.TryParse(new string(moniker.Where(char.IsDigit).ToArray()), out var raw) ? raw : 0;
            }
        }
        else
        {
            familyBase = 0;
            version = 0;
        }

        return familyBase + version;

        static int DottedVersion(string value)
        {
            var digits = new string(value.SkipWhile(character => !char.IsDigit(character)).ToArray());
            var segments = digits.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var major = segments.Length > 0 && int.TryParse(segments[0], out var parsedMajor) ? parsedMajor : 0;
            var minor = segments.Length > 1 && int.TryParse(segments[1], out var parsedMinor) ? parsedMinor : 0;
            return major * 100 + minor;
        }
    }
}
