using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Inspector.Findings;
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
public sealed partial class ApiSurfaceExtractorBoundsTests
{
    static readonly string SelfPath = typeof(ApiSurfaceExtractorBoundsTests).Assembly.Location;

    static int CompilerGeneratedProjectionMarker(int value)
    {
        static int Marker(int input) => input + 1;
        return Marker(value);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CompilerGeneratedBoundedProjection_IsOptInAndFinite()
    {
        var generous = new ApiSurfaceExtractionBounds(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(SelfPath);

        ApiSurface ordinary = Assert.IsType<
                ApiSurfaceExtractionResult.Extracted>(
                session.BoundedApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll,
                    generous))
            .Surface;
        ApiSurface generated = Assert.IsType<
                ApiSurfaceExtractionResult.Extracted>(
                session.BoundedApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll,
                    generous,
                    includeCompilerGenerated: true))
            .Surface;

        static bool IsMarker(ApiMember member) =>
            member.Name.Contains(
                "g__Marker|",
                StringComparison.Ordinal);
        Assert.DoesNotContain(
            ordinary.Types.SelectMany(type => type.Members),
            IsMarker);
        Assert.Contains(
            generated.Types.SelectMany(type => type.Members),
            IsMarker);

        int generatedMembers =
            generated.Types.Sum(type => type.Members.Count);
        var exceeded = Assert.IsType<
            ApiSurfaceExtractionResult.Exceeded>(
            session.BoundedApiSurface(
                ApiSurfaceExtractionScope.IncludeAll,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    generatedMembers - 1,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue),
                includeCompilerGenerated: true));
        Assert.Equal(
            ApiSurfaceExtractionBound.Members,
            exceeded.Bound);
    }

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
    public void MalformedModuleMvid_PrecedesMetadataRowBudget()
    {
        byte[] image = BuildInvalidModuleMvidImage();

        Assert.Throws<BadImageFormatException>(
            () => Extract(
                image,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    maxMetadataRows: 0)));
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
    public void InterfaceReferenceContributesItsCompleteRetainedText()
    {
        const string assemblyName = "Dependency";
        const string culture = "en-US";
        const string token = "0011223344556677";
        const string fullName = "Dependency.ReallyLongInterface";
        const string typeNamespace = "Dependency";
        const string typeName = "ReallyLongInterface";
        MetadataTypeDefinitionName definitionName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                typeNamespace,
                [typeName])).Name;
        var withoutReference = new ApiType();
        var withReference = new ApiType
        {
            InterfaceReferences =
            [
                new(
                    new ApiAssemblyIdentity(
                        assemblyName,
                        new Version(1, 2, 3, 4),
                        culture,
                        token),
                    fullName,
                    definitionName),
            ],
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
    public void InterfaceReferencesParticipateInExactRetainedTextBudget()
    {
        byte[] withoutReferences = BuildInterfaceFloodImage(
            interfaceCount: 0,
            nameLength: 32,
            typeCount: 2);
        byte[] withReferences = BuildInterfaceFloodImage(
            interfaceCount: 1,
            nameLength: 32,
            typeCount: 2);
        ApiSurfaceExtractionBounds generous = new(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);
        var without = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(withoutReferences, generous));
        var with = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(withReferences, generous));
        int displayOnlyBudget = checked(
            without.RetainedTextCharacters
            + with.Surface.Types.Sum(type =>
                type.Interfaces.Sum(value => value.Length)));

        Assert.True(with.RetainedTextCharacters > displayOnlyBudget);
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                withReferences,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    displayOnlyBudget)));
        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharacters,
            exceeded.Bound);
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
}
