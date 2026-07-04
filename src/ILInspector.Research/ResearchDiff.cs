using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Research;

[Flags]
public enum ResearchDiffMechanism
{
    None = 0,
    Api = 1,
    BodySignals = 2,
    IlBody = 4,
    CSharp = 8,
    AllAvailable = Api | BodySignals | IlBody,
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
}

public sealed record ResearchDiffOptions(
    ResearchDiffMechanism Mechanisms = ResearchDiffMechanism.AllAvailable,
    bool IncludeAllApi = false,
    ApiDiffScope ApiScope = ApiDiffScope.Signature);

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
    string? MemberName = null);

public sealed record ResearchDiffEvidence(
    ResearchDiffMechanism Mechanism,
    string ChangeId,
    ResearchDiffDirection Direction,
    string? OldValue = null,
    string? NewValue = null,
    int? OldIlOffset = null,
    int? NewIlOffset = null,
    string? Detail = null,
    ResearchDiffChangeCategory Category = ResearchDiffChangeCategory.Unknown);

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

public sealed record ResearchDiffResult(IReadOnlyList<ResearchSubjectDiff> Subjects)
{
    public IReadOnlyList<ResearchSubjectDiff> MembersWhere(Func<ResearchSubjectDiff, bool> predicate)
        => [.. Subjects.Where(subject => subject.Subject.Kind == ResearchDiffSubjectKind.Member && predicate(subject))];
}

public static class ResearchDiff
{
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

        if (options.Mechanisms.HasFlag(ResearchDiffMechanism.Api))
            AddApiDiff(builder, oldInput, newInput, options.IncludeAllApi, options.ApiScope);

        if (options.Mechanisms.HasFlag(ResearchDiffMechanism.BodySignals))
            AddBodySignalDiff(builder, oldInput, newInput);

        if (options.Mechanisms.HasFlag(ResearchDiffMechanism.IlBody))
            AddIlBodyDiff(builder, oldInput, newInput);

