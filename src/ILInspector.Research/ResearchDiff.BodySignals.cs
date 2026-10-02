using System.Collections.Immutable;
using ILInspector.Analysis;
using Inspector.Findings;

namespace ILInspector.Research;

public static partial class ResearchDiff
{
    // Untargeted whole-assembly body-signal comparison. It makes no target
    // request: methods pair by BodySignalMethodKey across one assembly pair,
    // and only the host's type filters narrow the census.
    static void AddBodySignalDiff(
        ResultBuilder builder,
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        IReadOnlySet<string>? typeFilters,
        IReadOnlySet<string> retainedComparisonDescriptorIds)
    {
        ArgumentNullException.ThrowIfNull(retainedComparisonDescriptorIds);
        BodySignalRetention retention =
            BodySignalRetention.From(retainedComparisonDescriptorIds);
        foreach (var (oldAnalysis, newAnalysis)
            in PairedBodySignalAnalyses(oldInput, newInput))
        {
            var oldSnapshot = BuildAnalysisSnapshot(
                oldAnalysis,
                typeFilters,
                retention);
            var newSnapshot = BuildAnalysisSnapshot(
                newAnalysis,
                typeFilters,
                retention);
            foreach (var key in oldSnapshot.Keys.Union(newSnapshot.Keys, StringComparer.Ordinal))
            {
                oldSnapshot.TryGetValue(key, out var oldMethod);
                newSnapshot.TryGetValue(key, out var newMethod);
                var subject = newMethod?.Subject ?? oldMethod?.Subject ?? UnknownMemberSubject(key);
                AddMethodSignalRows(
                    builder,
                    subject,
                    oldMethod,
                    newMethod,
                    retention,
                    BodySignalEndpointTopology.Census);
            }

            var methods = MethodSubjectsByBodySignalKey(
                oldAnalysis,
                newAnalysis);
            foreach (var change in UnsafetyFindingDiff.Compare(
                oldAnalysis,
                newAnalysis,
                BodySignalMethodKey))
            {
                if (!methods.TryGetValue(change.MemberKey, out var subject))
                    continue;
                if (!MatchesTypeFilters(subject.TypeName ?? "", typeFilters))
                    continue;
                AddUnsafetyChange(builder, subject, change);
            }
        }
    }

    // Targeted body-signal comparison over exactly the endpoint methods
    // selected from Research correspondence outcomes. No method pairing key
    // participates: each pair was selected by resolved address.
    internal static ResearchComparison CompareSelectedBodySignals(
        IReadOnlyList<SelectedBodySignalPair> pairs,
        BodySignalRetention retention)
    {
        var builder = new ResultBuilder();
        foreach (SelectedBodySignalPair pair in pairs)
        {
            ResearchAnalysisMethod? oldMethod = pair.Old is { } old
                ? AnalysisMethod(old.Analysis, old.Method, retention)
                : null;
            ResearchAnalysisMethod? newMethod = pair.New is { } @new
                ? AnalysisMethod(@new.Analysis, @new.Method, retention)
                : null;
            ResearchSubjectKey subject = SubjectFromMethod(
                (pair.New ?? pair.Old)!.Method);
            AddMethodSignalRows(
                builder,
                subject,
                oldMethod,
                newMethod,
                retention,
                new BodySignalEndpointTopology(
                    OldSubjectAbsent: pair.Old is null,
                    NewSubjectAbsent: pair.New is null));
            foreach (var change in UnsafetyFindingDiff.CompareMethods(
                pair.Old?.Analysis,
                pair.Old?.Method,
                pair.New?.Analysis,
                pair.New?.Method,
                subject.Id))
            {
                AddUnsafetyChange(builder, subject, change);
            }
        }

        return builder.ToResult();
    }

    static void AddUnsafetyChange(
        ResultBuilder builder,
        ResearchSubjectKey subject,
        UnsafetyFindingChange change)
    {
        var kind = change.Kind == UnsafetyFindingChangeKind.Added
            ? ResearchChangeKind.Added
            : ResearchChangeKind.Removed;
        var suffix = kind == ResearchChangeKind.Added ? "added" : "removed";
        string descriptorId =
            $"unsafe.{NormalizeChangePart(change.Signal)}.{suffix}";
        builder.Add(new ResearchChange(
            subject,
            ResearchChangeMechanism.BodySignals,
            Descriptor(descriptorId, change.Signal),
            kind,
            oldIlOffset: kind == ResearchChangeKind.Removed
                ? change.ILOffset
                : null,
            newIlOffset: kind == ResearchChangeKind.Added
                ? change.ILOffset
                : null,
            detail: $"{change.Operation}: {change.Evidence}",
            category: ResearchChangeCategory.BodySignal,
            signal: change.Signal));
    }

