using ILInspector.CSharp;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Classification of an API change's impact on consumers.
/// </summary>
public enum ChangeClassification
{
    /// <summary>New API that doesn't affect existing consumers.</summary>
    Additive,
    /// <summary>Change that will break existing consumers.</summary>
    Breaking,
    /// <summary>Change that may break some consumers depending on usage.</summary>
    PotentiallyBreaking
}

/// <summary>
/// The specific kind of API change detected.
/// </summary>
public enum ChangeKind
{
    // Type-level
    TypeAdded,
    TypeRemoved,
    TypeKindChanged,
    SealedAdded,
    SealedRemoved,
    AbstractAdded,
    AbstractRemoved,
    BaseTypeChanged,
    InterfaceAdded,
    InterfaceRemoved,
    TypeParameterCountChanged,
    TypeParameterVarianceChanged,
    TypeParameterConstraintTightened,
    TypeParameterConstraintLoosened,

    // Member-level
    MemberAdded,
    MemberRemoved,
    MemberSignatureChanged,
    VirtualRemoved,
    AbstractMemberAdded,
    EnumValueChanged,

    // Attribute-level
    TypeAttributeAdded,
    TypeAttributeRemoved,
    MemberAttributeAdded,
    MemberAttributeRemoved,
}

/// <summary>
/// The API dimension that produced a change. Signature is the compatibility
/// surface; attributes are opt-in metadata evidence over otherwise matched APIs.
/// </summary>
public enum ApiChangeCategory
{
    Signature,
    Attribute,
}

public enum ApiChangeSubjectKind
{
    Type,
    Member,
}

public sealed record ApiTypeHandle(ApiType Type)
{
    public string TypeFullName => Type.FullName;
}

public sealed record ApiMemberHandle(
    ApiType Type,
    ApiMember Member,
    MemberAnchor? Anchor)
{
    public string TypeFullName => Type.FullName;
    public string MemberName => Member.Name;
    public string? CanonicalSignature => Anchor?.CanonicalSignature;
    public string? Fingerprint => Anchor?.Fingerprint;
    public string? StableSelector => Anchor?.StableSelector;
    public string Identity => StableSelector ?? CanonicalSignature ?? Member.Signature ?? $"{Member.Kind}:{Member.Name}";
}

public sealed record ApiChangeSubject(
    ApiChangeSubjectKind Kind,
    ApiTypeHandle? OldType = null,
    ApiTypeHandle? NewType = null,
    ApiMemberHandle? OldMember = null,
    ApiMemberHandle? NewMember = null)
{
    public string? TypeFullName => NewType?.TypeFullName ?? OldType?.TypeFullName ?? NewMember?.TypeFullName ?? OldMember?.TypeFullName;
    public string? MemberName => NewMember?.MemberName ?? OldMember?.MemberName;
    public string? OldIdentity => OldMember?.Identity;
    public string? NewIdentity => NewMember?.Identity;

    public static ApiChangeSubject Type(ApiType? oldType, ApiType? newType)
        => new(
            ApiChangeSubjectKind.Type,
            oldType is null ? null : new ApiTypeHandle(oldType),
            newType is null ? null : new ApiTypeHandle(newType));

    public static ApiChangeSubject Member(ApiType? oldType, ApiMember? oldMember, ApiType? newType, ApiMember? newMember)
        => new(
            ApiChangeSubjectKind.Member,
            oldType is null ? null : new ApiTypeHandle(oldType),
            newType is null ? null : new ApiTypeHandle(newType),
            oldType is null || oldMember is null ? null : ApiMemberIdentity.CreateHandle(oldType, oldMember),
            newType is null || newMember is null ? null : ApiMemberIdentity.CreateHandle(newType, newMember));
}

[Flags]
public enum ApiDiffScope
{
    Signature = 1,
    Attributes = 2,
    All = Signature | Attributes,
}

