using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using InertText;
using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The API shape producer and settlement seam
/// (docs/design/annotated-type-map-api-family.md#gates): Key selection,
/// Agreement between the settled and inline API states, and the seam's keys.
/// </summary>
public sealed class FindingDiffAnyApiShapeTests
{
    const string Fixture = "FindingDiffAnyFixture.";

    [Fact]
    public void Produce_WithoutKeys_ReturnsTheTypeEntryAlone()
    {
        using PEReader image = Open(after: true);
        FindingDiffAnyApiShape shape = FindingDiffAnyApiShapes.Produce(image, Type(image, "OverloadDefaultChanged"));

        Assert.True(shape.Admitted);
        Assert.Empty(Assert.Single(shape.Surface.Types).Members);
        Assert.Empty(shape.Members);

        shape = FindingDiffAnyApiShapes.Produce(image, Type(image, "OverloadDefaultChanged"), []);
        Assert.Empty(Assert.Single(shape.Surface.Types).Members);
    }

    [Fact]
    public void Produce_SelectsOneOverloadByItsKey()
    {
        FindingDiffAnyTypeMembers members = Members("OverloadDefaultChanged");
        ImmutableArray<FindingDiffAnyMemberState> overloads =
            [.. members.Members.Where(member => Name(member.After) == "Value")];
        Assert.Equal(2, overloads.Length);

        using PEReader image = Open(after: true);
        TypeDefinitionHandle type = Type(image, "OverloadDefaultChanged");
        foreach (FindingDiffAnyMemberState overload in overloads)
        {
            FindingDiffAnyApiShape shape = FindingDiffAnyApiShapes.Produce(image, type, [overload.Key]);

            FindingDiffAnyApiShapeMember member = Assert.Single(shape.Members);
            Assert.Equal(overload.Key, member.Key);
            Assert.Equal(overload.After, member.Declaration);
            ApiMember projected = Assert.Single(Assert.Single(shape.Surface.Types).Members);
            Assert.Same(projected, member.Projection);
            Assert.Equal(MetadataTokens.GetToken(overload.After), projected.MetadataToken);
        }

        FindingDiffAnyApiShape both = FindingDiffAnyApiShapes.Produce(
            image, type, [.. overloads.Select(overload => overload.Key)]);
        Assert.Equal(2, Assert.Single(both.Surface.Types).Members.Count);
    }

    [Fact]
    public void Produce_SelectsAPropertyByItsRaisedKey_AndAnAbsentKeySelectsNothing()
    {
        FindingDiffAnyMemberState property = Assert.Single(
            Members("SetterToProtected").Members, member => member.After.Kind == HandleKind.PropertyDefinition);
        using PEReader image = Open(after: true);
        TypeDefinitionHandle type = Type(image, "SetterToProtected");

        FindingDiffAnyApiShape shape = FindingDiffAnyApiShapes.Produce(image, type, [property.Key]);
        ApiMember projected = Assert.Single(Assert.Single(shape.Surface.Types).Members);
        Assert.Equal("Value", projected.Name);
        Assert.Same(projected, Assert.Single(shape.Members).Projection);

        shape = FindingDiffAnyApiShapes.Produce(image, type, [OpaqueByteRange.Copy("absent"u8)]);
        Assert.Empty(shape.Members);
        Assert.Empty(Assert.Single(shape.Surface.Types).Members);
    }

    [Fact]
    public void Produce_ReportsAMemberOrTypeTheSurfaceDoesNotAdmit()
    {
        FindingDiffAnyMemberState hidden = Assert.Single(
            Members("PrivateMemberChanged").Members, member => Name(member.After) == "Helper");
        using PEReader image = Open(after: true);

        FindingDiffAnyApiShape shape = FindingDiffAnyApiShapes.Produce(
            image, Type(image, "PrivateMemberChanged"), [hidden.Key]);
        Assert.True(shape.Admitted);
        Assert.Null(Assert.Single(shape.Members).Projection);

        shape = FindingDiffAnyApiShapes.Produce(image, Type(image, "InternalBodyOnly"));
        Assert.False(shape.Admitted);
    }

