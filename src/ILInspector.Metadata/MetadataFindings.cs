using System.Collections.Immutable;
using ILInspector.Findings;

namespace ILInspector.Metadata;

/// <summary>
/// API-surface observations and comparisons over the domain-free Finding substrate.
/// Types and members are unordered identity sets; compatibility classification remains
/// the responsibility of <see cref="ApiDiffAnalyzer"/>.
/// </summary>
public static partial class MetadataFindings
{
    public static readonly FindingDescriptor TypeDescriptor = new("api.type", "API type");
    public static readonly FindingDescriptor MemberDescriptor = new("api.member", "API member");
    public static readonly FindingDescriptor AttributeDescriptor = new("api.attribute", "API attribute");
    public static readonly FindingMatchTier ExtensionInstanceMatchTier =
        new("api.member.extension-instance", 85);

    static readonly FindingMatchOptions IdentitySetOptions = new()
    {
        MatchMode = FindingMatchMode.IdentitySet,
    };

    public static FindingInspection<ApiTypeHandle> InspectApiTypes(
        ApiSurface surface,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(subject);

        return new FindingInspection<ApiTypeHandle>.Complete(
        [
            .. surface.Types.Select(type => new Finding<ApiTypeHandle>(
                subject,
                TypeDescriptor,
                new FindingKey(type.FullName),
                new ApiTypeHandle(type))),
        ]);
    }

    public static FindingInspection<ApiMemberHandle> InspectApiMembers(
        ApiSurface surface,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(subject);

        return new FindingInspection<ApiMemberHandle>.Complete(
        [
            .. EnumerateMembers(surface).Select(item =>
            {
                var handle = ApiMemberIdentity.CreateHandle(item.Type, item.Member);
                return new Finding<ApiMemberHandle>(
                    subject,
                    MemberDescriptor,
                    CreateMemberFindingKey(item.Type, item.Member, handle),
                    handle);
            }),
        ]);
    }

    public static FindingInspection<ApiTypeHandle> InspectApiType(
        ApiSurface? surface,
        FindingSubject subject,
        string typeFullName)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrEmpty(typeFullName);
        if (surface is null)
            return new FindingInspection<ApiTypeHandle>.Absent();

