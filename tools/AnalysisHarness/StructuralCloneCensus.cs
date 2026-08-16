using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public enum StructuralCloneCensusSeedStatus
{
    Clustered,
    Singleton,
    Unsupported,
    LimitReached,
    Failed,
    Unresolved,
}

public sealed record StructuralCloneCensusMethod(
    int Token,
    string Type,
    string Name);

public sealed record StructuralCloneCensusCluster(
    string Identity,
    ImmutableArray<StructuralCloneCensusMethod> Members,
    int UniqueCorrespondences,
    int AmbiguousCorrespondences);

public sealed record StructuralCloneCensusSuppressedBucket(
    ImmutableArray<StructuralCloneCensusMethod> Methods,
    StructuralCloneDiscoveryBlocker Reason);

public sealed record StructuralCloneCensusMethodOutcome(
    StructuralCloneCensusMethod Method,
    StructuralCloneDisposition Disposition,
    ImmutableArray<StructuralCloneDiscoveryBlocker> Blockers,
    StructuralCloneMethodReceipt Receipt);

public sealed record StructuralCloneCensusUnresolvedComparison(
    StructuralCloneCensusMethod Left,
    StructuralCloneCensusMethod Right,
    StructuralCloneDisposition Disposition,
    ImmutableArray<StructuralCloneBlocker> Blockers,
    StructuralCloneVerificationReceipt Receipt);

public sealed record StructuralCloneCensusSeed(
    string Selector,
    StructuralCloneCensusMethod Method,
    StructuralCloneCensusSeedStatus Status,
    StructuralCloneDisposition? ProductionDisposition,
    ImmutableArray<StructuralCloneDiscoveryBlocker> Blockers,
    StructuralCloneCensusCluster? Cluster);

public sealed record StructuralCloneCensusReport(
    string Assembly,
    Guid? ModuleVersionId,
    int MaximumMethods,
    int MaximumCandidateComparisons,
    StructuralCloneDiscoveryDisposition Disposition,
    ImmutableArray<StructuralCloneDiscoveryBlocker> Blockers,
    StructuralCloneDiscoveryReceipt Receipt,
    IReadOnlyDictionary<string, int> MethodDispositionCounts,
    IReadOnlyDictionary<string, int> MethodBlockerCounts,
    ImmutableArray<StructuralCloneCensusMethodOutcome> NonCompletedMethods,
    ImmutableArray<StructuralCloneCensusUnresolvedComparison>
        UnresolvedComparisons,
    int Clusters,
    int ClusteredMethods,
    int EligibleWithoutEmittedCluster,
    int? ExactSingletonMethods,
    int LargestCluster,
    ImmutableArray<StructuralCloneCensusCluster> Families,
    ImmutableArray<StructuralCloneCensusSuppressedBucket> SuppressedBuckets,
    StructuralCloneCensusSeed? Seed,
    long DiscoveryElapsedMilliseconds)
{
    public bool Success =>
        Disposition == StructuralCloneDiscoveryDisposition.Completed;
}

