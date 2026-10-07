using System.Collections.Immutable;

using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.ResearchSections;

[Flags]
public enum LibraryDiffCategory
{
    None = 0,
    ApiAddition = 1,
    ApiDeletion = 2,
    ApiChange = 4,
    MethodBodyChange = 8,
}

public sealed record LibraryDiffSummaryMember(
    string Identifier,
    LibraryApiMemberIdentity? Before,
    LibraryApiMemberIdentity? After,
    LibraryDiffCategory Categories);

public sealed record LibraryDiffSummaryType(
    string Identifier,
    string Display,
    LibraryApiTypeIdentity? Before,
    LibraryApiTypeIdentity? After,
    LibraryDiffCategory Categories,
    ImmutableArray<LibraryDiffSummaryMember> Members);

public sealed record LibraryDiffSummaryCounts(
    int ChangedTypeCount,
    int ChangedMemberCount,
    int ApiAdditionCount,
    int ApiDeletionCount,
    int ApiChangeCount,
    int MethodBodyChangeCount,
    int UnavailableMethodBodyCount);

public sealed record LibraryDiffSummary(
    string LibraryIdentifier,
    string LibraryDisplay,
    LibraryApiDiffEndpointSummary Before,
    LibraryApiDiffEndpointSummary After,
    LibraryDiffSummaryCounts Counts,
    ImmutableArray<LibraryDiffSummaryType> Types);

public abstract record LibraryDiffSummaryOutcome
{
    private LibraryDiffSummaryOutcome()
    {
    }

    public sealed record Available(LibraryDiffSummary Summary)
        : LibraryDiffSummaryOutcome;

    public sealed record Unavailable(
        LibraryApiDiffUnavailableKind Kind,
        LibraryApiDiffEndpointSummary Before,
        LibraryApiDiffEndpointSummary After)
        : LibraryDiffSummaryOutcome;

    public sealed record Rejected(
        LibraryApiDiffRejectionKind Kind,
        LibraryApiDiffEndpointSummary Before,
        LibraryApiDiffEndpointSummary After)
        : LibraryDiffSummaryOutcome;
}

