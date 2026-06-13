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
    EnumValueChanged
}

/// <summary>
/// A single detected API change.
/// </summary>
public record ApiChange(
    ChangeKind Kind,
    ChangeClassification Classification,
    string Message,
    string? OldValue = null,
    string? NewValue = null);

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
    public int TotalBreaking { get; init; }
    public int TotalAdditive { get; init; }
    public int TotalPotentiallyBreaking { get; init; }
    public bool IsEmpty => TypeDiffs.Count == 0;
}

/// <summary>
/// Compares two <see cref="ApiSurface"/> instances and produces a structured diff
/// with change classification (breaking, additive, potentially breaking).
/// </summary>
public static class ApiDiffAnalyzer
{
    public static ApiDiff Compare(ApiSurface oldSurface, ApiSurface newSurface)
    {
        var oldTypes = BuildTypeLookup(oldSurface);
        var newTypes = BuildTypeLookup(newSurface);

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
                changes = [new ApiChange(ChangeKind.TypeRemoved, ChangeClassification.Breaking,
                    $"Type '{typeName}' was removed")];
            }
            else if (!inOld && inNew)
            {
                changes = [new ApiChange(ChangeKind.TypeAdded, ChangeClassification.Additive,
                    $"Type '{typeName}' was added")];
            }
            else
            {
                changes = CompareTypes(oldType!, newType!);
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

    private static List<ApiChange> CompareTypes(ApiType oldType, ApiType newType)
    {
        List<ApiChange> changes = [];
        CompareTypeMetadata(oldType, newType, changes);
        CompareTypeParameters(oldType, newType, changes);
        CompareMembers(oldType, newType, changes);
        return changes;
    }

    private static void CompareTypeMetadata(ApiType oldType, ApiType newType, List<ApiChange> changes)
    {
        // Kind changed
        if (oldType.Kind != newType.Kind)
        {
            changes.Add(new ApiChange(ChangeKind.TypeKindChanged, ChangeClassification.Breaking,
                $"Type kind changed from '{oldType.Kind}' to '{newType.Kind}'",
                oldType.Kind, newType.Kind));
        }

        // Sealed changes — skip for static types (IsSealed && IsAbstract both true)
        bool oldIsStatic = oldType.IsSealed && oldType.IsAbstract;
        bool newIsStatic = newType.IsSealed && newType.IsAbstract;

        if (!oldIsStatic && !newIsStatic)
        {
            if (!oldType.IsSealed && newType.IsSealed)
            {
                changes.Add(new ApiChange(ChangeKind.SealedAdded, ChangeClassification.Breaking,
                    "Type was sealed"));
            }
            else if (oldType.IsSealed && !newType.IsSealed)
            {
                changes.Add(new ApiChange(ChangeKind.SealedRemoved, ChangeClassification.Additive,
                    "Type was unsealed"));
            }

            if (!oldType.IsAbstract && newType.IsAbstract)
            {
                changes.Add(new ApiChange(ChangeKind.AbstractAdded, ChangeClassification.Breaking,
                    "Type was made abstract"));
            }
            else if (oldType.IsAbstract && !newType.IsAbstract)
            {
                changes.Add(new ApiChange(ChangeKind.AbstractRemoved, ChangeClassification.Additive,
                    "Type was made non-abstract"));
            }
        }

        // Base type changed
        if (oldType.BaseType != newType.BaseType)
        {
            changes.Add(new ApiChange(ChangeKind.BaseTypeChanged, ChangeClassification.Breaking,
                $"Base type changed from '{oldType.BaseType ?? "none"}' to '{newType.BaseType ?? "none"}'",
                oldType.BaseType, newType.BaseType));
        }

        // Interfaces
        var oldInterfaces = new HashSet<string>(oldType.Interfaces ?? [], StringComparer.Ordinal);
        var newInterfaces = new HashSet<string>(newType.Interfaces ?? [], StringComparer.Ordinal);

        foreach (var removed in oldInterfaces.Except(newInterfaces).Order())
        {
            changes.Add(new ApiChange(ChangeKind.InterfaceRemoved, ChangeClassification.Breaking,
                $"Interface '{removed}' was removed"));
        }

        foreach (var added in newInterfaces.Except(oldInterfaces).Order())
        {
            // Adding interface to an interface is potentially breaking (implementers must add it)
            var classification = newType.Kind == "interface"
                ? ChangeClassification.PotentiallyBreaking
                : ChangeClassification.Additive;

            changes.Add(new ApiChange(ChangeKind.InterfaceAdded, classification,
                $"Interface '{added}' was added"));
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
                oldParams.Count.ToString(), newParams.Count.ToString()));
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
                    oldParam.Variance, newParam.Variance));
            }

            // Constraints changed
            var oldConstraints = new HashSet<string>(oldParam.Constraints, StringComparer.Ordinal);
            var newConstraints = new HashSet<string>(newParam.Constraints, StringComparer.Ordinal);

            var added = newConstraints.Except(oldConstraints).ToList();
            var removed = oldConstraints.Except(newConstraints).ToList();

            if (added.Count > 0)
            {
                changes.Add(new ApiChange(ChangeKind.TypeParameterConstraintTightened, ChangeClassification.Breaking,
                    $"Generic parameter '{newParam.Name}' added constraints: {string.Join(", ", added)}"));
            }

            if (removed.Count > 0)
            {
                changes.Add(new ApiChange(ChangeKind.TypeParameterConstraintLoosened, ChangeClassification.Additive,
                    $"Generic parameter '{newParam.Name}' removed constraints: {string.Join(", ", removed)}"));
            }
        }
    }

    private static void CompareMembers(ApiType oldType, ApiType newType, List<ApiChange> changes)
    {
        var oldMembers = FilterMembers(oldType.Members);
        var newMembers = FilterMembers(newType.Members);

        // Build signature sets for matching
        var oldBySignature = new Dictionary<string, ApiMember>(StringComparer.Ordinal);
        foreach (var m in oldMembers)
        {
            var key = GetMemberKey(m);
            oldBySignature.TryAdd(key, m);
        }

        var newBySignature = new Dictionary<string, ApiMember>(StringComparer.Ordinal);
        foreach (var m in newMembers)
        {
            var key = GetMemberKey(m);
            newBySignature.TryAdd(key, m);
        }

        // Find matched (by full signature key), unmatched old, unmatched new
        var matchedOldKeys = new HashSet<string>(StringComparer.Ordinal);
        var matchedNewKeys = new HashSet<string>(StringComparer.Ordinal);

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
                        GetMemberKey(oldMember), GetMemberKey(newMember)));
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
                    $"Member '{oldMember.Name}' was removed"));
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
                    $"Member '{newMember.Name}' was added"));
            }
        }

        // For members matched by full signature, check modifier changes
        foreach (var key in matchedOldKeys)
        {
            var oldMember = oldBySignature[key];
            var newMember = newBySignature[key];
            CompareMemberModifiers(oldMember, newMember, changes);
        }
    }

    private static void CompareMemberModifiers(ApiMember oldMember, ApiMember newMember, List<ApiChange> changes)
    {
        // Virtual removed
        if (oldMember.IsVirtual && !newMember.IsVirtual)
        {
            changes.Add(new ApiChange(ChangeKind.VirtualRemoved, ChangeClassification.Breaking,
                $"Member '{oldMember.Name}' is no longer virtual"));
        }

        // Abstract added
        if (!oldMember.IsAbstract && newMember.IsAbstract)
        {
            changes.Add(new ApiChange(ChangeKind.AbstractMemberAdded, ChangeClassification.Breaking,
                $"Member '{oldMember.Name}' was made abstract"));
        }

        // Enum value changed
        if (oldMember.EnumValue != null && newMember.EnumValue != null &&
            oldMember.EnumValue != newMember.EnumValue)
        {
            changes.Add(new ApiChange(ChangeKind.EnumValueChanged, ChangeClassification.PotentiallyBreaking,
                $"Enum member '{oldMember.Name}' value changed from {oldMember.EnumValue} to {newMember.EnumValue}",
                oldMember.EnumValue.Value.ToString(), newMember.EnumValue.Value.ToString()));
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

    private static string GetMemberKey(ApiMember member)
    {
        // Use signature when available (methods, properties), else fall back to kind + name
        return member.Signature ?? $"{member.Kind}:{member.Name}";
    }
}
