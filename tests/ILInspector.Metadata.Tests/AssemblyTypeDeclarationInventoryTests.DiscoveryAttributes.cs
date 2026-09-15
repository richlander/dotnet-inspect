using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed partial class AssemblyTypeDeclarationInventoryTests
{
    const string RequiredMembersObsoleteMessage =
        "Constructors of types with required members are not supported in this version of your compiler.";
    const string RefStructsObsoleteMessage =
        "Types with embedded references are not supported in this version of your compiler.";

    [Fact]
    public void Definitions_ExposeDiscoveryFactsWithoutChangingPublicOrAllViews()
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            AddDefinition(metadata, TypeAttributes.Public, "N", "Ordinary");
            TypeDefinitionHandle hidden =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Hidden");
            AddEditorBrowsable(metadata, hidden, attributes.EditorBrowsable, 1);
            TypeDefinitionHandle deprecated =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Deprecated");
            AddParameterlessAttribute(
                metadata, deprecated, attributes.ObsoleteParameterless);
            TypeDefinitionHandle nonpublic =
                AddDefinition(metadata, TypeAttributes.NotPublic, "N", "Internal");
            AddEditorBrowsable(
                metadata, nonpublic, attributes.EditorBrowsable, 2);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session);

        Assert.Equal(
            [
                Name("N", "Ordinary"),
                Name("N", "Hidden"),
                Name("N", "Deprecated"),
            ],
            inventory.GetDeclarations().Select(declaration => declaration.Name));
        Assert.Equal(4, inventory.GetDeclarations(includeAll: true).Count());
        Assert.All(inventory.Declarations, declaration =>
            Assert.NotNull(declaration.DiscoveryAttributes));
        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(false, false),
            Declaration(inventory, "Ordinary").DiscoveryAttributes);
        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(true, false),
            Declaration(inventory, "Hidden").DiscoveryAttributes);
        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(false, true),
            Declaration(inventory, "Deprecated").DiscoveryAttributes);
        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(false, false),
            Declaration(inventory, "Internal").DiscoveryAttributes);
    }

    [Fact]
    public void NestedDefinitions_UseOnlyLocallyDeclaredAttributes()
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            TypeDefinitionHandle outer =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Outer");
            AddEditorBrowsable(metadata, outer, attributes.EditorBrowsable, 1);
            AddParameterlessAttribute(
                metadata, outer, attributes.ObsoleteParameterless);
            TypeDefinitionHandle inherited =
                AddDefinition(metadata, TypeAttributes.NestedPublic, "", "Inherited");
            TypeDefinitionHandle local =
                AddDefinition(metadata, TypeAttributes.NestedPrivate, "", "Local");
            AddParameterlessAttribute(
                metadata, local, attributes.ObsoleteParameterless);
            metadata.AddNestedType(inherited, outer);
            metadata.AddNestedType(local, outer);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session);

        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(true, true),
            Declaration(inventory, "Outer").DiscoveryAttributes);
        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(false, false),
            Declaration(inventory, "Inherited").DiscoveryAttributes);
        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(false, true),
            Declaration(inventory, "Local").DiscoveryAttributes);
    }

    [Fact]
    public void EditorBrowsable_OnlyNeverIsHiddenAndRepeatedRowsAreOrCombined()
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            foreach (var (name, value) in new[]
            {
                ("Always", 0),
                ("Never", 1),
                ("Advanced", 2),
                ("Undefined", 42),
            })
            {
                TypeDefinitionHandle type =
                    AddDefinition(metadata, TypeAttributes.Public, "N", name);
                AddEditorBrowsable(
                    metadata, type, attributes.EditorBrowsable, value,
                    includeNamedArgumentCount: false);
            }
            TypeDefinitionHandle repeated =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Repeated");
            AddEditorBrowsable(metadata, repeated, attributes.EditorBrowsable, 0);
            AddEditorBrowsable(metadata, repeated, attributes.EditorBrowsable, 1);
            AddEditorBrowsable(metadata, repeated, attributes.EditorBrowsable, 2);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session);

        Assert.False(Facts(inventory, "Always").IsEditorBrowsableNever);
        Assert.True(Facts(inventory, "Never").IsEditorBrowsableNever);
        Assert.False(Facts(inventory, "Advanced").IsEditorBrowsableNever);
        Assert.False(Facts(inventory, "Undefined").IsEditorBrowsableNever);
        Assert.True(Facts(inventory, "Repeated").IsEditorBrowsableNever);
    }

    [Fact]
    public void Obsolete_ExcludesOnlyRecognizedPairedCompilerCompatibilityMarkers()
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            TypeDefinitionHandle required =
                AddDefinition(metadata, TypeAttributes.Public, "N", "RequiredCompatibility");
            AddStringAttribute(
                metadata, required, attributes.ObsoleteString,
                RequiredMembersObsoleteMessage);
            AddStringAttribute(
                metadata, required, attributes.CompilerFeatureRequired,
                "RequiredMembers");
            TypeDefinitionHandle refStruct =
                AddDefinition(metadata, TypeAttributes.Public, "N", "RefStructCompatibility");
            AddStringAttribute(
                metadata, refStruct, attributes.ObsoleteString,
                RefStructsObsoleteMessage);
            AddStringAttribute(
                metadata, refStruct, attributes.CompilerFeatureRequired,
                "RefStructs");
            TypeDefinitionHandle missingGuard =
                AddDefinition(metadata, TypeAttributes.Public, "N", "MissingGuard");
            AddStringAttribute(
                metadata, missingGuard, attributes.ObsoleteString,
                RequiredMembersObsoleteMessage);
            TypeDefinitionHandle mismatchedGuard =
                AddDefinition(metadata, TypeAttributes.Public, "N", "MismatchedGuard");
            AddStringAttribute(
                metadata, mismatchedGuard, attributes.ObsoleteString,
                RequiredMembersObsoleteMessage);
            AddStringAttribute(
                metadata, mismatchedGuard, attributes.CompilerFeatureRequired,
                "RefStructs");
            TypeDefinitionHandle genuine =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Genuine");
            AddParameterlessAttribute(
                metadata, genuine, attributes.ObsoleteParameterless);
            TypeDefinitionHandle malformed =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Malformed");
            AddAttributeBlob(
                metadata, malformed, attributes.ObsoleteParameterless, []);
            TypeDefinitionHandle repeated =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Repeated");
            AddStringAttribute(
                metadata, repeated, attributes.ObsoleteString,
                RequiredMembersObsoleteMessage);
            AddStringAttribute(
                metadata, repeated, attributes.CompilerFeatureRequired,
                "RequiredMembers");
            AddParameterlessAttribute(
                metadata, repeated, attributes.ObsoleteParameterless);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session);

        Assert.False(Facts(inventory, "RequiredCompatibility").IsObsolete);
        Assert.False(Facts(inventory, "RefStructCompatibility").IsObsolete);
        Assert.True(Facts(inventory, "MissingGuard").IsObsolete);
        Assert.True(Facts(inventory, "MismatchedGuard").IsObsolete);
        Assert.True(Facts(inventory, "Genuine").IsObsolete);
        Assert.True(Facts(inventory, "Malformed").IsObsolete);
        Assert.True(Facts(inventory, "Repeated").IsObsolete);
    }

    [Theory]
    [InlineData("InvalidProlog")]
    [InlineData("MissingEnum")]
    public void MalformedEditorBrowsablePrefix_RejectsBothInventoryEntryPoints(
        string scenario)
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            TypeDefinitionHandle type =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Malformed");
            byte[] value = scenario switch
            {
                "InvalidProlog" => [0, 0, 1, 0, 0, 0],
                "MissingEnum" => [1, 0, 1, 0, 0],
                _ => throw new InvalidOperationException(scenario),
            };
            AddAttributeBlob(metadata, type, attributes.EditorBrowsable, value);
        });

        AssertInvalidOnBothInventoryEntryPoints(image);
    }

    [Fact]
    public void EarlierNeverDoesNotHideLaterMalformedEditorBrowsable()
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            TypeDefinitionHandle type =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Malformed");
            AddEditorBrowsable(metadata, type, attributes.EditorBrowsable, 1);
            AddAttributeBlob(
                metadata, type, attributes.EditorBrowsable,
                [1, 0, 1, 0, 0]);
        });

        AssertInvalidOnBothInventoryEntryPoints(image);
    }

    [Fact]
    public void UnknownMalformedAttributeValue_IsNotDecoded()
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            TypeDefinitionHandle type =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Unknown");
            AddAttributeBlob(metadata, type, attributes.Unknown, []);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));

        Assert.Equal(
            new TypeDeclarationDiscoveryAttributes(false, false),
            Assert.Single(Read(session).Declarations).DiscoveryAttributes);
    }

    [Fact]
    public void LegacyEditorBrowsableHelper_RemainsPermissiveForInvalidProlog()
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            TypeDefinitionHandle type =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Legacy");
            AddAttributeBlob(
                metadata, type, attributes.EditorBrowsable,
                [0, 0, 1, 0, 0, 0]);
        });
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type = reader.TypeDefinitions.Skip(1).Single();

        Assert.True(AttributeReader.HasEditorBrowsableNeverAttribute(
            reader, reader.GetTypeDefinition(type).GetCustomAttributes()));
    }

    static AssemblyTypeDeclaration Declaration(
        AssemblyTypeDeclarationInventory inventory,
        string name) =>
        Assert.Single(inventory.Declarations,
            declaration => declaration.Name.Segments[^1] == name);

    static TypeDeclarationDiscoveryAttributes Facts(
        AssemblyTypeDeclarationInventory inventory,
        string name) =>
        Assert.IsType<TypeDeclarationDiscoveryAttributes>(
            Declaration(inventory, name).DiscoveryAttributes);

    static void AssertInvalidOnBothInventoryEntryPoints(byte[] image)
    {
        var descriptor = Descriptor(image);
        using var session = AssemblyInspectionSession.Open(descriptor);
        foreach (AssemblyTypeDeclarationInventoryOutcome outcome in new[]
        {
            session.TypeDeclarations(),
            AssemblyTypeDeclarationInventoryReader.Read(descriptor),
        })
        {
            var rejected =
                Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                    outcome);
            Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
            Assert.NotEmpty(rejected.Failure.Detail);
        }
    }

    static void AssertCompilerCompatibilityAttributes(
        byte[] image,
        string @namespace,
        string name)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinition definition = reader.GetTypeDefinition(
            Assert.Single(reader.TypeDefinitions, handle =>
            {
                TypeDefinition candidate = reader.GetTypeDefinition(handle);
                return reader.GetString(candidate.Namespace) == @namespace
                    && reader.GetString(candidate.Name) == name;
            }));
        CustomAttributeHandleCollection attributes =
            definition.GetCustomAttributes();
        CustomAttribute obsolete = Assert.Single(
            attributes.Select(reader.GetCustomAttribute),
            attribute => AttributeReader.GetAttributeTypeName(
                reader, attribute.Constructor) == "System.ObsoleteAttribute");
        CustomAttribute compilerFeatureRequired = Assert.Single(
            attributes.Select(reader.GetCustomAttribute),
            attribute => AttributeReader.GetAttributeTypeName(
                reader, attribute.Constructor)
                == "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute");

        Assert.Equal(
            RefStructsObsoleteMessage,
            AttributeReader.TryGetAttributeDisplayValue(reader, obsolete));
        Assert.Equal(
            "RefStructs",
            AttributeReader.TryGetAttributeDisplayValue(
                reader, compilerFeatureRequired));
        Assert.False(AttributeReader.TryGetObsoleteAttribute(
            reader, attributes, out string? message));
        Assert.Null(message);
    }

    static DiscoveryAttributeConstructors AddDiscoveryAttributeConstructors(
        MetadataBuilder metadata)
    {
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(10, 0, 0, 0),
            default,
            default,
            0,
            default);
        TypeReferenceHandle editorBrowsable = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.ComponentModel"),
            metadata.GetOrAddString("EditorBrowsableAttribute"));
        TypeReferenceHandle editorBrowsableState = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.ComponentModel"),
            metadata.GetOrAddString("EditorBrowsableState"));
        TypeReferenceHandle obsolete = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ObsoleteAttribute"));
        TypeReferenceHandle compilerFeatureRequired = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CompilerFeatureRequiredAttribute"));
        TypeReferenceHandle unknown = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("UnknownAttribute"));

        var noArguments = new BlobBuilder();
        new BlobEncoder(noArguments).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        var int32Argument = new BlobBuilder();
        new BlobEncoder(int32Argument).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Type(
                    editorBrowsableState, isValueType: true));
        var stringArgument = new BlobBuilder();
        new BlobEncoder(stringArgument).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().String());

        return new(
            metadata.AddMemberReference(
                editorBrowsable,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(int32Argument)),
            metadata.AddMemberReference(
                obsolete,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(noArguments)),
            metadata.AddMemberReference(
                obsolete,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(stringArgument)),
            metadata.AddMemberReference(
                compilerFeatureRequired,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(stringArgument)),
            metadata.AddMemberReference(
                unknown,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(noArguments)));
    }

    static void AddEditorBrowsable(
        MetadataBuilder metadata,
        EntityHandle parent,
        EntityHandle constructor,
        int value,
        bool includeNamedArgumentCount = true)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteInt32(value);
        if (includeNamedArgumentCount)
            blob.WriteUInt16(0);
        metadata.AddCustomAttribute(
            parent, constructor, metadata.GetOrAddBlob(blob));
    }

    static void AddParameterlessAttribute(
        MetadataBuilder metadata,
        EntityHandle parent,
        EntityHandle constructor)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteUInt16(0);
        metadata.AddCustomAttribute(
            parent, constructor, metadata.GetOrAddBlob(blob));
    }

    static void AddStringAttribute(
        MetadataBuilder metadata,
        EntityHandle parent,
        EntityHandle constructor,
        string value)
    {
        var blob = new BlobBuilder();
        blob.WriteUInt16(1);
        blob.WriteSerializedString(value);
        blob.WriteUInt16(0);
        metadata.AddCustomAttribute(
            parent, constructor, metadata.GetOrAddBlob(blob));
    }

    static void AddAttributeBlob(
        MetadataBuilder metadata,
        EntityHandle parent,
        EntityHandle constructor,
        byte[] value) =>
        metadata.AddCustomAttribute(
            parent, constructor, metadata.GetOrAddBlob(value));

    readonly record struct DiscoveryAttributeConstructors(
        MemberReferenceHandle EditorBrowsable,
        MemberReferenceHandle ObsoleteParameterless,
        MemberReferenceHandle ObsoleteString,
        MemberReferenceHandle CompilerFeatureRequired,
        MemberReferenceHandle Unknown);
}