        var type = FindType(surface, typeFullName);
        return type is null
            ? new FindingInspection<ApiTypeHandle>.Complete([])
            : new FindingInspection<ApiTypeHandle>.Complete(
            [
                new Finding<ApiTypeHandle>(
                    subject,
                    TypeDescriptor,
                    new FindingKey(type.FullName),
                    new ApiTypeHandle(type)),
            ]);
    }

    public static FindingInspection<ApiMemberHandle> InspectApiMembers(
        ApiSurface? surface,
        FindingSubject subject,
        string typeFullName)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrEmpty(typeFullName);
        if (surface is null)
            return new FindingInspection<ApiMemberHandle>.Absent();

        var type = FindType(surface, typeFullName);
        return type is null
            ? new FindingInspection<ApiMemberHandle>.Absent()
            : new FindingInspection<ApiMemberHandle>.Complete(
            [
                .. EnumerateMembers(type).Select(item =>
                {
                    var handle = ApiMemberIdentity.CreateHandle(item.Type, item.Member);
                    return new Finding<ApiMemberHandle>(
                        subject,
                        MemberDescriptor,
                        CreateMemberFindingKey(item.Type, item.Member, handle),
                        handle);
                }),
            ]);
    }

    public static FindingInspection<ApiAttributeHandle> InspectApiAttributes(
        ApiSurface? surface,
        FindingSubject subject,
        string typeFullName)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrEmpty(typeFullName);
        if (surface is null)
            return new FindingInspection<ApiAttributeHandle>.Absent();

        var type = FindType(surface, typeFullName);
        if (type is null)
            return new FindingInspection<ApiAttributeHandle>.Absent();

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        return new FindingInspection<ApiAttributeHandle>.Complete(
        [
            .. type.Attributes.Select(attribute =>
            {
                string name = GetAttributeName(attribute);
                int occurrence = occurrences.GetValueOrDefault(name);
                occurrences[name] = occurrence + 1;
                return new Finding<ApiAttributeHandle>(
                    subject,
                    AttributeDescriptor,
                    new FindingKey($"{type.FullName}|{name}|{occurrence}"),
                    new ApiAttributeHandle(
                        type.FullName,
                        name,
                        attribute,
                        occurrence),
                    Detail: attribute);
            }),
        ]);
    }

    public static FindingComparison<ApiTypeHandle> CompareApiTypes(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        FindingSubject subject,
        ApiDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldSurface);
        ArgumentNullException.ThrowIfNull(newSurface);
        ArgumentNullException.ThrowIfNull(subject);
        options ??= ApiDiffOptions.Default;

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, options);
        return CompareApiTypes(oldSurface, newSurface, subject, options, diff);
    }

    public static FindingComparison<ApiMemberHandle> CompareApiMembers(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        FindingSubject subject,
        ApiDiffOptions? options = null,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldSurface);
        ArgumentNullException.ThrowIfNull(newSurface);
        ArgumentNullException.ThrowIfNull(subject);
        options ??= ApiDiffOptions.Default;

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, options);
        return CompareApiMembers(
            oldSurface,
            newSurface,
            subject,
            options,
            diff,
            acceptanceThreshold);
    }

    public static FindingComparison<ApiTypeHandle> CompareApiType(
        ApiSurface? oldSurface,
        ApiSurface? newSurface,
        FindingSubject subject,
        string typeFullName,
        ApiDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrEmpty(typeFullName);
        options ??= ApiDiffOptions.Default;

        var comparison = FindingComparison.Compare(
            InspectApiType(oldSurface, subject, typeFullName),
            InspectApiType(newSurface, subject, typeFullName),
            IdentitySetOptions);
        if (oldSurface is null || newSurface is null)
            return comparison;

        var diff = ApiDiffAnalyzer.Compare(
            FocusSurface(oldSurface, typeFullName),
            FocusSurface(newSurface, typeFullName),
            options);
        return comparison.TransformPairs(
            pairs => ApplyTypeFacetChanges(pairs, diff, options.Scope));
    }

    public static FindingComparison<ApiMemberHandle> CompareApiMembers(
        ApiSurface? oldSurface,
        ApiSurface? newSurface,
        FindingSubject subject,
        string typeFullName,
        ApiDiffOptions? options = null,
        int acceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrEmpty(typeFullName);
        options ??= ApiDiffOptions.Default;

        var comparison = FindingComparison.Compare(
            InspectApiMembers(oldSurface, subject, typeFullName),
            InspectApiMembers(newSurface, subject, typeFullName),
            IdentitySetOptions,
            acceptanceThreshold);
        if (oldSurface is null || newSurface is null)
            return comparison;

        var diff = ApiDiffAnalyzer.Compare(
            FocusSurface(oldSurface, typeFullName),
            FocusSurface(newSurface, typeFullName),
            options);
        return comparison.TransformPairs(
            pairs => ApplyMemberFacetChanges(pairs, diff, options.Scope));
    }

    public static FindingComparison<ApiAttributeHandle> CompareApiAttributes(
        ApiSurface? oldSurface,
        ApiSurface? newSurface,
        FindingSubject subject,
        string typeFullName)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrEmpty(typeFullName);
        return FindingComparison.Compare(
            InspectApiAttributes(oldSurface, subject, typeFullName),
            InspectApiAttributes(newSurface, subject, typeFullName),
            IdentitySetOptions)
            .TransformPairs(ApplyAttributeValueChanges);
    }

    public static ApiFindingComparison CompareApi(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        FindingSubject subject,
        ApiDiffOptions? options = null,
        int memberAcceptanceThreshold = 100)
    {
        ArgumentNullException.ThrowIfNull(oldSurface);
        ArgumentNullException.ThrowIfNull(newSurface);
        ArgumentNullException.ThrowIfNull(subject);
        options ??= ApiDiffOptions.Default;

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, options);
        return new ApiFindingComparison(
            CompareApiTypes(oldSurface, newSurface, subject, options, diff),
            CompareApiMembers(
                oldSurface,
                newSurface,
                subject,
                options,
                diff,
                memberAcceptanceThreshold),
            diff);
    }

    static FindingComparison<ApiTypeHandle> CompareApiTypes(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        FindingSubject subject,
        ApiDiffOptions options,
        ApiDiff diff)
    {
        var oldInspection = InspectApiTypes(oldSurface, subject);
        var newInspection = InspectApiTypes(newSurface, subject);
        return FindingComparison.Compare(
            oldInspection,
            newInspection,
            IdentitySetOptions)
            .TransformPairs(pairs => ApplyTypeFacetChanges(pairs, diff, options.Scope));
    }

    static FindingComparison<ApiMemberHandle> CompareApiMembers(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        FindingSubject subject,
        ApiDiffOptions options,
        ApiDiff diff,
        int acceptanceThreshold = 100)
    {
        var oldInspection = InspectApiMembers(oldSurface, subject);
        var newInspection = InspectApiMembers(newSurface, subject);
        return FindingComparison.Compare(
            oldInspection,
            newInspection,
            IdentitySetOptions,
            acceptanceThreshold)
            .TransformPairs(pairs => ApplyMemberFacetChanges(pairs, diff, options.Scope));
    }

    static ImmutableArray<PairFinding<ApiTypeHandle>> ApplyTypeFacetChanges(
        ImmutableArray<PairFinding<ApiTypeHandle>> pairs,
        ApiDiff diff,
        ApiDiffScope scope)
    {
        var changesByKey = BuildTypeChangeMap(diff);
        var builder = ImmutableArray.CreateBuilder<PairFinding<ApiTypeHandle>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<ApiTypeHandle>.Present present)
            {
                var details = new List<string>();
                if (changesByKey.TryGetValue(present.New.Payload.TypeFullName, out var changes))
                    details.AddRange(changes.Select(FormatApiChange));
                AddTypeFacetDetails(present.Old.Payload.Type, present.New.Payload.Type, scope, details);

                builder.Add(details.Count == 0
                    ? pair
                    : new PairFinding<ApiTypeHandle>.Changed(
                        present.Old,
                        present.New,
                        present.Difference,
                        string.Join("; ", details.Distinct(StringComparer.Ordinal))));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.ToImmutable();
    }

    static ImmutableArray<PairFinding<ApiMemberHandle>> ApplyMemberFacetChanges(
        ImmutableArray<PairFinding<ApiMemberHandle>> pairs,
        ApiDiff diff,
        ApiDiffScope scope)
    {
        var changesByKey = BuildMemberChangeMap(diff);
        var builder = ImmutableArray.CreateBuilder<PairFinding<ApiMemberHandle>>(pairs.Length);
        foreach (var pair in pairs)
        {
            if (pair is PairFinding<ApiMemberHandle>.Present present)
            {
                string key = present.New.Key.IdentityKey;
                var details = new List<string>();
                if (changesByKey.TryGetValue(key, out var changes))
                    details.AddRange(changes.Select(FormatApiChange));
                AddMemberFacetDetails(present.Old.Payload.Member, present.New.Payload.Member, scope, details);

                builder.Add(details.Count == 0
                    ? pair
                    : new PairFinding<ApiMemberHandle>.Changed(
                        present.Old,
                        present.New,
                        present.Difference,
                        string.Join("; ", details.Distinct(StringComparer.Ordinal))));
            }
            else if (pair is PairFinding<ApiMemberHandle>.Changed changed
                && changed.Match is not null)
            {
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(changed.Detail))
                    details.Add(changed.Detail);
                AddMemberFacetDetails(
                    changed.Old.Payload.Member,
                    changed.New.Payload.Member,
                    scope,
                    details);
                AddFacetDetail(
                    details,
                    "declaring type",
                    changed.Old.Payload.TypeFullName,
                    changed.New.Payload.TypeFullName);

                builder.Add(new PairFinding<ApiMemberHandle>.Changed(
                    changed.Old,
                    changed.New,
                    changed.Difference,
                    string.Join("; ", details.Distinct(StringComparer.Ordinal)),
                    changed.Match));
            }
            else
            {
                builder.Add(pair);
            }
        }

        return builder.ToImmutable();
    }

    static ImmutableArray<PairFinding<ApiAttributeHandle>> ApplyAttributeValueChanges(
        ImmutableArray<PairFinding<ApiAttributeHandle>> pairs)
        => pairs
            .Select(pair => pair is PairFinding<ApiAttributeHandle>.Present present
                && !string.Equals(
                    present.Old.Payload.Attribute,
                    present.New.Payload.Attribute,
                    StringComparison.Ordinal)
                    ? new PairFinding<ApiAttributeHandle>.Changed(
                        present.Old,
                        present.New,
                        present.Difference,
                        $"{present.Old.Payload.Attribute} -> {present.New.Payload.Attribute}")
                    : pair)
            .ToImmutableArray();

    static Dictionary<string, List<ApiChange>> BuildTypeChangeMap(ApiDiff diff)
    {
        var changesByKey = new Dictionary<string, List<ApiChange>>(StringComparer.Ordinal);
        foreach (var change in diff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes))
        {
            if (change.Subject is not
                {
                    Kind: ApiChangeSubjectKind.Type,
                    OldType: not null,
                    NewType: not null,
                } subject
                || !string.Equals(subject.OldType.TypeFullName, subject.NewType.TypeFullName, StringComparison.Ordinal))
            {
                continue;
            }

            AddChange(changesByKey, subject.NewType.TypeFullName, change);
        }

        return changesByKey;
    }

    static Dictionary<string, List<ApiChange>> BuildMemberChangeMap(ApiDiff diff)
    {
        var changesByKey = new Dictionary<string, List<ApiChange>>(StringComparer.Ordinal);
        foreach (var change in diff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes))
        {
            if (change.Subject is not
                {
                    Kind: ApiChangeSubjectKind.Member,
                    OldMember.CanonicalSignature: not null,
                    NewMember.CanonicalSignature: not null,
                } subject
                || !string.Equals(
                    subject.OldMember.CanonicalSignature,
                    subject.NewMember.CanonicalSignature,
                    StringComparison.Ordinal))
            {
                continue;
            }

            AddChange(changesByKey, subject.NewMember.CanonicalSignature, change);
        }

        return changesByKey;
    }

    static void AddChange(Dictionary<string, List<ApiChange>> changesByKey, string key, ApiChange change)
    {
        if (!changesByKey.TryGetValue(key, out var changes))
            changesByKey.Add(key, changes = []);
        changes.Add(change);
    }

    static void AddTypeFacetDetails(ApiType oldType, ApiType newType, ApiDiffScope scope, List<string> details)
    {
        if (scope.HasFlag(ApiDiffScope.Signature))
        {
            AddFacetDetail(details, "kind", oldType.Kind, newType.Kind);
            AddFacetDetail(details, "enum underlying type", oldType.EnumUnderlyingType, newType.EnumUnderlyingType);
            AddFacetDetail(details, "sealed", oldType.IsSealed, newType.IsSealed);
            AddFacetDetail(details, "abstract", oldType.IsAbstract, newType.IsAbstract);
            AddFacetDetail(details, "static", oldType.IsStatic, newType.IsStatic);
            AddFacetDetail(details, "byref-like", oldType.IsByRefLike, newType.IsByRefLike);
            AddFacetDetail(details, "readonly", oldType.IsReadOnly, newType.IsReadOnly);
            AddFacetDetail(details, "base type", oldType.BaseType, newType.BaseType);
            AddFacetDetail(details, "interfaces", FormatSet(oldType.Interfaces), FormatSet(newType.Interfaces));
            AddFacetDetail(
                details,
                "type parameters",
                FormatTypeParameters(oldType.TypeParameters),
                FormatTypeParameters(newType.TypeParameters));
        }

        if (scope.HasFlag(ApiDiffScope.Attributes))
            AddFacetDetail(details, "attributes", FormatSet(oldType.Attributes), FormatSet(newType.Attributes));
    }

    static void AddMemberFacetDetails(ApiMember oldMember, ApiMember newMember, ApiDiffScope scope, List<string> details)
    {
        if (scope.HasFlag(ApiDiffScope.Signature))
        {
            AddFacetDetail(details, "signature", oldMember.Signature, newMember.Signature);
            AddFacetDetail(details, "return type", oldMember.ReturnType, newMember.ReturnType);
            AddFacetDetail(
                details,
                "accessibility",
                NormalizeAccessibility(oldMember.Accessibility),
                NormalizeAccessibility(newMember.Accessibility));
            AddFacetDetail(details, "static", oldMember.IsStatic, newMember.IsStatic);
            AddFacetDetail(details, "virtual", oldMember.IsVirtual, newMember.IsVirtual);
            AddFacetDetail(details, "abstract", oldMember.IsAbstract, newMember.IsAbstract);
            AddFacetDetail(details, "override", oldMember.IsOverride, newMember.IsOverride);
            AddFacetDetail(details, "sealed", oldMember.IsSealed, newMember.IsSealed);
            AddFacetDetail(details, "readonly", oldMember.IsReadOnly, newMember.IsReadOnly);
            AddFacetDetail(details, "const", oldMember.IsConst, newMember.IsConst);
            AddFacetDetail(details, "unsafe", oldMember.IsUnsafe, newMember.IsUnsafe);
            AddFacetDetail(details, "extension", oldMember.IsExtension, newMember.IsExtension);
            AddFacetDetail(details, "extended type", oldMember.ExtendedType, newMember.ExtendedType);
            AddFacetDetail(details, "enum value", FormatEnumValue(oldMember), FormatEnumValue(newMember));
        }

        if (scope.HasFlag(ApiDiffScope.Attributes))
        {
            AddFacetDetail(details, "attributes", FormatSet(oldMember.Attributes), FormatSet(newMember.Attributes));
            AddFacetDetail(details, "obsolete", oldMember.IsObsolete, newMember.IsObsolete);
            AddFacetDetail(details, "obsolete message", oldMember.ObsoleteMessage, newMember.ObsoleteMessage);
        }
    }

    static void AddFacetDetail(List<string> details, string name, bool oldValue, bool newValue)
    {
        if (oldValue != newValue)
            details.Add($"{name}: {oldValue.ToString().ToLowerInvariant()} -> {newValue.ToString().ToLowerInvariant()}");
    }

    static void AddFacetDetail(List<string> details, string name, string? oldValue, string? newValue)
    {
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            details.Add($"{name}: {FormatValue(oldValue)} -> {FormatValue(newValue)}");
    }

    static string FormatApiChange(ApiChange change)
    {
        string detail = $"{change.Category}: {change.Kind}";
        if (!string.IsNullOrEmpty(change.OldValue) || !string.IsNullOrEmpty(change.NewValue))
            detail += $" ({FormatValue(change.OldValue)} -> {FormatValue(change.NewValue)})";
        return detail;
    }

    static string FormatSet(IEnumerable<string> values)
        => string.Join(", ", values.Order(StringComparer.Ordinal));

    static string FormatTypeParameters(IEnumerable<TypeParameter> parameters)
        => string.Join(
            "; ",
            parameters.Select(parameter =>
                $"{parameter.Variance ?? ""} {parameter.Name}: {FormatSet(parameter.Constraints)}".Trim()));

    static string NormalizeAccessibility(string? accessibility)
        => string.IsNullOrWhiteSpace(accessibility) ? "public" : accessibility;

    static string? FormatEnumValue(ApiMember member)
        => member.EnumValueLiteral ?? member.EnumValue?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    static string FormatValue(string? value)
        => string.IsNullOrEmpty(value) ? "none" : value;

    static IEnumerable<(ApiType Type, ApiMember Member)> EnumerateMembers(ApiSurface surface)
    {
        foreach (var type in surface.Types)
        {
            foreach (var item in EnumerateMembers(type))
                yield return item;
        }
    }

    static IEnumerable<(ApiType Type, ApiMember Member)> EnumerateMembers(ApiType type)
    {
        foreach (var member in type.Members)
            yield return (type, member);
    }

    static FindingKey CreateMemberFindingKey(
        ApiType type,
        ApiMember member,
        ApiMemberHandle handle)
    {
        var softKeys = ApiMemberIdentity.TryGetExtensionInstanceProjection(
            type,
            member,
            out var identityKey,
            out var variant)
                ? (IEnumerable<FindingSoftKey>)
                [
                    new FindingSoftKey(
                        ExtensionInstanceMatchTier,
                        identityKey,
                        variant),
                ]
                : [];
        return new FindingKey(
            handle.CanonicalSignature ?? handle.Identity,
            type.FullName,
            softKeys);
    }

    static ApiType? FindType(ApiSurface surface, string typeFullName)
        => surface.Types.FirstOrDefault(
            type => string.Equals(type.FullName, typeFullName, StringComparison.Ordinal));

    static ApiSurface FocusSurface(ApiSurface surface, string typeFullName)
    {
        var type = FindType(surface, typeFullName);
        return new ApiSurface { Types = type is null ? [] : [type] };
    }

    static string GetAttributeName(string attribute)
    {
        int arguments = attribute.IndexOf('(');
        return arguments < 0 ? attribute : attribute[..arguments];
    }

}