/// <summary>
/// Produces the shallow Library and Type Compare inventory. It retains only
/// exact identities and high-level API/body categories.
/// </summary>
public static class LibraryDiffSummaryInspection
{
    public static InspectionEnvelope<LibraryDiffSummaryOutcome> Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        ApiSurfaceProjectionLimits limits,
        IReadOnlySet<string>? typeFilters = null,
        ApiDiffScope diffScope = ApiDiffScope.Signature,
        IReadOnlySet<string>? memberTargetIdentities = null)
    {
        AssemblyContextApiComparisonResult comparison =
            AssemblyContextApiComparisonQuery.Execute(
                beforeGroup,
                before,
                afterGroup,
                after,
                ApiSurfaceScope.Public,
                limits,
                diffScope);
        LibraryApiDiffEndpointSummary beforeEndpoint =
            LibraryApiDiffPresentationAdapter.CreateEndpointSummary(
                comparison.Before,
                comparison.Scope);
        LibraryApiDiffEndpointSummary afterEndpoint =
            LibraryApiDiffPresentationAdapter.CreateEndpointSummary(
                comparison.After,
                comparison.Scope);

        LibraryDiffSummaryOutcome outcome =
            Create(
                beforeGroup,
                before,
                afterGroup,
                after,
                comparison,
                beforeEndpoint,
                afterEndpoint,
                typeFilters,
                memberTargetIdentities);
        return new(
            outcome,
            new InspectionShare.NonProjectable(
                "comparison/endpoints",
                "Workspace Share does not yet represent a selected-Library comparison's ordered endpoints and API scope."));
    }

    static LibraryDiffSummaryOutcome Create(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        AssemblyContextApiComparisonResult result,
        LibraryApiDiffEndpointSummary beforeEndpoint,
        LibraryApiDiffEndpointSummary afterEndpoint,
        IReadOnlySet<string>? typeFilters,
        IReadOnlySet<string>? memberTargetIdentities)
    {
        if (!beforeEndpoint.IsComplete || !afterEndpoint.IsComplete)
        {
            LibraryApiDiffUnavailableKind kind =
                !beforeEndpoint.IsComplete && !afterEndpoint.IsComplete
                    ? LibraryApiDiffUnavailableKind.BothIncomplete
                    : !beforeEndpoint.IsComplete
                        ? LibraryApiDiffUnavailableKind.BeforeIncomplete
                        : LibraryApiDiffUnavailableKind.AfterIncomplete;
            return new LibraryDiffSummaryOutcome.Unavailable(
                kind,
                beforeEndpoint,
                afterEndpoint);
        }

        if (result.Comparison is not
            {
                Types.Value:
                    FindingComparison<ApiTypeHandle>.Complete typeComparison,
                Members.Value:
                    FindingComparison<ApiMemberHandle>.Complete
                        memberComparison,
            } comparison)
        {
            return new LibraryDiffSummaryOutcome.Rejected(
                LibraryApiDiffRejectionKind.FindingComparisonFailed,
                beforeEndpoint,
                afterEndpoint);
        }
        if (comparison.ApiDiff.InspectionFailures.Count > 0)
        {
            return new LibraryDiffSummaryOutcome.Rejected(
                LibraryApiDiffRejectionKind
                    .CompatibilityInspectionFailed,
                beforeEndpoint,
                afterEndpoint);
        }
        if (!TryCreateLibraryIdentity(
                beforeEndpoint.Identity,
                afterEndpoint.Identity,
                out string libraryIdentifier,
                out string libraryDisplay))
        {
            return new LibraryDiffSummaryOutcome.Rejected(
                LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
                beforeEndpoint,
                afterEndpoint);
        }

        var types = new Dictionary<string, TypeBuilder>(
            StringComparer.Ordinal);
        foreach (PairFinding<ApiTypeHandle> pair
            in typeComparison.Pairs)
        {
            if (!TryProjectTypePair(
                    pair,
                    out LibraryApiTypeIdentity? beforeType,
                    out LibraryApiTypeIdentity? afterType,
                    out LibraryDiffCategory category))
            {
                return new LibraryDiffSummaryOutcome.Rejected(
                    LibraryApiDiffRejectionKind.MissingExactTypeIdentity,
                    beforeEndpoint,
                    afterEndpoint);
            }
            string identifier =
                afterType?.Identifier ?? beforeType!.Identifier;
            if (!types.TryAdd(
                    identifier,
                    new TypeBuilder(
                        beforeType,
                        afterType,
                        category)))
            {
                return new LibraryDiffSummaryOutcome.Rejected(
                    LibraryApiDiffRejectionKind
                        .DuplicateExactTypeIdentity,
                    beforeEndpoint,
                    afterEndpoint);
            }
        }

        var members = new Dictionary<string, MemberBuilder>(
            StringComparer.Ordinal);
        foreach (PairFinding<ApiMemberHandle> pair
            in memberComparison.Pairs)
        {
            if (!TryProjectMemberPair(
                    pair,
                    out ApiMemberHandle? beforeHandle,
                    out ApiMemberHandle? afterHandle,
                    out LibraryApiMemberIdentity? beforeMember,
                    out LibraryApiMemberIdentity? afterMember,
                    out LibraryDiffCategory category))
            {
                return new LibraryDiffSummaryOutcome.Rejected(
                    beforeHandle?.Type.DefinitionName is null
                        || afterHandle?.Type.DefinitionName is null
                            ? LibraryApiDiffRejectionKind
                                .MissingExactTypeIdentity
                            : LibraryApiDiffRejectionKind
                                .MissingMemberAnchor,
                    beforeEndpoint,
                    afterEndpoint);
            }

            string key = MemberKey(beforeMember, afterMember);
            if (!members.TryGetValue(key, out MemberBuilder? member))
            {
                member = new(
                    beforeHandle,
                    afterHandle,
                    beforeMember,
                    afterMember,
                    category);
                members.Add(key, member);
                Place(member, beforeMember?.DeclaringType, types);
                if (afterMember?.DeclaringType.Identifier
                    != beforeMember?.DeclaringType.Identifier)
                {
                    Place(member, afterMember?.DeclaringType, types);
                }
            }
            else
            {
                member.Categories |= category;
            }
        }

        List<BodyComparison> bodyComparisons = [];
        foreach (MemberBuilder member in members.Values)
        {
            if (member.BeforeHandle is null
                || member.AfterHandle is null
                || !IsSelected(
                    member,
                    typeFilters,
                    memberTargetIdentities))
            {
                continue;
            }

            Dictionary<ResearchTargetRelationshipRole, int> beforeBodies =
                BodyTokens(member.BeforeHandle);
            Dictionary<ResearchTargetRelationshipRole, int> afterBodies =
                BodyTokens(member.AfterHandle);
            foreach (ResearchTargetRelationshipRole role
                in beforeBodies.Keys.Union(afterBodies.Keys))
            {
                bool hasBefore =
                    beforeBodies.TryGetValue(role, out int beforeToken);
                bool hasAfter =
                    afterBodies.TryGetValue(role, out int afterToken);
                if (!hasBefore || !hasAfter)
                    continue;
                bodyComparisons.Add(
                    new(
                        member,
                        new(
                            beforeToken,
                            afterToken)));
            }
        }

        int unavailableBodies = 0;
        if (bodyComparisons.Count > 0)
        {
            IReadOnlyList<ImplementationBodyChange> bodyResults =
                AssemblyContextImplementationChangeSummaryQuery.Execute(
                    beforeGroup,
                    before,
                    afterGroup,
                    after,
                    [.. bodyComparisons.Select(item => item.Pair)]);
            for (int index = 0; index < bodyResults.Count; index++)
            {
                switch (bodyResults[index].Kind)
                {
                    case ImplementationBodyChangeKind.Exact:
                        break;
                    case ImplementationBodyChangeKind.Changed:
                        bodyComparisons[index].Member.Categories |=
                            LibraryDiffCategory.MethodBodyChange;
                        break;
                    case ImplementationBodyChangeKind.Unavailable:
                        unavailableBodies++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown implementation body summary kind.");
                }
            }
        }

        TypeBuilder[] selectedTypeBuilders =
        [
            .. types.Values
                .Where(type => IsSelected(type, typeFilters)),
        ];
        ImmutableArray<LibraryDiffSummaryType> selectedTypes =
        [
            .. selectedTypeBuilders
                .Select(type =>
                    type.Build(memberTargetIdentities))
                .Where(type =>
                    type.Categories != LibraryDiffCategory.None
                    || type.Members.Length > 0),
        ];
        int changedMembers =
            selectedTypes
                .SelectMany(type => type.Members)
                .DistinctBy(member => member.Identifier)
                .Count();
        var counts = new LibraryDiffSummaryCounts(
            selectedTypes.Length,
            changedMembers,
            Count(
                selectedTypeBuilders,
                selectedTypes,
                LibraryDiffCategory.ApiAddition),
            Count(
                selectedTypeBuilders,
                selectedTypes,
                LibraryDiffCategory.ApiDeletion),
            Count(
                selectedTypeBuilders,
                selectedTypes,
                LibraryDiffCategory.ApiChange),
            Count(
                selectedTypeBuilders,
                selectedTypes,
                LibraryDiffCategory.MethodBodyChange),
            unavailableBodies);
        return new LibraryDiffSummaryOutcome.Available(
            new(
                libraryIdentifier,
                libraryDisplay,
                beforeEndpoint,
                afterEndpoint,
                counts,
                selectedTypes));
    }

    static int Count(
        IReadOnlyList<TypeBuilder> typeBuilders,
        ImmutableArray<LibraryDiffSummaryType> types,
        LibraryDiffCategory category)
        => typeBuilders.Count(type =>
                type.Categories.HasFlag(category))
            + types
            .SelectMany(type => type.Members)
            .DistinctBy(member => member.Identifier)
            .Count(member =>
                member.Categories.HasFlag(category));

    static void Place(
        MemberBuilder member,
        LibraryApiTypeIdentity? type,
        Dictionary<string, TypeBuilder> types)
    {
        if (type is null)
            return;
        if (!types.TryGetValue(type.Identifier, out TypeBuilder? owner))
        {
            owner = new(
                Before: null,
                After: type,
                LibraryDiffCategory.None);
            types.Add(type.Identifier, owner);
        }
        owner.Members.Add(member);
    }

    static bool IsSelected(
        MemberBuilder member,
        IReadOnlySet<string>? typeFilters,
        IReadOnlySet<string>? memberTargetIdentities)
        => MatchesTypeFilter(member, typeFilters)
            && MatchesMemberTarget(
                member,
                memberTargetIdentities);

    static bool MatchesTypeFilter(
        MemberBuilder member,
        IReadOnlySet<string>? typeFilters)
        => typeFilters is null
            || typeFilters.Count == 0
            || member.Before?.DeclaringType is { } before
                && typeFilters.Contains(before.Identifier)
            || member.After?.DeclaringType is { } after
                && typeFilters.Contains(after.Identifier);

    static bool IsSelected(
        TypeBuilder type,
        IReadOnlySet<string>? filters)
        => filters is null
            || filters.Count == 0
            || filters.Contains(type.Identifier);

    static bool MatchesMemberTarget(
        MemberBuilder member,
        IReadOnlySet<string>? memberTargetIdentities)
        => memberTargetIdentities is null
            || memberTargetIdentities.Count == 0
            || member.Before is { } before
                && memberTargetIdentities.Contains(
                    before.Anchor.StableSelector)
            || member.After is { } after
                && memberTargetIdentities.Contains(
                    after.Anchor.StableSelector);

    static Dictionary<ResearchTargetRelationshipRole, int> BodyTokens(
        ApiMemberHandle handle)
    {
        var bodies =
            new Dictionary<ResearchTargetRelationshipRole, int>();
        foreach (var body in CallGraphMemberResolver.CreateBodySelectors(
            handle.Type,
            handle.Member))
        {
            ResearchTargetRelationshipRole role =
                Role(handle.Member, body.BodyToken);
            if (!HasPublicBody(handle.Member, role))
                continue;
            bodies.TryAdd(role, body.BodyToken);
        }
        return bodies;
    }

    static ResearchTargetRelationshipRole Role(
        ApiMember member,
        int token)
        => token == member.GetterToken
            ? ResearchTargetRelationshipRole.Getter
            : token == member.SetterToken
                ? ResearchTargetRelationshipRole.Setter
                : token == member.AdderToken
                    ? ResearchTargetRelationshipRole.Adder
                    : token == member.RemoverToken
                        ? ResearchTargetRelationshipRole.Remover
                        : ResearchTargetRelationshipRole.Method;

    static bool HasPublicBody(
        ApiMember member,
        ResearchTargetRelationshipRole role)
        => role switch
        {
            ResearchTargetRelationshipRole.Getter =>
                member.GetterAccessibility is null or "public"
                && member.GetterHasMethodBody == true,
            ResearchTargetRelationshipRole.Setter =>
                member.SetterAccessibility is null or "public"
                && member.SetterHasMethodBody == true,
            ResearchTargetRelationshipRole.Adder =>
                member.AdderAccessibility is null or "public"
                && member.AdderHasMethodBody == true,
            ResearchTargetRelationshipRole.Remover =>
                member.RemoverAccessibility is null or "public"
                && member.RemoverHasMethodBody == true,
            ResearchTargetRelationshipRole.Method =>
                member.Accessibility is null or "public"
                && member.HasMethodBody == true,
            _ => false,
        };

    static bool TryProjectTypePair(
        PairFinding<ApiTypeHandle> pair,
        out LibraryApiTypeIdentity? before,
        out LibraryApiTypeIdentity? after,
        out LibraryDiffCategory category)
    {
        ApiTypeHandle? beforeHandle = null;
        ApiTypeHandle? afterHandle = null;
        switch (pair)
        {
            case PairFinding<ApiTypeHandle>.Added added:
                afterHandle = added.New.Payload;
                category = LibraryDiffCategory.ApiAddition;
                break;
            case PairFinding<ApiTypeHandle>.Removed removed:
                beforeHandle = removed.Old.Payload;
                category = LibraryDiffCategory.ApiDeletion;
                break;
            case PairFinding<ApiTypeHandle>.Changed changed:
                beforeHandle = changed.Old.Payload;
                afterHandle = changed.New.Payload;
                category = LibraryDiffCategory.ApiChange;
                break;
            case PairFinding<ApiTypeHandle>.Present present:
                beforeHandle = present.Old.Payload;
                afterHandle = present.New.Payload;
                category = LibraryDiffCategory.None;
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Type comparison pair.");
        }
        bool beforeProjected =
            TryProjectType(beforeHandle, out before);
        bool afterProjected =
            TryProjectType(afterHandle, out after);
        return beforeProjected && afterProjected;
    }

    static bool TryProjectMemberPair(
        PairFinding<ApiMemberHandle> pair,
        out ApiMemberHandle? beforeHandle,
        out ApiMemberHandle? afterHandle,
        out LibraryApiMemberIdentity? before,
        out LibraryApiMemberIdentity? after,
        out LibraryDiffCategory category)
    {
        beforeHandle = null;
        afterHandle = null;
        switch (pair)
        {
            case PairFinding<ApiMemberHandle>.Added added:
                afterHandle = added.New.Payload;
                category = LibraryDiffCategory.ApiAddition;
                break;
            case PairFinding<ApiMemberHandle>.Removed removed:
                beforeHandle = removed.Old.Payload;
                category = LibraryDiffCategory.ApiDeletion;
                break;
            case PairFinding<ApiMemberHandle>.Changed changed:
                beforeHandle = changed.Old.Payload;
                afterHandle = changed.New.Payload;
                category = LibraryDiffCategory.ApiChange;
                break;
            case PairFinding<ApiMemberHandle>.Present present:
                beforeHandle = present.Old.Payload;
                afterHandle = present.New.Payload;
                category = LibraryDiffCategory.None;
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Member comparison pair.");
        }
        bool beforeProjected =
            TryProjectMember(beforeHandle, out before);
        bool afterProjected =
            TryProjectMember(afterHandle, out after);
        return beforeProjected && afterProjected;
    }

    static bool TryProjectType(
        ApiTypeHandle? handle,
        out LibraryApiTypeIdentity? identity)
    {
        if (handle is null)
        {
            identity = null;
            return true;
        }
        if (handle.Type.DefinitionName is not { } definitionName
            || string.IsNullOrWhiteSpace(handle.TypeFullName))
        {
            identity = null;
            return false;
        }
        identity = new(
            definitionName,
            handle.TypeFullName);
        return true;
    }

    static bool TryProjectMember(
        ApiMemberHandle? handle,
        out LibraryApiMemberIdentity? identity)
    {
        if (handle is null)
        {
            identity = null;
            return true;
        }
        if (handle.Anchor is not { } anchor
            || !TryProjectType(
                new ApiTypeHandle(handle.Type),
                out LibraryApiTypeIdentity? declaringType)
            || declaringType is null
            || string.IsNullOrWhiteSpace(handle.MemberName))
        {
            identity = null;
            return false;
        }
        identity = new(
            declaringType,
            anchor,
            handle.AnchorKind,
            handle.Member.Kind,
            handle.MemberName);
        return true;
    }

    static string MemberKey(
        LibraryApiMemberIdentity? before,
        LibraryApiMemberIdentity? after)
        => $"{before?.DeclaringType.Identifier}|"
            + $"{before?.Anchor.StableSelector}|"
            + $"{after?.DeclaringType.Identifier}|"
            + $"{after?.Anchor.StableSelector}";

    static bool TryCreateLibraryIdentity(
        AssemblyReferenceIdentity before,
        AssemblyReferenceIdentity after,
        out string identifier,
        out string display)
    {
        string beforeCulture = NormalizeCulture(before.Culture);
        string afterCulture = NormalizeCulture(after.Culture);
        string beforeToken = before.PublicKeyToken ?? "";
        string afterToken = after.PublicKeyToken ?? "";
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                before.Name,
                after.Name)
            || !StringComparer.OrdinalIgnoreCase.Equals(
                beforeCulture,
                afterCulture)
            || !StringComparer.OrdinalIgnoreCase.Equals(
                beforeToken,
                afterToken))
        {
            identifier = "";
            display = "";
            return false;
        }

        identifier =
            $"{before.Name.ToUpperInvariant()}|"
                + $"{beforeCulture.ToUpperInvariant()}|"
                + beforeToken.ToUpperInvariant();
        display = before.Name;
        return true;
    }

    static string NormalizeCulture(string? value)
        => string.IsNullOrEmpty(value)
            || value.Equals(
                "neutral",
                StringComparison.OrdinalIgnoreCase)
                ? ""
                : value;

    sealed class TypeBuilder(
        LibraryApiTypeIdentity? Before,
        LibraryApiTypeIdentity? After,
        LibraryDiffCategory Categories)
    {
        internal LibraryApiTypeIdentity? Before { get; } = Before;
        internal LibraryApiTypeIdentity? After { get; } = After;
        internal LibraryDiffCategory Categories { get; set; } =
            Categories;
        internal List<MemberBuilder> Members { get; } = [];
        internal string Identifier =>
            After?.Identifier ?? Before!.Identifier;

        internal LibraryDiffSummaryType Build(
            IReadOnlySet<string>? memberTargetIdentities)
        {
            ImmutableArray<LibraryDiffSummaryMember> members =
            [
                .. Members
                    .DistinctBy(member => member.Identifier)
                    .Where(member =>
                        MatchesMemberTarget(
                            member,
                            memberTargetIdentities))
                    .Select(member => member.Build())
                    .Where(member =>
                        member.Categories
                            != LibraryDiffCategory.None),
            ];
            LibraryDiffCategory categories =
                Categories
                | members.Aggregate(
                    LibraryDiffCategory.None,
                    (value, member) =>
                        value | member.Categories);
            return new(
                Identifier,
                After?.Display ?? Before!.Display,
                Before,
                After,
                categories,
                members);
        }
    }

    sealed class MemberBuilder(
        ApiMemberHandle? BeforeHandle,
        ApiMemberHandle? AfterHandle,
        LibraryApiMemberIdentity? Before,
        LibraryApiMemberIdentity? After,
        LibraryDiffCategory Categories)
    {
        internal ApiMemberHandle? BeforeHandle { get; } =
            BeforeHandle;
        internal ApiMemberHandle? AfterHandle { get; } =
            AfterHandle;
        internal LibraryApiMemberIdentity? Before { get; } =
            Before;
        internal LibraryApiMemberIdentity? After { get; } = After;
        internal LibraryDiffCategory Categories { get; set; } =
            Categories;
        internal string Identifier => MemberKey(Before, After);

        internal LibraryDiffSummaryMember Build()
            => new(
                Identifier,
                Before,
                After,
                Categories);
    }

    sealed record BodyComparison(
        MemberBuilder Member,
        ImplementationBodyPair Pair);
}
