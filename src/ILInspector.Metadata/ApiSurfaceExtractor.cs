using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.CSharp;

namespace ILInspector.Metadata;

/// <summary>
/// Extracts public API surface from assemblies.
/// </summary>
public static class ApiSurfaceExtractor
{
    private const string OptionalAttributeName = "System.Runtime.InteropServices.Optional";
    private const string DateTimeConstantAttributeName = "System.Runtime.CompilerServices.DateTimeConstant";

    public static ApiSurface Extract(PEReader peReader, bool includeAll = false, bool typesOnly = false, bool includeCompilerGenerated = false)
    {
        var surface = new ApiSurface();
        var reader = peReader.GetMetadataReader();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            int publicMethodCount = surface.PublicMethodCount;
            int publicPropertyCount = surface.PublicPropertyCount;
            int publicEventCount = surface.PublicEventCount;
            int publicFieldCount = surface.PublicFieldCount;
            try
            {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            var attributes = typeDef.Attributes;

            // Only include public types by default. --all (includeAll) also surfaces
            // non-public types, including nested private/internal types, so ranking/triage rows
            // that already surface non-public IL can be copied into type/member drill commands.
            // Compiler-generated types are still skipped below regardless.
            if (!typeDef.IsPublic && !includeAll)
                continue;

            string metadataName = reader.GetString(typeDef.Name);

            // Skip compiler-generated types unless explicitly requested. The opt-in
            // surfaces closure/display/state-machine types and their real fields so
            // tooling (and compile-back reconstruction) can enumerate captured state.
            if (TypeFilters.IsCompilerGenerated(metadataName) && !includeCompilerGenerated)
                continue;

            // Skip EditorBrowsable(Never) and Obsolete types unless --all
            if (!includeAll && AttributeReader.HasHiddenAttribute(reader, typeDef.GetCustomAttributes()))
                continue;

            var (typeNamespace, typeName) = GetApiTypeNameParts(reader, typeDefHandle);

            var apiType = new ApiType
            {
                Namespace = typeNamespace,
                Name = typeName,
                MetadataName = GetMetadataName(reader, typeDefHandle),
                Accessibility = MetadataDeclarationQuery.TypeAccessibility(typeDef),
                IsSealed = (attributes & TypeAttributes.Sealed) != 0,
                IsAbstract = (attributes & TypeAttributes.Abstract) != 0,
                Attributes = AttributeReader.RenderAttributes(reader, typeDef.GetCustomAttributes(), qualifyNames: true),
            };

            // Determine kind
            if ((attributes & TypeAttributes.Interface) != 0)
            {
                apiType.Kind = "interface";
            }
            else if (!typeDef.BaseType.IsNil)
            {
                string baseTypeName = ResolveRequiredTypeName(
                    reader,
                    typeDef.BaseType);
                apiType.BaseType = ApplyDynamicView(
                    reader,
                    typeDef.BaseType,
                    typeDef.GetCustomAttributes(),
                    GenericContext.ForType(reader, typeDef),
                    baseTypeName);

                apiType.Kind = baseTypeName switch
                {
                    "System.Enum" => "enum",
                    "System.ValueType" => "struct",
                    "System.Delegate" or "System.MulticastDelegate" => "delegate",
                    _ => "class"
                };
            }
            else
            {
                apiType.Kind = "class";
            }

            apiType.IsStatic = apiType.IsSealed && apiType.IsAbstract;

            // The ref struct / readonly struct modifiers. Their [IsByRefLike] /
            // [IsReadOnly] attributes are compiler-synthesized from syntax and so
            // suppressed from the attribute list (AttributeReader.IsReEmitted), so
            // the modifier is reconstructed here from the still-present attribute.
            if (apiType.Kind == "struct")
            {
                var typeAttributes = typeDef.GetCustomAttributes();
                apiType.IsByRefLike = AttributeReader.HasAttribute(reader, typeAttributes, KnownAttributeNames.IsByRefLikeAttribute);
                apiType.IsReadOnly = AttributeReader.HasAttribute(reader, typeAttributes, KnownAttributeNames.IsReadOnlyAttribute);
            }

            // Check if this is an extension class (static class with [Extension] attribute)
            bool isExtensionClass = apiType.IsStatic && AttributeReader.HasExtensionAttribute(reader, typeDef.GetCustomAttributes());

            // Nullability context for annotated signatures
            byte typeNullableContext = NullabilityReader.GetNullableContext(reader, typeDef.GetCustomAttributes());

            // Get type's generic context for resolving interface type parameters
            var typeContext = GenericContext.ForType(reader, typeDef);

            apiType.TypeParameters = GenericParameters(reader, typeDef.GetGenericParameters(), typeContext, typeNullableContext, includeVariance: true);

            // Get interfaces
            var interfaces = typeDef.GetInterfaceImplementations();
            if (interfaces.Count > 0)
            {
                apiType.Interfaces = [];
                foreach (var ifaceHandle in interfaces)
                {
                    var iface = reader.GetInterfaceImplementation(ifaceHandle);
                    string ifaceName = ResolveRequiredTypeName(
                        reader,
                        iface.Interface,
                        typeContext);
                    ifaceName = ApplyDynamicView(
                        reader,
                        iface.Interface,
                        iface.GetCustomAttributes(),
                        typeContext,
                        ifaceName);
                    apiType.Interfaces.Add(ifaceName);
                }
            }

            // Get members (public only, or all when includeAll)
            if (!typesOnly)
            {
            apiType.Members = [];

            var explicitImplementationBodies = GetExplicitImplementationBodies(reader, typeDef);

            // Methods whose explicit `.override` MethodImpl targets
            // `System.Object::Finalize` — i.e. genuine class finalizers, the
            // slot the C# `~Type()` destructor compiles to.
            var objectFinalizeOverrides = GetObjectFinalizeOverrides(reader, typeDef);

            // Methods
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var methodAccess = method.Attributes & MethodAttributes.MemberAccessMask;
                var isExplicitInterfaceImplementation = explicitImplementationBodies.Contains(methodHandle);
                if (methodAccess != MethodAttributes.Public && !includeAll && !isExplicitInterfaceImplementation)
                    continue;

                string methodName = reader.GetString(method.Name);

                // Skip property accessors and event accessors
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_") ||
                    methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
                    continue;

                // Skip compiler-generated methods (lambdas, state machines, etc.)
                if (methodName.StartsWith("<"))
                    continue;

                // Skip EditorBrowsable(Never) methods unless --all; obsolete are surfaced with marker.
                if (!includeAll
                    && !isExplicitInterfaceImplementation
                    && AttributeReader.HasEditorBrowsableNeverAttribute(reader, method.GetCustomAttributes()))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(reader, method.GetCustomAttributes(), out var obsoleteMessage);

                var signature = GetMethodSignature(reader, typeDef, method, typeNullableContext);
                var isOperator = IsOperatorMethodName(methodName);
                var methodAttributes = method.Attributes;
                var isVirtual = (methodAttributes & MethodAttributes.Virtual) != 0;
                var isNewSlot = (methodAttributes & MethodAttributes.NewSlot) != 0;
                var isOverride = isVirtual && !isNewSlot && !isExplicitInterfaceImplementation;

                // A class finalizer is the `object.Finalize` override the C#
                // `~Type()` destructor compiles to. Roslyn emits it with an
                // explicit `.override` MethodImpl targeting
                // `System.Object::Finalize`, so we detect it by that target
                // rather than by name/signature shape. Keying on the overridden
                // slot (not the name `Finalize`, the reused virtual slot, or the
                // decoded signature) excludes the false positives a shape
                // heuristic admits: an implicit generic `Finalize<T>()`, an
                // override of an unrelated base/interface `Finalize()` slot, and
                // an explicit `IFoo.Finalize()` implementation (whose MethodImpl
                // targets the interface, not object). A method whose signature
                // failed to decode is likewise judged by its MethodImpl target
                // alone, so a degraded decode cannot masquerade as a finalizer.
                // A finalizer is never generic, so we still reject a method that
                // explicitly `.override`s object.Finalize while declaring its own
                // type parameters — rendering it `~Type()` would erase `<T>`.
                // VB-style implicit `object.Finalize` overrides carry no
                // MethodImpl and fall back to the literal `void Finalize()`.
                var isFinalizer = apiType.Kind == "class"
                    && method.GetGenericParameters().Count == 0
                    && objectFinalizeOverrides.Contains(methodHandle);

                var member = new ApiMember
                {
                    Name = methodName,
                    Kind = methodName switch
                    {
                        ".ctor" => "constructor",
                        _ when isOperator => "operator",
                        _ when isExplicitInterfaceImplementation => "explicit-interface-implementation",
                        _ => "method"
                    },
                    IsStatic = (methodAttributes & MethodAttributes.Static) != 0,
                    IsVirtual = isVirtual,
                    IsAbstract = (methodAttributes & MethodAttributes.Abstract) != 0,
                    IsOverride = isOverride,
                    IsSealed = isOverride && (methodAttributes & MethodAttributes.Final) != 0,
                    IsFinalizer = isFinalizer,
                    Signature = signature.Text,
                    SignatureModel = signature.Model,
                    SignatureDecodeStatus = signature.IsDegraded
                        ? SignatureDecodeStatus.Degraded
                        : null,
                    // Conversion operators overload on return type. SignatureModel is
                    // [JsonIgnore], so persist the return type on the serialized member
                    // too, letting the canonical-signature fallback disambiguate them on a
                    // round-tripped ApiSurface (where SignatureModel is gone).
                    ReturnType = ApiMemberIdentity.IsConversionOperator(methodName) ? signature.Model?.ReturnType : null,
                    MetadataToken = MetadataTokens.GetToken(methodHandle),
                    IsUnsafe = HasUnsafeSignature(signature.Text)
                        || AttributeReader.HasRequiresUnsafeAttribute(reader, method.GetCustomAttributes()),
                    Accessibility = isExplicitInterfaceImplementation && !isOperator ? null : GetAccessibility(methodAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    Attributes = RenderMemberAttributes(reader, method.GetCustomAttributes())
                };

                // Check for extension method
                if (isExtensionClass && member.IsStatic && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
                {
                    member.IsExtension = true;
                    member.ExtendedType = GetFirstParameterType(reader, typeDef, method);
                    member.DeclaringType = apiType.FullName;
                }

                apiType.Members.Add(member);
                surface.PublicMethodCount++;
            }

            // Properties
            foreach (var propHandle in typeDef.GetProperties())
            {
                var prop = reader.GetPropertyDefinition(propHandle);
                var accessors = prop.GetAccessors();

                // Determine best accessor visibility
                MethodAttributes bestAccess = 0;
                bool isStaticProperty = false;
                bool isVirtualProperty = false;
                bool isAbstractProperty = false;
                bool isOverrideProperty = false;
                bool isSealedProperty = false;
                if (!accessors.Getter.IsNil)
                {
                    var getter = reader.GetMethodDefinition(accessors.Getter);
                    var getterAttributes = getter.Attributes;
                    bestAccess = getter.Attributes & MethodAttributes.MemberAccessMask;
                    isStaticProperty = (getterAttributes & MethodAttributes.Static) != 0;
                    isVirtualProperty = (getterAttributes & MethodAttributes.Virtual) != 0;
                    isAbstractProperty = (getterAttributes & MethodAttributes.Abstract) != 0;
                    isOverrideProperty = isVirtualProperty && (getterAttributes & MethodAttributes.NewSlot) == 0;
                    isSealedProperty = isOverrideProperty && (getterAttributes & MethodAttributes.Final) != 0;
                }
                if (!accessors.Setter.IsNil)
                {
                    var setter = reader.GetMethodDefinition(accessors.Setter);
                    var setterAttributes = setter.Attributes;
                    var setterAccess = setterAttributes & MethodAttributes.MemberAccessMask;
                    if (setterAccess > bestAccess)
                        bestAccess = setterAccess;
                    var setterVirtual = (setterAttributes & MethodAttributes.Virtual) != 0;
                    var setterOverride = setterVirtual && (setterAttributes & MethodAttributes.NewSlot) == 0;
                    isStaticProperty |= (setterAttributes & MethodAttributes.Static) != 0;
                    isVirtualProperty |= setterVirtual;
                    isAbstractProperty |= (setterAttributes & MethodAttributes.Abstract) != 0;
                    isOverrideProperty |= setterOverride;
                    isSealedProperty |= setterOverride && (setterAttributes & MethodAttributes.Final) != 0;
                }

                bool isPublicProp = bestAccess == MethodAttributes.Public;
                if (!isPublicProp && !includeAll)
                    continue;

                // Skip EditorBrowsable(Never) properties unless --all; obsolete are surfaced with marker.
                if (!includeAll && AttributeReader.HasEditorBrowsableNeverAttribute(reader, prop.GetCustomAttributes()))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(reader, prop.GetCustomAttributes(), out var obsoleteMessage);

                var propertySignature = GetPropertySignature(reader, typeDef, prop, accessors, typeNullableContext, includeAll);
                var member = new ApiMember
                {
                    Name = reader.GetString(prop.Name),
                    Kind = "property",
                    Signature = propertySignature.Text,
                    SignatureModel = propertySignature.Model,
                    SignatureDecodeStatus = propertySignature.IsDegraded
                        ? SignatureDecodeStatus.Degraded
                        : null,
                    IsStatic = isStaticProperty,
                    IsVirtual = isVirtualProperty,
                    IsAbstract = isAbstractProperty,
                    IsOverride = isOverrideProperty,
                    IsSealed = isSealedProperty,
                    IsUnsafe = HasUnsafeSignature(propertySignature.Text),
                    Accessibility = GetAccessibility(bestAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    Attributes = RenderMemberAttributes(reader, prop.GetCustomAttributes()),
                    GetterToken = accessors.Getter.IsNil ? null : MetadataTokens.GetToken(accessors.Getter),
                    SetterToken = accessors.Setter.IsNil ? null : MetadataTokens.GetToken(accessors.Setter)
                };

                apiType.Members.Add(member);
                surface.PublicPropertyCount++;
            }

            // Fields (non-backing fields; non-public included with --all)
            bool isEnum = apiType.Kind == "enum";

            // A C# field-like event's compiler-generated backing field is private, is itself
            // marked [CompilerGenerated], and shares the event's exact (unmangled) name. That
            // pre-scan and the per-field fold below are factored into shared helpers so
            // API-surface extraction and compile-back reconstruction agree on the fold.
            var fieldLikeEventBackingFieldNames = FieldLikeEventBackingFieldNames(reader, typeDef);
            var autoPropertyBackingFields = AutoPropertyBackingFieldDescriptors(reader, typeDef, typeContext);

            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                var fieldAccess = field.Attributes & FieldAttributes.FieldAccessMask;
                if (fieldAccess != FieldAttributes.Public && !includeAll)
                    continue;

                string fieldName = reader.GetString(field.Name);
                if (!IsSurfaceableFieldName(fieldName, includeCompilerGenerated))
                    continue; // Skip compiler-generated (<...>) fields unless opted in

                if (IsAutoPropertyBackingField(reader, field, fieldName, autoPropertyBackingFields, typeContext))
                    continue; // Skip a synthesized auto-property backing field (re-synthesized on reconstruction)

                if (IsFieldLikeEventBackingField(reader, field, fieldName, fieldLikeEventBackingFieldNames))
                    continue; // Skip a field-like event's private, compiler-generated backing field

                // Skip EditorBrowsable(Never) fields unless --all; obsolete are surfaced with marker.
                if (!includeAll && AttributeReader.HasEditorBrowsableNeverAttribute(reader, field.GetCustomAttributes()))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(reader, field.GetCustomAttributes(), out var obsoleteMessage);

                // Decode field type. For enums the special value__ field carries
                // the underlying type; literal fields are constants, not fields in
                // source, so they do not need a field declaration type.
                string? fieldType = null;
                bool fieldSignatureDegraded = false;
                if (isEnum)
                {
                    if (fieldName == "value__")
                        apiType.EnumUnderlyingType = DecodeFieldType(
                            reader,
                            typeDef,
                            field,
                            typeNullableContext).Text;
                }
                else
                {
                    (fieldType, fieldSignatureDegraded) = DecodeFieldType(
                        reader,
                        typeDef,
                        field,
                        typeNullableContext);
                }

                var member = new ApiMember
                {
                    Name = fieldName,
                    Kind = "field",
                    ReturnType = fieldType,
                    SignatureModel = fieldType is null ? null : new ApiSignature
                    {
                        ReturnType = fieldType,
                        MemberName = fieldName
                    },
                    SignatureDecodeStatus = fieldSignatureDegraded
                        ? SignatureDecodeStatus.Degraded
                        : null,
                    IsStatic = (field.Attributes & FieldAttributes.Static) != 0,
                    IsReadOnly = (field.Attributes & FieldAttributes.InitOnly) != 0,
                    IsConst = (field.Attributes & FieldAttributes.Literal) != 0,
                    Accessibility = GetFieldAccessibility(fieldAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    Attributes = RenderMemberAttributes(reader, field.GetCustomAttributes())
                };

                // Read enum constant value
                if (isEnum && (field.Attributes & FieldAttributes.Literal) != 0)
                {
                    var constantHandle = field.GetDefaultValue();
                    if (!constantHandle.IsNil)
                    {
                        var constant = reader.GetConstant(constantHandle);
                        var blob = reader.GetBlobReader(constant.Value);
                        member.EnumValue = constant.TypeCode switch
                        {
                            ConstantTypeCode.SByte => blob.ReadSByte(),
                            ConstantTypeCode.Byte => blob.ReadByte(),
                            ConstantTypeCode.Int16 => blob.ReadInt16(),
                            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                            ConstantTypeCode.Int32 => blob.ReadInt32(),
                            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
                            ConstantTypeCode.Int64 => blob.ReadInt64(),
                            ConstantTypeCode.UInt64 => (long)blob.ReadUInt64(),
                            _ => null
                        };
                        blob = reader.GetBlobReader(constant.Value);
                        member.EnumValueLiteral = constant.TypeCode switch
                        {
                            ConstantTypeCode.SByte => blob.ReadSByte().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.Byte => blob.ReadByte().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.Int16 => blob.ReadInt16().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.Int32 => blob.ReadInt32().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.UInt32 => blob.ReadUInt32().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.Int64 => blob.ReadInt64().ToString(CultureInfo.InvariantCulture),
                            ConstantTypeCode.UInt64 => blob.ReadUInt64().ToString(CultureInfo.InvariantCulture),
                            _ => null
                        };
                    }
                }

                apiType.Members.Add(member);
                surface.PublicFieldCount++;
            }

            // Events
            foreach (var eventHandle in typeDef.GetEvents())
            {
                var evt = reader.GetEventDefinition(eventHandle);
                var accessors = evt.GetAccessors();

                // Check if adder exists
                if (accessors.Adder.IsNil)
                    continue;

                var adder = reader.GetMethodDefinition(accessors.Adder);
                var adderAccess = adder.Attributes & MethodAttributes.MemberAccessMask;
                if (adderAccess != MethodAttributes.Public && !includeAll)
                    continue;

                // Skip EditorBrowsable(Never) events unless --all; obsolete are surfaced with marker.
                if (!includeAll && AttributeReader.HasEditorBrowsableNeverAttribute(reader, evt.GetCustomAttributes()))
                    continue;

                var isObsolete = AttributeReader.TryGetObsoleteAttribute(reader, evt.GetCustomAttributes(), out var obsoleteMessage);
                var eventType = ResolveRequiredTypeName(
                    reader,
                    evt.Type,
                    GenericContext.ForType(reader, typeDef));
                var eventNullableBytes = NullabilityReader.GetNullableBytes(reader, evt.GetCustomAttributes());
                eventNullableBytes ??= NullabilityReader.GetParameterNullableBytes(reader, adder.GetParameters(), 1);
                if (eventNullableBytes is { Length: > 0 } && eventNullableBytes[0] == 2 && !eventType.EndsWith("?", StringComparison.Ordinal))
                    eventType += "?";
                // A `dynamic` event handler (e.g. EventHandler<dynamic>) or a
                // named-tuple handler (EventHandler<(int a, int b)>) is always a
                // generic instantiation, so re-decode the TypeSpec through the
                // TypeNode tree to recover the dynamic / tuple view. Plain events
                // are untouched.
                var eventTupleNames = TupleElementNamesReader.GetTupleElementNames(reader, evt.GetCustomAttributes());
                var eventDynamicFlags = evt.Type.Kind == HandleKind.TypeSpecification
                    ? DynamicReader.GetDynamicFlags(reader, evt.GetCustomAttributes())
                    : null;
                if (evt.Type.Kind == HandleKind.TypeSpecification
                    && (eventDynamicFlags is not null || eventTupleNames is not null))
                {
                    var eventNode = GuardedProviderDecode.TypeSpec(
                        reader,
                        (TypeSpecificationHandle)evt.Type,
                        TypeNodeProvider.Instance,
                        GenericContext.ForType(reader, typeDef),
                        (TypeNode)new DegradedTypeNode());
                    // Skip a rejected/degraded decode: its bare "object"/"dynamic" render
                    // would obliterate the resolved eventType string computed above.
                    if (!eventNode.IsDegraded)
                    {
                        int eventPos = 0;
                        eventNode.ApplyNullability(eventNullableBytes, ref eventPos, 0);
                        eventPos = 0;
                        eventNode.ApplyDynamic(eventDynamicFlags, ref eventPos);
                        eventNode.ApplyTupleNames(eventTupleNames);
                        eventType = eventNode.Render();
                    }
                }
                var adderAttributes = adder.Attributes;
                var isVirtualEvent = (adderAttributes & MethodAttributes.Virtual) != 0;
                var isOverrideEvent = isVirtualEvent && (adderAttributes & MethodAttributes.NewSlot) == 0;

                var member = new ApiMember
                {
                    Name = reader.GetString(evt.Name),
                    Kind = "event",
                    ReturnType = eventType,
                    Signature = $"{eventType} {reader.GetString(evt.Name)}",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = eventType,
                        MemberName = reader.GetString(evt.Name)
                    },
                    IsStatic = (adderAttributes & MethodAttributes.Static) != 0,
                    IsVirtual = isVirtualEvent,
                    IsAbstract = (adderAttributes & MethodAttributes.Abstract) != 0,
                    IsOverride = isOverrideEvent,
                    IsSealed = isOverrideEvent && (adderAttributes & MethodAttributes.Final) != 0,
                    Accessibility = GetAccessibility(adderAccess),
                    IsObsolete = isObsolete,
                    ObsoleteMessage = obsoleteMessage,
                    AdderToken = accessors.Adder.IsNil
                        ? null
                        : MetadataTokens.GetToken(accessors.Adder),
                    RemoverToken = accessors.Remover.IsNil
                        ? null
                        : MetadataTokens.GetToken(accessors.Remover)
                };

                apiType.Members.Add(member);
                surface.PublicEventCount++;
            }
            } // end if (!typesOnly)

            surface.Types.Add(apiType);
            surface.PublicTypeCount++;
            }
            catch (MetadataRowRejectedException ex)
            {
                surface.PublicMethodCount = publicMethodCount;
                surface.PublicPropertyCount = publicPropertyCount;
                surface.PublicEventCount = publicEventCount;
                surface.PublicFieldCount = publicFieldCount;
                AddInspectionFailure(
                    surface,
                    ex.Operation,
                    typeDefHandle,
                    ex.Failure);
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                surface.PublicMethodCount = publicMethodCount;
                surface.PublicPropertyCount = publicPropertyCount;
                surface.PublicEventCount = publicEventCount;
                surface.PublicFieldCount = publicFieldCount;
                AddInspectionFailure(
                    surface,
                    "type row",
                    typeDefHandle,
                    MetadataTypeNameFailure.Malformed(typeDefHandle, ex.Message));
            }
        }

        AttachLocalExtensionMethods(surface);

        // Extract type forwarders (ExportedTypes that are forwarded to other assemblies)
        foreach (var exportedTypeHandle in reader.ExportedTypes)
        {
            try
            {
                var exportedType = reader.GetExportedType(exportedTypeHandle);

                // Type forwarders have IsForwarder flag set
                if (!exportedType.IsForwarder)
                    continue;

                var fullName = reader.ResolveFullTypeName(exportedTypeHandle) switch
                {
                    RelationshipTraversalResult<string>.Completed completed =>
                        completed.Value,
                    RelationshipTraversalResult<string>.Rejected rejected =>
                        throw new MetadataRowRejectedException(
                            "type forwarder identity",
                            MetadataTypeNameFailure.From(rejected.Rejection)),
                    _ => throw new InvalidOperationException(
                        "Unknown exported-type relationship result."),
                };

                // Get the target assembly
                string targetAssembly = "";
                if (exportedType.Implementation.Kind == HandleKind.AssemblyReference)
                {
                    var assemblyRef = reader.GetAssemblyReference((AssemblyReferenceHandle)exportedType.Implementation);
                    targetAssembly = reader.GetString(assemblyRef.Name);
                }

                surface.TypeForwarders.Add(new TypeForwarder
                {
                    TypeName = fullName,
                    TargetAssembly = targetAssembly
                });
            }
            catch (MetadataRowRejectedException ex)
            {
                AddInspectionFailure(
                    surface,
                    ex.Operation,
                    exportedTypeHandle,
                    ex.Failure);
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                AddInspectionFailure(
                    surface,
                    "type forwarder row",
                    exportedTypeHandle,
                    MetadataTypeNameFailure.Malformed(exportedTypeHandle, ex.Message));
            }
        }

        ApiMemberIdentity.PopulateCanonicalIdentities(surface);

        return surface;
    }

