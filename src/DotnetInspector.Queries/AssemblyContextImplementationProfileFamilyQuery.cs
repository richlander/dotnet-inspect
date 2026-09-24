using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed record ImplementationProfileFamilySelection
{
    public ImplementationProfileFamilySelection(
        string typeDefinitionId,
        IEnumerable<string> stableSelectors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeDefinitionId);
        ArgumentNullException.ThrowIfNull(stableSelectors);

        ImmutableArray<string> selectors =
            [.. stableSelectors];
        if (selectors.Length < 2)
        {
            throw new ArgumentException(
                "An implementation-profile family requires at least two "
                    + "stable Member selectors.",
                nameof(stableSelectors));
        }
        if (selectors.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Implementation-profile family selectors cannot be blank.",
                nameof(stableSelectors));
        }
        if (selectors.Distinct(StringComparer.Ordinal).Count()
            != selectors.Length)
        {
            throw new ArgumentException(
                "Implementation-profile family selectors must be distinct.",
                nameof(stableSelectors));
        }

        TypeDefinitionId = typeDefinitionId;
        StableSelectors = selectors;
    }

    public string TypeDefinitionId { get; }
    public ImmutableArray<string> StableSelectors { get; }
}

public sealed record AssemblyImplementationProfileFamilyInspection(
    ImmutableArray<ImplementationProfilePublicMember> Members,
    ImmutableArray<AssemblyImplementationProfileMember> Profiles,
    ImplementationProfilePopulationCoverageReceipt Coverage,
    ImmutableArray<OverloadCallRelationship> OverloadRelationships,
    ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<ApiSurfaceInspectionFailure> ApiSurfaceInspectionFailures);

/// <summary>
/// Collects implementation profiles for one exact public method-overload
/// family selected by product-issued API identities.
/// </summary>
public static class AssemblyContextImplementationProfileFamilyQuery
{
    public static InspectionQuery<
        AssemblyContextEntry<AssemblyImplementationProfileFamilyInspection>>
        Definition { get; } =
        new(
            "Assembly context implementation profile family",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<
        AssemblyImplementationProfileFamilyInspection> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            ImplementationProfileFamilySelection selection)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(selection);

