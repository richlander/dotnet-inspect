extern alias legacyunsafe;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

using ContractFixtures =
    ILInspector.Metadata.MemorySafetyFixtures.MemorySafetyFixtures;
using LegacyFixtures =
    legacyunsafe::ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;

namespace ILInspector.Metadata.Tests;

public sealed partial class MemorySafetyMetadataIndexTests
{
    public static TheoryData<string> ReceiptFixtureNames =>
    [
        "legacy",
        "compiler",
        "updated",
        "conflicting",
        "malformed-marker",
        "malformed-pointer",
        "direct-accessor-carrier",
        "malformed-associated-carrier",
        "cross-type-accessor",
        "unobserved-semantics",
        "ordinary-event-adder",
        "setter-getter-arity",
        "shared-getter",
        "unsorted-attributes",
        "unordered-nested-class",
        "orphaned-methods",
        "association-budget",
        "attribute-budget",
        "name-budget",
    ];

    [Theory]
    [MemberData(nameof(ReceiptFixtureNames))]
    public void MemorySafetyMetadataIndex_ReceiptedMatchesUnreceiptedContracts(
        string fixture)
    {
        (byte[] image, int associationRows, int attributeRows, int nameChars) =
            ReceiptFixture(fixture);
        using OpenedMetadata opened = Open(image);
        MemorySafetyMetadataIndex plain = MemorySafetyMetadataIndex.Create(
            opened.Reader,
            associationRows,
            attributeRows,
            nameChars);
        MemorySafetyMetadataIndex receipted =
            MemorySafetyMetadataIndex.Create(
                opened.Reader,
                associationRows,
                attributeRows,
                nameChars,
                new MemorySafetyMetadataWorkRecorder());

        Assert.Null(plain.ConstructionWork);
        Assert.Null(plain.RecordedWork);
        Assert.NotNull(receipted.ConstructionWork);
        AssertSameRules(plain.Rules, receipted.Rules);
        Assert.Equal(plain.AssociationFailure, receipted.AssociationFailure);
        foreach (EntityHandle member in AllMembers(opened.Reader))
        {
            Assert.Equal(
                plain.GetMemberContract(member),
                receipted.GetMemberContract(member));
            if (member.Kind == HandleKind.MethodDefinition)
            {
                var method = (MethodDefinitionHandle)member;
                Assert.Equal(
                    plain.GetAccessorAssociation(method),
                    receipted.GetAccessorAssociation(method));
            }
        }
    }

    [Fact]
    public void MemorySafetyMetadataIndex_PublishesAccessorAssociationAndRole()
    {
        using OpenedMetadata opened = Open(
            BuildSyntheticImage([2], propertySetterHasGetterArity: false));
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.CreateReceipted(opened.Reader);
        var property = (PropertyDefinitionHandle)FindPropertyOrEvent(
            opened.Reader,
            "Samples.Target",
            "AssociatedProperty");
        var @event = (EventDefinitionHandle)FindPropertyOrEvent(
            opened.Reader,
            "Samples.Target",
            "AssociatedEvent");
        EventAccessors eventAccessors =
            opened.Reader.GetEventDefinition(@event).GetAccessors();

        Assert.Equal(
            new MemorySafetyAccessorAssociationResult.Associated(
                MetadataTokens.GetToken(property),
                MemorySafetyAccessorRole.PropertyGetter),
            index.GetAccessorAssociation(
                opened.Reader.GetPropertyDefinition(property)
                    .GetAccessors().Getter));
        Assert.Equal(
            new MemorySafetyAccessorAssociationResult.Associated(
                MetadataTokens.GetToken(@event),
                MemorySafetyAccessorRole.EventAdder),
            index.GetAccessorAssociation(eventAccessors.Adder));
        Assert.Equal(
            new MemorySafetyAccessorAssociationResult.Associated(
                MetadataTokens.GetToken(@event),
                MemorySafetyAccessorRole.EventRemover),
            index.GetAccessorAssociation(eventAccessors.Remover));
        Assert.IsType<MemorySafetyAccessorAssociationResult.None>(
            index.GetAccessorAssociation(
                FindMethod(opened.Reader, "Samples.Target", "PointerOnly")));
        Assert.IsType<MemorySafetyAccessorAssociationResult.Unavailable>(
            index.GetAccessorAssociation(default));

        // The getter inherits the property's carrier through the association
        // the lookup publishes, so both answers name the same member.
        var inherited =
            Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
                index.GetMemberContract(
                    opened.Reader.GetPropertyDefinition(property)
                        .GetAccessors().Getter));
        Assert.Equal(
            MetadataTokens.GetToken(property),
            inherited.Evidence.AssociatedMemberToken);

