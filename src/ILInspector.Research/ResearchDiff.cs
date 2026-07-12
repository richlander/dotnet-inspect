using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

public sealed record ResearchDiffOptions(
    ResearchChangeMechanism Mechanisms = ResearchChangeMechanism.AllAvailable,
    bool IncludeAllApi = false,
    ApiDiffScope ApiScope = ApiDiffScope.Signature,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlySet<string>? MemberTargetIdentities = null)
{
    public bool RetainAllocationComparisons { get; init; }
    public bool RetainCallSiteComparisons { get; init; }
}

public sealed record ResearchDiffInput(
    IReadOnlyList<string> AssemblyPaths,
    ApiSurface? ApiSurface = null,
    IReadOnlyList<LibraryBodyIndex>? BodyIndexes = null)
{
    public static ResearchDiffInput FromAssembly(string assemblyPath, ApiSurface? apiSurface = null, LibraryBodyIndex? bodyIndex = null)
        => new([assemblyPath], apiSurface, bodyIndex is null ? null : [bodyIndex]);

    public static ResearchDiffInput FromAssemblies(IReadOnlyList<string> assemblyPaths)
        => new(assemblyPaths);

    public static ResearchDiffInput FromApiSurface(ApiSurface apiSurface)
        => new([], apiSurface);
}

public static class ResearchDiff
{
    public static string ToChangeIdPart(string value)
        => ToKebabCase(value);

    static FindingDescriptor Descriptor(string id, string title)
        => new(id, title);

    public static ResearchComparison FromApiDiff(ApiDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var changes = ImmutableArray.CreateBuilder<ResearchChange>();
        foreach (var typeDiff in diff.TypeDiffs)
        {
            foreach (var change in typeDiff.Changes)
            {
                string descriptorId = $"api.{ToKebabCase(change.Kind.ToString())}";
                changes.Add(new ResearchChange(
                    ApiSubject(typeDiff.TypeFullName, change),
                    ResearchChangeMechanism.Api,
                    Descriptor(descriptorId, change.Kind.ToString()),
                    Direction(change.Kind),
                    oldValue: change.OldValue,
                    newValue: change.NewValue,
                    detail: change.Message,
                    category: ToResearchCategory(change.Category),
                    apiChange: change));
            }
        }

        return new ResearchComparison(changes.ToImmutable(), diff);
    }

