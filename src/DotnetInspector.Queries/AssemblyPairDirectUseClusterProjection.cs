using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using ILInspector.Metadata;
using DotnetInspector.Services;

namespace DotnetInspector.Queries;

/// <summary>How one direct-use cluster was derived.</summary>
public enum AssemblyPairDirectUseClusterDerivation
{
    ExactBipartiteConnectedComponent,
}

/// <summary>
/// Version-specific identity for one connected component of exact directed
/// source-method to target-method use.
/// </summary>
public sealed record AssemblyPairDirectUseClusterIdentity(
    AssemblyContextSubject Source,
    Guid SourceModuleVersionId,
    int AnchorSourceMethodToken,
    AssemblyContextSubject Target,
    Guid TargetModuleVersionId,
    int AnchorTargetMethodToken);

/// <summary>
/// One deterministic connected component of exact pairwise call-use evidence.
/// </summary>
public sealed record AssemblyPairDirectUseCluster(
    AssemblyPairDirectUseClusterIdentity Identity,
    AssemblyPairDirectUseClusterDerivation Derivation,
    int Ordinal,
    ImmutableArray<Analysis.MethodIdentity> SourceMethods,
    ImmutableArray<Analysis.TypeRef> TargetTypes,
    ImmutableArray<Analysis.MethodIdentity> TargetMethods,
    ImmutableArray<int> OccurrenceIndexes)
{
    public int ExtensionMethodCount =>
        TargetMethods.Count(static method => method.IsExtension);

    public int CallSiteCount => OccurrenceIndexes.Length;
}

