using System.IO.Compression;
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
    BrowserCallGraphNode Callees);

public sealed record BrowserCallGraphNode(
    string Label,
    string Status,
    bool InLoop,
    BrowserCallGraphNode[] Children);

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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageSurface))]
[JsonSerializable(typeof(BrowserMemberSource))]
[JsonSerializable(typeof(BrowserCallGraph))]
[JsonSerializable(typeof(BrowserMemberDocumentation))]
[JsonSerializable(typeof(BrowserMemberFacts))]
internal sealed partial class BrowserJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class BrowserInspectionEngine
{
    static readonly HttpClient Http = new();
    static readonly object PackageCacheLock = new();
    static readonly Dictionary<string, PackageCacheEntry> PackageCache = new(StringComparer.Ordinal);
    const int MaxCachedPackages = 4;
    const long MaxCachedPackageBytes = 64L * 1024 * 1024;
    static long _packageCacheClock;

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

            var surface = inspection.ApiSurface();
            var assemblyTypes = surface.Types
                .Select(type => ToBrowserType(type, candidate.Entry.Name))
                .OrderBy(type => type.Namespace, StringComparer.Ordinal)
                .ThenBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();

            assemblies.Add(new BrowserAssemblySurface(
                candidate.Entry.Name,
                candidate.Entry.FullName,
                assemblyTypes.Length,
                assemblyTypes.Sum(type => type.Members)));
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
            identifiedTypes.Sum(type => type.Members));

        return JsonSerializer.Serialize(result, BrowserJsonContext.Default.BrowserPackageSurface);
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

    [JSExport]
    public static async Task<string> QueryMemberSource(
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
        var packageBase =
            $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(normalizedId)}/" +
            $"{Uri.EscapeDataString(normalizedVersion)}/" +
            $"{Uri.EscapeDataString(normalizedId)}.{Uri.EscapeDataString(normalizedVersion)}";
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string? implementationPath;
            using (var stream = new MemoryStream(packageBytes, writable: false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var implementation = archive.Entries.FirstOrDefault(entry =>
                    entry.FullName.Equals($"lib/{targetFramework}/{assemblyName}", StringComparison.OrdinalIgnoreCase))
                    ?? archive.Entries.FirstOrDefault(entry =>
                        entry.FullName.Equals($"ref/{targetFramework}/{assemblyName}", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(
                        $"No implementation asset for {assemblyName} at {targetFramework}.");

                foreach (var entry in archive.Entries.Where(entry =>
                    entry.FullName.StartsWith($"lib/{targetFramework}/", StringComparison.OrdinalIgnoreCase)
                    && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    await WriteEntryAsync(entry, Path.Combine(tempRoot, entry.Name));
                }

                implementationPath = Path.Combine(tempRoot, implementation.Name);
                if (!File.Exists(implementationPath))
                    await WriteEntryAsync(implementation, implementationPath);
            }

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

            var decompiled = MemberBodyProducer.ProduceMember(type, member, implementationPath, File.Exists(pdbPath) ? pdbPath : null);
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

    [JSExport]
    public static async Task<string> QueryMemberCallGraph(
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
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            using var stream = new MemoryStream(packageBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries.Where(entry =>
                entry.FullName.StartsWith($"lib/{targetFramework}/", StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteEntryAsync(entry, Path.Combine(tempRoot, entry.Name));
            }

            var implementationPath = Path.Combine(tempRoot, assemblyName);
            if (!File.Exists(implementationPath))
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
            var callers = index.BuildCallerTree(token, maxDepth: 2, maxNodes: 30);
            var callees = index.BuildCallTree(token, maxDepth: 2, maxNodes: 30);
            var result = new BrowserCallGraph(
                CallGraphMermaid.Render(callers, callees),
                ToBrowserCallNode(callers),
                ToBrowserCallNode(callees));
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
        var packageBytes = await GetPackageBytesAsync(normalizedId, normalizedVersion);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"inspect-facts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            using var stream = new MemoryStream(packageBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries.Where(entry =>
                entry.FullName.StartsWith($"lib/{targetFramework}/", StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteEntryAsync(entry, Path.Combine(tempRoot, entry.Name));
            }

            var implementationPath = Path.Combine(tempRoot, assemblyName);
            if (!File.Exists(implementationPath))
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

    static BrowserCallGraphNode ToBrowserCallNode(Analysis.CallTreeNode node)
        => new(
            $"{node.Member.DeclaringType.ToDisplayString()}.{node.Member.Name}" +
            $"({string.Join(", ", node.Member.ParameterTypes.Select(type => type.ToDisplayString()))})",
            node.Status.ToString(),
            node.Perf?.InLoop ?? false,
            node.Children.Select(ToBrowserCallNode).ToArray());

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
        var digits = new string(framework.SkipWhile(character => !char.IsDigit(character)).ToArray());
        var segments = digits.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var major = segments.Length > 0 && int.TryParse(segments[0], out var parsedMajor) ? parsedMajor : 0;
        var minor = segments.Length > 1 && int.TryParse(segments[1], out var parsedMinor) ? parsedMinor : 0;
        return major * 100 + minor;
    }
}
