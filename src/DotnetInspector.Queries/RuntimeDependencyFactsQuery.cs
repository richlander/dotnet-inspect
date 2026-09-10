using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>Exact-content provenance over caller-supplied runtime dependency manifest bytes.</summary>
public sealed record RuntimeDependencyContentProvenance
{
    public RuntimeDependencyContentProvenance(string sha256)
    {
        if (sha256 is not { Length: 64 }
            || !RestoredProjectIdentityText.IsLowerHex(sha256))
        {
            throw new ArgumentException(
                "A content provenance digest must be a lowercase 64-character SHA-256 hex string.",
                nameof(sha256));
        }

        Sha256 = sha256;
    }

    public string Sha256 { get; }

    internal static RuntimeDependencyContentProvenance FromBytes(
        ReadOnlyMemory<byte> bytes) =>
        new(Convert.ToHexStringLower(SHA256.HashData(bytes.Span)));
}

/// <summary>Stable semantic identity over one selected runtime target's package facts.</summary>
public sealed record RuntimeDependencyManifestIdentity
{
    public RuntimeDependencyManifestIdentity(
        string targetIdentity,
        string factsDigest)
    {
        if (!RestoredProjectIdentityText.IsSafeTargetIdentity(targetIdentity))
        {
            throw new ArgumentException(
                "A runtime target identity must contain canonical or opaque target segments.",
                nameof(targetIdentity));
        }

        if (factsDigest is not { Length: 64 }
            || !RestoredProjectIdentityText.IsLowerHex(factsDigest))
        {
            throw new ArgumentException(
                "A runtime manifest identity must contain a lowercase 64-character SHA-256 digest.",
                nameof(factsDigest));
        }

        TargetIdentity = targetIdentity;
        FactsDigest = factsDigest;
    }

    public string TargetIdentity { get; }

    public string FactsDigest { get; }
}

/// <summary>The exact runtime target selected by <c>runtimeTarget.name</c>.</summary>
public sealed record RuntimeDependencyTarget(
    string FrameworkIdentity,
    string? RuntimeIdentifierIdentity,
    InertString SourceNameSpelling,
    InertString SourceFrameworkSpelling,
    InertString? SourceRuntimeIdentifierSpelling)
{
    public string Identity => RuntimeIdentifierIdentity is null
        ? FrameworkIdentity
        : $"{FrameworkIdentity}/{RuntimeIdentifierIdentity}";
}

/// <summary>Identifies the admitted runtime dependency manifest.</summary>
public readonly record struct RuntimeDependencyRootIdentity(
    RuntimeDependencyManifestIdentity Manifest);

/// <summary>Identifies one selected-target package node.</summary>
public readonly record struct RuntimeDependencyPackageNodeIdentity(
    RuntimeDependencyManifestIdentity Manifest,
    PackageSourceCoordinate Coordinate);

/// <summary>Identifies one selected-target non-package parent without exposing artifact text.</summary>
public readonly record struct RuntimeDependencyLibraryNodeIdentity(
    RuntimeDependencyManifestIdentity Manifest,
    string SourceIdentity);

/// <summary>The closed parent identity for one runtime package relationship.</summary>
public abstract record RuntimeDependencyGraphParentIdentity
{
    private RuntimeDependencyGraphParentIdentity()
    {
    }

    public sealed record Package(RuntimeDependencyPackageNodeIdentity Identity) :
        RuntimeDependencyGraphParentIdentity;

    public sealed record Library(RuntimeDependencyLibraryNodeIdentity Identity) :
        RuntimeDependencyGraphParentIdentity;
}

/// <summary>Identifies one runtime package relationship.</summary>
public readonly record struct RuntimeDependencyEdgeIdentity(
    RuntimeDependencyGraphParentIdentity Parent,
    RuntimeDependencyPackageNodeIdentity Dependency);

/// <summary>One selected-target package node with retained source spelling.</summary>
public sealed record RuntimeDependencyPackageNode(
    RuntimeDependencyPackageNodeIdentity Identity,
    InertString SourcePackageIdSpelling,
    InertString SourceVersionSpelling);