/// <summary>
/// A single detected API change.
/// </summary>
public record ApiChange(
    ChangeKind Kind,
    ChangeClassification Classification,
    string Message,
    string? OldValue = null,
    string? NewValue = null,
    ApiChangeCategory Category = ApiChangeCategory.Signature,
    ApiChangeSubject? Subject = null)
{
    /// <remarks>
    /// Every positional property is redeclared, in constructor order, even
    /// though only three need containment. A record that redeclares some of
    /// them emits the compiler-generated ones first, which silently reorders
    /// JSON keys, TSV columns, and the generated ToString. Redeclaring all of
    /// them is what keeps that order the constructor's.
    /// </remarks>
    public ChangeKind Kind { get; init; } = Kind;

    /// <inheritdoc cref="Kind"/>
    public ChangeClassification Classification { get; init; } = Classification;

    /// <summary>
    /// The human-readable description, which embeds untrusted type and member
    /// names. Containment happens here rather than at each of the eight sites
    /// that compose a message, so a ninth cannot reopen the hole. Identity lives
    /// in <see cref="Subject"/> and stays raw (issue #3319).
    /// </summary>
    public string Message { get; init; } = CSharpIdentifierCore.ContainComposedName(Message);

    /// <inheritdoc cref="Message"/>
    public string? OldValue { get; init; } = OldValue is null ? null : CSharpIdentifierCore.ContainComposedName(OldValue);

    /// <inheritdoc cref="Message"/>
    public string? NewValue { get; init; } = NewValue is null ? null : CSharpIdentifierCore.ContainComposedName(NewValue);

    /// <inheritdoc cref="Kind"/>
    public ApiChangeCategory Category { get; init; } = Category;

    /// <inheritdoc cref="Kind"/>
    public ApiChangeSubject? Subject { get; init; } = Subject;
}

/// <summary>
/// Named, explicitly-requested classification exceptions -- mirrors
/// <c>IlBodyDiffNormalization</c>'s policy shape. Default (<see cref="None"/>) is strict:
/// a fuzzy/soft member identity match (e.g. an extension method's static/instance
/// projection) is treated as unrelated (a breaking removal plus an additive addition),
/// the same as the legacy analyzer's own member matcher would report. Requesting
/// <see cref="NormalizeMemberProjection"/> instead treats a fuzzy-matched member pair as
/// the same member observed under two projections, classifying only its facet deltas.
/// </summary>
[Flags]
public enum ApiDiffNormalization
{
    None = 0,
    NormalizeMemberProjection = 1,
}

public sealed record ApiDiffOptions(
    ApiDiffScope Scope = ApiDiffScope.Signature,
    ApiDiffNormalization Normalization = ApiDiffNormalization.None)
{
    public static ApiDiffOptions Default { get; } = new();
}

/// <summary>
/// All changes detected for a single type.
/// </summary>
public record TypeDiff(string TypeFullName, IReadOnlyList<ApiChange> Changes)
{
    public int BreakingCount => CountByClassification(ChangeClassification.Breaking);
    public int AdditiveCount => CountByClassification(ChangeClassification.Additive);
    public int PotentiallyBreakingCount => CountByClassification(ChangeClassification.PotentiallyBreaking);

    public bool IsAdded => Changes.Count == 1 && Changes[0].Kind == ChangeKind.TypeAdded;
    public bool IsRemoved => Changes.Count == 1 && Changes[0].Kind == ChangeKind.TypeRemoved;

    private int CountByClassification(ChangeClassification classification)
    {
        int count = 0;
        foreach (var change in Changes)
        {
            if (change.Classification == classification)
                count++;
        }
        return count;
    }
}

/// <summary>
/// The complete result of comparing two API surfaces.
/// </summary>
public class ApiDiff
{
    public IReadOnlyList<TypeDiff> TypeDiffs { get; init; } = [];
    public IReadOnlyList<ApiDiffInspectionFailure> InspectionFailures { get; init; } = [];
    public int TotalBreaking { get; init; }
    public int TotalAdditive { get; init; }
    public int TotalPotentiallyBreaking { get; init; }
    public bool IsEmpty => TypeDiffs.Count == 0 && InspectionFailures.Count == 0;
}