/// <summary>
/// Deterministic direct-use clusters whose occurrence indexes address one
/// exact pair result.
/// </summary>
public sealed record AssemblyPairDirectUseClusterProjection(
    AssemblyPairCallUseResult Pair,
    ImmutableArray<AssemblyPairDirectUseCluster> Clusters)
{
    public bool IsComplete => Pair.IsComplete;

    public static AssemblyPairDirectUseClusterProjection Create(
        AssemblyPairCallUseResult pair)
    {
        ArgumentNullException.ThrowIfNull(pair);

        var indexesByDirection =
            new Dictionary<DirectionKey, List<int>>();
        var directions = new List<DirectionKey>();
        for (int index = 0; index < pair.Occurrences.Length; index++)
        {
            AssemblyPairCallUseOccurrence occurrence =
                pair.Occurrences[index];
            var direction = new DirectionKey(
                occurrence.Source.Registration,
                occurrence.SourceModuleVersionId,
                occurrence.Target.Registration,
                occurrence.TargetModuleVersionId);
            if (!indexesByDirection.TryGetValue(
                    direction,
                    out List<int>? indexes))
            {
                indexes = [];
                indexesByDirection.Add(direction, indexes);
                directions.Add(direction);
            }
            indexes.Add(index);
        }

        var clusters =
            ImmutableArray.CreateBuilder<AssemblyPairDirectUseCluster>();
        foreach (DirectionKey direction in directions)
        {
            AddDirectionClusters(
                pair,
                indexesByDirection[direction],
                clusters);
        }
        return new(pair, clusters.ToImmutable());
    }

    static void AddDirectionClusters(
        AssemblyPairCallUseResult pair,
        IReadOnlyList<int> directionIndexes,
        ImmutableArray<AssemblyPairDirectUseCluster>.Builder clusters)
    {
        var bySourceMethod = new Dictionary<int, List<int>>();
        var byTargetMethod = new Dictionary<int, List<int>>();
        foreach (int index in directionIndexes)
        {
            AssemblyPairCallUseOccurrence occurrence =
                pair.Occurrences[index];
            AddIndex(
                bySourceMethod,
                occurrence.SourceMethod.MetadataToken,
                index);
            AddIndex(
                byTargetMethod,
                occurrence.TargetMethod.MetadataToken,
                index);
        }

        var visited = new HashSet<int>();
        int ordinal = 0;
        foreach (int seedIndex in directionIndexes)
        {
            if (!visited.Add(seedIndex))
                continue;

            ordinal++;
            var componentIndexes = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(seedIndex);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                componentIndexes.Add(index);
                AssemblyPairCallUseOccurrence occurrence =
                    pair.Occurrences[index];
                EnqueueUnvisited(
                    bySourceMethod,
                    occurrence.SourceMethod.MetadataToken,
                    visited,
                    queue);
                EnqueueUnvisited(
                    byTargetMethod,
                    occurrence.TargetMethod.MetadataToken,
                    visited,
                    queue);
            }

            componentIndexes.Sort();
            clusters.Add(
                CreateCluster(
                    pair,
                    ordinal,
                    componentIndexes));
        }
    }

    static AssemblyPairDirectUseCluster CreateCluster(
        AssemblyPairCallUseResult pair,
        int ordinal,
        IReadOnlyList<int> componentIndexes)
    {
        AssemblyPairCallUseOccurrence first =
            pair.Occurrences[componentIndexes[0]];
        var sourceMethods = new List<Analysis.MethodIdentity>();
        var sourceMethodSet = new HashSet<Analysis.MethodIdentity>();
        var targetTypes = new List<Analysis.TypeRef>();
        var targetTypeSet = new HashSet<Analysis.TypeRef>();
        var targetMethods = new List<Analysis.MethodIdentity>();
        var targetMethodSet = new HashSet<Analysis.MethodIdentity>();
        int anchorSourceMethodToken = int.MaxValue;
        int anchorTargetMethodToken = int.MaxValue;

        foreach (int index in componentIndexes)
        {
            AssemblyPairCallUseOccurrence occurrence =
                pair.Occurrences[index];
            if (sourceMethodSet.Add(occurrence.SourceMethod))
                sourceMethods.Add(occurrence.SourceMethod);
            Analysis.TypeRef targetType =
                occurrence.TargetMethod.DeclaringType;
            if (targetTypeSet.Add(targetType))
                targetTypes.Add(targetType);
            if (targetMethodSet.Add(occurrence.TargetMethod))
                targetMethods.Add(occurrence.TargetMethod);
            anchorSourceMethodToken = Math.Min(
                anchorSourceMethodToken,
                occurrence.SourceMethod.MetadataToken);
            anchorTargetMethodToken = Math.Min(
                anchorTargetMethodToken,
                occurrence.TargetMethod.MetadataToken);
        }

        return new AssemblyPairDirectUseCluster(
            new AssemblyPairDirectUseClusterIdentity(
                first.Source,
                first.SourceModuleVersionId,
                anchorSourceMethodToken,
                first.Target,
                first.TargetModuleVersionId,
                anchorTargetMethodToken),
            AssemblyPairDirectUseClusterDerivation
                .ExactBipartiteConnectedComponent,
            ordinal,
            [.. sourceMethods],
            [.. targetTypes],
            [.. targetMethods],
            [.. componentIndexes]);
    }

    static void AddIndex(
        Dictionary<int, List<int>> indexesByMethod,
        int methodToken,
        int occurrenceIndex)
    {
        if (!indexesByMethod.TryGetValue(
                methodToken,
                out List<int>? indexes))
        {
            indexes = [];
            indexesByMethod.Add(methodToken, indexes);
        }
        indexes.Add(occurrenceIndex);
    }

    static void EnqueueUnvisited(
        Dictionary<int, List<int>> indexesByMethod,
        int methodToken,
        HashSet<int> visited,
        Queue<int> queue)
    {
        if (!indexesByMethod.Remove(
                methodToken,
                out List<int>? indexes))
        {
            return;
        }

        foreach (int index in indexes)
        {
            if (visited.Add(index))
                queue.Enqueue(index);
        }
    }

    readonly record struct DirectionKey(
        AssemblyAcquisitionRegistration Source,
        Guid SourceModuleVersionId,
        AssemblyAcquisitionRegistration Target,
        Guid TargetModuleVersionId);
}
