using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Extension methods are attached through the receiver's exact local metadata
/// definition name. The structured namespace, nested segments, and generic
/// arity remain distinct, so neither a backticked namespace nor a generic and
/// non-generic sibling can collide through display normalization.
/// </summary>
/// <remarks>
/// A backtick is not a C# identifier character, so this image cannot come from
/// the compiler; the metadata is written directly.
/// </remarks>
public sealed class ExtensionAttachmentNameBoundaryTests
{
    [Fact]
    public void ExtensionMethod_AttachesToTheExtendedType_NotTheNamespaceLookalike()
    {
        using var peReader = new PEReader(ImmutableArray.Create(BuildImage()));
        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        ApiType widget = Assert.Single(
            surface.Types,
            type => type.Namespace == "Ns`1" && type.Name == "Widget");
        ApiType lookalike = Assert.Single(
            surface.Types,
            type => string.IsNullOrEmpty(type.Namespace) && type.Name == "Ns");

        ApiMember[] attached = widget.Members
            .Where(member => member.Kind == "extension-method")
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["Extend", "ExtendByReference"],
            attached.Select(member => member.Name));
        Assert.All(
            attached,
            member => Assert.Equal("Ns`1.Widget", member.ExtendedType));
        Assert.DoesNotContain(
            widget.Members,
            member => member.Name == "ExternalExtend");

