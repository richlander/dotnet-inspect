using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates the TypeResolution adapter that turns already-retained defining
/// images into the optional CA enum-width resolver. Missing, unplanned, or
/// unopened definitions keep the owned decoder's Int32 fallback.
/// </summary>
public sealed class TypeResolutionEnumWidthTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Fact]
    public void TryCreateRequest_QualifiedName_IsFromReference()
    {
        ResolvedAssemblyReference requesting = Descriptor(
            BuildCrossAssemblyInt64NamedEnumImage());
        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                "Samples.E, Other",
                requesting,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));
        var start = Assert.IsType<TypeResolutionStart.Reference>(request.Start);
        Assert.Equal("Other", start.Value.Name);
        Assert.Equal("Samples", request.Type.Namespace);
        Assert.Equal(["E"], request.Type.Segments);
    }

    [Fact]
    public void QualifiedNestedName_DecodesInt64()
    {
        byte[] definingImage = BuildDefiningNestedInt64EnumImage();
        byte[] userImage = BuildCrossAssemblyInt64NamedEnumImage(
            enumName: "Samples.Outer+E, Other");
        ResolvedAssemblyReference defining = Descriptor(definingImage);
        ResolvedAssemblyReference user = Descriptor(userImage);
        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                "Samples.Outer+E, Other",
                user,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));
        Assert.Equal(["Outer", "E"], request.Type.Segments);
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                current => current.Target
                        is AssemblyBindingTarget.AssemblyReference reference
                    && reference.Identity.Name == "Other"
                        ? AssemblyBindingSelection.Found(defining)
                        : AssemblyBindingSelection.NotFound()),
            [user],
            [request]);
        using var userPe = new PEReader(
            new MemoryStream(userImage, writable: false));
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(context, [request]);

        var decoded = AttributeDecoder.TryDecode(
            userPe.GetMetadataReader(),
            FirstAttribute(userPe.GetMetadataReader()),
            beforeMaterialize: null,
            resolver);

        Assert.NotNull(decoded);
        Assert.Equal(7L, decoded.Value.NamedArguments[0].Value);
    }

    [Theory]
    [InlineData(@"Samples.E\+Kind, Other", "E+Kind")]
    [InlineData(@"Samples.E\,Kind, Other", "E,Kind")]
    [InlineData(@"Samples.E\\Kind, Other", @"E\Kind")]
    public void EscapedMetadataName_DecodesInt64(
        string serializedName,
        string metadataName)
    {
        byte[] definingImage = BuildDefiningTypeImage(
            "Other",
            PrimitiveTypeCode.Int64,
            DefinitionShape.Enum,
            metadataName);
        byte[] userImage = BuildCrossAssemblyInt64NamedEnumImage(
            enumName: serializedName);
        ResolvedAssemblyReference defining = Descriptor(definingImage);
        ResolvedAssemblyReference user = Descriptor(userImage);
        TypeResolutionRequest request = Request(serializedName, user);
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                current => current.Target
                        is AssemblyBindingTarget.AssemblyReference reference
                    && reference.Identity.Name == "Other"
                        ? AssemblyBindingSelection.Found(defining)
                        : AssemblyBindingSelection.NotFound()),
            [user],
            [request]);
        using var userPe = new PEReader(
            new MemoryStream(userImage, writable: false));
        MetadataReader userReader = userPe.GetMetadataReader();
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(context, [request]);
        Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(request));
        Assert.Equal(
            PrimitiveTypeCode.Int64,
            resolver(request.Type.ToMetadataFullName()));
        var callbackNames = new List<string>();
        PrimitiveTypeCode Observe(string name)
        {
            callbackNames.Add(name);
            return resolver(name);
        }

        var decoded = AttributeDecoder.TryDecode(
            userReader,
            FirstAttribute(userReader),
            beforeMaterialize: null,
            Observe);

        Assert.True(
            decoded.HasValue,
            $"Callback names: {string.Join(", ", callbackNames)}");
        Assert.NotEmpty(callbackNames);
        Assert.All(
            callbackNames,
            name => Assert.Equal(
                request.Type.ToMetadataFullName(),
                name));
        Assert.Equal(7L, decoded.Value.NamedArguments[0].Value);
    }

    [Fact]
    public void TryCreateRequest_FullPublicKey_DerivesToken()
    {
        byte[] publicKey =
        [
            0x00, 0x24, 0x00, 0x00, 0x04, 0x80, 0x00, 0x00,
            0x94, 0x00, 0x00, 0x00, 0x06, 0x02, 0x00, 0x00,
            0x00, 0x24, 0x00, 0x00, 0x52, 0x53, 0x41, 0x31,
            0x00, 0x04, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
        ];
        ResolvedAssemblyReference requesting = Descriptor(
            BuildCrossAssemblyInt64NamedEnumImage());

        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                $"Samples.E, Other, PublicKey={Convert.ToHexString(publicKey)}",
                requesting,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));

        var start = Assert.IsType<TypeResolutionStart.Reference>(request.Start);
        Assert.Equal(
            AssemblyReferenceIdentity.ComputePublicKeyToken(publicKey),
            start.Value.PublicKeyToken);
    }

    [Fact]
    public void TryCreateRequest_InvalidPublicKeyToken_IsRejected()
    {
        ResolvedAssemblyReference requesting = Descriptor(
            BuildCrossAssemblyInt64NamedEnumImage());

        Assert.False(
            TypeResolutionEnumWidth.TryCreateRequest(
                "Samples.E, Other, PublicKeyToken=0011",
                requesting,
                AssemblyResolutionScope.Any,
                out _));
    }

    [Fact]
    public void TryCreateRequest_ExplicitNullPublicKeyToken_IsPlanned()
    {
        ResolvedAssemblyReference requesting = Descriptor(
            BuildCrossAssemblyInt64NamedEnumImage());

        // `PublicKeyToken=null` names an unsigned assembly. It stays a planned
        // request carrying an empty token so the constraint survives to
        // binding; CreateResolver then drops a signed candidate.
        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                "Samples.E, Other, Version=1.0.0.0, Culture=neutral, "
                    + "PublicKeyToken=null",
                requesting,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));

        var start = Assert.IsType<TypeResolutionStart.Reference>(request.Start);
        Assert.Equal("", start.Value.PublicKeyToken);
    }

    [Fact]
    public void ExplicitNullPublicKeyToken_ResolvesUnsignedDefinition()
    {
        // The regression this narrowing must not reintroduce: refusing the
        // qualifier outright left every unsigned cross-assembly enum on the
        // Int32 default, so an Int64 enum decoded four bytes short.
        Assert.Equal(
            PrimitiveTypeCode.Int64,
            ResolveWidthFor(
                "Samples.E, Other, PublicKeyToken=null",
                BuildDefiningInt64EnumImage()));
    }

    [Fact]
    public void ExplicitNullPublicKeyToken_RejectsSignedDefinition()
    {
        // An empty token is a wildcard to MatchesCandidate, so binding can
        // still reach a signed assembly of the same name. The post-resolution
        // narrowing drops it and keeps the Int32 default.
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidthFor(
                "Samples.E, Other, PublicKeyToken=null",
                BuildSignedDefiningInt64EnumImage()));
    }

    [Fact]
    public void TryCreateRequest_ExplicitNeutralCulture_StaysAConstraint()
    {
        ResolvedAssemblyReference requesting = Descriptor(
            BuildCrossAssemblyInt64NamedEnumImage());

        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                "Samples.E, Other, Version=1.0.0.0, Culture=neutral",
                requesting,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));

        var start = Assert.IsType<TypeResolutionStart.Reference>(request.Start);

        // An explicit neutral culture must not bind a culture-specific
        // candidate; an omitted qualifier still may.
        Assert.False(
            start.Value.MatchesCandidate(
                new AssemblyReferenceIdentity(
                    "Other", new Version(1, 0, 0, 0), "fr-FR", null)));
        Assert.True(
            start.Value.MatchesCandidate(
                new AssemblyReferenceIdentity(
                    "Other", new Version(1, 0, 0, 0), null, null)));
    }

    [Fact]
    public void TryCreateRequest_OmittedCulture_RemainsAWildcard()
    {
        ResolvedAssemblyReference requesting = Descriptor(
            BuildCrossAssemblyInt64NamedEnumImage());

        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                "Samples.E, Other, Version=1.0.0.0",
                requesting,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));

        var start = Assert.IsType<TypeResolutionStart.Reference>(request.Start);
        Assert.True(
            start.Value.MatchesCandidate(
                new AssemblyReferenceIdentity(
                    "Other", new Version(1, 0, 0, 0), "fr-FR", null)));
    }

    [Fact]
    public void TryCreateRequest_SimpleName_IsFromAssembly()
    {
        ResolvedAssemblyReference requesting = Descriptor(
            BuildCrossAssemblyInt64NamedEnumImage());
        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                "Samples.E",
                requesting,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));
        Assert.IsType<TypeResolutionStart.Assembly>(request.Start);
        Assert.Equal("Samples.E", request.Type.ToMetadataFullName());
    }

    [Fact]
    public void PlannedQualifiedName_DecodesInt64FromRetainedDefiningImage()
    {
        using Harness harness = Harness.Create();
        CustomAttribute attribute = FirstAttribute(harness.UserReader);
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(
                harness.Context,
                [harness.Request]);

        var decoded = AttributeDecoder.TryDecode(
            harness.UserReader,
            attribute,
            beforeMaterialize: null,
            resolver);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded.Value.NamedArguments.Length);
        Assert.Equal("Kind", decoded.Value.NamedArguments[0].Name);
        Assert.Equal(7L, decoded.Value.NamedArguments[0].Value);
        Assert.Equal("Name", decoded.Value.NamedArguments[1].Name);
        Assert.Equal("ok", decoded.Value.NamedArguments[1].Value);
    }

    [Fact]
    public void UnplannedRequest_StaysInt32()
    {
        using Harness harness = Harness.Create(planRequest: false);
        CustomAttribute attribute = FirstAttribute(harness.UserReader);
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(
                harness.Context,
                [harness.Request]);

        Assert.Equal(PrimitiveTypeCode.Int32, resolver("Samples.E"));
        Assert.Null(
            AttributeDecoder.TryDecode(
                harness.UserReader,
                attribute,
                beforeMaterialize: null,
                resolver));
    }

    [Fact]
    public void MissingDefiningImage_StaysInt32()
    {
        using Harness harness = Harness.Create(bindDefining: false);
        CustomAttribute attribute = FirstAttribute(harness.UserReader);
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(
                harness.Context,
                [harness.Request]);

        Assert.Equal(PrimitiveTypeCode.Int32, resolver("Samples.E"));
        Assert.Null(
            AttributeDecoder.TryDecode(
                harness.UserReader,
                attribute,
                beforeMaterialize: null,
                resolver));
    }

    [Fact]
    public void DistinctRequestsWithSameCallbackName_StayInt32InEitherOrder()
    {
        byte[] userImage = BuildCrossAssemblyInt64NamedEnumImage();
        ResolvedAssemblyReference user = Descriptor(userImage);
        ResolvedAssemblyReference first = Descriptor(
            BuildDefiningTypeImage(
                "First",
                PrimitiveTypeCode.Byte,
                DefinitionShape.Enum));
        ResolvedAssemblyReference second = Descriptor(
            BuildDefiningTypeImage(
                "Second",
                PrimitiveTypeCode.Int64,
                DefinitionShape.Enum));
        TypeResolutionRequest firstRequest =
            Request("Samples.E, First", user);
        TypeResolutionRequest secondRequest =
            Request("Samples.E, Second", user);
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                current => current.Target
                        is AssemblyBindingTarget.AssemblyReference reference
                    ? reference.Identity.Name switch
                    {
                        "First" => AssemblyBindingSelection.Found(first),
                        "Second" => AssemblyBindingSelection.Found(second),
                        _ => AssemblyBindingSelection.NotFound(),
                    }
                    : AssemblyBindingSelection.NotFound()),
            [user],
            [firstRequest, secondRequest]);

        Func<string, PrimitiveTypeCode> forward =
            TypeResolutionEnumWidth.CreateResolver(
                context,
                [firstRequest, secondRequest]);
        Func<string, PrimitiveTypeCode> reverse =
            TypeResolutionEnumWidth.CreateResolver(
                context,
                [secondRequest, firstRequest]);

        Assert.Equal(PrimitiveTypeCode.Int32, forward("Samples.E"));
        Assert.Equal(PrimitiveTypeCode.Int32, reverse("Samples.E"));
    }

    [Fact]
    public void NonEnumDefinition_StaysInt32()
    {
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidth(
                BuildDefiningTypeImage(
                    "Other",
                    PrimitiveTypeCode.Int64,
                    DefinitionShape.NonEnum)));
    }

    [Fact]
    public void MalformedEnumDefinition_StaysInt32()
    {
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidth(
                BuildDefiningTypeImage(
                    "Other",
                    PrimitiveTypeCode.Int64,
                    DefinitionShape.MalformedEnum)));
    }

    [Fact]
    public void NonCoreSystemEnumBase_StaysInt32()
    {
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidth(
                BuildDefiningTypeImage(
                    "Other",
                    PrimitiveTypeCode.Int64,
                    DefinitionShape.Enum,
                    baseAssemblyName: "FakeCore")));
    }

    [Fact]
    public void SameModuleSystemEnumBase_StaysInt32()
    {
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidth(BuildSameModuleSystemEnumSpoofImage()));
    }

    [Fact]
    public void FacadeForwarder_DecodesInt64()
    {
        byte[] definingImage = BuildDefiningInt64EnumImage();
        byte[] facadeImage = BuildFacadeForwardingSamplesE(
            ReadIdentity(definingImage));
        byte[] userImage = BuildCrossAssemblyInt64NamedEnumImage();
        ResolvedAssemblyReference defining = Descriptor(definingImage);
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                "Samples.E",
                facade,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                current => current.Target
                        is AssemblyBindingTarget.AssemblyReference reference
                    && reference.Identity.Name == "Other"
                        ? AssemblyBindingSelection.Found(defining)
                        : AssemblyBindingSelection.NotFound()),
            [facade],
            [request]);
        using var userPe = new PEReader(
            new MemoryStream(userImage, writable: false));
        MetadataReader userReader = userPe.GetMetadataReader();
        CustomAttribute attribute = FirstAttribute(userReader);
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(context, [request]);

        var decoded = AttributeDecoder.TryDecode(
            userReader,
            attribute,
            beforeMaterialize: null,
            resolver);
        Assert.NotNull(decoded);
        Assert.Equal(7L, decoded.Value.NamedArguments[0].Value);
        Assert.Same(defining, Assert.IsType<TypeResolutionOutcome.Resolved>(
            context.Resolve(request)).Definition.Assembly.Assembly);
    }

    [Fact]
    public void HostileLeftoverCount_IsRefused()
    {
        using Harness harness = Harness.Create(hostileCount: 100_000_000);
        CustomAttribute attribute = FirstAttribute(harness.UserReader);
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(
                harness.Context,
                [harness.Request]);
        int charged = 0;

        Assert.Null(
            AttributeDecoder.TryDecode(
                harness.UserReader,
                attribute,
                count => charged = checked(charged + count),
                resolver));
        Assert.InRange(charged, 0, 100_000_000 - 1);
    }

    static CustomAttribute FirstAttribute(MetadataReader reader)
    {
        foreach (var handle in reader.CustomAttributes)
            return reader.GetCustomAttribute(handle);
        throw new InvalidOperationException("The image has no custom attributes.");
    }

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    static ResolvedAssemblyReference Descriptor(byte[] image) =>
        ResolvedAssemblyReference.Create(
            ReadIdentity(image),
            path: null,
            openRead: () => new MemoryStream(image, writable: false),
            provenance: AssemblyResolutionProvenance.Local("test"));

    static TypeResolutionRequest Request(
        string name,
        ResolvedAssemblyReference requesting)
    {
        Assert.True(
            TypeResolutionEnumWidth.TryCreateRequest(
                name,
                requesting,
                AssemblyResolutionScope.Any,
                out TypeResolutionRequest? request));
        return request;
    }

    static PrimitiveTypeCode ResolveWidth(byte[] definingImage)
    {
        byte[] userImage = BuildCrossAssemblyInt64NamedEnumImage();
        ResolvedAssemblyReference defining = Descriptor(definingImage);
        ResolvedAssemblyReference user = Descriptor(userImage);
        TypeResolutionRequest request = Request("Samples.E, Other", user);
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                current => current.Target
                        is AssemblyBindingTarget.AssemblyReference reference
                    && reference.Identity.Name == "Other"
                        ? AssemblyBindingSelection.Found(defining)
                        : AssemblyBindingSelection.NotFound()),
            [user],
            [request]);
        return TypeResolutionEnumWidth.CreateResolver(context, [request])(
            "Samples.E");
    }

    [Fact]
    public void MalformedEnumShapes_DoNotSupplyWidths()
    {
        // Each shape is sealed, extends System.Enum, and carries an Int64
        // `value__`, but none is a CLI-valid enum. A width from any of them
        // would let invalid metadata pick the decode width instead of the
        // Int32 default.
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidth(BuildMalformedEnumImage(MalformedShape.PrivateValueField)));
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidth(BuildMalformedEnumImage(MalformedShape.NonLiteralStaticField)));
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidth(BuildMalformedEnumImage(MalformedShape.GenericParameter)));
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidth(BuildMalformedEnumImage(MalformedShape.LiteralValueField)));
    }

    [Fact]
    public void WellFormedEnumWithLiteralConstant_StillSuppliesWidth()
    {
        // The negative case for the static-field rule: a real enum's named
        // constants are literal static fields and must stay acceptable.
        Assert.Equal(
            PrimitiveTypeCode.Int64,
            ResolveWidth(BuildMalformedEnumImage(MalformedShape.None)));
    }

    enum MalformedShape
    {
        None,
        PrivateValueField,
        NonLiteralStaticField,
        GenericParameter,
        LiteralValueField,
    }

    static byte[] BuildMalformedEnumImage(MalformedShape shape)
    {
        var metadata = CreateMetadata("Other");
        AssemblyReferenceHandle runtime = AddAssemblyReference(
            metadata,
            "System.Runtime");
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));

        AddFieldWithAttributes(
            metadata,
            "value__",
            (shape == MalformedShape.PrivateValueField
                ? FieldAttributes.Private
                : FieldAttributes.Public)
                | (shape == MalformedShape.LiteralValueField
                    ? FieldAttributes.Literal
                    : default)
                | FieldAttributes.SpecialName
                | FieldAttributes.RTSpecialName);
        AddFieldWithAttributes(
            metadata,
            "Named",
            FieldAttributes.Public
                | FieldAttributes.Static
                | (shape == MalformedShape.NonLiteralStaticField
                    ? default
                    : FieldAttributes.Literal));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (shape == MalformedShape.GenericParameter)
        {
            metadata.AddGenericParameter(
                type,
                default,
                metadata.GetOrAddString("T"),
                0);
        }
        return Serialize(metadata);
    }

    static void AddFieldWithAttributes(
        MetadataBuilder metadata,
        string name,
        FieldAttributes attributes)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature).FieldSignature().Int64();
        metadata.AddFieldDefinition(
            attributes,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature));
    }

    [Fact]
    public void ExplicitNullPublicKeyToken_RejectsSignedFacadeForwardingToUnsigned()
    {
        // The qualifier constrains the assembly the reference binds to, not
        // the terminal definition. A signed facade named `Other` that forwards
        // to an unsigned implementation must not satisfy `PublicKeyToken=null`.
        byte[] targetImage = BuildDefiningTypeImage(
            "Target",
            PrimitiveTypeCode.Int64,
            DefinitionShape.Enum);
        byte[] facadeImage = BuildFacadeForwardingSamplesE(
            ReadIdentity(targetImage),
            assemblyName: "Other",
            publicKey: SamplePublicKey);

        Assert.Equal(
            PrimitiveTypeCode.Int32,
            ResolveWidthThroughFacade(
                "Samples.E, Other, PublicKeyToken=null",
                facadeImage,
                targetImage));
    }

    [Fact]
    public void ExplicitNullPublicKeyToken_AcceptsUnsignedFacadeForwardingToSigned()
    {
        // The mirror case: the bound assembly `Other` is unsigned, so the
        // qualifier is satisfied even though the implementation is signed.
        byte[] targetImage = BuildDefiningTypeImage(
            "Target",
            PrimitiveTypeCode.Int64,
            DefinitionShape.Enum,
            publicKey: SamplePublicKey);
        byte[] facadeImage = BuildFacadeForwardingSamplesE(
            ReadIdentity(targetImage),
            assemblyName: "Other");

        Assert.Equal(
            PrimitiveTypeCode.Int64,
            ResolveWidthThroughFacade(
                "Samples.E, Other, PublicKeyToken=null",
                facadeImage,
                targetImage));
    }

    static readonly byte[] SamplePublicKey =
    [
        0x00, 0x24, 0x00, 0x00, 0x04, 0x80, 0x00, 0x00,
        0x94, 0x00, 0x00, 0x00, 0x06, 0x02, 0x00, 0x00,
        0x00, 0x24, 0x00, 0x00, 0x52, 0x53, 0x41, 0x31,
        0x00, 0x04, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
    ];

    static PrimitiveTypeCode ResolveWidthThroughFacade(
        string requestName,
        byte[] facadeImage,
        byte[] targetImage)
    {
        byte[] userImage = BuildCrossAssemblyInt64NamedEnumImage();
        ResolvedAssemblyReference facade = Descriptor(facadeImage);
        ResolvedAssemblyReference target = Descriptor(targetImage);
        ResolvedAssemblyReference user = Descriptor(userImage);
        TypeResolutionRequest request = Request(requestName, user);
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                current => current.Target
                        is AssemblyBindingTarget.AssemblyReference reference
                    ? reference.Identity.Name == "Other"
                        ? AssemblyBindingSelection.Found(facade)
                        : reference.Identity.Name == "Target"
                            ? AssemblyBindingSelection.Found(target)
                            : AssemblyBindingSelection.NotFound()
                    : AssemblyBindingSelection.NotFound()),
            [user],
            [request]);
        return TypeResolutionEnumWidth.CreateResolver(context, [request])(
            "Samples.E");
    }

    static PrimitiveTypeCode ResolveWidthFor(
        string requestName,
        byte[] definingImage)
    {
        byte[] userImage = BuildCrossAssemblyInt64NamedEnumImage();
        ResolvedAssemblyReference defining = Descriptor(definingImage);
        ResolvedAssemblyReference user = Descriptor(userImage);
        TypeResolutionRequest request = Request(requestName, user);
        using TypeResolutionContext context = TypeResolutionContext.Create(
            new RecordingPolicy(
                current => current.Target
                        is AssemblyBindingTarget.AssemblyReference reference
                    && reference.Identity.Name == "Other"
                        ? AssemblyBindingSelection.Found(defining)
                        : AssemblyBindingSelection.NotFound()),
            [user],
            [request]);
        return TypeResolutionEnumWidth.CreateResolver(context, [request])(
            "Samples.E");
    }

    static byte[] BuildDefiningInt64EnumImage() =>
        BuildDefiningTypeImage(
            "Other",
            PrimitiveTypeCode.Int64,
            DefinitionShape.Enum);

    static byte[] BuildSignedDefiningInt64EnumImage() =>
        BuildDefiningTypeImage(
            "Other",
            PrimitiveTypeCode.Int64,
            DefinitionShape.Enum,
            publicKey:
            [
                0x00, 0x24, 0x00, 0x00, 0x04, 0x80, 0x00, 0x00,
                0x94, 0x00, 0x00, 0x00, 0x06, 0x02, 0x00, 0x00,
                0x00, 0x24, 0x00, 0x00, 0x52, 0x53, 0x41, 0x31,
                0x00, 0x04, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
            ]);

    static byte[] BuildDefiningTypeImage(
        string assemblyName,
        PrimitiveTypeCode underlyingType,
        DefinitionShape shape,
        string typeName = "E",
        string baseAssemblyName = "System.Runtime",
        byte[]? publicKey = null)
    {
        var metadata = CreateMetadata(assemblyName, publicKey);
        AssemblyReferenceHandle runtime = AddAssemblyReference(
            metadata,
            baseAssemblyName);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString(
                shape == DefinitionShape.NonEnum ? "Object" : "Enum"));
        AddPrimitiveField(
            metadata,
            shape == DefinitionShape.MalformedEnum ? "payload" : "value__",
            underlyingType,
            special: shape != DefinitionShape.NonEnum);
        if (shape == DefinitionShape.MalformedEnum)
        {
            AddPrimitiveField(
                metadata,
                "value__",
                underlyingType,
                special: true);
        }
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | (shape == DefinitionShape.NonEnum
                    ? default
                    : TypeAttributes.Sealed),
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString(typeName),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildSameModuleSystemEnumSpoofImage()
    {
        var metadata = CreateMetadata("Other");
        AddPrimitiveField(
            metadata,
            "value__",
            PrimitiveTypeCode.Int64,
            special: true);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle fakeEnum = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            fakeEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildDefiningNestedInt64EnumImage()
    {
        var metadata = CreateMetadata("Other");
        AssemblyReferenceHandle runtime = AddAssemblyReference(
            metadata,
            "System.Runtime");
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
        AddPrimitiveField(
            metadata,
            "value__",
            PrimitiveTypeCode.Int64,
            special: true);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Outer"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle nested = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("E"),
            systemEnum,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(nested, outer);
        return Serialize(metadata);
    }

    static void AddPrimitiveField(
        MetadataBuilder metadata,
        string name,
        PrimitiveTypeCode code,
        bool special)
    {
        var signature = new BlobBuilder();
        SignatureTypeEncoder encoder =
            new BlobEncoder(signature).FieldSignature();
        switch (code)
        {
            case PrimitiveTypeCode.Byte:
                encoder.Byte();
                break;
            case PrimitiveTypeCode.Int64:
                encoder.Int64();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(code));
        }
        metadata.AddFieldDefinition(
            FieldAttributes.Public
                | (special
                    ? FieldAttributes.SpecialName
                        | FieldAttributes.RTSpecialName
                    : default),
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature));
    }

    static AssemblyReferenceHandle AddAssemblyReference(
        MetadataBuilder metadata,
        string name)
    {
        BlobHandle publicKeyToken = name == "System.Runtime"
            ? metadata.GetOrAddBlob(
                Convert.FromHexString("b03f5f7f11d50a3a"))
            : default;
        return metadata.AddAssemblyReference(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            publicKeyToken,
            default,
            default);
    }

    static byte[] BuildCrossAssemblyInt64NamedEnumImage(
        int? elementCount = null,
        string enumName = "Samples.E, Other")
    {
        var metadata = CreateMetadata("User");
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
                0,
                returnType => returnType.Void(),
                _ => { });
        MemberReferenceHandle constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteUInt16(2);
        value.WriteByte(0x53);
        value.WriteByte(0x55);
        value.WriteSerializedString(enumName);
        value.WriteSerializedString("Kind");
        value.WriteInt64(7);
        if (elementCount is int count)
        {
            value.WriteByte(0x53);
            value.WriteByte(0x1d);
            value.WriteByte(0x08);
            value.WriteSerializedString("V");
            value.WriteInt32(count);
        }
        else
        {
            value.WriteByte(0x53);
            value.WriteByte(0x0e);
            value.WriteSerializedString("Name");
            value.WriteSerializedString("ok");
        }

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
            metadata.GetOrAddString("Attributed"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddCustomAttribute(
            type,
            constructor,
            metadata.GetOrAddBlob(value));
        return Serialize(metadata);
    }

    enum DefinitionShape
    {
        Enum,
        NonEnum,
        MalformedEnum,
    }

    static byte[] BuildFacadeForwardingSamplesE(
        AssemblyReferenceIdentity target,
        string assemblyName = "Facade",
        byte[]? publicKey = null)
    {
        var metadata = CreateMetadata(assemblyName, publicKey);
        AssemblyReferenceHandle implementation = metadata.AddAssemblyReference(
            metadata.GetOrAddString(target.Name),
            target.Version ?? new Version(1, 0, 0, 0),
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
        metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("E"),
            implementation,
            typeDefinitionId: 0);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadata(
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
            publicKey is null ? default : AssemblyFlags.PublicKey,
            default);
        return metadata;
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

    sealed class Harness : IDisposable
    {
        readonly PEReader _userPe;
        readonly TypeResolutionContext _context;

        Harness(
            PEReader userPe,
            TypeResolutionContext context,
            TypeResolutionRequest request)
        {
            _userPe = userPe;
            _context = context;
            Request = request;
        }

        public TypeResolutionContext Context => _context;
        public TypeResolutionRequest Request { get; }
        public MetadataReader UserReader => _userPe.GetMetadataReader();

        public static Harness Create(
            bool planRequest = true,
            bool bindDefining = true,
            int? hostileCount = null)
        {
            byte[] definingImage = BuildDefiningInt64EnumImage();
            byte[] userImage = BuildCrossAssemblyInt64NamedEnumImage(
                hostileCount);
            ResolvedAssemblyReference defining = Descriptor(definingImage);
            ResolvedAssemblyReference user = Descriptor(userImage);
            Assert.True(
                TypeResolutionEnumWidth.TryCreateRequest(
                    "Samples.E, Other",
                    user,
                    AssemblyResolutionScope.Any,
                    out TypeResolutionRequest? request));
            TypeResolutionRequest[] planned = planRequest ? [request] : [];
            var context = TypeResolutionContext.Create(
                new RecordingPolicy(
                    current => bindDefining
                        && current.Target
                            is AssemblyBindingTarget.AssemblyReference reference
                        && reference.Identity.Name == "Other"
                            ? AssemblyBindingSelection.Found(defining)
                            : AssemblyBindingSelection.NotFound()),
                [user],
                planned);
            return new(
                new PEReader(new MemoryStream(userImage, writable: false)),
                context,
                request);
        }

        public void Dispose()
        {
            _context.Dispose();
            _userPe.Dispose();
        }
    }

    sealed class RecordingPolicy(
        Func<AssemblyBindingRequest, AssemblyBindingSelection> select)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)

        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                select(request);
        }
    }
}
