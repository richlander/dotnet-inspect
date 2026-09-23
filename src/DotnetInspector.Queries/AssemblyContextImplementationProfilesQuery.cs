using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed record ImplementationProfilePublicMember(
    string TypeDefinitionId,
    string Member,
    string StableSelector,
    ImmutableArray<int> BodyTokens);

public sealed record AssemblyImplementationProfileMember(
    MethodImplementationProfile Profile,
    ImmutableArray<ImplementationProfilePublicMember> PublicMembers);

public sealed record AssemblyImplementationProfileInspection(
    ImmutableArray<AssemblyImplementationProfileMember> Profiles,
    ImplementationProfilePopulationCoverageReceipt Coverage,
    ImmutableArray<OverloadCallRelationship> OverloadRelationships,
    ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<ApiSurfaceInspectionFailure> ApiSurfaceInspectionFailures);

/// <summary>
/// Collects implementation profiles for one assembly-context participant and
/// joins physical bodies to product-issued public API identities.
/// </summary>
public static class AssemblyContextImplementationProfilesQuery
{
    public static InspectionQuery<
        AssemblyContextEntry<AssemblyImplementationProfileInspection>>
        Definition { get; } =
        new(
            "Assembly context implementation profiles",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<AssemblyImplementationProfileInspection>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);