        // The fold attached the extension here instead, because `Ns`1.Widget`
        // keyed as `Ns`.
        Assert.DoesNotContain(lookalike.Members, member => member.Kind == "extension-method");
    }

    /// <summary>
    /// The ordinary case the key must keep working: an extension on a generic
    /// type, whose extended-type text spells arguments while the declaration
    /// spells metadata arity.
    /// </summary>
    [Fact]
    public void ExtensionMethod_StillAttachesAcrossTheGenericSpellingDifference()
    {
        using var peReader = new PEReader(ImmutableArray.Create(BuildImage()));
        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        ApiType nonGenericBox = Assert.Single(
            surface.Types,
            type => type.Namespace == "Ns" && type.Name == "Box");
        ApiType box = Assert.Single(
            surface.Types,
            type => type.Namespace == "Ns" && type.Name == "Box`1");

        ApiMember attached = Assert.Single(
            box.Members,
            member => member.Kind == "extension-method");
        Assert.Equal("Unwrap", attached.Name);
        Assert.Equal("Ns.Box<Ns`1.Widget>", attached.ExtendedType);
        Assert.DoesNotContain(
            nonGenericBox.Members,
            member => member.Kind == "extension-method");
    }

    [Fact]
    public void AttachedExtension_PreservesTypedDeclaringTypeAndAnchor()
    {
        using var peReader = new PEReader(ImmutableArray.Create(BuildImage()));
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);

        ApiType extensions = Assert.Single(
            surface.Types,
            type => type.Name == "Extensions.WithDot");
        ApiType widget = Assert.Single(
            surface.Types,
            type => type.Namespace == "Ns`1" && type.Name == "Widget");
        ApiMember original = Assert.Single(
            extensions.Members,
            member => member.Name == "Extend");
        ApiMember attached = Assert.Single(
            widget.Members,
            member => member.Kind == "extension-method"
                && member.Name == "Extend");

        MetadataTypeDefinitionName attachedDeclaringType = Assert.IsType<
            MetadataTypeDefinitionName>(
                attached.DeclaringTypeDefinitionName);
        Assert.Equal(
            extensions.DefinitionName,
            attachedDeclaringType);
        Assert.Null(original.DeclaringTypeDefinitionName);
        Assert.NotNull(attached.DeclaringTypeCanonicalName);
        Assert.Null(original.DeclaringTypeCanonicalName);
        Assert.NotEqual(
            widget.DefinitionName,
            attachedDeclaringType);
        Assert.Equal("", attachedDeclaringType.Namespace);
        Assert.Equal(
            ["Extensions.WithDot"],
            attachedDeclaringType.Segments);
        Assert.Equal(
            ApiMemberIdentity.FormatTypeAnchorName(extensions),
            attached.DeclaringTypeCanonicalName);
        Assert.Equal(
            ApiMemberIdentity.GetMemberAnchor(
                extensions,
                original).CanonicalSignature,
            ApiMemberIdentity.GetMemberAnchor(
                widget,
                attached).CanonicalSignature);
    }

    [Fact]
    public void ExtensionMethod_OnPrimitiveString_AttachesInsideTheCoreLibrary()
    {
        using var stream = File.OpenRead(typeof(string).Assembly.Location);
        using var peReader = new PEReader(stream);

        ApiSurface surface = ApiSurfaceExtractor.ExtractSummary(peReader);

        ApiType stringType = Assert.Single(
            surface.Types,
            type => type.Namespace == "System" && type.Name == "String");
        Assert.Contains(
            stringType.Members,
            member => member.Kind == "extension-method"
                && member.Name == "AsMemory");
    }

    public static TheoryData<PrimitiveTypeCode, string> LocalPrimitiveDefinitions =>
        new()
        {
            { PrimitiveTypeCode.Void, "Void" },
            { PrimitiveTypeCode.Boolean, "Boolean" },
            { PrimitiveTypeCode.Char, "Char" },
            { PrimitiveTypeCode.SByte, "SByte" },
            { PrimitiveTypeCode.Byte, "Byte" },
            { PrimitiveTypeCode.Int16, "Int16" },
            { PrimitiveTypeCode.UInt16, "UInt16" },
            { PrimitiveTypeCode.Int32, "Int32" },
            { PrimitiveTypeCode.UInt32, "UInt32" },
            { PrimitiveTypeCode.Int64, "Int64" },
            { PrimitiveTypeCode.UInt64, "UInt64" },
            { PrimitiveTypeCode.Single, "Single" },
            { PrimitiveTypeCode.Double, "Double" },
            { PrimitiveTypeCode.String, "String" },
            { PrimitiveTypeCode.Object, "Object" },
            { PrimitiveTypeCode.IntPtr, "IntPtr" },
            { PrimitiveTypeCode.UIntPtr, "UIntPtr" },
            { PrimitiveTypeCode.TypedReference, "TypedReference" },
        };

    [Theory]
    [MemberData(nameof(LocalPrimitiveDefinitions))]
    public void LocalPrimitiveReceiverDefinitions_MapToTheirCoreLibraryTypes(
        PrimitiveTypeCode typeCode,
        string expectedName)
    {
        MetadataTypeDefinitionName definition = Assert.IsType<
            MetadataTypeDefinitionName>(
                ApiSurfaceExtractor.GetLocalPrimitiveDefinition(typeCode));

        Assert.Equal("System", definition.Namespace);
        Assert.Equal([expectedName], definition.Segments);
    }

    static byte[] BuildImage()
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("BacktickNamespace.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("BacktickNamespace"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);

        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        TypeReferenceHandle extensionAttribute = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("ExtensionAttribute"));
        TypeReferenceHandle localWidgetReference = metadata.AddTypeReference(
            module,
            metadata.GetOrAddString("Ns`1"),
            metadata.GetOrAddString("Widget"));
        TypeReferenceHandle externalWidgetReference = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("Ns`1"),
            metadata.GetOrAddString("Widget"));

        var attributeCtorSignature = new BlobBuilder();
        new BlobEncoder(attributeCtorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, returnType => returnType.Void(), parameters => { });
        MemberReferenceHandle extensionAttributeCtor = metadata.AddMemberReference(
            extensionAttribute,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(attributeCtorSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        // A global type whose name equals the backticked namespace's first
        // component. This is the type the folded key collided with.
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Interface,
            default,
            metadata.GetOrAddString("Ns"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        TypeDefinitionHandle widget = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Interface,
            metadata.GetOrAddString("Ns`1"),
            metadata.GetOrAddString("Widget"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Interface,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Box"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        TypeDefinitionHandle box = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Interface,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Box`1"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            box,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T0"),
            index: 0);

        MethodDefinitionHandle extend = AddExtensionMethod(
            metadata,
            "Extend",
            parameters => parameters.AddParameter().Type().Type(widget, isValueType: false));
        MethodDefinitionHandle extendByReference = AddExtensionMethod(
            metadata,
            "ExtendByReference",
            parameters => parameters.AddParameter().Type().Type(
                localWidgetReference,
                isValueType: false));
        MethodDefinitionHandle externalExtend = AddExtensionMethod(
            metadata,
            "ExternalExtend",
            parameters => parameters.AddParameter().Type().Type(
                externalWidgetReference,
                isValueType: false));
        MethodDefinitionHandle unwrap = AddExtensionMethod(
            metadata,
            "Unwrap",
            parameters => parameters.AddParameter().Type().GenericInstantiation(box, 1, isValueType: false)
                .AddArgument().Type(widget, isValueType: false));

        TypeDefinitionHandle extensions = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("Extensions.WithDot"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: extend);

        // CustomAttribute rows are looked up by a sorted parent index, so they are
        // added in HasCustomAttribute coded-index order: MethodDef rows first.
        var attributeValue = metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        metadata.AddCustomAttribute(extend, extensionAttributeCtor, attributeValue);
        metadata.AddCustomAttribute(extendByReference, extensionAttributeCtor, attributeValue);
        metadata.AddCustomAttribute(externalExtend, extensionAttributeCtor, attributeValue);
        metadata.AddCustomAttribute(unwrap, extensionAttributeCtor, attributeValue);
        metadata.AddCustomAttribute(extensions, extensionAttributeCtor, attributeValue);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static MethodDefinitionHandle AddExtensionMethod(
        MetadataBuilder metadata,
        string name,
        Action<ParametersEncoder> encodeParameter)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(1, returnType => returnType.Void(), encodeParameter);
        return metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));
    }
}