/// <summary>One selected-target dependency entry resolved to a package node.</summary>
public sealed record RuntimeDependencyGraphEdge(
    RuntimeDependencyEdgeIdentity Identity,
    RuntimeDependencyGraphParentIdentity Parent,
    RuntimeDependencyPackageNodeIdentity Dependency,
    InertString SourceDependencyNameSpelling,
    InertString SourceDependencyVersionSpelling,
    int SourceOccurrenceCount)
{
    public int SourceOccurrenceCount { get; } = SourceOccurrenceCount >= 1
        ? SourceOccurrenceCount
        : throw new ArgumentOutOfRangeException(
            nameof(SourceOccurrenceCount),
            SourceOccurrenceCount,
            "A source occurrence count must be at least one.");
}

/// <summary>Whether every selected-target package fact was projected.</summary>
public enum RuntimeDependencyPhaseCompletion
{
    Complete,
    Incomplete,
}

/// <summary>Why selected-target package graph evidence is incomplete.</summary>
public enum RuntimeDependencyGraphFailureReason
{
    InvalidTargetLibraryShape,
    InvalidLibraryMetadata,
    InvalidPackageCoordinate,
    AmbiguousPackageCoordinate,
    InvalidDependencyShape,
    InvalidDependencyCoordinate,
    UnresolvedDependency,
}

/// <summary>One content-free graph failure and its exact source occurrence count.</summary>
public sealed record RuntimeDependencyGraphFailure(
    RuntimeDependencyGraphFailureReason Reason,
    int Count)
{
    public int Count { get; } = Count >= 1
        ? Count
        : throw new ArgumentOutOfRangeException(
            nameof(Count),
            Count,
            "A failure count must be at least one.");

    public string Message => Reason switch
    {
        RuntimeDependencyGraphFailureReason.InvalidTargetLibraryShape =>
            "A selected runtime target library has an invalid shape or identity.",
        RuntimeDependencyGraphFailureReason.InvalidLibraryMetadata =>
            "A selected runtime target library has missing or invalid library metadata.",
        RuntimeDependencyGraphFailureReason.InvalidPackageCoordinate =>
            "A selected runtime package has an invalid package coordinate.",
        RuntimeDependencyGraphFailureReason.AmbiguousPackageCoordinate =>
            "Several selected runtime libraries resolve to one package coordinate.",
        RuntimeDependencyGraphFailureReason.InvalidDependencyShape =>
            "A selected runtime library has an invalid dependencies shape.",
        RuntimeDependencyGraphFailureReason.InvalidDependencyCoordinate =>
            "A selected runtime dependency has an invalid name or version.",
        RuntimeDependencyGraphFailureReason.UnresolvedDependency =>
            "A selected runtime dependency does not resolve to one selected library.",
        _ => "A selected runtime package fact could not be projected.",
    };
}

/// <summary>The immutable selected-target package graph.</summary>
public sealed record RuntimeDependencyGraph
{
    public RuntimeDependencyGraph(
        ImmutableArray<RuntimeDependencyPackageNode> packages,
        ImmutableArray<RuntimeDependencyGraphEdge> edges,
        ImmutableArray<RuntimeDependencyGraphFailure> failures,
        RuntimeDependencyPhaseCompletion completion)
    {
        if (failures.IsDefaultOrEmpty
            != (completion == RuntimeDependencyPhaseCompletion.Complete))
        {
            throw new ArgumentException(
                "A complete runtime graph carries no failures and an incomplete graph carries at least one.",
                nameof(completion));
        }

        Packages = packages.IsDefault ? [] : packages;
        Edges = edges.IsDefault ? [] : edges;
        Failures = failures.IsDefault ? [] : failures;
        Completion = completion;
    }

    public ImmutableArray<RuntimeDependencyPackageNode> Packages { get; }

    public ImmutableArray<RuntimeDependencyGraphEdge> Edges { get; }

    public ImmutableArray<RuntimeDependencyGraphFailure> Failures { get; }

    public RuntimeDependencyPhaseCompletion Completion { get; }

    public bool IsComplete => Completion == RuntimeDependencyPhaseCompletion.Complete;
}

/// <summary>Immutable package facts from one selected runtime dependency target.</summary>
public sealed record RuntimeDependencyFacts(
    RuntimeDependencyContentProvenance ContentProvenance,
    RuntimeDependencyManifestIdentity ManifestIdentity,
    RuntimeDependencyTarget Target,
    RuntimeDependencyRootIdentity Root,
    RuntimeDependencyGraph Graph);

