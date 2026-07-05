using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

[Flags]
public enum ResearchDiffMechanism
{
    None = 0,
    Api = 1,
    BodySignals = 2,
    IlBody = 4,
    CSharp = 8,
    ReturnToSender = 16,
    AllAvailable = Api | BodySignals | IlBody | CSharp | ReturnToSender,
}

public enum ResearchDiffSubjectKind
{
    Type,
    Member,
}

public enum ResearchDiffDirection
{
    Added,
    Removed,
    Changed,
}

public enum ResearchDiffChangeCategory
{
    Unknown,
    Signature,
    Attribute,
    BodySignal,
    IlBody,
    CSharp,
    RoundTrip,
}

public enum ResearchDiffEvidenceKind
{
    MetadataApi,
    IlBody,
    BodySignal,
    CSharp,
}

public sealed record ResearchDiffRow(
    string ChangeId,
    ResearchDiffEvidenceKind EvidenceKind,
    string Message,
    ApiChange? ApiChange = null,
    IlDiffRow? IlRow = null,
    IlDiffDisplayRow? IlDisplayRow = null,
    IlDiffFailureRow? IlFailureRow = null,
    IlDiffDisplayFailureRow? IlDisplayFailureRow = null,
    BodySignalDiffRow? BodySignalRow = null,
    CSharpDiffRow? CSharpRow = null,
    CSharpDiffFailureRow? CSharpFailureRow = null,
    CSharpDiffDisplayFailureRow? CSharpDisplayFailureRow = null);

public sealed record ResearchDiffOptions(
    ResearchDiffMechanism Mechanisms = ResearchDiffMechanism.AllAvailable,
    bool IncludeAllApi = false,
    ApiDiffScope ApiScope = ApiDiffScope.Signature,
    IReadOnlySet<string>? TypeFilters = null);

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

public sealed record ResearchSubjectKey(
    ResearchDiffSubjectKind Kind,
    string Id,
    string Display,
    string? TypeName = null,
    string? MemberName = null,
    TypeAnchor? TypeAnchor = null,
    MemberAnchor? Anchor = null,
    MetadataMemberRef? MetadataMember = null,
    MetadataTypeRef? MetadataType = null);

public sealed record ResearchDiffEvidence(
    ResearchDiffMechanism Mechanism,
    string ChangeId,
    ResearchDiffDirection Direction,
    string? OldValue = null,
    string? NewValue = null,
    string? Delta = null,
    int? OldIlOffset = null,
    int? NewIlOffset = null,
    string? Detail = null,
    ResearchDiffChangeCategory Category = ResearchDiffChangeCategory.Unknown,
    MemberAnchor? Anchor = null,
    MetadataMemberRef? MetadataMember = null,
    MetadataTypeRef? MetadataType = null,
    IlDiffRow? IlRow = null,
    BodySignalDiffRow? BodySignalRow = null,
    CSharpDiffRow? CSharpRow = null,
    string? Signal = null,
    string? Shape = null,
    int? Magnitude = null,
    int DirectionScore = 0,
    bool SubjectInBoth = true,
    bool InLoop = false,
    ImmutableArray<IlDiffDisplayRow> IlDisplayRows = default,
    IlDiffDisplayFailureRow? IlDisplayFailureRow = null,
    CSharpDiffDisplayFailureRow? CSharpDisplayFailureRow = null);

public sealed record ResearchSubjectDiff(
    ResearchSubjectKey Subject,
    IReadOnlyList<ResearchDiffEvidence> Evidence)
{
    public bool ApiChanged => Evidence.Any(evidence => evidence.Mechanism == ResearchDiffMechanism.Api);

    public bool ApiSignatureChanged
        => Evidence.Any(evidence => evidence.Mechanism == ResearchDiffMechanism.Api && evidence.Category == ResearchDiffChangeCategory.Signature);

    public bool ApiAttributeChanged
        => Evidence.Any(evidence => evidence.Mechanism == ResearchDiffMechanism.Api && evidence.Category == ResearchDiffChangeCategory.Attribute);

    public bool ImplementationChanged
        => Evidence.Any(evidence => evidence.Mechanism is ResearchDiffMechanism.BodySignals or ResearchDiffMechanism.IlBody or ResearchDiffMechanism.CSharp);

    public bool HasMechanism(ResearchDiffMechanism mechanism)
        => Evidence.Any(evidence => evidence.Mechanism == mechanism);

    public bool HasChange(string changeId)
        => Evidence.Any(evidence => string.Equals(evidence.ChangeId, changeId, StringComparison.Ordinal));

    public bool HasChangePrefix(string changeIdPrefix)
        => Evidence.Any(evidence => evidence.ChangeId.StartsWith(changeIdPrefix, StringComparison.Ordinal));

    public bool HasChangeCategory(ResearchDiffChangeCategory category)
        => Evidence.Any(evidence => evidence.Category == category);
}

public sealed record ResearchDiffResult(
    IReadOnlyList<ResearchSubjectDiff> Subjects,
    ApiDiff? ApiDiff = null,
    ImmutableArray<ResearchDiffRow> Rows = default)
{
    public bool IsEmpty => Subjects.Count == 0 && Rows.IsDefaultOrEmpty;

    public IReadOnlyList<ResearchSubjectDiff> MembersWhere(Func<ResearchSubjectDiff, bool> predicate)
        => [.. Subjects.Where(subject => subject.Subject.Kind == ResearchDiffSubjectKind.Member && predicate(subject))];
}

