using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextTypeImplementationHeatQueryTests
{
    [Fact]
    public async Task ExecuteParticipant_AnalyzesEveryEligibleFamilyInOneExecution()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, FixturePath());
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        AssemblyTypeImplementationHeatInspection result = Available(
            group,
            participant,
            TypeId(group, participant, "ImplementationProfileSample"));

        Assert.Equal(
            ["Analyze", "AnalyzeAsync"],
            result.Families.Select(family => family.Member).Order());
        Assert.Equal(
            InspectionCost.Unbounded,
            AssemblyContextTypeImplementationHeatQuery.Definition.Cost);

        // One execution receipt, narrowed to the analyzed methods, covers the
        // bodies of both families.
        var coverage = Assert.IsType<
            ILInspector.Analysis.ImplementationProfilePopulationCoverageReceipt>(
                result.Coverage);
        HashSet<int> profiled =
            [.. coverage.ProfiledEvidenceBodies.Select(method => method.MetadataToken)];
        Assert.All(
            result.Families,
            family => Assert.Contains(
                family.Methods,
                method => profiled.Contains(method.MetadataToken)));
        HashSet<int> analyzed =
        [
            .. result.Families.SelectMany(family =>
                family.Methods.Select(method => method.MetadataToken)),
        ];
        Assert.All(
            coverage.DeclaredMethods,
            method => Assert.Contains(method.MetadataToken, analyzed));

        ImplementationHeatFamily async = Assert.Single(
            result.Families,
            family => family.Member == "AnalyzeAsync");
        Assert.All(
            async.Methods,
            method =>
            {
                Assert.True(method.IsRosterMember);
                Assert.True(method.IsComplete);
                // The state machine is counted with its logical owner, so an
                // async method is never sized by its stub alone.
                Assert.True(method.Size > 20);
            });
    }

    [Fact]
    public async Task ExecuteParticipant_RecordsNonPublicImplementationAndSameNameRelationships()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, FixturePath());
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        AssemblyTypeImplementationHeatInspection result = Available(
            group,
            participant,
            TypeId(
                group,
                participant,
                "ImplementationProfileHiddenImplementationSample"));

        ImplementationHeatFamily parse = Assert.Single(
            result.Families,
            family => family.Member == "Parse");
        Assert.Equal(2, parse.Roster.Length);
        ImplementationHeatMethod hidden = Assert.Single(
            parse.Methods,
            method => !method.IsRosterMember);
        Assert.All(
            parse.Methods.Where(method => method.IsRosterMember),
            method => Assert.True(method.Size * 2 < hidden.Size));
        Assert.Equal(
            parse.Roster.Select(member => member.MetadataToken).Order(),
            parse.Relationships
                .Where(relationship =>
                    relationship.CalleeToken == hidden.MetadataToken)
                .Select(relationship => relationship.CallerToken)
                .Order());

        ImplementationHeatFamily describe = Assert.Single(
            result.Families,
            family => family.Member == "Describe");
        Assert.Equal(2, describe.Roster.Length);
        Assert.Equal(3, describe.Methods.Length);
        Assert.Single(Hubs(describe));
    }

    [Fact]
    public async Task ExecuteParticipant_IncludesAttachedAndExcludesMixedExtensionGroups()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, FixturePath());
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        string typeId =
            TypeId(group, participant, "ImplementationHeatWidget");
        AssemblyTypeImplementationHeatInspection result = Available(
            group,
            participant,
            typeId);

        Assert.Equal(
            ["Scale", "Spin"],
            result.Families.Select(family => family.Member).Order());
        Assert.DoesNotContain(
            result.Families,
            family => family.Member is "Run" or "Shift");

        ImplementationHeatFamily spin = Assert.Single(
            result.Families,
            family => family.Member == "Spin");
        Assert.Equal(2, spin.Roster.Length);
        Assert.All(spin.Methods, method => Assert.True(method.IsRosterMember));
        Assert.All(
            spin.Roster,
            member => Assert.Equal(typeId, member.TypeDefinitionId));
    }

    [Fact]
    public async Task ExecuteParticipant_TrivialFlagFollowsEveryCountedBody()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, FixturePath());
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        string typeId = TypeId(group, participant, "ImplementationHeatLambdaSample");

        ImplementationHeatFamily scale = Assert.Single(
            Available(group, participant, typeId).Families);

        // Each logical body alone is trivial; its attributed lambda branches.
        AssemblyImplementationProfileFamilyInspection detail =
            Assert.IsType<
                AssemblyContextEntry<
                    AssemblyImplementationProfileFamilyInspection>.Available>(
                        AssemblyContextImplementationProfileFamilyQuery
                            .ExecuteParticipant(
                                group,
                                participant,
                                new ImplementationProfileFamilySelection(
                                    typeId,
                                    scale.Roster.Select(member =>
                                        member.StableSelector))))
                .Value;
        Assert.All(
            scale.Methods,
            method =>
            {
                var own = Assert.Single(
                    detail.Profiles,
                    profile =>
                        profile.Profile.Method.MetadataToken == method.MetadataToken
                        && profile.Profile.EvidenceMethod.MetadataToken
                            == method.MetadataToken).Profile;
                Assert.True(own.InstructionCount <= 8);
                Assert.Equal(0, own.BranchCount);
                Assert.True(method.Size > own.InstructionCount);
                Assert.False(method.IsTrivial);
            });
    }

    [Fact]
    public async Task ExecuteParticipant_TypeWithoutEligibleFamilyIsAvailableAndEmpty()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, FixturePath());
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        AssemblyTypeImplementationHeatInspection result = Available(
            group,
            participant,
            TypeId(group, participant, "ImplementationHeatSingleSample"));

        Assert.Empty(result.Families);
        Assert.Null(result.Coverage);
    }

    [Fact]
    public async Task ExecuteParticipant_FailsVisiblyForUnknownType()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, FixturePath());
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextEntry<
                AssemblyTypeImplementationHeatInspection>.Failed>(
                    AssemblyContextTypeImplementationHeatQuery.ExecuteParticipant(
                        group,
                        participant,
                        "Missing.Type"));
        Assert.Contains("was not found", failed.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteParticipant_SystemTextJsonRecordsNonPublicMaximumAndHubs()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            RealAsset("DocumentationQuery", "System.Text.Json.dll"));
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        AssemblyTypeImplementationHeatInspection document = Available(
            group,
            participant,
            TypeId(group, participant, "JsonDocument"));
        ImplementationHeatFamily parse = Assert.Single(
            document.Families,
            family => family.Member == "Parse");
        ImplementationHeatMethod largest = parse.Methods
            .Where(method => method.Size is not null)
            .MaxBy(method => method.Size)!;
        Assert.False(largest.IsRosterMember);
        Assert.All(
            parse.Methods.Where(method => method.IsRosterMember),
            method => Assert.True(method.Size * 2 < largest.Size));
        Assert.Empty(Hubs(parse));

        ImplementationHeatFamily deserialize = Assert.Single(
            document.Families,
            family => family.Member == "Deserialize");
        Assert.Equal(5, deserialize.Roster.Length);
        Assert.All(
            deserialize.Roster,
            member => Assert.Equal(
                document.TypeDefinitionId,
                member.TypeDefinitionId));
        Assert.All(
            deserialize.Roster,
            member => Assert.StartsWith(
                "extension:Deserialize~",
                member.StableSelector,
                StringComparison.Ordinal));
        Assert.Equal(
            [17, 21, 19, 19, 24],
            deserialize.Roster.Select(member =>
                Assert.Single(
                    deserialize.Methods,
                    method => method.MetadataToken == member.MetadataToken)
                .Size));

        AssemblyTypeImplementationHeatInspection writer = Available(
            group,
            participant,
            TypeId(group, participant, "Utf8JsonWriter"));
        ImplementationHeatFamily writeString = Assert.Single(
            writer.Families,
            family => family.Member == "WriteString");
        Assert.Equal(28, writeString.Roster.Length);
        Assert.Equal(8, Hubs(writeString).Count);
        Assert.True(writer.Families.Length > 1);
    }

    [Fact]
    public async Task ExecuteParticipant_DapperSizesAsyncImplementationWithItsStateMachine()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            RealAsset("TypeImplementationHeat", "Dapper.dll"));
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        AssemblyTypeImplementationHeatInspection result = Available(
            group,
            participant,
            TypeId(group, participant, "SqlMapper"));
        ImplementationHeatFamily queryAsync = Assert.Single(
            result.Families,
            family => family.Member == "QueryAsync");

        // The private QueryAsync(IDbConnection, Type, CommandDefinition) is a
        // 23-instruction stub whose 441-instruction state machine carries the
        // implementation.
        ImplementationHeatMethod implementation = queryAsync.Methods
            .Where(method => !method.IsRosterMember && method.Size is not null)
            .MaxBy(method => method.Size)!;
        Assert.Equal(464, implementation.Size);
        Assert.False(implementation.IsTrivial);
        Assert.All(
            queryAsync.Methods.Where(method => method.IsRosterMember),
            method => Assert.True(method.Size * 2 < implementation.Size));
        Assert.Empty(Hubs(queryAsync));
    }

    // The Browser-side hub rule, restated over the record for assertions.
    static List<ImplementationHeatMethod> Hubs(ImplementationHeatFamily family)
    {
        HashSet<int> analyzed =
            [.. family.Methods.Select(method => method.MetadataToken)];
        return
        [
            .. family.Methods.Where(method =>
                method.IsRosterMember
                && method.IsComplete
                && family.Relationships.Any(relationship =>
                    relationship.CalleeToken == method.MetadataToken
                    && relationship.CallerToken != method.MetadataToken
                    && analyzed.Contains(relationship.CallerToken))
                && !family.Relationships.Any(relationship =>
                    relationship.CallerToken == method.MetadataToken
                    && relationship.CalleeToken != method.MetadataToken
                    && analyzed.Contains(relationship.CalleeToken))),
        ];
    }

    static AssemblyTypeImplementationHeatInspection Available(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeDefinitionId) =>
        Assert.IsType<
            AssemblyContextEntry<
                AssemblyTypeImplementationHeatInspection>.Available>(
                    AssemblyContextTypeImplementationHeatQuery.ExecuteParticipant(
                        group,
                        participant,
                        typeDefinitionId))
            .Value;

    static string TypeId(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        string typeName)
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
        return AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type);
    }

    static string FixturePath() =>
        FixtureCatalog.AnalysisCallerLoop.AssemblyPath();

    static string RealAsset(string scenario, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "RealAssets", scenario, fileName);

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
                    "type-implementation-heat-fixture")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