public sealed record ApiDiffInspectionFailure(
    string Side,
    string Operation,
    int SubjectToken,
    MetadataTypeNameFailureMechanism Mechanism,
    string Kind,
    string Detail);

/// <summary>
/// Compares two <see cref="ApiSurface"/> instances and produces a structured diff
/// with change classification (breaking, additive, potentially breaking).
/// </summary>
public static class ApiDiffAnalyzer
{
    readonly record struct MemberKey(string Signature, SignatureDecodeStatus? DecodeStatus)
    {
        public override string ToString()
            => DecodeStatus is SignatureDecodeStatus.Degraded
                ? $"{Signature} [decode: degraded]"
                : Signature;
    }

    public static ApiDiff Compare(ApiSurface oldSurface, ApiSurface newSurface)
        => Compare(oldSurface, newSurface, ApiDiffOptions.Default);

    public static ApiDiff Compare(ApiSurface oldSurface, ApiSurface newSurface, ApiDiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var oldTypes = BuildTypeLookup(oldSurface);
        var newTypes = BuildTypeLookup(newSurface);
        bool oldIdentityIncomplete = oldSurface.InspectionFailures.Count > 0;
        bool newIdentityIncomplete = newSurface.InspectionFailures.Count > 0;

        var allTypeNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in oldTypes.Keys) allTypeNames.Add(key);
        foreach (var key in newTypes.Keys) allTypeNames.Add(key);

        List<TypeDiff> typeDiffs = [];
        int totalBreaking = 0;
        int totalAdditive = 0;
        int totalPotentiallyBreaking = 0;

        foreach (var typeName in allTypeNames)
        {
            bool inOld = oldTypes.TryGetValue(typeName, out var oldType);
            bool inNew = newTypes.TryGetValue(typeName, out var newType);

            List<ApiChange> changes;

            if (inOld && !inNew)
            {
                if (newIdentityIncomplete)
                    continue;
                changes = IncludesSignature(options)
                    ? [new ApiChange(ChangeKind.TypeRemoved, ChangeClassification.Breaking,
                        $"Type '{typeName}' was removed",
                        Subject: ApiChangeSubject.Type(oldType, null))]
                    : [];
            }
            else if (!inOld && inNew)
            {
                if (oldIdentityIncomplete)
                    continue;
                changes = IncludesSignature(options)
                    ? [new ApiChange(ChangeKind.TypeAdded, ChangeClassification.Additive,
                        $"Type '{typeName}' was added",
                        Subject: ApiChangeSubject.Type(null, newType))]
                    : [];
            }
            else
            {
                changes = CompareTypes(oldType!, newType!, options);
            }

            if (changes.Count > 0)
            {
                var diff = new TypeDiff(typeName, changes);
                typeDiffs.Add(diff);
                totalBreaking += diff.BreakingCount;
                totalAdditive += diff.AdditiveCount;
                totalPotentiallyBreaking += diff.PotentiallyBreakingCount;
            }
        }

