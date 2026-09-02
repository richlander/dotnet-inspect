extern alias legacyunsafe;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

using ContractFixtures =
    ILInspector.Metadata.MemorySafetyFixtures.MemorySafetyFixtures;
using LegacyFields =
    legacyunsafe::ILInspector.Decompiler.Fixtures.LegacyUnsafe.FixedBufferResiduals;
using LegacyFixtures =
    legacyunsafe::ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;

namespace ILInspector.Metadata.Tests;

public sealed class MemorySafetyMetadataIndexTests
{
    [Fact]
    public void MemorySafetyMetadataIndex_RecognizesCompilerProducedModels()
    {
        using OpenedMetadata legacy = Open(typeof(LegacyFixtures));
        AssertAvailable(
            MemorySafetyMetadataIndex.Create(legacy.Reader).Rules);
        var legacyRules =
            Assert.IsType<MemorySafetyRulesResult.Available>(
                MemorySafetyMetadataIndex.Create(legacy.Reader).Rules);
        Assert.Equal(MemorySafetyRulesState.Legacy, legacyRules.State);
        Assert.Empty(legacyRules.Observations);

        using OpenedMetadata updated = Open(typeof(ContractFixtures));
        var updatedRules =
            Assert.IsType<MemorySafetyRulesResult.Available>(
                MemorySafetyMetadataIndex.Create(updated.Reader).Rules);
        Assert.Equal(MemorySafetyRulesState.Updated, updatedRules.State);
        Assert.All(
            updatedRules.Observations,
            observation =>
            {
                Assert.Equal(
                    MemorySafetyRulesObservationState.Decoded,
                    observation.State);
                Assert.Equal(2, observation.Version);
            });
    }

    [Fact]
    public void MemorySafetyMetadataIndex_UsesVersionSpecificMemberContracts()
    {
        using OpenedMetadata legacy = Open(typeof(LegacyFixtures));
        MemorySafetyMetadataIndex legacyIndex =
            MemorySafetyMetadataIndex.Create(legacy.Reader);

        Assert.IsType<MemorySafetyMemberContractResult.Implicit>(
            legacyIndex.GetMemberContract(
                FindMethod(
                    legacy.Reader,
                    typeof(LegacyFixtures).FullName!,
                    nameof(LegacyFixtures.FreePointer))));
        Assert.IsType<MemorySafetyMemberContractResult.None>(
            legacyIndex.GetMemberContract(
                FindMethod(
                    legacy.Reader,
                    typeof(LegacyFixtures).FullName!,
                    nameof(LegacyFixtures.Risky))));

        using OpenedMetadata updated = Open(typeof(ContractFixtures));
        MemorySafetyMetadataIndex updatedIndex =
            MemorySafetyMetadataIndex.Create(updated.Reader);

        var explicitContract =
            Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
                updatedIndex.GetMemberContract(
                    FindMethod(
                        updated.Reader,
                        typeof(ContractFixtures).FullName!,
                        nameof(ContractFixtures.MethodContract))));
        Assert.True(
            explicitContract.Evidence.DirectAttribute.HasValidRow);

