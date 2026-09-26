using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
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
    public async Task ExecuteParticipant_MeasuresNonPublicImplementationOnlyInAnalyzedFamily()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                participant,
                "ImplementationProfileHiddenImplementationSample",
                "Parse");

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        Assert.Equal(2, result.Members.Length);
        Assert.All(
            result.Profiles,
            profile => Assert.Single(profile.PublicMembers));
        Assert.Empty(result.OverloadRelationships);

        ImplementationProfileAnalyzedFamily analyzed = result.AnalyzedFamily;
        Assert.Equal(3, analyzed.Methods.Length);
        ImplementationProfileAnalyzedMethod hidden = Assert.Single(
            analyzed.Methods,
            method => method.PublicMember is null);
        Assert.True(hidden.HasBody);
        Assert.DoesNotContain(
            result.Profiles,
            profile =>
                profile.Profile.Method.MetadataToken == hidden.MetadataToken);

        AssemblyImplementationProfileMember implementation = Assert.Single(
            analyzed.Profiles,
            profile =>
                profile.Profile.Method.MetadataToken == hidden.MetadataToken);
        Assert.Empty(implementation.PublicMembers);
        Assert.All(
            analyzed.Profiles.Where(profile => !profile.PublicMembers.IsEmpty),
            profile => Assert.True(
                profile.Profile.InstructionCount * 2
                    < implementation.Profile.InstructionCount));
        Assert.Equal(
            [.. analyzed.Methods
                .Where(method => method.PublicMember is not null)
                .Select(method => method.MetadataToken)
                .Order()],
            analyzed.OverloadRelationships
                .Where(relationship =>
                    relationship.Callee.MetadataToken == hidden.MetadataToken)
                .Select(relationship => relationship.Caller.MetadataToken)
                .Order());
        Assert.True(analyzed.Coverage.WasRequested);
        Assert.Empty(analyzed.Coverage.UnavailableBodies);
    }

    [Fact]
    public async Task ExecuteParticipant_KeepsRosterSiblingCountsWhenNonPublicOverloadsCall()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                participant,
                "ImplementationProfileHiddenImplementationSample",
                "Describe");

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        AssertHubCounts(
            result,
            hub => hub.ParameterTypes is [{ Name: "Int32" }],
            rosterIncoming: 1,
            analyzedIncoming: 2);
    }

    [Fact]
    public async Task ExecuteParticipant_AnalyzedFamilyMatchesRosterWithoutNonPublicOverloads()
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

        ImplementationProfileAnalyzedFamily analyzed = result.AnalyzedFamily;
        Assert.Equal(4, analyzed.Methods.Length);
        Assert.All(
            analyzed.Methods,
            method => Assert.NotNull(method.PublicMember));
        Assert.Equal(
            result.Profiles.Select(profile => profile.Profile),
            analyzed.Profiles.Select(profile => profile.Profile));
        Assert.Equal(
            result.OverloadRelationships,
            analyzed.OverloadRelationships);
    }

    [Fact]
    public async Task ExecuteParticipant_JsonDocumentParseImplementationIsNonPublic()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            RealAsset("DocumentationQuery", "System.Text.Json.dll"));
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(group, participant, "JsonDocument", "Parse");

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        ImplementationProfileAnalyzedFamily analyzed = result.AnalyzedFamily;
        Assert.Contains(
            analyzed.Methods,
            method => method.PublicMember is null);
        Assert.All(
            result.Profiles,
            profile => Assert.NotEmpty(profile.PublicMembers));
        MethodImplementationProfile largest = analyzed.Profiles
            .Select(profile => profile.Profile)
            .MaxBy(profile => profile.InstructionCount)!;
        Assert.DoesNotContain(
            analyzed.Methods,
            method =>
                method.MetadataToken == largest.Method.MetadataToken
                && method.PublicMember is not null);
        Assert.All(
            analyzed.Profiles.Where(profile => !profile.PublicMembers.IsEmpty),
            profile => Assert.True(
                profile.Profile.InstructionCount * 2
                    < largest.InstructionCount));
    }

    [Fact]
    public async Task ExecuteParticipant_JsonConvertToStringKeepsRosterIncomingCount()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            RealAsset("ImplementationProfileFamily", "Newtonsoft.Json.dll"));
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(group, participant, "JsonConvert", "ToString");

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        AssertHubCounts(
            result,
            method => method.ParameterTypes is
                [{ Name: "String" }, { Name: "Char" }],
            rosterIncoming: 1,
            analyzedIncoming: 3);
    }

    [Fact]
    public async Task ExecuteParticipant_AnalyzesAttachedExtensionFamilyOnDeclaringType()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            RealAsset(
                Path.Combine("PackageQueryReferences", "net10.0"),
                "Microsoft.Extensions.Http.dll"));
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        ImplementationProfileFamilySelection selection =
            Selection(
                group,
                participant,
                "IHttpClientBuilder",
                "AddHttpMessageHandler");

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        ImplementationProfileAnalyzedFamily analyzed = result.AnalyzedFamily;
        Assert.Equal(
            selection.StableSelectors.Order(StringComparer.Ordinal),
            analyzed.Methods
                .Where(method => method.PublicMember is not null)
                .Select(method => method.PublicMember!.StableSelector)
                .Order(StringComparer.Ordinal));
        Assert.All(
            analyzed.Profiles,
            profile => Assert.Contains(
                analyzed.Methods,
                method =>
                    method.MetadataToken
                        == profile.Profile.Method.MetadataToken));
        Assert.All(
            analyzed.Profiles,
            profile => Assert.Equal(
                "HttpClientBuilderExtensions",
                profile.Profile.Method.DeclaringType.Name));
    }

    static void AssertHubCounts(
        AssemblyImplementationProfileFamilyInspection result,
        Func<MethodIdentity, bool> isHub,
        int rosterIncoming,
        int analyzedIncoming)
    {
        MethodImplementationProfile roster = Assert.Single(
            result.Profiles,
            profile =>
                profile.Profile.Method == profile.Profile.EvidenceMethod
                && isHub(profile.Profile.Method))
            .Profile;
        MethodImplementationProfile analyzed = Assert.Single(
            result.AnalyzedFamily.Profiles,
            profile =>
                profile.Profile.Method == profile.Profile.EvidenceMethod
                && isHub(profile.Profile.Method))
            .Profile;

        Assert.Equal(rosterIncoming, roster.IncomingOverloadCallerCount);
        Assert.Equal(analyzedIncoming, analyzed.IncomingOverloadCallerCount);
        Assert.Equal(
            analyzedIncoming,
            result.AnalyzedFamily.OverloadRelationships
                .Where(relationship =>
                    relationship.Callee.MetadataToken
                        == analyzed.Method.MetadataToken)
                .Select(relationship => relationship.Caller.MetadataToken)
                .Distinct()
                .Count());
        Assert.All(
            result.OverloadRelationships,
            relationship => Assert.Contains(
                result.Members,
                member => member.BodyTokens.Contains(
                    relationship.Caller.MetadataToken)));
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

    static string RealAsset(string scenario, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "RealAssets", scenario, fileName);

    static AssemblyContextGroup Group(InspectionWorkspace workspace) =>
        Group(workspace, FixtureCatalog.AnalysisCallerLoop.AssemblyPath());

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string assemblyPath)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                File.ReadAllBytes(assemblyPath));
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
