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
        int nameCharacters)
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

        StringHandle repeatedName = metadata.GetOrAddString(new string('M', nameCharacters));
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
}