    static void AddMethodSignalRows(
        ResultBuilder builder,
        ResearchSubjectKey subject,
        ResearchAnalysisMethod? oldMethod,
        ResearchAnalysisMethod? newMethod,
        BodySignalRetention retention,
        BodySignalEndpointTopology topology)
    {
        var inBoth = oldMethod is not null && newMethod is not null;
        AddAllocationRow(
            builder,
            subject,
            inBoth,
            oldMethod?.Allocations ?? [],
            newMethod?.Allocations ?? [],
            Evidence(oldMethod?.Signals, newMethod?.Signals),
            retention.Allocations,
            topology);
        if (retention.CallSites)
        {
            ImmutableArray<DirectCall> oldCalls = oldMethod?.CallSites ?? [];
            ImmutableArray<DirectCall> newCalls = newMethod?.CallSites ?? [];
            AddRetainedComparison(
                builder,
                subject,
                AnalysisFindings.CallSiteDescriptor,
                topology.IsCensus
                    ? AnalysisFindings.CompareCallSites(
                        oldCalls,
                        newCalls,
                        new FindingSubject(subject.Id, subject.Display))
                    : topology.Compare(
                        AnalysisFindings.InspectCallSites,
                        oldCalls,
                        newCalls,
                        subject));
        }
        if (retention.Unsafety)
        {
            ImmutableArray<UnsafetyOccurrence> oldUnsafety =
                oldMethod?.Unsafety ?? [];
            ImmutableArray<UnsafetyOccurrence> newUnsafety =
                newMethod?.Unsafety ?? [];
            AddRetainedComparison(
                builder,
                subject,
                AnalysisFindings.UnsafetyDescriptor,
                topology.IsCensus
                    ? AnalysisFindings.CompareUnsafety(
                        oldUnsafety,
                        newUnsafety,
                        new FindingSubject(subject.Id, subject.Display))
                    : topology.Compare(
                        AnalysisFindings.InspectUnsafety,
                        oldUnsafety,
                        newUnsafety,
                        subject));
        }
        AddCountRows(builder, subject, inBoth, oldMethod?.Signals, newMethod?.Signals);
        AddExceptionRow(builder, subject, inBoth, oldMethod?.Signals, newMethod?.Signals);
        AddOptimizationRows(builder, subject, inBoth, oldMethod?.Opportunities, newMethod?.Opportunities);
    }

    static Dictionary<string, ResearchAnalysisMethod> BuildAnalysisSnapshot(
        BodySignalAnalysisInput analysis,
        IReadOnlySet<string>? typeFilters,
        BodySignalRetention retention)
    {
        var methods = new Dictionary<string, ResearchAnalysisMethod>(StringComparer.Ordinal);
        var generatedFrameworkTypes =
            analysis.GeneratedFrameworkTypes;
        foreach (var method in analysis.Methods)
        {
            if (IsGeneratedMethod(method, generatedFrameworkTypes))
                continue;
            if (!MatchesTypeFilters(method.DeclaringType.ToQualifiedDisplayString(), typeFilters))
                continue;
            var entry = AnalysisMethodEvidence(analysis, method, retention);
            var key = BodySignalMethodKey(method);
            if (!methods.TryGetValue(key, out var existing))
            {
                methods[key] = entry;
            }
            else
            {
                methods[key] = existing with
                {
                    Signals = entry.Signals,
                    Allocations = entry.Allocations,
                    CallSites = entry.CallSites,
                    Unsafety = entry.Unsafety,
                };
            }
        }

        foreach (var opportunity in analysis.Opportunities)
        {
            if (IsGeneratedMethod(opportunity.Method, generatedFrameworkTypes))
                continue;
            if (!MatchesTypeFilters(opportunity.Method.DeclaringType.ToQualifiedDisplayString(), typeFilters))
                continue;
            var key = BodySignalMethodKey(opportunity.Method);
            if (!methods.TryGetValue(key, out var entry))
            {
                entry = new ResearchAnalysisMethod(
                    SubjectFromMethod(opportunity.Method),
                    MethodSignals.None,
                    [],
                    [],
                    [],
                    []);
                methods[key] = entry;
            }
            entry.Opportunities.Add(opportunity);
        }

        return methods;
    }

