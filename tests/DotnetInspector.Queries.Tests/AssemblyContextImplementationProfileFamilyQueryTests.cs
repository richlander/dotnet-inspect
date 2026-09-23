using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextImplementationProfileFamilyQueryTests
{
    [Fact]
    public void Selection_RequiresMultipleDistinctSelectors()
    {
        Assert.Throws<ArgumentException>(
            () => new ImplementationProfileFamilySelection(
                "Example.Type",
                ["Route~one"]));
        Assert.Throws<ArgumentException>(
            () => new ImplementationProfileFamilySelection(
                "Example.Type",
                ["Route~one", "Route~one"]));
    }

    [Fact]
    public async Task ExecuteParticipant_ScopesAnalysisToExactFamily()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                participant,
                "ImplementationProfileSample",
                "Analyze");

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        Assert.Equal(
            InspectionCost.Unbounded,
            AssemblyContextImplementationProfileFamilyQuery.Definition.Cost);
        Assert.Equal(4, result.Members.Length);
        Assert.True(result.Coverage.WasRequested);
        Assert.False(result.Coverage.HasFullMethodEvidenceScope);
        Assert.Equal(4, result.Coverage.DeclaredMethods.Length);
        Assert.Equal(
            result.Profiles.Length,
            result.Coverage.ProfiledEvidenceBodies.Length);
        Assert.NotEmpty(result.Profiles);
        Assert.All(
            result.Profiles,
            profile =>
            {
                ImplementationProfilePublicMember owner =
                    Assert.Single(profile.PublicMembers);
                Assert.Equal("Analyze", owner.Member);
                Assert.Contains(
                    owner.StableSelector,
                    selection.StableSelectors);
                Assert.False(
                    profile.Profile.Method.Name is "Other" or "Hidden");
            });
        Assert.NotEmpty(result.OverloadRelationships);
        Assert.All(
            result.OverloadRelationships,
            relationship =>
            {
                Assert.Equal("Analyze", relationship.Caller.Name);
                Assert.Equal("Analyze", relationship.Callee.Name);
            });
    }

    [Fact]
    public async Task ExecuteParticipant_PreservesGeneratedBodiesSeparately()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                participant,
                "ImplementationProfileSample",
                "AnalyzeAsync");

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        Assert.Equal(2, result.Members.Length);
        AssemblyImplementationProfileMember generated = Assert.Single(
            result.Profiles,
            member =>
                member.Profile.Method.Name == "AnalyzeAsync"
                && member.Profile.Method.ParameterTypes.Length == 1
                && member.Profile.Method.ParameterTypes[0].Name == "Int32"
                && member.Profile.Async);
        Assert.NotEqual(
            generated.Profile.Method.MetadataToken,
            generated.Profile.EvidenceMethod.MetadataToken);
        ImplementationProfilePublicMember owner =
            Assert.Single(generated.PublicMembers);
        Assert.Contains(
            generated.Profile.Method.MetadataToken,
            owner.BodyTokens);
        Assert.Contains(
            generated.Profile.EvidenceMethod.MetadataToken,
            owner.BodyTokens);
        Assert.All(
            result.Profiles,
            profile => Assert.Equal(
                "AnalyzeAsync",
                Assert.Single(profile.PublicMembers).Member));
    }

    [Fact]
    public async Task ExecuteParticipant_RetainsBodylessOverloadRoster()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                participant,
                "IImplementationProfileBodylessSample",
                "Route");

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        Assert.Equal(2, result.Members.Length);
        Assert.All(
            result.Members,
            member => Assert.Empty(member.BodyTokens));
        Assert.Empty(result.Profiles);
        Assert.Equal(2, result.Coverage.DeclaredMethods.Length);
        Assert.Empty(result.Coverage.ProfiledEvidenceBodies);
        Assert.Empty(result.Coverage.UnavailableBodies);
    }

    [Fact]
    public async Task ExecuteParticipant_RejectsUnknownPartialAndCrossFamilySelection()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection analyze =
            Selection(
                group,
                participant,
                "ImplementationProfileSample",
                "Analyze");
        ImplementationProfileFamilySelection analyzeAsync =
            Selection(
                group,
                participant,
                "ImplementationProfileSample",
                "AnalyzeAsync");

        AssertFailed(
            group,
            participant,
            new(
                analyze.TypeDefinitionId,
                [analyze.StableSelectors[0], "Analyze~missing"]),
            "was not found");
        AssertFailed(
            group,
            participant,
            new(
                analyze.TypeDefinitionId,
                analyze.StableSelectors.Take(2)),
            "complete public overload family");
        AssertFailed(
            group,
            participant,
            new(
                analyze.TypeDefinitionId,
                [
                    analyze.StableSelectors[0],
                    analyzeAsync.StableSelectors[0],
                ]),
            "one method overload family");
    }

    [Fact]
    public async Task ExecuteParticipant_RejectsNonMethodSelection()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        AssemblyApiSurface surface =
            Assert.IsType<
                AssemblyContextEntry<AssemblyApiSurface>.Available>(
                    AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                        group,
                        participant))
                .Value;
        ApiType type = Assert.Single(
            surface.Surface.Types,
            type => type.Name == "ImplementationProfileSample");
        string[] selectors =
        [
            .. type.Members
                .Where(member =>
                    member.Name is "Value" or "Changed")
                .Select(member =>
                    ApiMemberIdentity
                        .GetMemberAnchor(type, member)
                        .StableSelector),
        ];

        AssertFailed(
            group,
            participant,
            new(
                AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
                selectors),
            "public methods only");
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
                    "unselected-implementation-profile-family-fixture")),
            policy);
        var selected = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    ImmutableCollectionsMarshal.AsArray(image)!,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    "selected-implementation-profile-family-fixture")),
            policy);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [unrelated, selected]);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                selected,
                "ImplementationProfileSample",
                "Analyze");

        var available = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Available>(
                    AssemblyContextImplementationProfileFamilyQuery
                        .ExecuteParticipant(
                            group,
                            selected,
                            selection));

        Assert.Same(
            selected.Assembly.Registration,
            available.Subject.Registration);
        Assert.Equal(0, unrelatedOpenCount);
    }

    static void AssertFailed(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        ImplementationProfileFamilySelection selection,
        string message)
    {
        var failed = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Failed>(
                    AssemblyContextImplementationProfileFamilyQuery
                        .ExecuteParticipant(
                            group,
                            participant,
                            selection));
        Assert.Contains(
            message,
            failed.Error.Message,
            StringComparison.Ordinal);
    }

    static AssemblyImplementationProfileFamilyInspection Available(
        AssemblyContextEntry<
            AssemblyImplementationProfileFamilyInspection> result) =>
        Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Available>(
                    result)
            .Value;

    static ImplementationProfileFamilySelection Selection(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeName,
        string memberName)
    {
        AssemblyApiSurface surface =
            Assert.IsType<
                AssemblyContextEntry<AssemblyApiSurface>.Available>(
                    AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                        group,
                        participant))
                .Value;
        ApiType type = Assert.Single(
            surface.Surface.Types,
            type => type.Name == typeName);
        string[] selectors =
        [
            .. type.Members
                .Where(member => member.Name == memberName)
                .Select(member =>
                    ApiMemberIdentity
                        .GetMemberAnchor(type, member)
                        .StableSelector),
        ];
        return new(
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
            selectors);
    }

    static AssemblyContextGroup Group(InspectionWorkspace workspace)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath()));
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
                    "implementation-profile-family-fixture")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