        AssemblyContextEntry<ImplementationProfilePublicMembers>
            publicMembers =
                AssemblyContextQueryExecutor.ExecuteParticipant(
                    group,
                    participant,
                    ProjectPublicMembers);
        return publicMembers switch
        {
            AssemblyContextEntry<
                ImplementationProfilePublicMembers>.Rejected rejected =>
                new AssemblyContextEntry<
                    AssemblyImplementationProfileInspection>.Rejected(
                        rejected.Subject,
                        rejected.Failure),
            AssemblyContextEntry<
                ImplementationProfilePublicMembers>.Failed failed =>
                new AssemblyContextEntry<
                    AssemblyImplementationProfileInspection>.Failed(
                        failed.Subject,
                        failed.Error),
            AssemblyContextEntry<
                ImplementationProfilePublicMembers>.Available available =>
                AnalyzeParticipant(
                    group,
                    participant,
                    available),
            _ => throw new InvalidOperationException(
                $"Unknown public-member entry '{publicMembers.GetType().Name}'."),
        };
    }

    static AssemblyContextEntry<AssemblyImplementationProfileInspection>
        AnalyzeParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyContextEntry<
                ImplementationProfilePublicMembers>.Available publicMembers) =>
        AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) =>
            {
                EnsureSameSubject(subject, publicMembers.Subject);
                return Analyze(
                    group,
                    subject,
                    snapshot,
                    publicMembers.Value);
            });

    static AssemblyImplementationProfileInspection Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        ImplementationProfilePublicMembers publicMembers)
    {
        var resolver = AssemblyContextAnalysisSource.Resolver(
            group,
            subject);
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecuteImage(
                AssemblyContextAnalysisSource.Name(subject),
                snapshot.Content,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.ImplementationProfiles),
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
                            + "for implementation-profile inspection."),
                ImplementationProfilesResult.Failed failed =>
                    throw new InspectionQueryException(
                        $"Implementation-profile inspection failed for "
                            + $"'{subject.Identity.Name}'.",
                        failed.Error),
                _ => throw new InvalidOperationException(
                    "Unknown implementation-profile query result."),
            };

        ImmutableArray<AssemblyImplementationProfileMember>
            attributedProfiles = AttributeProfiles(
                profiles.Profiles,
                publicMembers.ByDeclaredBodyToken);
        var result = new AssemblyImplementationProfileInspection(
            attributedProfiles,
            analysis.ImplementationProfiles.Coverage,
            profiles.OverloadRelationships,
            profiles.GeneratedFrameworkTypes,
            profiles.Diagnostics,
            publicMembers.InspectionFailures);
        resolver.ValidateForPublication();
        return result;
    }

    static ImplementationProfilePublicMembers ProjectPublicMembers(
        AssemblyInspectionSession session)
    {
        ApiSurface surface =
            session.ApiSurface(ApiSurfaceExtractionScope.Public);
        var byBodyToken =
            new Dictionary<
                int,
                List<ImplementationProfilePublicMember>>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                ImmutableArray<CallGraphMemberBodySelector> selectors =
                CallGraphMemberResolver.CreateBodySelectors(type, member);
                if (selectors.Length == 0)
                    continue;

                var publicMember =
                    new ImplementationProfilePublicMember(
                        AssemblyContextApiSurfaceQuery
                            .MetadataTypeIdentity(type),
                        member.Name,
                        ApiMemberIdentity
                            .GetMemberAnchor(type, member)
                            .StableSelector,
                        []);
                foreach (int bodyToken in selectors
                    .Select(selector => selector.BodyToken)
                    .Distinct())
                {
                    if (!byBodyToken.TryGetValue(
                            bodyToken,
                            out List<
                                ImplementationProfilePublicMember>? owners))
                    {
                        owners = [];
                        byBodyToken.Add(bodyToken, owners);
                    }
                    PublicMemberKey key =
                        PublicMemberKey.Create(publicMember);
                    if (!owners.Any(owner =>
                            PublicMemberKey.Create(owner) == key))
                    {
                        owners.Add(publicMember);
                    }
                }
            }
        }

        return new ImplementationProfilePublicMembers(
            byBodyToken.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .OrderBy(
                        member => member.TypeDefinitionId,
                        StringComparer.Ordinal)
                    .ThenBy(
                        member => member.StableSelector,
                        StringComparer.Ordinal)
                    .ToImmutableArray()),
            [.. surface.InspectionFailures]);
    }

    static ImmutableArray<AssemblyImplementationProfileMember>
        AttributeProfiles(
            ImmutableArray<MethodImplementationProfile> profiles,
            IReadOnlyDictionary<
                int,
                ImmutableArray<ImplementationProfilePublicMember>>
                    byDeclaredBodyToken)
    {
        var attributed = profiles
            .Select(profile => new AttributedProfile(
                profile,
                byDeclaredBodyToken.GetValueOrDefault(
                    profile.Method.MetadataToken,
                    [])))
            .ToImmutableArray();
        Dictionary<PublicMemberKey, ImmutableArray<int>> bodyTokens =
            attributed
                .SelectMany(item =>
                    item.PublicMembers.Select(publicMember =>
                        new
                        {
                            Key = PublicMemberKey.Create(publicMember),
                            item.Profile.EvidenceMethod.MetadataToken,
                        }))
                .GroupBy(item => item.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(item => item.MetadataToken)
                        .Distinct()
                        .Order()
                        .ToImmutableArray());

        return
        [
            .. attributed.Select(item =>
                new AssemblyImplementationProfileMember(
                    item.Profile,
                    [
                        .. item.PublicMembers.Select(publicMember =>
                            publicMember with
                            {
                                BodyTokens = bodyTokens[
                                    PublicMemberKey.Create(publicMember)],
                            }),
                    ])),
        ];
    }

    static void EnsureSameSubject(
        AssemblyContextSubject actual,
        AssemblyContextSubject expected)
    {
        if (!ReferenceEquals(actual.Registration, expected.Registration))
        {
            throw new InspectionQueryException(
                "Implementation-profile public members belong to another "
                    + "assembly-context participant.");
        }
    }

    sealed record ImplementationProfilePublicMembers(
        IReadOnlyDictionary<
            int,
            ImmutableArray<ImplementationProfilePublicMember>>
                ByDeclaredBodyToken,
        ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures);

    sealed record AttributedProfile(
        MethodImplementationProfile Profile,
        ImmutableArray<ImplementationProfilePublicMember> PublicMembers);

    readonly record struct PublicMemberKey(
        string TypeDefinitionId,
        string StableSelector)
    {
        public static PublicMemberKey Create(
            ImplementationProfilePublicMember member) =>
            new(member.TypeDefinitionId, member.StableSelector);
    }
}