    // One selected endpoint's evidence, read only by its MethodDef token from
    // the side's own focused Analysis results.
    static ResearchAnalysisMethod AnalysisMethod(
        BodySignalAnalysisInput analysis,
        MethodIdentity method,
        BodySignalRetention retention)
    {
        ResearchAnalysisMethod entry =
            AnalysisMethodEvidence(analysis, method, retention);
        foreach (var opportunity in analysis.Opportunities)
        {
            if (opportunity.Method.MetadataToken == method.MetadataToken)
                entry.Opportunities.Add(opportunity);
        }
        return entry;
    }

    static ResearchAnalysisMethod AnalysisMethodEvidence(
        BodySignalAnalysisInput analysis,
        MethodIdentity method,
        BodySignalRetention retention)
    {
        analysis.MethodSignals.TryGetValue(method.MetadataToken, out var signals);
        analysis.AllocationOccurrences.TryGetValue(method.MetadataToken, out var allocations);
        ImmutableArray<DirectCall> callSites = [];
        if (retention.CallSites
            && analysis.CallsByEvidenceMethod.TryGetValue(method.MetadataToken, out var retainedCallSites))
        {
            callSites = retainedCallSites;
        }
        ImmutableArray<UnsafetyOccurrence> unsafety = [];
        if (retention.Unsafety
            && analysis.UnsafetyOccurrences.TryGetValue(method.MetadataToken, out var retainedUnsafety))
        {
            unsafety = retainedUnsafety;
        }
        return new ResearchAnalysisMethod(
            SubjectFromMethod(method),
            signals ?? MethodSignals.None,
            allocations.IsDefault ? [] : allocations,
            callSites,
            unsafety,
            []);
    }

    static void AddAllocationRow(
        ResultBuilder builder,
        ResearchSubjectKey subject,
        bool inBoth,
        ImmutableArray<AllocationOccurrence> oldOccurrences,
        ImmutableArray<AllocationOccurrence> newOccurrences,
        string? evidence,
        bool retainComparison,
        BodySignalEndpointTopology topology)
    {
        var comparison = AnalysisFindings.CompareAllocations(
            oldOccurrences,
            newOccurrences,
            new FindingSubject(subject.Id, subject.Display));
        if (retainComparison)
        {
            AddRetainedComparison(
                builder,
                subject,
                AnalysisFindings.AllocationDescriptor,
                topology.IsCensus
                    ? comparison
                    : topology.Compare(
                        AnalysisFindings.InspectAllocations,
                        oldOccurrences,
                        newOccurrences,
                        subject));
        }
        var complete = comparison switch
        {
            FindingComparison<AllocationOccurrence>.Complete value => value,
            FindingComparison<AllocationOccurrence>.Failed failed =>
                throw new InvalidOperationException(
                    $"A comparison of total allocation censuses cannot fail: {failed.Failure}"),
        };
        if (complete.IsExact)
            return;

        int oldValue = oldOccurrences.Count(static occurrence => occurrence.CountsAsHeapAllocation);
        int newValue = newOccurrences.Count(static occurrence => occurrence.CountsAsHeapAllocation);
        bool oldInLoop = oldOccurrences.Any(IsHotAllocation);
        bool newInLoop = newOccurrences.Any(IsHotAllocation);
        int delta = newValue - oldValue;
        string deltaText;
        string? shape = null;
        int magnitude;
        int directionScore;
        bool inLoop;

        if (delta != 0)
        {
            deltaText = FormatDelta(delta);
            magnitude = Math.Abs(delta);
            directionScore = Math.Sign(delta);
            inLoop = delta > 0 ? newInLoop : oldInLoop;
            shape = inLoop ? "in-loop" : null;
        }
        else if (newValue > 0 && oldInLoop != newInLoop)
        {
            bool becameHot = newInLoop;
            deltaText = becameHot ? "hot" : "cold";
            shape = "in-loop";
            magnitude = 1;
            directionScore = becameHot ? 1 : -1;
            inLoop = true;
        }
        else
        {
            deltaText = "changed";
            magnitude = complete.Pairs.Count(static pair =>
                pair.Kind != PairKind.Present
                || pair.Difference != FindingDifferenceKind.None);
            directionScore = 0;
            inLoop = oldOccurrences.Any(static occurrence => occurrence.InLoop)
                || newOccurrences.Any(static occurrence => occurrence.InLoop);
        }

        builder.Add(new ResearchChange(
            subject,
            ResearchChangeMechanism.BodySignals,
            AnalysisFindings.AllocationDescriptor,
            ResearchChangeKind.Changed,
            oldValue.ToString(),
            newValue.ToString(),
            delta: deltaText,
            detail: evidence,
            category: ResearchChangeCategory.BodySignal,
            signal: "allocations",
            shape: shape,
            magnitude: magnitude,
            directionScore: directionScore,
            subjectInBoth: inBoth,
            inLoop: inLoop,
            allocationComparison: comparison));
    }

