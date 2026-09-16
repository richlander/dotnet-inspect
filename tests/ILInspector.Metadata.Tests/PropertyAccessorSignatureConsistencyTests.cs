using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public class PropertyAccessorSignatureConsistencyTests
{
    [Theory]
    [InlineData(AccessorMismatch.None, false)]
    [InlineData(AccessorMismatch.GetterReturn, true)]
    [InlineData(AccessorMismatch.GetterParameter, true)]
    [InlineData(AccessorMismatch.SetterReturn, true)]
    [InlineData(AccessorMismatch.SetterValue, true)]
    [InlineData(AccessorMismatch.SetterIndexParameter, true)]
    [InlineData(AccessorMismatch.GetterStaticness, true)]
    [InlineData(AccessorMismatch.GetterGenericHeader, true)]
    [InlineData(AccessorMismatch.PropertyGenericHeader, true)]
    [InlineData(AccessorMismatch.VoidProperty, false)]
    [InlineData(AccessorMismatch.IncomparableAccessibility, false)]
    public void PropertyAccessorRetainsWhetherItsSignatureCorresponds(
        AccessorMismatch mismatch,
        bool expectedMismatch)
    {
        using var stream = new MemoryStream(BuildImage(mismatch), writable: false);
        using var peReader = new PEReader(stream);

        ApiMember property = Assert.Single(
            Assert.Single(
                ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
                type => type.Name == "Target").Members,
            member => member.Kind == "property");

        Assert.Null(property.SignatureDecodeStatus);
        Assert.All(
            property.SignatureModel!.Accessors,
            accessor => Assert.Equal(
                ExpectedSignatureMatch(mismatch, expectedMismatch, accessor.Kind),
                accessor.SignatureMatchesProperty));
        if (mismatch == AccessorMismatch.VoidProperty)
        {
            Assert.Equal(
                ApiPrimitiveType.Void,
                property.SignatureModel.ReturnTypeShape!.Primitive);
        }
        if (mismatch == AccessorMismatch.IncomparableAccessibility)
        {
            Assert.Equal("protected", property.Accessibility);
            Assert.Equal(
                "internal",
                property.SignatureModel.Accessors
                    .Single(accessor => accessor.Kind == "get")
                    .Accessibility);
        }
    }

    static bool ExpectedSignatureMatch(
        AccessorMismatch mismatch,
        bool expectedMismatch,
        string accessorKind)
    {
        if (!expectedMismatch)
            return true;
        if (mismatch == AccessorMismatch.PropertyGenericHeader)
            return false;

        return accessorKind != (mismatch switch
        {
            AccessorMismatch.GetterReturn
                or AccessorMismatch.GetterParameter
                or AccessorMismatch.GetterStaticness
                or AccessorMismatch.GetterGenericHeader => "get",
            _ => "set",
        });
    }

    static byte[] BuildImage(AccessorMismatch mismatch)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("PropertyAccessors.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("PropertyAccessors"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        bool staticGetter = mismatch == AccessorMismatch.GetterStaticness;
        bool genericGetter = mismatch == AccessorMismatch.GetterGenericHeader;
        bool voidProperty = mismatch == AccessorMismatch.VoidProperty;
        byte getterReturn = voidProperty
            ? (byte)SignatureTypeCode.Void
            : mismatch == AccessorMismatch.GetterReturn
            ? (byte)SignatureTypeCode.Int32
            : (byte)SignatureTypeCode.String;
        byte[] getterParameters = mismatch == AccessorMismatch.GetterParameter
            ? [(byte)SignatureTypeCode.Int32]
            : [];
        byte setterReturn = mismatch == AccessorMismatch.SetterReturn
            ? (byte)SignatureTypeCode.Int32
            : (byte)SignatureTypeCode.Void;
        byte setterValue = mismatch == AccessorMismatch.SetterValue
            ? (byte)SignatureTypeCode.Int32
            : (byte)SignatureTypeCode.String;
        byte[] setterParameters = mismatch == AccessorMismatch.SetterIndexParameter
            ? [(byte)SignatureTypeCode.Int32, setterValue]
            : [setterValue];

        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            AccessorAttributes(
                staticGetter,
                mismatch == AccessorMismatch.IncomparableAccessibility
                    ? MethodAttributes.Assembly
                    : MethodAttributes.Public),
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob(
                MethodSignature(
                    isInstance: !staticGetter,
                    getterReturn,
                    isGeneric: genericGetter,
                    getterParameters)),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle setter = metadata.AddMethodDefinition(
            AccessorAttributes(
                isStatic: false,
                mismatch == AccessorMismatch.IncomparableAccessibility
                    ? MethodAttributes.Family
                    : MethodAttributes.Public),
            MethodImplAttributes.IL,
            metadata.GetOrAddString("set_Value"),
            metadata.GetOrAddBlob(
                MethodSignature(
                    isInstance: true,
                    setterReturn,
                    isGeneric: false,
                    setterParameters)),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);

        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(
                PropertySignature(
                    isGeneric:
                        mismatch == AccessorMismatch.PropertyGenericHeader,
                    voidProperty
                        ? (byte)SignatureTypeCode.Void
                        : (byte)SignatureTypeCode.String)));
        metadata.AddPropertyMap(target, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        if (!voidProperty)
        {
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Setter,
                setter);
        }

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly).Serialize(image);
        return image.ToArray();
    }

    static MethodAttributes AccessorAttributes(
        bool isStatic,
        MethodAttributes accessibility) =>
        accessibility
        | MethodAttributes.Abstract
        | MethodAttributes.Virtual
        | MethodAttributes.HideBySig
        | MethodAttributes.SpecialName
        | (isStatic
            ? MethodAttributes.Static
            : MethodAttributes.PrivateScope);

    static byte[] MethodSignature(
        bool isInstance,
        byte returnType,
        bool isGeneric,
        params byte[] parameterTypes) =>
        isGeneric
            ?
            [
                (byte)((isInstance ? 0x20 : 0x00) | 0x10),
                0x00,
                checked((byte)parameterTypes.Length),
                returnType,
                .. parameterTypes,
            ]
            :
            [
                isInstance ? (byte)0x20 : (byte)0x00,
                checked((byte)parameterTypes.Length),
                returnType,
                .. parameterTypes,
            ];

    static byte[] PropertySignature(bool isGeneric, byte returnType) =>
        isGeneric
            ?
            [
                0x38,
                0x00,
                0x00,
                returnType,
            ]
            :
            [
                0x28,
                0x00,
                returnType,
            ];

    public enum AccessorMismatch
    {
        None,
        GetterReturn,
        GetterParameter,
        SetterReturn,
        SetterValue,
        SetterIndexParameter,
        GetterStaticness,
        GetterGenericHeader,
        PropertyGenericHeader,
        VoidProperty,
        IncomparableAccessibility,
    }
}