        var pointerOnly =
            Assert.IsType<MemorySafetyMemberContractResult.None>(
                updatedIndex.GetMemberContract(
                    FindMethod(
                        updated.Reader,
                        typeof(ContractFixtures).FullName!,
                        nameof(ContractFixtures.PointerOnly))));
        Assert.Equal(
            MemorySafetyPointerEvidence.NotExamined,
            pointerOnly.Evidence.Pointer);
    }

    [Fact]
    public void MemorySafetyMetadataIndex_RecognizesCompilerFixedBufferFields()
    {
        using OpenedMetadata legacy = Open(typeof(LegacyFields));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(legacy.Reader);

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.None>(
                index.GetMemberContract(
                    FindField(
                        legacy.Reader,
                        typeof(LegacyFields).FullName!,
                        nameof(LegacyFields.Data))));

        Assert.Equal(
            MemorySafetyPointerEvidence.Absent,
            result.Evidence.Pointer);
        Assert.Equal(
            MemorySafetyFixedBufferEvidence.Present,
            result.Evidence.FixedBuffer);
    }

    [Fact]
    public void MalformedFixedBufferCarrierCannotSuppressPointerPropagation()
    {
        using OpenedMetadata opened = Open(
            BuildFixedBufferLookalikeImage(attributeCount: 1));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Implicit>(
                index.GetMemberContract(
                    MetadataTokens.FieldDefinitionHandle(1)));
        Assert.Equal(
            MemorySafetyPointerEvidence.Present,
            result.Evidence.Pointer);
        Assert.Equal(
            MemorySafetyFixedBufferEvidence.Unavailable,
            result.Evidence.FixedBuffer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FixedBufferEvidenceUsesAttributeAndNameWorkBudgets(
        bool attributeRows)
    {
        using OpenedMetadata opened = Open(
            BuildFixedBufferLookalikeImage(
                attributeCount: attributeRows ? 2 : 1));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(
                opened.Reader,
                associationRowBudget: 100,
                attributeRowBudget: attributeRows ? 1 : 100,
                nameWorkBudget: attributeRows ? 100 : 1);

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Implicit>(
                index.GetMemberContract(
                    MetadataTokens.FieldDefinitionHandle(1)));
        Assert.Equal(
            MemorySafetyPointerEvidence.Present,
            result.Evidence.Pointer);
        Assert.Equal(
            MemorySafetyFixedBufferEvidence.Unavailable,
            result.Evidence.FixedBuffer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalRulesCarrierAuthenticatesThroughEitherConstructorSpelling(
        bool memberReferenceConstructor)
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                localRulesMemberRefConstructor: memberReferenceConstructor));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var rules =
            Assert.IsType<MemorySafetyRulesResult.Available>(index.Rules);
        Assert.Equal(MemorySafetyRulesState.Updated, rules.State);
        MemorySafetyRulesObservation observation =
            Assert.Single(rules.Observations);
        Assert.Equal(
            MemorySafetyRulesObservationState.Decoded,
            observation.State);
        Assert.Equal(2, observation.Version);
    }

    [Fact]
    public void NestedLocalRulesCarrierStaysRejectedThroughAMemberReference()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                nestedRulesTypeDefinition: true,
                localRulesMemberRefConstructor: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var rules =
            Assert.IsType<MemorySafetyRulesResult.Available>(index.Rules);
        Assert.Equal(MemorySafetyRulesState.Legacy, rules.State);
        Assert.Empty(rules.Observations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedRulesCarrierCannotAliasTopLevelMarker(
        bool typeReference)
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                nestedRulesTypeDefinition: !typeReference,
                nestedRulesTypeReference: typeReference));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var rules =
            Assert.IsType<MemorySafetyRulesResult.Available>(index.Rules);
        Assert.Equal(MemorySafetyRulesState.Legacy, rules.State);
        Assert.Empty(rules.Observations);
        Assert.IsType<MemorySafetyMemberContractResult.Implicit>(
            index.GetMemberContract(
                FindMethod(
                    opened.Reader,
                    "Samples.Target",
                    "PointerOnly")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedRequiresUnsafeCarrierCannotAliasTopLevelContract(
        bool typeReference)
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                nestedRequiresUnsafeTypeDefinition: !typeReference,
                nestedRequiresUnsafeTypeReference: typeReference));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.None>(
                index.GetMemberContract(
                    FindMethod(
                        opened.Reader,
                        "Samples.Target",
                        "AttributeOnly")));
        Assert.False(result.Evidence.DirectAttribute.HasValidRow);
        Assert.False(result.Evidence.DirectAttribute.HasMalformedRow);
    }

    [Fact]
    public void UnsortedCustomAttributeRowsFailClosed()
    {
        using OpenedMetadata opened = Open(
            WithSwappedCustomAttributeRows(BuildSyntheticImage([2])));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var rules =
            Assert.IsType<MemorySafetyRulesResult.Unavailable>(index.Rules);
        Assert.Equal(
            MemorySafetyMetadataFailureKind.Malformed,
            rules.Failure.Kind);

        var member =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    MetadataTokens.MethodDefinitionHandle(4)));
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.MetadataUnavailable,
            member.Failure.Kind);
    }

    [Theory]
    [InlineData("System.Int32", true)]
    [InlineData(
        "System.Int32, System.Runtime, Version=11.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
        true)]
    [InlineData(
        "System.Int32, Attacker, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        false)]
    [InlineData(
        "System.Int32, System.Runtime, Version=11.0.0.0, Culture=neutral, PublicKeyToken=0123456789abcdef",
        false)]
    public void FixedBufferExemptionRequiresPlatformElementTypeIdentity(
        string serializedElementType,
        bool exempts)
    {
        using OpenedMetadata opened = Open(
            BuildFixedBufferElementTypeImage(serializedElementType));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        MemorySafetyMemberContractResult result =
            index.GetMemberContract(
                MetadataTokens.FieldDefinitionHandle(1));
        if (exempts)
        {
            var none =
                Assert.IsType<MemorySafetyMemberContractResult.None>(result);
            Assert.Equal(
                MemorySafetyFixedBufferEvidence.Present,
                none.Evidence.FixedBuffer);
            return;
        }

        var propagated =
            Assert.IsType<MemorySafetyMemberContractResult.Implicit>(result);
        Assert.Equal(
            MemorySafetyPointerEvidence.Present,
            propagated.Evidence.Pointer);
        Assert.Equal(
            MemorySafetyFixedBufferEvidence.Unavailable,
            propagated.Evidence.FixedBuffer);
    }

    [Theory]
    [InlineData("System.Runtime", true)]
    [InlineData("mscorlib", true)]
    [InlineData("netstandard", true)]
    [InlineData("Attacker", false)]
    [InlineData("System.Runtime.Extensions", false)]
    public void FixedBufferExemptionRequiresACoreContractCarrier(
        string carrierAssemblyName,
        bool exempts)
    {
        using OpenedMetadata opened = Open(
            BuildFixedBufferElementTypeImage(
                "System.Int32",
                undecodableFieldSignature: false,
                carrierAssemblyName));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        MemorySafetyMemberContractResult result =
            index.GetMemberContract(
                MetadataTokens.FieldDefinitionHandle(1));
        if (exempts)
        {
            var none =
                Assert.IsType<MemorySafetyMemberContractResult.None>(result);
            Assert.Equal(
                MemorySafetyFixedBufferEvidence.Present,
                none.Evidence.FixedBuffer);
            return;
        }

        var propagated =
            Assert.IsType<MemorySafetyMemberContractResult.Implicit>(result);
        Assert.Equal(
            MemorySafetyPointerEvidence.Present,
            propagated.Evidence.Pointer);
        Assert.Equal(
            MemorySafetyFixedBufferEvidence.Absent,
            propagated.Evidence.FixedBuffer);
    }

    [Fact]
    public void FixedBufferCarrierCannotSuppressAnUndecodableSignature()
    {
        using OpenedMetadata opened = Open(
            BuildFixedBufferElementTypeImage(
                "System.Int32",
                undecodableFieldSignature: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    MetadataTokens.FieldDefinitionHandle(1)));
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.SignatureUnavailable,
            result.Failure.Kind);
        Assert.Equal(
            MemorySafetyPointerEvidence.Unavailable,
            result.Evidence.Pointer);
        Assert.Equal(
            MemorySafetyFixedBufferEvidence.Present,
            result.Evidence.FixedBuffer);
    }

    [Fact]
    public void UnobservedMethodSemanticsRowsMakeAssociationsUnavailable()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                duplicatePropertySemantics: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        Assert.NotNull(index.AssociationFailure);
        Assert.Equal(
            MemorySafetyMetadataFailureKind.Malformed,
            index.AssociationFailure!.Kind);

        var accessor =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    FindMethod(
                        opened.Reader,
                        "Samples.Target",
                        "get_AssociatedProperty")));
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.MetadataUnavailable,
            accessor.Failure.Kind);

        // A direct carrier never consults the association map, so it still
        // decides the contract while the projection is untrustworthy.
        Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
            index.GetMemberContract(
                FindMethod(
                    opened.Reader,
                    "Samples.Target",
                    "AttributeOnly")));
    }

    [Theory]
    [InlineData(nameof(ContractFixtures.PropertyContract))]
    [InlineData(nameof(ContractFixtures.EventContract))]
    public void CompilerProducedAccessorsCarryDirectContracts(
        string memberName)
    {
        using OpenedMetadata opened = Open(typeof(ContractFixtures));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);
        EntityHandle member = FindPropertyOrEvent(
            opened.Reader,
            typeof(ContractFixtures).FullName!,
            memberName);

        Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
            index.GetMemberContract(member));

        MethodDefinitionHandle accessor = member.Kind switch
        {
            HandleKind.PropertyDefinition =>
                opened.Reader.GetPropertyDefinition(
                    (PropertyDefinitionHandle)member).GetAccessors().Getter,
            HandleKind.EventDefinition =>
                opened.Reader.GetEventDefinition(
                    (EventDefinitionHandle)member).GetAccessors().Adder,
            _ => throw new InvalidOperationException(),
        };
        var accessorResult =
            Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
                index.GetMemberContract(accessor));

        Assert.True(
            accessorResult.Evidence.DirectAttribute.HasValidRow);
        Assert.Equal(
            RequiresUnsafeAttributeEvidenceState.NotExamined,
            accessorResult.Evidence.AssociatedAttribute.State);
    }

    [Theory]
    [InlineData("AssociatedProperty")]
    [InlineData("AssociatedEvent")]
    public void AccessorFallsBackToAssociatedDefinitionCarrier(
        string memberName)
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2]));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);
        EntityHandle member = FindPropertyOrEvent(
            opened.Reader,
            "Samples.Target",
            memberName);
        MethodDefinitionHandle accessor = member.Kind switch
        {
            HandleKind.PropertyDefinition =>
                opened.Reader.GetPropertyDefinition(
                    (PropertyDefinitionHandle)member).GetAccessors().Getter,
            HandleKind.EventDefinition =>
                opened.Reader.GetEventDefinition(
                    (EventDefinitionHandle)member).GetAccessors().Adder,
            _ => throw new InvalidOperationException(),
        };

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
                index.GetMemberContract(accessor));
        Assert.False(result.Evidence.DirectAttribute.HasValidRow);
        Assert.True(result.Evidence.AssociatedAttribute.HasValidRow);
        Assert.Equal(
            MetadataTokens.GetToken(member),
            result.Evidence.AssociatedMemberToken);
    }

    /// <summary>
    /// R2: identity resolution binary-searches NestedClass, so a physical order
    /// contradicting the sorted claim hides the row that proves a carrier is
    /// nested. The spoofed carrier then reads as the top-level marker and flips
    /// the module to Updated, under which an unmarked member reports "no
    /// unsafety" from evidence that was never observed. Proving the projection
    /// makes the image fail closed instead.
    /// </summary>
    [Fact]
    public void UnorderedNestedClassRowsFailClosed()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                nestedRulesCarrierSpoof: true,
                localRulesMemberRefConstructor: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var rules =
            Assert.IsType<MemorySafetyRulesResult.Unavailable>(index.Rules);
        Assert.Equal(
            MemorySafetyMetadataFailureKind.Malformed,
            rules.Failure.Kind);
    }

    /// <summary>
    /// The control for <see cref="UnorderedNestedClassRowsFailClosed"/>: the
    /// same spoofed carrier with the rows in order stays readable, and
    /// structured identity rejects it on its own terms, so the module reads
    /// Legacy rather than failing.
    /// </summary>
    [Fact]
    public void OrderedNestedClassRowsRejectASpoofedNestedCarrier()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                nestedRulesCarrierSpoof: true,
                nestedClassOrdered: true,
                localRulesMemberRefConstructor: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var rules =
            Assert.IsType<MemorySafetyRulesResult.Available>(index.Rules);
        Assert.Equal(MemorySafetyRulesState.Legacy, rules.State);
    }

    [Fact]
    public void CrossTypeAccessorSemanticsDoesNotCarryAnAssociatedCarrier()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2], crossTypeAccessorSemantics: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);
        var property = (PropertyDefinitionHandle)FindPropertyOrEvent(
            opened.Reader,
            "Samples.Target",
            "AssociatedProperty");
        MethodDefinitionHandle foreignAccessor = opened.Reader
            .GetPropertyDefinition(property).GetAccessors().Getter;

        // The row names a method declared by the marker attribute type, so it
        // must not inherit the property's carrier from another type.
        Assert.NotEqual(
            MetadataTokens.GetToken(
                opened.Reader
                    .GetPropertyDefinition(property).GetDeclaringType()),
            MetadataTokens.GetToken(
                opened.Reader
                    .GetMethodDefinition(foreignAccessor).GetDeclaringType()));

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.None>(
                index.GetMemberContract(foreignAccessor));
        Assert.False(result.Evidence.AssociatedAttribute.HasValidRow);
        Assert.Null(result.Evidence.AssociatedMemberToken);
        Assert.Equal(
            MemorySafetyMetadataFailureKind.Malformed,
            index.AssociationFailure?.Kind);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(-1)]
    public void UnsupportedMarkersUseLegacyCompatibilityInference(
        int version)
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([version]));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.Rules);

        Assert.Equal(MemorySafetyRulesState.Unsupported, rules.State);
        Assert.Equal(version, Assert.Single(rules.Observations).Version);
        Assert.IsType<MemorySafetyMemberContractResult.Implicit>(
            index.GetMemberContract(
                FindMethod(
                    opened.Reader,
                    "Samples.Target",
                    "PointerOnly")));
        var attributeOnly =
            Assert.IsType<MemorySafetyMemberContractResult.None>(
            index.GetMemberContract(
                FindMethod(
                    opened.Reader,
                    "Samples.Target",
                    "AttributeOnly")));
        Assert.True(
            attributeOnly.Evidence.DirectAttribute.HasValidRow);
    }

    [Fact]
    public void MalformedMarkerUsesLegacyCompatibilityInference()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([null]));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.Rules);

        Assert.Equal(MemorySafetyRulesState.Malformed, rules.State);
        Assert.Equal(
            MemorySafetyRulesObservationState.Malformed,
            Assert.Single(rules.Observations).State);
        Assert.IsType<MemorySafetyMemberContractResult.Implicit>(
            index.GetMemberContract(
                FindMethod(
                    opened.Reader,
                    "Samples.Target",
                    "PointerOnly")));
        var attributeOnly =
            Assert.IsType<MemorySafetyMemberContractResult.None>(
            index.GetMemberContract(
                FindMethod(
                    opened.Reader,
                    "Samples.Target",
                    "AttributeOnly")));
        Assert.True(
            attributeOnly.Evidence.DirectAttribute.HasValidRow);
    }

    [Fact]
    public void
        MemorySafetyMetadataIndex_DuplicateIdenticalMarkersRetainEvidence()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2, 2]));
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            MemorySafetyMetadataIndex.Create(opened.Reader).Rules);

        Assert.Equal(MemorySafetyRulesState.Updated, rules.State);
        Assert.Equal(2, rules.Observations.Length);
        Assert.All(
            rules.Observations,
            observation => Assert.Equal(2, observation.Version));
    }

    [Fact]
    public void
        MemorySafetyMetadataIndex_ConflictingMarkersMakeContractsUnavailable()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2, 1]));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            index.Rules);

        Assert.Equal(MemorySafetyRulesState.Conflicting, rules.State);
        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    FindMethod(
                        opened.Reader,
                        "Samples.Target",
                        "AttributeOnly")));
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.ConflictingRules,
            result.Failure.Kind);
        Assert.True(result.Evidence.DirectAttribute.HasValidRow);
    }

    [Fact]
    public void NonModuleMarkersDoNotSelectTheModuleModel()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                moduleMarkers: [],
                addInvalidScopeMarkers: true));
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            MemorySafetyMetadataIndex.Create(opened.Reader).Rules);

        Assert.Equal(MemorySafetyRulesState.Legacy, rules.State);
        Assert.Empty(rules.Observations);
    }

    [Fact]
    public void MalformedRequiresUnsafeCarrierIsUnavailableUnderUpdatedRules()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                malformedRequiresUnsafe: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);
        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    FindMethod(
                        opened.Reader,
                        "Samples.Target",
                        "AttributeOnly")));

        Assert.Equal(
            MemorySafetyMemberContractFailureKind
                .MalformedRequiresUnsafeAttribute,
            result.Failure.Kind);
        Assert.True(result.Evidence.DirectAttribute.HasMalformedRow);
    }

    [Fact]
    public void DirectAccessorCarrierWinsBeforeAssociatedFallback()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                [2],
                directAccessorCarrier: true,
                malformedAssociatedCarrier: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);
        PropertyDefinitionHandle property =
            (PropertyDefinitionHandle)FindPropertyOrEvent(
                opened.Reader,
                "Samples.Target",
                "AssociatedProperty");
        MethodDefinitionHandle getter =
            opened.Reader.GetPropertyDefinition(property)
                .GetAccessors().Getter;

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
                index.GetMemberContract(getter));
        Assert.True(result.Evidence.DirectAttribute.HasValidRow);
        Assert.Equal(
            RequiresUnsafeAttributeEvidenceState.NotExamined,
            result.Evidence.AssociatedAttribute.State);
    }

    [Fact]
    public void MalformedAttributeConstructorsAreNotAccepted()
    {
        using OpenedMetadata malformedRules = Open(
            BuildSyntheticImage(
                [2],
                malformedRulesConstructor: true));
        var rules = Assert.IsType<MemorySafetyRulesResult.Available>(
            MemorySafetyMetadataIndex.Create(
                malformedRules.Reader).Rules);
        Assert.Equal(MemorySafetyRulesState.Malformed, rules.State);

        using OpenedMetadata malformedCarrier = Open(
            BuildSyntheticImage(
                [2],
                malformedRequiresUnsafeConstructor: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(malformedCarrier.Reader);
        var member =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    FindMethod(
                        malformedCarrier.Reader,
                        "Samples.Target",
                        "AttributeOnly")));
        Assert.Equal(
            MemorySafetyMemberContractFailureKind
                .MalformedRequiresUnsafeAttribute,
            member.Failure.Kind);
    }

    [Fact]
    public void MemorySafetyMetadataIndex_InvalidHandlesAreUnavailable()
    {
        using OpenedMetadata opened = Open(typeof(ContractFixtures));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        Assert.Equal(
            MemorySafetyMemberContractFailureKind.InvalidHandle,
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(default)).Failure.Kind);
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.InvalidHandle,
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    MetadataTokens.TypeDefinitionHandle(1))).Failure.Kind);
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.InvalidHandle,
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    MetadataTokens.MethodDefinitionHandle(
                        opened.Reader.GetTableRowCount(
                            TableIndex.MethodDef) + 1))).Failure.Kind);
    }

    [Fact]
    public void ModuleMarkerScanBudgetFailureIsTyped()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2, 2]));
        var rules =
            Assert.IsType<MemorySafetyRulesResult.Unavailable>(
                MemorySafetyMetadataIndex.Create(
                    opened.Reader,
                    associationRowBudget: 100,
                    attributeRowBudget: 1).Rules);

        Assert.Equal(
            MemorySafetyMetadataFailureKind.BudgetExceeded,
            rules.Failure.Kind);
    }

    [Fact]
    public void AccessorAssociationBudgetFailureIsTyped()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2]));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(
                opened.Reader,
                associationRowBudget: 2,
                attributeRowBudget: 100);
        MethodDefinitionHandle getter =
            opened.Reader.GetPropertyDefinition(
                (PropertyDefinitionHandle)FindPropertyOrEvent(
                    opened.Reader,
                    "Samples.Target",
                    "AssociatedProperty"))
                .GetAccessors().Getter;

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(getter));
        Assert.Equal(
            MemorySafetyMetadataFailureKind.BudgetExceeded,
            index.AssociationFailure?.Kind);
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.MetadataUnavailable,
            result.Failure.Kind);
    }

    [Fact]
    public void AttributeNameWorkBudgetFailureIsTyped()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2]));
        var rules =
            Assert.IsType<MemorySafetyRulesResult.Unavailable>(
                MemorySafetyMetadataIndex.Create(
                    opened.Reader,
                    associationRowBudget: 100,
                    attributeRowBudget: 100,
                    nameWorkBudget: 1).Rules);

        Assert.Equal(
            MemorySafetyMetadataFailureKind.BudgetExceeded,
            rules.Failure.Kind);
    }

    [Fact]
    public void MalformedLegacySignatureIsUnavailable()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage(
                moduleMarkers: [],
                malformedPointerSignature: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        var result =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                index.GetMemberContract(
                    FindMethod(
                        opened.Reader,
                        "Samples.Target",
                        "PointerOnly")));
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.SignatureUnavailable,
            result.Failure.Kind);
        Assert.Equal(
            MemorySafetyPointerEvidence.Unavailable,
            result.Evidence.Pointer);
    }

    static OpenedMetadata Open(Type type)
        => Open(File.ReadAllBytes(type.Assembly.Location));

    static void AssertAvailable(MemorySafetyRulesResult result)
    {
        if (result is MemorySafetyRulesResult.Unavailable unavailable)
        {
            Assert.Fail(
                $"{unavailable.Failure.Kind}: {unavailable.Failure.Detail}");
        }
    }

    static OpenedMetadata Open(byte[] image)
        => new(image);

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string fullTypeName,
        string methodName)
    {
        TypeDefinition type = reader.GetTypeDefinition(
            FindType(reader, fullTypeName));
        return Assert.Single(
            type.GetMethods(),
            handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                methodName));
    }

    static FieldDefinitionHandle FindField(
        MetadataReader reader,
        string fullTypeName,
        string fieldName)
    {
        TypeDefinition type = reader.GetTypeDefinition(
            FindType(reader, fullTypeName));
        return Assert.Single(
            type.GetFields(),
            handle => reader.StringComparer.Equals(
                reader.GetFieldDefinition(handle).Name,
                fieldName));
    }

    static EntityHandle FindPropertyOrEvent(
        MetadataReader reader,
        string fullTypeName,
        string memberName)
    {
        TypeDefinition type = reader.GetTypeDefinition(
            FindType(reader, fullTypeName));
        foreach (PropertyDefinitionHandle handle in type.GetProperties())
        {
            if (reader.StringComparer.Equals(
                    reader.GetPropertyDefinition(handle).Name,
                    memberName))
            {
                return handle;
            }
        }

        foreach (EventDefinitionHandle handle in type.GetEvents())
        {
            if (reader.StringComparer.Equals(
                    reader.GetEventDefinition(handle).Name,
                    memberName))
            {
                return handle;
            }
        }

        throw new InvalidOperationException(
            $"Member '{fullTypeName}.{memberName}' was not found.");
    }

    static TypeDefinitionHandle FindType(
        MetadataReader reader,
        string fullName)
    {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            string ns = reader.GetString(type.Namespace);
            if ((ns.Length == 0 ? name : $"{ns}.{name}") == fullName)
                return handle;
        }

        throw new InvalidOperationException(
            $"Type '{fullName}' was not found.");
    }

    static byte[] BuildSyntheticImage(
        int?[] moduleMarkers,
        bool addInvalidScopeMarkers = false,
        bool malformedRequiresUnsafe = false,
        bool malformedPointerSignature = false,
        bool directAccessorCarrier = false,
        bool malformedAssociatedCarrier = false,
        bool malformedRulesConstructor = false,
        bool malformedRequiresUnsafeConstructor = false,
        bool nestedRulesTypeDefinition = false,
        bool nestedRequiresUnsafeTypeDefinition = false,
        bool nestedRulesTypeReference = false,
        bool nestedRequiresUnsafeTypeReference = false,
        bool localRulesMemberRefConstructor = false,
        bool crossTypeAccessorSemantics = false,
        bool nestedRulesCarrierSpoof = false,
        bool eventAdderIsOrdinaryMethod = false,
        bool nestedClassOrdered = false,
        bool duplicatePropertySemantics = false)
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
            0,
            metadata.GetOrAddString("MemorySafetySynthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        AssemblyDefinitionHandle assembly = metadata.AddAssembly(
            metadata.GetOrAddString("MemorySafetySynthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        BlobHandle rulesConstructorSignature =
            AddMethodSignature(
                metadata,
                isInstance: true,
                parameterCount: 1,
                parameters =>
                    parameters.AddParameter().Type().Int32());
        BlobHandle markerConstructorSignature =
            AddMethodSignature(
                metadata,
                isInstance: true,
                parameterCount: 0,
                _ => { });
        BlobHandle pointerMethodSignature =
            malformedPointerSignature
                ? metadata.GetOrAddBlob(
                    new byte[] { 0x00, 0x01, 0x01 })
                : AddMethodSignature(
                    metadata,
                    isInstance: false,
                    parameterCount: 1,
                    parameters =>
                        parameters.AddParameter().Type().Pointer().Int32());
        BlobHandle emptyMethodSignature =
            AddMethodSignature(
                metadata,
                isInstance: false,
                parameterCount: 0,
                _ => { });
        var propertyGetterSignatureBuilder = new BlobBuilder();
        new BlobEncoder(propertyGetterSignatureBuilder)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Type().Int32(),
                _ => { });
        BlobHandle propertyGetterSignature =
            metadata.GetOrAddBlob(propertyGetterSignatureBuilder);
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle actionType = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Action"));
        BlobHandle eventAccessorSignature =
            AddMethodSignature(
                metadata,
                isInstance: false,
                parameterCount: 1,
                parameters => parameters.AddParameter().Type().Type(
                    actionType,
                    isValueType: false));

        MethodDefinitionHandle rulesConstructor =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString(
                    malformedRulesConstructor
                        ? "NotConstructor"
                        : ".ctor"),
                rulesConstructorSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle requiresUnsafeConstructor =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString(
                    malformedRequiresUnsafeConstructor
                        ? "NotConstructor"
                        : ".ctor"),
                markerConstructorSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle pointerOnly =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString("PointerOnly"),
                pointerMethodSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle attributeOnly =
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString("AttributeOnly"),
                emptyMethodSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle propertyGetter =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString("get_AssociatedProperty"),
                propertyGetterSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle eventAdder =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString("add_AssociatedEvent"),
                eventAccessorSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle eventRemover =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString("remove_AssociatedEvent"),
                eventAccessorSignature,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));

        EntityHandle rulesCarrierConstructor = rulesConstructor;
        if (nestedRulesTypeReference)
        {
            TypeReferenceHandle outer = metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("System.Runtime"),
                metadata.GetOrAddString("CompilerServices"));
            TypeReferenceHandle nested = metadata.AddTypeReference(
                outer,
                default,
                metadata.GetOrAddString("MemorySafetyRulesAttribute"));
            rulesCarrierConstructor = metadata.AddMemberReference(
                nested,
                metadata.GetOrAddString(".ctor"),
                rulesConstructorSignature);
        }

        EntityHandle requiresUnsafeCarrierConstructor =
            requiresUnsafeConstructor;
        if (nestedRequiresUnsafeTypeReference)
        {
            TypeReferenceHandle outer = metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("System.Diagnostics"),
                metadata.GetOrAddString("CodeAnalysis"));
            TypeReferenceHandle nested = metadata.AddTypeReference(
                outer,
                default,
                metadata.GetOrAddString("RequiresUnsafeAttribute"));
            requiresUnsafeCarrierConstructor = metadata.AddMemberReference(
                nested,
                metadata.GetOrAddString(".ctor"),
                markerConstructorSignature);
        }

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            rulesConstructor);
        TypeDefinitionHandle localRulesType;
        if (nestedRulesCarrierSpoof)
        {
            // A genuinely nested carrier that spells the top-level marker in
            // its OWN namespace and name. Structured identity rejects it while
            // NestedClass is readable. A second nested pair lets the rows be
            // written out of order while the sorted claim stands, which is what
            // makes SRM's binary search miss the spoof's row.
            TypeDefinitionHandle outerA = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Spoof"),
                metadata.GetOrAddString("OuterA"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                rulesConstructor);
            TypeDefinitionHandle spoof = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                metadata.GetOrAddString("System.Runtime.CompilerServices"),
                metadata.GetOrAddString("MemorySafetyRulesAttribute"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                rulesConstructor);
            TypeDefinitionHandle outerB = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Spoof"),
                metadata.GetOrAddString("OuterB"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                rulesConstructor);
            TypeDefinitionHandle other = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Other"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                rulesConstructor);
            if (nestedClassOrdered)
            {
                metadata.AddNestedType(spoof, outerA);
                metadata.AddNestedType(other, outerB);
            }
            else
            {
                metadata.AddNestedType(other, outerB);
                metadata.AddNestedType(spoof, outerA);
            }
            localRulesType = spoof;
        }
        else if (nestedRulesTypeDefinition)
        {
            TypeDefinitionHandle outer = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("System.Runtime"),
                metadata.GetOrAddString("CompilerServices"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                rulesConstructor);
            TypeDefinitionHandle nested = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("MemorySafetyRulesAttribute"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                rulesConstructor);
            metadata.AddNestedType(nested, outer);
            localRulesType = nested;
        }
        else
        {
            localRulesType = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("MemorySafetyRulesAttribute"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                rulesConstructor);
        }
        if (localRulesMemberRefConstructor)
        {
            // ECMA-335 lets a MemberRef name a member of a TypeDef in the same
            // module, so this is a legal alternate spelling of the same local
            // carrier rather than a foreign reference.
            rulesCarrierConstructor = metadata.AddMemberReference(
                localRulesType,
                metadata.GetOrAddString(".ctor"),
                rulesConstructorSignature);
        }
        if (nestedRequiresUnsafeTypeDefinition)
        {
            TypeDefinitionHandle outer = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("System.Diagnostics"),
                metadata.GetOrAddString("CodeAnalysis"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                requiresUnsafeConstructor);
            TypeDefinitionHandle nested = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("RequiresUnsafeAttribute"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                requiresUnsafeConstructor);
            metadata.AddNestedType(nested, outer);
        }
        else
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString(
                    "System.Diagnostics.CodeAnalysis"),
                metadata.GetOrAddString("RequiresUnsafeAttribute"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                requiresUnsafeConstructor);
        }
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            pointerOnly);
        var propertySignatureBuilder = new BlobBuilder();
        new BlobEncoder(propertySignatureBuilder)
            .PropertySignature(isInstanceProperty: false)
            .Parameters(
                0,
                returnType => returnType.Type().Int32(),
                _ => { });
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("AssociatedProperty"),
            metadata.GetOrAddBlob(propertySignatureBuilder));
        metadata.AddPropertyMap(target, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            crossTypeAccessorSemantics
                ? rulesConstructor
                : propertyGetter);
        if (duplicatePropertySemantics)
        {
            // PropertyAccessors keeps one Getter slot, so this second row is
            // dropped from the projection without any error surfacing.
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Getter,
                attributeOnly);
        }
        EventDefinitionHandle @event = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("AssociatedEvent"),
            actionType);
        metadata.AddEventMap(target, @event);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Adder,
            eventAdderIsOrdinaryMethod ? pointerOnly : eventAdder);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Remover,
            eventRemover);

        foreach (int? marker in moduleMarkers)
        {
            metadata.AddCustomAttribute(
                module,
                rulesCarrierConstructor,
                metadata.GetOrAddBlob(
                    marker is int version
                        ? RulesBlob(version)
                        : [0x01, 0x00, 0x02]));
        }

        if (addInvalidScopeMarkers)
        {
            metadata.AddCustomAttribute(
                assembly,
                rulesCarrierConstructor,
                metadata.GetOrAddBlob(RulesBlob(2)));
            metadata.AddCustomAttribute(
                target,
                rulesCarrierConstructor,
                metadata.GetOrAddBlob(RulesBlob(2)));
            metadata.AddCustomAttribute(
                attributeOnly,
                rulesCarrierConstructor,
                metadata.GetOrAddBlob(RulesBlob(2)));
        }

        metadata.AddCustomAttribute(
            attributeOnly,
            requiresUnsafeCarrierConstructor,
            metadata.GetOrAddBlob(
                malformedRequiresUnsafe
                    ? new byte[] { 0x01, 0x00, 0x01 }
                    : new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        metadata.AddCustomAttribute(
            property,
            requiresUnsafeCarrierConstructor,
            metadata.GetOrAddBlob(
                malformedAssociatedCarrier
                    ? new byte[] { 0x01, 0x00, 0x01 }
                    : new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        metadata.AddCustomAttribute(
            @event,
            requiresUnsafeCarrierConstructor,
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        if (directAccessorCarrier)
        {
            metadata.AddCustomAttribute(
                propertyGetter,
                requiresUnsafeCarrierConstructor,
                metadata.GetOrAddBlob(
                    new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildFixedBufferLookalikeImage(
        int attributeCount)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("FixedBufferLookalike.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("FixedBufferLookalike"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    Convert.FromHexString("B03F5F7F11D50A3A")),
                default,
                default);
        TypeReferenceHandle fixedBufferAttribute =
            metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("FixedBufferAttribute"));
        BlobHandle malformedConstructorSignature =
            AddMethodSignature(
                metadata,
                isInstance: true,
                parameterCount: 0,
                _ => { });
        MemberReferenceHandle malformedConstructor =
            metadata.AddMemberReference(
                fixedBufferAttribute,
                metadata.GetOrAddString(".ctor"),
                malformedConstructorSignature);
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature)
            .FieldSignature()
            .Pointer()
            .Int32();
        FieldDefinitionHandle field =
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                metadata.GetOrAddString("Pointer"),
                metadata.GetOrAddBlob(fieldSignature));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            field,
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            field,
            MetadataTokens.MethodDefinitionHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString("System.Int32");
        value.WriteInt32(4);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        for (int index = 0; index < attributeCount; index++)
        {
            metadata.AddCustomAttribute(
                field,
                malformedConstructor,
                valueHandle);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildFixedBufferElementTypeImage(
        string serializedElementType)
        => BuildFixedBufferElementTypeImage(
            serializedElementType,
            undecodableFieldSignature: false);

    static byte[] BuildFixedBufferElementTypeImage(
        string serializedElementType,
        bool undecodableFieldSignature)
        => BuildFixedBufferElementTypeImage(
            serializedElementType,
            undecodableFieldSignature,
            carrierAssemblyName: "System.Runtime");

    static byte[] BuildFixedBufferElementTypeImage(
        string serializedElementType,
        bool undecodableFieldSignature,
        string carrierAssemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("FixedBufferElementType.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("FixedBufferElementType"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                metadata.GetOrAddBlob(
                    Convert.FromHexString("B03F5F7F11D50A3A")),
                default,
                default);
        AssemblyReferenceHandle carrierAssembly =
            carrierAssemblyName == "System.Runtime"
                ? coreLibrary
                : metadata.AddAssemblyReference(
                    metadata.GetOrAddString(carrierAssemblyName),
                    new Version(11, 0, 0, 0),
                    default,
                    metadata.GetOrAddBlob(
                        Convert.FromHexString("B03F5F7F11D50A3A")),
                    default,
                    default);
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Type"));
        TypeReferenceHandle fixedBufferAttribute =
            metadata.AddTypeReference(
                carrierAssembly,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("FixedBufferAttribute"));
        BlobHandle constructorSignature = AddMethodSignature(
            metadata,
            isInstance: true,
            parameterCount: 2,
            parameters =>
            {
                parameters.AddParameter().Type().Type(
                    systemType,
                    isValueType: false);
                parameters.AddParameter().Type().Int32();
            });
        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                fixedBufferAttribute,
                metadata.GetOrAddString(".ctor"),
                constructorSignature);
        var fieldSignature = new BlobBuilder();
        if (undecodableFieldSignature)
        {
            // A pointer chain deep enough for the signature guard to refuse it
            // before any decode, so pointer evidence is Unavailable rather than
            // definitely present or absent.
            fieldSignature.WriteByte(0x06);
            for (int depth = 0; depth < 70_000; depth++)
                fieldSignature.WriteByte(0x0F);
            fieldSignature.WriteByte(0x08);
        }
        else
        {
            new BlobEncoder(fieldSignature)
                .FieldSignature()
                .Pointer()
                .Int32();
        }

        FieldDefinitionHandle field =
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                metadata.GetOrAddString("Buffer"),
                metadata.GetOrAddBlob(fieldSignature));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            field,
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            field,
            MetadataTokens.MethodDefinitionHandle(1));

        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(serializedElementType);
        value.WriteInt32(4);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            field,
            constructor,
            metadata.GetOrAddBlob(value));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    /// <summary>
    /// Swaps the first two physical CustomAttribute rows while leaving the
    /// tables stream's sorted claim intact, reproducing an image whose owner
    /// ranges SRM answers with a binary search over unsorted rows.
    /// </summary>
    static byte[] WithSwappedCustomAttributeRows(byte[] image)
    {
        byte[] mutated = (byte[])image.Clone();
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        Assert.True(
            reader.GetTableRowCount(TableIndex.CustomAttribute) >= 2);
        int rowSize = reader.GetTableRowSize(TableIndex.CustomAttribute);
        int offset =
            pe.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.CustomAttribute);
        Span<byte> rows = mutated.AsSpan(offset, rowSize * 2);
        byte[] scratch = rows[..rowSize].ToArray();
        rows[rowSize..].CopyTo(rows[..rowSize]);
        scratch.CopyTo(rows[rowSize..]);
        return mutated;
    }

    static BlobHandle AddMethodSignature(
        MetadataBuilder metadata,
        bool isInstance,
        int parameterCount,
        Action<ParametersEncoder> encodeParameters)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: isInstance)
            .Parameters(
                parameterCount,
                returnType => returnType.Void(),
                encodeParameters);
        return metadata.GetOrAddBlob(signature);
    }

    static byte[] RulesBlob(int version)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteInt32(version);
        blob.WriteUInt16(0);
        return blob.ToArray();
    }

    sealed class OpenedMetadata : IDisposable
    {
        readonly MemoryStream _stream;
        readonly PEReader _pe;

        public OpenedMetadata(byte[] image)
        {
            _stream = new(image, writable: false);
            _pe = new(_stream);
            Reader = _pe.GetMetadataReader();
        }

        public MetadataReader Reader { get; }

        public void Dispose()
        {
            _pe.Dispose();
            _stream.Dispose();
        }
    }

    /// <summary>
    /// R3: same-type ownership proves a semantics row names a member of the
    /// right type, not that the member can be that accessor. A row naming an
    /// ordinary method still projects, so without validating the relationship
    /// the event's carrier would inherit to a method that is not an accessor
    /// and report Explicit.
    /// </summary>
    [Fact]
    public void OrdinaryMethodNamedAsEventAdderInheritsNoCarrier()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2], eventAdderIsOrdinaryMethod: true));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.Create(opened.Reader);

        MethodDefinitionHandle pointerOnly = FindMethod(
            opened.Reader,
            "PointerOnly");
        var result =
            Assert.IsType<MemorySafetyMemberContractResult.None>(
                index.GetMemberContract(pointerOnly));

        Assert.Null(result.Evidence.AssociatedMemberToken);
        Assert.NotNull(index.AssociationFailure);
    }

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string name)
    {
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            if (reader.GetString(reader.GetMethodDefinition(handle).Name)
                == name)
            {
                return handle;
            }
        }

        throw new InvalidOperationException($"{name} not found.");
    }


}