    static void AddCountRows(ResultBuilder builder, ResearchSubjectKey subject, bool inBoth, MethodSignals? oldSignals, MethodSignals? newSignals)
    {
        AddCountRow(builder, subject, inBoth, "copies", oldSignals?.Copies ?? 0, newSignals?.Copies ?? 0, Evidence(oldSignals, newSignals));
        AddCountRow(builder, subject, inBoth, "reflection", oldSignals?.Reflection ?? 0, newSignals?.Reflection ?? 0, Evidence(oldSignals, newSignals));
        AddCountRow(builder, subject, inBoth, "throws", oldSignals?.Throws ?? 0, newSignals?.Throws ?? 0, Evidence(oldSignals, newSignals));
        AddCountRow(builder, subject, inBoth, "catches", oldSignals?.Catches ?? 0, newSignals?.Catches ?? 0, Evidence(oldSignals, newSignals));
        AddCountRow(builder, subject, inBoth, "finallys", oldSignals?.Finallys ?? 0, newSignals?.Finallys ?? 0, Evidence(oldSignals, newSignals));
        AddCountRow(builder, subject, inBoth, "unsafe", oldSignals?.Unsafe == true ? 1 : 0, newSignals?.Unsafe == true ? 1 : 0, Evidence(oldSignals, newSignals));
    }

    static bool IsHotAllocation(AllocationOccurrence occurrence)
        => occurrence.CountsAsHeapAllocation
            && occurrence.InLoop
            && occurrence.Escape != AllocationEscape.ThrowPath;

    static void AddCountRow(ResultBuilder builder, ResearchSubjectKey subject, bool inBoth, string signal, int oldValue, int newValue, string? evidence, bool oldAllocInLoop = false, bool newAllocInLoop = false)
    {
        var delta = newValue - oldValue;
        if (delta == 0)
        {
            if (newValue > 0 && oldAllocInLoop != newAllocInLoop)
            {
                bool becameHot = newAllocInLoop;
                AddAnalysisEvidence(
                    builder,
                    subject,
                    $"analysis.signal.{signal}",
                    signal,
                    oldValue.ToString(),
                    newValue.ToString(),
                    becameHot ? "hot" : "cold",
                    "in-loop",
                    evidence,
                    magnitude: 1,
                    directionScore: becameHot ? 1 : -1,
                    inBoth,
                    inLoop: true);
            }
            return;
        }

        var inLoop = delta > 0 ? newAllocInLoop : oldAllocInLoop;
        AddAnalysisEvidence(
            builder,
            subject,
            $"analysis.signal.{signal}",
            signal,
            oldValue.ToString(),
            newValue.ToString(),
            FormatDelta(delta),
            inLoop ? "in-loop" : null,
            evidence,
            Math.Abs(delta),
            Math.Sign(delta),
            inBoth,
            inLoop);
    }

    static void AddExceptionRow(ResultBuilder builder, ResearchSubjectKey subject, bool inBoth, MethodSignals? oldSignals, MethodSignals? newSignals)
    {
        var oldTypes = oldSignals?.ExceptionTypes ?? [];
        var newTypes = newSignals?.ExceptionTypes ?? [];
        if (oldTypes.SequenceEqual(newTypes))
            return;
        var delta = newTypes.Length - oldTypes.Length;
        AddAnalysisEvidence(
            builder,
            subject,
            "analysis.signal.constructed-exceptions",
            "constructed-exceptions",
            FormatList(oldTypes),
            FormatList(newTypes),
            "changed",
            shape: null,
            Evidence(oldSignals, newSignals),
            Math.Max(1, Math.Abs(delta)),
            Math.Sign(delta),
            inBoth,
            inLoop: false);
    }

