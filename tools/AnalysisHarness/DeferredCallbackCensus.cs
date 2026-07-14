using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public enum DeferredCallbackSiteClassification
{
    ConstructionOutsideLoop,
    StandaloneFunctionLoad,
    CachedConstruction,
    ConstructedWithoutImmediateConsumer,
    ImmediateInvocation,
    UnknownConsumer,
    FrameworkRegistration,
}

public enum DeferredCallbackReachClassification
{
    None,
    Target,
    Downstream,
    BeyondBound,
}

public sealed record DeferredCallbackSite(
    string Assembly,
    string Caller,
    int CallerToken,
    string Target,
    int TargetToken,
    int FunctionLoadOffset,
    CallKind FunctionLoadKind,
    int? ConstructionOffset,
    string DelegateType,
    int? ConsumerOffset,
    string Consumer,
    string ConsumerKind,
    bool ConsumptionProven,
    DeferredCallbackSiteClassification Classification);

public sealed record DeferredCallbackInvocationStep(
    string Caller,
    string Callee,
    int ILOffset,
    CallKind Kind);

public sealed record DeferredCallbackCensusRow(
    string Assembly,
    string Candidate,
    string Member,
    int MethodToken,
    int? ILOffset,
    string Shape,
    string Confidence,
    string LocalMultiplicity,
    bool LocalInLoop,
    PerformanceTriageProvenance Provenance,
    DeferredCallbackReachClassification Classification,
    int? DownstreamDepth,
    DeferredCallbackSite? Construction,
    IReadOnlyList<DeferredCallbackInvocationStep> InvocationWitness);

public sealed record DeferredCallbackCensusFailure(string AssemblyPath, string Error);

public sealed record DeferredCallbackCensusReport(
    int MaxDepth,
    int Assemblies,
    int Opened,
    int Failed,
    int FunctionLoads,
    int Opportunities,
    int ReachableOpportunities,
    int DistinctReachableCandidates,
    int DistinctReachableMethods,
    IReadOnlyDictionary<string, int> SitesByClassification,
    IReadOnlyDictionary<string, int> SitesByConsumer,
    IReadOnlyDictionary<string, int> RowsByClassification,
    IReadOnlyDictionary<string, int> OpportunityCross,
    IReadOnlyList<DeferredCallbackCensusFailure> Failures,
    IReadOnlyList<DeferredCallbackSite> Sites,
    IReadOnlyList<DeferredCallbackCensusRow> Rows);

public static class DeferredCallbackCensus
{
    const string Empty = "<empty>";

    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static DeferredCallbackCensusReport Measure(
        IReadOnlyList<string> assemblyPaths,
        int maxDepth = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 0);