    private static byte? GetEffectiveNullable(
        MetadataReader reader, CustomAttributeHandleCollection attributes, byte nullableContext)
    {
        var bytes = NullabilityReader.GetNullableBytes(reader, attributes);
        if (bytes is { Length: > 0 })
            return bytes[0];
        return nullableContext != 0 ? nullableContext : null;
    }

    private static List<TypeParameter> GenericParameters(
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        GenericContext context,
        byte nullableContext,
        bool includeVariance)
    {
        var parameters = new List<TypeParameter>();
        foreach (var paramHandle in handles)
        {
            var param = reader.GetGenericParameter(paramHandle);
            var typeParam = new TypeParameter
            {
                Name = reader.GetString(param.Name)
            };
            var structured = new List<TypeParameterConstraint>();

            var attrs = param.Attributes;
            if (includeVariance && GenericConstraintKeywords.VarianceKeyword(attrs) is { } variance)
                typeParam.Variance = variance;

            var nullable = GetEffectiveNullable(reader, param.GetCustomAttributes(), nullableContext);
            var isUnmanaged = AttributeReader.HasAttribute(reader, param.GetCustomAttributes(),
                KnownAttributeNames.IsUnmanagedAttribute);

            if (GenericConstraintKeywords.PrimaryKeyword(attrs, nullable ?? 0, isUnmanaged) is { } primaryKeyword)
            {
                typeParam.Constraints.Add(primaryKeyword);
                structured.Add(new TypeParameterConstraint(primaryKeyword, IsTypeName: false));
            }

            foreach (var constraintHandle in param.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                string constraintTypeName = ResolveRequiredTypeName(
                    reader,
                    constraint.Type,
                    context);
                if (constraintTypeName is "System.ValueType" or "System.Object")
                    continue;
                var formatted = FormatConstraintType(reader, constraint, constraintTypeName, nullableContext);
                typeParam.Constraints.Add(formatted);
                structured.Add(new TypeParameterConstraint(formatted, IsTypeName: true));
            }

            if (GenericConstraintKeywords.NewConstraintKeyword(attrs) is { } newConstraint)
            {
                typeParam.Constraints.Add(newConstraint);
                structured.Add(new TypeParameterConstraint(newConstraint, IsTypeName: false));
            }
            if (GenericConstraintKeywords.AllowsRefStructKeyword(attrs) is { } allowsRefStruct)
            {
                typeParam.Constraints.Add(allowsRefStruct);
                structured.Add(new TypeParameterConstraint(allowsRefStruct, IsTypeName: false));
            }

            typeParam.StructuredConstraints = structured;
            parameters.Add(typeParam);
        }

        return parameters;
    }

