using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates the bounded API-surface extraction: the bound is a hard retention budget the walk
/// enforces on itself, not a total a caller checks after the fact.
/// </summary>
/// <remarks>
/// The two claims that matter are that a bound is reachable — an image over budget is reported as
/// <see cref="ApiSurfaceExtractionResult.Exceeded"/> and yields no surface at all — and that a
/// retained count is exact for an ordinary surface: a budget equal to the unbounded walk's own
/// total extracts the whole surface, and one less stops it. Hostile-shape tests separately gate
/// the conservative pre-materialization work bound used to stop allocation amplification.
/// </remarks>
public sealed class ApiSurfaceExtractorBoundsTests
{
    static readonly string SelfPath = typeof(ApiSurfaceExtractorBoundsTests).Assembly.Location;

    [Fact]
    public void GenerousBounds_ExtractTheSameSurfaceAsTheUnboundedWalk()
    {
        ApiSurface unbounded = Unbounded();
        ApiSurface bounded = Extracted(
            new ApiSurfaceExtractionBounds(
                int.MaxValue,
                int.MaxValue,
                int.MaxValue,
                int.MaxValue,
                int.MaxValue));

        Assert.Equal(
            unbounded.Types.Select(type => (type.FullName, type.Members.Count)),
            bounded.Types.Select(type => (type.FullName, type.Members.Count)));
        Assert.Equal(unbounded.TypeForwarders.Count, bounded.TypeForwarders.Count);
        Assert.Equal(
            unbounded.InspectionFailures.Count,
            bounded.InspectionFailures.Count);
    }

    [Fact]
    public void BoundsEqualToTheSurfaceSize_ExtractItWhole()
    {
        ApiSurface unbounded = Unbounded();
        int types = unbounded.Types.Count;
        int members = unbounded.Types.Sum(type => type.Members.Count);
        int inspectionFailures = unbounded.InspectionFailures.Count;
        int typeForwarders = unbounded.TypeForwarders.Count;
        Assert.True(types > 0);
        Assert.True(members > 0);

        ApiSurface exact = Extracted(
            new ApiSurfaceExtractionBounds(
                types,
                members,
                inspectionFailures,
                typeForwarders,
                int.MaxValue));

        Assert.Equal(types, exact.Types.Count);
        Assert.Equal(members, exact.Types.Sum(type => type.Members.Count));
    }