        var sites = new List<DeferredCallbackSite>();
        var rows = new List<DeferredCallbackCensusRow>();
        var failures = new List<DeferredCallbackCensusFailure>();
        int opened = 0;
        foreach (string path in assemblyPaths)
        {
            try
            {
                var index = LibraryBodyIndex.Open(path);
                var result = Analyze(
                    Path.GetFileName(path),
                    index.Methods,
                    index.DirectCalls,
                    index.GetAllocationOccurrences(),
                    index.OptimizationOpportunities,
                    maxDepth);
                sites.AddRange(result.Sites);
                rows.AddRange(result.Rows);
                opened++;
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or ArgumentException)
            {
                failures.Add(new DeferredCallbackCensusFailure(path, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        return BuildReport(
            maxDepth,
            assemblyPaths.Count,
            opened,
            failures,
            sites,
            rows);
    }

    public static (
        IReadOnlyList<DeferredCallbackSite> Sites,
        IReadOnlyList<DeferredCallbackCensusRow> Rows) Analyze(
        string assembly,
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> allocations,
        ImmutableArray<OptimizationOpportunity> opportunities,
        int maxDepth = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 0);

        var callsByCoordinate = directCalls.ToDictionary(
            static call => (call.Caller.MetadataToken, call.ILOffset));
        var allocationsByCoordinate = allocations
            .SelectMany(static pair => pair.Value)
            .ToDictionary(
                static occurrence => (occurrence.Method.MetadataToken, occurrence.ILOffset));

        var evidenceSites = directCalls
            .Where(static call => call.Kind is CallKind.LoadFunction or CallKind.LoadVirtualFunction)
            .OrderBy(static call => call.Caller.MetadataToken)
            .ThenBy(static call => call.ILOffset)
            .Select(call => ClassifySite(
                assembly,
                call,
                callsByCoordinate,
                allocationsByCoordinate))
            .ToArray();

        var coherentSites = evidenceSites
            .Where(static site => site.Row.Classification is
                DeferredCallbackSiteClassification.ImmediateInvocation
                or DeferredCallbackSiteClassification.UnknownConsumer
                or DeferredCallbackSiteClassification.FrameworkRegistration)
            .ToArray();
        var siteBySeed = coherentSites.ToDictionary(
            static site => (site.Load.Caller.MetadataToken, site.Load.ILOffset));

        // Keep evidence classes separate so a nearer unknown consumer cannot hide a proven
        // invocation or trusted registration. Within each class, the product analysis still
        // selects the nearest deterministic witness.
        var nearestByPriority = new[]
        {
            FindNearest(
                methods,
                directCalls,
                coherentSites.Where(static site =>
                    site.Row.Classification == DeferredCallbackSiteClassification.ImmediateInvocation)),
            FindNearest(
                methods,
                directCalls,
                coherentSites.Where(static site =>
                    site.Row.Classification == DeferredCallbackSiteClassification.FrameworkRegistration)),
            FindNearest(
                methods,
                directCalls,
                coherentSites.Where(static site =>
                    site.Row.Classification == DeferredCallbackSiteClassification.UnknownConsumer)),
        };

        var rows = opportunities
            .OrderBy(static opportunity => opportunity.Method.MetadataToken)
            .ThenBy(static opportunity => opportunity.ILOffset ?? -1)
            .ThenBy(static opportunity => opportunity.Shape, StringComparer.Ordinal)
            .Select(opportunity => CreateRow(
                assembly,
                opportunity,
                nearestByPriority,
                siteBySeed,
                maxDepth))
            .ToArray();

        return (evidenceSites.Select(static site => site.Row).ToArray(), rows);
    }

    public static string ToJson(DeferredCallbackCensusReport report)
        => JsonSerializer.Serialize(report, s_json);

    public static string FormatCard(DeferredCallbackCensusReport report, int top)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DEFERRED-CALLBACK CENSUS: {report.Opened}/{report.Assemblies} assemblies opened ({report.Failed} failed), max-downstream-depth={report.MaxDepth}");
        sb.AppendLine($"  function-loads={report.FunctionLoads} opportunities={report.Opportunities} reachable={report.ReachableOpportunities} distinct-candidates={report.DistinctReachableCandidates} distinct-methods={report.DistinctReachableMethods}");
        AppendCounts(sb, "sites by classification", report.SitesByClassification);
        AppendCounts(sb, "sites by consumer", report.SitesByConsumer);
        AppendCounts(sb, "opportunity rows", report.RowsByClassification);
        AppendCounts(sb, "callback|consumer|shape|confidence|local-multiplicity|provenance", report.OpportunityCross, top);
        foreach (var failure in report.Failures)
            sb.AppendLine($"  failed: {failure.AssemblyPath}: {failure.Error}");

        sb.AppendLine("  examples:");
        foreach (var row in report.Rows
            .Where(static row => row.Classification != DeferredCallbackReachClassification.None)
            .OrderByDescending(static row => row.Construction?.ConsumptionProven)
            .ThenBy(static row => row.Classification)
            .ThenBy(static row => row.DownstreamDepth)
            .ThenBy(static row => row.Assembly, StringComparer.Ordinal)
            .ThenBy(static row => row.Member, StringComparer.Ordinal)
            .Take(top))
        {
            var site = row.Construction!;
            sb.AppendLine($"    {Reach(row.Classification)} depth={row.DownstreamDepth}: {row.Assembly} {row.Member} [{row.Shape}, {row.Candidate}]");
            sb.AppendLine($"      {SiteClass(site.Classification)}: {site.Caller} @ IL_{site.FunctionLoadOffset:X4} -- {site.FunctionLoadKind} --> {site.Target}; newobj IL_{site.ConstructionOffset:X4}; {site.ConsumerKind} {site.Consumer}");
            foreach (var step in row.InvocationWitness)
                sb.AppendLine($"      -> {step.Caller} @ IL_{step.ILOffset:X4} -- {step.Kind} --> {step.Callee}");
        }
        return sb.ToString();
    }

    static SiteEvidence ClassifySite(
        string assembly,
        DirectCall load,
        IReadOnlyDictionary<(int MethodToken, int Offset), DirectCall> calls,
        IReadOnlyDictionary<(int MethodToken, int Offset), AllocationOccurrence> allocations)
    {
        DirectCall? construction = null;
        AllocationOccurrence? allocation = null;
        DirectCall? consumer = null;
        if (load.ReturnAddress is { } constructionOffset
            && calls.TryGetValue((load.Caller.MetadataToken, constructionOffset), out var candidateConstruction)
            && candidateConstruction.Kind == CallKind.NewObject
            && allocations.TryGetValue((load.Caller.MetadataToken, constructionOffset), out var candidateAllocation)
            && candidateAllocation.Source == AllocationFactSource.Newobj
            && candidateAllocation.CountsAsHeapAllocation
            && candidateAllocation.AllocatedType?.Equals(candidateConstruction.Callee.DeclaringType) == true
            && IsDelegateConstructorShape(candidateConstruction.Callee))
        {
            construction = candidateConstruction;
            allocation = candidateAllocation;
            if (construction.ReturnAddress is { } consumerOffset
                && calls.TryGetValue((load.Caller.MetadataToken, consumerOffset), out var candidateConsumer)
                && candidateConsumer.Kind is CallKind.Call or CallKind.CallVirtual)
            {
                consumer = candidateConsumer;
            }
        }

        DeferredCallbackSiteClassification classification;
        string consumerKind = Empty;
        bool consumptionProven = false;
        if (construction is null || allocation is null)
        {
            classification = DeferredCallbackSiteClassification.StandaloneFunctionLoad;
        }
        else if (!load.InLoop)
        {
            classification = DeferredCallbackSiteClassification.ConstructionOutsideLoop;
        }
        else if (!construction.InLoop)
        {
            classification = DeferredCallbackSiteClassification.ConstructedWithoutImmediateConsumer;
        }
        else if (allocation.Frequency == AllocationFrequency.CachedOnce)
        {
            classification = DeferredCallbackSiteClassification.CachedConstruction;
        }
        else if (consumer is null)
        {
            classification = DeferredCallbackSiteClassification.ConstructedWithoutImmediateConsumer;
        }
        else if (!consumer.InLoop)
        {
            classification = DeferredCallbackSiteClassification.ConstructedWithoutImmediateConsumer;
        }
        else if (IsImmediateInvoke(consumer))
        {
            classification = DeferredCallbackSiteClassification.ImmediateInvocation;
            consumerKind = "delegate-invoke";
            consumptionProven = true;
        }
        else if (IsRenderTreeRegistration(consumer))
        {
            classification = DeferredCallbackSiteClassification.FrameworkRegistration;
            consumerKind = "render-tree-registration";
        }
        else
        {
            classification = DeferredCallbackSiteClassification.UnknownConsumer;
            consumerKind = "unknown";
        }

        var row = new DeferredCallbackSite(
            assembly,
            MethodDisplay(load.Caller),
            load.Caller.MetadataToken,
            MemberDisplay(load.Callee),
            load.CalleeDefinitionToken,
            load.ILOffset,
            load.Kind,
            construction?.ILOffset,
            construction is null ? Empty : construction.Callee.DeclaringType.ToQualifiedDisplayString(),
            consumer?.ILOffset,
            consumer is null ? Empty : MemberDisplay(consumer.Callee),
            consumerKind,
            consumptionProven,
            classification);
        return new SiteEvidence(row, load);
    }

    static DeferredCallbackCensusRow CreateRow(
        string assembly,
        OptimizationOpportunity opportunity,
        IReadOnlyList<IReadOnlyDictionary<int, CallerLoopEvidence>> nearestByPriority,
        IReadOnlyDictionary<(int MethodToken, int Offset), SiteEvidence> siteBySeed,
        int maxDepth)
    {
        CallerLoopEvidence? evidence = null;
        foreach (var nearest in nearestByPriority)
        {
            if (nearest.TryGetValue(opportunity.Method.MetadataToken, out evidence))
                break;
        }
        SiteEvidence? site = null;
        if (evidence is { Witness.IsDefaultOrEmpty: false })
        {
            var seed = evidence.Witness[0];
            siteBySeed.TryGetValue((seed.Caller.MetadataToken, seed.ILOffset), out site);
        }

        int? downstreamDepth = evidence is null ? null : evidence.Depth - 1;
        var classification = downstreamDepth is null
            ? DeferredCallbackReachClassification.None
            : downstreamDepth == 0
                ? DeferredCallbackReachClassification.Target
                : downstreamDepth <= maxDepth
                    ? DeferredCallbackReachClassification.Downstream
                    : DeferredCallbackReachClassification.BeyondBound;

        return new DeferredCallbackCensusRow(
            assembly,
            opportunity.CandidateId ?? Empty,
            MethodDisplay(opportunity.Method),
            opportunity.Method.MetadataToken,
            opportunity.ILOffset,
            opportunity.Shape,
            opportunity.Confidence,
            Text(opportunity.Multiplicity),
            opportunity.InLoop,
            opportunity.Provenance,
            classification,
            downstreamDepth,
            site?.Row,
            evidence?.Witness.Skip(1).Select(static step => new DeferredCallbackInvocationStep(
                MethodDisplay(step.Caller),
                MethodDisplay(step.Callee),
                step.ILOffset,
                step.Kind)).ToArray() ?? []);
    }

    static IReadOnlyDictionary<int, CallerLoopEvidence> FindNearest(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<DirectCall> directCalls,
        IEnumerable<SiteEvidence> sites)
    {
        var seeds = sites.ToArray();
        if (seeds.Length == 0)
            return ImmutableDictionary<int, CallerLoopEvidence>.Empty;

        // Original loop flags are cleared so ordinary loop calls cannot masquerade as
        // deferred-callback evidence.
        var graphCalls = directCalls
            .Select(static call => call with { InLoop = false })
            .Concat(seeds.Select(static site => site.Load with
            {
                Kind = CallKind.Call,
                InLoop = true,
            }))
            .ToImmutableArray();
        return CallerLoopEvidenceAnalysis.FindNearest(methods, graphCalls);
    }

    static DeferredCallbackCensusReport BuildReport(
        int maxDepth,
        int assemblies,
        int opened,
        IReadOnlyList<DeferredCallbackCensusFailure> failures,
        IReadOnlyList<DeferredCallbackSite> sites,
        IReadOnlyList<DeferredCallbackCensusRow> rows)
    {
        var reachable = rows
            .Where(static row => row.Classification != DeferredCallbackReachClassification.None)
            .ToArray();
        var candidateRows = reachable
            .GroupBy(
                static row => row.Candidate == Empty
                    ? $"{row.Assembly}|{row.MethodToken:X8}|{row.ILOffset?.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) ?? Empty}|{row.Shape}"
                    : $"{row.Assembly}|{row.Candidate}",
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        var methodRows = reachable
            .GroupBy(static row => $"{row.Assembly}|{row.MethodToken:X8}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        return new DeferredCallbackCensusReport(
            maxDepth,
            assemblies,
            opened,
            failures.Count,
            sites.Count,
            rows.Count,
            reachable.Length,
            candidateRows.Length,
            methodRows.Length,
            Counts(sites, static site => SiteClass(site.Classification)),
            Counts(sites, static site => site.ConsumerKind),
            Counts(rows, static row => Reach(row.Classification)),
            Counts(reachable, static row =>
                $"{SiteClass(row.Construction!.Classification)}|{row.Construction.ConsumerKind}|{row.Shape}|{row.Confidence}|{row.LocalMultiplicity}|{row.Provenance.ToString().ToLowerInvariant()}"),
            failures,
            sites,
            rows);
    }

    static bool IsImmediateInvoke(DirectCall consumer)
        => consumer.Callee.HasThis
            && consumer.Callee.Name == "Invoke"
            && consumer.Callee.ParameterTypes.Length == 0;

    static bool IsDelegateConstructorShape(MemberRef constructor)
        => constructor.Kind == MemberKind.Constructor
            && constructor.ParameterTypes.Length == 2
            && constructor.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "Object"))
            && constructor.ParameterTypes[1].Equals(TypeRef.CoreLib("System", "IntPtr"));