/// <summary>Why a runtime dependency document could not be admitted.</summary>
public enum RuntimeDependencyFailureReason
{
    MalformedOrDuplicateBearingJson,
    UnsupportedDocumentShape,
    ConfiguredLimitExceeded,
}

/// <summary>One content-free whole-document runtime dependency failure.</summary>
public sealed record RuntimeDependencyFailure(RuntimeDependencyFailureReason Reason)
{
    public string Message => Reason switch
    {
        RuntimeDependencyFailureReason.MalformedOrDuplicateBearingJson =>
            "The runtime dependency manifest is not well-formed JSON, or contains a duplicate property name.",
        RuntimeDependencyFailureReason.UnsupportedDocumentShape =>
            "The runtime dependency manifest has an unsupported required document shape.",
        RuntimeDependencyFailureReason.ConfiguredLimitExceeded =>
            "The runtime dependency manifest exceeds a configured resource limit.",
        _ => "The runtime dependency manifest could not be projected.",
    };
}

/// <summary>The typed outcome of projecting one runtime dependency manifest.</summary>
public abstract record RuntimeDependencyFactsResult
{
    private RuntimeDependencyFactsResult()
    {
    }

    public sealed record Available(RuntimeDependencyFacts Value) :
        RuntimeDependencyFactsResult;

    public sealed record Failed(RuntimeDependencyFailure Failure) :
        RuntimeDependencyFactsResult;
}

/// <summary>Projects bounded package facts from exact caller-supplied <c>.deps.json</c> bytes.</summary>
public static class RuntimeDependencyFactsQuery
{
    public const int MaxManifestBytes = 4 * 1024 * 1024;
    public const int MaxScalarCharacters = 1024;
    public const int MaxTargetLibraries = 8192;
    public const int MaxDependencyOccurrences = 16384;
    public const int MaxFailureOccurrences = 4096;

    public static InspectionQuery<RuntimeDependencyFactsResult> Definition { get; } =
        new("Runtime dependency facts", InspectionCost.NetworkFree);

