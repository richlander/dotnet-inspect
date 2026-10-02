using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>A public roster member of one eligible overload family.</summary>
public sealed record ImplementationHeatRosterMember(
    string TypeDefinitionId,
    string StableSelector,
    int MetadataToken);

/// <summary>
/// One same-name method in an eligible family's analyzed family, regardless
/// of accessibility. <see cref="Size"/> sums the logical body and every
/// generated body attributed to it; it is null when the method has no body or
/// no profile was issued.
/// </summary>
public sealed record ImplementationHeatMethod(
    int MetadataToken,
    bool IsRosterMember,
    bool HasBody,
    int? Size,
    bool IsTrivial,
    bool IsComplete);

/// <summary>An owner-issued same-name call between two analyzed methods.</summary>
public sealed record ImplementationHeatRelationship(
    int CallerToken,
    int CalleeToken);

/// <summary>
/// Compact heat evidence for one eligible overload family: every public
/// overload of one method name on the Type, all of member kind
/// <c>method</c>.
/// </summary>
public sealed record ImplementationHeatFamily(
    string Member,
    ImmutableArray<ImplementationHeatRosterMember> Roster,
    ImmutableArray<ImplementationHeatMethod> Methods,
    ImmutableArray<ImplementationHeatRelationship> Relationships,
    ImmutableArray<ImplementationProfileUnavailableBody> UnavailableBodies,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>
/// Heat evidence for every eligible overload family on one Type, measured in a
/// single Analysis execution. <see cref="Coverage"/> is that execution's
/// receipt narrowed to the analyzed methods, or null when no family method has
/// a body.
/// </summary>
public sealed record AssemblyTypeImplementationHeatInspection(
    string TypeDefinitionId,
    ImmutableArray<ImplementationHeatFamily> Families,
    ImplementationProfilePopulationCoverageReceipt? Coverage,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<ApiSurfaceInspectionFailure> ApiSurfaceInspectionFailures);

/// <summary>
/// Collects member-list heat for one public Type: every eligible overload
/// family's same-name methods, of any accessibility, analyzed in one scope.
/// </summary>
public static class AssemblyContextTypeImplementationHeatQuery
{
    // Breadth is bounded by the Type, but cost is dominated by whole-assembly
    // Analysis setup, so the query remains an explicit request.
    public static InspectionQuery<
        AssemblyContextEntry<AssemblyTypeImplementationHeatInspection>>
        Definition { get; } =
        new(
            "Assembly context type implementation heat",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<
        AssemblyTypeImplementationHeatInspection> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            string typeDefinitionId)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDefinitionId);

        AssemblyContextEntry<SelectedType> selected =
            AssemblyContextQueryExecutor.ExecuteParticipant(
                group,
                participant,
                session => SelectType(session, typeDefinitionId));
        return selected switch
        {
            AssemblyContextEntry<SelectedType>.Rejected rejected =>
                new AssemblyContextEntry<
                    AssemblyTypeImplementationHeatInspection>.Rejected(
                        rejected.Subject,
                        rejected.Failure),
            AssemblyContextEntry<SelectedType>.Failed failed =>
                new AssemblyContextEntry<
                    AssemblyTypeImplementationHeatInspection>.Failed(
                        failed.Subject,
                        failed.Error),
            AssemblyContextEntry<SelectedType>.Available available =>
                AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
                    group,
                    participant,
                    (subject, snapshot) =>
                    {
                        if (!ReferenceEquals(
                                subject.Registration,
                                available.Subject.Registration))
                        {
                            throw new InspectionQueryException(
                                "Type implementation-heat selection belongs "
                                    + "to another assembly-context participant.");
                        }
                        return Analyze(
                            group,
                            subject,
                            snapshot,
                            available.Value);
                    }),
            _ => throw new InvalidOperationException(
                $"Unknown type implementation-heat selection outcome "
                    + $"'{selected.GetType().Name}'."),
        };
    }

    static AssemblyTypeImplementationHeatInspection Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        SelectedType type)
    {
        ImmutableHashSet<int> bodyTokens =
        [
            .. type.Families.SelectMany(family =>
                family.Methods
                    .Where(method => method.HasBody)
                    .Select(method => method.MetadataToken)),
        ];
        if (bodyTokens.IsEmpty)
        {
            return new(
                type.TypeDefinitionId,
                [.. type.Families.Select(family => Project(family, null))],
                Coverage: null,
                [],
                type.InspectionFailures);
        }

        var resolver = AssemblyContextAnalysisSource.Resolver(group, subject);
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecuteImage(
                AssemblyContextAnalysisSource.Name(subject),
                snapshot.Content,
                LibraryBodyAnalysisRequest
                    .CreateCompleteImplementationProfile(bodyTokens),
                resolver);
        ImplementationProfilesResult.Available profiles =
            ImplementationProfilesQuery.Execute(
                analysis.ImplementationProfiles) switch
            {
                ImplementationProfilesResult.Available available =>
                    available,
                ImplementationProfilesResult.NoMetadata =>
                    throw new InspectionQueryException(
                        $"Assembly '{subject.Identity.Name}' has no metadata "
                            + "for type implementation-heat inspection."),
                ImplementationProfilesResult.Failed failed =>
                    throw new InspectionQueryException(
                        $"Type implementation-heat inspection failed for "
                            + $"'{subject.Identity.Name}'.",
                        failed.Error),
                _ => throw new InvalidOperationException(
                    "Unknown implementation-profile query result."),
            };
        var measured = new Measured(
            profiles,
            AssemblyContextImplementationProfileFamilyQuery.ScopeCoverage(
                analysis.ImplementationProfiles.Coverage,
                profiles.Profiles.Where(profile =>
                    bodyTokens.Contains(profile.Method.MetadataToken)),
                bodyTokens));

        ImmutableArray<ImplementationHeatFamily> families =
        [
            .. type.Families.Select(family => Project(family, measured)),
        ];
        var result = new AssemblyTypeImplementationHeatInspection(
            type.TypeDefinitionId,
            families,
            measured.Coverage,
            [
                .. measured.Coverage.Diagnostics.Where(diagnostic =>
                    bodyTokens.Contains(diagnostic.MethodToken)
                    || diagnostic.SourceMethodToken is { } source
                        && bodyTokens.Contains(source)),
            ],
            type.InspectionFailures);
        resolver.ValidateForPublication();
        return result;
    }

    static ImplementationHeatFamily Project(
        SelectedFamily family,
        Measured? measured)
    {
        ImmutableHashSet<int> tokens =
            [.. family.Methods.Select(method => method.MetadataToken)];
        Dictionary<int, List<MethodImplementationProfile>> byMethod = [];
        ImmutableArray<ImplementationProfileUnavailableBody> unavailable = [];
        ImmutableArray<AnalysisDiagnostic> diagnostics = [];
        ImmutableArray<ImplementationHeatRelationship> relationships = [];
        if (measured is not null)
        {
            // Declared-source attribution issues each generated body under its
            // logical owner, so grouping by the logical method counts every
            // physical body exactly once.
            foreach (MethodImplementationProfile profile
                in measured.Profiles.Profiles)
            {
                int owner = profile.Method.MetadataToken;
                if (!tokens.Contains(owner))
                    continue;
                if (!byMethod.TryGetValue(owner, out var owned))
                    byMethod.Add(owner, owned = []);
                owned.Add(profile);
            }
            unavailable =
            [
                .. measured.Coverage.UnavailableBodies.Where(body =>
                    tokens.Contains(body.MethodToken)
                    || body.Diagnostic?.SourceMethodToken is { } source
                        && tokens.Contains(source)),
            ];
            diagnostics =
            [
                .. measured.Coverage.Diagnostics.Where(diagnostic =>
                    tokens.Contains(diagnostic.MethodToken)
                    || diagnostic.SourceMethodToken is { } source
                        && tokens.Contains(source)),
            ];
            relationships =
            [
                .. measured.Profiles.OverloadRelationships
                    .Where(relationship =>
                        tokens.Contains(relationship.Caller.MetadataToken)
                        && tokens.Contains(relationship.Callee.MetadataToken))
                    .Select(relationship =>
                        new ImplementationHeatRelationship(
                            relationship.Caller.MetadataToken,
                            relationship.Callee.MetadataToken))
                    .Distinct()
                    .OrderBy(relationship => relationship.CallerToken)
                    .ThenBy(relationship => relationship.CalleeToken),
            ];
        }

        HashSet<int> unavailableOwners =
        [
            .. unavailable.Select(body =>
                body.Diagnostic?.SourceMethodToken is { } source
                    && tokens.Contains(source)
                    ? source
                    : body.MethodToken),
        ];
        ImmutableArray<ImplementationHeatMethod> methods =
        [
            .. family.Methods.Select(method =>
            {
                List<MethodImplementationProfile>? counted =
                    byMethod.GetValueOrDefault(method.MetadataToken);
                bool measuredBody = counted is { Count: > 0 };
                return new ImplementationHeatMethod(
                    method.MetadataToken,
                    method.IsRosterMember,
                    method.HasBody,
                    measuredBody
                        ? counted!.Sum(profile => profile.InstructionCount)
                        : null,
                    measuredBody && counted!.All(IsTrivial),
                    !method.HasBody
                        || measuredBody
                            && counted!.All(profile => profile.IsComplete)
                            && !unavailableOwners.Contains(
                                method.MetadataToken));
            }),
        ];
        return new ImplementationHeatFamily(
            family.Member,
            family.Roster,
            methods,
            relationships,
            unavailable,
            diagnostics);
    }

    static bool IsTrivial(MethodImplementationProfile profile) =>
        profile.InstructionCount <= 8
        && profile.BranchCount == 0
        && profile.LoopCount == 0
        && profile.CatchCount + profile.FilterCount
            + profile.FinallyCount + profile.FaultCount == 0
        && !profile.Unsafe
        && profile.ReflectionCallCount == 0;

    static SelectedType SelectType(
        AssemblyInspectionSession session,
        string typeDefinitionId)
    {
        ApiSurface surface =
            session.ApiSurface(ApiSurfaceExtractionScope.Public);
        ApiType[] matchingTypes =
        [
            .. surface.Types.Where(type =>
                AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type)
                    == typeDefinitionId),
        ];
        if (matchingTypes.Length != 1)
        {
            throw new InspectionQueryException(
                matchingTypes.Length == 0
                    ? $"Public API Type '{typeDefinitionId}' was not found."
                    : $"Public API Type '{typeDefinitionId}' is ambiguous.");
        }

        ApiType type = matchingTypes[0];
        var families = ImmutableArray.CreateBuilder<SelectedFamily>();
        foreach (IGrouping<string, ApiMember> group
            in type.Members.GroupBy(member => member.Name, StringComparer.Ordinal))
        {
            ApiMember[] members = [.. group];
            if (members.Length < 2 || !IsEligibleFamily(members))
            {
                continue;
            }

            var roster = ImmutableArray.CreateBuilder<
                ImplementationHeatRosterMember>(members.Length);
            foreach (ApiMember member in members)
            {
                if (member.MetadataToken is not { } token)
                {
                    throw new InspectionQueryException(
                        $"Public API Member '{type.FullName}.{member.Name}' "
                            + "has no metadata token for implementation heat.");
                }
                roster.Add(new ImplementationHeatRosterMember(
                    typeDefinitionId,
                    ApiMemberIdentity.GetMemberAnchor(type, member)
                        .StableSelector,
                    token));
            }

            // Every roster member has one declaring Type and name, so one
            // enumeration lists the whole analyzed family.
            ImmutableArray<ImplementationHeatRosterMember> rosterMembers =
                roster.MoveToImmutable();
            HashSet<int> rosterTokens =
                [.. rosterMembers.Select(member => member.MetadataToken)];
            families.Add(new SelectedFamily(
                group.Key,
                rosterMembers,
                [
                    .. session.MethodBodies
                        .EnumerateSameNameMethods(rosterMembers[0].MetadataToken)
                        .Select(method => new AnalyzedMethod(
                            method.MetadataToken,
                            rosterTokens.Contains(method.MetadataToken),
                            method.HasBody))
                        .OrderBy(method => method.MetadataToken),
                ]));
        }

        HashSet<int> typeTokens =
        [
            .. families.SelectMany(family =>
                family.Methods.Select(method => method.MetadataToken)),
        ];
        if (type.MetadataToken is { } typeToken)
            typeTokens.Add(typeToken);
        return new SelectedType(
            typeDefinitionId,
            families.ToImmutable(),
            [
                .. surface.InspectionFailures.Where(failure =>
                    typeTokens.Contains(failure.SubjectToken)),
            ]);
    }

    static bool IsEligibleFamily(IReadOnlyList<ApiMember> members)
    {
        if (members.All(member =>
                member.Kind == "method"
                && member.DeclaringTypeDefinitionName is null))
        {
            return true;
        }

        if (members.Any(member => member.Kind != "extension-method"))
            return false;

        MetadataTypeDefinitionName? declaringType =
            members[0].DeclaringTypeDefinitionName;
        return declaringType is not null
            && members.All(member =>
                member.DeclaringTypeDefinitionName == declaringType);
    }

    sealed record SelectedType(
        string TypeDefinitionId,
        ImmutableArray<SelectedFamily> Families,
        ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

    sealed record SelectedFamily(
        string Member,
        ImmutableArray<ImplementationHeatRosterMember> Roster,
        ImmutableArray<AnalyzedMethod> Methods);

    sealed record AnalyzedMethod(
        int MetadataToken,
        bool IsRosterMember,
        bool HasBody);

    sealed record Measured(
        ImplementationProfilesResult.Available Profiles,
        ImplementationProfilePopulationCoverageReceipt Coverage);
}