public static class ResearchDiff
{
    public static string ToChangeIdPart(string value)
        => ToKebabCase(value);

    public static ResearchDiffResult FromApiDiff(ApiDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var rows = ImmutableArray.CreateBuilder<ResearchDiffRow>();
        foreach (var typeDiff in diff.TypeDiffs)
        {
            foreach (var change in typeDiff.Changes)
            {
                rows.Add(new ResearchDiffRow(
                    $"api.{ToKebabCase(change.Kind.ToString())}",
                    ResearchDiffEvidenceKind.MetadataApi,
                    change.Message,
                    ApiChange: change));
            }
        }

        return new ResearchDiffResult([], diff, rows.ToImmutable());
    }

    public static ResearchDiffResult FromIlBodyDiff(IlBodyDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var rows = ImmutableArray.CreateBuilder<ResearchDiffRow>();
        if (!diff.FailureRows.IsDefaultOrEmpty)
        {
            rows.AddRange(diff.FailureRows.Select(row => new ResearchDiffRow(
                $"il.diff.{ToKebabCase(row.Kind.ToString())}",
                ResearchDiffEvidenceKind.IlBody,
                row.Message,
                IlFailureRow: row,
                IlDisplayFailureRow: IlDiffPrinter.ToDisplayFailureRow(row))));
        }
        else if (diff.Failure is { Length: > 0 } failure)
        {
            rows.Add(new ResearchDiffRow("il.diff.failed", ResearchDiffEvidenceKind.IlBody, failure));
        }

        if (!diff.Rows.IsDefaultOrEmpty)
        {
            rows.AddRange(diff.Rows.Select(row =>
            {
                var display = IlDiffPrinter.ToDisplayRow(row);
                return new ResearchDiffRow(
                    $"il.operation.{ChangeIdSuffix(row.Kind)}",
                    ResearchDiffEvidenceKind.IlBody,
                    display.Message,
                    IlRow: row,
                    IlDisplayRow: display);
            }));
        }

        return new ResearchDiffResult([], Rows: rows.ToImmutable());
    }

    public static ResearchDiffResult FromBodySignalDiff(BodySignalDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return new ResearchDiffResult(
            [],
            Rows:
            [
                .. diff.Rows.Select(row => new ResearchDiffRow(
                    $"unsafe.{ToKebabCase(row.Signal)}.{ToKebabCase(row.Kind.ToString())}",
                    ResearchDiffEvidenceKind.BodySignal,
                    $"{row.Kind} {row.Signal}: {row.Operation}",
                    BodySignalRow: row))
            ]);
    }

    public static ResearchDiffResult FromCSharpBodyDiff(CSharpBodyDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var rows = ImmutableArray.CreateBuilder<ResearchDiffRow>();
        if (!diff.FailureRows.IsDefaultOrEmpty)
        {
            rows.AddRange(diff.FailureRows.Select(row => new ResearchDiffRow(
                $"csharp.diff.{ToKebabCase(row.Kind.ToString())}",
                ResearchDiffEvidenceKind.CSharp,
                row.Message,
                CSharpFailureRow: row,
                CSharpDisplayFailureRow: CSharpDiffPrinter.ToDisplayFailureRow(row))));
        }

        if (!diff.Rows.IsDefaultOrEmpty)
        {
            rows.AddRange(diff.Rows.Select(row => new ResearchDiffRow(
                row.ChangeId,
                ResearchDiffEvidenceKind.CSharp,
                row.Message,
                CSharpRow: row)));
        }

        return new ResearchDiffResult([], Rows: rows.ToImmutable());
    }

    public static ResearchDiffResult Combine(params ResearchDiffResult[] results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var builder = new ResultBuilder
        {
            ApiDiff = results.FirstOrDefault(result => result.ApiDiff is not null)?.ApiDiff
        };

        foreach (var subject in results.SelectMany(result => result.Subjects))
        {
            foreach (var evidence in subject.Evidence)
                builder.Add(subject.Subject, evidence);
        }

        var combined = builder.ToResult();
        return combined with { Rows = [.. results.SelectMany(result => result.Rows.IsDefault ? [] : result.Rows)] };
    }

    public static ResearchDiffResult CompareAssemblies(string oldAssemblyPath, string newAssemblyPath, ResearchDiffOptions? options = null)
        => Compare(ResearchDiffInput.FromAssembly(oldAssemblyPath), ResearchDiffInput.FromAssembly(newAssemblyPath), options);

    public static ResearchDiffResult CompareApiSurfaces(ApiSurface oldSurface, ApiSurface newSurface)
        => Compare(ResearchDiffInput.FromApiSurface(oldSurface), ResearchDiffInput.FromApiSurface(newSurface),
            new ResearchDiffOptions(ResearchDiffMechanism.Api));

    public static ResearchDiffResult Compare(ResearchDiffInput oldInput, ResearchDiffInput newInput, ResearchDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldInput);
        ArgumentNullException.ThrowIfNull(newInput);

        options ??= new ResearchDiffOptions();
        var builder = new ResultBuilder();
        var anchors = new MemberAnchorCache();