    static bool IsRenderTreeRegistration(DirectCall consumer)
    {
        var type = consumer.Callee.DeclaringType.Kind == TypeRefKind.GenericInstance
            ? consumer.Callee.DeclaringType.ElementType ?? consumer.Callee.DeclaringType
            : consumer.Callee.DeclaringType;
        return type.TrustedFrameworkAssembly
            && type.Assembly == "Microsoft.AspNetCore.Components"
            && type.Namespace == "Microsoft.AspNetCore.Components.Rendering"
            && type.Name == "RenderTreeBuilder"
            && consumer.Callee.Name == "AddAttribute"
            && consumer.Callee.HasThis
            && consumer.Callee.ParameterTypes.SequenceEqual(
                [
                    TypeRef.CoreLib("System", "Int32"),
                    TypeRef.CoreLib("System", "String"),
                    TypeRef.CoreLib("System", "MulticastDelegate"),
                ]);
    }

    static string MethodDisplay(MethodIdentity method)
        => $"{method.DeclaringType.ToQualifiedDisplayString()}::{method.Name}({string.Join(", ", method.ParameterTypes.Select(static type => type.ToQualifiedDisplayString()))})";

    static string MemberDisplay(MemberRef member)
        => $"{member.DeclaringType.ToQualifiedDisplayString()}::{member.Name}({string.Join(", ", member.ParameterTypes.Select(static type => type.ToQualifiedDisplayString()))})";

