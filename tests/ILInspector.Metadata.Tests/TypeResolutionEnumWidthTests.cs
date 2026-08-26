using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates the TypeResolution adapter that turns already-retained defining
/// images into the optional CA enum-width resolver. Missing, unplanned, or
/// unopened definitions stay Int32 so guard skip and SRM stay aligned.
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

        Assert.True(
            CustomAttributeValueGuard.IsSafeToDecode(
                harness.UserReader,
                attribute,
                beforeMaterialize: null,
                resolver));
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
    public void HostileLeftoverCount_IsUnsafe()
    {
        using Harness harness = Harness.Create(hostileCount: 100_000_000);
        CustomAttribute attribute = FirstAttribute(harness.UserReader);
        Func<string, PrimitiveTypeCode> resolver =
            TypeResolutionEnumWidth.CreateResolver(
                harness.Context,
                [harness.Request]);
        int charged = 0;

        Assert.False(
            CustomAttributeValueGuard.IsSafeToDecode(
                harness.UserReader,
                attribute,
                count => charged = checked(charged + count),
                resolver));
        Assert.True(
            charged >= (2 + 100_000_000)
                * CustomAttributeValueGuard.DeclaredSlotCharge,
            $"Expected the 100M array count to be charged, charged {charged}.");
        Assert.Null(
            AttributeDecoder.TryDecode(
                harness.UserReader,
                attribute,
                beforeMaterialize: null,
                resolver));
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

    static byte[] BuildDefiningInt64EnumImage()
    {
        var metadata = CreateMetadata("Other");
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle systemEnum = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Enum"));
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
        return Serialize(metadata);
    }

    static byte[] BuildCrossAssemblyInt64NamedEnumImage(int? elementCount = null)
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
        value.WriteSerializedString("Samples.E, Other");
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

    static byte[] BuildFacadeForwardingSamplesE(AssemblyReferenceIdentity target)
    {
        var metadata = CreateMetadata("Facade");
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

    static MetadataBuilder CreateMetadata(string assemblyName)
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
            default,
            default,
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

        public AssemblyBindingSelection Select(AssemblyBindingRequest request)
            => select(request);
    }
}