        AssemblyContextEntry<SelectedFamily> selected =
            AssemblyContextQueryExecutor.ExecuteParticipant(
                group,
                participant,
                session => SelectFamily(session, selection));
        return selected switch
        {
            AssemblyContextEntry<SelectedFamily>.Rejected rejected =>
                new AssemblyContextEntry<
                    AssemblyImplementationProfileFamilyInspection>.Rejected(
                        rejected.Subject,
                        rejected.Failure),
            AssemblyContextEntry<SelectedFamily>.Failed failed =>
                new AssemblyContextEntry<
                    AssemblyImplementationProfileFamilyInspection>.Failed(
                        failed.Subject,
                        failed.Error),
            AssemblyContextEntry<SelectedFamily>.Available available =>
                AnalyzeParticipant(
                    group,
                    participant,
                    available),
            _ => throw new InvalidOperationException(
                $"Unknown implementation-profile family selection outcome "
                    + $"'{selected.GetType().Name}'."),
        };
    }

    static AssemblyContextEntry<AssemblyImplementationProfileFamilyInspection>
        AnalyzeParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyContextEntry<SelectedFamily>.Available selected) =>
        AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) =>
            {
                EnsureSameSubject(subject, selected.Subject);
                return Analyze(
                    group,
                    subject,
                    snapshot,
                    selected.Value);
            });

    static AssemblyImplementationProfileFamilyInspection Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        SelectedFamily family)
    {
        var resolver = AssemblyContextAnalysisSource.Resolver(
            group,
            subject);
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecuteImage(
                AssemblyContextAnalysisSource.Name(subject),
                snapshot.Content,
                LibraryBodyAnalysisRequest
                    .CreateCompleteImplementationProfile(
                    family.DeclaredBodyTokens),
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
                            + "for implementation-profile family inspection."),
                ImplementationProfilesResult.Failed failed =>
                    throw new InspectionQueryException(
                        $"Implementation-profile family inspection failed for "
                            + $"'{subject.Identity.Name}'.",
                        failed.Error),
                _ => throw new InvalidOperationException(
                    "Unknown implementation-profile query result."),
            };

        ImmutableArray<AssemblyImplementationProfileMember>
            attributedProfiles =
        [
            .. AssemblyContextImplementationProfilesQuery
                .AttributeProfiles(
                    profiles.Profiles,
                    family.ByDeclaredBodyToken)
                .Where(profile => !profile.PublicMembers.IsEmpty),
        ];
        ImmutableHashSet<int> declaredTokens =
            family.DeclaredBodyTokens;
        ImmutableArray<OverloadCallRelationship> relationships =
        [
            .. profiles.OverloadRelationships.Where(
                relationship =>
                    declaredTokens.Contains(
                        relationship.Caller.MetadataToken)
                    && declaredTokens.Contains(
                        relationship.Callee.MetadataToken)),
        ];
        ImmutableArray<ImplementationProfilePublicMember> members =
            CompleteMembers(
                family.Members,
                attributedProfiles);
        ImplementationProfilePopulationCoverageReceipt coverage =
            ScopeCoverage(
                analysis.ImplementationProfiles.Coverage,
                attributedProfiles,
                declaredTokens);
        var result =
            new AssemblyImplementationProfileFamilyInspection(
                members,
                attributedProfiles,
                coverage,
                relationships,
                profiles.GeneratedFrameworkTypes,
                coverage.Diagnostics,
                family.InspectionFailures);
        resolver.ValidateForPublication();
        return result;
    }

    static SelectedFamily SelectFamily(
        AssemblyInspectionSession session,
        ImplementationProfileFamilySelection selection)
    {
        ApiSurface surface =
            session.ApiSurface(ApiSurfaceExtractionScope.Public);
        ApiType[] matchingTypes =
        [
            .. surface.Types.Where(type =>
                AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type)
                    == selection.TypeDefinitionId),
        ];
        if (matchingTypes.Length != 1)
        {
            throw new InspectionQueryException(
                matchingTypes.Length == 0
                    ? $"Public API Type '{selection.TypeDefinitionId}' "
                        + "was not found."
                    : $"Public API Type '{selection.TypeDefinitionId}' "
                        + "is ambiguous.");
        }

        ApiType type = matchingTypes[0];
        ImmutableArray<FamilyCandidate> candidates =
        [
            .. type.Members.Select(member =>
                Candidate(type, member)),
        ];
        var selected =
            ImmutableArray.CreateBuilder<FamilyCandidate>(
                selection.StableSelectors.Length);
        foreach (string selector in selection.StableSelectors)
        {
            FamilyCandidate[] matches =
            [
                .. candidates.Where(candidate =>
                    candidate.PublicMember.StableSelector == selector),
            ];
            if (matches.Length != 1)
            {
                throw new InspectionQueryException(
                    matches.Length == 0
                        ? $"Public API Member selector '{selector}' was not "
                            + $"found on '{selection.TypeDefinitionId}'."
                        : $"Public API Member selector '{selector}' is "
                            + $"ambiguous on '{selection.TypeDefinitionId}'.");
            }
            selected.Add(matches[0]);
        }

        ImmutableArray<FamilyCandidate> selectedMembers =
            selected.MoveToImmutable();
        if (selectedMembers.Any(candidate => !candidate.IsMethod))
        {
            throw new InspectionQueryException(
                "Implementation-profile family selection accepts public "
                    + "methods only.");
        }

        string[] names =
        [
            .. selectedMembers
                .Select(candidate => candidate.PublicMember.Member)
                .Distinct(StringComparer.Ordinal),
        ];
        if (names.Length != 1)
        {
            throw new InspectionQueryException(
                "Implementation-profile selectors must name one method "
                    + "overload family.");
        }

        ImmutableArray<FamilyCandidate> family =
        [
            .. candidates.Where(candidate =>
                candidate.IsMethod
                && candidate.PublicMember.Member == names[0]),
        ];
        if (family.Length < 2)
        {
            throw new InspectionQueryException(
                $"Public method '{selection.TypeDefinitionId}.{names[0]}' "
                    + "does not have multiple overloads.");
        }

        HashSet<string> requested =
            selection.StableSelectors.ToHashSet(StringComparer.Ordinal);
        if (family.Any(candidate =>
                !requested.Contains(
                    candidate.PublicMember.StableSelector))
            || requested.Count != family.Length)
        {
            throw new InspectionQueryException(
                $"Implementation-profile selection for "
                    + $"'{selection.TypeDefinitionId}.{names[0]}' must name "
                    + "the complete public overload family.");
        }

        var byDeclaredBodyToken =
            new Dictionary<
                int,
                List<ImplementationProfilePublicMember>>();
        foreach (FamilyCandidate candidate in family)
        {
            foreach (int bodyToken in candidate.DeclaredBodyTokens)
            {
                if (!byDeclaredBodyToken.TryGetValue(
                        bodyToken,
                        out List<
                            ImplementationProfilePublicMember>? owners))
                {
                    owners = [];
                    byDeclaredBodyToken.Add(bodyToken, owners);
                }
                owners.Add(candidate.PublicMember);
            }
        }

        HashSet<int> familyTokens =
        [
            .. family.SelectMany(candidate =>
                candidate.DeclaredBodyTokens),
        ];
        if (type.MetadataToken is { } typeToken)
            familyTokens.Add(typeToken);
        foreach (FamilyCandidate candidate in family)
        {
            if (candidate.MemberToken is { } memberToken)
                familyTokens.Add(memberToken);
            if (candidate.DeclarationToken is { } declarationToken)
                familyTokens.Add(declarationToken);
        }

        return new SelectedFamily(
            [.. family.Select(candidate => candidate.PublicMember)],
            byDeclaredBodyToken.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutableArray()),
            [
                .. family.SelectMany(candidate =>
                    candidate.DeclaredBodyTokens),
            ],
            [
                .. surface.InspectionFailures.Where(
                    failure => familyTokens.Contains(
                        failure.SubjectToken)),
            ]);
    }

    static FamilyCandidate Candidate(
        ApiType type,
        ApiMember member)
    {
        ImmutableArray<int> bodyTokens =
        [
            .. CallGraphMemberResolver
                .CreateBodySelectors(type, member)
                .Select(selector => selector.BodyToken)
                .Distinct()
                .Order(),
        ];
        return new(
            member,
            new ImplementationProfilePublicMember(
                AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
                member.Name,
                ApiMemberIdentity
                    .GetMemberAnchor(type, member)
                    .StableSelector,
                []),
            bodyTokens,
            member.Kind is "method" or "extension-method",
            member.MetadataToken,
            member.DeclarationMetadataToken);
    }

    static ImmutableArray<ImplementationProfilePublicMember>
        CompleteMembers(
            ImmutableArray<ImplementationProfilePublicMember> members,
            ImmutableArray<AssemblyImplementationProfileMember> profiles)
    {
        Dictionary<PublicMemberKey, ImmutableArray<int>> bodyTokens =
            profiles
                .SelectMany(profile =>
                    profile.PublicMembers.Select(member =>
                        new
                        {
                            Key = PublicMemberKey.Create(member),
                            member.BodyTokens,
                        }))
                .GroupBy(item => item.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .SelectMany(item => item.BodyTokens)
                        .Distinct()
                        .Order()
                        .ToImmutableArray());
        return
        [
            .. members.Select(member =>
                member with
                {
                    BodyTokens = bodyTokens.GetValueOrDefault(
                        PublicMemberKey.Create(member),
                        []),
                }),
        ];
    }

    static ImplementationProfilePopulationCoverageReceipt ScopeCoverage(
        ImplementationProfilePopulationCoverageReceipt coverage,
        ImmutableArray<AssemblyImplementationProfileMember> profiles,
        IReadOnlySet<int> declaredTokens)
    {
        ImmutableArray<MethodIdentity> profiledBodies =
        [
            .. profiles
                .Select(profile => profile.Profile.EvidenceMethod)
                .Distinct()
                .OrderBy(method => method.MetadataToken),
        ];
        ImmutableArray<ImplementationProfileUnavailableBody>
            unavailableBodies =
        [
            .. coverage.UnavailableBodies.Where(body =>
                declaredTokens.Contains(body.MethodToken)
                || body.Diagnostic?.SourceMethodToken is { } sourceToken
                    && declaredTokens.Contains(sourceToken)),
        ];
        HashSet<int> scopedTokens =
        [
            .. declaredTokens,
            .. profiledBodies.Select(
                method => method.MetadataToken),
            .. unavailableBodies.Select(body => body.MethodToken),
        ];
        return coverage with
        {
            DeclaredMethods =
            [
                .. coverage.DeclaredMethods.Where(method =>
                    declaredTokens.Contains(method.MetadataToken)),
            ],
            ManagedMethodBodies =
            [
                .. coverage.ManagedMethodBodies.Where(method =>
                    scopedTokens.Contains(method.MetadataToken)),
            ],
            ProfiledEvidenceBodies = profiledBodies,
            UnavailableBodies = unavailableBodies,
            Diagnostics =
            [
                .. coverage.Diagnostics.Where(diagnostic =>
                    scopedTokens.Contains(diagnostic.MethodToken)
                    || diagnostic.SourceMethodToken is { } sourceToken
                        && declaredTokens.Contains(sourceToken)),
            ],
        };
    }

    static void EnsureSameSubject(
        AssemblyContextSubject actual,
        AssemblyContextSubject expected)
    {
        if (!ReferenceEquals(actual.Registration, expected.Registration))
        {
            throw new InspectionQueryException(
                "Implementation-profile family selection belongs to another "
                    + "assembly-context participant.");
        }
    }

    sealed record SelectedFamily(
        ImmutableArray<ImplementationProfilePublicMember> Members,
        IReadOnlyDictionary<
            int,
            ImmutableArray<ImplementationProfilePublicMember>>
                ByDeclaredBodyToken,
        ImmutableHashSet<int> DeclaredBodyTokens,
        ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

    sealed record FamilyCandidate(
        ApiMember Member,
        ImplementationProfilePublicMember PublicMember,
        ImmutableArray<int> DeclaredBodyTokens,
        bool IsMethod,
        int? MemberToken,
        int? DeclarationToken);

    readonly record struct PublicMemberKey(
        string TypeDefinitionId,
        string StableSelector)
    {
        public static PublicMemberKey Create(
            ImplementationProfilePublicMember member) =>
            new(member.TypeDefinitionId, member.StableSelector);
    }
}
