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
    [InlineData(AccessorMismatch.StaticAttributeInstanceSignature, true)]
    [InlineData(AccessorMismatch.InstanceAttributeStaticSignature, true)]
    [InlineData(AccessorMismatch.DivergentDeclarationModifiers, false)]
    [InlineData(AccessorMismatch.FinalVirtualNewSlot, false)]
    [InlineData(AccessorMismatch.GetterGenericHeader, true)]
    [InlineData(AccessorMismatch.PropertyGenericHeader, true)]
    [InlineData(AccessorMismatch.GetterReservedHeader, true)]
    [InlineData(AccessorMismatch.PropertyReservedHeader, true)]
    [InlineData(AccessorMismatch.VoidProperty, false)]
    [InlineData(AccessorMismatch.IncomparableAccessibility, false)]
    [InlineData(AccessorMismatch.PrivateScopeAccessibility, false)]
    [InlineData(AccessorMismatch.ExplicitInterfaceGetter, false)]
    [InlineData(AccessorMismatch.NamedClassValueType, true)]
    [InlineData(AccessorMismatch.GenericClassValueType, true)]
    [InlineData(AccessorMismatch.NativeInt, false)]
    [InlineData(AccessorMismatch.NativeUInt, false)]
    [InlineData(AccessorMismatch.MethodGenericParameter, true)]
    [InlineData(AccessorMismatch.ArrayMethodGenericParameter, true)]
    [InlineData(AccessorMismatch.GenericArgumentMethodGenericParameter, true)]
    [InlineData(AccessorMismatch.TypeGenericParameter, false)]
    [InlineData(AccessorMismatch.OutOfRangeTypeGenericParameter, true)]
    [InlineData(AccessorMismatch.ArrayOutOfRangeTypeGenericParameter, true)]
    [InlineData(AccessorMismatch.GenericArgumentOutOfRangeTypeGenericParameter, true)]
    [InlineData(AccessorMismatch.GenericArityAbsent, true)]
    [InlineData(AccessorMismatch.GenericArityMismatch, true)]
    [InlineData(AccessorMismatch.GenericArityTrustedNoSuffix, false)]
    [InlineData(AccessorMismatch.NamedSameModuleGenericDefinition, true)]
    [InlineData(AccessorMismatch.NamedSameModuleNonGenericDefinition, false)]
    [InlineData(AccessorMismatch.NamedSameModuleMissingDefinition, true)]
    [InlineData(AccessorMismatch.NamedSameModuleAmbiguousDefinition, true)]
    public void PropertyAccessorRetainsWhetherItsSignatureCorresponds(
        AccessorMismatch mismatch,
        bool expectedMismatch)
    {
        using var stream = new MemoryStream(BuildImage(mismatch), writable: false);
        using var peReader = new PEReader(stream);

        ApiMember property = Assert.Single(
            Assert.Single(
                ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
                type => type.Namespace == "Samples").Members,
            member => member.Kind == "property");

        Assert.Null(property.SignatureDecodeStatus);
        Assert.All(
            property.SignatureModel!.Accessors,
            accessor => Assert.Equal(
                ExpectedSignatureMatch(mismatch, expectedMismatch, accessor.Kind),
                accessor.SignatureMatchesProperty));
        Assert.All(
            property.SignatureModel.Accessors,
            accessor => Assert.Equal(
                mismatch != AccessorMismatch.PrivateScopeAccessibility
                    || accessor.Kind != "get",
                accessor.AccessibilityIsRepresentable));
        Assert.All(
            property.SignatureModel.Accessors,
            accessor => Assert.Equal(
                mismatch is not (
                    AccessorMismatch.GetterStaticness
                    or AccessorMismatch.DivergentDeclarationModifiers),
                accessor.DeclarationModifiersMatchProperty));
        Assert.All(
            property.SignatureModel.Accessors,
            accessor => Assert.Equal(
                mismatch != AccessorMismatch.FinalVirtualNewSlot,
                accessor.DeclarationModifiersAreRepresentable));
        Assert.All(
            property.SignatureModel.Accessors,
            accessor => Assert.Equal(
                mismatch == AccessorMismatch.ExplicitInterfaceGetter
                    && accessor.Kind == "get",
                accessor.IsExplicitInterfaceImplementation));
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
        if (mismatch == AccessorMismatch.PrivateScopeAccessibility)
        {
            Assert.Null(
                property.SignatureModel.Accessors
                    .Single(accessor => accessor.Kind == "get")
                    .Accessibility);
        }
        if (mismatch is AccessorMismatch.NativeInt or AccessorMismatch.NativeUInt)
        {
            Assert.Equal(
                mismatch == AccessorMismatch.NativeInt
                    ? ApiPrimitiveType.IntPtr
                    : ApiPrimitiveType.UIntPtr,
                property.SignatureModel.ReturnTypeShape!.Primitive);
        }
        if (mismatch is
            AccessorMismatch.NamedClassValueType
            or AccessorMismatch.GenericClassValueType)
        {
            Assert.False(property.SignatureModel.ReturnTypeShape!.IsValueType);
        }
        if (mismatch is
            AccessorMismatch.MethodGenericParameter
            or AccessorMismatch.TypeGenericParameter)
        {
            Assert.Equal(
                mismatch == AccessorMismatch.MethodGenericParameter,
                property.SignatureModel.ReturnTypeShape!
                    .IsMethodGenericParameter);
        }
        if (mismatch == AccessorMismatch.ArrayMethodGenericParameter)
        {
            Assert.True(
                property.SignatureModel.ReturnTypeShape!.ElementType!
                    .IsMethodGenericParameter);
        }
        if (mismatch == AccessorMismatch.GenericArgumentMethodGenericParameter)
        {
            Assert.True(
                Assert.Single(
                    property.SignatureModel.ReturnTypeShape!.TypeArguments)
                    .IsMethodGenericParameter);
        }
        if (mismatch is
            AccessorMismatch.GenericArityAbsent
            or AccessorMismatch.GenericArityMismatch
            or AccessorMismatch.NamedSameModuleGenericDefinition
            or AccessorMismatch.NamedSameModuleMissingDefinition
            or AccessorMismatch.NamedSameModuleAmbiguousDefinition)
        {
            Assert.Null(property.SignatureModel.ReturnTypeShape);
        }
        if (mismatch == AccessorMismatch.GenericArityTrustedNoSuffix)
        {
            Assert.True(
                property.SignatureModel.ReturnTypeShape!
                    .DefinitionArityMatchesTypeArguments);
        }
    }

    static bool ExpectedSignatureMatch(
        AccessorMismatch mismatch,
        bool expectedMismatch,
        string accessorKind)
    {
        if (!expectedMismatch)
            return true;
        if (mismatch is
            AccessorMismatch.PropertyGenericHeader
            or AccessorMismatch.PropertyReservedHeader)
        {
            return false;
        }

        return accessorKind != (mismatch switch
        {
            AccessorMismatch.GetterReturn
                or AccessorMismatch.GetterParameter
                or AccessorMismatch.GetterStaticness
                or AccessorMismatch.StaticAttributeInstanceSignature
                or AccessorMismatch.InstanceAttributeStaticSignature
                or AccessorMismatch.GetterGenericHeader
                or AccessorMismatch.GetterReservedHeader
                or AccessorMismatch.NamedClassValueType
                or AccessorMismatch.GenericClassValueType
                or AccessorMismatch.MethodGenericParameter
                or AccessorMismatch.ArrayMethodGenericParameter
                or AccessorMismatch.GenericArgumentMethodGenericParameter
                or AccessorMismatch.OutOfRangeTypeGenericParameter
                or AccessorMismatch.ArrayOutOfRangeTypeGenericParameter
                or AccessorMismatch.GenericArgumentOutOfRangeTypeGenericParameter
                or AccessorMismatch.GenericArityAbsent
                or AccessorMismatch.GenericArityMismatch
                or AccessorMismatch.GenericArityTrustedNoSuffix
                or AccessorMismatch.NamedSameModuleGenericDefinition
                or AccessorMismatch.NamedSameModuleMissingDefinition
                or AccessorMismatch.NamedSameModuleAmbiguousDefinition =>
                "get",
            _ => "set",
        });
    }

    static byte[] BuildImage(AccessorMismatch mismatch)
    {
        var metadata = new MetadataBuilder();
        ModuleDefinitionHandle module = metadata.AddModule(
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
        AssemblyReferenceHandle contractAssembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("ContractAssembly"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        bool methodStatic = mismatch is
            AccessorMismatch.GetterStaticness
            or AccessorMismatch.StaticAttributeInstanceSignature;
        bool signatureStatic = mismatch is
            AccessorMismatch.GetterStaticness
            or AccessorMismatch.InstanceAttributeStaticSignature;
        bool propertyStatic =
            mismatch == AccessorMismatch.InstanceAttributeStaticSignature;
        bool genericGetter = mismatch == AccessorMismatch.GetterGenericHeader;
        bool reservedGetter =
            mismatch == AccessorMismatch.GetterReservedHeader;
        bool voidProperty = mismatch == AccessorMismatch.VoidProperty;
        bool encodedReturn = mismatch is
            AccessorMismatch.NamedClassValueType
            or AccessorMismatch.GenericClassValueType
            or AccessorMismatch.NativeInt
            or AccessorMismatch.NativeUInt
            or AccessorMismatch.MethodGenericParameter
            or AccessorMismatch.ArrayMethodGenericParameter
            or AccessorMismatch.GenericArgumentMethodGenericParameter
            or AccessorMismatch.TypeGenericParameter
            or AccessorMismatch.OutOfRangeTypeGenericParameter
            or AccessorMismatch.ArrayOutOfRangeTypeGenericParameter
            or AccessorMismatch.GenericArgumentOutOfRangeTypeGenericParameter
            or AccessorMismatch.GenericArityAbsent
            or AccessorMismatch.GenericArityMismatch
            or AccessorMismatch.GenericArityTrustedNoSuffix
            or AccessorMismatch.NamedSameModuleGenericDefinition
            or AccessorMismatch.NamedSameModuleNonGenericDefinition
            or AccessorMismatch.NamedSameModuleMissingDefinition
            or AccessorMismatch.NamedSameModuleAmbiguousDefinition;
        bool getOnly = voidProperty
            || encodedReturn
            || mismatch is
                AccessorMismatch.StaticAttributeInstanceSignature
                or AccessorMismatch.InstanceAttributeStaticSignature
                or AccessorMismatch.FinalVirtualNewSlot;
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
        bool sameModuleReference = mismatch is
            AccessorMismatch.NamedSameModuleGenericDefinition
            or AccessorMismatch.NamedSameModuleNonGenericDefinition
            or AccessorMismatch.NamedSameModuleMissingDefinition
            or AccessorMismatch.NamedSameModuleAmbiguousDefinition;

        metadata.AddTypeReference(
            sameModuleReference ? module : contractAssembly,
            metadata.GetOrAddString(
                sameModuleReference ? "ReferencedTypes" : "Samples"),
            metadata.GetOrAddString(
                mismatch is
                    AccessorMismatch.GenericClassValueType
                    or AccessorMismatch.GenericArgumentMethodGenericParameter
                    or AccessorMismatch.GenericArgumentOutOfRangeTypeGenericParameter
                    or AccessorMismatch.GenericArityMismatch
                    ? "Referenced`1"
                    : "Referenced"));
        byte[] propertyReturnType = mismatch switch
        {
            AccessorMismatch.NamedClassValueType => [0x12, 0x05],
            AccessorMismatch.GenericClassValueType =>
                [0x15, 0x12, 0x05, 0x01, 0x08],
            AccessorMismatch.NativeInt => [0x18],
            AccessorMismatch.NativeUInt => [0x19],
            AccessorMismatch.MethodGenericParameter => [0x1E, 0x00],
            AccessorMismatch.ArrayMethodGenericParameter =>
                [0x1D, 0x1E, 0x00],
            AccessorMismatch.GenericArgumentMethodGenericParameter =>
                [0x15, 0x12, 0x05, 0x01, 0x1E, 0x00],
            AccessorMismatch.TypeGenericParameter => [0x13, 0x00],
            AccessorMismatch.OutOfRangeTypeGenericParameter =>
                [0x13, 0x00],
            AccessorMismatch.ArrayOutOfRangeTypeGenericParameter =>
                [0x1D, 0x13, 0x00],
            AccessorMismatch.GenericArgumentOutOfRangeTypeGenericParameter =>
                [0x15, 0x12, 0x05, 0x01, 0x13, 0x00],
            AccessorMismatch.GenericArityAbsent
                or AccessorMismatch.GenericArityMismatch =>
                [0x15, 0x12, 0x05, 0x02, 0x08, 0x0E],
            AccessorMismatch.GenericArityTrustedNoSuffix =>
                [0x15, 0x12, 0x0C, 0x02, 0x08, 0x0E],
            AccessorMismatch.NamedSameModuleGenericDefinition
                or AccessorMismatch.NamedSameModuleNonGenericDefinition
                or AccessorMismatch.NamedSameModuleMissingDefinition
                or AccessorMismatch.NamedSameModuleAmbiguousDefinition =>
                [0x12, 0x05],
            _ =>
            [
                voidProperty
                    ? (byte)SignatureTypeCode.Void
                    : (byte)SignatureTypeCode.String,
            ],
        };
        byte[] getterReturnType =
            mismatch switch
            {
                AccessorMismatch.NamedClassValueType => [0x11, 0x05],
                AccessorMismatch.GenericClassValueType =>
                    [0x15, 0x11, 0x05, 0x01, 0x08],
                _ => propertyReturnType,
            };
        BlobHandle getterSignature = metadata.GetOrAddBlob(
            encodedReturn
                ? EncodedMethodSignature(
                    isInstance: !signatureStatic,
                    getterReturnType)
                : MethodSignature(
                    isInstance: !signatureStatic,
                    getterReturn,
                    isGeneric: genericGetter,
                    hasReservedFlag: reservedGetter,
                    getterParameters));
        MethodAttributes getterAccessibility = mismatch switch
        {
            AccessorMismatch.IncomparableAccessibility =>
                MethodAttributes.Assembly,
            AccessorMismatch.PrivateScopeAccessibility =>
                MethodAttributes.PrivateScope,
            AccessorMismatch.ExplicitInterfaceGetter =>
                MethodAttributes.Private,
            _ => MethodAttributes.Public,
        };
        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            mismatch == AccessorMismatch.FinalVirtualNewSlot
                ? getterAccessibility
                    | MethodAttributes.Virtual
                    | MethodAttributes.Final
                    | MethodAttributes.NewSlot
                    | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName
                : AccessorAttributes(
                    methodStatic,
                    getterAccessibility),
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            getterSignature,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle setter = metadata.AddMethodDefinition(
            mismatch == AccessorMismatch.DivergentDeclarationModifiers
                ? MethodAttributes.Public
                    | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName
                : AccessorAttributes(
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
                    hasReservedFlag: false,
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
            metadata.GetOrAddString(
                mismatch == AccessorMismatch.TypeGenericParameter
                    ? "Target`1"
                    : "Target"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);

        if (mismatch == AccessorMismatch.TypeGenericParameter)
        {
            metadata.AddGenericParameter(
                target,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        }
        if (mismatch == AccessorMismatch.GenericArityTrustedNoSuffix)
        {
            TypeDefinitionHandle referenced = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("ReferencedTypes"),
                metadata.GetOrAddString("Referenced"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(3));
            metadata.AddGenericParameter(
                referenced,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TFirst"),
                index: 0);
            metadata.AddGenericParameter(
                referenced,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("TSecond"),
                index: 1);
        }
        if (mismatch is
            AccessorMismatch.NamedSameModuleGenericDefinition
            or AccessorMismatch.NamedSameModuleNonGenericDefinition
            or AccessorMismatch.NamedSameModuleAmbiguousDefinition)
        {
            TypeDefinitionHandle referenced = metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("ReferencedTypes"),
                metadata.GetOrAddString("Referenced"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(3));
            if (mismatch
                == AccessorMismatch.NamedSameModuleGenericDefinition)
            {
                metadata.AddGenericParameter(
                    referenced,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("TFirst"),
                    index: 0);
                metadata.AddGenericParameter(
                    referenced,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("TSecond"),
                    index: 1);
            }
            if (mismatch
                == AccessorMismatch.NamedSameModuleAmbiguousDefinition)
            {
                metadata.AddTypeDefinition(
                    TypeAttributes.NotPublic,
                    metadata.GetOrAddString("ReferencedTypes"),
                    metadata.GetOrAddString("Referenced"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(3));
            }
        }
        if (mismatch == AccessorMismatch.ExplicitInterfaceGetter)
        {
            TypeReferenceHandle contract = metadata.AddTypeReference(
                contractAssembly,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("IContract"));
            MemberReferenceHandle declaration = metadata.AddMemberReference(
                contract,
                metadata.GetOrAddString("get_Value"),
                getterSignature);
            metadata.AddMethodImplementation(target, getter, declaration);
        }

        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(
                encodedReturn
                    ? EncodedPropertySignature(
                        propertyReturnType,
                        isInstance: !propertyStatic)
                    : PropertySignature(
                        isInstance: !propertyStatic,
                        isGeneric:
                            mismatch == AccessorMismatch.PropertyGenericHeader,
                        hasReservedFlag:
                            mismatch == AccessorMismatch.PropertyReservedHeader,
                        voidProperty
                            ? (byte)SignatureTypeCode.Void
                            : (byte)SignatureTypeCode.String)));
        metadata.AddPropertyMap(target, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        if (!getOnly)
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
        bool hasReservedFlag,
        params byte[] parameterTypes) =>
        isGeneric
            ?
            [
                (byte)((isInstance ? 0x20 : 0x00)
                    | 0x10
                    | (hasReservedFlag ? 0x80 : 0x00)),
                0x00,
                checked((byte)parameterTypes.Length),
                returnType,
                .. parameterTypes,
            ]
            :
            [
                (byte)((isInstance ? 0x20 : 0x00)
                    | (hasReservedFlag ? 0x80 : 0x00)),
                checked((byte)parameterTypes.Length),
                returnType,
                .. parameterTypes,
            ];

    static byte[] PropertySignature(
        bool isInstance,
        bool isGeneric,
        bool hasReservedFlag,
        byte returnType) =>
        isGeneric
            ?
            [
                (byte)((isInstance ? 0x28 : 0x08)
                    | 0x10
                    | (hasReservedFlag ? 0x80 : 0x00)),
                0x00,
                0x00,
                returnType,
            ]
            :
            [
                (byte)((isInstance ? 0x28 : 0x08)
                    | (hasReservedFlag ? 0x80 : 0x00)),
                0x00,
                returnType,
            ];

    static byte[] EncodedMethodSignature(
        bool isInstance,
        byte[] returnType) =>
    [
        isInstance ? (byte)0x20 : (byte)0x00,
        0x00,
        .. returnType,
    ];

    static byte[] EncodedPropertySignature(
        byte[] returnType,
        bool isInstance) =>
    [
        isInstance ? (byte)0x28 : (byte)0x08,
        0x00,
        .. returnType,
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
        StaticAttributeInstanceSignature,
        InstanceAttributeStaticSignature,
        DivergentDeclarationModifiers,
        FinalVirtualNewSlot,
        GetterGenericHeader,
        PropertyGenericHeader,
        GetterReservedHeader,
        PropertyReservedHeader,
        VoidProperty,
        IncomparableAccessibility,
        PrivateScopeAccessibility,
        ExplicitInterfaceGetter,
        NamedClassValueType,
        GenericClassValueType,
        NativeInt,
        NativeUInt,
        MethodGenericParameter,
        ArrayMethodGenericParameter,
        GenericArgumentMethodGenericParameter,
        TypeGenericParameter,
        OutOfRangeTypeGenericParameter,
        ArrayOutOfRangeTypeGenericParameter,
        GenericArgumentOutOfRangeTypeGenericParameter,
        GenericArityAbsent,
        GenericArityMismatch,
        GenericArityTrustedNoSuffix,
        NamedSameModuleGenericDefinition,
        NamedSameModuleNonGenericDefinition,
        NamedSameModuleMissingDefinition,
        NamedSameModuleAmbiguousDefinition,
    }
}