    public static ResearchComparison FromIlBodyDiff(IlBodyDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var changes = ImmutableArray.CreateBuilder<ResearchChange>();
        var subject = UnknownMemberSubject("il-body");
        if (!diff.FailureRows.IsDefaultOrEmpty)
        {
            changes.AddRange(diff.FailureRows.Select(row =>
            {
                string descriptorId = $"il.diff.{ToKebabCase(row.Kind.ToString())}";
                return new ResearchChange(
                    subject,
                    ResearchChangeMechanism.IlBody,
                    Descriptor(descriptorId, row.Kind.ToString()),
                    Direction(row.Kind),
                    detail: row.Message,
                    category: ResearchChangeCategory.IlBody,
                    ilFailureRow: row,
                    ilDisplayFailureRow: IlDiffPrinter.ToDisplayFailureRow(row));
            }));
        }
        else if (diff.Failure is { Length: > 0 } failure)
        {
            changes.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.IlBody,
                Descriptor("il.diff.failed", "IL diff failed"),
                ResearchChangeKind.Changed,
                detail: failure,
                category: ResearchChangeCategory.IlBody));
        }

        if (!diff.Rows.IsDefaultOrEmpty)
        {
            changes.AddRange(diff.Rows
                .Where(row => row.Kind != IlDiffKind.Context)
                .Select(row =>
                {
                    string descriptorId = $"il.operation.{ChangeIdSuffix(row.Kind)}";
                    return new ResearchChange(
                        subject,
                        ResearchChangeMechanism.IlBody,
                        Descriptor(descriptorId, "IL operation"),
                        Direction(row.Kind),
                        oldValue: row.Kind == IlDiffKind.Remove ? row.Operation.Display : null,
                        newValue: row.Kind == IlDiffKind.Add ? row.Operation.Display : null,
                        oldIlOffset: row.Kind == IlDiffKind.Remove ? row.Operation.Offset : null,
                        newIlOffset: row.Kind == IlDiffKind.Add ? row.Operation.Offset : null,
                        detail: row.Message,
                        category: ResearchChangeCategory.IlBody,
                        ilRow: row,
                        ilDisplayRows: [IlDiffPrinter.ToDisplayRow(row)]);
                }));
        }

        return new ResearchComparison(changes.ToImmutable());
    }

    public static ResearchComparison FromBodySignalDiff(BodySignalDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return new ResearchComparison(
        [
            .. diff.Rows.Select(row =>
            {
                string descriptorId =
                    $"unsafe.{ToKebabCase(row.Signal)}.{ToKebabCase(row.Kind.ToString())}";
                var kind = row.Kind == BodySignalDiffKind.Added
                    ? ResearchChangeKind.Added
                    : ResearchChangeKind.Removed;
                return new ResearchChange(
                    UnknownMemberSubject(row.Member),
                    ResearchChangeMechanism.BodySignals,
                    Descriptor(descriptorId, row.Signal),
                    kind,
                    oldIlOffset: kind == ResearchChangeKind.Removed ? row.ILOffset : null,
                    newIlOffset: kind == ResearchChangeKind.Added ? row.ILOffset : null,
                    detail: $"{row.Operation}: {row.Evidence}",
                    category: ResearchChangeCategory.BodySignal,
                    signal: row.Signal,
                    bodySignalRow: row);
            })
        ]);
    }

    public static ResearchComparison FromCSharpBodyDiff(CSharpBodyDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var changes = ImmutableArray.CreateBuilder<ResearchChange>();
        if (!diff.FailureRows.IsDefaultOrEmpty)
        {
            changes.AddRange(diff.FailureRows.Select(row =>
            {
                string descriptorId = $"csharp.diff.{ToKebabCase(row.Kind.ToString())}";
                return new ResearchChange(
                    ResearchMemberIdentity.SubjectFromAnchor(row.Anchor, row.Member),
                    ResearchChangeMechanism.CSharp,
                    Descriptor(descriptorId, row.Kind.ToString()),
                    Direction(row.Kind),
                    oldValue: row.Side == "old" ? row.Detail ?? row.Message : null,
                    newValue: row.Side == "new" ? row.Detail ?? row.Message : null,
                    detail: row.Message,
                    category: ResearchChangeCategory.CSharp,
                    cSharpFailureRow: row,
                    cSharpDisplayFailureRow: CSharpDiffPrinter.ToDisplayFailureRow(row));
            }));
        }

        if (!diff.Rows.IsDefaultOrEmpty)
        {
            changes.AddRange(diff.Rows.Select(row =>
            {
                var kind = Direction(row.Kind);
                return new ResearchChange(
                    ResearchMemberIdentity.SubjectFromAnchor(row.Anchor, row.Member),
                    ResearchChangeMechanism.CSharp,
                    Descriptor(row.ChangeId, row.Kind.ToString()),
                    kind,
                    oldValue: row.OldOperation?.Value ?? row.OldValue
                        ?? (kind == ResearchChangeKind.Removed ? row.Text : null),
                    newValue: row.NewOperation?.Value ?? row.NewValue
                        ?? (kind == ResearchChangeKind.Added ? row.Text : null),
                    detail: row.Message,
                    category: ResearchChangeCategory.CSharp,
                    cSharpRow: row,
                    cSharpDisplayRows: [CSharpDiffPrinter.ToDisplayRow(row)]);
            }));
        }

        return new ResearchComparison(changes.ToImmutable());
    }

    public static ResearchComparison Combine(params ResearchComparison[] results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Any(result => result is null))
            throw new ArgumentException("Results cannot contain null entries.", nameof(results));
        var apiComparison = results
            .Select(result => result.ApiComparison)
            .FirstOrDefault(comparison => comparison is not null);
        var apiDiff = apiComparison?.ApiDiff
            ?? results.FirstOrDefault(result => result.ApiDiff is not null)?.ApiDiff;
        return new ResearchComparison(
            [.. results.SelectMany(result => result.Changes)],
            apiDiff,
            apiComparison,
            [.. results.SelectMany(result =>
                result.AllocationComparisons.IsDefault
                    ? []
                    : result.AllocationComparisons)],
            [.. results.SelectMany(result =>
                result.CallSiteComparisons.IsDefault
                    ? []
                    : result.CallSiteComparisons)]);
    }

    public static ResearchComparison CompareAssemblies(string oldAssemblyPath, string newAssemblyPath, ResearchDiffOptions? options = null)
        => Compare(ResearchDiffInput.FromAssembly(oldAssemblyPath), ResearchDiffInput.FromAssembly(newAssemblyPath), options);

    public static ResearchComparison CompareApiSurfaces(ApiSurface oldSurface, ApiSurface newSurface)
        => Compare(ResearchDiffInput.FromApiSurface(oldSurface), ResearchDiffInput.FromApiSurface(newSurface),
            new ResearchDiffOptions(ResearchChangeMechanism.Api));

    public static ResearchComparison Compare(ResearchDiffInput oldInput, ResearchDiffInput newInput, ResearchDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldInput);
        ArgumentNullException.ThrowIfNull(newInput);

        options ??= new ResearchDiffOptions();
        var builder = new ResultBuilder();

        if (options.Mechanisms.HasFlag(ResearchChangeMechanism.Api))
            AddApiDiff(builder, oldInput, newInput, options.IncludeAllApi, options.ApiScope);

        if (options.Mechanisms.HasFlag(ResearchChangeMechanism.BodySignals))
        {
            AddBodySignalDiff(
                builder,
                oldInput,
                newInput,
                options.TypeFilters,
                options.MemberTargetIdentities,
                options.RetainAllocationComparisons,
                options.RetainCallSiteComparisons);
        }

        if (options.Mechanisms.HasFlag(ResearchChangeMechanism.IlBody))
            AddIlBodyDiff(builder, oldInput, newInput, options.TypeFilters, options.MemberTargetIdentities);

        if (options.Mechanisms.HasFlag(ResearchChangeMechanism.CSharp))
            AddCSharpDiff(builder, oldInput, newInput, options.TypeFilters, options.MemberTargetIdentities);

        return builder.ToResult();
    }

    static void AddApiDiff(ResultBuilder builder, ResearchDiffInput oldInput, ResearchDiffInput newInput, bool includeAll, ApiDiffScope apiScope)
    {
        var oldSurface = ResolveApiSurface(oldInput, includeAll);
        var newSurface = ResolveApiSurface(newInput, includeAll);
        if (oldSurface is null || newSurface is null)
            return;

        var findings = MetadataFindings.CompareApi(
            oldSurface,
            newSurface,
            new FindingSubject("api", "API surface"),
            new ApiDiffOptions(apiScope));
        var diff = findings.ApiDiff;
        builder.ApiComparison = findings;
        foreach (var typeDiff in diff.TypeDiffs)
        {
            foreach (var change in typeDiff.Changes)
            {
                var subject = ApiSubject(typeDiff.TypeFullName, change);
                string descriptorId = $"api.{ToKebabCase(change.Kind.ToString())}";
                builder.Add(new ResearchChange(
                    subject,
                    ResearchChangeMechanism.Api,
                    Descriptor(descriptorId, change.Kind.ToString()),
                    Direction(change.Kind),
                    change.OldValue,
                    change.NewValue,
                    detail: $"{change.Classification}: {change.Message}",
                    category: ToResearchCategory(change.Category),
                    apiChange: change));
            }
        }
    }

    static void AddBodySignalDiff(
        ResultBuilder builder,
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        IReadOnlySet<string>? typeFilters,
        IReadOnlySet<string>? memberTargetIdentities,
        bool retainAllocationComparisons,
        bool retainCallSiteComparisons)
    {
        foreach (var (oldIndex, newIndex) in PairedBodyIndexes(oldInput, newInput))
        {
            AddAnalysisSignalDiff(
                builder,
                oldIndex,
                newIndex,
                typeFilters,
                memberTargetIdentities,
                retainAllocationComparisons,
                retainCallSiteComparisons);

            var methods = MethodSubjectsByBodySignalKey(oldIndex, newIndex);
            foreach (var row in BodySignalDiff.CompareUnsafe(oldIndex, newIndex).Rows)
            {
                if (!methods.TryGetValue(row.Member, out var subject))
                    continue;
                if (!MatchesTypeFilters(subject.TypeName ?? "", typeFilters))
                    continue;
                if (!MatchesMemberTargets(subject, memberTargetIdentities))
                    continue;
                var kind = row.Kind == BodySignalDiffKind.Added
                    ? ResearchChangeKind.Added
                    : ResearchChangeKind.Removed;
                var suffix = kind == ResearchChangeKind.Added ? "added" : "removed";
                string descriptorId = $"unsafe.{NormalizeChangePart(row.Signal)}.{suffix}";
                builder.Add(new ResearchChange(
                    subject,
                    ResearchChangeMechanism.BodySignals,
                    Descriptor(descriptorId, row.Signal),
                    kind,
                    oldIlOffset: kind == ResearchChangeKind.Removed ? row.ILOffset : null,
                    newIlOffset: kind == ResearchChangeKind.Added ? row.ILOffset : null,
                    detail: $"{row.Operation}: {row.Evidence}",
                    category: ResearchChangeCategory.BodySignal,
                    signal: row.Signal,
                    bodySignalRow: row));
            }
        }

        static void AddAnalysisSignalDiff(
            ResultBuilder builder,
            LibraryBodyIndex oldIndex,
            LibraryBodyIndex newIndex,
            IReadOnlySet<string>? typeFilters,
            IReadOnlySet<string>? memberTargetIdentities,
            bool retainAllocationComparisons,
            bool retainCallSiteComparisons)
        {
            var oldSnapshot = BuildAnalysisSnapshot(
                oldIndex,
                typeFilters,
                memberTargetIdentities,
                retainCallSiteComparisons);
            var newSnapshot = BuildAnalysisSnapshot(
                newIndex,
                typeFilters,
                memberTargetIdentities,
                retainCallSiteComparisons);
            foreach (var key in oldSnapshot.Keys.Union(newSnapshot.Keys, StringComparer.Ordinal))
            {
                oldSnapshot.TryGetValue(key, out var oldMethod);
                newSnapshot.TryGetValue(key, out var newMethod);
                var subject = newMethod?.Subject ?? oldMethod?.Subject ?? UnknownMemberSubject(key);
                var inBoth = oldMethod is not null && newMethod is not null;
                AddAllocationRow(
                    builder,
                    subject,
                    inBoth,
                    oldMethod?.Allocations ?? [],
                    newMethod?.Allocations ?? [],
                    Evidence(oldMethod?.Signals, newMethod?.Signals),
                    retainAllocationComparisons);
                if (retainCallSiteComparisons)
                {
                    AddCallSiteComparison(
                        builder,
                        subject,
                        oldMethod?.CallSites ?? [],
                        newMethod?.CallSites ?? []);
                }
                AddCountRows(builder, subject, inBoth, oldMethod?.Signals, newMethod?.Signals);
                AddExceptionRow(builder, subject, inBoth, oldMethod?.Signals, newMethod?.Signals);
                AddOptimizationRows(builder, subject, inBoth, oldMethod?.Opportunities, newMethod?.Opportunities);
            }
        }

        static Dictionary<string, ResearchAnalysisMethod> BuildAnalysisSnapshot(
            LibraryBodyIndex index,
            IReadOnlySet<string>? typeFilters,
            IReadOnlySet<string>? memberTargetIdentities,
            bool includeCallSites)
        {
            var methods = new Dictionary<string, ResearchAnalysisMethod>(StringComparer.Ordinal);
            var generatedFrameworkTypes = index.GeneratedFrameworkTypeNames;
            var signalsByToken = index.GetMethodSignals();
            var allocationsByToken = index.GetAllocationOccurrences();
            var callsByToken = includeCallSites ? index.GetDirectCallsByCaller() : null;
            foreach (var method in index.Methods)
            {
                if (IsGeneratedMethod(method, generatedFrameworkTypes))
                    continue;
                if (!MatchesTypeFilters(method.DeclaringType.ToQualifiedDisplayString(), typeFilters))
                    continue;
                var subject = SubjectFromMethod(method);
                if (!MatchesMemberTargets(subject, memberTargetIdentities))
                    continue;
                signalsByToken.TryGetValue(method.MetadataToken, out var signals);
                allocationsByToken.TryGetValue(method.MetadataToken, out var allocations);
                ImmutableArray<DirectCall> callSites = [];
                if (callsByToken is not null
                    && callsByToken.TryGetValue(method.MetadataToken, out var retainedCallSites))
                {
                    callSites = retainedCallSites;
                }
                var key = BodySignalMethodKey(method);
                if (!methods.TryGetValue(key, out var entry))
                {
                    entry = new ResearchAnalysisMethod(
                        subject,
                        signals ?? MethodSignals.None,
                        allocations.IsDefault ? [] : allocations,
                        callSites,
                        []);
                    methods[key] = entry;
                }
                else
                {
                    methods[key] = entry with
                    {
                        Signals = signals ?? MethodSignals.None,
                        Allocations = allocations.IsDefault ? [] : allocations,
                        CallSites = callSites,
                    };
                }
            }

            foreach (var opportunity in index.OptimizationOpportunities)
            {
                if (IsGeneratedMethod(opportunity.Method, generatedFrameworkTypes))
                    continue;
                if (!MatchesTypeFilters(opportunity.Method.DeclaringType.ToQualifiedDisplayString(), typeFilters))
                    continue;
                var subject = SubjectFromMethod(opportunity.Method);
                if (!MatchesMemberTargets(subject, memberTargetIdentities))
                    continue;
                var key = BodySignalMethodKey(opportunity.Method);
                if (!methods.TryGetValue(key, out var entry))
                {
                    entry = new ResearchAnalysisMethod(subject, MethodSignals.None, [], [], []);
                    methods[key] = entry;
                }
                entry.Opportunities.Add(opportunity);
            }

            return methods;
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

        static void AddAllocationRow(
            ResultBuilder builder,
            ResearchSubjectKey subject,
            bool inBoth,
            ImmutableArray<AllocationOccurrence> oldOccurrences,
            ImmutableArray<AllocationOccurrence> newOccurrences,
            string? evidence,
            bool retainComparison)
        {
            var comparison = AnalysisFindings.CompareAllocations(
                oldOccurrences,
                newOccurrences,
                new FindingSubject(subject.Id, subject.Display));
            if (retainComparison)
                builder.Add(new AllocationFindingComparison(subject, comparison));
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

        static void AddCallSiteComparison(
            ResultBuilder builder,
            ResearchSubjectKey subject,
            ImmutableArray<DirectCall> oldCallSites,
            ImmutableArray<DirectCall> newCallSites)
            => builder.Add(new CallSiteFindingComparison(
                subject,
                AnalysisFindings.CompareCallSites(
                    oldCallSites,
                    newCallSites,
                    new FindingSubject(subject.Id, subject.Display))));

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
    }

    static void AddIlBodyDiff(
        ResultBuilder builder,
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        IReadOnlySet<string>? typeFilters,
        IReadOnlySet<string>? memberTargetIdentities)
    {
        foreach (var pair in PairedBodyIndexEntries(oldInput, newInput))
        {
            var oldMethods = MethodLookup(pair.Old.Index);
            var newMethods = MethodLookup(pair.New.Index);
            var keys = oldMethods.Keys.Intersect(newMethods.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var oldBodies = new MethodBodyLookup(pair.Old.Path);
            using var newBodies = new MethodBodyLookup(pair.New.Path);

            foreach (var key in keys)
            {
                var oldMethod = oldMethods[key];
                var newMethod = newMethods[key];
                var subject = SubjectFromMethod(newMethod);
                if (!MatchesTypeFilters(subject.TypeName ?? "", typeFilters))
                    continue;
                if (!MatchesMemberTargets(subject, memberTargetIdentities))
                    continue;
                var oldAvailable = oldBodies.TryDecode(oldMethod.MetadataToken, out var oldBody, out var oldReason);
                var newAvailable = newBodies.TryDecode(newMethod.MetadataToken, out var newBody, out var newReason);

                if (!oldAvailable || !newAvailable)
                {
                    if (!oldAvailable && !newAvailable)
                        continue;
                    AddIlFailureEvidence(
                        builder,
                        subject,
                        !oldAvailable
                            ? IlBodyDiffResult.OldBodyMissing(oldReason).FailureRows[0]
                            : IlBodyDiffResult.NewBodyMissing(newReason).FailureRows[0]);
                    continue;
                }

                var diff = IlBodyDiff.Compare(
                    oldBodies.MetadataReader,
                    oldBody!,
                    newBodies.MetadataReader,
                    newBody!);
                if (diff.IsExact)
                    continue;
                if (!diff.FailureRows.IsDefaultOrEmpty)
                {
                    foreach (var failure in diff.FailureRows)
                        AddIlFailureEvidence(builder, subject, failure);
                    if (diff.Rows.IsDefaultOrEmpty)
                        continue;
                }
                else if (!string.IsNullOrEmpty(diff.Failure))
                {
                    AddIlFailureEvidence(
                        builder,
                        subject,
                        new IlDiffFailureRow(IlDiffFailureKind.DecodeFailure, diff.Failure));
                    if (diff.Rows.IsDefaultOrEmpty)
                        continue;
                }

                foreach (var hunk in diff.Rows.GroupBy(row => row.HunkId).OrderBy(group => group.Key))
                {
                    var removed = hunk.Where(row => row.Kind == IlDiffKind.Remove).ToArray();
                    var added = hunk.Where(row => row.Kind == IlDiffKind.Add).ToArray();
                    var displayRows = hunk.Select(IlDiffPrinter.ToDisplayRow).ToImmutableArray();
                    var kind = removed.Length == 0
                        ? ResearchChangeKind.Added
                        : added.Length == 0
                            ? ResearchChangeKind.Removed
                            : ResearchChangeKind.Changed;
                    string descriptorId = kind switch
                        {
                            ResearchChangeKind.Added => "il.operation.added",
                            ResearchChangeKind.Removed => "il.operation.removed",
                            _ => "il.hunk.changed",
                        };
                    builder.Add(new ResearchChange(
                        subject,
                        ResearchChangeMechanism.IlBody,
                        Descriptor(descriptorId, "IL body"),
                        kind,
                        oldValue: FormatOperations(removed),
                        newValue: FormatOperations(added),
                        oldIlOffset: removed.Select(row => (int?)row.Operation.Offset).FirstOrDefault(offset => offset is not null),
                        newIlOffset: added.Select(row => (int?)row.Operation.Offset).FirstOrDefault(offset => offset is not null),
                        category: ResearchChangeCategory.IlBody,
                        ilDisplayRows: displayRows));
                }
            }
        }
    }

    static void AddIlFailureEvidence(ResultBuilder builder, ResearchSubjectKey subject, IlDiffFailureRow failure)
    {
        var kind = failure.Kind switch
        {
            IlDiffFailureKind.OldBodyMissing => ResearchChangeKind.Added,
            IlDiffFailureKind.NewBodyMissing => ResearchChangeKind.Removed,
            _ => ResearchChangeKind.Changed,
        };
        string descriptorId = $"il.diff.{ToKebabCase(failure.Kind.ToString())}";
        builder.Add(new ResearchChange(
            subject,
            ResearchChangeMechanism.IlBody,
            Descriptor(descriptorId, failure.Kind.ToString()),
            kind,
            oldValue: failure.Side == "old" ? failure.Detail ?? failure.Message : null,
            newValue: failure.Side == "new" ? failure.Detail ?? failure.Message : null,
            detail: failure.Detail ?? failure.Message,
            category: ResearchChangeCategory.IlBody,
            ilFailureRow: failure,
            ilDisplayFailureRow: IlDiffPrinter.ToDisplayFailureRow(failure)));
    }

    static void AddCSharpDiff(
        ResultBuilder builder,
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        IReadOnlySet<string>? typeFilters,
        IReadOnlySet<string>? memberTargetIdentities)
    {
        if (oldInput.AssemblyPaths.Count == 0 || newInput.AssemblyPaths.Count == 0)
            return;

        var diff = CSharpBodyDiff.CompareAssemblies(oldInput.AssemblyPaths, newInput.AssemblyPaths, typeFilters: typeFilters);
        foreach (var failure in diff.FailureRows.IsDefault ? [] : diff.FailureRows)
        {
            var subject = ResearchMemberIdentity.SubjectFromAnchor(failure.Anchor, failure.Member);
            if (MatchesMemberTargets(subject, memberTargetIdentities))
                AddCSharpFailureEvidence(builder, subject, failure);
        }

        foreach (var row in diff.Rows)
        {
            var subject = ResearchMemberIdentity.SubjectFromAnchor(row.Anchor, row.Member);
            if (!MatchesMemberTargets(subject, memberTargetIdentities))
                continue;
            var kind = row.Kind switch
            {
                CSharpDiffKind.Add => ResearchChangeKind.Added,
                CSharpDiffKind.Remove => ResearchChangeKind.Removed,
                _ => ResearchChangeKind.Changed,
            };
            builder.Add(new ResearchChange(
                subject,
                ResearchChangeMechanism.CSharp,
                Descriptor(row.ChangeId, row.Kind.ToString()),
                kind,
                oldValue: row.OldOperation?.Value ?? row.OldValue
                    ?? (kind == ResearchChangeKind.Removed ? row.Text : null),
                newValue: row.NewOperation?.Value ?? row.NewValue
                    ?? (kind == ResearchChangeKind.Added ? row.Text : null),
                detail: row.Message,
                category: ResearchChangeCategory.CSharp,
                cSharpRow: row,
                cSharpDisplayRows: [CSharpDiffPrinter.ToDisplayRow(row)]));
        }
    }

    static void AddCSharpFailureEvidence(ResultBuilder builder, ResearchSubjectKey subject, CSharpDiffFailureRow failure)
    {
        var kind = failure.Kind switch
        {
            CSharpDiffFailureKind.OldBodyMissing => ResearchChangeKind.Added,
            CSharpDiffFailureKind.NewBodyMissing => ResearchChangeKind.Removed,
            _ => ResearchChangeKind.Changed,
        };
        string descriptorId = $"csharp.diff.{ToKebabCase(failure.Kind.ToString())}";
        builder.Add(new ResearchChange(
            subject,
            ResearchChangeMechanism.CSharp,
            Descriptor(descriptorId, failure.Kind.ToString()),
            kind,
            oldValue: failure.Side == "old" ? failure.Detail ?? failure.Message : null,
            newValue: failure.Side == "new" ? failure.Detail ?? failure.Message : null,
            detail: failure.Detail ?? failure.Message,
            category: ResearchChangeCategory.CSharp,
            cSharpFailureRow: failure,
            cSharpDisplayFailureRow: CSharpDiffPrinter.ToDisplayFailureRow(failure)));
    }

    static ApiSurface? ResolveApiSurface(ResearchDiffInput input, bool includeAll)
    {
        if (input.ApiSurface is not null)
            return input.ApiSurface;
        if (input.AssemblyPaths.Count == 0)
            return null;

        var surfaces = input.AssemblyPaths.Select(path => AssemblyReader.ExtractApiSurface(path, includeAll)).ToArray();
        if (surfaces.Any(surface => surface is null))
            throw new InvalidOperationException("Could not extract API surface for one or more diff inputs.");
        if (surfaces.Length == 1)
            return surfaces[0];

        return new ApiSurface
        {
            Name = string.Join(",", surfaces.Select(surface => surface!.Name).Where(name => !string.IsNullOrEmpty(name))),
            Types = [.. surfaces.SelectMany(surface => surface!.Types)],
        };
    }

    static IEnumerable<(LibraryBodyIndex Old, LibraryBodyIndex New)> PairedBodyIndexes(ResearchDiffInput oldInput, ResearchDiffInput newInput)
        => PairedBodyIndexEntries(oldInput, newInput).Select(pair => (pair.Old.Index, pair.New.Index));

    static IEnumerable<(BodyIndexEntry Old, BodyIndexEntry New)> PairedBodyIndexEntries(ResearchDiffInput oldInput, ResearchDiffInput newInput)
    {
        var oldIndexes = BodyIndexEntries(oldInput).ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var newIndexes = BodyIndexEntries(newInput).ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        foreach (var key in oldIndexes.Keys.Intersect(newIndexes.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            yield return (oldIndexes[key], newIndexes[key]);
    }

    static IEnumerable<BodyIndexEntry> BodyIndexEntries(ResearchDiffInput input)
    {
        if (input.BodyIndexes is { Count: > 0 } bodyIndexes)
        {
            foreach (var index in bodyIndexes)
                yield return new BodyIndexEntry(AssemblyKey(index), index.Path, index);
            yield break;
        }

        foreach (var path in input.AssemblyPaths)
        {
            var index = LibraryBodyIndex.Open(path);
            yield return new BodyIndexEntry(AssemblyKey(index), path, index);
        }
    }

    static Dictionary<string, ResearchSubjectKey> MethodSubjectsByBodySignalKey(
        LibraryBodyIndex oldIndex,
        LibraryBodyIndex newIndex,
        IReadOnlySet<string>? memberTargetIdentities = null)
    {
        var oldGeneratedFrameworkTypes = oldIndex.GeneratedFrameworkTypeNames;
        var newGeneratedFrameworkTypes = newIndex.GeneratedFrameworkTypeNames;
        return oldIndex.Methods
            .Where(method => !IsGeneratedMethod(method, oldGeneratedFrameworkTypes))
            .Concat(newIndex.Methods.Where(method => !IsGeneratedMethod(method, newGeneratedFrameworkTypes)))
            .Select(method => (Key: BodySignalMethodKey(method), Subject: SubjectFromMethod(method)))
            .Where(entry => MatchesMemberTargets(entry.Subject, memberTargetIdentities))
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Subject, StringComparer.Ordinal);
    }

    static bool MatchesMemberTargets(ResearchSubjectKey subject, IReadOnlySet<string>? memberTargetIdentities)
        => memberTargetIdentities is null
           || memberTargetIdentities.Count == 0
           || memberTargetIdentities.Contains(subject.Id);

    static ResearchSubjectKey ApiSubject(string typeName, ApiChange change)
    {
        if (!IsMemberChange(change.Kind))
            return new ResearchSubjectKey(ResearchSubjectKind.Type, $"type:{typeName}", typeName, typeName: typeName);

        var kind = Direction(change.Kind);
        var handle = kind == ResearchChangeKind.Removed
            ? change.Subject?.OldMember
            : change.Subject?.NewMember ?? change.Subject?.OldMember;
        var memberName = handle?.MemberName ?? change.Subject?.MemberName;

        string memberId;
        string display;
        if (handle is not null)
        {
            memberId = handle.Anchor?.StableSelector ?? handle.Anchor?.CanonicalSignature ?? ApiMemberId(handle.Type, handle.Member);
            display = ApiMemberDisplay(handle.Type, handle.Member);
        }
        else
        {
            var value = kind == ResearchChangeKind.Removed
                ? change.Subject?.OldIdentity ?? change.OldValue
                : change.Subject?.NewIdentity ?? change.NewValue;
            memberId = $"member:{typeName}::{value ?? memberName ?? change.Kind.ToString()}";
            display = $"{typeName}.{memberName ?? value ?? change.Kind.ToString()}";
        }

        return new ResearchSubjectKey(ResearchSubjectKind.Member, memberId, display, typeName, memberName);
    }

    static ApiType? FindType(ApiSurface surface, string typeName)
        => surface.Types.FirstOrDefault(type => type.FullName == typeName);

    static string ApiMemberId(ApiType type, ApiMember member)
    {
        if (ApiMemberIdentity.TryGetCanonicalSignature(type, member, out var canonical))
            return ApiMemberIdentity.CreateHandle(type, member).Anchor?.StableSelector ?? $"member:{canonical}";
        return $"member:{type.FullName}::{member.Signature ?? $"{member.Kind}:{member.Name}"}";
    }

    static string ApiMemberDisplay(ApiType type, ApiMember member)
        => member.SignatureModel is { } signature
            ? $"{type.FullName}.{member.Name}{signature.ParameterTypesSummary}"
            : $"{type.FullName}.{member.Name}";

    static bool IsMemberChange(ChangeKind kind)
        => kind is ChangeKind.MemberAdded or ChangeKind.MemberRemoved or ChangeKind.MemberSignatureChanged
            or ChangeKind.VirtualRemoved or ChangeKind.AbstractMemberAdded or ChangeKind.EnumValueChanged
            or ChangeKind.MemberAttributeAdded or ChangeKind.MemberAttributeRemoved;

    static ResearchChangeKind Direction(ChangeKind kind)
        => kind switch
        {
            ChangeKind.TypeAdded or ChangeKind.MemberAdded or ChangeKind.InterfaceAdded
                or ChangeKind.TypeAttributeAdded or ChangeKind.MemberAttributeAdded => ResearchChangeKind.Added,
            ChangeKind.TypeRemoved or ChangeKind.MemberRemoved or ChangeKind.InterfaceRemoved
                or ChangeKind.TypeAttributeRemoved or ChangeKind.MemberAttributeRemoved => ResearchChangeKind.Removed,
            _ => ResearchChangeKind.Changed,
        };

    static ResearchChangeKind Direction(IlDiffKind kind)
        => kind switch
        {
            IlDiffKind.Add => ResearchChangeKind.Added,
            IlDiffKind.Remove => ResearchChangeKind.Removed,
            _ => ResearchChangeKind.Changed,
        };

    static ResearchChangeKind Direction(IlDiffFailureKind kind)
        => kind switch
        {
            IlDiffFailureKind.OldBodyMissing => ResearchChangeKind.Added,
            IlDiffFailureKind.NewBodyMissing => ResearchChangeKind.Removed,
            _ => ResearchChangeKind.Changed,
        };

    static ResearchChangeKind Direction(CSharpDiffKind kind)
        => kind switch
        {
            CSharpDiffKind.Add => ResearchChangeKind.Added,
            CSharpDiffKind.Remove => ResearchChangeKind.Removed,
            _ => ResearchChangeKind.Changed,
        };

    static ResearchChangeKind Direction(CSharpDiffFailureKind kind)
        => kind switch
        {
            CSharpDiffFailureKind.OldBodyMissing => ResearchChangeKind.Added,
            CSharpDiffFailureKind.NewBodyMissing => ResearchChangeKind.Removed,
            _ => ResearchChangeKind.Changed,
        };

    static ResearchChangeCategory ToResearchCategory(ApiChangeCategory category)
        => category switch
        {
            ApiChangeCategory.Attribute => ResearchChangeCategory.Attribute,
            _ => ResearchChangeCategory.Signature,
        };

    static ResearchSubjectKey SubjectFromMethod(MethodIdentity method)
        => ResearchMemberIdentity.SubjectFromMethod(method);

    static bool IsConversionOperator(string methodName)
        => methodName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";

    static ResearchSubjectKey UnknownMemberSubject(string key)
        => new(ResearchSubjectKind.Member, $"member:{key}", key);

    static string BodySignalMethodKey(MethodIdentity method)
        => $"{method.AssemblyName}|{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{method.GenericArity}|{method.IsExtension}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    static string MethodMatchKey(MethodIdentity method)
        => $"{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{method.GenericArity}|{method.IsExtension}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    static Dictionary<string, MethodIdentity> MethodLookup(LibraryBodyIndex index)
    {
        var methods = new Dictionary<string, MethodIdentity>(StringComparer.Ordinal);
        foreach (var method in index.Methods)
            methods.TryAdd(MethodMatchKey(method), method);
        return methods;
    }

    static Dictionary<string, int> CountShapes(List<OptimizationOpportunity>? opportunities)
        => opportunities is null
            ? []
            : opportunities.GroupBy(opportunity => opportunity.Shape, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    static string FormatDelta(int delta) => delta > 0 ? $"+{delta}" : delta.ToString();

    static string FormatList(ImmutableArray<string> values)
        => values.IsDefaultOrEmpty ? "-" : string.Join(", ", values);

    static string? Evidence(MethodSignals? oldSignals, MethodSignals? newSignals)
    {
        var oldEvidence = FormatOffsets(oldSignals?.Evidence ?? []);
        var newEvidence = FormatOffsets(newSignals?.Evidence ?? []);
        if (oldEvidence is null && newEvidence is null)
            return null;
        return $"old {oldEvidence ?? "-"}; new {newEvidence ?? "-"}";
    }

    static string? FormatOptimizationEvidence(List<OptimizationOpportunity>? oldOps, List<OptimizationOpportunity>? newOps, string shape)
    {
        var oldOffsets = FormatOffsets(Offsets(oldOps, shape));
        var newOffsets = FormatOffsets(Offsets(newOps, shape));
        if (oldOffsets is null && newOffsets is null)
            return null;
        return $"old {oldOffsets ?? "-"}; new {newOffsets ?? "-"}";
    }

    static ImmutableArray<int> Offsets(List<OptimizationOpportunity>? ops, string shape)
        => ops is null ? [] : [.. ops.Where(op => op.Shape == shape && op.ILOffset is not null).Select(op => op.ILOffset!.Value).Distinct().Order()];

    static string? FormatOffsets(ImmutableArray<int> offsets)
        => offsets.IsDefaultOrEmpty ? null : string.Join(",", offsets.Select(offset => $"IL_{offset:X4}"));

    internal static bool MatchesTypeFilters(string typeFullName, IReadOnlySet<string>? filters)
        => filters is null || filters.Count == 0 || filters.Any(filter => MatchesDiffTypeFilter(typeFullName, filter));

    static bool MatchesDiffTypeFilter(string typeFullName, string filter)
    {
        if (TypeMatcher.MatchesTypeFilter(typeFullName, filter))
            return true;

        if (filter.Contains('*') || filter.Contains('?'))
            return false;

        var normalizedFilter = TypeMatcher.Normalize(filter);
        return typeFullName.StartsWith(normalizedFilter + ".", StringComparison.OrdinalIgnoreCase)
               || typeFullName.Contains("." + normalizedFilter + ".", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsGeneratedMethod(MethodIdentity method, IReadOnlySet<string> generatedFrameworkTypes)
        => MemberFilters.IsCompilerGenerated(method.Name)
           || TypeFilters.IsCompilerGeneratedNested(method.DeclaringType.Name)
           || IsSystemTextJsonContextGeneratedMethod(method)
           || generatedFrameworkTypes.Contains(method.DeclaringType.ToQualifiedDisplayString());

    static bool IsSystemTextJsonContextGeneratedMethod(MethodIdentity method)
        => method.Name is "TryGetTypeInfoForRuntimeCustomConverter"
           && method.IsStatic
           && method.ReturnType.Equals(TypeRef.CoreLib("System", "Boolean"))
           && method.ParameterTypes.Length == 2
           && method.ParameterTypes[0].Equals(TypeRef.Definition("System.Text.Json", "System.Text.Json", "JsonSerializerOptions"))
           && method.ParameterTypes[1] is { Kind: TypeRefKind.ByRef, ElementType: { } jsonTypeInfo }
           && IsJsonTypeInfo(jsonTypeInfo);

    static bool IsJsonTypeInfo(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
           && type.ElementType is { } definition
           && definition.Equals(TypeRef.Definition("System.Text.Json", "System.Text.Json.Serialization.Metadata", "JsonTypeInfo`1"));

    static string AssemblyKey(LibraryBodyIndex index)
        => index.Methods.Select(method => method.AssemblyName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? Path.GetFileNameWithoutExtension(index.Path);

    static string FormatOperations(IReadOnlyList<IlDiffRow> rows)
        => rows.Count == 0 ? "" : string.Join("; ", rows.Select(row => row.Operation.Display));

    static string NormalizeChangePart(string value)
        => value.Replace(' ', '-').Replace('_', '-').ToLowerInvariant();

    static string ToKebabCase(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsUpper(ch) && i > 0)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    static string ChangeIdSuffix(IlDiffKind kind)
        => kind switch
        {
            IlDiffKind.Add => "added",
            IlDiffKind.Remove => "removed",
            IlDiffKind.Context => "context",
            _ => ToKebabCase(kind.ToString()),
        };

    sealed record BodyIndexEntry(string Key, string Path, LibraryBodyIndex Index);

    sealed record ResearchAnalysisMethod(
        ResearchSubjectKey Subject,
        MethodSignals Signals,
        ImmutableArray<AllocationOccurrence> Allocations,
        ImmutableArray<DirectCall> CallSites,
        List<OptimizationOpportunity> Opportunities);

    sealed class ResultBuilder
    {
        readonly List<ResearchChange> _changes = [];
        readonly List<AllocationFindingComparison> _allocationComparisons = [];
        readonly List<CallSiteFindingComparison> _callSiteComparisons = [];

        public ApiFindingComparison? ApiComparison { get; set; }

        public void Add(ResearchChange change) => _changes.Add(change);

        public void Add(AllocationFindingComparison comparison)
            => _allocationComparisons.Add(comparison);

        public void Add(CallSiteFindingComparison comparison)
            => _callSiteComparisons.Add(comparison);

        public ResearchComparison ToResult()
            => new(
                [.. _changes],
                apiDiff: null,
                apiComparison: ApiComparison,
                allocationComparisons: [.. _allocationComparisons],
                callSiteComparisons: [.. _callSiteComparisons]);
    }

    sealed class MethodBodyLookup : IDisposable
    {
        readonly FileStream _stream;
        readonly PEReader _peReader;
        readonly MetadataReader _metadataReader;

        public MethodBodyLookup(string path)
        {
            _stream = File.OpenRead(path);
            _peReader = new PEReader(_stream, PEStreamOptions.PrefetchEntireImage);
            _metadataReader = _peReader.GetMetadataReader();
        }

        public MetadataReader MetadataReader => _metadataReader;

        public bool TryDecode(int metadataToken, out MethodBodyBlock? body, out string? unavailableReason)
        {
            body = null;
            unavailableReason = null;
            var handle = MetadataTokens.EntityHandle(metadataToken);
            if (handle.Kind != HandleKind.MethodDefinition)
            {
                unavailableReason = $"token 0x{metadataToken:X8} is not a MethodDef";
                return false;
            }

            var method = _metadataReader.GetMethodDefinition((MethodDefinitionHandle)handle);
            if (method.RelativeVirtualAddress == 0)
            {
                unavailableReason = "method has no IL body";
                return false;
            }

            body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
            return true;
        }

        public void Dispose()
        {
            _peReader.Dispose();
            _stream.Dispose();
        }
    }
}