    [Fact]
    public void Produce_SelectsEveryCensusKeyOfEveryFixtureType_ByItsOwnDeclaration()
    {
        using PEReader before = Open(after: false);
        using PEReader after = Open(after: true);
        var pairing = new FindingDiffAnyTypePairing(before, after);
        using var producer = new FindingDiffAnyApiShapeProducer(after);
        MetadataReader reader = after.GetMetadataReader();
        MetadataReader beforeReader = before.GetMetadataReader();
        HashSet<string> paired = [.. beforeReader.TypeDefinitions.Select(handle => beforeReader.GetTypeDefinition(handle))
            .Where(definition => definition.GetDeclaringType().IsNil)
            .Select(definition => beforeReader.GetString(definition.Namespace) + "." + beforeReader.GetString(definition.Name))];
        int checkedKeys = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            if (!definition.GetDeclaringType().IsNil
                || reader.GetString(definition.Namespace) + "." != Fixture
                || !paired.Contains(Fixture + reader.GetString(definition.Name)))
            {
                continue;
            }
            ImmutableArray<FindingDiffAnyMemberState> members =
                pairing.Members(Fixture + reader.GetString(definition.Name)).Members;
            FindingDiffAnyMemberState[] present = [.. members.Where(member => !member.After.IsNil)];
            if (present.Length == 0)
                continue;

            FindingDiffAnyApiShape shape = producer.Produce(handle, [.. present.Select(member => member.Key)]);

            Assert.Equal(
                present.Select(member => (member.Key, member.After)).OrderBy(pair => pair.Key),
                shape.Members.Select(member => (member.Key, member.Declaration)).OrderBy(pair => pair.Key));
            checkedKeys += present.Length;
        }
        Assert.True(checkedKeys > 50, $"Only {checkedKeys} keys were checked.");
    }

    [Fact]
    public void Producer_ReusedAcrossTypes_MatchesOneShotProduction()
    {
        using PEReader image = Open(after: true);
        using var producer = new FindingDiffAnyApiShapeProducer(image);
        foreach (string typeName in (string[])["OverloadDefaultChanged", "SetterToProtected", "TypeAttributeChanged", "OverloadDefaultChanged"])
        {
            TypeDefinitionHandle type = Type(image, typeName);
            OpaqueByteRange[] keys = [.. Members(typeName).Members.Where(member => !member.After.IsNil).Select(member => member.Key)];

            FindingDiffAnyApiShape reused = producer.Produce(type, keys);
            FindingDiffAnyApiShape once = FindingDiffAnyApiShapes.Produce(image, type, keys);

            Assert.Equal(once.Admitted, reused.Admitted);
            Assert.Equal(keys.Length, reused.Members.Length);
            Assert.Equal(
                once.Members.Select(member => (member.Key, member.Declaration, member.Projection?.MetadataToken)),
                reused.Members.Select(member => (member.Key, member.Declaration, member.Projection?.MetadataToken)));
            Assert.Equal(
                Assert.Single(once.Surface.Types).Members.Select(member => member.MetadataToken),
                Assert.Single(reused.Surface.Types).Members.Select(member => member.MetadataToken));
        }

        producer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => producer.Produce(Type(image, "OverloadDefaultChanged")));
    }

    [Theory]
    // An overload's uncertain default selects that overload alone.
    [InlineData("OverloadDefaultChanged", "overload")]
    // An accessor's uncertain status selects its property.
    [InlineData("MemberFoldUncertainGetterStatus", "property")]
    // A Type-owned uncertain difference selects every Member of either side.
    [InlineData("TypeAttributeChanged", "all")]
    // ... and none when neither side declares a Member.
    [InlineData("ITypeAttributeOnly", "none")]
    public void Settler_ReceivesTheKeysOfTheOwningMembers(string typeName, string expected)
    {
        var seen = new Dictionary<string, IReadOnlyList<OpaqueByteRange>?>(StringComparer.Ordinal);
        FindingDiffAnyResult result = Compare((before, beforeType, after, afterType, keys) =>
        {
            seen.Add(Name(after.Image, afterType), keys);
            return Settle(before, beforeType, after, afterType, keys);
        });
        Assert.NotNull(result);

        IReadOnlyList<OpaqueByteRange>? keys = seen[typeName];
        FindingDiffAnyTypeMembers members = Members(typeName);
        switch (expected)
        {
            case "none":
                Assert.Empty(members.Members);
                Assert.Null(keys);
                break;
            case "all":
                IReadOnlyList<OpaqueByteRange> all = Assert.IsAssignableFrom<IReadOnlyList<OpaqueByteRange>>(keys);
                Assert.Equal(all.Count, all.Distinct().Count());
                Assert.Equal(members.Members.Select(member => member.Key).ToHashSet(), all.ToHashSet());
                break;
            case "overload":
                FindingDiffAnyMemberState changed = Assert.Single(
                    members.Members, member => Name(member.After) == "Value" && Parameter(member.After) == "x");
                Assert.Equal([changed.Key], keys);
                break;
            case "property":
                FindingDiffAnyMemberState property = Assert.Single(
                    members.Members, member => member.After.Kind == HandleKind.PropertyDefinition);
                Assert.Equal([property.Key], keys);
                break;
        }
    }

    [Fact]
    public void Settler_AgreesWithTheInlineSettlement_ForEveryFixtureType()
    {
        FindingDiffAnyResult inline = Compare(settler: null);
        int settled = 0;
        FindingDiffAnyResult viaSeam = Compare((before, beforeType, after, afterType, keys) =>
        {
            settled++;
            return Settle(before, beforeType, after, afterType, keys);
        });

        Assert.True(settled > 3);
        Assert.Equal(
            inline.Types.Select(type => (type.FullName, type.Api)),
            viaSeam.Types.Select(type => (type.FullName, type.Api)));
    }

    [Fact]
    public void Settler_DecisionIsTheApiState()
    {
        FindingDiffAnyApiShapeProducer? handed = null;
        TypeDefinitionHandle handedType = default;
        FindingDiffAnyResult result = Compare((before, beforeType, _, _, _) =>
        {
            handed = before;
            handedType = beforeType;
            return FindingExistsDecision.Indeterminate;
        });
        // A handed producer serves its call only.
        Assert.Throws<ObjectDisposedException>(() => handed!.Produce(handedType));

        Assert.Equal(
            FindingDiffAnyState.Indeterminate,
            Assert.Single(result.Types, type => type.FullName == Fixture + "OverloadDefaultChanged").Api);
        Assert.Equal(
            FindingDiffAnyState.Unchanged,
            Assert.Single(result.Types, type => type.FullName == Fixture + "Unchanged").Api);
    }

    // A test-hosted settler over the producer and the complete API comparison,
    // the comparison the API shape family's settler makes.
    static FindingExistsDecision Settle(
        FindingDiffAnyApiShapeProducer before, TypeDefinitionHandle beforeType,
        FindingDiffAnyApiShapeProducer after, TypeDefinitionHandle afterType,
        IReadOnlyList<OpaqueByteRange>? keys)
    {
        FindingDiffAnyApiShape a = before.Produce(beforeType, keys?.ToArray());
        FindingDiffAnyApiShape b = after.Produce(afterType, keys?.ToArray());
        ApiFindingComparison comparison = MetadataFindings.CompareApi(
            a.Surface, b.Surface, new FindingSubject("api", "Settled Type API"),
            new ApiDiffOptions(ApiDiffScope.All));
        if (comparison.Types.Value is not FindingComparison<ApiTypeHandle>.Complete
            || comparison.Members.Value is not FindingComparison<ApiMemberHandle>.Complete
            || comparison.ApiDiff.InspectionFailures.Count != 0)
        {
            return FindingExistsDecision.Indeterminate;
        }
        return comparison.ApiDiff.IsEmpty ? FindingExistsDecision.Unchanged : FindingExistsDecision.Changed;
    }

    static FindingDiffAnyResult Compare(FindingDiffAnyApiSettler? settler)
    {
        using PEReader before = Open(after: false);
        using PEReader after = Open(after: true);
        return FindingDiffAny.Compare(before, after, bodyReader: null, settler, FindingDiffAnyAxes.Api);
    }

    static FindingDiffAnyTypeMembers Members(string typeName)
    {
        using PEReader before = Open(after: false);
        using PEReader after = Open(after: true);
        var pairing = new FindingDiffAnyTypePairing(before, after);
        return pairing.Members(Fixture + typeName);
    }

    static PEReader Open(bool after)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        return new PEReader(File.OpenRead(after ? pair.NewAssemblyPath() : pair.OldAssemblyPath()));
    }

    static TypeDefinitionHandle Type(PEReader image, string name)
    {
        MetadataReader reader = image.GetMetadataReader();
        return reader.TypeDefinitions.Single(handle =>
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            return type.GetDeclaringType().IsNil && reader.GetString(type.Name) == name;
        });
    }

    static string Name(PEReader image, TypeDefinitionHandle handle)
    {
        MetadataReader reader = image.GetMetadataReader();
        return reader.GetString(reader.GetTypeDefinition(handle).Name);
    }

    static string Name(EntityHandle member)
    {
        using PEReader image = Open(after: true);
        MetadataReader reader = image.GetMetadataReader();
        return member.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)member).Name),
            HandleKind.PropertyDefinition => reader.GetString(reader.GetPropertyDefinition((PropertyDefinitionHandle)member).Name),
            HandleKind.FieldDefinition => reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)member).Name),
            HandleKind.EventDefinition => reader.GetString(reader.GetEventDefinition((EventDefinitionHandle)member).Name),
            _ => "",
        };
    }

    static string Parameter(EntityHandle method)
    {
        using PEReader image = Open(after: true);
        MetadataReader reader = image.GetMetadataReader();
        foreach (ParameterHandle handle in reader.GetMethodDefinition((MethodDefinitionHandle)method).GetParameters())
        {
            Parameter parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber == 1)
                return reader.GetString(parameter.Name);
        }
        return "";
    }
}
