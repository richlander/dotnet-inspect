using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using CSharpText;

namespace ILInspector.Metadata;

public static partial class ApiSurfaceExtractor
{

    private static (string Text, ApiSignature Model, bool IsDegraded) GetPropertySignature(
        MetadataReader reader,
        GenericContext context,
        PropertyDefinition prop,
        PropertyAccessors accessors,
        byte typeNullableContext,
        string? defaultMemberName,
        IReadOnlySet<MethodDefinitionHandle> explicitImplementationBodies,
        bool includeAll = false,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null,
        Action<int>? beforeAttributeMaterialize = null)
    {
        Action<int>? attributeMaterialize =
            beforeAttributeMaterialize ?? beforeDecodeWork;
        string name = DecodeString(
            reader,
            prop.Name,
            beforeDecodeWork);
        var typeNodeProvider = beforeRetainText is null
            ? TypeNodeProvider.Instance
            : new TypeNodeProvider(beforeRetainText, beforeDecodeWork);
        var treeSignature = GuardedProviderDecode.Property(
            reader,
            prop,
            typeNodeProvider,
            context,
            (TypeNode)new DegradedTypeNode());

        // Apply nullability to the property type
        var propBytes = NullabilityReader.GetNullableBytes(
            reader,
            prop.GetCustomAttributes(),
            beforeDecodeWork);
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(propBytes, ref pos, typeNullableContext);
        var propDynamicFlags = DynamicReader.GetDynamicFlags(
            reader,
            prop.GetCustomAttributes(),
            beforeDecodeWork);
        pos = 0;
        treeSignature.ReturnType.ApplyDynamic(propDynamicFlags, ref pos);
        treeSignature.ReturnType.ApplyTupleNames(
            TupleElementNamesReader.GetTupleElementNames(
                reader,
                prop.GetCustomAttributes(),
                beforeDecodeWork));

        // Determine accessor visibility
        MethodAttributes getterAccess = 0;
        MethodAttributes setterAccess = 0;
        bool hasGetter = !accessors.Getter.IsNil;
        bool hasSetter = !accessors.Setter.IsNil;

        if (hasGetter)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            getterAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
        }

        if (hasSetter)
        {
            var setter = reader.GetMethodDefinition(accessors.Setter);
            setterAccess = setter.Attributes & MethodAttributes.MemberAccessMask;
        }

        bool hasPublicGetter = hasGetter && getterAccess == MethodAttributes.Public;
        bool hasPublicSetter = hasSetter && setterAccess == MethodAttributes.Public;

