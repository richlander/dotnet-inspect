extern alias legacyunsafe;

using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Metadata.MemorySafetyFixtures;
using Contracts = ILInspector.Metadata.MemorySafetyFixtures.MemorySafetyFixtures;
using Legacy = legacyunsafe::ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;

namespace ILInspector.Metadata.Tests;

public sealed class ApiMemorySafetyFactsTests
{
    [Fact]
    public void UpdatedContractsAndPointersAreIndependent()
    {
        ApiType type = ExtractType(typeof(Contracts));
        ApiMember pointer = Member(type, nameof(Contracts.PointerOnly));
        var pointerFacts = Assert.IsType<ApiMemberMemorySafetyFacts>(pointer.MemorySafety);
        Assert.IsType<MemorySafetyMemberContractResult.None>(pointerFacts.CallerContract);
        Assert.Equal(MemorySafetyPointerEvidence.Present, pointerFacts.SignaturePointer);
        Assert.True(pointer.IsUnsafe);

        ApiMember contract = Member(type, nameof(Contracts.MethodContract));
        Assert.IsType<MemorySafetyMemberContractResult.Explicit>(contract.MemorySafety!.CallerContract);
        Assert.Equal(MemorySafetyPointerEvidence.Absent, contract.MemorySafety.SignaturePointer);
        Assert.True(contract.IsUnsafe);

        foreach (string name in new[] { nameof(Contracts.PropertyContract), nameof(Contracts.EventContract) })
        {
            ApiMember member = Member(type, name);
            Assert.IsType<MemorySafetyMemberContractResult.Explicit>(member.MemorySafety!.CallerContract);
            Assert.False(member.IsUnsafe);
            Assert.NotEmpty(member.AccessorMemorySafety!.Value);
            Assert.All(member.AccessorMemorySafety.Value, accessor =>
            {
                Assert.IsType<MemorySafetyMemberContractResult.Explicit>(accessor.CallerContract);
                Assert.Equal(member.MemorySafety.ModuleVersionId, accessor.ModuleVersionId);
            });
        }
    }

    [Fact]
    public void LegacyPointersKeepTheirImplicitContract()
    {
        ApiMember pointer = Member(ExtractType(typeof(Legacy)), nameof(Legacy.FreePointer));
        Assert.IsType<MemorySafetyMemberContractResult.Implicit>(pointer.MemorySafety!.CallerContract);
        Assert.Equal(MemorySafetyPointerEvidence.Present, pointer.MemorySafety.SignaturePointer);
        Assert.True(pointer.IsUnsafe);
    }

