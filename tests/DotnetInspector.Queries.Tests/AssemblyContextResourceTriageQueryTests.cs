using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextResourceTriageQueryTests
{
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ExecuteParticipant_PreservesFindingsAndLimitationsWithoutOpeningNeighbors()
    {
        string path = FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        byte[] bytes = File.ReadAllBytes(path);
        using var reader = new PEReader(ImmutableArray.Create(bytes));
        var identity = AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path)
        {
            PreferImplementationAssemblies = true,
        });
        var participant = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(identity, path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local("resource-triage-fixture")), resolver);
        int neighborOpens = 0;
        var neighbor = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(identity, path: null,
                () => { neighborOpens++; throw new IOException("Unrelated participant must not be opened."); },
                AssemblyResolutionProvenance.Local("unrelated-fixture")), resolver);
        await using var workspace = new InspectionWorkspace();
        var participants = new List<AssemblyContextParticipant> { participant, neighbor };
        var pending = new Queue<ResolvedAssemblyReference>();
        pending.Enqueue(participant.Assembly);
        var seen = new HashSet<string>(StringComparer.Ordinal) { identity.Name };
        while (pending.TryDequeue(out var assembly))
        {
            using Stream stream = assembly.OpenRead();
            using var dependencyReader = new PEReader(stream);
            var metadata = dependencyReader.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = AssemblyReferenceIdentity.From(metadata, handle);
                var resolved = resolver.Resolve(reference, AssemblyResolutionScope.Any);
                if (resolved is not null && seen.Add(resolved.Identity.Name))
                {
                    participants.Add(new AssemblyContextParticipant(resolved, resolver));
                    pending.Enqueue(resolved);
                }
            }
        }
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(participants);

        var entry = Assert.IsType<AssemblyContextEntry<AssemblyResourceTriageResult>.Available>(
            AssemblyContextResourceTriageQuery.ExecuteParticipant(group, participant));
        Assert.False(entry.Value.Triage is ResourceTriageResult.Failed,
            (entry.Value.Triage as ResourceTriageResult.Failed)?.Error.Reason);
        var partial = Assert.IsType<ResourceTriageResult.Incomplete>(entry.Value.Triage);
        Assert.NotEmpty(partial.Limitations);
        var read = Assert.Single(partial.Assessments,
            candidate => candidate.Source.Payload.Method.Name == "RentReadBeforeReturn");
        Assert.Equal(ResourceTriageActionability.UntrustedActionable, read.Actionability);
        Assert.Contains(partial.Assessments,
            candidate => candidate.Source.Payload.Method.Name == "RentAcrossSiblingTypedThenCatchAllCleanup");
        Assert.DoesNotContain(partial.Assessments,
            candidate => candidate.Source.Payload.Method.Name == "RentAcrossCatchAllCleanup");
        Assert.True(entry.Value.PublicMembers.ContainsKey(read.Source.Payload.Method.MetadataToken));
        Assert.Equal(0, neighborOpens);

        var execution = LibraryBodyAnalysisService.ExecutePath(path,
            LibraryBodyAnalysisRequest.CreateResourceLifecycle(ArrayPoolResourceEffectModel.Create()), resolver);
        var baseline = Assert.IsType<ResourceTriageResult.Incomplete>(ResourceTriageQuery.Execute(
            execution.ResourceLifecycle,
            new Inspector.Findings.FindingSubject(identity.ToString(), identity.Name)));
        Assert.Equal(baseline.Assessments.Select(candidate => candidate.CandidateId),
            partial.Assessments.Select(candidate => candidate.CandidateId));
    }
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ExecuteParticipantWithRuntime_ResolvesArrayPoolFromAdmittedCoreLibrary()
    {
        string path = FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path)
        {
            PreferImplementationAssemblies = true,
        });
        var root = ResolvedAssemblyReference.CreateFromPath(path,
            AssemblyResolutionProvenance.Local("resource-triage-fixture"));
        var core = Assert.IsType<AssemblyBindingSelection.Selected>(resolver.Select(
            new AssemblyBindingRequest(AssemblyBindingTarget.CoreLibrary(),
                AssemblyBindingOrigin.FromAssembly(root), AssemblyResolutionScope.Platform)).Selection).Assembly;
        core = ResolvedAssemblyReference.CreateFromPath(core.Path!,
            AssemblyResolutionProvenance.Platform("Microsoft.NETCore.App", "11.0.0", "fixture"));
        var packagePolicy = new PackageAssemblyContextRoles.RoleBindingPolicy([root], null);
        var runtimeAssemblies = new List<ResolvedAssemblyReference> { core };
        var pending = new Queue<ResolvedAssemblyReference>();
        pending.Enqueue(root);
        pending.Enqueue(core);
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.Identity.Name, core.Identity.Name };
        while (pending.TryDequeue(out var assembly))
        {
            using Stream stream = assembly.OpenRead();
            using var dependencyReader = new PEReader(stream);
            var dependencyMetadata = dependencyReader.GetMetadataReader();
            foreach (var handle in dependencyMetadata.AssemblyReferences)
            {
                var reference = AssemblyReferenceIdentity.From(dependencyMetadata, handle);
                var resolved = resolver.Resolve(reference, AssemblyResolutionScope.Any);
                if (resolved is not null && seen.Add(resolved.Identity.Name))
                {
                    var admitted = ResolvedAssemblyReference.CreateFromPath(resolved.Path!,
                        AssemblyResolutionProvenance.Platform("Microsoft.NETCore.App", "11.0.0", "fixture"));
                    runtimeAssemblies.Add(admitted);
                    pending.Enqueue(admitted);
                }
            }
        }
        var runtimePolicy = new PackageAssemblyContextRoles.RoleBindingPolicy([.. runtimeAssemblies], core);
        await using var workspace = new InspectionWorkspace();
        using var package = workspace.CreateAssemblyContextGroup([new AssemblyContextParticipant(root, packagePolicy)]);
        // Merely acquiring CoreLib does not admit the forwarding/runtime population.
        var sparsePolicy = new PackageAssemblyContextRoles.RoleBindingPolicy([core], core);
        using (var sparseRuntime = workspace.CreateAssemblyContextGroup(
            [new AssemblyContextParticipant(core, sparsePolicy)]))
        {
            var sparse = await AssemblyContextResourceTriageQuery.ExecuteParticipantWithRuntimeAsync(
                package, package.Participants[0], sparseRuntime, sparseRuntime.Participants[0]);
            var sparseEntry = Assert.IsType<AssemblyContextEntry<AssemblyResourceTriageResult>.Available>(sparse);
            Assert.IsType<ResourceTriageResult.Failed>(sparseEntry.Value.Triage);
        }
        using var runtime = workspace.CreateAssemblyContextGroup(runtimeAssemblies.Select(assembly => new AssemblyContextParticipant(assembly, runtimePolicy)));
        var result = await AssemblyContextResourceTriageQuery.ExecuteParticipantWithRuntimeAsync(
                package, package.Participants[0], runtime, runtime.Participants[0]);
        Assert.False(result is AssemblyContextEntry<AssemblyResourceTriageResult>.Failed,
            (result as AssemblyContextEntry<AssemblyResourceTriageResult>.Failed)?.Error.Message);
        var entry = Assert.IsType<AssemblyContextEntry<AssemblyResourceTriageResult>.Available>(result);
        Assert.False(entry.Value.Triage is ResourceTriageResult.Failed,
            (entry.Value.Triage as ResourceTriageResult.Failed)?.Error.Reason);
        var partial = Assert.IsType<ResourceTriageResult.Incomplete>(entry.Value.Triage);
        Assert.Contains(partial.Assessments,
            candidate => candidate.Source.Payload.Method.Name == "RentReadBeforeReturn");
    }

}