        return new ApiDiff
        {
            TypeDiffs = typeDiffs,
            InspectionFailures =
            [
                .. oldSurface.InspectionFailures.Select(failure =>
                    new ApiDiffInspectionFailure(
                        "old",
                        failure.Operation,
                        failure.SubjectToken,
                        failure.Mechanism,
                        failure.Kind,
                        failure.Detail)),
                .. newSurface.InspectionFailures.Select(failure =>
                    new ApiDiffInspectionFailure(
                        "new",
                        failure.Operation,
                        failure.SubjectToken,
                        failure.Mechanism,
                        failure.Kind,
                        failure.Detail)),
            ],
            TotalBreaking = totalBreaking,
            TotalAdditive = totalAdditive,
            TotalPotentiallyBreaking = totalPotentiallyBreaking
        };
    }

    private static Dictionary<string, ApiType> BuildTypeLookup(ApiSurface surface)
    {
        var lookup = new Dictionary<string, ApiType>(StringComparer.Ordinal);
        foreach (var type in surface.Types)
        {
            lookup[type.FullName] = type;
        }
        return lookup;
    }

    private static List<ApiChange> CompareTypes(ApiType oldType, ApiType newType, ApiDiffOptions options)
    {
        var changes = CompareTypeFacetsOnly(oldType, newType, options);
        CompareMembers(oldType, newType, changes, options);
        return changes;
    }

    /// <summary>
    /// Type-facet classification only (kind/sealed/abstract/base type/interfaces/type
    /// parameters/type attributes) -- no member-level walk. Reused by
    /// <see cref="ApiFindingClassifier"/>, which sources member-level changes from the
    /// Finding lane's own member comparison instead of this analyzer's member matcher.
    /// </summary>
    internal static List<ApiChange> CompareTypeFacetsOnly(ApiType oldType, ApiType newType, ApiDiffOptions options)
    {
        List<ApiChange> changes = [];
        if (IncludesSignature(options))
        {
            CompareTypeMetadata(oldType, newType, changes);
            CompareTypeParameters(oldType, newType, changes);
        }
        if (IncludesAttributes(options))
            CompareAttributes(
                oldType.Attributes,
                newType.Attributes,
                changes,
                ChangeKind.TypeAttributeAdded,
                ChangeKind.TypeAttributeRemoved,
                $"Type '{oldType.FullName}'",
                ApiChangeSubject.Type(oldType, newType));
        return changes;
    }

    private static void CompareTypeMetadata(ApiType oldType, ApiType newType, List<ApiChange> changes)
    {
        // Kind changed
        if (oldType.Kind != newType.Kind)
        {
            changes.Add(new ApiChange(ChangeKind.TypeKindChanged, ChangeClassification.Breaking,
                $"Type kind changed from '{oldType.Kind}' to '{newType.Kind}'",
                oldType.Kind, newType.Kind,
                Subject: ApiChangeSubject.Type(oldType, newType)));
        }

        // Sealed changes — skip for static types (IsSealed && IsAbstract both true)
        bool oldIsStatic = oldType.IsSealed && oldType.IsAbstract;
        bool newIsStatic = newType.IsSealed && newType.IsAbstract;

        if (!oldIsStatic && !newIsStatic)
        {
            if (!oldType.IsSealed && newType.IsSealed)
            {
                changes.Add(new ApiChange(ChangeKind.SealedAdded, ChangeClassification.Breaking,
                    "Type was sealed",
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }
            else if (oldType.IsSealed && !newType.IsSealed)
            {
                changes.Add(new ApiChange(ChangeKind.SealedRemoved, ChangeClassification.Additive,
                    "Type was unsealed",
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }

            if (!oldType.IsAbstract && newType.IsAbstract)
            {
                changes.Add(new ApiChange(ChangeKind.AbstractAdded, ChangeClassification.Breaking,
                    "Type was made abstract",
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }
            else if (oldType.IsAbstract && !newType.IsAbstract)
            {
                changes.Add(new ApiChange(ChangeKind.AbstractRemoved, ChangeClassification.Additive,
                    "Type was made non-abstract",
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }
        }

        // Base type changed
        if (oldType.BaseType != newType.BaseType)
        {
            changes.Add(new ApiChange(ChangeKind.BaseTypeChanged, ChangeClassification.Breaking,
                $"Base type changed from '{oldType.BaseType ?? "none"}' to '{newType.BaseType ?? "none"}'",
                oldType.BaseType, newType.BaseType,
                Subject: ApiChangeSubject.Type(oldType, newType)));
        }

        // Interfaces
        var oldInterfaces = new HashSet<string>(oldType.Interfaces ?? [], StringComparer.Ordinal);
        var newInterfaces = new HashSet<string>(newType.Interfaces ?? [], StringComparer.Ordinal);

        foreach (var removed in oldInterfaces.Except(newInterfaces).Order())
        {
            changes.Add(new ApiChange(ChangeKind.InterfaceRemoved, ChangeClassification.Breaking,
                $"Interface '{removed}' was removed",
                OldValue: removed,
                Subject: ApiChangeSubject.Type(oldType, newType)));
        }

        foreach (var added in newInterfaces.Except(oldInterfaces).Order())
        {
            // Adding interface to an interface is potentially breaking (implementers must add it)
            var classification = newType.Kind == "interface"
                ? ChangeClassification.PotentiallyBreaking
                : ChangeClassification.Additive;

            changes.Add(new ApiChange(ChangeKind.InterfaceAdded, classification,
                $"Interface '{added}' was added",
                NewValue: added,
                Subject: ApiChangeSubject.Type(oldType, newType)));
        }
    }

    private static void CompareTypeParameters(ApiType oldType, ApiType newType, List<ApiChange> changes)
    {
        var oldParams = oldType.TypeParameters ?? [];
        var newParams = newType.TypeParameters ?? [];

        if (oldParams.Count != newParams.Count)
        {
            changes.Add(new ApiChange(ChangeKind.TypeParameterCountChanged, ChangeClassification.Breaking,
                $"Generic parameter count changed from {oldParams.Count} to {newParams.Count}",
                oldParams.Count.ToString(), newParams.Count.ToString(),
                Subject: ApiChangeSubject.Type(oldType, newType)));
            return; // No point comparing individual params when count changed
        }

        for (int i = 0; i < oldParams.Count; i++)
        {
            var oldParam = oldParams[i];
            var newParam = newParams[i];

            // Variance changed
            if (oldParam.Variance != newParam.Variance)
            {
                changes.Add(new ApiChange(ChangeKind.TypeParameterVarianceChanged, ChangeClassification.Breaking,
                    $"Generic parameter '{newParam.Name}' variance changed from '{oldParam.Variance ?? "none"}' to '{newParam.Variance ?? "none"}'",
                    oldParam.Variance, newParam.Variance,
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }

            // Constraints changed
            var oldConstraints = new HashSet<string>(oldParam.Constraints, StringComparer.Ordinal);
            var newConstraints = new HashSet<string>(newParam.Constraints, StringComparer.Ordinal);

            var added = newConstraints.Except(oldConstraints).ToList();
            var removed = oldConstraints.Except(newConstraints).ToList();

            if (added.Count > 0)
            {
                changes.Add(new ApiChange(ChangeKind.TypeParameterConstraintTightened, ChangeClassification.Breaking,
                    $"Generic parameter '{newParam.Name}' added constraints: {string.Join(", ", added)}",
                    NewValue: string.Join(", ", added),
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }

            if (removed.Count > 0)
            {
                changes.Add(new ApiChange(ChangeKind.TypeParameterConstraintLoosened, ChangeClassification.Additive,
                    $"Generic parameter '{newParam.Name}' removed constraints: {string.Join(", ", removed)}",
                    OldValue: string.Join(", ", removed),
                    Subject: ApiChangeSubject.Type(oldType, newType)));
            }
        }
    }

    private static void CompareMembers(ApiType oldType, ApiType newType, List<ApiChange> changes, ApiDiffOptions options)
    {
        var oldMembers = FilterMembers(oldType.Members);
        var newMembers = FilterMembers(newType.Members);
        bool includeDecodeStatus = IncludesSignature(options);

        // Build signature sets for matching
        var oldBySignature = new Dictionary<MemberKey, ApiMember>();
        foreach (var m in oldMembers)
        {
            var key = GetMemberKey(m, includeDecodeStatus);
            oldBySignature.TryAdd(key, m);
        }

        var newBySignature = new Dictionary<MemberKey, ApiMember>();
        foreach (var m in newMembers)
        {
            var key = GetMemberKey(m, includeDecodeStatus);
            newBySignature.TryAdd(key, m);
        }

        // Find matched (by full signature key), unmatched old, unmatched new
        var matchedOldKeys = new HashSet<MemberKey>();
        var matchedNewKeys = new HashSet<MemberKey>();

        foreach (var key in oldBySignature.Keys)
        {
            if (newBySignature.ContainsKey(key))
            {
                matchedOldKeys.Add(key);
                matchedNewKeys.Add(key);
            }
        }

        List<ApiMember> unmatchedOld = [];
        foreach (var kvp in oldBySignature)
        {
            if (!matchedOldKeys.Contains(kvp.Key))
                unmatchedOld.Add(kvp.Value);
        }

        List<ApiMember> unmatchedNew = [];
        foreach (var kvp in newBySignature)
        {
            if (!matchedNewKeys.Contains(kvp.Key))
                unmatchedNew.Add(kvp.Value);
        }

        if (IncludesSignature(options))
        {
            // Pair unmatched by (Name, Kind) for signature changes
            var pairedOld = new HashSet<ApiMember>(ReferenceEqualityComparer.Instance);
            var pairedNew = new HashSet<ApiMember>(ReferenceEqualityComparer.Instance);

            foreach (var oldMember in unmatchedOld)
            {
                foreach (var newMember in unmatchedNew)
                {
                    if (pairedNew.Contains(newMember))
                        continue;

                    if (oldMember.Name == newMember.Name && oldMember.Kind == newMember.Kind)
                    {
                        pairedOld.Add(oldMember);
                        pairedNew.Add(newMember);

                        changes.Add(new ApiChange(ChangeKind.MemberSignatureChanged, ChangeClassification.Breaking,
                            $"Member '{oldMember.Name}' signature changed",
                            GetMemberKey(oldMember).ToString(), GetMemberKey(newMember).ToString(),
                            Subject: ApiChangeSubject.Member(oldType, oldMember, newType, newMember)));
                        break;
                    }
                }
            }

            // Remaining unmatched → removed / added
            foreach (var oldMember in unmatchedOld)
            {
                if (!pairedOld.Contains(oldMember))
                {
                    changes.Add(new ApiChange(ChangeKind.MemberRemoved, ChangeClassification.Breaking,
                        $"Member '{oldMember.Name}' was removed",
                        OldValue: GetMemberKey(oldMember).ToString(),
                        Subject: ApiChangeSubject.Member(oldType, oldMember, null, null)));
                }
            }

            foreach (var newMember in unmatchedNew)
            {
                if (!pairedNew.Contains(newMember))
                {
                    // Adding member to interface is potentially breaking
                    var classification = newType.Kind == "interface"
                        ? ChangeClassification.PotentiallyBreaking
                        : ChangeClassification.Additive;

                    changes.Add(new ApiChange(ChangeKind.MemberAdded, classification,
                        $"Member '{newMember.Name}' was added",
                        NewValue: GetMemberKey(newMember).ToString(),
                        Subject: ApiChangeSubject.Member(null, null, newType, newMember)));
                }
            }

            // For members matched by full signature, check modifier changes
            foreach (var key in matchedOldKeys)
            {
                var oldMember = oldBySignature[key];
                var newMember = newBySignature[key];
                CompareMemberModifiers(oldType, newType, oldMember, newMember, changes);
            }
        }

        if (IncludesAttributes(options))
        {
            foreach (var key in matchedOldKeys)
            {
                var oldMember = oldBySignature[key];
                var newMember = newBySignature[key];
                CompareAttributes(
                    oldMember.Attributes,
                    newMember.Attributes,
                    changes,
                    ChangeKind.MemberAttributeAdded,
                    ChangeKind.MemberAttributeRemoved,
                    $"Member '{oldMember.Name}'",
                    ApiChangeSubject.Member(oldType, oldMember, newType, newMember));
            }
        }
    }

    internal static void CompareMemberModifiers(ApiType oldType, ApiType newType, ApiMember oldMember, ApiMember newMember, List<ApiChange> changes)
    {
        // Virtual removed
        if (oldMember.IsVirtual && !newMember.IsVirtual)
        {
            changes.Add(new ApiChange(ChangeKind.VirtualRemoved, ChangeClassification.Breaking,
                $"Member '{oldMember.Name}' is no longer virtual",
                GetMemberKey(oldMember).ToString(),
                GetMemberKey(newMember).ToString(),
                Subject: ApiChangeSubject.Member(oldType, oldMember, newType, newMember)));
        }

        // Abstract added
        if (!oldMember.IsAbstract && newMember.IsAbstract)
        {
            changes.Add(new ApiChange(ChangeKind.AbstractMemberAdded, ChangeClassification.Breaking,
                $"Member '{oldMember.Name}' was made abstract",
                GetMemberKey(oldMember).ToString(),
                GetMemberKey(newMember).ToString(),
                Subject: ApiChangeSubject.Member(oldType, oldMember, newType, newMember)));
        }

        // Enum value changed
        if (oldMember.EnumValue != null && newMember.EnumValue != null &&
            oldMember.EnumValue != newMember.EnumValue)
        {
            changes.Add(new ApiChange(ChangeKind.EnumValueChanged, ChangeClassification.PotentiallyBreaking,
                $"Enum member '{oldMember.Name}' value changed from {oldMember.EnumValue} to {newMember.EnumValue}",
                oldMember.EnumValue.Value.ToString(), newMember.EnumValue.Value.ToString(),
                Subject: ApiChangeSubject.Member(oldType, oldMember, newType, newMember)));
        }
    }

    private static List<ApiMember> FilterMembers(List<ApiMember>? members)
    {
        if (members == null || members.Count == 0)
            return [];

        var filtered = new List<ApiMember>(members.Count);
        foreach (var member in members)
        {
            if (!MemberFilters.IsCompilerGenerated(member.Name))
                filtered.Add(member);
        }
        return filtered;
    }

    private static MemberKey GetMemberKey(ApiMember member, bool includeDecodeStatus = true)
    {
        // Use signature when available (methods, properties), else fall back to kind + name
        return new MemberKey(
            member.Signature ?? $"{member.Kind}:{member.Name}",
            includeDecodeStatus ? member.SignatureDecodeStatus : null);
    }

    internal static void CompareAttributes(
        IEnumerable<string> oldAttributes,
        IEnumerable<string> newAttributes,
        List<ApiChange> changes,
        ChangeKind addedKind,
        ChangeKind removedKind,
        string subject,
        ApiChangeSubject structuredSubject)
    {
        var oldSet = new HashSet<string>(oldAttributes, StringComparer.Ordinal);
        var newSet = new HashSet<string>(newAttributes, StringComparer.Ordinal);

        foreach (var removed in oldSet.Except(newSet).Order(StringComparer.Ordinal))
        {
            changes.Add(new ApiChange(
                removedKind,
                ChangeClassification.PotentiallyBreaking,
                $"{subject} attribute '{removed}' was removed",
                OldValue: removed,
                Category: ApiChangeCategory.Attribute,
                Subject: structuredSubject));
        }

        foreach (var added in newSet.Except(oldSet).Order(StringComparer.Ordinal))
        {
            changes.Add(new ApiChange(
                addedKind,
                ChangeClassification.PotentiallyBreaking,
                $"{subject} attribute '{added}' was added",
                NewValue: added,
                Category: ApiChangeCategory.Attribute,
                Subject: structuredSubject));
        }
    }

    internal static bool IncludesSignature(ApiDiffOptions options)
        => options.Scope.HasFlag(ApiDiffScope.Signature);

    internal static bool IncludesAttributes(ApiDiffOptions options)
        => options.Scope.HasFlag(ApiDiffScope.Attributes);
}
