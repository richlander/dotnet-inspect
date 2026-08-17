using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates the bounded API-surface extraction: the bound is a hard retention budget the walk
/// enforces on itself, not a total a caller checks after the fact.
/// </summary>
/// <remarks>
/// The two claims that matter are that a bound is reachable — an image over budget is reported as
/// <see cref="ApiSurfaceExtractionResult.Exceeded"/> and yields no surface at all — and that a
/// bound is exact: budgets equal to the unbounded walk's own totals still extract the whole
/// surface, and one less than the walk needs stops it. Exactness is what lets a caller spend one
/// budget across several images and know the bounded accept set matches the unbounded one
/// whenever the image fits.
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
                int.MaxValue,
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
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.Types, exceeded.Bound);
    }

    [Fact]
    public void NegativeBounds_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(-1, 0, 0, 0, 0, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, -1, 0, 0, 0, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, -1, 0, 0, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, -1, 0, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, 0, -1, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, 0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceExtractionBounds(0, 0, 0, 0, 0, 0, -1));
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
                    int.MaxValue,
                    int.MaxValue)));

        Assert.Equal(ApiSurfaceExtractionBound.TypeForwarders, exceeded.Bound);
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
                    generous.MetadataRows,
                    int.MaxValue)));
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    generous.MetadataRows - 1,
                    int.MaxValue)));

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
        Assert.Equal(
            ApiSurfaceRetainedText.Surface(generous.Surface),
            generous.RetainedTextCharacters);

        var exact = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
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

        Assert.Equal(generous.RetainedTextCharacters, exact.RetainedTextCharacters);
        Assert.Equal(ApiSurfaceExtractionBound.RetainedTextCharacters, exceeded.Bound);
    }

    [Fact]
    public void RetainedTextPerModelBudget_IsExact()
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
        int largestModel = checked(
            (int)generous.Surface.Types.Select(ApiSurfaceRetainedText.TypeHeader)
                .Concat(
                    generous.Surface.Types.SelectMany(
                        type => type.Members.Select(ApiSurfaceRetainedText.Member)))
                .Concat(generous.Surface.InspectionFailures.Select(
                    ApiSurfaceRetainedText.InspectionFailure))
                .Concat(generous.Surface.TypeForwarders.Select(
                    ApiSurfaceRetainedText.TypeForwarder))
                .Max());

        Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    largestModel)));
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    largestModel - 1)));

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
    }

    [Fact]
    public void RepeatedLongMethodName_IsStoppedByRetainedTextBeforeRowBounds()
    {
        byte[] image = BuildRepeatedLongMethodNameImage(
            methodCount: 500,
            nameCharacters: 20_000);
        Assert.True(image.Length < 100_000);

        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(
                image,
                new ApiSurfaceExtractionBounds(
                    10,
                    1_000,
                    0,
                    0,
                    10_000,
                    100_000)));

        Assert.Equal(ApiSurfaceExtractionBound.RetainedTextCharacters, exceeded.Bound);
    }

    [Fact]
    public void ParameterFanOut_IsStoppedBeforeLargeSignatureMaterialization()
    {
        byte[] image = BuildParameterFanOutImage(
            parameterCount: 2_000,
            nameCharacters: 4_000);
        Assert.True(image.Length < 100_000);
        var bounds = new ApiSurfaceExtractionBounds(
            10,
            10,
            0,
            0,
            10_000,
            32_000_000,
            1_000_000);

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 24_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void InterfaceTypeFanOut_IsStoppedBeforeLargeHeaderMaterialization()
    {
        byte[] image = BuildInterfaceFanOutImage(
            argumentCount: 2_000,
            nameCharacters: 4_000);
        Assert.True(image.Length < 100_000);
        var bounds = new ApiSurfaceExtractionBounds(
            10,
            0,
            0,
            0,
            10_000,
            32_000_000,
            1_000_000);

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 24_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void ExpandingMethodName_IsStoppedBeforeContainedSpellingMaterialization()
    {
        byte[] image = BuildRepeatedLongMethodNameImage(
            methodCount: 1,
            nameCharacters: 200_000,
            nameCharacter: '\u202e');
        var bounds = BrowserTextBounds();

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 4_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void GiantParameterName_IsStoppedBeforeGetStringMaterialization()
    {
        // Sol R6: GetParameterInfo called GetString before the model budget, so a
        // 20M-char parameter name allocated ~80 MB on Exceeded.
        byte[] image = BuildGiantParameterNameImage(20_000_000);
        var bounds = BrowserTextBounds();

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 4_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void GiantPropertyName_IsStoppedBeforeGetStringMaterialization()
    {
        // Sol R6: GetPropertySignature called GetString before ReadBudgetedString.
        byte[] image = BuildGiantPropertyNameImage(20_000_000);
        var bounds = BrowserTextBounds();

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 4_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void StringDefault_IsStoppedBeforeDecodeAndEscaping()
    {
        byte[] image = BuildStringDefaultImage(2_000_000);
        var bounds = BrowserTextBounds();

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 4_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void AttributeString_IsStoppedBeforeDecodeAndEscaping()
    {
        byte[] image = BuildAttributeStringImage(2_000_000);
        var bounds = BrowserTextBounds();

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 4_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void EventComposite_IsStoppedBeforeSignatureMaterialization()
    {
        byte[] image = BuildEventImage(
            typeNameCharacters: 600_000,
            eventNameCharacters: 600_000);
        var bounds = BrowserTextBounds();

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 8_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void TypeSpecificationPreflight_UsesRetainedGenericGrammar()
    {
        byte[] image = BuildTupleEventImage();
        using (var peReader = new PEReader(
            new MemoryStream(image, writable: false)))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            TypeSpecificationHandle handle =
                MetadataTokens.TypeSpecificationHandle(1);
            TypeNode node = GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                TypeNodeProvider.CreateCaching(),
                (GenericContext?)null,
                new DegradedTypeNode());
            string resolved = TypeResolver.GetTypeNameFromSpecification(
                reader,
                handle);
            Assert.Equal(resolved, node.RenderCanonical());
            Assert.Equal(
                resolved.Length,
                node.RenderLength(canonicalTuples: true));
        }
        var bounds = new ApiSurfaceExtractionBounds(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            32_000_000,
            40);

        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
    }

    [Fact]
    public void TypeSpecificationPreflight_DoesNotExpandHostileNestedArity()
    {
        byte[] image = BuildNestedArityConstraintImage(250_000_000);
        var bounds = BrowserTextBounds();

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains(
            "N.Outer<int>.Inner<>",
            extracted.Surface.Types.Single().TypeParameters.Single().Constraints);
        Assert.True(
            allocated < 1_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void TypeForwarderCount_IsCheckedBeforeFullNameMaterialization()
    {
        byte[] image = BuildForwarderImage(1_200_000);
        var bounds = new ApiSurfaceExtractionBounds(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            0,
            int.MaxValue,
            32_000_000,
            1_000_000);

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(ApiSurfaceExtractionBound.TypeForwarders, exceeded.Bound);
        Assert.True(
            allocated < 1_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void TypeForwarderText_IsCheckedBeforeFullNameMaterialization()
    {
        byte[] image = BuildForwarderImage(1_200_000);
        var bounds = BrowserTextBounds();

        _ = Extract(image, bounds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var exceeded = Assert.IsType<ApiSurfaceExtractionResult.Exceeded>(
            Extract(image, bounds));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(
            ApiSurfaceExtractionBound.RetainedTextCharactersPerModel,
            exceeded.Bound);
        Assert.True(
            allocated < 1_000_000,
            $"Bounded extraction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void TypeNodeRenderLengths_AgreeWithEveryRenderedView()
    {
        var tuple = new GenericTypeNode(
            "System.ValueTuple",
            isReferenceType: false,
            [
                new NamedTypeNode("System.String", isReferenceType: true)
                {
                    IsNullableAnnotated = true,
                    TupleElementName = "text"
                },
                new SZArrayTypeNode(
                    new GenericTypeNode(
                        "Example.Result",
                        isReferenceType: true,
                        [new PrimitiveTypeNode("int", isReferenceType: false)]))
                {
                    TupleElementName = "values"
                }
            ]);
        TypeNode[] nodes =
        [
            tuple,
            new ByRefTypeNode(tuple),
            new MDArrayTypeNode(tuple, rank: 3) { IsNullableAnnotated = true },
            new PointerTypeNode(new PrimitiveTypeNode("int", isReferenceType: false)),
        ];

        foreach (TypeNode node in nodes)
        {
            Assert.Equal(node.Render().Length, node.RenderLength(canonicalTuples: false));
            Assert.Equal(
                node.RenderCanonical().Length,
                node.RenderLength(canonicalTuples: true));
        }
    }

    [Fact]
    public void RetainedTextCounter_CoversNestedTransferModels()
    {
        MetadataTypeDefinitionName definitionName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Definition.Namespace",
                    ["Outer", "Inner"]))
            .Name;
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Variance = "out",
            Constraints = ["Constraint"],
            StructuredConstraints =
                [new TypeParameterConstraint("Constraint", IsTypeName: true)]
        };
        var member = new ApiMember
        {
            Name = "Member",
            Kind = "method",
            Attributes = ["MemberAttribute"],
            ReturnType = "Return",
            Signature = "Signature",
            Digest = "Digest",
            CanonicalSignature = "Canonical",
            Accessibility = "protected",
            ObsoleteMessage = "Obsolete",
            ExtendedType = "Extended",
            DeclaringType = "Declaring",
            EnumValueLiteral = "42",
            SourceFilePath = "Member.cs",
            SourceUrl = "https://source/member",
            SourceChecksumAlgorithm = "SHA256",
            SignatureModel = new ApiSignature
            {
                ReturnType = "ModelReturn",
                CanonicalReturnType = "ModelCanonicalReturn",
                ReturnAttributes = ["ReturnAttribute"],
                MemberName = "ModelMember",
                TypeParameters = [typeParameter],
                Parameters =
                [
                    new ApiParameter
                    {
                        Attributes = ["ParameterAttribute"],
                        Name = "parameter",
                        Type = "ParameterType",
                        CanonicalType = "CanonicalParameterType",
                        Modifier = "ref",
                        DefaultValueText = "default"
                    }
                ],
                Accessors =
                [
                    new ApiAccessor
                    {
                        Kind = "get",
                        Accessibility = "private",
                        ReturnAttributes = ["AccessorAttribute"]
                    }
                ]
            }
        };
        var type = new ApiType
        {
            Namespace = "Namespace",
            Name = "Type",
            MetadataName = "MetadataType",
            DefinitionName = definitionName,
            Accessibility = "internal",
            Kind = "class",
            Attributes = ["TypeAttribute"],
            EnumUnderlyingType = "int",
            BaseType = "Base",
            Interfaces = ["Interface"],
            DerivedTypes = ["Derived"],
            TypeParameters = [typeParameter],
            SourceFilePath = "Type.cs",
            SourceUrl = "https://source/type",
            GitHubBrowseUrl = "https://github/type",
            SourceChecksumAlgorithm = "SHA256",
            SourceResolution = "SourceLink",
            AdditionalSourceFiles =
            [
                new PartialSourceFileInfo
                {
                    FilePath = "Partial.cs",
                    SourceUrl = "https://source/partial",
                    GitHubBrowseUrl = "https://github/partial",
                    SourceChecksumAlgorithm = "SHA256"
                }
            ],
            SourceAssemblyPath = "Assembly.dll"
        };
        var failure = new ApiSurfaceInspectionFailure(
            "operation",
            1,
            MetadataTypeNameFailureMechanism.Metadata,
            "kind",
            "detail");
        var forwarder = new TypeForwarder
        {
            DefinitionName = definitionName,
            TypeName = "Forwarded.Type",
            TargetAssembly = "Target"
        };
        string[] retainedText =
        [
            "Member", "method", "MemberAttribute", "Return", "Signature", "Digest",
            "Canonical", "protected", "Obsolete", "Extended", "Declaring", "42",
            "Member.cs", "https://source/member", "SHA256",
            "ModelReturn", "ModelCanonicalReturn", "ReturnAttribute", "ModelMember",
            "T", "out", "Constraint", "Constraint",
            "ParameterAttribute", "parameter", "ParameterType",
            "CanonicalParameterType", "ref", "default",
            "get", "private", "AccessorAttribute",
            "Namespace", "Type", "MetadataType",
            "Definition.Namespace", "Outer", "Inner",
            "internal", "class", "TypeAttribute", "int", "Base", "Interface", "Derived",
            "T", "out", "Constraint", "Constraint",
            "Type.cs", "https://source/type", "https://github/type", "SHA256",
            "SourceLink", "Partial.cs", "https://source/partial", "https://github/partial",
            "SHA256", "Assembly.dll",
            "operation", "kind", "detail",
            "Definition.Namespace", "Outer", "Inner", "Forwarded.Type", "Target"
        ];

        long counted = ApiSurfaceRetainedText.TypeHeader(type)
            + ApiSurfaceRetainedText.Member(member)
            + ApiSurfaceRetainedText.InspectionFailure(failure)
            + ApiSurfaceRetainedText.TypeForwarder(forwarder);

        Assert.Equal(retainedText.Sum(text => text.Length), counted);
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
        return Extract(peReader, bounds, typesOnly);
    }

    static ApiSurfaceExtractionResult Extract(
        byte[] image,
        ApiSurfaceExtractionBounds bounds)
    {
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        return Extract(peReader, bounds, typesOnly: false);
    }

    static ApiSurfaceExtractionResult Extract(
        PEReader peReader,
        ApiSurfaceExtractionBounds bounds,
        bool typesOnly)
    {
        return ApiSurfaceExtractor.ExtractBounded(
            peReader,
            ApiSurfaceExtractionScope.Public,
            bounds,
            typesOnly);
    }

    static byte[] BuildRepeatedLongMethodNameImage(
        int methodCount,
        int nameCharacters,
        char nameCharacter = 'M')
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Repeated.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Repeated"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Repeated"),
            metadata.GetOrAddString("Surface"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        StringHandle repeatedName = metadata.GetOrAddString(
            new string(nameCharacter, nameCharacters));
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(0);
        signature.WriteByte(0x01);
        BlobHandle signatureHandle = metadata.GetOrAddBlob(signature);
        for (int index = 0; index < methodCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                repeatedName,
                signatureHandle,
                bodyOffset: 0,
                parameterList: MetadataTokens.ParameterHandle(1));
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

    static byte[] BuildParameterFanOutImage(
        int parameterCount,
        int nameCharacters)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Fanout.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Fanout"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        TypeReferenceHandle repeatedType = metadata.AddTypeReference(
            target,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(new string('T', nameCharacters)));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fanout"),
            metadata.GetOrAddString("Surface"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        int codedType = CodedIndex.TypeDefOrRefOrSpec(repeatedType);
        for (int index = 0; index < parameterCount; index++)
        {
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(codedType);
        }
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildInterfaceFanOutImage(
        int argumentCount,
        int nameCharacters)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("InterfaceFanout.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("InterfaceFanout"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        TypeReferenceHandle genericInterface = metadata.AddTypeReference(
            target,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString($"IGeneric`{argumentCount}"));
        TypeReferenceHandle repeatedType = metadata.AddTypeReference(
            target,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(new string('T', nameCharacters)));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle surface = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fanout"),
            metadata.GetOrAddString("Surface"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(genericInterface));
        signature.WriteCompressedInteger(argumentCount);
        int codedArgument = CodedIndex.TypeDefOrRefOrSpec(repeatedType);
        for (int index = 0; index < argumentCount; index++)
        {
            signature.WriteByte(0x12);
            signature.WriteCompressedInteger(codedArgument);
        }
        TypeSpecificationHandle interfaceType =
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature));
        metadata.AddInterfaceImplementation(surface, interfaceType);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ApiSurfaceExtractionBounds BrowserTextBounds() => new(
        100_000,
        1_000_000,
        1_024,
        100_000,
        250_000,
        32_000_000,
        1_000_000);

    static byte[] BuildStringDefaultImage(int characterCount)
    {
        var metadata = CreateMetadata("StringDefault");
        AddModuleAndSurfaceTypes(metadata);
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x0e);
        signature.WriteByte(0x0e);
        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.Optional | ParameterAttributes.HasDefault,
            metadata.GetOrAddString("value"),
            1);
        metadata.AddConstant(parameter, new string('x', characterCount));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            0,
            parameter);
        return Serialize(metadata);
    }

    static byte[] BuildGiantParameterNameImage(int nameCharacters)
    {
        var metadata = CreateMetadata("GiantParam");
        AddModuleAndSurfaceTypes(metadata);
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x0e);
        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString(new string('P', nameCharacters)),
            1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            0,
            parameter);
        return Serialize(metadata);
    }

    static byte[] BuildGiantPropertyNameImage(int nameCharacters)
    {
        var metadata = CreateMetadata("GiantProp");
        TypeDefinitionHandle surface = AddModuleAndSurfaceTypes(metadata);
        var getterSignature = new BlobBuilder();
        getterSignature.WriteByte(0x00);
        getterSignature.WriteCompressedInteger(0);
        getterSignature.WriteByte(0x0e);
        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_P"),
            metadata.GetOrAddBlob(getterSignature),
            0,
            MetadataTokens.ParameterHandle(1));
        var propertySignature = new BlobBuilder();
        propertySignature.WriteByte(0x28);
        propertySignature.WriteCompressedInteger(0);
        propertySignature.WriteByte(0x0e);
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString(new string('P', nameCharacters)),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(surface, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        return Serialize(metadata);
    }

    static byte[] BuildAttributeStringImage(int characterCount)
    {
        var metadata = CreateMetadata("AttributeString");
        TypeDefinitionHandle surface = AddModuleAndSurfaceTypes(metadata);
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle attributeType = metadata.AddTypeReference(
            target,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("MarkAttribute"));
        var signature = new BlobBuilder();
        signature.WriteByte(0x20);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(0x0e);
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(signature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteSerializedString(new string('a', characterCount));
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            surface,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    static byte[] BuildEventImage(
        int typeNameCharacters,
        int eventNameCharacters)
    {
        var metadata = CreateMetadata("Event");
        TypeDefinitionHandle surface = AddModuleAndSurfaceTypes(metadata);
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle eventType = metadata.AddTypeReference(
            target,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(new string('T', typeNameCharacters)));
        MethodDefinitionHandle adder = AddEventAdder(metadata, eventType);
        EventDefinitionHandle eventDefinition = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString(new string('E', eventNameCharacters)),
            eventType);
        metadata.AddEventMap(surface, eventDefinition);
        metadata.AddMethodSemantics(
            eventDefinition,
            MethodSemanticsAttributes.Adder,
            adder);
        return Serialize(metadata);
    }

    static byte[] BuildTupleEventImage()
    {
        var metadata = CreateMetadata("TupleEvent");
        AssemblyReferenceHandle coreLib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Private.CoreLib"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle valueTuple = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ValueTuple`2"));
        TypeReferenceHandle stringType = metadata.AddTypeReference(
            coreLib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("String"));
        var typeSignature = new BlobBuilder();
        typeSignature.WriteByte(0x15);
        typeSignature.WriteByte(0x11);
        typeSignature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(valueTuple));
        typeSignature.WriteCompressedInteger(2);
        typeSignature.WriteByte(0x12);
        typeSignature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(stringType));
        typeSignature.WriteByte(0x12);
        typeSignature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(stringType));
        TypeSpecificationHandle tupleType = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(typeSignature));
        TypeDefinitionHandle surface = AddModuleAndSurfaceTypes(metadata);
        MethodDefinitionHandle adder = AddEventAdder(metadata, tupleType);
        EventDefinitionHandle eventDefinition = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("E"),
            tupleType);
        metadata.AddEventMap(surface, eventDefinition);
        metadata.AddMethodSemantics(
            eventDefinition,
            MethodSemanticsAttributes.Adder,
            adder);
        return Serialize(metadata);
    }

    static byte[] BuildForwarderImage(int nameCharacters)
    {
        var metadata = CreateMetadata("Forwarder");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddExportedType(
            TypeAttributes.Public | (TypeAttributes)0x0020_0000,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(new string('F', nameCharacters)),
            target,
            0);
        return Serialize(metadata);
    }

    static byte[] BuildNestedArityConstraintImage(int nestedArity)
    {
        var metadata = CreateMetadata("NestedArity");
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle outer = metadata.AddTypeReference(
            target,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer`1"));
        TypeReferenceHandle inner = metadata.AddTypeReference(
            outer,
            default,
            metadata.GetOrAddString($"Inner`{nestedArity}"));
        var typeSignature = new BlobBuilder();
        typeSignature.WriteByte(0x15);
        typeSignature.WriteByte(0x12);
        typeSignature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(inner));
        typeSignature.WriteCompressedInteger(1);
        typeSignature.WriteByte(0x08);
        TypeSpecificationHandle constraint =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle surface = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Surface`1"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        GenericParameterHandle parameter = metadata.AddGenericParameter(
            surface,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            0);
        metadata.AddGenericParameterConstraint(parameter, constraint);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadata(string name)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        return metadata;
    }

    static TypeDefinitionHandle AddModuleAndSurfaceTypes(
        MetadataBuilder metadata)
    {
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Surface"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
    }

    static MethodDefinitionHandle AddEventAdder(
        MetadataBuilder metadata,
        EntityHandle eventType)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x01);
        signature.WriteByte(
            eventType.Kind == HandleKind.TypeSpecification
                ? (byte)0x11
                : (byte)0x12);
        signature.WriteCompressedInteger(
            CodedIndex.TypeDefOrRefOrSpec(eventType));
        ParameterHandle parameter = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("value"),
            1);
        return metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("add_E"),
            metadata.GetOrAddBlob(signature),
            0,
            parameter);
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