    static void AddOptimizationRows(ResultBuilder builder, ResearchSubjectKey subject, bool inBoth, List<OptimizationOpportunity>? oldOps, List<OptimizationOpportunity>? newOps)
    {
        var oldCounts = CountShapes(oldOps);
        var newCounts = CountShapes(newOps);
        foreach (var shape in oldCounts.Keys.Union(newCounts.Keys).OrderBy(shape => shape, StringComparer.Ordinal))
        {
            var oldValue = oldCounts.GetValueOrDefault(shape);
            var newValue = newCounts.GetValueOrDefault(shape);
            if (oldValue == newValue)
                continue;
            var delta = newValue - oldValue;
            AddAnalysisEvidence(
                builder,
                subject,
                $"analysis.optimization.{shape}",
                "optimization",
                oldValue.ToString(),
                newValue.ToString(),
                FormatDelta(delta),
                shape,
                FormatOptimizationEvidence(oldOps, newOps, shape),
                Math.Abs(delta),
                Math.Sign(delta),
                inBoth,
                inLoop: false);
        }
    }

    static void AddAnalysisEvidence(
        ResultBuilder builder,
        ResearchSubjectKey subject,
        string changeId,
        string signal,
        string oldValue,
        string newValue,
        string delta,
        string? shape,
        string? detail,
        int magnitude,
        int directionScore,
        bool inBoth,
        bool inLoop)
        => builder.Add(new ResearchChange(
            subject,
            ResearchChangeMechanism.BodySignals,
            Descriptor(changeId, signal),
            ResearchChangeKind.Changed,
            oldValue,
            newValue,
            delta: delta,
            detail: detail,
            category: ResearchChangeCategory.BodySignal,
            signal: signal,
            shape: shape,
            magnitude: magnitude,
            directionScore: directionScore,
            subjectInBoth: inBoth,
            inLoop: inLoop));

    static IEnumerable<(
        BodySignalAnalysisInput Old,
        BodySignalAnalysisInput New)> PairedBodySignalAnalyses(
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput)
    {
        var oldAnalyses = BodySignalAnalysisEntries(oldInput)
            .ToDictionary(
                analysis => AssemblyKey(analysis.Receipt),
                StringComparer.Ordinal);
        var newAnalyses = BodySignalAnalysisEntries(newInput)
            .ToDictionary(
                analysis => AssemblyKey(analysis.Receipt),
                StringComparer.Ordinal);
        foreach (string key in oldAnalyses.Keys
            .Intersect(newAnalyses.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            yield return (oldAnalyses[key], newAnalyses[key]);
        }
    }

    static IEnumerable<BodySignalAnalysisInput>
        BodySignalAnalysisEntries(ResearchDiffInput input)
    {
        if (input.BodySignalAnalyses is { } analyses)
        {
            foreach (BodySignalAnalysisInput analysis in analyses)
                yield return analysis;
            yield break;
        }

        if (input.AssemblyPaths.Count == 0)
        {
            throw new ArgumentException(
                "Body-signal comparison requires focused Analysis inputs "
                    + "or assembly paths.",
                nameof(input));
        }

        const LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.MethodEvidence
            | LibraryBodyAnalysisFeatures.Allocations
            | LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        foreach (string path in input.AssemblyPaths)
        {
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecutePath(
                    path,
                    LibraryBodyAnalysisRequest.Create(features));
            yield return new BodySignalAnalysisInput(
                execution.Allocations,
                execution.Safety,
                execution.CallGraph,
                execution.Optimization);
        }
    }

    static Dictionary<string, ResearchSubjectKey> MethodSubjectsByBodySignalKey(
        BodySignalAnalysisInput oldAnalysis,
        BodySignalAnalysisInput newAnalysis)
    {
        var oldGeneratedFrameworkTypes =
            oldAnalysis.GeneratedFrameworkTypes;
        var newGeneratedFrameworkTypes =
            newAnalysis.GeneratedFrameworkTypes;
        return oldAnalysis.Methods
            .Where(method => !IsGeneratedMethod(method, oldGeneratedFrameworkTypes))
            .Concat(newAnalysis.Methods.Where(method =>
                !IsGeneratedMethod(
                    method,
                    newGeneratedFrameworkTypes)))
            .Select(method => (Key: BodySignalMethodKey(method), Subject: SubjectFromMethod(method)))
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Subject, StringComparer.Ordinal);
    }
}