        // Build accessor string
        string accessorStr;
        var accessorModels = new List<ApiAccessor>();
        if (includeAll)
        {
            // Show explicit access levels for non-public accessors
            var getStr = hasGetter ? FormatAccessor("get", getterAccess, Math.Max((int)getterAccess, (int)setterAccess)) : null;
            var setStr = hasSetter ? FormatAccessor("set", setterAccess, Math.Max((int)getterAccess, (int)setterAccess)) : null;
            if (hasGetter)
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    Accessibility = AccessorAccessibility(getterAccess, Math.Max((int)getterAccess, (int)setterAccess)),
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            if (hasSetter)
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    Accessibility = AccessorAccessibility(setterAccess, Math.Max((int)getterAccess, (int)setterAccess)),
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            accessorStr = (getStr, setStr) switch
            {
                (not null, not null) => $"{{ {getStr}; {setStr}; }}",
                (not null, null) => $"{{ {getStr}; }}",
                (null, not null) => $"{{ {setStr}; }}",
                _ => "{ get; }"
            };
        }
        else
        {
            if (hasPublicGetter && hasPublicSetter)
            {
                accessorStr = "{ get; set; }";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else if (hasPublicGetter && hasSetter)
            {
                string? setterAccessibility =
                    GetAccessibility(setterAccess);
                accessorStr =
                    $"{{ get; {setterAccessibility ?? "private"} set; }}";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    Accessibility = setterAccessibility,
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else if (hasPublicGetter)
            {
                accessorStr = "{ get; }";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else if (hasPublicSetter && hasGetter)
            {
                string? getterAccessibility =
                    GetAccessibility(getterAccess);
                accessorStr =
                    $"{{ {getterAccessibility ?? "private"} get; set; }}";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "get",
                    Accessibility = getterAccessibility,
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Getter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else if (hasPublicSetter)
            {
                accessorStr = "{ set; }";
                accessorModels.Add(new ApiAccessor
                {
                    Kind = "set",
                    ReturnAttributes = ReturnParameterAttributes(
                        reader,
                        reader.GetMethodDefinition(accessors.Setter).GetParameters(),
                        beforeRetainText,
                        attributeMaterialize)
                });
            }
            else
            {
                accessorStr = "{ get; }"; // Fallback
                accessorModels.Add(new ApiAccessor { Kind = "get" });
            }
        }

        var requiredPrefix = AttributeReader.HasRequiredMemberAttribute(
                reader,
                prop.GetCustomAttributes(),
                beforeDecodeWork)
            ? "required "
            : "";
        var isRequired = requiredPrefix.Length > 0;

        MethodDefinitionHandle parameterAccessor = hasGetter
            ? accessors.Getter
            : accessors.Setter;
        var parameterAccessorMethod = parameterAccessor.IsNil
            ? default
            : reader.GetMethodDefinition(parameterAccessor);
        var paramHandles = parameterAccessor.IsNil
            ? default
            : parameterAccessorMethod.GetParameters();
        byte parameterNullableContext = parameterAccessor.IsNil
            ? typeNullableContext
            : NullabilityReader.GetNullableContext(
                    reader,
                    parameterAccessorMethod.GetCustomAttributes(),
                    beforeDecodeWork)
                ?? typeNullableContext;
        (List<string> indexerParameters, List<ApiParameter> parameterModels) =
            ProjectPropertyParameters(
                reader,
                treeSignature.ParameterTypes,
                paramHandles,
                parameterNullableContext,
                beforeRetainText,
                beforeDecodeWork,
                attributeMaterialize);

        var returnType = FormatMethodReturnType(
            reader,
            treeSignature.ReturnType,
            paramHandles,
            beforeDecodeWork);
        var canonicalReturnType = FormatCanonicalMethodReturnType(
            reader,
            treeSignature.ReturnType,
            paramHandles,
            beforeDecodeWork);
        bool returnIsByRef = IsByRefType(treeSignature.ReturnType);
        bool returnIsReadOnlyByRef =
            returnIsByRef
            && IsReadOnlyByRefReturn(
                reader,
                treeSignature.ReturnType,
                paramHandles,
                beforeDecodeWork);
        ApplyAccessorStructuralReturns(
            accessorModels,
            reader,
            name,
            kind => kind switch
            {
                "get" => accessors.Getter,
                "set" => accessors.Setter,
                _ => default,
            },
            typeNodeProvider,
            context,
            explicitImplementationBodies,
            beforeRetainText,
            beforeDecodeWork,
            treeSignature,
            propertyReturnIsByRef: returnIsByRef,
            propertyReturnIsReadOnlyByRef: returnIsReadOnlyByRef);
        IReadOnlyList<string>? xmlDocumentationParameterTypes =
            TryGetXmlDocumentationNames(
                treeSignature.ParameterTypes,
                beforeRetainText);
        bool nameMatchesDefaultMember =
            defaultMemberName is not null
            && string.Equals(
                name,
                defaultMemberName,
                StringComparison.Ordinal);
        bool? isIndexerDeclaration = parameterModels.Count switch
        {
            > 0 when nameMatchesDefaultMember => true,
            > 0 => null,
            0 when nameMatchesDefaultMember => null,
            _ => false,
        };
        var model = new ApiSignature
        {
            XmlDocumentationParameterTypes =
                xmlDocumentationParameterTypes,
            ReturnType = returnType,
            CanonicalReturnType = canonicalReturnType,
            StructuralReturnType = treeSignature.ReturnType.HasStructuralPayload
                ? treeSignature.ReturnType.StructuralIdentity()
                : null,
            ReturnTypeCustomModifiersAreRepresentable =
                PropertyCustomModifiersAreRepresentable(
                    treeSignature.ReturnType,
                    allowReadOnlyByRef: returnIsReadOnlyByRef),
            ReturnTypeReferences =
                [.. treeSignature.ReturnType.ReferencedTypes().Distinct()],
            ReturnTypeDefinitionReference =
                treeSignature.ReturnType.DefinitionReference(),
            ReturnTypeShape = PropertyReturnTypeShape(
                treeSignature.ReturnType,
                returnIsByRef,
                returnIsReadOnlyByRef),
            MemberName = isIndexerDeclaration == true ? "this[]" : name,
            IsIndexerDeclaration = isIndexerDeclaration,
            IsRequired = isRequired,
            Parameters = parameterModels,
            Accessors = accessorModels
        };

        if (isIndexerDeclaration == true)
            return (
                $"{requiredPrefix}{returnType} this[{string.Join(", ", indexerParameters)}] {accessorStr}",
                model,
                treeSignature.ReturnType.IsDegraded
                    || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));

        return (
            $"{requiredPrefix}{returnType} {SanitizeIdentifier(name)} {accessorStr}",
            model,
            treeSignature.ReturnType.IsDegraded
                || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));
    }

    internal static ImmutableArray<string> GetCanonicalPropertyParameterTypes(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        PropertyDefinition property,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        var typeDefinition = reader.GetTypeDefinition(typeHandle);
        var typeNodeProvider = beforeRetainText is null
            ? TypeNodeProvider.Instance
            : new TypeNodeProvider(beforeRetainText, beforeDecodeWork);
        MethodSignature<TypeNode> signature =
            GuardedProviderDecode.Property(
                reader,
                property,
                typeNodeProvider,
                GenericContext.ForType(reader, typeDefinition),
                (TypeNode)new DegradedTypeNode());
        if (signature.ReturnType.IsDegraded
            || signature.ParameterTypes.Any(
                static parameter => parameter.IsDegraded))
        {
            throw new BadImageFormatException(
                "The property signature could not be decoded.");
        }
        PropertyAccessors accessors = property.GetAccessors();
        MethodDefinitionHandle parameterAccessor = !accessors.Getter.IsNil
            ? accessors.Getter
            : accessors.Setter;
        var parameterAccessorMethod = parameterAccessor.IsNil
            ? default
            : reader.GetMethodDefinition(parameterAccessor);
        var parameterHandles = parameterAccessor.IsNil
            ? default
            : parameterAccessorMethod.GetParameters();
        byte typeNullableContext =
            NullabilityReader.GetTypeNullableContext(
                reader,
                typeHandle,
                beforeDecodeWork);
        byte parameterNullableContext = parameterAccessor.IsNil
            ? typeNullableContext
            : NullabilityReader.GetNullableContext(
                    reader,
                    parameterAccessorMethod.GetCustomAttributes(),
                    beforeDecodeWork)
                ?? typeNullableContext;
        (_, List<ApiParameter> parameters) = ProjectPropertyParameters(
            reader,
            signature.ParameterTypes,
            parameterHandles,
            parameterNullableContext,
            beforeRetainText,
            beforeDecodeWork,
            beforeDecodeWork);
        return
        [
            .. parameters.Select(
                static parameter => parameter.CanonicalTypeWithModifier),
        ];
    }

    static (List<string> Display, List<ApiParameter> Models)
        ProjectPropertyParameters(
            MetadataReader reader,
            ImmutableArray<TypeNode> parameterTypes,
            ParameterHandleCollection parameterHandles,
            byte parameterNullableContext,
            Action<string>? beforeRetainText,
            Action<int>? beforeDecodeWork,
            Action<int>? beforeAttributeMaterialize)
    {
        List<string> display = [];
        List<ApiParameter> models = [];
        var parameterInfos = Enumerable.Range(1, parameterTypes.Length)
            .Select(sequenceNumber => GetParameterInfo(
                reader,
                parameterHandles,
                sequenceNumber,
                beforeRetainText,
                beforeAttributeMaterialize))
            .ToArray();
        string[] parameterNames = CSharpParameterNames.Allocate(
            parameterInfos.Select(info => info.name).ToArray());
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            var parameterType = parameterTypes[i];
            var nullableBytes =
                NullabilityReader.GetParameterNullableBytes(
                    reader,
                    parameterHandles,
                    i + 1,
                    beforeDecodeWork);
            int position = 0;
            parameterType.ApplyNullability(
                nullableBytes,
                ref position,
                parameterNullableContext);
            var dynamicFlags =
                DynamicReader.GetParameterDynamicFlags(
                    reader,
                    parameterHandles,
                    i + 1,
                    beforeDecodeWork);
            position = 0;
            parameterType.ApplyDynamic(dynamicFlags, ref position);
            parameterType.ApplyTupleNames(
                TupleElementNamesReader.GetParameterTupleElementNames(
                    reader,
                    parameterHandles,
                    i + 1,
                    beforeDecodeWork));
            string renderedType = parameterType.Render();
            string canonicalType = parameterType.RenderCanonical();
            var (_, isParams, refKind, hasDefault, defaultValue, attributes) =
                parameterInfos[i];
            bool isByRef =
                renderedType.StartsWith("ref ", StringComparison.Ordinal);
            if (isByRef)
            {
                renderedType = renderedType["ref ".Length..];
                canonicalType = canonicalType["ref ".Length..];
                refKind ??= "ref";
            }
            else
            {
                refKind = null;
            }

            string? modifier = isParams ? "params" : refKind;
            bool acceptsNullDefault = AcceptsNullDefault(parameterType);
            string? defaultValueText = DefaultValueText(
                reader,
                defaultValue,
                renderedType,
                hasDefault,
                acceptsNullDefault,
                beforeDecodeWork);
            string renderedParameter = FormatParameter(
                renderedType,
                parameterNames[i],
                modifier,
                hasDefault,
                defaultValue,
                defaultValueText);
            beforeRetainText?.Invoke(renderedParameter);
            var parameterModel = new ApiParameter
            {
                Attributes = attributes,
                Name = parameterNames[i],
                Type = renderedType,
                CanonicalType = canonicalType,
                StructuralType = parameterType.HasStructuralPayload
                    ? parameterType.StructuralIdentity()
                    : null,
                CustomModifiersAreRepresentable =
                    CustomModifiersAreRepresentable(
                        parameterType,
                        requireReadOnlyByRefModifier: modifier == "in"),
                TypeReferences =
                    [.. parameterType.ReferencedTypes().Distinct()],
                Modifier = modifier,
                HasDefault = hasDefault,
                DefaultValueText = defaultValueText
            };
            ObserveText(parameterModel, beforeRetainText);
            display.Add(renderedParameter);
            models.Add(parameterModel);
        }

        return (display, models);
    }

