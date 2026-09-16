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
    public void PropertyAccessorRetainsWhetherItsSignatureCorresponds(
        AccessorMismatch mismatch,
        bool expectedMismatch)
    {
        using var stream = new MemoryStream(BuildImage(mismatch), writable: false);
        using var peReader = new PEReader(stream);

        ApiMember property = Assert.Single(
            Assert.Single(
                ApiSurfaceExtractor.Extract(peReader).Types,
                type => type.Name == "Target").Members,
            member => member.Kind == "property");

        Assert.Null(property.SignatureDecodeStatus);
        Assert.All(
            property.SignatureModel!.Accessors,
            accessor => Assert.Equal(
                !expectedMismatch
                    || accessor.Kind != MismatchedAccessorKind(mismatch),
                accessor.SignatureMatchesProperty));
    }

    static string? MismatchedAccessorKind(AccessorMismatch mismatch) =>
        mismatch switch
        {
            AccessorMismatch.None => null,
            AccessorMismatch.GetterReturn
                or AccessorMismatch.GetterParameter
                or AccessorMismatch.GetterStaticness => "get",
            _ => "set",
        };

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
        byte getterReturn = mismatch == AccessorMismatch.GetterReturn
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
            AccessorAttributes(staticGetter),
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob(
                MethodSignature(
                    isInstance: !staticGetter,
                    getterReturn,
                    getterParameters)),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle setter = metadata.AddMethodDefinition(
            AccessorAttributes(isStatic: false),
            MethodImplAttributes.IL,
            metadata.GetOrAddString("set_Value"),
            metadata.GetOrAddBlob(
                MethodSignature(
                    isInstance: true,
                    setterReturn,
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
            metadata.GetOrAddBlob(new byte[]
            {
                0x28,
                0x00,
                (byte)SignatureTypeCode.String,
            }));
        metadata.AddPropertyMap(target, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Setter,
            setter);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly).Serialize(image);
        return image.ToArray();
    }

    static MethodAttributes AccessorAttributes(bool isStatic) =>
        MethodAttributes.Public
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
        params byte[] parameterTypes) =>
        [
            isInstance ? (byte)0x20 : (byte)0x00,
            checked((byte)parameterTypes.Length),
            returnType,
            .. parameterTypes,
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
    }
}
