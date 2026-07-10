using System.Collections.Immutable;
using ILInspector.Findings;

namespace ILInspector.Metadata;

/// <summary>
/// API-surface observations and comparisons over the domain-free Finding substrate.
/// Types and members are unordered identity sets; compatibility classification remains
/// the responsibility of <see cref="ApiDiffAnalyzer"/>.
/// </summary>
public static class MetadataFindings
{
    public static readonly FindingDescriptor TypeDescriptor = new("api.type", "API type");
    public static readonly FindingDescriptor MemberDescriptor = new("api.member", "API member");

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
            .. surface.Types.Select((type, position) => new Finding<ApiTypeHandle>(
                subject,
                TypeDescriptor,
                new FindingKey(type.FullName),
                position,
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
            .. EnumerateMembers(surface).Select((item, position) =>
            {
                var handle = ApiMemberIdentity.CreateHandle(item.Type, item.Member);
                return new Finding<ApiMemberHandle>(
                    subject,
                    MemberDescriptor,
                    new FindingKey(
                        handle.CanonicalSignature ?? handle.Identity,
                        item.Type.FullName),
                    position,
                    handle);
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
        ApiDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(oldSurface);
        ArgumentNullException.ThrowIfNull(newSurface);
        ArgumentNullException.ThrowIfNull(subject);
        options ??= ApiDiffOptions.Default;

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, options);
        return CompareApiMembers(oldSurface, newSurface, subject, options, diff);
    }

    public static ApiFindingComparison CompareApi(
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
        return new ApiFindingComparison(
            CompareApiTypes(oldSurface, newSurface, subject, options, diff),
            CompareApiMembers(oldSurface, newSurface, subject, options, diff),
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
        var oldFindings = InspectionFindings(oldInspection);
        var newFindings = InspectionFindings(newInspection);
        var match = FindingMatcher.Match(oldFindings.Keys(), newFindings.Keys(), IdentitySetOptions);
        var pairs = FindingFold.ToPairs(match, oldFindings, newFindings);
        pairs = ApplyTypeFacetChanges(pairs, diff, options.Scope);

        return new FindingComparison<ApiTypeHandle>.Complete(
            pairs,
            match,
            oldInspection,
            newInspection);
    }

    static FindingComparison<ApiMemberHandle> CompareApiMembers(
        ApiSurface oldSurface,
        ApiSurface newSurface,
        FindingSubject subject,
        ApiDiffOptions options,
        ApiDiff diff)
    {
        var oldInspection = InspectApiMembers(oldSurface, subject);
        var newInspection = InspectApiMembers(newSurface, subject);
        var oldFindings = InspectionFindings(oldInspection);
        var newFindings = InspectionFindings(newInspection);
        var match = FindingMatcher.Match(oldFindings.Keys(), newFindings.Keys(), IdentitySetOptions);
        var pairs = FindingFold.ToPairs(match, oldFindings, newFindings);
        pairs = ApplyMemberFacetChanges(pairs, diff, options.Scope);

        return new FindingComparison<ApiMemberHandle>.Complete(
            pairs,
            match,
            oldInspection,
            newInspection);
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
            else
            {
                builder.Add(pair);
            }
        }

        return builder.ToImmutable();
    }

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

    static ImmutableArray<Finding<T>> InspectionFindings<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection is FindingInspection<T>.Complete complete
            ? complete.Findings
            : throw new InvalidOperationException("API-surface inspection unexpectedly failed.");

    static IEnumerable<(ApiType Type, ApiMember Member)> EnumerateMembers(ApiSurface surface)
    {
        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
                yield return (type, member);
        }
    }
}

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