    static void ApplyAccessorStructuralReturns(
        List<ApiAccessor> accessors,
        MetadataReader reader,
        string declarationName,
        Func<string, MethodDefinitionHandle> handleForKind,
        TypeNodeProvider provider,
        GenericContext context,
        IReadOnlySet<MethodDefinitionHandle> explicitImplementationBodies,
        Action<string>? beforeRetainText,
        Action<int>? beforeDecodeWork,
        MethodSignature<TypeNode>? propertySignature = null,
        bool propertyReturnIsByRef = false,
        bool propertyReturnIsReadOnlyByRef = false,
        TypeNode? eventType = null)
    {
        bool declarationModifiersMatch =
            AccessorDeclarationModifiersMatchProperty(
                accessors,
                reader,
                handleForKind);
        foreach (ApiAccessor accessor in accessors)
        {
            MethodDefinitionHandle handle = handleForKind(accessor.Kind);
            accessor.Name = MethodDefinitionName(reader, handle, beforeDecodeWork);
            accessor.NameMatchesDeclaration =
                accessor.Name is not null
                && AccessorNameMatchesDeclaration(
                    accessor.Name,
                    accessor.Kind,
                    declarationName);
            if (accessor.Name is not null)
                beforeRetainText?.Invoke(accessor.Name);
            if (!handle.IsNil)
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                accessor.IsExplicitInterfaceImplementation =
                    explicitImplementationBodies.Contains(handle)
                    && (method.Attributes & MethodAttributes.MemberAccessMask)
                        == MethodAttributes.Private;
                accessor.AccessibilityIsRepresentable =
                    IsRepresentableMethodAccessibility(
                        method.Attributes & MethodAttributes.MemberAccessMask);
                accessor.DeclarationModifiersMatchProperty =
                    declarationModifiersMatch;
                accessor.DeclarationModifiersAreRepresentable =
                    AreRepresentablePropertyAccessorDeclarationModifiers(
                        method.Attributes,
                        method.ImplAttributes,
                        accessor.IsExplicitInterfaceImplementation == true);
                MethodSignature<TypeNode> signature = GuardedProviderDecode.Method(
                    reader,
                    method,
                    provider,
                    context,
                    (TypeNode)new DegradedTypeNode());
                accessor.MethodDeclarationHeaderIsRepresentable =
                    MethodDeclarationHeaderIsRepresentable(
                        method,
                        signature,
                        accessor.Name!);
                accessor.StructuralReturnType = MethodStructuralReturnType(
                    signature.ReturnType,
                    beforeRetainText);
                accessor.CustomModifiersAreRepresentable =
                    PropertyCustomModifiersAreRepresentable(
                        signature.ReturnType,
                        allowReadOnlyByRef:
                            propertyReturnIsReadOnlyByRef
                            && accessor.Kind == "get")
                    && signature.ParameterTypes.All(
                        parameter => !ContainsCustomModifier(parameter));
                if (propertySignature is { } property)
                {
                    bool signatureMatchesProperty =
                        AccessorSignatureMatchesProperty(
                            accessor.Kind,
                            signature,
                            property,
                            context.TypeParameters.Count,
                            method,
                            accessor.Name!,
                            propertyReturnIsByRef,
                            propertyReturnIsReadOnlyByRef);
                    accessor.SignatureMatchesProperty =
                        signatureMatchesProperty;
                    accessor.SignatureMatchesDeclaration =
                        signatureMatchesProperty;
                }
                else if (eventType is not null)
                {
                    accessor.SignatureMatchesDeclaration =
                        AccessorSignatureMatchesEvent(
                            accessor.Kind,
                            signature,
                            eventType,
                            context.TypeParameters.Count,
                            method,
                            accessor.Name!);
                }
                accessor.IsReadOnly = AttributeReader.HasAttribute(
                    reader,
                    method.GetCustomAttributes(),
                    KnownAttributeNames.IsReadOnlyAttribute,
                    beforeDecodeWork);
            }
        }
    }

    static bool AccessorDeclarationModifiersMatchProperty(
        IReadOnlyList<ApiAccessor> accessors,
        MetadataReader reader,
        Func<string, MethodDefinitionHandle> handleForKind)
    {
        MethodAttributes? common = null;
        foreach (ApiAccessor accessor in accessors)
        {
            MethodDefinitionHandle handle = handleForKind(accessor.Kind);
            if (handle.IsNil)
                return false;
            MethodAttributes modifiers =
                reader.GetMethodDefinition(handle).Attributes
                & PropertyAccessorDeclarationModifierMask;
            if (common is not null && common != modifiers)
                return false;
            common = modifiers;
        }

        return common is not null;
    }

    static bool AreRepresentablePropertyAccessorDeclarationModifiers(
        MethodAttributes attributes,
        MethodImplAttributes implementationAttributes,
        bool isExplicitInterfaceImplementation)
    {
        if ((attributes & ~RepresentablePropertyAccessorAttributeMask) != 0
            || (attributes & RequiredPropertyAccessorAttributes)
                != RequiredPropertyAccessorAttributes
            || implementationAttributes != MethodImplAttributes.IL)
        {
            return false;
        }

        bool isVirtual = (attributes & MethodAttributes.Virtual) != 0;
        bool isAbstract = (attributes & MethodAttributes.Abstract) != 0;
        bool isNewSlot = (attributes & MethodAttributes.NewSlot) != 0;
        bool isFinal = (attributes & MethodAttributes.Final) != 0;

        if (isExplicitInterfaceImplementation
            && (attributes & MethodAttributes.MemberAccessMask)
                == MethodAttributes.Private
            && isVirtual
            && !isAbstract
            && isNewSlot
            && isFinal)
        {
            return true;
        }

        return (!isAbstract || isVirtual && !isFinal)
            && (!isNewSlot || isVirtual)
            && (!isFinal || isVirtual && !isNewSlot && !isAbstract);
    }

    static string? MethodDefinitionName(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        Action<int>? beforeDecodeWork)
    {
        if (handle.IsNil)
            return null;

        return DecodeString(
            reader,
            reader.GetMethodDefinition(handle).Name,
            beforeDecodeWork);
    }

    static bool AccessorNameMatchesDeclaration(
        string accessorName,
        string accessorKind,
        string declarationName)
    {
        string prefix = accessorKind switch
        {
            "get" => "get",
            "set" => "set",
            "add" => "add",
            "remove" => "remove",
            _ => "",
        };
        if (prefix.Length == 0)
            return false;

        int separator = declarationName.LastIndexOf('.');
        ReadOnlySpan<char> declarationPrefix = separator < 0
            ? []
            : declarationName.AsSpan(0, separator + 1);
        ReadOnlySpan<char> memberName = declarationName.AsSpan(
            separator + 1);
        int prefixOffset = declarationPrefix.Length;
        int memberOffset = prefixOffset + prefix.Length + 1;
        return memberName.Length > 0
            && accessorName.Length == memberOffset + memberName.Length
            && accessorName.AsSpan(0, prefixOffset)
                .SequenceEqual(declarationPrefix)
            && accessorName.AsSpan(prefixOffset, prefix.Length)
                .SequenceEqual(prefix)
            && accessorName[memberOffset - 1] == '_'
            && accessorName.AsSpan(memberOffset).SequenceEqual(memberName);
    }

    static string? MethodStructuralReturnType(
        TypeNode returnType,
        Action<string>? beforeRetainText)
    {
        if (!returnType.HasStructuralPayload)
            return null;

        string identity = returnType.StructuralIdentity();
        beforeRetainText?.Invoke(identity);
        return identity;
    }

    static bool AccessorSignatureMatchesProperty(
        string kind,
        MethodSignature<TypeNode> accessor,
        MethodSignature<TypeNode> property,
        int declaringTypeParameterCount,
        MethodDefinition accessorMethod,
        string accessorName,
        bool propertyReturnIsByRef,
        bool propertyReturnIsReadOnlyByRef)
    {
        if (!MethodDeclarationHeaderIsRepresentable(
                accessorMethod,
                accessor,
                accessorName)
            || accessor.Header.IsGeneric
            || accessor.GenericParameterCount != 0
            || accessorMethod.GetGenericParameters().Count != 0
            || property.Header.Kind != SignatureKind.Property
            || property.Header.HasExplicitThis
            || property.Header.IsGeneric
            || (property.Header.RawValue & ReservedSignatureFlag) != 0
            || property.GenericParameterCount != 0
            || property.RequiredParameterCount != property.ParameterTypes.Length
            || accessor.Header.IsInstance != property.Header.IsInstance
            || ((accessorMethod.Attributes & MethodAttributes.Static) != 0)
                == property.Header.IsInstance)
        {
            return false;
        }

        return kind switch
        {
            "get" =>
                (SignatureTypeMatches(
                        accessor.ReturnType,
                        property.ReturnType,
                        declaringTypeParameterCount)
                    || propertyReturnIsByRef
                        && ByRefSignatureTypeMatches(
                            accessor.ReturnType,
                            property.ReturnType,
                            declaringTypeParameterCount,
                            propertyReturnIsReadOnlyByRef))
                && SignatureTypesMatch(
                    accessor.ParameterTypes,
                    property.ParameterTypes,
                    declaringTypeParameterCount),
            "set" =>
                IsVoidReturn(accessor.ReturnType)
                && accessor.ParameterTypes.Length
                    == property.ParameterTypes.Length + 1
                && SignatureTypePrefixMatches(
                    accessor.ParameterTypes,
                    property.ParameterTypes,
                    declaringTypeParameterCount)
                && SignatureTypeMatches(
                    accessor.ParameterTypes[^1],
                    property.ReturnType,
                    declaringTypeParameterCount),
            _ => false,
        };
    }

    static bool ByRefSignatureTypeMatches(
        TypeNode left,
        TypeNode right,
        int declaringTypeParameterCount,
        bool allowReadOnlyByRef)
    {
        if (!TryGetByRefElement(
                left,
                allowReadOnlyByRef,
                out TypeNode leftElement)
            || !TryGetByRefElement(
                right,
                allowReadOnlyByRef,
                out TypeNode rightElement))
        {
            return false;
        }

        return SignatureTypeMatches(
            leftElement,
            rightElement,
            declaringTypeParameterCount);
    }

    static bool TryGetByRefElement(
        TypeNode type,
        bool allowReadOnlyByRef,
        out TypeNode element)
    {
        element = null!;
        if (!PropertyCustomModifiersAreRepresentable(
                type,
                allowReadOnlyByRef))
        {
            return false;
        }

        while (type is ModifiedTypeNode modified)
            type = modified.Inner;
        if (type is not ByRefTypeNode byRef
            || ContainsCustomModifier(byRef.ElementType))
        {
            return false;
        }

        element = byRef.ElementType;
        return true;
    }

    static bool IsByRefType(TypeNode type)
    {
        while (type is ModifiedTypeNode modified)
            type = modified.Inner;
        return type is ByRefTypeNode;
    }

    static ApiTypeShape? PropertyReturnTypeShape(
        TypeNode type,
        bool isByRef,
        bool allowReadOnlyByRef)
    {
        if (!isByRef)
            return ApiTypeShapeFactory.FromTypeNode(type);

        return TryGetByRefElement(
            type,
            allowReadOnlyByRef,
            out TypeNode element)
                ? ApiTypeShapeFactory.FromTypeNode(element)
                : null;
    }

    static bool AccessorSignatureMatchesEvent(
        string kind,
        MethodSignature<TypeNode> accessor,
        TypeNode eventType,
        int declaringTypeParameterCount,
        MethodDefinition accessorMethod,
        string accessorName)
        => MethodDeclarationHeaderIsRepresentable(
                accessorMethod,
                accessor,
                accessorName)
            && !accessor.Header.IsGeneric
            && accessor.GenericParameterCount == 0
            && accessorMethod.GetGenericParameters().Count == 0
            && kind is "add" or "remove"
            && IsVoidReturn(accessor.ReturnType)
            && accessor.ParameterTypes.Length == 1
            && SignatureTypeMatches(
                accessor.ParameterTypes[0],
                eventType,
                declaringTypeParameterCount);

    static bool SignatureTypesMatch(
        ImmutableArray<TypeNode> left,
        ImmutableArray<TypeNode> right,
        int declaringTypeParameterCount)
    {
        if (left.Length != right.Length)
            return false;

        for (int index = 0; index < left.Length; index++)
        {
            if (!SignatureTypeMatches(
                    left[index],
                    right[index],
                    declaringTypeParameterCount))
                return false;
        }

        return true;
    }

    static bool SignatureTypePrefixMatches(
        ImmutableArray<TypeNode> left,
        ImmutableArray<TypeNode> prefix,
        int declaringTypeParameterCount)
    {
        if (left.Length < prefix.Length)
            return false;

        for (int index = 0; index < prefix.Length; index++)
        {
            if (!SignatureTypeMatches(
                    left[index],
                    prefix[index],
                    declaringTypeParameterCount))
                return false;
        }

        return true;
    }

    static bool SignatureTypeMatches(
        TypeNode left,
        TypeNode right,
        int declaringTypeParameterCount)
    {
        if (ApiTypeShapeFactory.FromTypeNode(left) is not { } leftShape
            || ApiTypeShapeFactory.FromTypeNode(right) is not { } rightShape
            || ContainsUnboundGenericParameter(
                leftShape,
                declaringTypeParameterCount)
            || ContainsUnboundGenericParameter(
                rightShape,
                declaringTypeParameterCount))
        {
            return false;
        }

        return leftShape.Equals(rightShape);
    }

    static bool ContainsUnboundGenericParameter(
        ApiTypeShape shape,
        int declaringTypeParameterCount)
    {
        if (shape is
            {
                Kind: ApiTypeShapeKind.GenericParameter,
                GenericParameterIndex: var index,
                IsMethodGenericParameter: var isMethod,
            })
        {
            return isMethod
                || index < 0
                || index >= declaringTypeParameterCount;
        }
        if (shape.ElementType is not null
            && ContainsUnboundGenericParameter(
                shape.ElementType,
                declaringTypeParameterCount))
        {
            return true;
        }
        foreach (ApiTypeShape argument in shape.TypeArguments)
        {
            if (ContainsUnboundGenericParameter(
                    argument,
                    declaringTypeParameterCount))
                return true;
        }

        return false;
    }

    static bool IsVoidReturn(TypeNode type)
    {
        while (type is ModifiedTypeNode modified)
            type = modified.Inner;

        return type is PrimitiveTypeNode { Name: "void" };
    }

    static bool CustomModifiersAreRepresentable(
        TypeNode type,
        bool requireReadOnlyByRefModifier)
    {
        if (!ContainsCustomModifier(type))
            return !requireReadOnlyByRefModifier;
        return requireReadOnlyByRefModifier
            && type is ModifiedTypeNode
            {
                IsRequired: true,
                Modifier: { } modifier,
                Inner: ByRefTypeNode byRef,
            }
            && IsReadOnlyByRefModifier(modifier)
            && !ContainsCustomModifier(byRef.ElementType);
    }

    static bool PropertyCustomModifiersAreRepresentable(
        TypeNode type,
        bool allowReadOnlyByRef)
    {
        if (!ContainsCustomModifier(type))
            return true;
        if (!allowReadOnlyByRef)
            return false;

        bool sawByRef = false;
        int modifierCount = 0;
        TypeNode current = type;
        while (true)
        {
            if (current is ModifiedTypeNode modified)
            {
                if (!modified.IsRequired
                    || !IsPropertyReadOnlyByRefModifier(modified.Modifier))
                {
                    return false;
                }
                modifierCount++;
                current = modified.Inner;
                continue;
            }
            if (current is ByRefTypeNode byRef && !sawByRef)
            {
                sawByRef = true;
                current = byRef.ElementType;
                continue;
            }
            break;
        }

        return sawByRef
            && modifierCount == 1
            && !ContainsCustomModifier(current);
    }

    static bool ContainsCustomModifier(TypeNode type) => type switch
    {
        ModifiedTypeNode => true,
        GenericTypeNode generic =>
            generic.Arguments.Any(ContainsCustomModifier),
        SZArrayTypeNode array =>
            ContainsCustomModifier(array.ElementType),
        MDArrayTypeNode array =>
            ContainsCustomModifier(array.ElementType),
        PointerTypeNode pointer =>
            ContainsCustomModifier(pointer.ElementType),
        ByRefTypeNode byRef =>
            ContainsCustomModifier(byRef.ElementType),
        FunctionPointerTypeNode functionPointer =>
            functionPointer.ChildTypes.Any(ContainsCustomModifier),
        PassthroughTypeNode passthrough =>
            ContainsCustomModifier(passthrough.Inner),
        _ => false,
    };

    static bool IsReadOnlyByRefModifier(TypeNode modifier)
    {
        ApiTypeReferenceIdentity? reference =
            modifier.DefinitionReference();
        if (reference?.DefinitionName is not { } definitionName
            || definitionName.Segments.Length != 1
            || !ResolvesThroughCoreLibrary(reference.Assembly))
        {
            return false;
        }

        string name = definitionName.Segments[0];
        return (definitionName.Namespace
                    == "System.Runtime.InteropServices"
                && name == "InAttribute");
    }

    static bool IsPropertyReadOnlyByRefModifier(TypeNode modifier)
    {
        ApiTypeReferenceIdentity? reference =
            modifier.DefinitionReference();
        if (reference?.DefinitionName is not { } definitionName
            || definitionName.Segments.Length != 1
            || !ResolvesThroughCoreLibrary(reference.Assembly))
        {
            return false;
        }

        string name = definitionName.Segments[0];
        return (definitionName.Namespace
                    == "System.Runtime.CompilerServices"
                && name is
                    "IsReadOnlyAttribute"
                        or "RequiresLocationAttribute")
            || (definitionName.Namespace
                    == "System.Runtime.InteropServices"
                && name == "InAttribute");
    }

    /// <summary>
    /// Formats a property accessor with its access level prefix when it differs from the property's overall level.
    /// </summary>
    private static string FormatAccessor(string kind, MethodAttributes access, int bestAccess)
    {
        if ((int)access == bestAccess)
            return kind;
        var prefix = GetAccessibility(access);
        return prefix != null ? $"{prefix} {kind}" : kind;
    }

    private static string? AccessorAccessibility(MethodAttributes access, int bestAccess)
        => (int)access == bestAccess ? null : GetAccessibility(access);

    /// <summary>Gets the first parameter type for the lightweight extraction path.</summary>
    private static string? GetFirstParameterType(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        return GuardedSignatureText.MethodText(reader, method, context)
            .TryGetValue(out var signature)
                && signature.ParameterTypes.Length > 0
                    ? signature.ParameterTypes[0]
                    : null;
    }

    private static MetadataTypeDefinitionName? GetFirstParameterDefinitionName(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        MethodSignature<MetadataTypeDefinitionName?> signature =
            GuardedProviderDecode.Method(
                reader,
                method,
                DefinesPrimitiveTypes(reader)
                    ? ExtensionReceiverDefinitionProvider.WithLocalPrimitives
                    : ExtensionReceiverDefinitionProvider.WithoutLocalPrimitives,
                context,
                fallbackReturn: null);
        return signature.ParameterTypes.Length > 0
            ? signature.ParameterTypes[0]
            : null;
    }
}