        if (options.Mechanisms.HasFlag(ResearchDiffMechanism.Api))
            AddApiDiff(builder, oldInput, newInput, options.IncludeAllApi, options.ApiScope);

        if (options.Mechanisms.HasFlag(ResearchDiffMechanism.BodySignals))
            AddBodySignalDiff(builder, oldInput, newInput, options.TypeFilters, anchors);

        if (options.Mechanisms.HasFlag(ResearchDiffMechanism.IlBody))
            AddIlBodyDiff(builder, oldInput, newInput, anchors);

        if (options.Mechanisms.HasFlag(ResearchDiffMechanism.CSharp))
            AddCSharpDiff(builder, oldInput, newInput, options.TypeFilters, anchors);

        return builder.ToResult();
    }

    static void AddApiDiff(ResultBuilder builder, ResearchDiffInput oldInput, ResearchDiffInput newInput, bool includeAll, ApiDiffScope apiScope)
    {
        var oldSurface = ResolveApiSurface(oldInput, includeAll);
        var newSurface = ResolveApiSurface(newInput, includeAll);
        if (oldSurface is null || newSurface is null)
            return;

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, new ApiDiffOptions(apiScope));
        builder.ApiDiff = diff;
        foreach (var typeDiff in diff.TypeDiffs)
        {
            foreach (var change in typeDiff.Changes)
            {
                var subject = ApiSubject(oldSurface, newSurface, typeDiff.TypeFullName, change);
                builder.Add(subject, new ResearchDiffEvidence(
                    ResearchDiffMechanism.Api,
                    $"api.{ToKebabCase(change.Kind.ToString())}",
                    Direction(change.Kind),
                    change.OldValue,
                    change.NewValue,
                    Detail: $"{change.Classification}: {change.Message}",
                    Category: ToResearchCategory(change.Category)));
            }
        }
    }

    static void AddBodySignalDiff(
        ResultBuilder builder,
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        IReadOnlySet<string>? typeFilters,
        MemberAnchorCache anchors)
    {
        foreach (var pair in PairedBodyIndexEntries(oldInput, newInput))
        {
            var oldAnchors = anchors.Get(pair.Old.Path);
            var newAnchors = anchors.Get(pair.New.Path);
            AddAnalysisSignalDiff(builder, pair.Old.Index, pair.New.Index, typeFilters, oldAnchors, newAnchors);

            var oldSubjects = MethodSubjectsByBodySignalKey(pair.Old.Index, oldAnchors);
            var newSubjects = MethodSubjectsByBodySignalKey(pair.New.Index, newAnchors);
            foreach (var row in BodySignalDiff.CompareUnsafe(pair.Old.Index, pair.New.Index).Rows)
            {
                var direction = row.Kind == BodySignalDiffKind.Added ? ResearchDiffDirection.Added : ResearchDiffDirection.Removed;
                var subject = direction == ResearchDiffDirection.Added
                    ? newSubjects.GetValueOrDefault(row.Member) ?? oldSubjects.GetValueOrDefault(row.Member) ?? UnknownMemberSubject(row.Member)
                    : oldSubjects.GetValueOrDefault(row.Member) ?? newSubjects.GetValueOrDefault(row.Member) ?? UnknownMemberSubject(row.Member);
                var suffix = direction == ResearchDiffDirection.Added ? "added" : "removed";
                builder.Add(subject, new ResearchDiffEvidence(
                    ResearchDiffMechanism.BodySignals,
                    $"unsafe.{NormalizeChangePart(row.Signal)}.{suffix}",
                    direction,
                    OldIlOffset: direction == ResearchDiffDirection.Removed ? row.ILOffset : null,
                    NewIlOffset: direction == ResearchDiffDirection.Added ? row.ILOffset : null,
                    Detail: $"{row.Operation}: {row.Evidence}",
                    Category: ResearchDiffChangeCategory.BodySignal,
                    Anchor: subject.Anchor,
                    MetadataMember: subject.MetadataMember,
                    MetadataType: subject.MetadataType,
                    BodySignalRow: row));
            }
        }

        static void AddAnalysisSignalDiff(
            ResultBuilder builder,
            LibraryBodyIndex oldIndex,
            LibraryBodyIndex newIndex,
            IReadOnlySet<string>? typeFilters,
            IReadOnlyDictionary<int, MemberAnchor> oldAnchors,
            IReadOnlyDictionary<int, MemberAnchor> newAnchors)
        {
            var oldSnapshot = BuildAnalysisSnapshot(oldIndex, typeFilters, oldAnchors);
            var newSnapshot = BuildAnalysisSnapshot(newIndex, typeFilters, newAnchors);
            foreach (var key in oldSnapshot.Keys.Union(newSnapshot.Keys, StringComparer.Ordinal))
            {
                oldSnapshot.TryGetValue(key, out var oldMethod);
                newSnapshot.TryGetValue(key, out var newMethod);
                var subject = newMethod?.Subject ?? oldMethod?.Subject ?? UnknownMemberSubject(key);
                var inBoth = oldMethod is not null && newMethod is not null;
                AddCountRows(builder, subject, inBoth, oldMethod?.Signals, newMethod?.Signals);
                AddExceptionRow(builder, subject, inBoth, oldMethod?.Signals, newMethod?.Signals);
                AddOptimizationRows(builder, subject, inBoth, oldMethod?.Opportunities, newMethod?.Opportunities);
            }
        }

        static Dictionary<string, ResearchAnalysisMethod> BuildAnalysisSnapshot(
            LibraryBodyIndex index,
            IReadOnlySet<string>? typeFilters,
            IReadOnlyDictionary<int, MemberAnchor> anchors)
        {
            var methods = new Dictionary<string, ResearchAnalysisMethod>(StringComparer.Ordinal);
            var generatedFrameworkTypes = index.GeneratedFrameworkTypeNames;
            var signalsByToken = index.GetMethodSignals();
            foreach (var method in index.Methods)
            {
                if (IsGeneratedMethod(method, generatedFrameworkTypes))
                    continue;
                if (!MatchesTypeFilters(method.DeclaringType.ToQualifiedDisplayString(), typeFilters))
                    continue;
                signalsByToken.TryGetValue(method.MetadataToken, out var signals);
                var key = BodySignalMethodKey(method);
                if (!methods.TryGetValue(key, out var entry))
                {
                    entry = new ResearchAnalysisMethod(SubjectFromMethod(method, anchors.GetValueOrDefault(method.MetadataToken)), signals ?? MethodSignals.None, []);
                    methods[key] = entry;
                }
                else
                {
                    methods[key] = entry with { Signals = signals ?? MethodSignals.None };
                }
            }

            foreach (var opportunity in index.OptimizationOpportunities)
            {
                if (IsGeneratedMethod(opportunity.Method, generatedFrameworkTypes))
                    continue;
                if (!MatchesTypeFilters(opportunity.Method.DeclaringType.ToQualifiedDisplayString(), typeFilters))
                    continue;
                var key = BodySignalMethodKey(opportunity.Method);
                if (!methods.TryGetValue(key, out var entry))
                {
                    entry = new ResearchAnalysisMethod(SubjectFromMethod(opportunity.Method, anchors.GetValueOrDefault(opportunity.Method.MetadataToken)), MethodSignals.None, []);
                    methods[key] = entry;
                }
                entry.Opportunities.Add(opportunity);
            }

            return methods;
        }

        static void AddCountRows(ResultBuilder builder, ResearchSubjectKey subject, bool inBoth, MethodSignals? oldSignals, MethodSignals? newSignals)
        {
            AddCountRow(builder, subject, inBoth, "allocations", oldSignals?.Allocations ?? 0, newSignals?.Allocations ?? 0, Evidence(oldSignals, newSignals), oldSignals?.AllocInLoop ?? false, newSignals?.AllocInLoop ?? false);
            AddCountRow(builder, subject, inBoth, "copies", oldSignals?.Copies ?? 0, newSignals?.Copies ?? 0, Evidence(oldSignals, newSignals));
            AddCountRow(builder, subject, inBoth, "reflection", oldSignals?.Reflection ?? 0, newSignals?.Reflection ?? 0, Evidence(oldSignals, newSignals));
            AddCountRow(builder, subject, inBoth, "throws", oldSignals?.Throws ?? 0, newSignals?.Throws ?? 0, Evidence(oldSignals, newSignals));
            AddCountRow(builder, subject, inBoth, "catches", oldSignals?.Catches ?? 0, newSignals?.Catches ?? 0, Evidence(oldSignals, newSignals));
            AddCountRow(builder, subject, inBoth, "finallys", oldSignals?.Finallys ?? 0, newSignals?.Finallys ?? 0, Evidence(oldSignals, newSignals));
            AddCountRow(builder, subject, inBoth, "unsafe", oldSignals?.Unsafe == true ? 1 : 0, newSignals?.Unsafe == true ? 1 : 0, Evidence(oldSignals, newSignals));
        }

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
            => builder.Add(subject, new ResearchDiffEvidence(
                ResearchDiffMechanism.BodySignals,
                changeId,
                ResearchDiffDirection.Changed,
                oldValue,
                newValue,
                Delta: delta,
                Detail: detail,
                Category: ResearchDiffChangeCategory.BodySignal,
                Anchor: subject.Anchor,
                MetadataMember: subject.MetadataMember,
                MetadataType: subject.MetadataType,
                Signal: signal,
                Shape: shape,
                Magnitude: magnitude,
                DirectionScore: directionScore,
                SubjectInBoth: inBoth,
                InLoop: inLoop));
    }

    static void AddIlBodyDiff(
        ResultBuilder builder,
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        MemberAnchorCache anchors)
    {
        foreach (var pair in PairedBodyIndexEntries(oldInput, newInput))
        {
            var oldMethods = MethodLookup(pair.Old.Index);
            var newMethods = MethodLookup(pair.New.Index);
            var oldAnchors = anchors.Get(pair.Old.Path);
            var newAnchors = anchors.Get(pair.New.Path);
            var keys = oldMethods.Keys.Intersect(newMethods.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var oldBodies = new MethodBodyLookup(pair.Old.Path);
            using var newBodies = new MethodBodyLookup(pair.New.Path);

            foreach (var key in keys)
            {
                var oldMethod = oldMethods[key];
                var newMethod = newMethods[key];
                var oldAnchor = oldAnchors.GetValueOrDefault(oldMethod.MetadataToken);
                var newAnchor = newAnchors.GetValueOrDefault(newMethod.MetadataToken);
                var subject = SubjectFromMethod(newMethod, newAnchor ?? oldAnchor);
                var oldAvailable = oldBodies.TryDecode(oldMethod.MetadataToken, out var oldBody, out var oldReason);
                var newAvailable = newBodies.TryDecode(newMethod.MetadataToken, out var newBody, out var newReason);

                if (!oldAvailable || !newAvailable)
                {
                    if (!oldAvailable && !newAvailable)
                        continue;
                    var subjectAnchor = !oldAvailable ? newAnchor ?? oldAnchor : oldAnchor ?? newAnchor;
                    var failureAnchor = !oldAvailable ? oldAnchor ?? newAnchor : newAnchor ?? oldAnchor;
                    var failureMethod = !oldAvailable ? oldMethod : newMethod;
                    var activeSubject = !oldAvailable
                        ? SubjectFromMethod(newMethod, subjectAnchor)
                        : SubjectFromMethod(oldMethod, subjectAnchor);
                    AddIlFailureEvidence(
                        builder,
                        activeSubject,
                        !oldAvailable
                            ? IlBodyDiffResult.OldBodyMissing(oldReason).FailureRows[0]
                            : IlBodyDiffResult.NewBodyMissing(newReason).FailureRows[0],
                        failureAnchor,
                        MetadataMemberRef(failureMethod));
                    continue;
                }

                var diff = IlBodyDiff.Compare(oldBody!, newBody!);
                if (diff.IsExact)
                    continue;
                if (!diff.FailureRows.IsDefaultOrEmpty)
                {
                    foreach (var failure in diff.FailureRows)
                        AddIlFailureEvidence(
                            builder,
                            subject,
                            failure,
                            AnchorForFailure(failure, oldAnchor, newAnchor),
                            MetadataMemberRefForFailure(failure, oldMethod, newMethod));
                    if (diff.Rows.IsDefaultOrEmpty)
                        continue;
                }
                else if (!string.IsNullOrEmpty(diff.Failure))
                {
                    AddIlFailureEvidence(
                        builder,
                        subject,
                        new IlDiffFailureRow(IlDiffFailureKind.DecodeFailure, diff.Failure),
                        newAnchor ?? oldAnchor,
                        MetadataMemberRef(newMethod));
                    if (diff.Rows.IsDefaultOrEmpty)
                        continue;
                }

                foreach (var hunk in diff.Rows.GroupBy(row => row.HunkId).OrderBy(group => group.Key))
                {
                    var removed = hunk.Where(row => row.Kind == IlDiffKind.Remove).ToArray();
                    var added = hunk.Where(row => row.Kind == IlDiffKind.Add).ToArray();
                    var displayRows = hunk.Select(IlDiffPrinter.ToDisplayRow).ToImmutableArray();
                    var direction = removed.Length == 0
                        ? ResearchDiffDirection.Added
                        : added.Length == 0
                            ? ResearchDiffDirection.Removed
                            : ResearchDiffDirection.Changed;
                    builder.Add(subject, new ResearchDiffEvidence(
                        ResearchDiffMechanism.IlBody,
                        direction switch
                        {
                            ResearchDiffDirection.Added => "il.operation.added",
                            ResearchDiffDirection.Removed => "il.operation.removed",
                            _ => "il.hunk.changed",
                        },
                        direction,
                        OldValue: FormatDisplayLines(displayRows.Where(row => row.Kind == IlDiffKind.Remove)),
                        NewValue: FormatDisplayLines(displayRows.Where(row => row.Kind == IlDiffKind.Add)),
                        OldIlOffset: removed.Select(row => (int?)row.Operation.Offset).FirstOrDefault(offset => offset is not null),
                        NewIlOffset: added.Select(row => (int?)row.Operation.Offset).FirstOrDefault(offset => offset is not null),
                        Detail: FormatDisplayLines(displayRows),
                        Category: ResearchDiffChangeCategory.IlBody,
                        Anchor: newAnchor ?? oldAnchor,
                        MetadataMember: MetadataMemberRef(newMethod),
                        IlDisplayRows: displayRows));
                }
            }
        }
    }

    static void AddIlFailureEvidence(
        ResultBuilder builder,
        ResearchSubjectKey subject,
        IlDiffFailureRow failure,
        MemberAnchor? anchor = null,
        MetadataMemberRef? metadataMember = null)
    {
        var direction = failure.Kind switch
        {
            IlDiffFailureKind.OldBodyMissing => ResearchDiffDirection.Added,
            IlDiffFailureKind.NewBodyMissing => ResearchDiffDirection.Removed,
            _ => ResearchDiffDirection.Changed,
        };
        builder.Add(subject, new ResearchDiffEvidence(
            ResearchDiffMechanism.IlBody,
            $"il.diff.{ToKebabCase(failure.Kind.ToString())}",
            direction,
            OldValue: failure.Side == "old" ? failure.Detail ?? failure.Message : null,
            NewValue: failure.Side == "new" ? failure.Detail ?? failure.Message : null,
            Detail: failure.Detail ?? failure.Message,
            Category: ResearchDiffChangeCategory.IlBody,
            Anchor: anchor,
            MetadataMember: metadataMember,
            IlDisplayFailureRow: IlDiffPrinter.ToDisplayFailureRow(failure)));
    }

    static MemberAnchor? AnchorForFailure(IlDiffFailureRow failure, MemberAnchor? oldAnchor, MemberAnchor? newAnchor)
        => failure.Side switch
        {
            "old" => oldAnchor ?? newAnchor,
            "new" => newAnchor ?? oldAnchor,
            _ => newAnchor ?? oldAnchor,
        };

    static MetadataMemberRef MetadataMemberRefForFailure(IlDiffFailureRow failure, MethodIdentity oldMethod, MethodIdentity newMethod)
        => failure.Side switch
        {
            "old" => MetadataMemberRef(oldMethod),
            "new" => MetadataMemberRef(newMethod),
            _ => MetadataMemberRef(newMethod),
        };

    static void AddCSharpDiff(
        ResultBuilder builder,
        ResearchDiffInput oldInput,
        ResearchDiffInput newInput,
        IReadOnlySet<string>? typeFilters,
        MemberAnchorCache anchors)
    {
        if (oldInput.AssemblyPaths.Count == 0 || newInput.AssemblyPaths.Count == 0)
            return;

        var diff = CSharpBodyDiff.CompareAssemblies(oldInput.AssemblyPaths, newInput.AssemblyPaths, typeFilters: typeFilters, memberAnchorsByToken: anchors.Get);
        foreach (var failure in diff.FailureRows.IsDefault ? [] : diff.FailureRows)
            AddCSharpFailureEvidence(builder, failure);

        foreach (var row in diff.Rows)
        {
            var subject = new ResearchSubjectKey(
                ResearchDiffSubjectKind.Member,
                row.Anchor.StableSelector,
                $"{row.Anchor.TypeFullName}.{row.Anchor.MemberName}",
                row.Anchor.TypeFullName,
                row.Anchor.MemberName,
                new TypeAnchor(row.Anchor.TypeFullName),
                row.Anchor,
                row.MemberRef,
                row.TypeRef);
            var direction = row.Kind switch
            {
                CSharpDiffKind.Add => ResearchDiffDirection.Added,
                CSharpDiffKind.Remove => ResearchDiffDirection.Removed,
                _ => ResearchDiffDirection.Changed,
            };
            builder.Add(subject, new ResearchDiffEvidence(
                ResearchDiffMechanism.CSharp,
                row.ChangeId,
                direction,
                OldValue: row.OldOperation?.Value ?? row.OldValue ?? (direction == ResearchDiffDirection.Removed ? row.Text : null),
                NewValue: row.NewOperation?.Value ?? row.NewValue ?? (direction == ResearchDiffDirection.Added ? row.Text : null),
                Detail: row.Message,
                Category: ResearchDiffChangeCategory.CSharp,
                Anchor: row.Anchor,
                MetadataMember: row.MemberRef,
                MetadataType: row.TypeRef,
                CSharpRow: row));
        }
    }

    static void AddCSharpFailureEvidence(ResultBuilder builder, CSharpDiffFailureRow failure)
    {
        var subject = new ResearchSubjectKey(
            ResearchDiffSubjectKind.Member,
            failure.Anchor.StableSelector,
            $"{failure.Anchor.TypeFullName}.{failure.Anchor.MemberName}",
            failure.Anchor.TypeFullName,
            failure.Anchor.MemberName,
            new TypeAnchor(failure.Anchor.TypeFullName),
            failure.Anchor);
        var direction = failure.Kind switch
        {
            CSharpDiffFailureKind.OldBodyMissing => ResearchDiffDirection.Added,
            CSharpDiffFailureKind.NewBodyMissing => ResearchDiffDirection.Removed,
            _ => ResearchDiffDirection.Changed,
        };
        builder.Add(subject, new ResearchDiffEvidence(
            ResearchDiffMechanism.CSharp,
            $"csharp.diff.{ToKebabCase(failure.Kind.ToString())}",
            direction,
            OldValue: failure.Side == "old" ? failure.Detail ?? failure.Message : null,
            NewValue: failure.Side == "new" ? failure.Detail ?? failure.Message : null,
            Detail: failure.Detail ?? failure.Message,
            Category: ResearchDiffChangeCategory.CSharp,
            Anchor: failure.Anchor,
            CSharpDisplayFailureRow: CSharpDiffPrinter.ToDisplayFailureRow(failure)));
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
        LibraryBodyIndex index,
        IReadOnlyDictionary<int, MemberAnchor> anchors)
        => index.Methods
            .GroupBy(BodySignalMethodKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => SubjectFromMethod(group.Last(), anchors.GetValueOrDefault(group.Last().MetadataToken)), StringComparer.Ordinal);

    static ResearchSubjectKey ApiSubject(ApiSurface oldSurface, ApiSurface newSurface, string typeName, ApiChange change)
    {
        if (!IsMemberChange(change.Kind))
        {
            var typeAnchor = change.Subject?.TypeAnchor ?? new TypeAnchor(typeName);
            return new ResearchSubjectKey(
                ResearchDiffSubjectKind.Type,
                $"type:{typeAnchor.TypeFullName}",
                typeAnchor.TypeFullName,
                TypeName: typeAnchor.TypeFullName,
                TypeAnchor: typeAnchor);
        }

        var direction = Direction(change.Kind);
        var handle = direction == ResearchDiffDirection.Removed
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
            var value = direction == ResearchDiffDirection.Removed
                ? change.Subject?.OldIdentity ?? change.OldValue
                : change.Subject?.NewIdentity ?? change.NewValue;
            memberId = $"member:{typeName}::{value ?? memberName ?? change.Kind.ToString()}";
            display = $"{typeName}.{memberName ?? value ?? change.Kind.ToString()}";
        }

        return new ResearchSubjectKey(
            ResearchDiffSubjectKind.Member,
            memberId,
            display,
            typeName,
            memberName,
            handle?.Anchor is { } memberAnchor ? new TypeAnchor(memberAnchor.TypeFullName) : change.Subject?.TypeAnchor,
            handle?.Anchor);
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

    static ResearchDiffDirection Direction(ChangeKind kind)
        => kind switch
        {
            ChangeKind.TypeAdded or ChangeKind.MemberAdded or ChangeKind.InterfaceAdded
                or ChangeKind.TypeAttributeAdded or ChangeKind.MemberAttributeAdded => ResearchDiffDirection.Added,
            ChangeKind.TypeRemoved or ChangeKind.MemberRemoved or ChangeKind.InterfaceRemoved
                or ChangeKind.TypeAttributeRemoved or ChangeKind.MemberAttributeRemoved => ResearchDiffDirection.Removed,
            _ => ResearchDiffDirection.Changed,
        };

    static ResearchDiffChangeCategory ToResearchCategory(ApiChangeCategory category)
        => category switch
        {
            ApiChangeCategory.Attribute => ResearchDiffChangeCategory.Attribute,
            _ => ResearchDiffChangeCategory.Signature,
        };

    static ResearchSubjectKey SubjectFromMethod(MethodIdentity method, MemberAnchor? anchor = null)
    {
        var typeName = anchor?.TypeFullName ?? method.DeclaringType.ToQualifiedDisplayString();
        var methodName = method.Name == ".ctor" ? "#ctor" : method.Name;
        var memberName = anchor?.MemberName ?? methodName;
        var selectorName = ResearchMemberSelector.ForMetadataName(method.Name, method.IsExtension);
        var parameters = string.Join(",", method.ParameterTypes.Select(ApiTypeName));
        var displayParameters = string.Join(", ", method.ParameterTypes.Select(type => type.ToQualifiedDisplayString()));
        var methodGeneric = ApiMethodGenericList(method);
        var returnSuffix = "";
        var canonical = $"M:{typeName}.{methodName}{methodGeneric}({parameters}){returnSuffix}";
        var fingerprint = MemberAnchor.ComputeFingerprint(canonical);
        return new ResearchSubjectKey(
            ResearchDiffSubjectKind.Member,
            anchor?.StableSelector ?? $"{selectorName}~{fingerprint}",
            $"{typeName}.{methodName}({displayParameters})",
            typeName,
            memberName,
            new TypeAnchor(anchor?.TypeFullName ?? typeName),
            anchor,
            MetadataMemberRef(method));
    }

    static MetadataMemberRef MetadataMemberRef(MethodIdentity method)
        => new(method.AssemblyName, method.ModuleVersionId, method.MetadataToken);

    static string ApiTypeName(TypeRef type)
        => type.Kind switch
        {
            TypeRefKind.Definition => type.Namespace.Length == 0
                ? type.Name.Replace("+", ".", StringComparison.Ordinal)
                : $"{type.Namespace}.{type.Name.Replace("+", ".", StringComparison.Ordinal)}",
            TypeRefKind.GenericInstance => $"{ApiTypeName(type.ElementType!)}<{string.Join(",", type.TypeArguments.Select(ApiTypeName))}>",
            TypeRefKind.SzArray => $"{ApiTypeName(type.ElementType!)}[]",
            TypeRefKind.Array => $"{ApiTypeName(type.ElementType!)}[{(type.Rank == 1 ? "*" : new string(',', type.Rank - 1))}]",
            TypeRefKind.ByRef => $"{ApiTypeName(type.ElementType!)}&",
            TypeRefKind.Pointer => $"{ApiTypeName(type.ElementType!)}*",
            TypeRefKind.Pinned => $"pinned {ApiTypeName(type.ElementType!)}",
            TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter
                => type.GenericParameterName.Length == 0 ? $"!{type.GenericParameterIndex}" : type.GenericParameterName,
            _ => type.ToQualifiedDisplayString(),
        };

    static string ApiMethodGenericList(MethodIdentity method)
    {
        if (method.GenericArity == 0)
            return "";
        return $"<{string.Join(",", Enumerable.Range(0, method.GenericArity).Select(index =>
            index < method.GenericParameterNames.Length && method.GenericParameterNames[index].Length > 0
                ? method.GenericParameterNames[index]
                : $"!!{index}"))}>";
    }

    static bool IsConversionOperator(string methodName)
        => methodName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";

    static ResearchSubjectKey UnknownMemberSubject(string key)
        => new(ResearchDiffSubjectKind.Member, $"member:{key}", key);

    internal static class ResearchMemberSelector
    {
        public static string ForMetadataName(string methodName, bool isExtensionMethod = false)
            => methodName switch
            {
                ".ctor" => ".ctor",
                _ when isExtensionMethod => $"extension:{methodName}",
                _ when methodName.StartsWith("op_", StringComparison.Ordinal) => $"operator:{methodName}",
                _ when methodName.Contains('.') => $"explicit:{methodName}",
                _ => methodName,
            };
    }

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

    static bool MatchesTypeFilters(string typeFullName, IReadOnlySet<string>? filters)
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

    static string FormatDisplayLines(IEnumerable<IlDiffDisplayRow> rows)
        => string.Join("; ", rows.Select(row => row.UnifiedLine));

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

    sealed record ResearchAnalysisMethod(ResearchSubjectKey Subject, MethodSignals Signals, List<OptimizationOpportunity> Opportunities);

    sealed class ResultBuilder
    {
        readonly Dictionary<ResearchSubjectKey, List<ResearchDiffEvidence>> _rows = new(ResearchSubjectKeyIdentityComparer.Instance);

        public ApiDiff? ApiDiff { get; set; }

        public void Add(ResearchSubjectKey subject, ResearchDiffEvidence evidence)
        {
            if (!_rows.TryGetValue(subject, out var evidenceRows))
            {
                evidenceRows = [];
                _rows.Add(subject, evidenceRows);
            }
            else
            {
                var existingSubject = _rows.Keys.First(key => ResearchSubjectKeyIdentityComparer.Instance.Equals(key, subject));
                var mergedSubject = MergeSubject(existingSubject, subject, evidence.Direction);
                if (mergedSubject != existingSubject)
                {
                    _rows.Remove(existingSubject);
                    _rows.Add(mergedSubject, evidenceRows);
                }
            }
            evidenceRows.Add(evidence);
        }

        static ResearchSubjectKey MergeSubject(ResearchSubjectKey existing, ResearchSubjectKey candidate, ResearchDiffDirection candidateDirection)
        {
            var preferCandidate = candidateDirection != ResearchDiffDirection.Removed;
            return existing with
            {
                TypeName = existing.TypeName ?? candidate.TypeName,
                MemberName = existing.MemberName ?? candidate.MemberName,
                TypeAnchor = preferCandidate ? candidate.TypeAnchor ?? existing.TypeAnchor : existing.TypeAnchor ?? candidate.TypeAnchor,
                Anchor = preferCandidate ? candidate.Anchor ?? existing.Anchor : existing.Anchor ?? candidate.Anchor,
                MetadataMember = preferCandidate ? candidate.MetadataMember ?? existing.MetadataMember : existing.MetadataMember ?? candidate.MetadataMember,
                MetadataType = preferCandidate ? candidate.MetadataType ?? existing.MetadataType : existing.MetadataType ?? candidate.MetadataType,
            };
        }

        public ResearchDiffResult ToResult()
            => new([.. _rows
                .OrderBy(pair => pair.Key.Kind)
                .ThenBy(pair => pair.Key.Id, StringComparer.Ordinal)
                .Select(pair => new ResearchSubjectDiff(pair.Key, [.. pair.Value
                    .OrderBy(evidence => evidence.Mechanism)
                    .ThenBy(evidence => evidence.ChangeId, StringComparer.Ordinal)
                    .ThenBy(evidence => evidence.OldIlOffset)
                    .ThenBy(evidence => evidence.NewIlOffset)]))],
                ApiDiff,
                Rows: []);
    }

    sealed class ResearchSubjectKeyIdentityComparer : IEqualityComparer<ResearchSubjectKey>
    {
        public static ResearchSubjectKeyIdentityComparer Instance { get; } = new();

        public bool Equals(ResearchSubjectKey? x, ResearchSubjectKey? y)
            => ReferenceEquals(x, y)
               || (x is not null
                   && y is not null
                   && x.Kind == y.Kind
                   && string.Equals(x.Id, y.Id, StringComparison.Ordinal));

        public int GetHashCode(ResearchSubjectKey obj)
            => HashCode.Combine(obj.Kind, StringComparer.Ordinal.GetHashCode(obj.Id));
    }

    sealed class MemberAnchorCache
    {
        readonly Dictionary<string, IReadOnlyDictionary<int, MemberAnchor>> _anchors = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<int, MemberAnchor> Get(string path)
        {
            if (_anchors.TryGetValue(path, out var anchors))
                return anchors;

            anchors = Build(path);
            _anchors.Add(path, anchors);
            return anchors;
        }

        static IReadOnlyDictionary<int, MemberAnchor> Build(string path)
        {
            var surface = AssemblyReader.ExtractApiSurface(path, includeAll: true);
            if (surface is null)
                return new Dictionary<int, MemberAnchor>();

            var anchors = new Dictionary<int, MemberAnchor>();
            foreach (var type in surface.Types)
            {
                foreach (var member in type.Members)
                {
                    var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
                    if (member.MetadataToken is { } token)
                        anchors.TryAdd(token, anchor);
                    if (member.GetterToken is { } getter)
                        anchors.TryAdd(getter, anchor);
                    if (member.SetterToken is { } setter)
                        anchors.TryAdd(setter, anchor);
                }
            }

            return anchors;
        }
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

        public bool TryDecode(int metadataToken, out MethodInstructions? body, out string? unavailableReason)
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

            body = MethodInstructions.Decode(_peReader.GetMethodBody(method.RelativeVirtualAddress));
            return true;
        }

        public void Dispose()
        {
            _peReader.Dispose();
            _stream.Dispose();
        }
    }
}