        return builder.ToResult();
    }

    static void AddApiDiff(ResultBuilder builder, ResearchDiffInput oldInput, ResearchDiffInput newInput, bool includeAll, ApiDiffScope apiScope)
    {
        var oldSurface = ResolveApiSurface(oldInput, includeAll);
        var newSurface = ResolveApiSurface(newInput, includeAll);
        if (oldSurface is null || newSurface is null)
            return;

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, new ApiDiffOptions(apiScope));
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

    static void AddBodySignalDiff(ResultBuilder builder, ResearchDiffInput oldInput, ResearchDiffInput newInput)
    {
        foreach (var (oldIndex, newIndex) in PairedBodyIndexes(oldInput, newInput))
        {
            var methods = MethodSubjectsByBodySignalKey(oldIndex, newIndex);
            foreach (var row in BodySignalDiff.CompareUnsafe(oldIndex, newIndex).Rows)
            {
                var subject = methods.GetValueOrDefault(row.Member) ?? UnknownMemberSubject(row.Member);
                var direction = row.Kind == BodySignalDiffKind.Added ? ResearchDiffDirection.Added : ResearchDiffDirection.Removed;
                var suffix = direction == ResearchDiffDirection.Added ? "added" : "removed";
                builder.Add(subject, new ResearchDiffEvidence(
                    ResearchDiffMechanism.BodySignals,
                    $"unsafe.{NormalizeChangePart(row.Signal)}.{suffix}",
                    direction,
                    OldIlOffset: direction == ResearchDiffDirection.Removed ? row.ILOffset : null,
                    NewIlOffset: direction == ResearchDiffDirection.Added ? row.ILOffset : null,
                    Detail: $"{row.Operation}: {row.Evidence}",
                    Category: ResearchDiffChangeCategory.BodySignal));
            }
        }
    }

    static void AddIlBodyDiff(ResultBuilder builder, ResearchDiffInput oldInput, ResearchDiffInput newInput)
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
                var oldAvailable = oldBodies.TryDecode(oldMethod.MetadataToken, out var oldBody, out var oldReason);
                var newAvailable = newBodies.TryDecode(newMethod.MetadataToken, out var newBody, out var newReason);

                if (!oldAvailable || !newAvailable)
                {
                    if (!oldAvailable && !newAvailable)
                        continue;
                    builder.Add(subject, new ResearchDiffEvidence(
                        ResearchDiffMechanism.IlBody,
                        !oldAvailable ? "il.body.added" : "il.body.removed",
                        !oldAvailable ? ResearchDiffDirection.Added : ResearchDiffDirection.Removed,
                        OldValue: oldReason,
                        NewValue: newReason,
                        Category: ResearchDiffChangeCategory.IlBody));
                    continue;
                }

                var diff = IlBodyDiff.Compare(oldBody!, newBody!);
                if (diff.IsExact)
                    continue;
                if (!string.IsNullOrEmpty(diff.Failure))
                {
                    builder.Add(subject, new ResearchDiffEvidence(
                        ResearchDiffMechanism.IlBody,
                        "il.body.decode-failed",
                        ResearchDiffDirection.Changed,
                        Detail: diff.Failure,
                        Category: ResearchDiffChangeCategory.IlBody));
                    continue;
                }

                foreach (var hunk in diff.Rows.GroupBy(row => row.HunkId).OrderBy(group => group.Key))
                {
                    var removed = hunk.Where(row => row.Kind == IlDiffKind.Remove).ToArray();
                    var added = hunk.Where(row => row.Kind == IlDiffKind.Add).ToArray();
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
                        OldValue: FormatOperations(removed),
                        NewValue: FormatOperations(added),
                        OldIlOffset: removed.Select(row => (int?)row.Operation.Offset).FirstOrDefault(offset => offset is not null),
                        NewIlOffset: added.Select(row => (int?)row.Operation.Offset).FirstOrDefault(offset => offset is not null),
                        Category: ResearchDiffChangeCategory.IlBody));
                }
            }
        }
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

    static Dictionary<string, ResearchSubjectKey> MethodSubjectsByBodySignalKey(LibraryBodyIndex oldIndex, LibraryBodyIndex newIndex)
        => oldIndex.Methods.Concat(newIndex.Methods)
            .GroupBy(BodySignalMethodKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => SubjectFromMethod(group.Last()), StringComparer.Ordinal);

    static ResearchSubjectKey ApiSubject(ApiSurface oldSurface, ApiSurface newSurface, string typeName, ApiChange change)
    {
        if (!IsMemberChange(change.Kind))
            return new ResearchSubjectKey(ResearchDiffSubjectKind.Type, $"type:{typeName}", typeName, TypeName: typeName);

        var direction = Direction(change.Kind);
        var memberName = ExtractQuotedName(change.Message);
        var type = direction == ResearchDiffDirection.Removed
            ? FindType(oldSurface, typeName)
            : FindType(newSurface, typeName) ?? FindType(oldSurface, typeName);
        var member = type is null || memberName is null
            ? null
            : FindMember(type, memberName, direction == ResearchDiffDirection.Removed ? change.OldValue : change.NewValue);

        string memberId;
        string display;
        if (type is not null && member is not null)
        {
            memberId = ApiMemberId(type, member);
            display = ApiMemberDisplay(type, member);
        }
        else
        {
            var value = direction == ResearchDiffDirection.Removed ? change.OldValue : change.NewValue;
            memberId = $"member:{typeName}::{value ?? memberName ?? change.Kind.ToString()}";
            display = $"{typeName}.{memberName ?? value ?? change.Kind.ToString()}";
        }

        return new ResearchSubjectKey(ResearchDiffSubjectKind.Member, memberId, display, typeName, memberName);
    }

    static ApiType? FindType(ApiSurface surface, string typeName)
        => surface.Types.FirstOrDefault(type => type.FullName == typeName);

    static ApiMember? FindMember(ApiType type, string memberName, string? signature)
    {
        var candidates = type.Members.Where(candidate => candidate.Name == memberName).ToArray();
        if (!string.IsNullOrWhiteSpace(signature))
        {
            var exact = candidates.FirstOrDefault(candidate => candidate.Signature == signature);
            if (exact is not null)
                return exact;
        }
        return candidates.Length == 1 ? candidates[0] : null;
    }

    static string ApiMemberId(ApiType type, ApiMember member)
    {
        if (ApiMemberIdentity.TryGetCanonicalSignature(type, member, out var canonical))
            return $"member:{canonical}";
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

    static ResearchSubjectKey SubjectFromMethod(MethodIdentity method)
    {
        var typeName = method.DeclaringType.ToQualifiedDisplayString();
        var memberName = method.Name == ".ctor" ? "#ctor" : method.Name;
        var parameters = string.Join(",", method.ParameterTypes.Select(type => type.ToQualifiedDisplayString()));
        var displayParameters = string.Join(", ", method.ParameterTypes.Select(type => type.ToQualifiedDisplayString()));
        return new ResearchSubjectKey(
            ResearchDiffSubjectKind.Member,
            $"member:M:{typeName}.{memberName}({parameters})",
            $"{typeName}.{memberName}({displayParameters})",
            typeName,
            memberName);
    }

    static ResearchSubjectKey UnknownMemberSubject(string key)
        => new(ResearchDiffSubjectKind.Member, $"member:{key}", key);

    static string BodySignalMethodKey(MethodIdentity method)
        => $"{method.AssemblyName}|{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    static string MethodMatchKey(MethodIdentity method)
        => $"{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|{method.Name}|{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    static Dictionary<string, MethodIdentity> MethodLookup(LibraryBodyIndex index)
    {
        var methods = new Dictionary<string, MethodIdentity>(StringComparer.Ordinal);
        foreach (var method in index.Methods)
            methods.TryAdd(MethodMatchKey(method), method);
        return methods;
    }

    static string AssemblyKey(LibraryBodyIndex index)
        => index.Methods.Select(method => method.AssemblyName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? Path.GetFileNameWithoutExtension(index.Path);

    static string? ExtractQuotedName(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0)
            return null;
        var end = message.IndexOf('\'', start + 1);
        return end > start ? message[(start + 1)..end] : null;
    }

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

    sealed record BodyIndexEntry(string Key, string Path, LibraryBodyIndex Index);

    sealed class ResultBuilder
    {
        readonly Dictionary<ResearchSubjectKey, List<ResearchDiffEvidence>> _rows = [];

        public void Add(ResearchSubjectKey subject, ResearchDiffEvidence evidence)
        {
            if (!_rows.TryGetValue(subject, out var evidenceRows))
            {
                evidenceRows = [];
                _rows.Add(subject, evidenceRows);
            }
            evidenceRows.Add(evidence);
        }

        public ResearchDiffResult ToResult()
            => new([.. _rows
                .OrderBy(pair => pair.Key.Kind)
                .ThenBy(pair => pair.Key.Id, StringComparer.Ordinal)
                .Select(pair => new ResearchSubjectDiff(pair.Key, [.. pair.Value
                    .OrderBy(evidence => evidence.Mechanism)
                    .ThenBy(evidence => evidence.ChangeId, StringComparer.Ordinal)
                    .ThenBy(evidence => evidence.OldIlOffset)
                    .ThenBy(evidence => evidence.NewIlOffset)]))]);
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