    public static RuntimeDependencyFactsResult Execute(
        ReadOnlyMemory<byte> manifestBytes)
    {
        if (manifestBytes.Length > MaxManifestBytes)
            return Failed(RuntimeDependencyFailureReason.ConfiguredLimitExceeded);

        RuntimeDependencyContentProvenance provenance =
            RuntimeDependencyContentProvenance.FromBytes(manifestBytes);

        JsonDocument document;
        try
        {
            document = HardenedJson.Parse(manifestBytes);
        }
        catch (JsonException)
        {
            return Failed(
                RuntimeDependencyFailureReason.MalformedOrDuplicateBearingJson);
        }
        catch (InvalidOperationException)
        {
            return Failed(
                RuntimeDependencyFailureReason.MalformedOrDuplicateBearingJson);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !ContainsOnlyValidJsonStrings(root))
            {
                return Failed(
                    root.ValueKind == JsonValueKind.Object
                        ? RuntimeDependencyFailureReason
                            .MalformedOrDuplicateBearingJson
                        : RuntimeDependencyFailureReason.UnsupportedDocumentShape);
            }

            ReadScalarResult targetNameResult =
                ReadRuntimeTargetName(root, out string targetName);
            if (targetNameResult != ReadScalarResult.Success)
                return Failed(MapReadFailure(targetNameResult));

            if (!root.TryGetProperty("targets", out JsonElement targets)
                || targets.ValueKind != JsonValueKind.Object
                || !targets.TryGetProperty(targetName, out JsonElement selectedTarget)
                || selectedTarget.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("libraries", out JsonElement libraries)
                || libraries.ValueKind != JsonValueKind.Object)
            {
                return Failed(RuntimeDependencyFailureReason.UnsupportedDocumentShape);
            }

            RuntimeDependencyTarget target = CreateTarget(targetName);
            ProjectionResult projection = ProjectGraph(selectedTarget, libraries);
            if (projection.LimitExceeded)
                return Failed(RuntimeDependencyFailureReason.ConfiguredLimitExceeded);

            ImmutableArray<RuntimeDependencyGraphFailure> failures =
                projection.Failures.ToImmutable();
            RuntimeDependencyPhaseCompletion completion = failures.IsEmpty
                ? RuntimeDependencyPhaseCompletion.Complete
                : RuntimeDependencyPhaseCompletion.Incomplete;
            string factsDigest = ComputeFactsDigest(
                target.Identity,
                projection.Packages,
                projection.Edges,
                failures,
                completion);
            var manifestIdentity = new RuntimeDependencyManifestIdentity(
                target.Identity,
                factsDigest);
            RuntimeDependencyGraph graph = RescopeGraph(
                manifestIdentity,
                projection.Packages,
                projection.Edges,
                failures,
                completion);

            return new RuntimeDependencyFactsResult.Available(
                new RuntimeDependencyFacts(
                    provenance,
                    manifestIdentity,
                    target,
                    new RuntimeDependencyRootIdentity(manifestIdentity),
                    graph));
        }
    }

    private static RuntimeDependencyFactsResult Failed(
        RuntimeDependencyFailureReason reason) =>
        new RuntimeDependencyFactsResult.Failed(
            new RuntimeDependencyFailure(reason));

    private static RuntimeDependencyFailureReason MapReadFailure(
        ReadScalarResult result) =>
        result switch
        {
            ReadScalarResult.LimitExceeded =>
                RuntimeDependencyFailureReason.ConfiguredLimitExceeded,
            _ => RuntimeDependencyFailureReason.UnsupportedDocumentShape,
        };

    private static ReadScalarResult ReadRuntimeTargetName(
        JsonElement root,
        out string targetName)
    {
        targetName = "";
        if (!root.TryGetProperty("runtimeTarget", out JsonElement runtimeTarget))
            return ReadScalarResult.Invalid;

        JsonElement name = runtimeTarget;
        if (runtimeTarget.ValueKind == JsonValueKind.Object)
        {
            if (!runtimeTarget.TryGetProperty("name", out name))
                return ReadScalarResult.Invalid;
        }

        if (name.ValueKind != JsonValueKind.String)
            return ReadScalarResult.Invalid;

        targetName = name.GetString() ?? "";
        if (targetName.Length == 0)
            return ReadScalarResult.Invalid;
        if (targetName.Length > MaxScalarCharacters)
            return ReadScalarResult.LimitExceeded;

        int separator = targetName.IndexOf('/');
        if (separator == 0 || separator == targetName.Length - 1)
            return ReadScalarResult.Invalid;

        return ReadScalarResult.Success;
    }

    private static RuntimeDependencyTarget CreateTarget(string targetName)
    {
        int separator = targetName.IndexOf('/');
        string framework = separator < 0
            ? targetName
            : targetName[..separator];
        string? runtimeIdentifier = separator < 0
            ? null
            : targetName[(separator + 1)..];
        string frameworkIdentity = NuGetTargetFrameworkIdentity.TryNormalize(
            framework,
            out string canonicalFramework)
                ? canonicalFramework
                : RestoredProjectIdentityText.Opaque(framework);
        string? runtimeIdentifierIdentity = runtimeIdentifier is null
            ? null
            : PackageCoordinateResolver.IsAcquisitionTargetText(
                runtimeIdentifier)
                    ? runtimeIdentifier.ToLowerInvariant()
                    : RestoredProjectIdentityText.Opaque(runtimeIdentifier);

        return new RuntimeDependencyTarget(
            frameworkIdentity,
            runtimeIdentifierIdentity,
            Field(targetName),
            Field(framework),
            runtimeIdentifier is null ? null : Field(runtimeIdentifier));
    }

    private static ProjectionResult ProjectGraph(
        JsonElement selectedTarget,
        JsonElement libraries)
    {
        var failures = new FailureTally();
        var entries = new List<TargetLibraryDraft>();
        int libraryCount = 0;
        foreach (JsonProperty property in EnumeratePropertiesInCanonicalOrder(
            selectedTarget))
        {
            if (++libraryCount > MaxTargetLibraries)
                return ProjectionResult.Exceeded(failures);

            string sourceKey = property.Name;
            bool keyShapeValid = TrySplitLibraryKey(
                sourceKey,
                out string name,
                out string version);
            if (sourceKey.Length == 0
                || sourceKey.Length > MaxScalarCharacters
                || property.Value.ValueKind != JsonValueKind.Object
                || !keyShapeValid)
            {
                failures.Add(
                    RuntimeDependencyGraphFailureReason
                        .InvalidTargetLibraryShape);
                if (failures.Total > MaxFailureOccurrences)
                    return ProjectionResult.Exceeded(failures);

                if (sourceKey.Length == 0
                    || sourceKey.Length > MaxScalarCharacters
                    || property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
            }

            JsonElement typeElement = default;
            bool metadataValid =
                libraries.TryGetProperty(sourceKey, out JsonElement metadata)
                && metadata.ValueKind == JsonValueKind.Object
                && metadata.TryGetProperty("type", out typeElement)
                && typeElement.ValueKind == JsonValueKind.String;
            string type = metadataValid ? typeElement.GetString() ?? "" : "";
            if (!metadataValid
                || type.Length == 0
                || type.Length > MaxScalarCharacters)
            {
                failures.Add(
                    RuntimeDependencyGraphFailureReason.InvalidLibraryMetadata);
                if (failures.Total > MaxFailureOccurrences)
                    return ProjectionResult.Exceeded(failures);
                metadataValid = false;
            }

            PackageSourceCoordinate? packageCoordinate = null;
            bool compileOnly = false;
            bool compileOnlyShapeValid = true;
            if (property.Value.TryGetProperty(
                    "compileOnly",
                    out JsonElement compileOnlyElement))
            {
                if (compileOnlyElement.ValueKind == JsonValueKind.True)
                {
                    compileOnly = true;
                }
                else if (compileOnlyElement.ValueKind
                    is not (JsonValueKind.False or JsonValueKind.Null))
                {
                    failures.Add(
                        RuntimeDependencyGraphFailureReason
                            .InvalidTargetLibraryShape);
                    if (failures.Total > MaxFailureOccurrences)
                        return ProjectionResult.Exceeded(failures);
                    compileOnlyShapeValid = false;
                }
            }

            LibraryClassification classification =
                !keyShapeValid || !metadataValid || !compileOnlyShapeValid
                    ? LibraryClassification.Unknown
                    : compileOnly
                        ? LibraryClassification.CompileOnly
                        : type.Equals("package", StringComparison.Ordinal)
                            ? LibraryClassification.Package
                            : LibraryClassification.NonPackage;
            if (classification == LibraryClassification.Package)
            {
                if (!PackageCoordinateResolver.IsCanonicalPackageId(name))
                {
                    classification = LibraryClassification.Unknown;
                    failures.Add(
                        RuntimeDependencyGraphFailureReason
                            .InvalidPackageCoordinate);
                }
                else
                {
                    try
                    {
                        packageCoordinate = PackageSourceCoordinate.Create(
                            name,
                            version);
                    }
                    catch (ArgumentException)
                    {
                        classification = LibraryClassification.Unknown;
                        failures.Add(
                            RuntimeDependencyGraphFailureReason
                                .InvalidPackageCoordinate);
                    }
                }

                if (failures.Total > MaxFailureOccurrences)
                    return ProjectionResult.Exceeded(failures);
            }

            LibraryLookupKey? lookupKey =
                TryCreateLookupKey(name, version, out LibraryLookupKey key)
                    ? key
                    : null;
            entries.Add(
                new TargetLibraryDraft(
                    sourceKey,
                    name,
                    version,
                    property.Value,
                    classification,
                    packageCoordinate,
                    lookupKey));
        }

        var packageGroups = entries
            .Where(entry => entry.PackageCoordinate is not null)
            .GroupBy(entry => entry.PackageCoordinate!)
            .OrderBy(group => group.Key.PackageId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Version, StringComparer.Ordinal)
            .ToArray();
        var uniquePackageEntries =
            new Dictionary<PackageSourceCoordinate, TargetLibraryDraft>();
        var packageDrafts =
            ImmutableArray.CreateBuilder<PackageDraft>(packageGroups.Length);
        foreach (IGrouping<PackageSourceCoordinate, TargetLibraryDraft> group in
            packageGroups)
        {
            TargetLibraryDraft[] occurrences = [.. group];
            if (occurrences.Length != 1)
            {
                failures.Add(
                    RuntimeDependencyGraphFailureReason
                        .AmbiguousPackageCoordinate,
                    occurrences.Length);
                if (failures.Total > MaxFailureOccurrences)
                    return ProjectionResult.Exceeded(failures);
                continue;
            }

            TargetLibraryDraft entry = occurrences[0];
            uniquePackageEntries.Add(group.Key, entry);
            packageDrafts.Add(
                new PackageDraft(
                    group.Key,
                    Field(entry.Name),
                    Field(entry.Version)));
        }

        var lookup = entries
            .Where(entry => entry.LookupKey is not null)
            .GroupBy(entry => entry.LookupKey!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());
        var edgeDrafts = new Dictionary<EdgeDraftKey, EdgeDraft>();
        int dependencyCount = 0;
        foreach (TargetLibraryDraft entry in entries)
        {
            if (entry.Classification == LibraryClassification.CompileOnly)
                continue;

            if (!entry.Value.TryGetProperty(
                    "dependencies",
                    out JsonElement dependencies))
            {
                continue;
            }

            if (dependencies.ValueKind != JsonValueKind.Object)
            {
                failures.Add(
                    RuntimeDependencyGraphFailureReason.InvalidDependencyShape);
                if (failures.Total > MaxFailureOccurrences)
                    return ProjectionResult.Exceeded(failures);
                continue;
            }

            foreach (JsonProperty dependency in
                EnumeratePropertiesInCanonicalOrder(dependencies))
            {
                if (++dependencyCount > MaxDependencyOccurrences)
                    return ProjectionResult.Exceeded(failures);

                string dependencyName = dependency.Name;
                string dependencyVersion =
                    dependency.Value.ValueKind == JsonValueKind.String
                        ? dependency.Value.GetString() ?? ""
                        : "";
                if (dependencyName.Length == 0
                    || dependencyName.Length > MaxScalarCharacters
                    || dependencyVersion.Length == 0
                    || dependencyVersion.Length > MaxScalarCharacters
                    || !TryCreateLookupKey(
                        dependencyName,
                        dependencyVersion,
                        out LibraryLookupKey dependencyKey))
                {
                    failures.Add(
                        RuntimeDependencyGraphFailureReason
                            .InvalidDependencyCoordinate);
                    if (failures.Total > MaxFailureOccurrences)
                        return ProjectionResult.Exceeded(failures);
                    continue;
                }

                if (!lookup.TryGetValue(
                        dependencyKey,
                        out TargetLibraryDraft[]? matches)
                    || matches.Length != 1)
                {
                    failures.Add(
                        RuntimeDependencyGraphFailureReason
                            .UnresolvedDependency);
                    if (failures.Total > MaxFailureOccurrences)
                        return ProjectionResult.Exceeded(failures);
                    continue;
                }

                TargetLibraryDraft dependencyEntry = matches[0];
                if (dependencyEntry.Classification
                    is LibraryClassification.NonPackage
                        or LibraryClassification.CompileOnly)
                {
                    continue;
                }

                if (dependencyEntry.Classification
                        != LibraryClassification.Package
                    || dependencyEntry.PackageCoordinate is not { } coordinate
                    || !uniquePackageEntries.ContainsKey(coordinate))
                {
                    failures.Add(
                        RuntimeDependencyGraphFailureReason
                            .UnresolvedDependency);
                    if (failures.Total > MaxFailureOccurrences)
                        return ProjectionResult.Exceeded(failures);
                    continue;
                }

                ParentDraft? parent = entry.Classification switch
                {
                    LibraryClassification.Package
                        when entry.PackageCoordinate is { } parentCoordinate
                            && uniquePackageEntries.ContainsKey(
                                parentCoordinate) =>
                        ParentDraft.Package(parentCoordinate),
                    LibraryClassification.NonPackage =>
                        ParentDraft.Library(
                            RestoredProjectIdentityText.Opaque(entry.SourceKey)),
                    _ => null,
                };
                if (parent is null)
                    continue;

                var edgeKey = new EdgeDraftKey(parent.Value, coordinate);
                if (edgeDrafts.TryGetValue(
                        edgeKey,
                        out EdgeDraft? existing))
                {
                    edgeDrafts[edgeKey] = existing.AddOccurrence(
                        dependencyName,
                        dependencyVersion);
                }
                else
                {
                    edgeDrafts.Add(
                        edgeKey,
                        new EdgeDraft(
                            parent.Value,
                            coordinate,
                            dependencyName,
                            dependencyVersion,
                            1));
                }
            }
        }

        ImmutableArray<EdgeDraft> edges =
        [
            .. edgeDrafts.Values
                .OrderBy(edge => edge.Parent.OrderKey, StringComparer.Ordinal)
                .ThenBy(
                    edge => edge.Dependency.PackageId,
                    StringComparer.Ordinal)
                .ThenBy(
                    edge => edge.Dependency.Version,
                    StringComparer.Ordinal),
        ];
        return new ProjectionResult(
            packageDrafts.ToImmutable(),
            edges,
            failures,
            LimitExceeded: false);
    }

    private static RuntimeDependencyGraph RescopeGraph(
        RuntimeDependencyManifestIdentity manifest,
        ImmutableArray<PackageDraft> packages,
        ImmutableArray<EdgeDraft> edges,
        ImmutableArray<RuntimeDependencyGraphFailure> failures,
        RuntimeDependencyPhaseCompletion completion)
    {
        ImmutableArray<RuntimeDependencyPackageNode> scopedPackages =
        [
            .. packages.Select(package =>
                new RuntimeDependencyPackageNode(
                    new RuntimeDependencyPackageNodeIdentity(
                        manifest,
                        package.Coordinate),
                    package.SourcePackageIdSpelling,
                    package.SourceVersionSpelling)),
        ];
        ImmutableArray<RuntimeDependencyGraphEdge> scopedEdges =
        [
            .. edges.Select(edge =>
            {
                RuntimeDependencyGraphParentIdentity parent =
                    edge.Parent.PackageCoordinate is { } package
                        ? new RuntimeDependencyGraphParentIdentity.Package(
                            new RuntimeDependencyPackageNodeIdentity(
                                manifest,
                                package))
                        : new RuntimeDependencyGraphParentIdentity.Library(
                            new RuntimeDependencyLibraryNodeIdentity(
                                manifest,
                                edge.Parent.OpaqueIdentity!));
                var dependency = new RuntimeDependencyPackageNodeIdentity(
                    manifest,
                    edge.Dependency);
                return new RuntimeDependencyGraphEdge(
                    new RuntimeDependencyEdgeIdentity(parent, dependency),
                    parent,
                    dependency,
                    Field(edge.SourceDependencyName),
                    Field(edge.SourceDependencyVersion),
                    edge.SourceOccurrenceCount);
            }),
        ];
        return new RuntimeDependencyGraph(
            scopedPackages,
            scopedEdges,
            failures,
            completion);
    }

    private static string ComputeFactsDigest(
        string targetIdentity,
        ImmutableArray<PackageDraft> packages,
        ImmutableArray<EdgeDraft> edges,
        ImmutableArray<RuntimeDependencyGraphFailure> failures,
        RuntimeDependencyPhaseCompletion completion)
    {
        var text = new StringBuilder();
        Field(text, "rdmf/1");
        Field(text, targetIdentity);
        Count(text, (int)completion);
        Count(text, packages.Length);
        foreach (PackageDraft package in packages)
        {
            Field(text, package.Coordinate.PackageId);
            Field(text, package.Coordinate.Version);
        }

        Count(text, edges.Length);
        foreach (EdgeDraft edge in edges)
        {
            Field(text, edge.Parent.OrderKey);
            Field(text, edge.Dependency.PackageId);
            Field(text, edge.Dependency.Version);
            Count(text, edge.SourceOccurrenceCount);
        }

        Count(text, failures.Length);
        foreach (RuntimeDependencyGraphFailure failure in failures)
        {
            Count(text, (int)failure.Reason);
            Count(text, failure.Count);
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static bool TrySplitLibraryKey(
        string key,
        out string name,
        out string version)
    {
        int separator = key.IndexOf('/');
        name = separator < 0 ? "" : key[..separator];
        version = separator < 0 ? "" : key[(separator + 1)..];
        return name.Length > 0
            && version.Length > 0
            && name.Length <= MaxScalarCharacters
            && version.Length <= MaxScalarCharacters;
    }

    private static bool TryCreateLookupKey(
        string name,
        string version,
        out LibraryLookupKey key)
    {
        key = default;
        if (name.Length == 0
            || name.Length > MaxScalarCharacters
            || version.Length == 0
            || version.Length > MaxScalarCharacters
            || !NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            return false;
        }

        key = new LibraryLookupKey(
            name.ToLowerInvariant(),
            parsed.ToNormalizedString().ToLowerInvariant());
        return true;
    }

    private static IEnumerable<JsonProperty> EnumeratePropertiesInCanonicalOrder(
        JsonElement value) =>
        value.EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private static bool ContainsOnlyValidJsonStrings(JsonElement value)
    {
        try
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        _ = property.Name;
                        if (!ContainsOnlyValidJsonStrings(property.Value))
                            return false;
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (!ContainsOnlyValidJsonStrings(item))
                            return false;
                    }

                    break;
                case JsonValueKind.String:
                    _ = value.GetString();
                    break;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value, MaxScalarCharacters);

    private static void Field(StringBuilder text, string value) =>
        text.Append(value.Length).Append(':').Append(value).Append(';');

    private static void Count(StringBuilder text, int value) =>
        text.Append('#').Append(value).Append(';');

    private enum ReadScalarResult
    {
        Success,
        Invalid,
        LimitExceeded,
    }

    private readonly record struct LibraryLookupKey(string Name, string Version);

    private sealed record TargetLibraryDraft(
        string SourceKey,
        string Name,
        string Version,
        JsonElement Value,
        LibraryClassification Classification,
        PackageSourceCoordinate? PackageCoordinate,
        LibraryLookupKey? LookupKey);

    private enum LibraryClassification
    {
        Unknown,
        Package,
        NonPackage,
        CompileOnly,
    }

    private sealed record PackageDraft(
        PackageSourceCoordinate Coordinate,
        InertString SourcePackageIdSpelling,
        InertString SourceVersionSpelling);

    private readonly record struct ParentDraft(
        PackageSourceCoordinate? PackageCoordinate,
        string? OpaqueIdentity)
    {
        public string OrderKey => PackageCoordinate is { } package
            ? $"package:{package.PackageId}/{package.Version}"
            : $"library:{OpaqueIdentity}";

        public static ParentDraft Package(PackageSourceCoordinate coordinate) =>
            new(coordinate, null);

        public static ParentDraft Library(string opaqueIdentity) =>
            new(null, opaqueIdentity);
    }

    private readonly record struct EdgeDraftKey(
        ParentDraft Parent,
        PackageSourceCoordinate Dependency);

    private sealed record EdgeDraft(
        ParentDraft Parent,
        PackageSourceCoordinate Dependency,
        string SourceDependencyName,
        string SourceDependencyVersion,
        int SourceOccurrenceCount)
    {
        public EdgeDraft AddOccurrence(string name, string version)
        {
            bool replace =
                string.CompareOrdinal(name, SourceDependencyName) < 0
                || (string.Equals(
                        name,
                        SourceDependencyName,
                        StringComparison.Ordinal)
                    && string.CompareOrdinal(
                        version,
                        SourceDependencyVersion) < 0);
            return this with
            {
                SourceDependencyName = replace
                    ? name
                    : SourceDependencyName,
                SourceDependencyVersion = replace
                    ? version
                    : SourceDependencyVersion,
                SourceOccurrenceCount = SourceOccurrenceCount + 1,
            };
        }
    }

    private sealed record ProjectionResult(
        ImmutableArray<PackageDraft> Packages,
        ImmutableArray<EdgeDraft> Edges,
        FailureTally Failures,
        bool LimitExceeded)
    {
        public static ProjectionResult Exceeded(FailureTally failures) =>
            new([], [], failures, LimitExceeded: true);
    }

    private sealed class FailureTally
    {
        private readonly Dictionary<RuntimeDependencyGraphFailureReason, int>
            _counts = [];

        public int Total { get; private set; }

        public void Add(
            RuntimeDependencyGraphFailureReason reason,
            int count = 1)
        {
            _counts.TryGetValue(reason, out int current);
            _counts[reason] = checked(current + count);
            Total = checked(Total + count);
        }

        public ImmutableArray<RuntimeDependencyGraphFailure> ToImmutable() =>
        [
            .. _counts
                .OrderBy(pair => pair.Key)
                .Select(pair => new RuntimeDependencyGraphFailure(
                    pair.Key,
                    pair.Value)),
        ];
    }
}
