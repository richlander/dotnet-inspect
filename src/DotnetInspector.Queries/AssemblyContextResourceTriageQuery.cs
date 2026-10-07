using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Inspector.Findings;

namespace DotnetInspector.Queries;

public sealed record ResourceTriagePublicMember(string Type, string Member, string StableSelector);

public sealed record AssemblyResourceTriageResult(
    ResourceTriageResult Triage,
    ImmutableDictionary<int, ResourceTriagePublicMember> PublicMembers,
    ImmutableArray<ApiSurfaceInspectionFailure> ApiSurfaceInspectionFailures);

/// <summary>Executes the shipped ArrayPool census for one admitted implementation participant.</summary>
public static class AssemblyContextResourceTriageQuery
{
    /// <summary>
    /// Borrows an admitted package context and runtime context for one bounded
    /// combined analysis. Runtime acquisition remains the host's explicit work.
    /// </summary>
    public static async Task<AssemblyContextEntry<AssemblyResourceTriageResult>> ExecuteParticipantWithRuntimeAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextGroup runtimeGroup,
        AssemblyContextParticipant runtimeParticipant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(runtimeGroup);
        ArgumentNullException.ThrowIfNull(runtimeParticipant);
        if (!group.Participants.Contains(participant)
            || !runtimeGroup.Participants.Contains(runtimeParticipant))
            throw new ArgumentException("Selected participants must belong to their admitted contexts.");
        AssemblyBindingSelection selection = runtimeParticipant.BindingPolicy.Select(
            new AssemblyBindingRequest(AssemblyBindingTarget.CoreLibrary(),
                AssemblyBindingOrigin.FromAssembly(runtimeParticipant.Assembly),
                AssemblyResolutionScope.Platform)).Selection;
        if (selection is not AssemblyBindingSelection.Selected selected)
            throw new InvalidOperationException("The admitted runtime context has no selected intrinsic core library.");

        var assemblies = ImmutableArray.CreateBuilder<ResolvedAssemblyReference>();
        var registrations = new HashSet<AssemblyAcquisitionRegistration>(ReferenceEqualityComparer.Instance);
        foreach (AssemblyContextGroup source in new[] { group, runtimeGroup })
        {
            foreach (AssemblyContextParticipant candidate in source.Participants)
            {
                if (!registrations.Add(candidate.Assembly.Registration))
                    continue;
                var retained = source.RetainAssemblyReference(candidate.Assembly);
                if (retained is not AssemblyImageAccessResult<ResolvedAssemblyReference>.Available available)
                    throw new InvalidOperationException("An admitted Resource Triage context image could not be retained.");
                assemblies.Add(available.Value);
            }
        }
        ImmutableArray<ResolvedAssemblyReference> admitted = assemblies.ToImmutable();
        ResolvedAssemblyReference coreLibrary = admitted.Single(assembly =>
            ReferenceEquals(assembly.Registration, selected.Assembly.Registration));
        var policy = new PackageAssemblyContextRoles.RoleBindingPolicy(admitted, coreLibrary);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup combined = workspace.CreateAssemblyContextGroup(
            admitted.Select(assembly => new AssemblyContextParticipant(assembly, policy)),
            new AssemblyContextGroupOptions { MaxRetainedImageBytes = 64L * 1024 * 1024 });
        AssemblyContextParticipant target = combined.Participants.Single(candidate =>
            ReferenceEquals(candidate.Assembly.Registration, participant.Assembly.Registration));
        return ExecuteParticipant(combined, target);
    }

    public static AssemblyContextEntry<AssemblyResourceTriageResult> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        AssemblyContextEntry<ApiSurface> surface = AssemblyContextQueryExecutor.ExecuteParticipant(
            group, participant,
            static session => session.CompatibilityApiSurface(ApiSurfaceExtractionScope.Public));
        return surface switch
        {
            AssemblyContextEntry<ApiSurface>.Rejected rejected =>
                new AssemblyContextEntry<AssemblyResourceTriageResult>.Rejected(rejected.Subject, rejected.Failure),
            AssemblyContextEntry<ApiSurface>.Failed failed =>
                new AssemblyContextEntry<AssemblyResourceTriageResult>.Failed(failed.Subject, failed.Error),
            AssemblyContextEntry<ApiSurface>.Available available =>
                AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
                    group, participant,
                    (subject, snapshot) =>
                    {
                        var resolver = AssemblyContextAnalysisSource.Resolver(group, subject);
                        LibraryBodyAnalysisExecution execution = LibraryBodyAnalysisService.ExecuteImage(
                            AssemblyContextAnalysisSource.Name(subject), snapshot.Content,
                            LibraryBodyAnalysisRequest.CreateResourceLifecycle(ArrayPoolResourceEffectModel.Create()),
                            resolver, group.CreateSnapshotBackedReference(participant.Assembly));
                        ResourceTriageResult triage = ResourceTriageQuery.Execute(
                            execution.ResourceLifecycle,
                            new FindingSubject(subject.Identity.ToString(), subject.Identity.Name));
                        var members = ImmutableDictionary.CreateBuilder<int, ResourceTriagePublicMember>();
                        foreach (ApiType type in available.Value.Types)
                        {
                            foreach (ApiMember member in type.Members)
                            {
                                var reference = new ResourceTriagePublicMember(
                                    AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type), member.Name,
                                    ApiMemberIdentity.GetMemberAnchor(type, member).StableSelector);
                                foreach (var selector in CallGraphMemberResolver.CreateBodySelectors(type, member))
                                    members.TryAdd(selector.BodyToken, reference);
                            }
                        }
                        resolver.ValidateForPublication();
                        return new AssemblyResourceTriageResult(triage, members.ToImmutable(),
                            [.. available.Value.InspectionFailures]);
                    }),
            _ => throw new InvalidOperationException("Unknown API surface outcome."),
        };
    }
}