    private static string FormatConstraintType(
        MetadataReader reader, GenericParameterConstraint constraint, string constraintTypeName, byte nullableContext)
    {
        var nullable = GetEffectiveNullable(reader, constraint.GetCustomAttributes(), nullableContext);
        return nullable == 2 && !constraintTypeName.EndsWith("?", StringComparison.Ordinal)
            ? $"{constraintTypeName}?"
            : constraintTypeName;
    }

    private static (string? Namespace, string Name) GetApiTypeNameParts(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var result = MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(
            reader,
            handle);
        if (result is RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Rejected rejected)
        {
            throw new MetadataRowRejectedException(
                "type identity",
                MetadataTypeNameFailure.From(rejected.Rejection));
        }

        var chain = ((RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Completed)result).Value;
        var rootNamespace = reader.GetString(
            reader.GetTypeDefinition(chain.Handles[0]).Namespace);
        string name = string.Join(
            ".",
            chain.Handles.Select(current =>
                reader.GetString(reader.GetTypeDefinition(current).Name)));
        string fullName = rootNamespace.Length == 0
            ? name
            : $"{rootNamespace}.{name}";
        if (rootNamespace.Length == 0)
            return (null, fullName);

        var prefix = rootNamespace + ".";
        return fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? (rootNamespace, fullName[prefix.Length..])
            : (rootNamespace, fullName);
    }

