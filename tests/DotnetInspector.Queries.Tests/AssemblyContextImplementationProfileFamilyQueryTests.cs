using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Analysis.ImplementationProfileFixtures;
using ILInspector.Analysis.Planning;
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
    public async Task
            ExecuteParticipant_UsesExactTypeProjectionForPreparedFamily()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        AssemblyApiSurface projected =
            Assert.IsType<
                AssemblyContextEntry<AssemblyApiSurface>.Available>(
                    AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                        group,
                        participant))
                .Value;
        ApiType type = Assert.Single(
            projected.Surface.Types,
            type => type.Name == "ImplementationProfileSample");
        ApiMember[] members =
        [
            .. type.Members.Where(member =>
                member.Name == "Analyze"),
        ];
        var familyFailure = new ApiSurfaceInspectionFailure(
            "decode method",
            members[0].MetadataToken!.Value,
            MetadataTypeNameFailureMechanism.Metadata,
            "MethodDef",
            "The selected method is incomplete.");
        projected.Surface.InspectionFailures.Add(familyFailure);
        ImplementationProfileFamilySelection selection =
            ImplementationProfileFamilySelection.CreatePrepared(
                type,
                members);
        Assert.True(type.Members.Remove(members[^1]));
        ImplementationProfileFamilySelection partialSelection =
            ImplementationProfileFamilySelection.CreatePrepared(
                type,
                members.Take(3));
        var partialFailure = Assert.IsType<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>.Failed>(
                    AssemblyContextImplementationProfileFamilyQuery
                        .ExecuteParticipant(
                            group,
                            participant,
                            partialSelection));
        Assert.Contains(
            "complete public overload family",
            partialFailure.Error.Message,
            StringComparison.Ordinal);

        AssemblyImplementationProfileFamilyInspection result =
            Available(
                AssemblyContextImplementationProfileFamilyQuery
                    .ExecuteParticipant(
                        group,
                        participant,
                        selection));

        Assert.Equal(4, result.Members.Length);
        Assert.Equal(
            selection.StableSelectors,
            result.Members.Select(member =>
                member.StableSelector));
        Assert.Empty(result.ApiSurfaceInspectionFailures);
        Assert.NotEmpty(result.Profiles);
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
    public async Task
            ExecuteParticipant_MatchesLegacyProfilesThroughExactMethodSource()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        foreach ((string typeName, string memberName) in new[]
        {
            ("ImplementationProfileSample", "Analyze"),
            ("ImplementationProfileSample", "AnalyzeAsync"),
            ("ImplementationHeatWidget", "Spin"),
            ("IImplementationProfileBodylessSample", "Route"),
        })
        {
            AssemblyImplementationProfileFamilyInspection result =
                Available(
                    AssemblyContextImplementationProfileFamilyQuery
                        .ExecuteParticipant(
                            group,
                            participant,
                            Selection(
                                group,
                                participant,
                                typeName,
                                memberName)));
            ImmutableHashSet<int> declaredTokens =
            [
                .. result.Coverage.DeclaredMethods.Select(
                    static method => method.MetadataToken),
            ];
            LibraryImplementationProfileAnalysisResult legacy =
                LibraryBodyAnalysisService.ExecutePath(
                    FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                    LibraryBodyAnalysisRequest
                        .CreateCompleteImplementationProfile(
                            declaredTokens))
                .ImplementationProfiles;
            ImmutableArray<MethodImplementationProfile> focusedProfiles =
            [
                .. result.Profiles.Select(
                    static profile => profile.Profile),
            ];

            Assert.Equal(legacy.Profiles, focusedProfiles);
            Assert.Equal(
                legacy.OverloadRelationships,
                result.OverloadRelationships);
            Assert.True(
                legacy.GeneratedFrameworkTypes.SetEquals(
                    result.GeneratedFrameworkTypes));

            MethodDefinitionSourceReceipt receipt =
                Assert.IsType<MethodDefinitionSourceReceipt>(
                    result.MethodSourceReceipt);
            Assert.Equal(
                MethodDefinitionSourceBreadthKind.ExactMethods,
                receipt.Breadth.Kind);
            Assert.Equal(
                MethodDefinitionSourceExpansion.GeneratedExecutionBodies,
                receipt.Breadth.Expansion);
            Assert.Equal(
                declaredTokens.Count,
                receipt.Breadth.Methods.Length);
            Assert.Equal(
                MethodDefinitionSourceCompletion.Exhausted,
                receipt.Completion);
            Assert.Equal(
                result.Coverage.ManagedMethodBodyCount,
                receipt.Coverage.TerminalWork.BodiesAdmitted);
            Assert.Equal(
                result.Coverage.ProfiledEvidenceBodyCount,
                receipt.Coverage.BodiesAcquired.Count);
            Assert.NotNull(result.WorkReceipt);
        }
    }

    [Fact]
    public async Task
            ExecuteParticipant_RecoversMalformedGeneratedMetadataWithoutLegacyService()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath());
        CorruptAnalyzeAsyncStateMachineSignature(image);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            ImmutableCollectionsMarshal.AsImmutableArray(image));
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
        ImmutableHashSet<int> declaredTokens =
        [
            .. result.Coverage.DeclaredMethods.Select(
                static method => method.MetadataToken),
        ];
        LibraryImplementationProfileAnalysisResult legacy =
            LibraryBodyAnalysisService.ExecuteImage(
                "MalformedImplementationProfileFamily.dll",
                ImmutableArray.Create(image),
                LibraryBodyAnalysisRequest
                    .CreateCompleteImplementationProfile(
                        declaredTokens))
            .ImplementationProfiles;

        ImmutableArray<MethodImplementationProfile> actualProfiles =
        [
            .. result.Profiles.Select(
                static profile => profile.Profile),
        ];
        Assert.Equal(
            legacy.Profiles.Length,
            actualProfiles.Length);
        for (int index = 0; index < legacy.Profiles.Length; index++)
        {
            MethodImplementationProfile expected =
                legacy.Profiles[index];
            MethodImplementationProfile actual =
                actualProfiles[index];
            Assert.Equal(
                expected with { IncompleteReasons = [] },
                actual with { IncompleteReasons = [] });
            Assert.Equal(
                expected.IncompleteReasons,
                actual.IncompleteReasons);
        }
        Assert.Equal(
            legacy.OverloadRelationships,
            result.OverloadRelationships);
        MethodDefinitionSourceReceipt recoveryReceipt =
            Assert.IsType<MethodDefinitionSourceReceipt>(
                result.MethodSourceReceipt);
        Assert.Equal(
            MethodDefinitionSourceExpansion.None,
            recoveryReceipt.Breadth.Expansion);
        Assert.Single(result.MethodSourceReceipts);
        Assert.Single(result.WorkReceipts);
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
    public async Task ExecuteParticipant_ProfilesAttachedExtensionRoster()
    {
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        AssemblyApiSurface projected =
            Assert.IsType<
                AssemblyContextEntry<AssemblyApiSurface>.Available>(
                    AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                        group,
                        participant))
                .Value;
        ApiType type = Assert.Single(
            projected.Surface.Types,
            type => type.Name == "ImplementationHeatWidget");
        ImplementationProfileFamilySelection selection =
            ImplementationProfileFamilySelection.CreatePrepared(
                type,
                type.Members.Where(member => member.Name == "Spin"));

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
            member =>
            {
                Assert.Equal("Spin", member.Member);
                Assert.StartsWith(
                    "extension:Spin~",
                    member.StableSelector,
                    StringComparison.Ordinal);
            });
        Assert.NotEmpty(result.Profiles);
        Assert.All(
            result.Profiles,
            profile => Assert.Contains(
                profile.Profile.Method.MetadataToken,
                result.Members.SelectMany(member => member.BodyTokens)));
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
        return Group(workspace, image);
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        ImmutableArray<byte> image)
    {
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

    static void CorruptAnalyzeAsyncStateMachineSignature(
        byte[] image)
    {
        Type stateMachineType = typeof(ImplementationProfileSample)
            .GetMethod(
                nameof(ImplementationProfileSample.AnalyzeAsync),
                [typeof(int)])!
            .GetCustomAttribute<AsyncStateMachineAttribute>()!
            .StateMachineType;
        int token = stateMachineType
            .GetMethod(
                nameof(IAsyncStateMachine.MoveNext),
                BindingFlags.Instance
                    | BindingFlags.NonPublic
                    | BindingFlags.Public)!
            .MetadataToken;
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        var methodHandle =
            (MethodDefinitionHandle)
                MetadataTokens.EntityHandle(token);
        int stringIndexSize =
            MetadataHeapIndexSize(
                reader,
                HeapIndex.String);
        int blobIndexSize =
            MetadataHeapIndexSize(
                reader,
                HeapIndex.Blob);
        int rowOffset =
            reader.GetTableMetadataOffset(TableIndex.MethodDef)
            + (MetadataTokens.GetRowNumber(methodHandle) - 1)
                * reader.GetTableRowSize(TableIndex.MethodDef);
        int signatureOffset =
            peReader.PEHeaders.MetadataStartOffset
            + rowOffset
            + sizeof(uint)
            + sizeof(ushort)
            + sizeof(ushort)
            + stringIndexSize;
        if (blobIndexSize == sizeof(uint))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(signatureOffset, blobIndexSize),
                0x7FFFFFFF);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(signatureOffset, blobIndexSize),
                0xFFFF);
        }
    }

    static int MetadataHeapIndexSize(
        MetadataReader reader,
        HeapIndex heap) =>
        reader.GetHeapSize(heap) < 0x10000
            ? sizeof(ushort)
            : sizeof(uint);

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