public static class StructuralCloneCensus
{
    static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false),
        },
    };

    public static StructuralCloneCensusReport Run(
        string assemblyPath,
        string? seedSelector = null,
        int maximumMethods = 50_000,
        int maximumCandidateComparisons = 100_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMethods, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumCandidateComparisons,
            1);

        string fullPath = Path.GetFullPath(assemblyPath);
        using var stream = File.OpenRead(fullPath);
        using var image = new PEReader(stream);
        MetadataReader reader = GetMetadataReader(image, fullPath);
        ImmutableArray<MethodDefinitionHandle> population =
            ImmutableArray.CreateRange(reader.MethodDefinitions);
        MethodDefinitionHandle? seed = seedSelector is null
            ? null
            : ResolveSeed(reader, seedSelector);

        var limits = new StructuralCloneDiscoveryLimits(
            maximumMethods,
            maximumCandidateComparisons);
        Stopwatch stopwatch = Stopwatch.StartNew();
        StructuralCloneDiscoveryResult discovery =
            StructuralCloneAnalysis.Discover(image, population, limits);
        stopwatch.Stop();

        var methodCache =
            new Dictionary<MethodDefinitionHandle, StructuralCloneCensusMethod>();
        StructuralCloneCensusMethod Project(MethodDefinitionHandle handle)
        {
            if (methodCache.TryGetValue(
                    handle,
                    out StructuralCloneCensusMethod? method))
            {
                return method;
            }
            MethodDefinition definition = reader.GetMethodDefinition(handle);
            method = new StructuralCloneCensusMethod(
                MetadataTokens.GetToken(handle),
                TypeName(reader, definition.GetDeclaringType()),
                reader.GetString(definition.Name));
            methodCache.Add(handle, method);
            return method;
        }

        ImmutableArray<StructuralCloneCensusCluster> families =
        [
            .. discovery.Clusters
                .Select(cluster =>
                    new StructuralCloneCensusCluster(
                        cluster.Identity.ToString(),
                        [
                            .. cluster.Members.Select(member =>
                                Project(member.Handle)),
                        ],
                        cluster.Evidence.Count(static comparison =>
                            comparison.Correspondence?.Kind
                                == StructuralCloneCorrespondenceKind.Unique),
                        cluster.Evidence.Count(static comparison =>
                            comparison.Correspondence?.Kind
                                == StructuralCloneCorrespondenceKind.Ambiguous)))
                .OrderByDescending(static cluster => cluster.Members.Length)
                .ThenBy(static cluster => cluster.Members[0].Token),
        ];
        ImmutableArray<StructuralCloneCensusSuppressedBucket>
            suppressedBuckets =
        [
            .. discovery.SuppressedBuckets.Select(bucket =>
                new StructuralCloneCensusSuppressedBucket(
                    [
                        .. bucket.Methods.Select(method =>
                            Project(method.Handle)),
                    ],
                    bucket.Reason)),
        ];
        ImmutableArray<StructuralCloneCensusMethodOutcome>
            nonCompletedMethods =
        [
            .. discovery.Methods
                .Where(static method =>
                    method.Disposition
                        != StructuralCloneDisposition.Completed)
                .Select(method =>
                    new StructuralCloneCensusMethodOutcome(
                        Project(method.Method.Handle),
                        method.Disposition,
                        method.Blockers,
                        method.Receipt))
                .OrderBy(static method =>
                    NonCompletedOrder(method.Disposition))
                .ThenBy(static method => method.Method.Token),
        ];
        ImmutableArray<StructuralCloneCensusUnresolvedComparison>
            unresolvedComparisons =
        [
            .. discovery.UnresolvedComparisons
                .Select(comparison =>
                    new StructuralCloneCensusUnresolvedComparison(
                        Project(comparison.Left.Handle),
                        Project(comparison.Right.Handle),
                        comparison.Disposition,
                        comparison.Blockers,
                        comparison.Receipt))
                .OrderBy(static comparison => comparison.Left.Token)
                .ThenBy(static comparison => comparison.Right.Token),
        ];
        int clusteredMethods = families.Sum(static cluster =>
            cluster.Members.Length);
        int eligibleWithoutCluster =
            discovery.Receipt.EligibleMethods - clusteredMethods;
        StructuralCloneCensusSeed? seedResult = seed is { } seedHandle
            ? ProjectSeed(
                seedSelector!,
                seedHandle,
                Project(seedHandle),
                discovery,
                families)
            : null;
        Guid? moduleVersionId =
            discovery.Methods.IsEmpty
                ? families.IsEmpty
                    ? null
                    : discovery.Clusters[0].Identity.ModuleVersionId
                : discovery.Methods[0].Method.ModuleVersionId;

        return new StructuralCloneCensusReport(
            fullPath,
            moduleVersionId,
            maximumMethods,
            maximumCandidateComparisons,
            discovery.Disposition,
            discovery.Blockers,
            discovery.Receipt,
            Counts(
                discovery.Methods,
                static method => method.Disposition.ToString()),
            Counts(
                discovery.Methods.SelectMany(static method =>
                    method.Blockers),
                static blocker => blocker.Kind.ToString()),
            nonCompletedMethods,
            unresolvedComparisons,
            families.Length,
            clusteredMethods,
            eligibleWithoutCluster,
            discovery.Disposition
                == StructuralCloneDiscoveryDisposition.Completed
                ? eligibleWithoutCluster
                : null,
            families.IsEmpty ? 0 : families[0].Members.Length,
            families,
            suppressedBuckets,
            seedResult,
            stopwatch.ElapsedMilliseconds);
    }

    public static string ToJson(StructuralCloneCensusReport report)
        => JsonSerializer.Serialize(report, s_json);

    public static string Format(
        StructuralCloneCensusReport report,
        int top = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        StringBuilder output = new();
        output.Append("EXACT CLONE CENSUS: ");
        output.Append(report.Disposition);
        output.Append(' ');
        output.AppendLine(Path.GetFileName(report.Assembly));
        output.Append("  limits: methods=");
        output.Append(report.MaximumMethods);
        output.Append(" candidate-comparisons=");
        output.AppendLine(
            report.MaximumCandidateComparisons.ToString(
                CultureInfo.InvariantCulture));
        output.Append("  methods: input=");
        output.Append(report.Receipt.InputMethods);
        output.Append(" processed=");
        output.Append(report.Receipt.ProcessedMethods);
        output.Append(" eligible=");
        output.Append(report.Receipt.EligibleMethods);
        output.Append(" unsupported=");
        output.Append(report.Receipt.UnsupportedMethods);
        output.Append(" limited=");
        output.Append(report.Receipt.LimitReachedMethods);
        output.Append(" failed=");
        output.AppendLine(
            report.Receipt.FailedMethods.ToString(
                CultureInfo.InvariantCulture));
        output.Append("  families: clusters=");
        output.Append(report.Clusters);
        output.Append(" clustered-methods=");
        output.Append(report.ClusteredMethods);
        output.Append(" largest=");
        output.Append(report.LargestCluster);
        output.Append(" exact-singletons=");
        output.AppendLine(
            report.ExactSingletonMethods?.ToString(
                CultureInfo.InvariantCulture)
            ?? "<unresolved>");
        output.Append("  candidates: buckets=");
        output.Append(report.Receipt.CandidateBuckets);
        output.Append(" completed=");
        output.Append(report.Receipt.CompletedCandidateBuckets);
        output.Append(" suppressed=");
        output.Append(report.Receipt.SuppressedCandidateBuckets);
        output.Append(" comparisons=");
        output.Append(report.Receipt.CandidateComparisons);
        output.Append(" exact=");
        output.Append(report.Receipt.ExactComparisons);
        output.Append(" different=");
        output.Append(report.Receipt.DifferentComparisons);
        output.Append(" unresolved=");
        output.AppendLine(
            report.Receipt.UnresolvedComparisons.ToString(
                CultureInfo.InvariantCulture));
        output.Append("  body-productions=");
        output.Append(report.Receipt.BodyProductions);
        output.Append(" elapsed-ms=");
        output.AppendLine(
            report.DiscoveryElapsedMilliseconds.ToString(
                CultureInfo.InvariantCulture));

        foreach (StructuralCloneDiscoveryBlocker blocker in report.Blockers)
        {
            output.Append("  blocker: ");
            output.Append(blocker.Kind);
            output.Append(": ");
            output.AppendLine(blocker.Detail);
        }

        foreach (IGrouping<StructuralCloneDisposition,
            StructuralCloneCensusMethodOutcome> group
            in report.NonCompletedMethods.GroupBy(static method =>
                method.Disposition))
        {
            output.Append("  ");
            output.Append(group.Key);
            output.Append(" methods (top ");
            output.Append(top);
            output.AppendLine("):");
            foreach (StructuralCloneCensusMethodOutcome method
                in group.Take(top))
            {
                output.Append("    ");
                output.AppendLine(MethodDisplay(method.Method));
                foreach (StructuralCloneDiscoveryBlocker blocker
                    in method.Blockers)
                {
                    output.Append("      ");
                    output.Append(blocker.Kind);
                    output.Append(": ");
                    output.AppendLine(blocker.Detail);
                }
            }
            int omittedMethods =
                group.Count() - Math.Min(group.Count(), top);
            if (omittedMethods > 0)
            {
                output.Append("    ... ");
                output.Append(omittedMethods);
                output.AppendLine(" more methods omitted");
            }
        }

        if (!report.UnresolvedComparisons.IsEmpty)
        {
            output.Append("  unresolved comparisons (top ");
            output.Append(top);
            output.AppendLine("):");
            foreach (StructuralCloneCensusUnresolvedComparison comparison
                in report.UnresolvedComparisons.Take(top))
            {
                output.Append("    ");
                output.Append(MethodDisplay(comparison.Left));
                output.Append(" <> ");
                output.AppendLine(MethodDisplay(comparison.Right));
                foreach (StructuralCloneBlocker blocker
                    in comparison.Blockers)
                {
                    output.Append("      ");
                    output.Append(blocker.Kind);
                    output.Append(": ");
                    output.AppendLine(blocker.Detail);
                }
            }
            int omittedComparisons =
                report.UnresolvedComparisons.Length
                    - Math.Min(report.UnresolvedComparisons.Length, top);
            if (omittedComparisons > 0)
            {
                output.Append("    ... ");
                output.Append(omittedComparisons);
                output.AppendLine(" more comparisons omitted");
            }
        }

        if (!report.SuppressedBuckets.IsEmpty)
        {
            output.Append("  suppressed buckets (top ");
            output.Append(top);
            output.AppendLine("):");
            foreach (StructuralCloneCensusSuppressedBucket bucket
                in report.SuppressedBuckets.Take(top))
            {
                output.Append("    ");
                output.Append(bucket.Reason.Kind);
                output.Append(": ");
                output.AppendLine(bucket.Reason.Detail);
                foreach (StructuralCloneCensusMethod method
                    in bucket.Methods.Take(top))
                {
                    output.Append("      ");
                    output.AppendLine(MethodDisplay(method));
                }
                int omittedMethods =
                    bucket.Methods.Length
                        - Math.Min(bucket.Methods.Length, top);
                if (omittedMethods > 0)
                {
                    output.Append("      ... ");
                    output.Append(omittedMethods);
                    output.AppendLine(" more methods omitted");
                }
            }
            int omittedBuckets =
                report.SuppressedBuckets.Length
                    - Math.Min(report.SuppressedBuckets.Length, top);
            if (omittedBuckets > 0)
            {
                output.Append("    ... ");
                output.Append(omittedBuckets);
                output.AppendLine(" more buckets omitted");
            }
        }

        if (report.Seed is { } seed)
        {
            output.Append("  seed ");
            output.Append(seed.Status);
            output.Append(": ");
            output.AppendLine(MethodDisplay(seed.Method));
            foreach (StructuralCloneDiscoveryBlocker blocker
                in seed.Blockers)
            {
                output.Append("    ");
                output.Append(blocker.Kind);
                output.Append(": ");
                output.AppendLine(blocker.Detail);
            }
            if (seed.Cluster is { } family)
            {
                output.AppendLine("    exact family:");
                AppendFamily(
                    output,
                    family,
                    top,
                    "      ",
                    seed.Method);
            }
        }

        output.Append("  exact families (top ");
        output.Append(top);
        output.AppendLine("):");
        HashSet<string> emitted = [];
        if (report.Seed?.Cluster is { } seedFamily)
            emitted.Add(seedFamily.Identity);
        int familyCount = 0;
        foreach (StructuralCloneCensusCluster family in report.Families)
        {
            if (emitted.Contains(family.Identity))
                continue;
            if (familyCount == top)
                break;
            AppendFamily(output, family, top, "    ");
            emitted.Add(family.Identity);
            familyCount++;
        }
        int omitted = report.Families.Length - emitted.Count;
        if (omitted > 0)
        {
            output.Append("    ... ");
            output.Append(omitted);
            output.AppendLine(" more families omitted");
        }
        return output.ToString();
    }

    static MetadataReader GetMetadataReader(
        PEReader image,
        string assemblyPath)
    {
        try
        {
            if (!image.HasMetadata)
            {
                throw new InvalidDataException(
                    $"The clone census target is not a managed assembly: "
                        + assemblyPath);
            }
            return image.GetMetadataReader();
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            throw new InvalidDataException(
                $"The clone census target has invalid managed metadata: "
                    + assemblyPath,
                ex);
        }
    }

    static MethodDefinitionHandle ResolveSeed(
        MetadataReader reader,
        string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new InvalidDataException(
                "A clone census seed selector cannot be empty.");
        }

        if (selector.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(
                    selector.AsSpan(2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out int token)
                || (token & unchecked((int)0xFF000000))
                    != 0x06000000)
            {
                throw InvalidSeed(selector);
            }
            int row = token & 0x00FFFFFF;
            if (row < 1
                || row > reader.GetTableRowCount(TableIndex.MethodDef))
            {
                throw InvalidSeed(selector);
            }
            return MetadataTokens.MethodDefinitionHandle(row);
        }

        int separator = selector.LastIndexOf(
            "::",
            StringComparison.Ordinal);
        if (separator <= 0 || separator == selector.Length - 2)
            throw InvalidSeed(selector);
        string typeName = selector[..separator];
        string methodName = selector[(separator + 2)..];
        MethodDefinitionHandle[] matches =
        [
            .. reader.MethodDefinitions.Where(handle =>
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                return StringComparer.Ordinal.Equals(
                        reader.GetString(method.Name),
                        methodName)
                    && StringComparer.Ordinal.Equals(
                        TypeName(reader, method.GetDeclaringType()),
                        typeName);
            }),
        ];
        if (matches.Length == 1)
            return matches[0];
        if (matches.Length == 0)
            throw InvalidSeed(selector);

        string tokens = string.Join(
            ", ",
            matches.Select(static handle =>
                $"0x{MetadataTokens.GetToken(handle):X8}"));
        throw new InvalidDataException(
            $"Clone census seed '{selector}' is ambiguous; "
                + $"use one of these MethodDef tokens: {tokens}.");
    }

    static StructuralCloneCensusSeed ProjectSeed(
        string selector,
        MethodDefinitionHandle seed,
        StructuralCloneCensusMethod method,
        StructuralCloneDiscoveryResult discovery,
        ImmutableArray<StructuralCloneCensusCluster> families)
    {
        StructuralCloneCensusCluster? family =
            families.FirstOrDefault(cluster =>
                cluster.Members.Any(member =>
                    member.Token == MetadataTokens.GetToken(seed)));
        StructuralCloneMethodOutcome? outcome =
            discovery.Methods.FirstOrDefault(item =>
                item.Method.Handle == seed);
        StructuralCloneCensusSeedStatus status =
            family is not null
                ? StructuralCloneCensusSeedStatus.Clustered
                : outcome?.Disposition switch
                {
                    StructuralCloneDisposition.Unsupported =>
                        StructuralCloneCensusSeedStatus.Unsupported,
                    StructuralCloneDisposition.LimitReached =>
                        StructuralCloneCensusSeedStatus.LimitReached,
                    StructuralCloneDisposition.Failed =>
                        StructuralCloneCensusSeedStatus.Failed,
                    StructuralCloneDisposition.Completed
                        when discovery.Disposition
                            == StructuralCloneDiscoveryDisposition.Completed =>
                        StructuralCloneCensusSeedStatus.Singleton,
                    _ => StructuralCloneCensusSeedStatus.Unresolved,
                };
        return new StructuralCloneCensusSeed(
            selector,
            method,
            status,
            outcome?.Disposition,
            outcome?.Blockers
                ?? ImmutableArray<StructuralCloneDiscoveryBlocker>.Empty,
            family);
    }

    static string TypeName(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        List<string> segments = [];
        string @namespace = "";
        int remaining = reader.GetTableRowCount(TableIndex.TypeDef) + 1;
        while (!handle.IsNil && remaining-- > 0)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            segments.Add(reader.GetString(type.Name));
            TypeDefinitionHandle declaringType = type.GetDeclaringType();
            if (declaringType.IsNil)
                @namespace = reader.GetString(type.Namespace);
            handle = declaringType;
        }
        if (!handle.IsNil)
        {
            throw new BadImageFormatException(
                "A nested type declaration cycle was detected.");
        }
        segments.Reverse();
        string name = string.Join("+", segments);
        return string.IsNullOrEmpty(@namespace)
            ? name
            : $"{@namespace}.{name}";
    }

    static InvalidDataException InvalidSeed(string selector)
        => new(
            $"Could not resolve clone census seed '{selector}'. "
                + "Use 0x followed by a full MethodDef token, or a unique "
                + "Type::Method selector.");

    static IReadOnlyDictionary<string, int> Counts<T>(
        IEnumerable<T> values,
        Func<T, string> key)
        => values
            .GroupBy(key, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);

    static int NonCompletedOrder(StructuralCloneDisposition disposition)
        => disposition switch
        {
            StructuralCloneDisposition.Failed => 0,
            StructuralCloneDisposition.LimitReached => 1,
            _ => 2,
        };

    static void AppendFamily(
        StringBuilder output,
        StructuralCloneCensusCluster family,
        int top,
        string indent,
        StructuralCloneCensusMethod? pinnedSeed = null)
    {
        output.Append(indent);
        output.Append("family size=");
        output.Append(family.Members.Length);
        output.Append(" anchor=0x");
        output.Append(family.Members[0].Token.ToString(
            "X8",
            CultureInfo.InvariantCulture));
        output.Append(" correspondence=");
        output.Append(family.UniqueCorrespondences);
        output.Append(" unique/");
        output.Append(family.AmbiguousCorrespondences);
        output.AppendLine(" ambiguous");
        IEnumerable<StructuralCloneCensusMethod> members =
            pinnedSeed is null
                ? family.Members
                :
                [
                    pinnedSeed,
                    .. family.Members.Where(member =>
                        member.Token != pinnedSeed.Token),
                ];
        int memberLimit = pinnedSeed is null
            ? top
            : Math.Max(top, Math.Min(2, family.Members.Length));
        foreach (StructuralCloneCensusMethod member
            in members.Take(memberLimit))
        {
            output.Append(indent);
            output.Append("  ");
            output.AppendLine(MethodDisplay(member));
        }
        int omitted = family.Members.Length - Math.Min(
            family.Members.Length,
            memberLimit);
        if (omitted > 0)
        {
            output.Append(indent);
            output.Append("  ... ");
            output.Append(omitted);
            output.AppendLine(" more members omitted");
        }
    }

    static string MethodDisplay(StructuralCloneCensusMethod method)
        => $"0x{method.Token:X8} {method.Type}::{method.Name}";
}