    private static string GetMetadataName(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var result = MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(
            reader,
            handle);
        if (result is RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Rejected rejected)
        {
            throw new MetadataRowRejectedException(
                "type metadata identity",
                MetadataTypeNameFailure.From(rejected.Rejection));
        }

        var chain = ((RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Completed)result).Value;
        return string.Join(
            "+",
            chain.Handles.Select(current =>
                reader.GetString(reader.GetTypeDefinition(current).Name)));
    }

    private static (string Text, bool IsDegraded) DecodeFieldType(
        MetadataReader reader,
        TypeDefinition typeDef,
        FieldDefinition field,
        byte typeNullableContext)
    {
        var context = GenericContext.ForType(reader, typeDef);
        var fieldNode = GuardedProviderDecode.Field(
            reader,
            field,
            TypeNodeProvider.Instance,
            context,
            (TypeNode)new DegradedTypeNode());
        var fieldBytes = NullabilityReader.GetNullableBytes(reader, field.GetCustomAttributes());
        int pos = 0;
        fieldNode.ApplyNullability(fieldBytes, ref pos, typeNullableContext);
        var fieldDynamicFlags = DynamicReader.GetDynamicFlags(reader, field.GetCustomAttributes());
        pos = 0;
        fieldNode.ApplyDynamic(fieldDynamicFlags, ref pos);
        fieldNode.ApplyTupleNames(
            TupleElementNamesReader.GetTupleElementNames(reader, field.GetCustomAttributes()));
        return (fieldNode.Render(), fieldNode.IsDegraded);
    }