    [Fact]
    public void ConstructorsFieldsAndProjectedExtensionsRetainTheirFacts()
    {
        ApiType type = ExtractType(typeof(MemorySafetyDeclarationFixtures));
        Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
            Member(type, ".ctor").MemorySafety!.CallerContract);
        ApiMember pointer = Member(type, nameof(MemorySafetyDeclarationFixtures.PointerField));
        Assert.IsType<MemorySafetyMemberContractResult.None>(pointer.MemorySafety!.CallerContract);
        Assert.Equal(MemorySafetyPointerEvidence.Present, pointer.MemorySafety.SignaturePointer);
        Assert.False(pointer.IsUnsafe);
        ApiMember contract = Member(type, nameof(MemorySafetyDeclarationFixtures.ContractField));
        Assert.IsType<MemorySafetyMemberContractResult.Explicit>(contract.MemorySafety!.CallerContract);
        Assert.False(contract.IsUnsafe);
        ApiMember extension = Member(type, nameof(Contracts.ExtensionContract));
        Assert.Equal("extension-method", extension.Kind);
        Assert.IsType<MemorySafetyMemberContractResult.Explicit>(extension.MemorySafety!.CallerContract);
    }

    [Theory]
    [InlineData("Property", false, ApiBackingStorageConvention.AutoProperty)]
    [InlineData("StaticProperty", true, ApiBackingStorageConvention.AutoProperty)]
    [InlineData("Event", false, ApiBackingStorageConvention.FieldLikeEvent)]
    [InlineData("StaticEvent", true, ApiBackingStorageConvention.FieldLikeEvent)]
    public void CompilerBackingMatchesPreserveStorageAndEvidence(
        string name, bool isStatic, ApiBackingStorageConvention convention)
    {
        ApiType type = ExtractType(typeof(MemorySafetyDeclarationFixtures));
        ApiMember member = Member(type, name);
        var association = Assert.IsType<ApiBackingStorageAssociation>(member.BackingStorage);
        Assert.Equal(ApiBackingStorageState.Associated, association.State);
        Assert.Equal(convention, association.Convention);
        Assert.Equal(member.MemorySafety!.ModuleVersionId, association.ModuleVersionId);
        var field = Assert.Single(association.Candidates);
        Assert.True(field.FieldToken > 0);
        Assert.Equal(isStatic, field.IsStatic);
        Assert.Equal(convention == ApiBackingStorageConvention.AutoProperty
            ? $"<{name}>k__BackingField" : name, field.MatchedName);
    }

    [Theory]
    [InlineData("CustomProperty")]
    [InlineData("CustomEvent")]
    public void AnUnprovenAssociationDoesNotClaimAbsence(string name)
    {
        ApiMember member = Member(ExtractType(typeof(MemorySafetyDeclarationFixtures)), name);
        Assert.Equal(ApiBackingStorageState.Unknown, member.BackingStorage!.State);
        Assert.Empty(member.BackingStorage.Candidates);
    }

    [Theory]
    [InlineData("Property")]
    [InlineData("ArrayProperty")]
    [InlineData("PointerProperty")]
    [InlineData("FunctionPointerProperty")]
    [InlineData("Event")]
    public void CompilerBackingMatchesRetainGenericAndPointerShape(string name)
    {
        ApiMember member = Member(ExtractType(typeof(MemorySafetyGenericStorage<>)), name);
        Assert.Equal(ApiBackingStorageState.Associated, member.BackingStorage!.State);
        Assert.False(Assert.Single(member.BackingStorage.Candidates).IsStatic);
    }

    [Theory]
    [InlineData(false, false, ApiBackingStorageState.Associated)]
    [InlineData(false, true, ApiBackingStorageState.Unknown)]
    [InlineData(true, false, ApiBackingStorageState.Associated)]
    [InlineData(true, true, ApiBackingStorageState.Unknown)]
    public void BackingTypeMatchesRetainScope(
        bool fieldLikeEvent, bool distinctTypeScopes, ApiBackingStorageState expected)
    {
        ApiType type = Assert.Single(Extract(ApiMemorySafetyFactsSamples.BackingFields(
            namedType: true, distinctTypeScopes: distinctTypeScopes,
            fieldLikeEvent: fieldLikeEvent)).Types);
        Assert.Equal(expected, Member(type, "Value").BackingStorage!.State);
    }

    [Fact]
    public void BackingTypeMatchesRetainArrayBounds()
    {
        ApiType type = Assert.Single(Extract(ApiMemorySafetyFactsSamples.BackingFields(
            propertyTypeOverride: [0x14, 8, 1, 0, 0],
            fieldTypeOverride: [0x14, 8, 1, 1, 3, 0])).Types);
        Assert.Equal(ApiBackingStorageState.Unknown, Member(type, "Value").BackingStorage!.State);
    }

    [Theory]
    [InlineData(1, false, ApiBackingStorageState.Associated, 1)]
    [InlineData(2, false, ApiBackingStorageState.Ambiguous, 2)]
    [InlineData(2, true, ApiBackingStorageState.Unknown, 1)]
    [InlineData(0, false, ApiBackingStorageState.Unknown, 0)]
    public void BackingMatchRetainsAmbiguityAndIncompleteEvidence(
        int count, bool degraded, ApiBackingStorageState state, int candidates)
    {
        ApiType type = Assert.Single(Extract(
            ApiMemorySafetyFactsSamples.BackingFields(count, degraded)).Types);
        var association = Member(type, "Value").BackingStorage!;
        Assert.Equal(state, association.State);
        Assert.Equal(candidates, association.Candidates.Length);
    }

    [Theory]
    [InlineData(0, ApiTypeLayout.Auto)]
    [InlineData(8, ApiTypeLayout.Sequential)]
    [InlineData(16, ApiTypeLayout.Explicit)]
    [InlineData(24, ApiTypeLayout.Extended)]
    public void LayoutRetainsTheMetadataKind(int flags, ApiTypeLayout expected)
    {
        ApiType type = Assert.Single(Extract(
            ApiMemorySafetyFactsSamples.BackingFields(layout: (TypeAttributes)flags)).Types);
        Assert.Equal(expected, type.Layout);
    }

    [Fact]
    public void CompilerExplicitLayoutIsRetained()
        => Assert.Equal(ApiTypeLayout.Explicit, ExtractType(typeof(MemorySafetyExplicitLayout)).Layout);

    [Fact]
    public void DuplicatePropertyNamesDoNotClaimAUniqueBackingOwner()
    {
        ApiType type = Assert.Single(Extract(
            ApiMemorySafetyFactsSamples.BackingFields(duplicateProperty: true)).Types);
        Assert.Equal(2, type.Members.Count);
        Assert.All(type.Members, member =>
            Assert.Equal(ApiBackingStorageState.Unknown, member.BackingStorage!.State));
    }

    [Fact]
    public void TypesOnlyExtractionDoesNotAcquireContractFacts()
    {
        using var stream = File.OpenRead(typeof(Contracts).Assembly.Location);
        using var pe = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(pe, typesOnly: true);
        Assert.NotEmpty(surface.Types);
        Assert.All(surface.Types, type =>
        {
            Assert.NotNull(type.Layout);
            Assert.Null(type.MemorySafety);
            Assert.Empty(type.Members);
        });
    }

    [Theory]
    [InlineData(2, MemorySafetyRulesState.Updated)]
    [InlineData(99, MemorySafetyRulesState.Unsupported)]
    [InlineData(null, MemorySafetyRulesState.Malformed)]
    public void RulesKeepVersionAndObservationEvidence(int? marker, MemorySafetyRulesState state)
    {
        ApiType type = ExtractTarget(
            MemorySafetyMetadataIndexTests.BuildSyntheticImage([marker]));
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(type.MemorySafety!.Rules);
        Assert.Equal(state, rules.State);
        Assert.Equal(marker, Assert.Single(rules.Observations).Version);
        var facts = Member(type, "PointerOnly").MemorySafety!;
        Assert.Equal(state, facts.CallerContract.Evidence.RulesState);
        Assert.Equal(MemorySafetyPointerEvidence.Present, facts.SignaturePointer);
    }

    [Fact]
    public void ConflictingRulesDoNotEraseStructuralPointerEvidence()
    {
        ApiType type = ExtractTarget(MemorySafetyMetadataIndexTests.BuildSyntheticImage([2, 1]));
        var facts = Member(type, "PointerOnly").MemorySafety!;
        Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(facts.CallerContract);
        Assert.Equal(MemorySafetyPointerEvidence.Present, facts.SignaturePointer);
    }

    [Fact]
    public void ADegradedSignatureIsNotAPointerFreeSignature()
    {
        ApiType type = ExtractTarget(MemorySafetyMetadataIndexTests.BuildSyntheticImage(
            [2], malformedPointerSignature: true));
        var facts = Member(type, "PointerOnly").MemorySafety!;
        Assert.IsType<MemorySafetyMemberContractResult.None>(facts.CallerContract);
        Assert.Equal(MemorySafetyPointerEvidence.Unavailable, facts.SignaturePointer);
    }

    [Fact]
    public void ContractRefusalsSurviveExtractionAndJson()
    {
        ApiType type = ExtractTarget(MemorySafetyMetadataIndexTests.BuildSyntheticImage(
            [2], malformedRequiresUnsafe: true));
        ApiType restored = JsonSerializer.Deserialize<ApiType>(JsonSerializer.Serialize(type))!;
        var failure = Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
            Member(restored, "AttributeOnly").MemorySafety!.CallerContract);
        Assert.Equal(MemorySafetyMemberContractFailureKind.MalformedRequiresUnsafeAttribute,
            failure.Failure.Kind);
    }

    [Fact]
    public void NewFactsSurviveReaderDisposalAndJson()
    {
        ApiType live = ExtractType(typeof(MemorySafetyDeclarationFixtures));
        ApiType restored = JsonSerializer.Deserialize<ApiType>(JsonSerializer.Serialize(live))!;
        Assert.Equal(live.Layout, restored.Layout);
        Assert.Equal(live.MemorySafety!.ModuleVersionId, restored.MemorySafety!.ModuleVersionId);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(restored.MemorySafety.Rules);
        Assert.Equal(MemorySafetyRulesState.Updated, rules.State);
        ApiMember member = Member(restored, "Property");
        Assert.Equal(ApiBackingStorageState.Associated, member.BackingStorage!.State);
        Assert.Equal(2, member.AccessorMemorySafety!.Value.Length);
        Assert.IsType<MemorySafetyMemberContractResult.None>(member.MemorySafety!.CallerContract);
    }

    [Fact]
    public void OlderSurfacesDoNotInventNewFacts()
    {
        ApiType type = JsonSerializer.Deserialize<ApiType>(
            """{"Name":"Old","Members":[{"Name":"M","IsUnsafe":false}]}""")!;
        Assert.Null(type.MemorySafety);
        Assert.Null(type.Layout);
        var member = Assert.Single(type.Members);
        Assert.Null(member.MemorySafety);
        Assert.Null(member.AccessorMemorySafety);
        Assert.Null(member.BackingStorage);
    }

    [Fact]
    public void BackingEvidenceContributesItsRetainedText()
    {
        ApiMember member = Member(
            Assert.Single(Extract(ApiMemorySafetyFactsSamples.BackingFields()).Types), "Value");
        long retained = ApiSurfaceExtractor.CountRetainedText(member);
        int names = member.BackingStorage!.Candidates.Sum(field => field.MatchedName.Length);
        member.BackingStorage = null;
        Assert.Equal(names, retained - ApiSurfaceExtractor.CountRetainedText(member));
    }

    static ApiType ExtractType(Type type)
        => Extract(File.ReadAllBytes(type.Assembly.Location)).Types.Single(
            candidate => candidate.FullName == type.FullName);

    static ApiType ExtractTarget(byte[] image)
        => Extract(image).Types.Single(type => type.FullName == "Samples.Target");

    static ApiSurface Extract(byte[] image)
    {
        using var stream = new MemoryStream(image);
        using var pe = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(pe, includeAll: true);
    }

    static ApiMember Member(ApiType type, string name)
        => type.Members.Single(member => member.Name == name);
}
