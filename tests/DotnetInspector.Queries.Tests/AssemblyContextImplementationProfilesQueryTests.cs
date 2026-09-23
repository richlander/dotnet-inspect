using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextImplementationProfilesQueryTests
{
    [Fact]
    public async Task ExecuteParticipant_PreservesProfilesRelationshipsAndPublicOwners()
    {
        byte[] content = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, content);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        AssemblyImplementationProfileInspection result =
            Assert.IsType<
                AssemblyContextEntry<
                    AssemblyImplementationProfileInspection>.Available>(
                        AssemblyContextImplementationProfilesQuery
                            .ExecuteParticipant(group, participant))
                .Value;

        Assert.Equal(
            InspectionCost.Unbounded,
            AssemblyContextImplementationProfilesQuery.Definition.Cost);
        Assert.True(result.Coverage.WasRequested);
        Assert.True(result.Coverage.HasFullMethodEvidenceScope);
        Assert.Equal(
            result.Profiles.Length,
            result.Coverage.ProfiledEvidenceBodyCount);
        Assert.All(
            result.Profiles,
            member => Assert.Contains(
                result.Coverage.ProfiledEvidenceBodies,
                method => method.MetadataToken
                    == member.Profile.EvidenceMethod.MetadataToken));
        AssemblyImplementationProfileMember wrapper = Assert.Single(
            result.Profiles,
            member =>
                member.Profile.Method.DeclaringType.Name
                    == "ImplementationProfileSample"
                && member.Profile.Method.Name == "Analyze"
                && member.Profile.Method.ParameterTypes.Length == 1
                && member.Profile.Method.ParameterTypes[0].Name == "Int32");
        ImplementationProfilePublicMember owner =
            Assert.Single(wrapper.PublicMembers);
        Assert.EndsWith(
            ".ImplementationProfileSample",
            owner.TypeDefinitionId,
            StringComparison.Ordinal);
        Assert.Equal("Analyze", owner.Member);
        Assert.Contains(
            wrapper.Profile.EvidenceMethod.MetadataToken,
            owner.BodyTokens);
        Assert.StartsWith(
            "Analyze~",
            owner.StableSelector,
            StringComparison.Ordinal);
        Assert.Contains(
            result.OverloadRelationships,
            relationship =>
                relationship.Caller
                    == wrapper.Profile.Method
                && relationship.Callee.ParameterTypes.Length == 2);
    }

    [Fact]
    public async Task ExecuteParticipant_KeepsGeneratedAndPrivateBodiesDistinct()
    {
        byte[] content = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, content);

        AssemblyImplementationProfileInspection result =
            Assert.IsType<
                AssemblyContextEntry<
                    AssemblyImplementationProfileInspection>.Available>(
                        AssemblyContextImplementationProfilesQuery
                            .ExecuteParticipant(
                                group,
                                Assert.Single(group.Participants)))
                .Value;

        AssemblyImplementationProfileMember generated = Assert.Single(
            result.Profiles,
            member =>
                member.Profile.Method.DeclaringType.Name
                    == "ImplementationProfileSample"
                && member.Profile.Method.Name == "AnalyzeAsync"
                && member.Profile.Method.ParameterTypes.Length == 1
                && member.Profile.Method.ParameterTypes[0].Name == "Int32"
                && member.Profile.Async);
        Assert.NotEqual(
            generated.Profile.Method.MetadataToken,
            generated.Profile.EvidenceMethod.MetadataToken);
        ImplementationProfilePublicMember generatedOwner =
            Assert.Single(generated.PublicMembers);
        Assert.Equal("AnalyzeAsync", generatedOwner.Member);
        Assert.Contains(
            generated.Profile.EvidenceMethod.MetadataToken,
            generatedOwner.BodyTokens);
        Assert.Contains(
            generated.Profile.Method.MetadataToken,
            generatedOwner.BodyTokens);

        AssemblyImplementationProfileMember hidden = Assert.Single(
            result.Profiles,
            member =>
                member.Profile.Method.DeclaringType.Name
                    == "ImplementationProfileSample"
                && member.Profile.Method.Name == "Hidden");
        Assert.Empty(hidden.PublicMembers);
    }

    [Fact]
    public async Task ExecuteParticipant_DoesNotOpenUnselectedParticipant()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath()));
        using var reader = new PEReader(image);
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        int unrelatedOpenCount = 0;
        var policy = new MissingBindingPolicy();
        var unrelated = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity with { Name = "WrongIdentity" },
                path: null,
                () =>
                {
                    unrelatedOpenCount++;
                    return new MemoryStream(
                        ImmutableCollectionsMarshal.AsArray(image)!,
                        writable: false);
                },
                AssemblyResolutionProvenance.Local(
                    "unselected-implementation-profile-fixture")),
            policy);
        var selected = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "selected-implementation-profile-fixture")),
            policy);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [unrelated, selected]);

        var available = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileInspection>.Available>(
                    AssemblyContextImplementationProfilesQuery
                        .ExecuteParticipant(group, selected));

        Assert.Same(
            selected.Assembly.Registration,
            available.Subject.Registration);
        Assert.Equal(0, unrelatedOpenCount);
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        byte[] content)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(content);
        using var reader = new PEReader(image);
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        var participant = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "implementation-profile-fixture")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
        }
    }
}