    private static HashSet<MethodDefinitionHandle> GetExplicitImplementationBodies(
        MetadataReader reader, TypeDefinition typeDef)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody.Kind == HandleKind.MethodDefinition)
                handles.Add((MethodDefinitionHandle)implementation.MethodBody);
        }

        return handles;
    }

    /// <summary>
    /// The set of methods on <paramref name="typeDef"/> whose explicit
    /// <c>.override</c> MethodImpl targets <c>System.Object::Finalize</c> — the
    /// slot a C# <c>~Type()</c> destructor compiles to. Keying on the overridden
    /// declaration (not the method's own name/slot/signature) is what lets the
    /// C# writer spell <c>~Type()</c> for real finalizers while excluding a
    /// same-named override of an unrelated <c>Finalize</c> slot or an explicit
    /// interface implementation.
    /// </summary>
    private static HashSet<MethodDefinitionHandle> GetObjectFinalizeOverrides(
        MetadataReader reader, TypeDefinition typeDef)
    {
        HashSet<MethodDefinitionHandle> handles = [];
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody.Kind != HandleKind.MethodDefinition)
                continue;
            if (ReferencesObjectFinalize(reader, implementation.MethodDeclaration))
                handles.Add((MethodDefinitionHandle)implementation.MethodBody);
        }

        return handles;
    }

    /// <summary>
    /// True when <paramref name="methodDeclaration"/> (the target of a
    /// <c>.override</c> MethodImpl) names <c>Finalize</c> on <c>System.Object</c>.
    /// The target is a <see cref="MemberReferenceHandle"/> in the common case
    /// (object lives in another assembly) and a <see cref="MethodDefinitionHandle"/>
    /// only when inspecting the assembly that defines <c>System.Object</c>.
    /// </summary>
    private static bool ReferencesObjectFinalize(MetadataReader reader, EntityHandle methodDeclaration)
    {
        switch (methodDeclaration.Kind)
        {
            case HandleKind.MemberReference:
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)methodDeclaration);
                return string.Equals(reader.GetString(memberRef.Name), "Finalize", StringComparison.Ordinal)
                    && IsSystemObjectType(reader, memberRef.Parent);
            case HandleKind.MethodDefinition:
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)methodDeclaration);
                return string.Equals(reader.GetString(methodDef.Name), "Finalize", StringComparison.Ordinal)
                    && IsSystemObjectType(reader, methodDef.GetDeclaringType());
            default:
                return false;
        }
    }

    /// <summary>True when <paramref name="typeHandle"/> resolves to <c>System.Object</c>.</summary>
    private static bool IsSystemObjectType(MetadataReader reader, EntityHandle typeHandle)
    {
        switch (typeHandle.Kind)
        {
            case HandleKind.TypeReference:
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)typeHandle);
                return string.Equals(reader.GetString(typeRef.Namespace), "System", StringComparison.Ordinal)
                    && string.Equals(reader.GetString(typeRef.Name), "Object", StringComparison.Ordinal);
            case HandleKind.TypeDefinition:
                var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle);
                // The genuine root object is the only `System.Object` with no
                // base type. An adversarial assembly can define its own
                // `System.Object` that extends the real one (a non-root fake);
                // requiring a nil base type rejects it while still accepting the
                // real object when inspecting the assembly that defines it.
                return typeDef.BaseType.IsNil
                    && string.Equals(reader.GetString(typeDef.Namespace), "System", StringComparison.Ordinal)
                    && string.Equals(reader.GetString(typeDef.Name), "Object", StringComparison.Ordinal);
            default:
                return false;
        }
    }

    private static bool IsOperatorMethodName(string methodName) =>
        methodName.StartsWith("op_", StringComparison.Ordinal);

    private static void AttachLocalExtensionMethods(ApiSurface surface)
    {
        var targets = surface.Types
            .SelectMany(type => GetTypeMatchKeys(type).Select(key => (key, type)))
            .GroupBy(item => item.key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().type, StringComparer.OrdinalIgnoreCase);

        foreach (var declaringType in surface.Types)
        {
            foreach (var extension in declaringType.Members.Where(member => member.IsExtension))
            {
                var key = NormalizeTypeMatchKey(extension.ExtendedType);
                if (key == null || !targets.TryGetValue(key, out var targetType))
                    continue;
                if (ReferenceEquals(targetType, declaringType))
                    continue;
                if (targetType.Members.Any(member =>
                    member.Kind == "extension-method"
                    && string.Equals(member.DeclaringType, declaringType.FullName, StringComparison.Ordinal)
                    && string.Equals(member.Name, extension.Name, StringComparison.Ordinal)
                    && string.Equals(member.Signature, extension.Signature, StringComparison.Ordinal)))
                    continue;

                var declaringOverloadIndex = declaringType.Members
                    .Where(member => string.Equals(member.Name, extension.Name, StringComparison.Ordinal))
                    .ToList()
                    .IndexOf(extension) + 1;

                targetType.Members.Add(new ApiMember
                {
                    Name = extension.Name,
                    Kind = "extension-method",
                    ReturnType = extension.ReturnType,
                    Signature = extension.Signature,
                    SignatureModel = extension.SignatureModel,
                    SignatureDecodeStatus = extension.SignatureDecodeStatus,
                    MetadataToken = extension.MetadataToken,
                    IsStatic = extension.IsStatic,
                    IsVirtual = extension.IsVirtual,
                    IsAbstract = extension.IsAbstract,
                    IsOverride = extension.IsOverride,
                    IsSealed = extension.IsSealed,
                    IsUnsafe = extension.IsUnsafe,
                    IsExtension = true,
                    ExtendedType = extension.ExtendedType,
                    DeclaringType = declaringType.FullName,
                    DeclaringOverloadIndex = declaringOverloadIndex,
                    IsObsolete = extension.IsObsolete,
                    ObsoleteMessage = extension.ObsoleteMessage,
                    Documentation = extension.Documentation
                });
            }
        }
    }

    private static IEnumerable<string> GetTypeMatchKeys(ApiType type)
    {
        var fullNameKey = NormalizeTypeMatchKey(type.FullName);
        if (fullNameKey != null)
            yield return fullNameKey;
    }

    private static string? NormalizeTypeMatchKey(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var value = typeName.Trim();
        foreach (var prefix in (ReadOnlySpan<string>)["ref ", "in ", "out "])
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                value = value[prefix.Length..].TrimStart();
                break;
            }
        }

        if (value.EndsWith("?", StringComparison.Ordinal))
            value = value[..^1];

        value = PrimitiveTypeNames.ToClrFullName(value);

        var genericIndex = value.IndexOf('<');
        if (genericIndex > 0)
            value = value[..genericIndex];

        var arityIndex = value.IndexOf('`');
        if (arityIndex > 0)
            value = value[..arityIndex];

        return value;
    }

    /// <summary>
    /// Names of a type's field-like events. A C# field-like event's compiler-generated backing
    /// field is private, is itself marked <c>[CompilerGenerated]</c>, and shares the event's exact
    /// (unmangled) name. Only events whose adder is <c>[CompilerGenerated]</c> (i.e. genuinely
    /// field-like) contribute a name; hand-authored or non-C# accessors are excluded so a
    /// legitimate same-named field is not suppressed.
    /// </summary>
    static HashSet<string>? FieldLikeEventBackingFieldNames(MetadataReader reader, TypeDefinition typeDef)
    {
        HashSet<string>? names = null;
        foreach (var eventHandle in typeDef.GetEvents())
        {
            var eventDef = reader.GetEventDefinition(eventHandle);
            var adder = eventDef.GetAccessors().Adder;
            if (adder.IsNil
                || !AttributeReader.HasAttribute(
                    reader,
                    reader.GetMethodDefinition(adder).GetCustomAttributes(),
                    KnownAttributeNames.CompilerGeneratedAttribute))
            {
                continue;
            }

            (names ??= new HashSet<string>(StringComparer.Ordinal)).Add(reader.GetString(eventDef.Name));
        }

        return names;
    }

    /// <summary>
    /// True when a field is a field-like event's private, compiler-generated backing field. The
    /// decisive signal is the candidate field's own <c>[CompilerGenerated]</c> marker (not the
    /// accessor's): the C# CS0102 same-name restriction does not bind arbitrary IL, so a genuine
    /// field could share an event's name; requiring the field itself to be private and
    /// compiler-generated keeps it from being folded away.
    /// </summary>
    static bool IsFieldLikeEventBackingField(
        MetadataReader reader,
        FieldDefinition field,
        string fieldName,
        HashSet<string>? fieldLikeEventBackingFieldNames)
        => (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private
           && fieldLikeEventBackingFieldNames?.Contains(fieldName) == true
           && AttributeReader.HasAttribute(
               reader,
               field.GetCustomAttributes(),
               KnownAttributeNames.CompilerGeneratedAttribute);

    /// <summary>
    /// A declared auto-property's backing-field descriptor: the property's decoded return type and
    /// whether its accessors are static. A genuine backing field must agree with both, so a merely
    /// same-named compiler-generated field of a different type or staticness is not folded.
    /// </summary>
    readonly record struct AutoPropertyBackingField(string PropertyType, bool IsStatic);

    /// <summary>
    /// Maps each of a type's auto-property backing-field names (<c>&lt;Prop&gt;k__BackingField</c>)
    /// to its <see cref="AutoPropertyBackingField"/> descriptor. Only genuine auto-properties
    /// contribute: the property has a <c>[CompilerGenerated]</c> accessor (auto signal) and a
    /// decodable return type, and its name carries no <c>&lt;</c> or <c>.</c> (compiler-generated or
    /// explicit-interface names cannot name a C# auto-property). The per-field fold then also
    /// requires the candidate field's type and staticness to match this descriptor, mirroring the
    /// discriminator the compile-back planner historically applied so a same-named but
    /// type/static-mismatched or non-auto-property field is preserved rather than silently dropped.
    /// </summary>
    static Dictionary<string, AutoPropertyBackingField>? AutoPropertyBackingFieldDescriptors(
        MetadataReader reader,
        TypeDefinition typeDef,
        GenericContext context)
    {
        Dictionary<string, AutoPropertyBackingField>? descriptors = null;
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            string propertyName = reader.GetString(property.Name);
            if (propertyName.Contains('<', StringComparison.Ordinal)
                || propertyName.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryGetAutoPropertyAccessorStaticness(reader, property.GetAccessors(), out bool isStatic))
                continue; // Not an auto-property: no [CompilerGenerated] accessor.

            if (!GuardedSignatureText.PropertyText(reader, property, context)
                    .TryGetValue(out var propertySignature))
            {
                continue; // Undecodable property signature: cannot prove a type match.
            }

            (descriptors ??= new Dictionary<string, AutoPropertyBackingField>(StringComparer.Ordinal))
                [$"<{propertyName}>k__BackingField"]
                    = new AutoPropertyBackingField(propertySignature.ReturnType, isStatic);
        }

        return descriptors;
    }

    /// <summary>
    /// True when a property is an auto-property, i.e. either accessor is <c>[CompilerGenerated]</c>;
    /// <paramref name="isStatic"/> reports that accessor's staticness, which the backing field's own
    /// storage must share.
    /// </summary>
    static bool TryGetAutoPropertyAccessorStaticness(
        MetadataReader reader,
        PropertyAccessors accessors,
        out bool isStatic)
    {
        if (!accessors.Getter.IsNil)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            if (AttributeReader.HasAttribute(reader, getter.GetCustomAttributes(), KnownAttributeNames.CompilerGeneratedAttribute))
            {
                isStatic = (getter.Attributes & MethodAttributes.Static) != 0;
                return true;
            }
        }

        if (!accessors.Setter.IsNil)
        {
            var setter = reader.GetMethodDefinition(accessors.Setter);
            if (AttributeReader.HasAttribute(reader, setter.GetCustomAttributes(), KnownAttributeNames.CompilerGeneratedAttribute))
            {
                isStatic = (setter.Attributes & MethodAttributes.Static) != 0;
                return true;
            }
        }

        isStatic = false;
        return false;
    }

    /// <summary>
    /// True when a field is a genuine auto-property backing field that reconstruction will
    /// re-synthesize from auto-property syntax: it is <c>[CompilerGenerated]</c>, its name matches a
    /// declared auto-property's backing-field name, and its staticness and type agree with that
    /// property. Requiring type and staticness agreement (not the mangled name shape alone) mirrors
    /// the compile-back planner's historical discriminator, so a same-named but type/static-mismatched
    /// or non-auto-property compiler-generated field is preserved (on reconstruction no auto-property
    /// re-creates it, so the raw field must stay declared).
    /// </summary>
    static bool IsAutoPropertyBackingField(
        MetadataReader reader,
        FieldDefinition field,
        string fieldName,
        Dictionary<string, AutoPropertyBackingField>? autoPropertyBackingFields,
        GenericContext context)
    {
        if (autoPropertyBackingFields is null
            || !autoPropertyBackingFields.TryGetValue(fieldName, out var descriptor))
        {
            return false;
        }

        if (!AttributeReader.HasAttribute(reader, field.GetCustomAttributes(), KnownAttributeNames.CompilerGeneratedAttribute))
            return false;

        if (((field.Attributes & FieldAttributes.Static) != 0) != descriptor.IsStatic)
            return false;

        return GuardedSignatureText.FieldText(reader, field, context).TryGetValue(out var fieldType)
            && fieldType == descriptor.PropertyType;
    }

    /// <summary>
    /// Whether a field name belongs to a type's declarable field surface based on its name alone.
    /// Compiler-generated (<c>&lt;...&gt;</c>) fields are excluded unless
    /// <paramref name="includeCompilerGenerated"/> is set; ordinary fields are surfaced. Backing
    /// fields (auto-property, field-like event) and an enum's <c>value__</c> slot carry additional
    /// positive-evidence checks applied by callers.
    /// </summary>
    static bool IsSurfaceableFieldName(string name, bool includeCompilerGenerated)
    {
        if (name.StartsWith('<'))
            return includeCompilerGenerated;
        return true;
    }

    /// <summary>
    /// The field handles that make up a type's declarable field surface: ordinary fields,
    /// excluding synthesized auto-property backing fields (positive <c>[CompilerGenerated]</c>
    /// evidence), an enum's storage slot (<c>value__</c>), and a field-like event's
    /// compiler-generated backing field. Compiler-generated fields (e.g. state-machine hoisted
    /// locals, display-class captures) are included only when
    /// <paramref name="includeCompilerGenerated"/> is set; non-public fields only when
    /// <paramref name="includeAll"/> is set. This is the single field-inclusion decision shared by
    /// API-surface extraction and compile-back reconstruction so both agree on which fields a type
    /// really has.
    /// </summary>
    public static List<FieldDefinitionHandle> SurfaceFieldHandles(
        MetadataReader reader,
        TypeDefinition typeDef,
        bool includeAll,
        bool includeCompilerGenerated)
    {
        bool isEnum = IsEnum(reader, typeDef);
        var context = GenericContext.ForType(reader, typeDef);
        var fieldLikeEventBackingFieldNames = FieldLikeEventBackingFieldNames(reader, typeDef);
        var autoPropertyBackingFields = AutoPropertyBackingFieldDescriptors(reader, typeDef, context);
        var handles = new List<FieldDefinitionHandle>();
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public && !includeAll)
                continue;

            string fieldName = reader.GetString(field.Name);
            if (isEnum && fieldName == "value__")
                continue; // An enum's storage slot is not a declarable field member
            if (!IsSurfaceableFieldName(fieldName, includeCompilerGenerated))
                continue;
            if (IsAutoPropertyBackingField(reader, field, fieldName, autoPropertyBackingFields, context))
                continue; // Skip a synthesized auto-property backing field (re-synthesized on reconstruction)
            if (IsFieldLikeEventBackingField(reader, field, fieldName, fieldLikeEventBackingFieldNames))
                continue;

            handles.Add(fieldHandle);
        }

        return handles;
    }

    /// <summary>
    /// Populates DerivedTypes for a specific type by scanning all types in the surface.
    /// </summary>
    public static void PopulateDerivedTypes(ApiSurface surface, ApiType targetType)
    {
        var fullName = string.IsNullOrEmpty(targetType.Namespace)
            ? targetType.Name
            : $"{targetType.Namespace}.{targetType.Name}";

        List<string> derivedTypes = [];

        foreach (var type in surface.Types)
        {
            if (type == targetType)
                continue;

            // Check if this type's base is our target
            if (type.BaseType == fullName)
            {
                var derivedFullName = string.IsNullOrEmpty(type.Namespace)
                    ? type.Name
                    : $"{type.Namespace}.{type.Name}";
                derivedTypes.Add(derivedFullName);
            }

            // Check if this type implements our target (if target is an interface)
            if (targetType.Kind == "interface" && type.Interfaces != null)
            {
                if (type.Interfaces.Contains(fullName))
                {
                    var derivedFullName = string.IsNullOrEmpty(type.Namespace)
                        ? type.Name
                        : $"{type.Namespace}.{type.Name}";
                    if (!derivedTypes.Contains(derivedFullName))
                        derivedTypes.Add(derivedFullName);
                }
            }
        }

        if (derivedTypes.Count > 0)
        {
            derivedTypes.Sort(StringComparer.Ordinal);
            targetType.DerivedTypes = derivedTypes;
        }
    }

    private static (string Text, ApiSignature Model, bool IsDegraded) GetMethodSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        byte typeNullableContext)
    {
        string name = reader.GetString(method.Name);
        var context = GenericContext.ForMethod(reader, typeDef, method);
        var treeSignature = GuardedProviderDecode.Method(
            reader,
            method,
            TypeNodeProvider.Instance,
            context,
            (TypeNode)new DegradedTypeNode());

        // Determine the effective nullable default: method overrides type
        byte methodContext = NullabilityReader.GetNullableContext(reader, method.GetCustomAttributes());
        byte nullableDefault = methodContext != 0 ? methodContext : typeNullableContext;

        // Apply nullability to return type
        var paramHandles = method.GetParameters();
        var returnBytes = NullabilityReader.GetParameterNullableBytes(reader, paramHandles, 0);
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(returnBytes, ref pos, nullableDefault);
        var returnDynamicFlags = DynamicReader.GetParameterDynamicFlags(reader, paramHandles, 0);
        pos = 0;
        treeSignature.ReturnType.ApplyDynamic(returnDynamicFlags, ref pos);
        treeSignature.ReturnType.ApplyTupleNames(
            TupleElementNamesReader.GetParameterTupleElementNames(reader, paramHandles, 0));

        // Build parameter list with nullability
        var paramTypes = treeSignature.ParameterTypes;

        List<string> parameters = [];
        List<ApiParameter> parameterModels = [];
        for (int i = 0; i < paramTypes.Length; i++)
        {
            // Apply nullability to this parameter's type tree
            var paramBytes = NullabilityReader.GetParameterNullableBytes(reader, paramHandles, i + 1);
            pos = 0;
            paramTypes[i].ApplyNullability(paramBytes, ref pos, nullableDefault);
            var paramDynamicFlags = DynamicReader.GetParameterDynamicFlags(reader, paramHandles, i + 1);
            pos = 0;
            paramTypes[i].ApplyDynamic(paramDynamicFlags, ref pos);
            paramTypes[i].ApplyTupleNames(
                TupleElementNamesReader.GetParameterTupleElementNames(reader, paramHandles, i + 1));
            string type = paramTypes[i].Render();
            string canonicalType = paramTypes[i].RenderCanonical();

            // Parameter handles may include return parameter at SequenceNumber 0
            // Actual parameters have SequenceNumber 1, 2, 3...
            var (paramName, isParams, refKind, hasDefault, defaultValue, attributes) = GetParameterInfo(reader, paramHandles, i + 1);
            paramName ??= $"arg{i}";

            var isByRef = type.StartsWith("ref ", StringComparison.Ordinal);
            if (isByRef)
            {
                type = type["ref ".Length..];
                canonicalType = canonicalType["ref ".Length..];
                refKind ??= "ref";
            }
            else
            {
                refKind = null;
            }

            var modifier = isParams ? "params" : refKind;
            var paramStr = FormatParameter(
                reader,
                type,
                paramName,
                modifier,
                hasDefault,
                defaultValue,
                AcceptsNullDefault(paramTypes[i]));

            parameters.Add(paramStr);
            parameterModels.Add(new ApiParameter
            {
                Attributes = attributes,
                Name = paramName,
                Type = type,
                CanonicalType = canonicalType,
                Modifier = modifier,
                HasDefault = hasDefault,
                DefaultValueText = DefaultValueText(reader, defaultValue, type, hasDefault, AcceptsNullDefault(paramTypes[i]))
            });
        }

        string paramStr2 = string.Join(", ", parameters);
        var returnType = FormatMethodReturnType(reader, treeSignature.ReturnType, paramHandles);
        var canonicalReturnType = FormatCanonicalMethodReturnType(reader, treeSignature.ReturnType, paramHandles);
        var returnAttributes = ReturnParameterAttributes(reader, paramHandles);
        var methodTypeParameters = GenericParameters(reader, method.GetGenericParameters(), context, nullableDefault, includeVariance: false);
        var methodName = context.MethodParameters.Count > 0
            ? $"{name}<{string.Join(", ", methodTypeParameters.Select(parameter => parameter.Name))}>"
            : name;
        return ($"{returnType} {methodName}({paramStr2})", new ApiSignature
        {
            ReturnType = returnType,
            CanonicalReturnType = canonicalReturnType,
            ReturnAttributes = returnAttributes,
            MemberName = methodName,
            TypeParameters = methodTypeParameters,
            Parameters = parameterModels
        }, treeSignature.ReturnType.IsDegraded
            || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));
    }

    private static List<string> ReturnParameterAttributes(MetadataReader reader, ParameterHandleCollection handles)
    {
        foreach (var handle in handles)
        {
            if (reader.GetParameter(handle).SequenceNumber == 0)
                return AttributeReader.RenderParameterAttributes(reader, handle);
        }

        return [];
    }

    private static List<string> RenderMemberAttributes(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => AttributeReader.RenderAttributes(
            reader,
            attributes,
            skipAttribute: static name => name == "System.ObsoleteAttribute",
            qualifyNames: true);

    private static string FormatMethodReturnType(MetadataReader reader, TypeNode returnType, ParameterHandleCollection paramHandles)
    {
        var rendered = returnType.Render();
        if (!rendered.StartsWith("ref ", StringComparison.Ordinal)
            || !IsReadOnlyByRefReturn(reader, returnType, paramHandles))
        {
            return rendered;
        }

        return $"ref readonly {rendered["ref ".Length..]}";
    }

    /// <summary>
    /// Canonical (tuple-erased) counterpart to <see cref="FormatMethodReturnType"/>. Mirrors
    /// its <c>ref readonly</c> synthesis so the canonical return spelling preserves by-ref
    /// return modifiers used by member identity, differing from the display spelling only in
    /// tuple rendering.
    /// </summary>
    private static string FormatCanonicalMethodReturnType(MetadataReader reader, TypeNode returnType, ParameterHandleCollection paramHandles)
    {
        var rendered = returnType.RenderCanonical();
        if (!rendered.StartsWith("ref ", StringComparison.Ordinal)
            || !IsReadOnlyByRefReturn(reader, returnType, paramHandles))
        {
            return rendered;
        }

        return $"ref readonly {rendered["ref ".Length..]}";
    }

    private static bool IsReadOnlyByRefReturn(MetadataReader reader, TypeNode returnType, ParameterHandleCollection paramHandles)
    {
        foreach (var handle in paramHandles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber == 0 && HasReadOnlyByRefAttribute(reader, parameter.GetCustomAttributes()))
                return true;
        }

        return returnType.HasRequiredModifier("System.Runtime.CompilerServices", "IsReadOnlyAttribute")
            || returnType.HasRequiredModifier("System.Runtime.CompilerServices", "RequiresLocationAttribute")
            || returnType.HasRequiredModifier("System.Runtime.InteropServices", "InAttribute");
    }

    private static bool HasReadOnlyByRefAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
        => AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.IsReadOnlyAttribute)
            || AttributeReader.HasAttribute(reader, attributes, "System.Runtime.CompilerServices.RequiresLocationAttribute");

    private static (string? name, bool isParams, string? refKind, bool hasDefault, object? defaultValue, List<string> attributes) GetParameterInfo(
        MetadataReader reader, ParameterHandleCollection handles, int sequenceNumber)
    {
        foreach (var handle in handles)
        {
            var param = reader.GetParameter(handle);
            if (param.SequenceNumber == sequenceNumber)
            {
                string name = reader.GetString(param.Name);
                var attributes = param.GetCustomAttributes();
                bool isParams = AttributeReader.HasAttribute(reader, attributes, "System.ParamArrayAttribute")
                    || AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.ParamCollectionAttribute);
                var renderedAttributes = AttributeReader.RenderParameterAttributes(reader, handle);
                string? refKind = (param.Attributes & System.Reflection.ParameterAttributes.Out) != 0
                    ? "out"
                    : (param.Attributes & System.Reflection.ParameterAttributes.In) != 0
                        ? "in"
                        : null;

                bool hasDefault = (param.Attributes & System.Reflection.ParameterAttributes.HasDefault) != 0;
                object? defaultValue = null;

                if (TryReadAttributedParameterDefault(reader, attributes, out var attributedDefault))
                {
                    hasDefault = true;
                    defaultValue = attributedDefault;
                }
                else if (hasDefault)
                {
                    var constantHandle = param.GetDefaultValue();
                    if (!constantHandle.IsNil)
                    {
                        var constant = reader.GetConstant(constantHandle);
                        defaultValue = ReadConstantValue(reader, constant);
                    }
                }

                return (name, isParams, refKind, hasDefault, defaultValue, renderedAttributes);
            }
        }

        return (null, false, null, false, null, []);
    }

    private sealed record DateTimeConstantDefault(long Ticks);

    private static bool TryReadAttributedParameterDefault(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        out object? defaultValue)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var attributeTypeName = AttributeReader.GetAttributeTypeName(reader, attribute.Constructor);
            if (attributeTypeName == KnownAttributeNames.DecimalConstantAttribute
                && TryReadDecimalConstantAttribute(reader, attribute, out var decimalValue))
            {
                defaultValue = decimalValue;
                return true;
            }

            if (attributeTypeName == KnownAttributeNames.DateTimeConstantAttribute
                && TryReadDateTimeConstantAttribute(reader, attribute, out var ticks))
            {
                defaultValue = new DateTimeConstantDefault(ticks);
                return true;
            }
        }

        defaultValue = null;
        return false;
    }

    private static bool TryReadDecimalConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out decimal value)
    {
        if (AttributeDecoder.TryDecode(reader, attribute) is not { } decoded
            || decoded.FixedArguments.Length != 5
            || decoded.FixedArguments[0].Value is not byte scale
            || decoded.FixedArguments[1].Value is not byte sign
            || !TryGetUInt32(decoded.FixedArguments[2].Value, out var hi)
            || !TryGetUInt32(decoded.FixedArguments[3].Value, out var mid)
            || !TryGetUInt32(decoded.FixedArguments[4].Value, out var low)
            || scale > 28
            || sign > 1)
        {
            value = default;
            return false;
        }

        value = new decimal(
            unchecked((int)low),
            unchecked((int)mid),
            unchecked((int)hi),
            sign != 0,
            scale);
        return true;
    }

    private static bool TryGetUInt32(object? value, out uint result)
    {
        switch (value)
        {
            case uint unsigned:
                result = unsigned;
                return true;
            case int signed:
                result = unchecked((uint)signed);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryReadDateTimeConstantAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out long ticks)
    {
        if (AttributeDecoder.TryDecode(reader, attribute) is { FixedArguments.Length: 1 } decoded
            && decoded.FixedArguments[0].Value is long value)
        {
            ticks = value;
            return true;
        }

        ticks = 0;
        return false;
    }

    private static object? ReadConstantValue(MetadataReader reader, Constant constant)
    {
        var blob = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Boolean => blob.ReadBoolean(),
            ConstantTypeCode.Char => blob.ReadChar(),
            ConstantTypeCode.SByte => blob.ReadSByte(),
            ConstantTypeCode.Byte => blob.ReadByte(),
            ConstantTypeCode.Int16 => blob.ReadInt16(),
            ConstantTypeCode.UInt16 => blob.ReadUInt16(),
            ConstantTypeCode.Int32 => blob.ReadInt32(),
            ConstantTypeCode.UInt32 => blob.ReadUInt32(),
            ConstantTypeCode.Int64 => blob.ReadInt64(),
            ConstantTypeCode.UInt64 => blob.ReadUInt64(),
            ConstantTypeCode.Single => blob.ReadSingle(),
            ConstantTypeCode.Double => blob.ReadDouble(),
            ConstantTypeCode.String => blob.ReadUTF16(blob.Length),
            ConstantTypeCode.NullReference => null,
            _ => null
        };
    }

    // `null` is a legal default only for a reference type or a Nullable<T> (a
    // value type that nonetheless accepts the `null` literal). A non-nullable
    // value type must spell its null constant `default`.
    private static bool AcceptsNullDefault(TypeNode node)
        => node.IsReferenceType
            || node.Render().StartsWith("System.Nullable<", StringComparison.Ordinal);

    private static string FormatDefaultValue(MetadataReader reader, object? value, string typeName, bool acceptsNullDefault)
    {
        // A null constant is `default(T)` for a non-nullable value-type parameter
        // (the only legal spelling — `T x = null` is CS1750), and a genuine `null`
        // for reference types and Nullable<T> (both accept `null` as a literal
        // default). value-vs-reference comes from the signature's element type
        // (ELEMENT_TYPE_VALUETYPE), already on the decoded type node.
        if (value == null)
            return acceptsNullDefault ? "null" : "default";

        if (TryFormatEnumDefaultValue(reader, value, typeName) is { } enumValue)
            return enumValue;

        if (!acceptsNullDefault
            && IsLikelyEnumDefaultType(typeName)
            && TryConvertEnumConstant(value, out var defaultValue))
        {
            return $"({typeName}){defaultValue.ToString(CultureInfo.InvariantCulture)}";
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            decimal d => FormatDecimalLiteral(d),
            string s => StringLiteral(s),
            char c => $"'{EscapeCharLiteral(c)}'",
            float f => f.ToString("G") + "f",
            double d => d.ToString("G"),
            _ => value.ToString() ?? "default"
        };
    }

    private static string? DefaultValueText(MetadataReader reader, object? value, string typeName, bool hasDefault, bool acceptsNullDefault)
    {
        if (!hasDefault || value is DateTimeConstantDefault)
            return null;
        return FormatDefaultValue(reader, value, typeName, acceptsNullDefault);
    }

    private static string EscapeCharLiteral(char c) => c switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\0' => "\\0",
        '\a' => "\\a",
        '\b' => "\\b",
        '\f' => "\\f",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\v' => "\\v",
        '\u0085' or '\u2028' or '\u2029' => $"\\u{(int)c:x4}",
        _ when char.IsControl(c) => $"\\u{(int)c:x4}",
        _ => c.ToString()
    };

    private static string FormatParameter(
        MetadataReader reader,
        string type,
        string name,
        string? modifier,
        bool hasDefault,
        object? defaultValue,
        bool acceptsNullDefault)
    {
        var escapedName = EscapeIdentifier(name);
        var parameter = modifier is null ? $"{type} {escapedName}" : $"{modifier} {type} {escapedName}";
        if (!hasDefault)
            return parameter;

        if (defaultValue is DateTimeConstantDefault dateTime)
        {
            var ticks = FormatInt64Literal(dateTime.Ticks);
            return $"[{OptionalAttributeName}, {DateTimeConstantAttributeName}({ticks})] {parameter}";
        }

        return $"{parameter} = {FormatDefaultValue(reader, defaultValue, type, acceptsNullDefault)}";
    }

    private static string EscapeIdentifier(string name)
        => CSharpKeywords.RequiresDeclarationEscape(name) ? "@" + name : name;

    private static string FormatDecimalLiteral(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture) + "m";

    private static string FormatInt64Literal(long value)
    {
        long minValue = long.MaxValue;
        minValue = -minValue - 1;
        return value == minValue
            ? "long.MinValue"
            : value.ToString(CultureInfo.InvariantCulture) + "L";
    }

    private static string StringLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\0' => "\\0",
                '\a' => "\\a",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\v' => "\\v",
                _ when char.IsControl(c) => $"\\u{(int)c:X4}",
                _ => c.ToString()
            });
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string? TryFormatEnumDefaultValue(MetadataReader reader, object value, string typeName)
    {
        if (!TryConvertEnumConstant(value, out var defaultValue))
            return null;

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            try
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                if (!IsEnum(reader, typeDef))
                    continue;

                if (TypeResolver.ResolveTypeName(reader, typeHandle)
                    is not MetadataTypeNameResult.Resolved resolvedEnumType)
                {
                    continue;
                }

                var enumTypeName = resolvedEnumType.Value;
                if (!string.Equals(typeName, enumTypeName, StringComparison.Ordinal))
                    continue;

                foreach (var fieldHandle in typeDef.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & FieldAttributes.Literal) == 0)
                        continue;
                    var constantHandle = field.GetDefaultValue();
                    if (constantHandle.IsNil)
                        continue;
                    var constant = reader.GetConstant(constantHandle);
                    if (TryReadEnumConstant(reader, constant, out var memberValue)
                        && memberValue == defaultValue)
                    {
                        return $"{typeName}.{reader.GetString(field.Name)}";
                    }
                }

                return $"({typeName}){defaultValue.ToString(CultureInfo.InvariantCulture)}";
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                continue;
            }
        }

        return null;
    }

    private static bool IsLikelyEnumDefaultType(string typeName)
        => typeName is not ("bool" or "char" or "sbyte" or "byte" or "short" or "ushort"
            or "int" or "uint" or "long" or "ulong" or "float" or "double" or "decimal"
            or "System.Boolean" or "System.Char" or "System.SByte" or "System.Byte"
            or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32"
            or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double"
            or "System.Decimal" or "System.DateTime");

    // Base types, interfaces, and events resolve to a display string via the
    // string-based TypeResolver, which has no DynamicAttribute context. Only a
    // generic instantiation (a TypeSpecification) can carry `dynamic`, so when
    // one does, re-decode it through the TypeNode tree and apply the flags. Every
    // other case (non-TypeSpec, or no DynamicAttribute) returns the string result
    // unchanged, so this never alters non-dynamic output.
    private static string ApplyDynamicView(
        MetadataReader reader,
        EntityHandle typeHandle,
        CustomAttributeHandleCollection attributes,
        GenericContext context,
        string fallback)
    {
        if (typeHandle.Kind != HandleKind.TypeSpecification)
            return fallback;
        if (DynamicReader.GetDynamicFlags(reader, attributes) is not { } flags)
            return fallback;
        var node = GuardedProviderDecode.TypeSpec(
            reader,
            (TypeSpecificationHandle)typeHandle,
            TypeNodeProvider.Instance,
            context,
            (TypeNode)new DegradedTypeNode());
        // A rejected/degraded TypeSpec renders as a bare "object"/"dynamic", which would
        // obliterate the fully resolved string fallback. Keep failure visible: trust the
        // string resolver rather than silently collapsing the type.
        if (node.IsDegraded)
            return fallback;
        int position = 0;
        node.ApplyDynamic(flags, ref position);
        return node.Render();
    }

    private static string ResolveRequiredTypeName(
        MetadataReader reader,
        EntityHandle handle,
        GenericContext? context = null)
        => TypeResolver.ResolveTypeName(reader, handle, context) switch
        {
            MetadataTypeNameResult.Resolved resolved => resolved.Value,
            MetadataTypeNameResult.Rejected rejected =>
                throw new MetadataRowRejectedException(
                    "type name",
                    rejected.Failure),
            MetadataTypeNameResult.Absent =>
                throw new MetadataRowRejectedException(
                    "type name",
                    MetadataTypeNameFailure.ForMechanism(
                        MetadataTypeNameFailureMechanism.Metadata,
                        handle,
                        "The metadata type name is absent.")),
            _ => throw new InvalidOperationException(
                "Unknown metadata type-name result."),
        };

    private static void AddInspectionFailure(
        ApiSurface surface,
        string operation,
        EntityHandle subject,
        MetadataTypeNameFailure failure)
        => surface.InspectionFailures.Add(new ApiSurfaceInspectionFailure(
            operation,
            failure.SubjectToken ?? MetadataTokens.GetToken(subject),
            failure.Mechanism,
            failure.Kind,
            failure.Detail));

    private static bool IsEnum(MetadataReader reader, TypeDefinition typeDef)
        => !typeDef.BaseType.IsNil
            && TypeResolver.ResolveTypeName(reader, typeDef.BaseType)
                is MetadataTypeNameResult.Resolved { Value: "System.Enum" };

    private sealed class MetadataRowRejectedException
        : InvalidOperationException
    {
        public MetadataRowRejectedException(
            string operation,
            MetadataTypeNameFailure failure)
            : base(
                $"Metadata row rejected during {operation} "
                + $"({failure.Mechanism}/{failure.Kind}): {failure.Detail}")
        {
            Operation = operation;
            Failure = failure;
        }

        public string Operation { get; }
        public MetadataTypeNameFailure Failure { get; }
    }

    private static bool TryReadEnumConstant(MetadataReader reader, Constant constant, out decimal value)
    {
        var blob = reader.GetBlobReader(constant.Value);
        switch (constant.TypeCode)
        {
            case ConstantTypeCode.SByte:
                return TryConvertEnumConstant(blob.ReadSByte(), out value);
            case ConstantTypeCode.Byte:
                return TryConvertEnumConstant(blob.ReadByte(), out value);
            case ConstantTypeCode.Int16:
                return TryConvertEnumConstant(blob.ReadInt16(), out value);
            case ConstantTypeCode.UInt16:
                return TryConvertEnumConstant(blob.ReadUInt16(), out value);
            case ConstantTypeCode.Int32:
                return TryConvertEnumConstant(blob.ReadInt32(), out value);
            case ConstantTypeCode.UInt32:
                return TryConvertEnumConstant(blob.ReadUInt32(), out value);
            case ConstantTypeCode.Int64:
                return TryConvertEnumConstant(blob.ReadInt64(), out value);
            case ConstantTypeCode.UInt64:
                return TryConvertEnumConstant(blob.ReadUInt64(), out value);
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryConvertEnumConstant(object value, out decimal converted)
    {
        switch (value)
        {
            case sbyte v:
                converted = v;
                return true;
            case byte v:
                converted = v;
                return true;
            case short v:
                converted = v;
                return true;
            case ushort v:
                converted = v;
                return true;
            case int v:
                converted = v;
                return true;
            case uint v:
                converted = v;
                return true;
            case long v:
                converted = v;
                return true;
            case ulong v:
                converted = v;
                return true;
            default:
                converted = 0;
                return false;
        }
    }

    private static (string Text, ApiSignature Model, bool IsDegraded) GetPropertySignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        PropertyDefinition prop,
        PropertyAccessors accessors,
        byte typeNullableContext,
        bool includeAll = false)
    {
        string name = reader.GetString(prop.Name);
        var context = GenericContext.ForType(reader, typeDef);
        var treeSignature = GuardedProviderDecode.Property(
            reader,
            prop,
            TypeNodeProvider.Instance,
            context,
            (TypeNode)new DegradedTypeNode());

        // Apply nullability to the property type
        var propBytes = NullabilityReader.GetNullableBytes(reader, prop.GetCustomAttributes());
        int pos = 0;
        treeSignature.ReturnType.ApplyNullability(propBytes, ref pos, typeNullableContext);
        var propDynamicFlags = DynamicReader.GetDynamicFlags(reader, prop.GetCustomAttributes());
        pos = 0;
        treeSignature.ReturnType.ApplyDynamic(propDynamicFlags, ref pos);
        treeSignature.ReturnType.ApplyTupleNames(
            TupleElementNamesReader.GetTupleElementNames(reader, prop.GetCustomAttributes()));

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
                    ReturnAttributes = ReturnParameterAttributes(reader, reader.GetMethodDefinition(accessors.Getter).GetParameters())
                });
            if (hasSetter)
                accessorModels.Add(new ApiAccessor { Kind = "set", Accessibility = AccessorAccessibility(setterAccess, Math.Max((int)getterAccess, (int)setterAccess)) });
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
                accessorModels.Add(new ApiAccessor { Kind = "get", ReturnAttributes = ReturnParameterAttributes(reader, reader.GetMethodDefinition(accessors.Getter).GetParameters()) });
                accessorModels.Add(new ApiAccessor { Kind = "set" });
            }
            else if (hasPublicGetter && hasSetter)
            {
                accessorStr = "{ get; private set; }";
                accessorModels.Add(new ApiAccessor { Kind = "get", ReturnAttributes = ReturnParameterAttributes(reader, reader.GetMethodDefinition(accessors.Getter).GetParameters()) });
                accessorModels.Add(new ApiAccessor { Kind = "set", Accessibility = "private" });
            }
            else if (hasPublicGetter)
            {
                accessorStr = "{ get; }";
                accessorModels.Add(new ApiAccessor { Kind = "get", ReturnAttributes = ReturnParameterAttributes(reader, reader.GetMethodDefinition(accessors.Getter).GetParameters()) });
            }
            else if (hasPublicSetter)
            {
                accessorStr = "{ set; }";
                accessorModels.Add(new ApiAccessor { Kind = "set" });
            }
            else
            {
                accessorStr = "{ get; }"; // Fallback
                accessorModels.Add(new ApiAccessor { Kind = "get" });
            }
        }

        var requiredPrefix = AttributeReader.HasRequiredMemberAttribute(reader, prop.GetCustomAttributes())
            ? "required "
            : "";
        var isRequired = requiredPrefix.Length > 0;

        var paramHandles = hasGetter
            ? reader.GetMethodDefinition(accessors.Getter).GetParameters()
            : hasSetter
                ? reader.GetMethodDefinition(accessors.Setter).GetParameters()
                : default;
        var paramTypes = treeSignature.ParameterTypes;
        List<string> indexerParameters = [];
        List<ApiParameter> parameterModels = [];
        for (var i = 0; i < paramTypes.Length; i++)
        {
            var paramBytes = NullabilityReader.GetParameterNullableBytes(reader, paramHandles, i + 1);
            pos = 0;
            paramTypes[i].ApplyNullability(paramBytes, ref pos, typeNullableContext);
            var paramDynamicFlags = DynamicReader.GetParameterDynamicFlags(reader, paramHandles, i + 1);
            pos = 0;
            paramTypes[i].ApplyDynamic(paramDynamicFlags, ref pos);
            paramTypes[i].ApplyTupleNames(
                TupleElementNamesReader.GetParameterTupleElementNames(reader, paramHandles, i + 1));
            var paramType = paramTypes[i].Render();
            var canonicalParamType = paramTypes[i].RenderCanonical();
            var (paramName, isParams, refKind, hasDefault, defaultValue, attributes) = GetParameterInfo(reader, paramHandles, i + 1);
            paramName ??= $"arg{i}";

            var isByRef = paramType.StartsWith("ref ", StringComparison.Ordinal);
            if (isByRef)
            {
                paramType = paramType["ref ".Length..];
                canonicalParamType = canonicalParamType["ref ".Length..];
                refKind ??= "ref";
            }
            else
            {
                refKind = null;
            }

            var modifier = isParams ? "params" : refKind;
            var parameter = FormatParameter(
                reader,
                paramType,
                paramName,
                modifier,
                hasDefault,
                defaultValue,
                AcceptsNullDefault(paramTypes[i]));
            indexerParameters.Add(parameter);
            parameterModels.Add(new ApiParameter
            {
                Attributes = attributes,
                Name = paramName,
                Type = paramType,
                CanonicalType = canonicalParamType,
                Modifier = modifier,
                HasDefault = hasDefault,
                DefaultValueText = DefaultValueText(reader, defaultValue, paramType, hasDefault, AcceptsNullDefault(paramTypes[i]))
            });
        }

        var returnType = FormatMethodReturnType(reader, treeSignature.ReturnType, paramHandles);
        var canonicalReturnType = FormatCanonicalMethodReturnType(reader, treeSignature.ReturnType, paramHandles);
        var model = new ApiSignature
        {
            ReturnType = returnType,
            CanonicalReturnType = canonicalReturnType,
            MemberName = indexerParameters.Count > 0 ? "this[]" : name,
            IsRequired = isRequired,
            Parameters = parameterModels,
            Accessors = accessorModels
        };

        if (indexerParameters.Count > 0)
            return (
                $"{requiredPrefix}{returnType} this[{string.Join(", ", indexerParameters)}] {accessorStr}",
                model,
                treeSignature.ReturnType.IsDegraded
                    || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));

        return (
            $"{requiredPrefix}{returnType} {name} {accessorStr}",
            model,
            treeSignature.ReturnType.IsDegraded
                || treeSignature.ParameterTypes.Any(parameter => parameter.IsDegraded));
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

    /// <summary>
    /// Gets the first parameter type for extension methods.
    /// </summary>
    private static string? GetFirstParameterType(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
    {
        var context = GenericContext.ForMethod(reader, typeDef, method);
        return GuardedSignatureText.MethodText(reader, method, context)
            .TryGetValue(out var signature)
                && signature.ParameterTypes.Length > 0
                    ? signature.ParameterTypes[0]
                    : null;
    }

    /// <summary>
    /// Checks if a method signature contains unsafe constructs (pointers). This
    /// catches members whose signature renders a pointer; members declared
    /// <c>unsafe</c> with no pointer in the signature are detected separately via
    /// <see cref="AttributeReader.HasRequiresUnsafeAttribute"/>.
    /// </summary>
    private static bool HasUnsafeSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return false;

        // Check for pointer types (e.g., int*, void*, byte*)
        // and function pointers (delegate*)
        return signature.Contains('*');
    }

    /// <summary>
    /// Maps MethodAttributes access level to C# keyword. Returns null for public.
    /// </summary>
    private static string? GetAccessibility(MethodAttributes access) => access switch
    {
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        _ => null // Public
    };

    /// <summary>
    /// Maps FieldAttributes access level to C# keyword. Returns null for public.
    /// </summary>
    private static string? GetFieldAccessibility(FieldAttributes access) => access switch
    {
        FieldAttributes.Private => "private",
        FieldAttributes.FamANDAssem => "private protected",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected internal",
        _ => null // Public
    };
}