public sealed record ApiAttributeHandle(
    string TypeFullName,
    string Name,
    string Attribute,
    int Occurrence);

/// <summary>
/// One API comparison operation: generic type/member transitions plus Metadata-owned
/// compatibility classifications over the same two surfaces.
/// </summary>
public sealed record ApiFindingComparison
{
    public ApiFindingComparison(
        FindingComparison<ApiTypeHandle> types,
        FindingComparison<ApiMemberHandle> members,
        ApiDiff apiDiff)
    {
        Types = types ?? throw new ArgumentNullException(nameof(types));
        Members = members ?? throw new ArgumentNullException(nameof(members));
        ApiDiff = apiDiff ?? throw new ArgumentNullException(nameof(apiDiff));
    }

    public FindingComparison<ApiTypeHandle> Types { get; }
    public FindingComparison<ApiMemberHandle> Members { get; }
    public ApiDiff ApiDiff { get; }

    public bool IsExact
        => Types is FindingComparison<ApiTypeHandle>.Complete typeComparison
        && Members is FindingComparison<ApiMemberHandle>.Complete memberComparison
        && ApiDiff.IsEmpty
        && FindingEquivalence.Exact.IsEquivalent(typeComparison.Pairs)
        && FindingEquivalence.Exact.IsEquivalent(memberComparison.Pairs);
}