        using OpenedMetadata shared = Open(
            BuildSyntheticImage([2], sharedPropertyGetter: true));
        MemorySafetyMetadataIndex sharedIndex =
            MemorySafetyMetadataIndex.CreateReceipted(shared.Reader);
        MethodDefinitionHandle sharedGetter = FindMethod(
            shared.Reader,
            "Samples.Target",
            "get_AssociatedProperty");
        Assert.IsType<MemorySafetyAccessorAssociationResult.Ambiguous>(
            sharedIndex.GetAccessorAssociation(sharedGetter));
        var ambiguous =
            Assert.IsType<MemorySafetyMemberContractResult.Unavailable>(
                sharedIndex.GetMemberContract(sharedGetter));
        Assert.Equal(
            MemorySafetyMemberContractFailureKind.AmbiguousAssociation,
            ambiguous.Failure.Kind);

        using OpenedMetadata unobserved = Open(
            BuildSyntheticImage([2], duplicatePropertySemantics: true));
        MemorySafetyMetadataIndex unobservedIndex =
            MemorySafetyMetadataIndex.CreateReceipted(unobserved.Reader);
        var unavailable =
            Assert.IsType<MemorySafetyAccessorAssociationResult.Unavailable>(
                unobservedIndex.GetAccessorAssociation(
                    FindMethod(
                        unobserved.Reader,
                        "Samples.Target",
                        "get_AssociatedProperty")));
        Assert.Equal(unobservedIndex.AssociationFailure, unavailable.Failure);
    }

    [Fact]
    public void MemorySafetyMetadataIndex_ReceiptsConstructionAndQueryWork()
    {
        using OpenedMetadata opened = Open(BuildSyntheticImage([2]));
        MetadataReader reader = opened.Reader;
        MemorySafetyMetadataIndex index =
            MemorySafetyMetadataIndex.CreateReceipted(reader);
        MemorySafetyMetadataWork construction =
            Assert.IsType<MemorySafetyMetadataWork>(index.ConstructionWork);

        // Integrity validation is receipted before any exact lookup runs.
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.CustomAttribute),
            construction.CustomAttributeOrderRows);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.TypeDef),
            construction.IntegrityTypeDefRows);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.MethodDef),
            construction.IntegrityMethodDefRows);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.NestedClass),
            construction.IntegrityNestedClassRows);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.Property),
            construction.IntegrityPropertyRows);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.Event),
            construction.IntegrityEventRows);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.Property),
            construction.AssociationPropertyRows);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.Event),
            construction.AssociationEventRows);
        Assert.Equal(
            reader.GetTableRowCount(TableIndex.MethodSemantics),
            construction.AssociationMethodSemanticsRows);
        Assert.Equal(
            reader.GetModuleDefinition().GetCustomAttributes().Count,
            construction.ModuleAttributeRows);
        Assert.Equal(0, construction.MemberAttributeRows);
        Assert.True(construction.NameCharacters > 0);
        Assert.True(construction.SignatureBytes > 0);
        Assert.Equal(construction, index.RecordedWork);

        var property = (PropertyDefinitionHandle)FindPropertyOrEvent(
            reader,
            "Samples.Target",
            "AssociatedProperty");
        MethodDefinitionHandle getter =
            reader.GetPropertyDefinition(property).GetAccessors().Getter;
        _ = index.GetMemberContract(getter);
        MemorySafetyMetadataWork query =
            index.RecordedWork!.Since(construction);
        Assert.Equal(
            reader.GetMethodDefinition(getter).GetCustomAttributes().Count
                + reader.GetPropertyDefinition(property)
                    .GetCustomAttributes().Count,
            query.MemberAttributeRows);
        Assert.True(query.NameCharacters > 0);
        Assert.Equal(0, query.CustomAttributeOrderRows);
        Assert.Equal(0, query.AssociationMethodSemanticsRows);

        MemorySafetyMetadataWork beforeAssociation = index.RecordedWork!;
        _ = index.GetAccessorAssociation(getter);
        Assert.Equal(beforeAssociation, index.RecordedWork);

        using OpenedMetadata legacy = Open(typeof(LegacyFixtures));
        MemorySafetyMetadataIndex legacyIndex =
            MemorySafetyMetadataIndex.CreateReceipted(legacy.Reader);
        MemorySafetyMetadataWork beforePointer = legacyIndex.RecordedWork!;
        MethodDefinitionHandle pointer = FindMethod(
            legacy.Reader,
            typeof(LegacyFixtures).FullName!,
            nameof(LegacyFixtures.FreePointer));
        Assert.IsType<MemorySafetyMemberContractResult.Implicit>(
            legacyIndex.GetMemberContract(pointer));
        Assert.Equal(
            legacy.Reader.GetBlobReader(
                legacy.Reader.GetMethodDefinition(pointer).Signature).Length,
            legacyIndex.RecordedWork!.Since(beforePointer).SignatureBytes);

        using OpenedMetadata unsorted = Open(
            WithSwappedCustomAttributeRows(BuildSyntheticImage([2])));
        MemorySafetyMetadataWork failed =
            MemorySafetyMetadataIndex.CreateReceipted(unsorted.Reader)
                .ConstructionWork!;
        Assert.True(failed.CustomAttributeOrderRows > 0);
        Assert.Equal(0, failed.IntegrityTypeDefRows);
        Assert.Equal(0, failed.AssociationMethodSemanticsRows);
    }

    static (byte[] Image, int AssociationRows, int AttributeRows, int NameChars)
        ReceiptFixture(string name)
    {
        int associationRows =
            MetadataSafetyPolicy.MaxMemorySafetyAssociationRows;
        int attributeRows = MetadataSafetyPolicy.MaxMemorySafetyAttributeRows;
        int nameChars = MetadataSafetyPolicy.MaxMemorySafetyNameWorkChars;
        byte[] image = name switch
        {
            "legacy" => File.ReadAllBytes(
                typeof(LegacyFixtures).Assembly.Location),
            "compiler" => File.ReadAllBytes(
                typeof(ContractFixtures).Assembly.Location),
            "updated" => BuildSyntheticImage([2]),
            "conflicting" => BuildSyntheticImage([2, 1]),
            "malformed-marker" => BuildSyntheticImage([null]),
            "malformed-pointer" => BuildSyntheticImage(
                [],
                malformedPointerSignature: true),
            "direct-accessor-carrier" => BuildSyntheticImage(
                [2],
                directAccessorCarrier: true),
            "malformed-associated-carrier" => BuildSyntheticImage(
                [2],
                malformedAssociatedCarrier: true),
            "cross-type-accessor" => BuildSyntheticImage(
                [2],
                crossTypeAccessorSemantics: true),
            "unobserved-semantics" => BuildSyntheticImage(
                [2],
                duplicatePropertySemantics: true),
            "ordinary-event-adder" => BuildSyntheticImage(
                [2],
                eventAdderIsOrdinaryMethod: true),
            "setter-getter-arity" => BuildSyntheticImage(
                [2],
                propertySetterHasGetterArity: true),
            "shared-getter" => BuildSyntheticImage(
                [2],
                sharedPropertyGetter: true),
            "unsorted-attributes" => WithSwappedCustomAttributeRows(
                BuildSyntheticImage([2])),
            "unordered-nested-class" => BuildSyntheticImage(
                [2],
                nestedRulesCarrierSpoof: true,
                localRulesMemberRefConstructor: true),
            "orphaned-methods" => BuildOrphanedMarkerConstructorImage(),
            "association-budget" => BuildSyntheticImage([2]),
            "attribute-budget" => BuildSyntheticImage([2]),
            "name-budget" => BuildSyntheticImage([2]),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        switch (name)
        {
            case "association-budget":
                associationRows = 2;
                break;
            case "attribute-budget":
                attributeRows = 1;
                break;
            case "name-budget":
                nameChars = 64;
                break;
        }

        return (image, associationRows, attributeRows, nameChars);
    }

    static IEnumerable<EntityHandle> AllMembers(MetadataReader reader)
    {
        yield return default(MethodDefinitionHandle);
        yield return MetadataTokens.MethodDefinitionHandle(
            reader.GetTableRowCount(TableIndex.MethodDef) + 1);
        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
            yield return handle;
        foreach (FieldDefinitionHandle handle in reader.FieldDefinitions)
            yield return handle;
        foreach (PropertyDefinitionHandle handle in reader.PropertyDefinitions)
            yield return handle;
        foreach (EventDefinitionHandle handle in reader.EventDefinitions)
            yield return handle;
    }

    static void AssertSameRules(
        MemorySafetyRulesResult expected,
        MemorySafetyRulesResult actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Observations, actual.Observations);
        switch (expected)
        {
            case MemorySafetyRulesResult.Available available:
                Assert.Equal(
                    available.State,
                    ((MemorySafetyRulesResult.Available)actual).State);
                break;
            case MemorySafetyRulesResult.Unavailable unavailable:
                Assert.Equal(
                    unavailable.Failure,
                    ((MemorySafetyRulesResult.Unavailable)actual).Failure);
                break;
        }
    }
}