    static string Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? Empty : value;

    static string SiteClass(DeferredCallbackSiteClassification value)
        => value switch
        {
            DeferredCallbackSiteClassification.ConstructionOutsideLoop => "construction-outside-loop",
            DeferredCallbackSiteClassification.StandaloneFunctionLoad => "standalone-function-load",
            DeferredCallbackSiteClassification.CachedConstruction => "cached-construction",
            DeferredCallbackSiteClassification.ConstructedWithoutImmediateConsumer => "constructed-without-immediate-consumer",
            DeferredCallbackSiteClassification.ImmediateInvocation => "immediate-invocation",
            DeferredCallbackSiteClassification.UnknownConsumer => "unknown-consumer",
            _ => "framework-registration",
        };

    static string Reach(DeferredCallbackReachClassification value)
        => value switch
        {
            DeferredCallbackReachClassification.Target => "target",
            DeferredCallbackReachClassification.Downstream => "downstream",
            DeferredCallbackReachClassification.BeyondBound => "beyond-bound",
            _ => "none",
        };

    static IReadOnlyDictionary<string, int> Counts<T>(
        IEnumerable<T> values,
        Func<T, string> key)
        => values
            .GroupBy(key, StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

    static void AppendCounts(
        StringBuilder sb,
        string title,
        IReadOnlyDictionary<string, int> counts,
        int top = int.MaxValue)
    {
        sb.AppendLine($"  {title}:");
        foreach (var (key, value) in counts.Take(top))
            sb.AppendLine($"    {key}={value}");
        if (counts.Count > top)
            sb.AppendLine($"    ... {counts.Count - top} more");
    }

    sealed record SiteEvidence(DeferredCallbackSite Row, DirectCall Load);
}