    [Fact]
    public void OneTypeShortOfTheSurfaceSize_IsAbandonedAtTheTypeBound()
    {
        ApiSurface unbounded = Unbounded();
        int members = unbounded.Types.Sum(type => type.Members.Count);

        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    unbounded.Types.Count - 1,
                    members,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.Types, exceeded.Bound);
    }

    [Fact]
    public void OneMemberShortOfTheSurfaceSize_IsAbandonedAtTheMemberBound()
    {
        ApiSurface unbounded = Unbounded();
        int members = unbounded.Types.Sum(type => type.Members.Count);

        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    unbounded.Types.Count,
                    members - 1,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.Members, exceeded.Bound);
    }

    // An exhausted budget is a legal input: a caller spending one budget across several images
    // hands the next image nothing, and must get a refusal rather than an argument failure.
    [Fact]
    public void AnExhaustedTypeBudget_RefusesBeforeWalkingMembers()
    {
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    0,
                    0,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.Types, exceeded.Bound);
    }

    [Fact]
    public void NegativeBounds_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(-1, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, -1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, 0, 0, -1));
    }

    [Fact]
    public void TypesOnlyExtraction_SpendsNoMemberBudget()
    {
        ApiSurfaceExtractionResult result = Extract(
            new ApiSurfaceExtractionBounds(
                int.MaxValue,
                0,
                int.MaxValue,
                int.MaxValue,
                int.MaxValue),
            typesOnly: true);

        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
    }

    [Fact]
    public void OneTypeForwarderShortOfTheSurfaceSize_IsAbandoned()
    {
        ApiSurface unbounded = Unbounded();
        Assert.True(unbounded.TypeForwarders.Count > 0);

        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    unbounded.TypeForwarders.Count - 1,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.TypeForwarders, exceeded.Bound);
    }

    [Fact]
    public void ExhaustedForwarderBudgetStopsBeforeDecodingItsName()
    {
        byte[] image = BuildForwarderImage(
            new string('F', 4_000_000),
            "Target");
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                10,
                10,
                10,
                0,
                100,
                1_024));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(result);
        Assert.Equal(ApiSurfaceExtractionBound.TypeForwarders, exceeded.Bound);
        Assert.True(
            allocated < 4L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ForwarderTargetAssemblyIsChargedBeforeDecoding()
    {
        byte[] image = BuildForwarderImage(
            "Forwarded",
            new string('A', 4_000_000));
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                10,
                10,
                10,
                1,
                100,
                1_024));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(result);
        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
        Assert.True(
            allocated < 4L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void MetadataRowBudget_IsExactAndStopsBeforeExtraction()
    {
        var generous = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        Assert.True(generous.MetadataRows > 0);

        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.MetadataRows)));
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.MetadataRows - 1)));

        Assert.Equal(ApiSurfaceExtractionBound.MetadataRows, exceeded.Bound);
    }

    [Fact]
    public void RetainedTextBudget_IsExact()
    {
        var generous = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        Assert.True(generous.RetainedTextCharacters > 0);

        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.RetainedTextCharacters)));
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.RetainedTextCharacters - 1)));

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
    }

    [Fact]
    public void ProjectedDeclaringTypeIdentityContributesItsOwnRetainedText()
    {
        const string receiver = "System.Collections.Generic.IEnumerable<T>";
        const string declaringType = "Samples.Extensions";
        MetadataTypeDefinitionName declaringTypeDefinition = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Samples",
                    ["Extensions"]))
            .Name;
        var withoutReceiver = new ApiMember
        {
            Name = "M",
            Kind = "extension-method",
            SignatureModel = new ApiSignature(),
        };
        var withReceiver = new ApiMember
        {
            Name = "M",
            Kind = "extension-method",
            SignatureModel = new ApiSignature
            {
                ExtensionReceiverType = receiver,
            },
            DeclaringTypeCanonicalName = declaringType,
            DeclaringTypeDefinitionName = declaringTypeDefinition,
        };

        Assert.Equal(
            receiver.Length
                + declaringType.Length
                + declaringTypeDefinition.Namespace.Length
                + declaringTypeDefinition.Segments.Sum(
                    static segment => segment.Length),
            ApiSurfaceExtractor.CountRetainedText(withReceiver)
                - ApiSurfaceExtractor.CountRetainedText(withoutReceiver));
    }

    [Fact]
    public void JsonPropertyNameFactsContributeTheirRetainedText()
    {
        const string propertyName = "wire_name";
        var withoutNames = new ApiMember();
        var withNames = new ApiMember
        {
            JsonPropertyName = propertyName,
        };
        var withoutFilteredName = new ApiType();
        var withFilteredName = new ApiType
        {
            FilteredJsonPropertyNameFacts =
            [
                new(
                    FilteredJsonPropertyNameKind.AutoPropertyBackingField,
                    "Value",
                    0x04000001,
                    ["backing_wire_name"]),
            ],
        };

        Assert.Equal(
            propertyName.Length,
            ApiSurfaceExtractor.CountRetainedText(withNames)
                - ApiSurfaceExtractor.CountRetainedText(withoutNames));
        Assert.Equal(
            "Value".Length + "backing_wire_name".Length,
            ApiSurfaceExtractor.CountRetainedText(withFilteredName)
                - ApiSurfaceExtractor.CountRetainedText(withoutFilteredName));
    }

    [Fact]
    public void JsonSerializablePropertyNameContributesItsRetainedText()
    {
        const string propertyName = "RegisteredCustomPayload";
        var withoutName = new ApiType
        {
                JsonSerializableRoots =
                [
                    new(ElementType: null, IsArray: false),
                ],
        };
        var withName = new ApiType
        {
                JsonSerializableRoots =
                [
                    new(
                        ElementType: null,
                        IsArray: false,
                        TypeInfoPropertyName: propertyName),
                ],
        };

        Assert.Equal(
                propertyName.Length,
                ApiSurfaceExtractor.CountRetainedText(withName)
                    - ApiSurfaceExtractor.CountRetainedText(withoutName));
    }

    [Fact]
    public void GetterAccessibilityContributesItsRetainedText()
    {
        const string accessibility = "private";
        var withoutAccessibility = new ApiMember();
        var withAccessibility = new ApiMember
        {
            GetterAccessibility = accessibility,
        };

        Assert.Equal(
            accessibility.Length,
            ApiSurfaceExtractor.CountRetainedText(withAccessibility)
                - ApiSurfaceExtractor.CountRetainedText(withoutAccessibility));
    }

    [Fact]
    public void SetterAccessibilityContributesItsRetainedText()
    {
        const string accessibility = "private";
        var withoutAccessibility = new ApiMember();
        var withAccessibility = new ApiMember
        {
            SetterAccessibility = accessibility,
        };

        Assert.Equal(
            accessibility.Length,
            ApiSurfaceExtractor.CountRetainedText(withAccessibility)
                - ApiSurfaceExtractor.CountRetainedText(withoutAccessibility));
    }

    [Fact]
    public void BaseTypeReferenceContributesItsCompleteRetainedText()
    {
        const string assemblyName = "Dependency";
        const string culture = "en-US";
        const string token = "0011223344556677";
        const string fullName = "Dependency.ReallyLongBaseType";
        const string typeNamespace = "Dependency";
        const string typeName = "ReallyLongBaseType";
        MetadataTypeDefinitionName definitionName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                typeNamespace,
                [typeName])).Name;
        var withoutReference = new ApiType();
        var withReference = new ApiType
        {
            BaseTypeReference = new(
                new ApiAssemblyIdentity(
                    assemblyName,
                    new Version(1, 2, 3, 4),
                    culture,
                    token),
                fullName,
                definitionName),
        };

        Assert.Equal(
            assemblyName.Length
                + culture.Length
                + token.Length
                + fullName.Length
                + typeNamespace.Length
                + typeName.Length,
            ApiSurfaceExtractor.CountRetainedText(withReference)
                - ApiSurfaceExtractor.CountRetainedText(withoutReference));
    }

    [Fact]
    public void ParameterTypeReferenceContributesItsCompleteRetainedText()
    {
        const string assemblyName = "Dependency";
        const string token = "0011223344556677";
        const string fullName = "Dependency.ParameterType";
        var withoutReference = new ApiMember
        {
            SignatureModel = new ApiSignature
            {
                Parameters = [new ApiParameter()],
            },
        };
        var withReference = new ApiMember
        {
            SignatureModel = new ApiSignature
            {
                Parameters =
                [
                    new ApiParameter
                    {
                        TypeReferences =
                        [
                            new(
                                new ApiAssemblyIdentity(
                                    assemblyName,
                                    new Version(1, 2, 3, 4),
                                    culture: null,
                                    publicKeyToken: token),
                                fullName),
                        ],
                    },
                ],
            },
        };

        Assert.Equal(
            assemblyName.Length + token.Length + fullName.Length,
            ApiSurfaceExtractor.CountRetainedText(withReference)
                - ApiSurfaceExtractor.CountRetainedText(withoutReference));
    }

    [Fact]
    public void RepeatedLongMemberName_StopsBeforeLargeAllocationAmplification()
    {
        byte[] image = BuildRepeatedLongMethodNameImage(
            methodCount: 10_000,
            nameLength: 4_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 10,
                maxMembers: 10_000,
                maxInspectionFailures: 10,
                maxTypeForwarders: 10,
                maxMetadataRows: 50_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(result);
        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void RepeatedLongSkippedAccessorName_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedLongMethodNameImage(
                methodCount: 10_000,
                nameLength: 4_000,
                prefix: "get_"));
    }

    [Fact]
    public void RepeatedLongSkippedFieldName_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedLongFieldNameImage(
                fieldCount: 10_000,
                nameLength: 4_000));
    }

    [Fact]
    public void RepeatedLongVisibilityAttributeTypeName_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedLongVisibilityAttributeTypeNameImage(
                methodCount: 10_000,
                nameLength: 4_000));
    }

    [Fact]
    public void OneWideSignature_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildWideSignatureImage(parameterCount: 10_000, nameLength: 4_000));
    }

    [Fact]
    public void OneInterfaceHeavyType_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildInterfaceFloodImage(interfaceCount: 10_000, nameLength: 4_000));
    }

    [Fact]
    public void OneWideFieldSignature_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildWideTypeSpecImage(WideTypeSpecUse.Field, argumentCount: 1_000, nameLength: 10_000));
    }

    [Theory]
    [InlineData(WideTypeSpecUse.BaseType)]
    [InlineData(WideTypeSpecUse.Event)]
    [InlineData(WideTypeSpecUse.Interface)]
    [InlineData(WideTypeSpecUse.GenericConstraint)]
    public void OneWideTypeSpec_StopsBeforeLargeAllocationAmplification(
        WideTypeSpecUse use)
    {
        AssertTextAmplificationIsBounded(
            BuildWideTypeSpecImage(use, argumentCount: 1_000, nameLength: 10_000));
    }

    [Fact]
    public void OneLargeCustomAttribute_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildLargeAttributeImage(valueLength: 4_000_000));
    }

    [Fact]
    public void RepeatedEnumAttributeLookups_DoNotAllocateQuadratically()
    {
        byte[] image = BuildRepeatedEnumAttributeLookupImage(
            typeCount: 2_000,
            namedArgumentCount: 2_000,
            attributeCount: 1);
        AssertEnumAttributeLookupsDoNotAllocateQuadratically(image);
    }

    [Fact]
    public void SeparateEnumAttributes_ReuseTheChargedTypeNameIndex()
    {
        byte[] image = BuildRepeatedEnumAttributeLookupImage(
            typeCount: 2_000,
            namedArgumentCount: 1,
            attributeCount: 2_000,
            poisonTypeDefinitionIndex: false);
        AssertEnumAttributeLookupsDoNotAllocateQuadratically(image);
    }

    [Fact]
    public void FailedEnumAttributeIndexBuild_IsCachedAndVisible()
        => AssertFailedEnumAttributeIndexIsCachedAndVisible(bounded: true);

    [Fact]
    public void FailedEnumAttributeIndexBuild_IsCachedOnTheUnboundedPath()
        => AssertFailedEnumAttributeIndexIsCachedAndVisible(bounded: false);

    [Fact]
    public void ParameterEnumAttributes_ReuseTheChargedTypeNameIndex()
        => AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(bounded: true);

    [Fact]
    public void ParameterEnumAttributes_ReuseTheChargedTypeNameIndexOnTheUnboundedPath()
        => AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(bounded: false);

    [Fact]
    public void DecimalConstantParameterAttributes_ReuseTheChargedTypeNameIndex()
        => AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(
            bounded: true,
            attributeNamespace: "System.Runtime.CompilerServices",
            attributeName: "DecimalConstantAttribute");

    [Fact]
    public void DecimalConstantParameterAttributes_ReuseTheChargedTypeNameIndexOnTheUnboundedPath()
        => AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(
            bounded: false,
            attributeNamespace: "System.Runtime.CompilerServices",
            attributeName: "DecimalConstantAttribute");

    static void AssertFailedEnumAttributeIndexIsCachedAndVisible(bool bounded)
    {
        byte[] image = BuildRepeatedEnumAttributeLookupImage(
            typeCount: 2_000,
            namedArgumentCount: 1,
            attributeCount: 2_000,
            poisonTypeDefinitionIndex: true);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurface surface;
        if (bounded)
        {
            var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
                ApiSurfaceExtractor.ExtractBounded(
                    peReader,
                    ApiSurfaceExtractionScope.Public,
                    new ApiSurfaceExtractionBounds(
                        maxTypes: 100_000,
                        maxMembers: 1_000_000,
                        maxInspectionFailures: 1_024,
                        maxTypeForwarders: 100_000,
                        maxMetadataRows: 250_000,
                        maxRetainedTextCharacters: 8_000_000)));
            surface = extracted.Surface;
        }
        else
        {
            surface = ApiSurfaceExtractor.Extract(peReader);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        ApiSurfaceInspectionFailure failure = Assert.Single(surface.InspectionFailures);
        Assert.Equal("enum attribute type index", failure.Operation);
        Assert.Equal(MetadataTypeNameFailureMechanism.Metadata, failure.Mechanism);
        ApiType attributed = Assert.Single(surface.Types, type => type.Name == "Attributed");
        Assert.Empty(attributed.Attributes);
        var inspection = MetadataFindings.InspectApiAttributes(
            surface,
            new FindingSubject("Attributed", "Attributed"),
            attributed.FullName);
        var failed = Assert.IsType<FindingInspection<ApiAttributeHandle>.Failed>(
            inspection.Value);
        Assert.Contains("enum attribute type index", failed.Error.Reason);
        var missingType = Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(
            MetadataFindings.InspectApiType(
                surface,
                new FindingSubject("Missing", "Missing"),
                "Missing").Value);
        Assert.Empty(missingType.Findings);
        var missingMembers =
            Assert.IsType<FindingInspection<ApiMemberHandle>.Absent>(
                MetadataFindings.InspectApiMembers(
                    surface,
                    new FindingSubject("Missing", "Missing"),
                    "Missing").Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.SubjectAbsent,
            missingMembers.Kind);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"{(bounded ? "bounded" : "unbounded")} extraction allocated {allocated:N0} bytes");
    }

    static void AssertParameterEnumAttributesReuseTheChargedTypeNameIndex(
        bool bounded,
        string attributeNamespace = "Samples",
        string attributeName = "SampleAttribute")
    {
        byte[] image = BuildRepeatedParameterEnumAttributeLookupImage(
            typeCount: 2_000,
            methodCount: 2_000,
            attributeNamespace,
            attributeName);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurface surface;
        if (bounded)
        {
            var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
                ApiSurfaceExtractor.ExtractBounded(
                    peReader,
                    ApiSurfaceExtractionScope.Public,
                    new ApiSurfaceExtractionBounds(
                        maxTypes: 100_000,
                        maxMembers: 1_000_000,
                        maxInspectionFailures: 1_024,
                        maxTypeForwarders: 100_000,
                        maxMetadataRows: 250_000,
                        maxRetainedTextCharacters: 8_000_000)));
            surface = extracted.Surface;
        }
        else
        {
            surface = ApiSurfaceExtractor.Extract(peReader);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        ApiType host = Assert.Single(surface.Types, type => type.Name == "Host");
        Assert.Equal(2_000, host.Members.Count);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"{(bounded ? "bounded" : "unbounded")} parameter-attribute extraction allocated {allocated:N0} bytes");
    }

    static void AssertEnumAttributeLookupsDoNotAllocateQuadratically(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void GenericAttributeTypeSpec_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildWideGenericAttributeImage(
                argumentCount: 1_000,
                nameLength: 150_000));
    }

    [Fact]
    public void PropertyAccessorReturnAttribute_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildLargeAccessorReturnAttributeImage(
                AccessorOwner.Property,
                valueLength: 4_000_000));
    }

    [Fact]
    public void EventAccessorReturnAttribute_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildLargeAccessorReturnAttributeImage(
                AccessorOwner.Event,
                valueLength: 4_000_000));
    }

    [Fact]
    public void OneDeeplyNestedTypeSpec_StopsBeforeLargeAllocationAmplification()
    {
        // Stacked GENERICINST prefixes are not ECMA-335 II.23.2.12 (the first
        // slot must be CLASS|VALUETYPE). SignatureBlobGuard rejects the field
        // before SRM can expand the long argument name, so the retained-text
        // bound is not the tripwire. The allocation bound still is.
        AssertRejectedSignatureDoesNotAmplify(
            BuildNestedTypeSpecFieldImage(depth: 500, nameLength: 3_900));
    }

    [Fact]
    public void OneArgumentNestedTypeSpec_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildArgumentNestedTypeSpecFieldImage(depth: 511, nameLength: 1_700));
    }

    [Fact]
    public void OneNestedArrayType_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildNestedArrayFieldImage(depth: 500, rank: 3_800));
    }

    [Fact]
    public void EnclosingTypeNameChain_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildNestedTypeNameChainImage(
                depth: 256,
                nameLength: 4_000));
    }

    [Fact]
    public void EnclosingTypeReferenceChain_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildNestedTypeReferenceFieldImage(
                depth: 256,
                nameLength: 100_000));
    }

    [Fact]
    public void RejectedTypes_SpendDecodeWorkAcrossTheExtraction()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedNestedGenericTypesImage(
                typeCount: 64,
                depth: 20,
                nameLength: 7_000,
                poison: true));
    }

    [Theory]
    [InlineData(TransformArrayKind.TupleElementNames)]
    [InlineData(TransformArrayKind.Nullable)]
    [InlineData(TransformArrayKind.Dynamic)]
    public void LargeTransformArray_StopsBeforeLargeAllocationAmplification(
        TransformArrayKind kind)
    {
        AssertTextAmplificationIsBounded(
            BuildLargeTransformArrayImage(kind, elementCount: 5_000_000));
    }

    [Fact]
    public void RepeatedMethodGenericContext_ReusesTypeParameterNames()
    {
        byte[] image = BuildRepeatedMethodGenericContextImage(
            genericParameterCount: 1_000,
            nameLength: 1_000,
            methodCount: 300);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result =
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    maxTypes: 100_000,
                    maxMembers: 1_000_000,
                    maxInspectionFailures: 1_024,
                    maxTypeForwarders: 100_000,
                    maxMetadataRows: 250_000,
                    maxRetainedTextCharacters: 32_000_000));
        Assert.True(
            result is ApiSurfaceExtractionResult.Extracted,
            $"Extraction rejected the reusable generic context: {result}");
        var extracted =
            (ApiSurfaceExtractionResult.Extracted)result;

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Single(extracted.Surface.Types);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void OneHugeArrayRank_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildHugeArrayRankFieldImage(rank: 40_000_000));
    }

    [Fact]
    public void RepeatedHiddenAttributeProbe_DoesNotCopyTheValueBlob()
    {
        byte[] image = BuildRepeatedHiddenAttributeImage(
            typeCount: 1_000,
            blobLength: 1_000_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                ApiSurfaceExtractionScope.PublicWithNonPublicTypes,
                new ApiSurfaceExtractionBounds(
                    maxTypes: 100_000,
                    maxMembers: 1_000_000,
                    maxInspectionFailures: 1_024,
                    maxTypeForwarders: 100_000,
                    maxMetadataRows: 250_000,
                    maxRetainedTextCharacters: 32_000_000)));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(extracted.Surface.Types);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void HiddenAutoPropertySignature_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildHiddenAutoPropertyImage(
                argumentCount: 10_000,
                nameLength: 4_000));
    }

    [Fact]
    public void HugeParameterDefault_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildHugeParameterDefaultImage(characterCount: 10_000_000));
    }

    [Fact]
    public void EnumDefaultScan_ChargesSkippedEnclosingTypeNames()
    {
        AssertTextAmplificationIsBounded(
            BuildNestedEnumDefaultImage(
                depth: 256,
                nameLength: 4_000));
    }

    [Fact]
    public void EnumDefaultScan_ChargesRejectedBaseTypeNames()
    {
        AssertTextAmplificationIsBounded(
            BuildEnumDefaultDecoyImage(
                decoyTypeCount: 512,
                defaultMethodCount: 32,
                baseNameLength: 8_000));
    }

    [Fact]
    public void EnumDefaultScan_ChargesTypeSpecArrayRank()
    {
        AssertTextAmplificationIsBounded(
            BuildEnumDefaultTypeSpecDecoyImage(
                decoyTypeCount: 64,
                defaultMethodCount: 16,
                rank: 40_000_000));
    }

    [Fact]
    public void AttributeTypeSpec_ChargesArrayRankBeforeRendering()
    {
        AssertTextAmplificationIsBounded(
            BuildTypeSpecArrayAttributeImage(
                rank: 40_000_000,
                typeCount: 1));
    }

    [Fact]
    public void OneHugeCustomAttributeArrayCount_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildCustomAttributeArrayCountImage(
                attributeCount: 1,
                elementCount: 100_000_000));
    }

    [Fact]
    public void RepeatedNamedArgumentCount_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildCustomAttributeNamedArgumentCountImage(
                attributeCount: 64,
                namedArgumentCount: 65_535));
    }

    [Fact]
    public void PropertyAccessorNullableContextTypeSpec_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildAccessorTypeSpecArrayAttributeImage(rank: 40_000_000));
    }

    [Fact]
    public void DeepBoxedCustomAttribute_StopsBeforeStackOverflow()
    {
        byte[] image = BuildDeepBoxedAttributeImage(depth: 80_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 32_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void OneHugeNamedArgumentArrayCount_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildNamedArgumentArrayCountImage(elementCount: 100_000_000));
    }

    [Fact]
    public void DeepNamedNestedArrayCustomAttribute_StopsBeforeStackOverflow()
    {
        byte[] image = BuildNamedNestedArrayAttributeImage(depth: 80_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 32_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void TypeRefEnumWidthDesync_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildTypeRefEnumDesyncImage(elementCount: 100_000_000));
    }

    [Fact]
    public void OverDeepEnumFieldModifiers_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildEnumCmodDesyncImage(
                modifierCount: SignatureBlobGuard.DefaultMaxDepth + 1,
                elementCount: 100_000_000));
    }

    [Fact]
    public void AssemblyQualifiedNamedEnum_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildAssemblyQualifiedNamedEnumImage(elementCount: 100_000_000));
    }

    [Fact]
    public void ClassSystemStringFixedArgument_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildClassSystemStringImage(elementCount: 100_000_000));
    }

    [Fact]
    public void DottedSystemTypeTypeRef_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildDottedSystemTypeImage(elementCount: 100_000_000));
    }

    [Fact]
    public void StringTypedEnumValue_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildStringTypedEnumImage(elementCount: 100_000_000));
    }

    [Fact]
    public void BoxedEnumArrayEmptyName_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildBoxedEnumArrayEmptyNameImage(elementCount: 100_000_000));
    }

    [Fact]
    public void FnPtrEarlierGenericArgumentThenArray_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildGenericEarlierThenArrayImage(pointerToFnPtr: false, elementCount: 100_000_000));
    }

    [Fact]
    public void PtrFnPtrEarlierGenericArgumentThenArray_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildGenericEarlierThenArrayImage(pointerToFnPtr: true, elementCount: 100_000_000));
    }

    [Fact]
    public void ClassTypeDefRow4EarlierArgument_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildClassTypeDefRow4DesyncImage(elementCount: 100_000_000));
    }

    [Fact]
    public void ValueTypeTypeRefRow4EarlierArgument_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildValueTypeTypeRefRow4DesyncImage(elementCount: 100_000_000));
    }

    [Fact]
    public void SelfReferentialGenericVar_StopsBeforeStackOverflow()
    {
        byte[] image = BuildSelfReferentialGenericVarImage();
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 32_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void LegalNestedLongEnumNamedArgument_HasBoundedUnboundedParity()
    {
        AssertCompilerAttributeParity(
            nameof(LegalNestedEnumFixture),
            "LegalNestedEnum",
            "Choice");
    }

    [Fact]
    public void LegalGenericCtorAttribute_HasBoundedUnboundedParity()
    {
        AssertCompilerAttributeParity(
            nameof(LegalGenericCtorFixture),
            "LegalGenericCtor",
            "5");
    }

    [Fact]
    public void PropertyRefReturnDuplicateSeq0Attributes_StopsBeforeLargeAllocationAmplification()
    {
        AssertTextAmplificationIsBounded(
            BuildRefPropertyDuplicateSeq0TypeSpecImage(
                returnParameterCount: 8,
                rank: 1_000_000));
    }

    [Fact]
    public void LocalExtensionAttachment_DoesNotAllocateQuadratically()
    {
        byte[] image = BuildLocalExtensionFloodImage(methodCount: 4_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    maxTypes: 100_000,
                    maxMembers: 1_000_000,
                    maxInspectionFailures: 1_024,
                    maxTypeForwarders: 100_000,
                    maxMetadataRows: 250_000,
                    maxRetainedTextCharacters: 32_000_000)));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(
            8_000,
            extracted.Surface.Types.Sum(type => type.Members.Count));
        var attached = Assert.Single(
            extracted.Surface.Types,
            type => type.FullName == "Samples.Target").Members;
        Assert.Equal(
            Enumerable.Range(1, 4_000),
            attached.Select(member => member.DeclaringOverloadIndex!.Value));
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void ExtensionScan_DoesNotHashNonCoreLibraryPublicKeyPerMethod()
    {
        byte[] image = BuildLocalExtensionFloodImage(
            methodCount: 64,
            assemblyPublicKeyLength: 1024 * 1024);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurface surface = ApiSurfaceExtractor.ExtractSummary(peReader);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(
            128,
            surface.Types.Sum(type => type.Members.Count));
        Assert.True(
            allocated < 24L * 1024 * 1024,
            $"extension scan allocated {allocated:N0} bytes");
    }

    [Fact]
    public void FinalizerScan_ChargesCoreLibraryPublicKeyBeforeCopying()
    {
        AssertTextAmplificationIsBounded(
            BuildRepeatedFinalizerImage(
                typeCount: 64,
                publicKeyLength: 2_100_000));
    }

    [Fact]
    public void LargeVisibilityAttribute_StopsBeforeDecodingItsMessage()
    {
        byte[] image = BuildLargeObsoleteAttributeImage(messageLength: 40_000_000);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                peReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    maxTypes: 100_000,
                    maxMembers: 1_000_000,
                    maxInspectionFailures: 1_024,
                    maxTypeForwarders: 100_000,
                    maxMetadataRows: 250_000,
                    maxRetainedTextCharacters: 8_000_000)));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(extracted.Surface.Types);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    [Fact]
    public void GenericAttributeConstructorHasBoundedUnboundedParity()
    {
        using var unboundedStream = File.OpenRead(SelfPath);
        using var unboundedReader = new PEReader(unboundedStream);
        ApiSurface unbounded = ApiSurfaceExtractor.Extract(
            unboundedReader,
            ApiSurfaceExtractionScope.Public);
        Assert.Contains(
            unbounded.Types,
            type => type.FullName.EndsWith(
                nameof(GenericAttributeBoundedFixture),
                StringComparison.Ordinal));
        using var boundedStream = File.OpenRead(SelfPath);
        using var boundedReader = new PEReader(boundedStream);
        var bounded = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                boundedReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(
            unbounded.Types.Select(type => type.FullName),
            bounded.Surface.Types.Select(type => type.FullName));
        Assert.Equal(
            unbounded.InspectionFailures.Count,
            bounded.Surface.InspectionFailures.Count);
    }

    [Fact]
    public void LegalNamedAttribute_HasBoundedUnboundedParity()
    {
        using var unboundedStream = File.OpenRead(SelfPath);
        using var unboundedReader = new PEReader(unboundedStream);
        ApiSurface unbounded = ApiSurfaceExtractor.Extract(
            unboundedReader,
            ApiSurfaceExtractionScope.Public);
        ApiType unboundedType = Assert.Single(
            unbounded.Types,
            type => type.FullName.EndsWith(
                nameof(LegalNamedAttributeFixture),
                StringComparison.Ordinal));
        using var boundedStream = File.OpenRead(SelfPath);
        using var boundedReader = new PEReader(boundedStream);
        var bounded = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                boundedReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        ApiType boundedType = Assert.Single(
            bounded.Surface.Types,
            type => type.FullName.EndsWith(
                nameof(LegalNamedAttributeFixture),
                StringComparison.Ordinal));

        Assert.Equal(unboundedType.Attributes, boundedType.Attributes);
        Assert.Contains(
            unboundedType.Attributes,
            attribute => attribute.Contains("LegalNamed", StringComparison.Ordinal)
                && attribute.Contains("Count", StringComparison.Ordinal));
    }

    static ApiSurface Unbounded()
    {
        using var stream = File.OpenRead(SelfPath);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, ApiSurfaceExtractionScope.Public);
    }

    static ApiSurface Extracted(ApiSurfaceExtractionBounds bounds)
        => Assert.IsType<ApiSurfaceExtractionResult.Extracted>(Extract(bounds)).Surface;

    static ApiSurfaceExtractionResult Extract(
        ApiSurfaceExtractionBounds bounds,
        bool typesOnly = false)
    {
        using var stream = File.OpenRead(SelfPath);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            bounds,
            typesOnly);
    }

    static void AssertRejectedSignatureDoesNotAmplify(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(result);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
        ApiMember field = Assert.Single(
            extracted.Surface.Types.SelectMany(type => type.Members),
            member => member.Name == "Value");
        Assert.Equal(SignatureDecodeStatus.Degraded, field.SignatureDecodeStatus);
        Assert.True(
            (field.ReturnType?.Length ?? 0) < 64,
            $"rejected field still retained {field.ReturnType?.Length:N0} characters");
    }

    static void AssertTextAmplificationIsBounded(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        long before = GC.GetAllocatedBytesForCurrentThread();

        ApiSurfaceExtractionResult result = ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            new ApiSurfaceExtractionBounds(
                maxTypes: 100_000,
                maxMembers: 1_000_000,
                maxInspectionFailures: 1_024,
                maxTypeForwarders: 100_000,
                maxMetadataRows: 250_000,
                maxRetainedTextCharacters: 8_000_000));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(result);
        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
        Assert.True(
            allocated < 64L * 1024 * 1024,
            $"bounded extraction allocated {allocated:N0} bytes");
    }

    static byte[] BuildRepeatedLongMethodNameImage(
        int methodCount,
        int nameLength,
        string prefix = "")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Amplification.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Amplification"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Amplifier"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        StringHandle name =
            metadata.GetOrAddString(
                prefix + new string('M', nameLength - prefix.Length));
        for (int index = 0; index < methodCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                name,
                signatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildRepeatedLongFieldNameImage(
        int fieldCount,
        int nameLength)
    {
        var metadata = Metadata("FieldAmplification");
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int32();
        BlobHandle signatureHandle = metadata.GetOrAddBlob(fieldSignature);
        StringHandle name = metadata.GetOrAddString(
            "<" + new string('F', nameLength - 1));
        for (int index = 0; index < fieldCount; index++)
        {
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                name,
                signatureHandle);
        }
        AddModuleAndPublicType(metadata, "Amplifier");
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedLongVisibilityAttributeTypeNameImage(
        int methodCount,
        int nameLength)
    {
        var metadata = Metadata("AttributeNameAmplification");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle longAttributeType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(
                new string('A', nameLength - "Attribute".Length)
                    + "Attribute"));
        TypeReferenceHandle editorBrowsableType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.ComponentModel"),
            metadata.GetOrAddString("EditorBrowsableAttribute"));
        var emptyConstructorSignature = new BlobBuilder();
        new BlobEncoder(emptyConstructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle longConstructor = metadata.AddMemberReference(
            longAttributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(emptyConstructorSignature));
        var editorBrowsableConstructorSignature = new BlobBuilder();
        new BlobEncoder(editorBrowsableConstructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());
        MemberReferenceHandle editorBrowsableConstructor =
            metadata.AddMemberReference(
                editorBrowsableType,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(editorBrowsableConstructorSignature));
        AddModuleAndPublicType(metadata, "Amplifier");
        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature).MethodSignature().Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        BlobHandle methodSignatureHandle = metadata.GetOrAddBlob(methodSignature);
        var emptyValue = new BlobBuilder();
        emptyValue.WriteUInt16(1);
        emptyValue.WriteUInt16(0);
        BlobHandle emptyValueHandle = metadata.GetOrAddBlob(emptyValue);
        var hiddenValue = new BlobBuilder();
        hiddenValue.WriteUInt16(1);
        hiddenValue.WriteInt32(1);
        hiddenValue.WriteUInt16(0);
        BlobHandle hiddenValueHandle = metadata.GetOrAddBlob(hiddenValue);
        for (int index = 0; index < methodCount; index++)
        {
            MethodDefinitionHandle method = metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"Method{index}"),
                methodSignatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
            metadata.AddCustomAttribute(
                method,
                longConstructor,
                emptyValueHandle);
            metadata.AddCustomAttribute(
                method,
                editorBrowsableConstructor,
                hiddenValueHandle);
        }
        return Serialize(metadata);
    }

    static byte[] BuildWideSignatureImage(int parameterCount, int nameLength)
    {
        var metadata = Metadata("Wide");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle parameterType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('P', nameLength)));
        AddModuleAndPublicType(metadata, "Wide");
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            parameterCount,
            returnType => returnType.Void(),
            parameters =>
            {
                for (int index = 0; index < parameterCount; index++)
                    parameters.AddParameter().Type().Type(parameterType, isValueType: false);
            });
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Wide"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildLargeAccessorReturnAttributeImage(
        AccessorOwner owner,
        int valueLength)
    {
        var metadata = Metadata($"{owner}ReturnAttribute");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(
            metadata,
            $"{owner}ReturnAttribute");
        ParameterHandle returnParameter = metadata.AddParameter(
            ParameterAttributes.None,
            default,
            sequenceNumber: 0);
        var accessorSignature = new BlobBuilder();
        new BlobEncoder(accessorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType =>
                {
                    if (owner == AccessorOwner.Property)
                        returnType.Type().Int32();
                    else
                        returnType.Void();
                },
                _ => { });
        MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(
                owner == AccessorOwner.Property ? "get_Value" : "add_Changed"),
            metadata.GetOrAddBlob(accessorSignature),
            bodyOffset: -1,
            returnParameter);
        if (owner == AccessorOwner.Property)
        {
            var propertySignature = new BlobBuilder();
            new BlobEncoder(propertySignature).PropertySignature(
                isInstanceProperty: true).Parameters(
                    0,
                    returnType => returnType.Type().Int32(),
                    _ => { });
            PropertyDefinitionHandle property = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(propertySignature));
            metadata.AddPropertyMap(type, property);
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Getter,
                accessor);
        }
        else
        {
            TypeReferenceHandle eventType = metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("EventHandler"));
            EventDefinitionHandle @event = metadata.AddEvent(
                EventAttributes.None,
                metadata.GetOrAddString("Changed"),
                eventType);
            metadata.AddEventMap(type, @event);
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Adder,
                accessor);
        }
        var value = new BlobBuilder(valueLength + 16);
        value.WriteUInt16(1);
        value.WriteCompressedInteger(valueLength);
        for (int index = 0; index < valueLength; index++)
            value.WriteByte((byte)'"');
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            returnParameter,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildInterfaceFloodImage(int interfaceCount, int nameLength)
    {
        var metadata = Metadata("Interfaces");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle interfaceType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('I', nameLength)));
        TypeDefinitionHandle type = AddModuleAndPublicType(
            metadata,
            "Interfaces",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        for (int index = 0; index < interfaceCount; index++)
            metadata.AddInterfaceImplementation(type, interfaceType);
        return Serialize(metadata);
    }

    static byte[] BuildWideTypeSpecImage(
        WideTypeSpecUse use,
        int argumentCount,
        int nameLength)
    {
        var metadata = Metadata($"Wide{use}");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle genericType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"Generic`{argumentCount}"));
        TypeReferenceHandle argumentType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('A', nameLength)));
        var typeSpecSignature = new BlobBuilder();
        WriteWideGenericType(
            typeSpecSignature,
            genericType,
            argumentType,
            argumentCount);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Wide"),
            use == WideTypeSpecUse.BaseType ? typeSpec : default(EntityHandle),
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        switch (use)
        {
            case WideTypeSpecUse.Field:
                var fieldSignature = new BlobBuilder();
                fieldSignature.WriteByte(0x06);
                WriteWideGenericType(
                    fieldSignature,
                    genericType,
                    argumentType,
                    argumentCount);
                metadata.AddFieldDefinition(
                    FieldAttributes.Public | FieldAttributes.Static,
                    metadata.GetOrAddString("Value"),
                    metadata.GetOrAddBlob(fieldSignature));
                break;
            case WideTypeSpecUse.Interface:
                metadata.AddInterfaceImplementation(type, typeSpec);
                break;
            case WideTypeSpecUse.Event:
                var accessorSignature = new BlobBuilder();
                new BlobEncoder(accessorSignature).MethodSignature().Parameters(
                    0,
                    returnType => returnType.Void(),
                    _ => { });
                MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("add_Changed"),
                    metadata.GetOrAddBlob(accessorSignature),
                    bodyOffset: -1,
                    MetadataTokens.ParameterHandle(1));
                EventDefinitionHandle @event = metadata.AddEvent(
                    EventAttributes.None,
                    metadata.GetOrAddString("Changed"),
                    typeSpec);
                metadata.AddEventMap(type, @event);
                metadata.AddMethodSemantics(
                    @event,
                    MethodSemanticsAttributes.Adder,
                    accessor);
                break;
            case WideTypeSpecUse.GenericConstraint:
                GenericParameterHandle parameter = metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
                metadata.AddGenericParameterConstraint(parameter, typeSpec);
                break;
        }

        return Serialize(metadata);
    }

    static void WriteWideGenericType(
        BlobBuilder signature,
        TypeReferenceHandle genericType,
        TypeReferenceHandle argumentType,
        int argumentCount)
    {
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(genericType) << 2 | 1);
        signature.WriteCompressedInteger(argumentCount);
        int argumentCode = MetadataTokens.GetRowNumber(argumentType) << 2 | 1;
        for (int index = 0; index < argumentCount; index++)
        {
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(argumentCode);
        }
    }

    static byte[] BuildLargeAttributeImage(int valueLength)
    {
        var metadata = Metadata("LargeAttribute");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Attributed");
        var value = new BlobBuilder(valueLength + 16);
        value.WriteUInt16(1);
        value.WriteCompressedInteger(valueLength);
        for (int index = 0; index < valueLength; index++)
            value.WriteByte((byte)'"');
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            type,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildNestedTypeSpecFieldImage(int depth, int nameLength)
    {
        var metadata = Metadata("Nested");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle head = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("Node`1"));
        TypeReferenceHandle argument = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('A', nameLength)));
        AddModuleAndPublicType(metadata, "Nested");
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        for (int index = 0; index < depth; index++)
            fieldSignature.WriteByte(0x15);
        fieldSignature.WriteByte(0x12);
        WriteTypeDefOrRef(fieldSignature, head);
        for (int index = 0; index < depth; index++)
        {
            fieldSignature.WriteCompressedInteger(1);
            fieldSignature.WriteByte(0x12);
            WriteTypeDefOrRef(fieldSignature, argument);
        }
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        return Serialize(metadata);
    }

    static byte[] BuildArgumentNestedTypeSpecFieldImage(int depth, int nameLength)
    {
        var metadata = Metadata("ArgumentNested");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle head = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"{new string('H', nameLength)}`1"));
        AddModuleAndPublicType(metadata, "ArgumentNested");
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        for (int index = 0; index < depth; index++)
        {
            fieldSignature.WriteByte(0x15);
            fieldSignature.WriteByte(0x12);
            WriteTypeDefOrRef(fieldSignature, head);
            fieldSignature.WriteCompressedInteger(1);
        }
        fieldSignature.WriteByte(0x08);
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        return Serialize(metadata);
    }

    static byte[] BuildNestedArrayFieldImage(int depth, int rank)
    {
        var metadata = Metadata("NestedArray");
        AddModuleAndPublicType(metadata, "NestedArray");
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        for (int index = 0; index < depth; index++)
            fieldSignature.WriteByte(0x14);
        fieldSignature.WriteByte(0x08);
        for (int index = 0; index < depth; index++)
        {
            fieldSignature.WriteCompressedInteger(rank);
            fieldSignature.WriteCompressedInteger(0);
            fieldSignature.WriteCompressedInteger(0);
        }
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        return Serialize(metadata);
    }

    static byte[] BuildNestedTypeNameChainImage(int depth, int nameLength)
    {
        var metadata = Metadata("NestedNames");
        StringHandle sharedName =
            metadata.GetOrAddString(new string('N', nameLength));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var handles = new TypeDefinitionHandle[depth];
        for (int index = 0; index < depth; index++)
        {
            handles[index] = metadata.AddTypeDefinition(
                index == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic,
                default,
                sharedName,
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
        for (int index = 1; index < handles.Length; index++)
            metadata.AddNestedType(handles[index], handles[index - 1]);
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedNestedGenericTypesImage(
        int typeCount,
        int depth,
        int nameLength,
        bool poison)
    {
        var metadata = Metadata("RepeatedNested");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle head = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"{new string('G', nameLength)}`1"));
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        for (int index = 0; index < depth; index++)
        {
            fieldSignature.WriteByte(0x15);
            fieldSignature.WriteByte(0x12);
            WriteTypeDefOrRef(fieldSignature, head);
            fieldSignature.WriteCompressedInteger(1);
        }
        fieldSignature.WriteByte(0x0e);
        BlobHandle fieldSignatureHandle =
            metadata.GetOrAddBlob(fieldSignature);
        var poisonSignature = new BlobBuilder();
        poisonSignature.WriteByte(0x06);
        BlobHandle poisonSignatureHandle =
            metadata.GetOrAddBlob(poisonSignature);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        int fieldRow = 1;
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"T{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(fieldRow),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                metadata.GetOrAddString("Value"),
                fieldSignatureHandle);
            fieldRow++;
            if (poison)
            {
                metadata.AddFieldDefinition(
                    FieldAttributes.Public,
                    metadata.GetOrAddString("Poison"),
                    poisonSignatureHandle);
                fieldRow++;
            }
        }
        return Serialize(metadata);
    }

    static byte[] BuildLargeTransformArrayImage(
        TransformArrayKind kind,
        int elementCount)
    {
        var metadata = Metadata($"Large{kind}");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        string attributeName = kind switch
        {
            TransformArrayKind.TupleElementNames => "TupleElementNamesAttribute",
            TransformArrayKind.Nullable => "NullableAttribute",
            TransformArrayKind.Dynamic => "DynamicAttribute",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString(attributeName));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x1d);
        constructorSignature.WriteByte((byte)(kind switch
        {
            TransformArrayKind.TupleElementNames => 0x0e,
            TransformArrayKind.Nullable => 0x05,
            TransformArrayKind.Dynamic => 0x02,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        }));
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        AddModuleAndPublicType(metadata, "Transformed");
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        fieldSignature.WriteByte(0x1c);
        FieldDefinitionHandle field = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        var value = new BlobBuilder(elementCount + 8);
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        byte element = kind == TransformArrayKind.TupleElementNames
            ? (byte)0xff
            : (byte)0;
        for (int index = 0; index < elementCount; index++)
            value.WriteByte(element);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            field,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedMethodGenericContextImage(
        int genericParameterCount,
        int nameLength,
        int methodCount)
    {
        var metadata = Metadata("GenericContext");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString($"Host`{genericParameterCount}"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        StringHandle genericName =
            metadata.GetOrAddString(new string('G', nameLength));
        for (int index = 0; index < genericParameterCount; index++)
        {
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                genericName,
                index);
        }
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            0,
            returnType => returnType.Void(),
            _ => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < methodCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{index}"),
                signatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildHugeArrayRankFieldImage(int rank)
    {
        var metadata = Metadata("HugeArray");
        AddModuleAndPublicType(metadata, "HugeArray");
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(0x14);
        signature.WriteByte(0x08);
        signature.WriteCompressedInteger(rank);
        signature.WriteCompressedInteger(0);
        signature.WriteCompressedInteger(0);
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(signature));
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedHiddenAttributeImage(
        int typeCount,
        int blobLength)
    {
        var metadata = Metadata("Hidden");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System.ComponentModel"),
            metadata.GetOrAddString("EditorBrowsableAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder(blobLength);
        value.WriteUInt16(1);
        value.WriteInt32(1);
        for (int index = 6; index < blobLength; index++)
            value.WriteByte(0);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var types = new List<TypeDefinitionHandle>(typeCount);
        for (int index = 0; index < typeCount; index++)
        {
            types.Add(metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"T{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1)));
        }
        foreach (TypeDefinitionHandle type in types)
            metadata.AddCustomAttribute(type, constructor, valueHandle);
        return Serialize(metadata);
    }

    static byte[] BuildHiddenAutoPropertyImage(
        int argumentCount,
        int nameLength)
    {
        var metadata = Metadata("HiddenAuto");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle compilerGenerated = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CompilerGeneratedAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            compilerGenerated,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle genericType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString($"Generic`{argumentCount}"));
        TypeReferenceHandle argumentType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('A', nameLength)));
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        var getterSignature = new BlobBuilder();
        new BlobEncoder(getterSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            MethodAttributes.Private
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Hidden"),
            metadata.GetOrAddBlob(getterSignature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        var attributeValue = new BlobBuilder();
        attributeValue.WriteUInt16(1);
        attributeValue.WriteUInt16(0);
        metadata.AddCustomAttribute(
            getter,
            constructor,
            metadata.GetOrAddBlob(attributeValue));
        var propertySignature = new BlobBuilder();
        propertySignature.WriteByte(0x28);
        propertySignature.WriteCompressedInteger(0);
        WriteWideGenericType(
            propertySignature,
            genericType,
            argumentType,
            argumentCount);
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Hidden"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        return Serialize(metadata);
    }

    static byte[] BuildHugeParameterDefaultImage(int characterCount)
    {
        var metadata = Metadata("DefaultBomb");
        AddModuleAndPublicType(metadata, "Host");
        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.Optional | ParameterAttributes.HasDefault,
            metadata.GetOrAddString("value"),
            sequenceNumber: 1);
        metadata.AddConstant(parameter, new string('"', characterCount));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            1,
            returnType => returnType.Void(),
            parameters => parameters.AddParameter().Type().String());
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            parameter);
        return Serialize(metadata);
    }

    static byte[] BuildNestedEnumDefaultImage(int depth, int nameLength)
    {
        var metadata = Metadata("EnumDefaultBomb");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumBase = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeDefinitionHandle host = AddModuleAndPublicType(metadata, "Host");
        TypeDefinitionHandle target =
            MetadataTokens.TypeDefinitionHandle(depth + 3);
        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.Optional | ParameterAttributes.HasDefault,
            metadata.GetOrAddString("value"),
            sequenceNumber: 1);
        metadata.AddConstant(parameter, 1);
        var methodSignature = new BlobBuilder();
        methodSignature.WriteByte(0x00);
        methodSignature.WriteCompressedInteger(1);
        methodSignature.WriteByte(0x01);
        methodSignature.WriteByte(0x11);
        WriteTypeDefOrRef(methodSignature, target);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Static
                | MethodAttributes.Abstract,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(methodSignature),
            bodyOffset: -1,
            parameter);

        TypeDefinitionHandle parent = host;
        for (int index = 0; index < depth; index++)
        {
            TypeDefinitionHandle nested = metadata.AddTypeDefinition(
                TypeAttributes.NestedPrivate
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString(new string('E', nameLength)),
                enumBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
            metadata.AddNestedType(nested, parent);
            parent = nested;
        }

        TypeDefinitionHandle actualTarget = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("TargetEnum"),
            enumBase,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(2));
        Assert.Equal(target, actualTarget);
        var enumFieldSignature = new BlobBuilder();
        enumFieldSignature.WriteByte(0x06);
        enumFieldSignature.WriteByte(0x08);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(enumFieldSignature));
        FieldDefinitionHandle literal = metadata.AddFieldDefinition(
            FieldAttributes.Public
                | FieldAttributes.Static
                | FieldAttributes.Literal,
            metadata.GetOrAddString("One"),
            metadata.GetOrAddBlob(enumFieldSignature));
        metadata.AddConstant(literal, 1);
        return Serialize(metadata);
    }

    static byte[] BuildEnumDefaultDecoyImage(
        int decoyTypeCount,
        int defaultMethodCount,
        int baseNameLength)
    {
        var metadata = Metadata("EnumDefaultDecoyBomb");
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle longBase = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString(new string('B', baseNameLength)));
        AddModuleAndPublicType(metadata, "Host");
        for (int index = 0; index < decoyTypeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Decoys"),
                metadata.GetOrAddString($"D{index}"),
                longBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(defaultMethodCount + 1));
        }

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            1,
            returnType => returnType.Void(),
            parameters => parameters.AddParameter().Type().Int32());
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < defaultMethodCount; index++)
        {
            ParameterHandle parameter = metadata.AddParameter(
                ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
            metadata.AddConstant(parameter, 1);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{index}"),
                signatureHandle,
                bodyOffset: -1,
                parameter);
        }
        return Serialize(metadata);
    }

    static byte[] BuildEnumDefaultTypeSpecDecoyImage(
        int decoyTypeCount,
        int defaultMethodCount,
        int rank)
    {
        var metadata = Metadata("EnumDefaultTypeSpecBomb");
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x14);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteCompressedInteger(rank);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle decoyBase =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        AddModuleAndPublicType(metadata, "Host");
        for (int index = 0; index < decoyTypeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Decoys"),
                metadata.GetOrAddString($"D{index}"),
                decoyBase,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(defaultMethodCount + 1));
        }

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            1,
            returnType => returnType.Void(),
            parameters => parameters.AddParameter().Type().Int32());
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < defaultMethodCount; index++)
        {
            ParameterHandle parameter = metadata.AddParameter(
                ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
            metadata.AddConstant(parameter, 1);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{index}"),
                signatureHandle,
                bodyOffset: -1,
                parameter);
        }
        return Serialize(metadata);
    }

    static byte[] BuildDeepBoxedAttributeImage(int depth)
    {
        var metadata = Metadata("BoxedNest");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Object());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        for (int index = 0; index < depth; index++)
            value.WriteByte(0x51);
        value.WriteByte(0x08);
        value.WriteInt32(1);
        value.WriteUInt16(0);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildNamedArgumentArrayCountImage(int elementCount)
    {
        var metadata = Metadata("NamedArrayCount");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(elementCount);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildNamedNestedArrayAttributeImage(int depth)
    {
        var metadata = Metadata("NamedNest");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x51);
        value.WriteSerializedString("V");
        value.WriteInt32(1);
        for (int index = 0; index < depth; index++)
        {
            value.WriteByte(0x1d);
            value.WriteByte(0x51);
            value.WriteInt32(1);
        }

        value.WriteByte(0x08);
        value.WriteInt32(7);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildTypeRefEnumDesyncImage(int elementCount)
    {
        var metadata = Metadata("EnumDesync");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle enumRef = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"));
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumRef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt64(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildEnumCmodDesyncImage(int modifierCount, int elementCount)
    {
        var metadata = Metadata("EnumCmod");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumDef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        int coded = (MetadataTokens.GetRowNumber(systemEnum) << 2) | 0x01;
        for (int index = 0; index < modifierCount; index++)
        {
            fieldSignature.WriteByte(0x20);
            fieldSignature.WriteCompressedInteger(coded);
        }

        fieldSignature.WriteByte(0x0a);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildAssemblyQualifiedNamedEnumImage(int elementCount)
    {
        var metadata = Metadata("EnumSuffix");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString("Samples.E, Other");
        value.WriteSerializedString("F");
        value.WriteInt64(0);
        value.WriteByte(0x53);
        value.WriteByte(0x1d);
        value.WriteByte(0x08);
        value.WriteSerializedString("V");
        value.WriteInt32(elementCount);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildGenericEarlierThenArrayImage(
        bool pointerToFnPtr,
        int elementCount)
    {
        var metadata = Metadata("FnPtrDesync");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(2);
        if (pointerToFnPtr)
            typeSpecSignature.WriteByte(0x0f);
        typeSpecSignature.WriteByte(0x1b);
        typeSpecSignature.WriteByte(0x00);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteByte(0x01);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildClassTypeDefRow4DesyncImage(int elementCount)
    {
        var metadata = Metadata("ClassDesync");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Pad"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle dummy = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Dummy"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(3);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, dummy);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildValueTypeTypeRefRow4DesyncImage(int elementCount)
    {
        var metadata = Metadata("VtDesync");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T1"));
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T2"));
        metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T3"));
        TypeReferenceHandle typeRef4 = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("A"),
            metadata.GetOrAddString("T4"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`2"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(3);
        typeSpecSignature.WriteByte(0x11);
        WriteTypeDefOrRef(typeSpecSignature, typeRef4);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteByte(0x1d);
        typeSpecSignature.WriteByte(0x08);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(1);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildSelfReferentialGenericVarImage()
    {
        var metadata = Metadata("VarSo");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle attributeType = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("MyAttr`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        WriteTypeDefOrRef(typeSpecSignature, attributeType);
        typeSpecSignature.WriteCompressedInteger(1);
        typeSpecSignature.WriteByte(0x13);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(1);
        constructorSignature.WriteByte(0x01);
        constructorSignature.WriteByte(0x13);
        constructorSignature.WriteCompressedInteger(0);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            attributed,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildClassSystemStringImage(int elementCount)
    {
        var metadata = Metadata("ClassString");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemString = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("String"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(systemString, isValueType: false);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildDottedSystemTypeImage(int elementCount)
    {
        var metadata = Metadata("DottedType");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemType = metadata.AddTypeReference(
            other,
            default,
            metadata.GetOrAddString("System.Type"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(systemType, isValueType: false);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(string.Empty);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        metadata.AddCustomAttribute(type, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildStringTypedEnumImage(int elementCount)
    {
        var metadata = Metadata("StringEnum");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        TypeDefinitionHandle enumDef = MetadataTokens.TypeDefinitionHandle(2);
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(enumDef, isValueType: true);
                    parameters.AddParameter().Type().SZArray().Int32();
                });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var fieldSignature = new BlobBuilder();
        new BlobEncoder(fieldSignature).FieldSignature().String();
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            metadata.GetOrAddString("value__"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle host = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(2),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(0);
        value.WriteInt32(elementCount);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(host, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildBoxedEnumArrayEmptyNameImage(int elementCount)
    {
        var metadata = Metadata("BoxedEnumAmp");
        AssemblyReferenceHandle other = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            other,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Object());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle host = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Host"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteByte(0x1d);
        value.WriteByte(0x55);
        value.WriteByte(0x00);
        value.WriteInt32(elementCount);
        metadata.AddCustomAttribute(host, constructor, metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildCustomAttributeArrayCountImage(
        int attributeCount,
        int elementCount)
    {
        var metadata = Metadata("ArrayCount");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().SZArray().Int32());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(elementCount);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < attributeCount; index++)
        {
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Attributed{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddCustomAttribute(type, constructor, valueHandle);
        }

        return Serialize(metadata);
    }

    static byte[] BuildCustomAttributeNamedArgumentCountImage(
        int attributeCount,
        int namedArgumentCount)
    {
        var metadata = Metadata("NamedArgCount");
        AssemblyReferenceHandle assembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Other"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16((ushort)namedArgumentCount);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < attributeCount; index++)
        {
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Attributed{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddCustomAttribute(type, constructor, valueHandle);
        }

        return Serialize(metadata);
    }

    static byte[] BuildRefPropertyDuplicateSeq0TypeSpecImage(
        int returnParameterCount,
        int rank)
    {
        var metadata = Metadata("RefPropertySeq0TypeSpec");
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x14);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteCompressedInteger(rank);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        BlobHandle attributeValue =
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        ParameterHandle first = default;
        for (int i = 0; i < returnParameterCount; i++)
        {
            ParameterHandle parameter = metadata.AddParameter(
                ParameterAttributes.None,
                default,
                sequenceNumber: 0);
            if (i == 0)
                first = parameter;
            metadata.AddCustomAttribute(parameter, constructor, attributeValue);
        }

        var accessorSignature = new BlobBuilder();
        new BlobEncoder(accessorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Type(isByRef: true).Int32(),
                _ => { });
        MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob(accessorSignature),
            bodyOffset: -1,
            first);
        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature).PropertySignature(
            isInstanceProperty: true).Parameters(
                0,
                returnType => returnType.Type(isByRef: true).Int32(),
                _ => { });
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            accessor);
        return Serialize(metadata);
    }

    static byte[] BuildAccessorTypeSpecArrayAttributeImage(int rank)
    {
        var metadata = Metadata("AccessorTypeSpec");
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x14);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteCompressedInteger(rank);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(metadata, "Host");
        ParameterHandle returnParameter = metadata.AddParameter(
            ParameterAttributes.None,
            default,
            sequenceNumber: 0);
        var accessorSignature = new BlobBuilder();
        new BlobEncoder(accessorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Type().Int32(),
                _ => { });
        MethodDefinitionHandle accessor = metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob(accessorSignature),
            bodyOffset: -1,
            returnParameter);
        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature).PropertySignature(
            isInstanceProperty: true).Parameters(
                0,
                returnType => returnType.Type().Int32(),
                _ => { });
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            accessor);
        metadata.AddCustomAttribute(
            accessor,
            constructor,
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00 }));
        return Serialize(metadata);
    }

    static byte[] BuildTypeSpecArrayAttributeImage(int rank, int typeCount)
    {
        var metadata = Metadata("AttributeTypeSpecBomb");
        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x14);
        typeSpecSignature.WriteByte(0x08);
        typeSpecSignature.WriteCompressedInteger(rank);
        typeSpecSignature.WriteCompressedInteger(0);
        typeSpecSignature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        BlobHandle truncatedValue =
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00 });
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < typeCount; index++)
        {
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Host{index}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddCustomAttribute(type, constructor, truncatedValue);
        }
        return Serialize(metadata);
    }

    static byte[] BuildLocalExtensionFloodImage(
        int methodCount,
        int assemblyPublicKeyLength = 0)
    {
        var metadata = Metadata(
            "ExtensionFlood",
            assemblyPublicKeyLength == 0
                ? null
                : new byte[assemblyPublicKeyLength]);
        AssemblyReferenceHandle coreLibrary = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle extensionAttribute = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("ExtensionAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            extensionAttribute,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        BlobHandle attributeValue =
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle extensions = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Extensions"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddCustomAttribute(extensions, constructor, attributeValue);
        for (int index = 0; index < methodCount; index++)
        {
            TypeReferenceHandle discriminator = metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"T{index}"));
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature().Parameters(
                2,
                returnType => returnType.Void(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(
                        target,
                        isValueType: false);
                    parameters.AddParameter().Type().Type(
                        discriminator,
                        isValueType: false);
                });
            MethodDefinitionHandle method = metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
            metadata.AddCustomAttribute(method, constructor, attributeValue);
        }
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedFinalizerImage(
        int typeCount,
        int publicKeyLength)
    {
        var metadata = Metadata("FinalizerTokenBomb");
        AssemblyReferenceHandle coreLibrary = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[publicKeyLength]),
            AssemblyFlags.PublicKey,
            default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString($"Finalizable{index}"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(index + 1));
        }

        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Finalize"),
                signatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }
        return Serialize(metadata);
    }

    static byte[] BuildForwarderImage(string typeName, string assemblyName)
    {
        var metadata = Metadata("ForwarderBomb");
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddExportedType(
            TypeAttributes.Public | (TypeAttributes)0x00200000,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(typeName),
            target,
            typeDefinitionId: 0);
        return Serialize(metadata);
    }

    static byte[] BuildWideGenericAttributeImage(
        int argumentCount,
        int nameLength)
    {
        var metadata = Metadata("GenericAttributeBomb");
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString($"GenericAttribute`{argumentCount}"));
        TypeReferenceHandle argumentType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(new string('A', nameLength)));
        var typeSpecSignature = new BlobBuilder();
        WriteWideGenericType(
            typeSpecSignature,
            attributeType,
            argumentType,
            argumentCount);
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            typeSpec,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle host = AddModuleAndPublicType(metadata, "Host");
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            host,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedEnumAttributeLookupImage(
        int typeCount,
        int namedArgumentCount,
        int attributeCount,
        bool poisonTypeDefinitionIndex = false)
    {
        var metadata = Metadata("EnumAttributeLookupBomb");
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("SampleAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        AddModuleAndPublicType(metadata, "Host");
        if (poisonTypeDefinitionIndex)
        {
            TypeDefinitionHandle poison = metadata.AddTypeDefinition(
                TypeAttributes.NestedAssembly,
                default,
                metadata.GetOrAddString("Poison"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(poison, poison);
        }
        for (int i = 0; i < typeCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples.Decoys.Namespace"),
                metadata.GetOrAddString($"Decoy{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        TypeDefinitionHandle attributed = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16((ushort)namedArgumentCount);
        for (int i = 0; i < namedArgumentCount; i++)
        {
            value.WriteByte(0x54);
            value.WriteByte(0x55);
            value.WriteSerializedString("NoSuchEnumType");
            value.WriteSerializedString("P");
            value.WriteInt32(1);
        }
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        for (int i = 0; i < attributeCount; i++)
        {
            metadata.AddCustomAttribute(
                attributed,
                constructor,
                valueHandle);
        }
        return Serialize(metadata);
    }

    static byte[] BuildRepeatedParameterEnumAttributeLookupImage(
        int typeCount,
        int methodCount,
        string attributeNamespace,
        string attributeName)
    {
        var metadata = Metadata("ParamEnumAttributeLookupBomb");
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString(attributeNamespace),
            metadata.GetOrAddString(attributeName));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));

        AddModuleAndPublicType(metadata, "Host");
        for (int i = 0; i < typeCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Samples.Decoys.Namespace"),
                metadata.GetOrAddString($"Decoy{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(methodCount + 1));
        }

        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: false).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Int32());
        BlobHandle methodSignatureHandle = metadata.GetOrAddBlob(methodSignature);
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(1);
        value.WriteByte(0x54);
        value.WriteByte(0x55);
        value.WriteSerializedString("NoSuchEnumType");
        value.WriteSerializedString("P");
        value.WriteInt32(1);
        BlobHandle valueHandle = metadata.GetOrAddBlob(value);
        for (int i = 0; i < methodCount; i++)
        {
            ParameterHandle parameter = metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("arg"),
                sequenceNumber: 1);
            metadata.AddCustomAttribute(parameter, constructor, valueHandle);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{i}"),
                methodSignatureHandle,
                bodyOffset: -1,
                parameter);
        }

        return Serialize(metadata);
    }

    static byte[] BuildNestedTypeReferenceFieldImage(int depth, int nameLength)
    {
        var metadata = Metadata("NestedTypeReferenceBomb");
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        EntityHandle scope = target;
        for (int index = 0; index < depth; index++)
        {
            scope = metadata.AddTypeReference(
                scope,
                index == 0
                    ? metadata.GetOrAddString("Contracts")
                    : default,
                metadata.GetOrAddString(new string('N', nameLength)));
        }

        AddModuleAndPublicType(metadata, "Host");
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        signature.WriteByte(0x12);
        WriteTypeDefOrRef(signature, scope);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(signature));
        return Serialize(metadata);
    }

    static byte[] BuildLargeObsoleteAttributeImage(int messageLength)
    {
        var metadata = Metadata("ObsoleteBomb");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle obsoleteType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ObsoleteAttribute"));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            obsoleteType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        TypeDefinitionHandle type = AddModuleAndPublicType(
            metadata,
            "ObsoleteBomb");
        var value = new BlobBuilder(messageLength + 8);
        value.WriteUInt16(1);
        value.WriteCompressedInteger(messageLength);
        for (int index = 0; index < messageLength; index++)
            value.WriteByte((byte)'X');
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            type,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    public sealed class GenericApiSurfaceAttribute<T> : Attribute
    {
    }

    [GenericApiSurfaceAttribute<string>]
    public sealed class GenericAttributeBoundedFixture
    {
    }

    public sealed class LegalNamedAttribute : Attribute
    {
        public LegalNamedAttribute(string value)
        {
        }

        public int Count { get; set; }
    }

    [LegalNamed("ok", Count = 3)]
    public sealed class LegalNamedAttributeFixture
    {
    }

    public sealed class NestedEnumHost
    {
        public enum Wide : long
        {
            Value = 0x112233445566778
        }
    }

    public sealed class LegalNestedEnumAttribute : Attribute
    {
        public NestedEnumHost.Wide Choice { get; set; }
    }

    [LegalNestedEnum(Choice = NestedEnumHost.Wide.Value)]
    public sealed class LegalNestedEnumFixture
    {
    }

    public sealed class LegalGenericCtorAttribute<T> : Attribute
    {
        public LegalGenericCtorAttribute(T value)
        {
        }
    }

    [LegalGenericCtor<int>(5)]
    public sealed class LegalGenericCtorFixture
    {
    }

    static void AssertCompilerAttributeParity(
        string fixtureTypeName,
        string attributeMarker,
        string valueMarker)
    {
        using var unboundedStream = File.OpenRead(SelfPath);
        using var unboundedReader = new PEReader(unboundedStream);
        ApiSurface unbounded = ApiSurfaceExtractor.Extract(
            unboundedReader,
            ApiSurfaceExtractionScope.Public);
        ApiType unboundedType = Assert.Single(
            unbounded.Types,
            type => type.FullName.EndsWith(fixtureTypeName, StringComparison.Ordinal));
        using var boundedStream = File.OpenRead(SelfPath);
        using var boundedReader = new PEReader(boundedStream);
        var bounded = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                boundedReader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        ApiType boundedType = Assert.Single(
            bounded.Surface.Types,
            type => type.FullName.EndsWith(fixtureTypeName, StringComparison.Ordinal));
        Assert.Equal(unboundedType.Attributes, boundedType.Attributes);
        Assert.Contains(
            unboundedType.Attributes,
            attribute => attribute.Contains(attributeMarker, StringComparison.Ordinal)
                && attribute.Contains(valueMarker, StringComparison.Ordinal));
    }

    static void WriteTypeDefOrRef(BlobBuilder signature, EntityHandle handle)
    {
        int tag = handle.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            HandleKind.TypeSpecification => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(handle)),
        };
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(handle) << 2 | tag);
    }

    public enum WideTypeSpecUse
    {
        Field,
        BaseType,
        Event,
        Interface,
        GenericConstraint,
    }

    enum AccessorOwner
    {
        Property,
        Event,
    }

    public enum TransformArrayKind
    {
        TupleElementNames,
        Nullable,
        Dynamic,
    }

    static MetadataBuilder Metadata(
        string assemblyName,
        byte[]? publicKey = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            publicKey is null
                ? default
                : metadata.GetOrAddBlob(publicKey),
            publicKey is null
                ? default
                : AssemblyFlags.PublicKey,
            default);
        return metadata;
    }

    static TypeDefinitionHandle AddModuleAndPublicType(
        MetadataBuilder metadata,
        string name,
        TypeAttributes attributes = TypeAttributes.Public | TypeAttributes.Abstract)
    {
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata.AddTypeDefinition(
            attributes,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(name),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
