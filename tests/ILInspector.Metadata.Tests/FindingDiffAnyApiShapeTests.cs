using System.Collections.Immutable;
using System.Reflection;
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
    public void Produce_SelectedAccessorOwnersIncludeTheirSeparateSemanticMethods()
    {
        (byte[] bytes, TypeDefinitionHandle type, PropertyDefinitionHandle property,
            EventDefinitionHandle @event, _) = ProjectionCompletenessImage();
        using var image = new PEReader(new MemoryStream(bytes));

        FindingDiffAnyApiShape propertyShape = FindingDiffAnyApiShapes.Produce(
            image, type, [Key(image, type, property)]);
        Assert.Equal(
            ((string[])["P", "other_P"]).Order(),
            Assert.Single(propertyShape.Surface.Types).Members
                .Select(member => member.Name)
                .Order());
        Assert.Equal(property, Assert.Single(propertyShape.Members).Declaration);

        FindingDiffAnyApiShape eventShape = FindingDiffAnyApiShapes.Produce(
            image, type, [Key(image, type, @event)]);
        Assert.Equal(
            ((string[])["E", "other_E", "raise_E"]).Order(),
            Assert.Single(eventShape.Surface.Types).Members
                .Select(member => member.Name)
                .Order());
        Assert.Equal(@event, Assert.Single(eventShape.Members).Declaration);
    }

    [Fact]
    public void Produce_TypeEntryIncludesEnumStorageWithoutMemberKeys()
    {
        (byte[] bytes, _, _, _, TypeDefinitionHandle type) =
            ProjectionCompletenessImage();
        using var image = new PEReader(new MemoryStream(bytes));

        FindingDiffAnyApiShape shape =
            FindingDiffAnyApiShapes.Produce(image, type);

        ApiType projection = Assert.Single(shape.Surface.Types);
        Assert.Equal("int", projection.EnumUnderlyingType);
        Assert.Empty(projection.Members);
        Assert.Empty(shape.Members);
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
    // A Type-owned uncertain difference, here the nullable context, selects
    // every Member of either side.
    [InlineData("NullableOuter", "all")]
    // ... and none when neither side declares a Member.
    [InlineData("ITypeUnrenderedAttributeOnly", "none")]
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

    // A changed rendered attribute list is a presented API difference: the
    // Type is Changed without settling, whichever subject owns the list. An
    // accessor's attribute, an unrendered argument, a Member's Obsolete, and a
    // Member the comparison treats as compiler-generated by name are outside
    // those lists and still settle.
    [Theory]
    [InlineData("TypeAttributeChanged", false, FindingDiffAnyState.Changed)]
    [InlineData("ITypeAttributeOnly", false, FindingDiffAnyState.Changed)]
    [InlineData("MemberAttributeChanged", false, FindingDiffAnyState.Changed)]
    [InlineData("PropertyAttributeChanged", false, FindingDiffAnyState.Changed)]
    [InlineData("FieldAttributeChanged", false, FindingDiffAnyState.Changed)]
    [InlineData("ITypeUnrenderedAttributeOnly", true, FindingDiffAnyState.Unchanged)]
    [InlineData("GeneratedNameAttributeChanged", true, FindingDiffAnyState.Unchanged)]
    [InlineData("PublicAttribute", true, FindingDiffAnyState.Changed)]
    [InlineData("SetterBecomesObsolete", true, FindingDiffAnyState.Changed)]
    public void RenderedAttributeChanges_AreDecidedWithoutSettling(
        string typeName, bool settles, FindingDiffAnyState api)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        FindingDiffAnyResult result = Compare((before, beforeType, after, afterType, keys) =>
        {
            seen.Add(Name(after.Image, afterType));
            return Settle(before, beforeType, after, afterType, keys);
        });

        Assert.Equal(settles, seen.Contains(typeName));
        Assert.Equal(api, Assert.Single(result.Types, type => type.FullName == Fixture + typeName).Api);
    }

    // An admitted method's implementation flags and its Intrinsic and
    // state-machine attributes are Body facts, so they leave the API census
    // equal and nothing settles. A property accessor's flags decide how the
    // surface spells its property, so they stay uncertain and settle.
    [Theory]
    [InlineData("InliningChanged", false)]
    [InlineData("IntrinsicGained", false)]
    [InlineData("TypeIntrinsicGained", false)]
    [InlineData("StateMachineRenumbered", false)]
    [InlineData("GetterInliningChanged", true)]
    public void CompilerEmittedImplementationFacts_AreNotSettled(string typeName, bool settles)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Compare((before, beforeType, after, afterType, keys) =>
        {
            seen.Add(Name(after.Image, afterType));
            return Settle(before, beforeType, after, afterType, keys);
        });

        Assert.Equal(settles, seen.Contains(typeName));
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

    static OpaqueByteRange Key(
        PEReader image,
        TypeDefinitionHandle type,
        EntityHandle declaration)
    {
        var side = new FindingDiffAny.Side(
            image,
            new SymbolKeyInterner<FindingDiffAnyPart>());
        try
        {
            return side.Interner.ExpandDetached(
                side.MemberKey(type, declaration),
                side.Transient);
        }
        finally
        {
            side.Complete();
        }
    }

    static (
        byte[] Image,
        TypeDefinitionHandle AccessorType,
        PropertyDefinitionHandle Property,
        EventDefinitionHandle Event,
        TypeDefinitionHandle EnumType)
        ProjectionCompletenessImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ProjectionCompleteness.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ProjectionCompleteness"),
            new Version(1, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        TypeReferenceHandle enumType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));

        var noParameterVoid = new BlobBuilder();
        new BlobEncoder(noParameterVoid).MethodSignature().Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        BlobHandle voidSignature = metadata.GetOrAddBlob(noParameterVoid);
        var noParameterInt = new BlobBuilder();
        new BlobEncoder(noParameterInt).MethodSignature().Parameters(
            0,
            returnType => returnType.Type().Int32(),
            _ => { });
        MethodDefinitionHandle getter = AddMethod(
            "get_P",
            metadata.GetOrAddBlob(noParameterInt),
            MethodAttributes.SpecialName);
        MethodDefinitionHandle propertyOther = AddMethod(
            "other_P",
            voidSignature);
        MethodDefinitionHandle adder = AddMethod(
            "add_E",
            voidSignature,
            MethodAttributes.SpecialName);
        MethodDefinitionHandle remover = AddMethod(
            "remove_E",
            voidSignature,
            MethodAttributes.SpecialName);
        MethodDefinitionHandle raiser = AddMethod("raise_E", voidSignature);
        MethodDefinitionHandle eventOther = AddMethod("other_E", voidSignature);

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);
        TypeDefinitionHandle accessorOwner = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Accessors"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);

        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature)
            .PropertySignature(isInstanceProperty: false)
            .Parameters(
                0,
                returnType => returnType.Type().Int32(),
                _ => { });
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("P"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(accessorOwner, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Other,
            propertyOther);

        EventDefinitionHandle @event = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("E"),
            objectType);
        metadata.AddEventMap(accessorOwner, @event);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Adder,
            adder);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Remover,
            remover);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Raiser,
            raiser);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Other,
            eventOther);

        FieldDefinitionHandle storage = metadata.AddFieldDefinition(
            FieldAttributes.Public
                | FieldAttributes.SpecialName
                | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        TypeDefinitionHandle enumeration = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("E"),
            enumType,
            storage,
            MetadataTokens.MethodDefinitionHandle(7));

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return (image.ToArray(), accessorOwner, property, @event, enumeration);

        MethodDefinitionHandle AddMethod(
            string name,
            BlobHandle signature,
            MethodAttributes extra = MethodAttributes.PrivateScope)
            => metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | extra,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString(name),
                signature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
    }
}
