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

    private static void AttachLocalExtensionMethods(
        ApiSurface surface,
        IReadOnlyDictionary<ApiMember, MetadataTypeDefinitionName> extensionReceiverDefinitions,
        ExtractionBudget? budget = null)
    {
        var targets = new Dictionary<MetadataTypeDefinitionName, ApiType>();
        var ambiguousTargets = new HashSet<MetadataTypeDefinitionName>();
        foreach (ApiType type in surface.Types)
        {
            if (type.DefinitionName is not { } definitionName
                || ambiguousTargets.Contains(definitionName))
            {
                continue;
            }

            if (!targets.TryAdd(definitionName, type))
            {
                targets.Remove(definitionName);
                ambiguousTargets.Add(definitionName);
            }
        }

        foreach (var declaringType in surface.Types)
        {
            var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var extension in declaringType.Members)
            {
                overloadCounts.TryGetValue(extension.Name, out int declaringOverloadIndex);
                declaringOverloadIndex++;
                overloadCounts[extension.Name] = declaringOverloadIndex;
                if (!extensionReceiverDefinitions.TryGetValue(
                        extension,
                        out MetadataTypeDefinitionName? targetName)
                    || !targets.TryGetValue(
                        targetName,
                        out ApiType? targetType))
                {
                    continue;
                }
                if (ReferenceEquals(targetType, declaringType))
                    continue;
                MetadataTypeDefinitionName declaringTypeDefinitionName =
                    declaringType.DefinitionName
                    ?? throw new InvalidOperationException(
                        "An extension declaration must retain exact Type identity before projection.");
                string declaringTypeCanonicalName =
                    ApiMemberIdentity.FormatTypeAnchorName(declaringType);
                if (targetType.Members.Any(member =>
                    member.Kind == "extension-method"
                    && string.Equals(
                        member.DeclaringTypeCanonicalName
                            ?? member.DeclaringType,
                        declaringTypeCanonicalName,
                        StringComparison.Ordinal)
                    && string.Equals(member.Name, extension.Name, StringComparison.Ordinal)
                    && string.Equals(member.Signature, extension.Signature, StringComparison.Ordinal)))
                {
                    continue;
                }

                var attached = new ApiMember
                {
                    Name = extension.Name,
                    Kind = "extension-method",
                    ReturnType = extension.ReturnType,
                    Signature = extension.Signature,
                    SignatureModel = extension.SignatureModel,
                    SignatureDecodeStatus = extension.SignatureDecodeStatus,
                    MethodSemantics = extension.MethodSemantics,
                    MetadataToken = extension.MetadataToken,
                    GenericArity = extension.GenericArity,
                    IsStatic = extension.IsStatic,
                    IsVirtual = extension.IsVirtual,
                    IsAbstract = extension.IsAbstract,
                    IsOverride = extension.IsOverride,
                    IsSealed = extension.IsSealed,
                    IsUnsafe = extension.IsUnsafe,
                    MemorySafety = extension.MemorySafety,
                    MethodImplementation = extension.MethodImplementation,
                    HasMethodBody = extension.HasMethodBody,
                    IsExtension = true,
                    ExtendedType = extension.ExtendedType,
                    DeclaringType = declaringType.FullName,
                    DeclaringTypeCanonicalName =
                        declaringTypeCanonicalName,
                    DeclaringTypeDefinitionName =
                        declaringTypeDefinitionName,
                    DeclaringOverloadIndex = declaringOverloadIndex,
                    IsObsolete = extension.IsObsolete,
                    ObsoleteMessage = extension.ObsoleteMessage,
                    ObsoleteIsError = extension.ObsoleteIsError,
                    Documentation = extension.Documentation
                };
                budget?.RetainAttachedMember(attached);
                targetType.Members.Add(attached);
            }
        }
    }

    /// <summary>
    /// Names of a type's field-like events. A C# field-like event's compiler-generated backing
    /// field is private, is itself marked <c>[CompilerGenerated]</c>, and shares the event's exact
    /// (unmangled) name. Only events whose adder is <c>[CompilerGenerated]</c> (i.e. genuinely
    /// field-like) contribute a name; hand-authored or non-C# accessors are excluded so a
    /// legitimate same-named field is not suppressed.
    /// </summary>
    static HashSet<string>? FieldLikeEventBackingFieldNames(
        MetadataReader reader,
        TypeDefinition typeDef,
        Action<int>? beforeDecodeWork = null)
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
                    KnownAttributeNames.CompilerGeneratedAttribute,
                    beforeDecodeWork))
            {
                continue;
            }

            (names ??= new HashSet<string>(StringComparer.Ordinal)).Add(
                DecodeString(reader, eventDef.Name, beforeDecodeWork));
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
        HashSet<string>? fieldLikeEventBackingFieldNames,
        Action<int>? beforeDecodeWork = null)
        => (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private
           && fieldLikeEventBackingFieldNames?.Contains(fieldName) == true
           && AttributeReader.HasAttribute(
               reader,
               field.GetCustomAttributes(),
               KnownAttributeNames.CompilerGeneratedAttribute,
               beforeDecodeWork);

    /// <summary>
    /// A declared auto-property's backing-field descriptor: the property name, decoded return type,
    /// and whether its accessors are static. A genuine backing field must agree with the latter two,
    /// so a merely same-named compiler-generated field of a different type or staticness is not
    /// folded.
    /// </summary>
    readonly record struct AutoPropertyBackingField(
        int PropertyToken,
        string PropertyName,
        string PropertyType,
        bool IsStatic);

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
        GenericContext context,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        Dictionary<string, AutoPropertyBackingField>? descriptors = null;
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            string propertyName = DecodeString(
                reader,
                property.Name,
                beforeDecodeWork);
            if (propertyName.Contains('<', StringComparison.Ordinal)
                || propertyName.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryGetAutoPropertyAccessorStaticness(
                    reader,
                    property.GetAccessors(),
                    out bool isStatic,
                    beforeDecodeWork))
                continue; // Not an auto-property: no [CompilerGenerated] accessor.

            string? propertyType;
            if (beforeDecodeWork is null)
            {
                if (!GuardedSignatureText.PropertyText(reader, property, context)
                        .TryGetValue(out var propertySignature))
                {
                    continue; // Undecodable property signature: cannot prove a type match.
                }
                propertyType = propertySignature.ReturnType;
            }
            else
            {
                TypeNodeProvider provider =
                    new(beforeRetainText, beforeDecodeWork);
                MethodSignature<TypeNode> signature =
                    GuardedProviderDecode.Property(
                        reader,
                        property,
                        provider,
                        context,
                        (TypeNode)new DegradedTypeNode());
                if (signature.ReturnType.IsDegraded)
                    continue;
                propertyType = signature.ReturnType.Render();
            }

            (descriptors ??= new Dictionary<string, AutoPropertyBackingField>(StringComparer.Ordinal))
                [$"<{propertyName}{GeneratedNameGrammar.BackingFieldSuffix}"]
                    = new AutoPropertyBackingField(
                        MetadataTokens.GetToken(propertyHandle),
                        propertyName,
                        propertyType,
                        isStatic);
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
        out bool isStatic,
        Action<int>? beforeDecodeWork = null)
    {
        if (!accessors.Getter.IsNil)
        {
            var getter = reader.GetMethodDefinition(accessors.Getter);
            if (AttributeReader.HasAttribute(
                    reader,
                    getter.GetCustomAttributes(),
                    KnownAttributeNames.CompilerGeneratedAttribute,
                    beforeDecodeWork))
            {
                isStatic = (getter.Attributes & MethodAttributes.Static) != 0;
                return true;
            }
        }

        if (!accessors.Setter.IsNil)
        {
            var setter = reader.GetMethodDefinition(accessors.Setter);
            if (AttributeReader.HasAttribute(
                    reader,
                    setter.GetCustomAttributes(),
                    KnownAttributeNames.CompilerGeneratedAttribute,
                    beforeDecodeWork))
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
        GenericContext context,
        Action<string>? beforeRetainText = null,
        Action<int>? beforeDecodeWork = null)
    {
        if (autoPropertyBackingFields is null
            || !autoPropertyBackingFields.TryGetValue(fieldName, out var descriptor))
        {
            return false;
        }

        if (!AttributeReader.HasAttribute(
                reader,
                field.GetCustomAttributes(),
                KnownAttributeNames.CompilerGeneratedAttribute,
                beforeDecodeWork))
            return false;

        if (((field.Attributes & FieldAttributes.Static) != 0) != descriptor.IsStatic)
            return false;

        if (beforeDecodeWork is null)
        {
            return GuardedSignatureText.FieldText(reader, field, context)
                    .TryGetValue(out var fieldType)
                && fieldType == descriptor.PropertyType;
        }

        TypeNode node = GuardedProviderDecode.Field(
            reader,
            field,
            new TypeNodeProvider(beforeRetainText, beforeDecodeWork),
            context,
            new DegradedTypeNode());
        return !node.IsDegraded && node.Render() == descriptor.PropertyType;
    }

    static ImmutableArray<ApiMemberMemorySafetyFacts> ReadAccessorMemorySafety(
        MetadataReader reader,
        MemorySafetyMetadataIndex index,
        Guid moduleVersionId,
        MethodDefinitionHandle[] handles)
        => [.. handles.Where(handle => !handle.IsNil).Distinct()
            .Select(handle => ApiMemorySafetyFacts.Read(
                reader, index, moduleVersionId, handle))];

    static Dictionary<int, ApiBackingStorageAssociation> ReadBackingStorageAssociations(
        MetadataReader reader,
        TypeDefinition type,
        GenericContext context,
        Guid moduleVersionId,
        Dictionary<string, AutoPropertyBackingField>? properties,
        HashSet<string>? eventNames,
        Action<string>? beforeRetainText,
        Action<int>? beforeDecodeWork)
    {
        var results = new Dictionary<int, ApiBackingStorageAssociation>();
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        var ambiguousPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propertyHandle in type.GetProperties())
        {
            string name = DecodeString(
                reader, reader.GetPropertyDefinition(propertyHandle).Name, beforeDecodeWork);
            if (!propertyNames.Add(name))
                ambiguousPropertyNames.Add(name);
            results.Add(
                MetadataTokens.GetToken(propertyHandle),
                Unknown(ApiBackingStorageConvention.AutoProperty));
        }
        var events = new Dictionary<string, EventDefinitionHandle>(StringComparer.Ordinal);
        foreach (var eventHandle in type.GetEvents())
        {
            var @event = reader.GetEventDefinition(eventHandle);
            string name = DecodeString(reader, @event.Name, beforeDecodeWork);
            if (!events.TryAdd(name, eventHandle))
                events[name] = default;
            results.Add(
                MetadataTokens.GetToken(eventHandle),
                Unknown(ApiBackingStorageConvention.FieldLikeEvent));
        }
        if (results.Count == 0)
            return results;

        var fields = new Dictionary<string, List<FieldDefinitionHandle>>(StringComparer.Ordinal);
        foreach (var fieldHandle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            string name = DecodeString(reader, field.Name, beforeDecodeWork);
            if (!fields.TryGetValue(name, out var sameNamedFields))
                fields.Add(name, sameNamedFields = []);
            sameNamedFields.Add(fieldHandle);
        }

        if (properties is not null)
        {
            foreach (var (name, descriptor) in properties)
            {
                if (ambiguousPropertyNames.Contains(descriptor.PropertyName))
                    continue;
                results[descriptor.PropertyToken] = Match(
                    name,
                    ApiBackingStorageConvention.AutoProperty,
                    field =>
                    {
                        if (((field.Attributes & FieldAttributes.Static) != 0) != descriptor.IsStatic
                            || !AttributeReader.HasAttribute(
                                reader, field.GetCustomAttributes(),
                                KnownAttributeNames.CompilerGeneratedAttribute, beforeDecodeWork))
                        {
                            return false;
                        }
                        return MatchBackingType(
                            field, MetadataTokens.EntityHandle(descriptor.PropertyToken));
                    });
            }
        }
        foreach (var (name, eventHandle) in events)
        {
            if (eventHandle.IsNil || eventNames?.Contains(name) != true)
                continue;
            var @event = reader.GetEventDefinition(eventHandle);
            var adder = reader.GetMethodDefinition(@event.GetAccessors().Adder);
            bool isStatic = (adder.Attributes & MethodAttributes.Static) != 0;
            results[MetadataTokens.GetToken(eventHandle)] = Match(
                name,
                ApiBackingStorageConvention.FieldLikeEvent,
                field =>
                {
                    if (((field.Attributes & FieldAttributes.Static) != 0) != isStatic
                        || !IsFieldLikeEventBackingField(
                            reader, field, name, eventNames, beforeDecodeWork))
                    {
                        return false;
                    }
                    return MatchBackingType(field, eventHandle);
                });
        }
        return results;

        ApiBackingStorageAssociation Unknown(ApiBackingStorageConvention convention) =>
            new(moduleVersionId, convention, ApiBackingStorageState.Unknown, []);

        bool? MatchBackingType(FieldDefinition field, EntityHandle declaration)
        {
            TypeNode node = GuardedProviderDecode.Field(
                reader, field,
                new TypeNodeProvider(beforeRetainText, beforeDecodeWork),
                context, (TypeNode)new DegradedTypeNode());
            if (node.IsDegraded)
                return null;

            // Exact encoding is sufficient within this module, including token scope,
            // generic positions and shape. Alternate encodings remain unproven.
            BlobReader fieldType = reader.GetBlobReader(field.Signature);
            beforeDecodeWork?.Invoke(fieldType.Length);
            if (fieldType.ReadSignatureHeader().Kind != SignatureKind.Field)
                return null;

            BlobReader declaredType;
            if (declaration.Kind == HandleKind.PropertyDefinition)
            {
                declaredType = reader.GetBlobReader(
                    reader.GetPropertyDefinition((PropertyDefinitionHandle)declaration).Signature);
                beforeDecodeWork?.Invoke(declaredType.Length);
                SignatureHeader header = declaredType.ReadSignatureHeader();
                if (header.Kind != SignatureKind.Property || header.IsGeneric
                    || declaredType.ReadCompressedInteger() != 0)
                {
                    return null;
                }
            }
            else
            {
                EntityHandle eventType = reader.GetEventDefinition(
                    (EventDefinitionHandle)declaration).Type;
                if (eventType.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference)
                {
                    return fieldType.ReadSignatureTypeCode() == SignatureTypeCode.TypeHandle
                        && fieldType.ReadTypeHandle() == eventType
                        && fieldType.RemainingBytes == 0;
                }
                if (eventType.Kind != HandleKind.TypeSpecification)
                    return null;
                declaredType = reader.GetBlobReader(
                    reader.GetTypeSpecification((TypeSpecificationHandle)eventType).Signature);
                beforeDecodeWork?.Invoke(declaredType.Length);
            }

            if (fieldType.RemainingBytes != declaredType.RemainingBytes)
                return false;
            while (fieldType.RemainingBytes > 0)
            {
                if (fieldType.ReadByte() != declaredType.ReadByte())
                    return false;
            }
            return true;
        }

        ApiBackingStorageAssociation Match(
            string name,
            ApiBackingStorageConvention convention,
            Func<FieldDefinition, bool?> matches)
        {
            if (!fields.TryGetValue(name, out var candidates))
                return Unknown(convention);
            var evidence = ImmutableArray.CreateBuilder<ApiBackingFieldEvidence>();
            bool incomplete = false;
            foreach (var candidate in candidates)
            {
                var field = reader.GetFieldDefinition(candidate);
                bool? match;
                try
                {
                    match = matches(field);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentException
                        or InvalidOperationException)
                {
                    match = null;
                }
                incomplete |= match is null;
                if (match == true)
                {
                    evidence.Add(new(
                        MetadataTokens.GetToken(candidate),
                        name,
                        (field.Attributes & FieldAttributes.Static) != 0));
                }
            }
            return new(
                moduleVersionId,
                convention,
                evidence.Count > 1
                    ? ApiBackingStorageState.Ambiguous
                    : evidence.Count == 1 && !incomplete
                        ? ApiBackingStorageState.Associated
                        : ApiBackingStorageState.Unknown,
                evidence.ToImmutable());
        }
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
        MetadataTypeDefinitionName? definitionName =
            targetType.DefinitionName;
        ApiAssemblyIdentity? assemblyIdentity =
            surface.AssemblyIdentity;

        List<string> derivedTypes = [];

        foreach (var type in surface.Types)
        {
            if (type == targetType)
                continue;

            bool isDerived = definitionName is not null
                && assemblyIdentity is not null
                    ? type.BaseTypeReference is
                        {
                            DefinitionName: { } baseDefinition,
                            Assembly: { } baseAssembly,
                        }
                        && baseDefinition.Equals(definitionName)
                        && baseAssembly.Equals(assemblyIdentity)
                    : type.BaseType == fullName;
            if (isDerived)
            {
                var derivedFullName = string.IsNullOrEmpty(type.Namespace)
                    ? type.Name
                    : $"{type.Namespace}.{type.Name}";
                derivedTypes.Add(derivedFullName);
            }

            if (targetType.Kind == "interface")
            {
                bool implements = definitionName is not null
                    && assemblyIdentity is not null
                        ? type.InterfaceReferences.Any(reference =>
                            reference.DefinitionName?.Equals(
                                definitionName) == true
                            && reference.Assembly.Equals(
                                assemblyIdentity))
                        : type.Interfaces.Contains(fullName);
                if (implements)
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

    sealed class ExtensionReceiverDefinitionProvider :
        ISignatureTypeProvider<MetadataTypeDefinitionName?, GenericContext?>
    {
        readonly bool primitivesAreLocal;

        ExtensionReceiverDefinitionProvider(bool primitivesAreLocal) =>
            this.primitivesAreLocal = primitivesAreLocal;

        public bool HasRejectedMetadata { get; private set; }

        public static ExtensionReceiverDefinitionProvider Create(
            bool primitivesAreLocal) =>
            new(primitivesAreLocal);

        public MetadataTypeDefinitionName? GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            MetadataTypeDefinitionNameReadResult result =
                MetadataTypeDefinitionNameReader.Read(reader, handle);
            if (result is MetadataTypeDefinitionNameReadResult.Read read)
                return read.Name;
            HasRejectedMetadata = true;
            return null;
        }

        public MetadataTypeDefinitionName? GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            Span<TypeReferenceHandle> rootToLeaf =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    rootToLeaf,
                    out _,
                    out EntityHandle terminal,
                    out _))
            {
                HasRejectedMetadata = true;
                return null;
            }
            if (terminal.Kind != HandleKind.ModuleDefinition)
                return null;

            MetadataTypeDefinitionNameReadResult result =
                MetadataTypeDefinitionNameReader.Read(reader, handle);
            if (result is MetadataTypeDefinitionNameReadResult.Read read)
                return read.Name;
            HasRejectedMetadata = true;
            return null;
        }

        public MetadataTypeDefinitionName? GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            {
                HasRejectedMetadata = true;
                return null;
            }
            using (scope)
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        }

        public MetadataTypeDefinitionName? GetGenericInstantiation(
            MetadataTypeDefinitionName? genericType,
            ImmutableArray<MetadataTypeDefinitionName?> typeArguments)
            => genericType;

        public MetadataTypeDefinitionName? GetByReferenceType(
            MetadataTypeDefinitionName? elementType)
            => elementType;

        public MetadataTypeDefinitionName? GetModifiedType(
            MetadataTypeDefinitionName? modifier,
            MetadataTypeDefinitionName? unmodifiedType,
            bool isRequired)
            => unmodifiedType;

        public MetadataTypeDefinitionName? GetPinnedType(
            MetadataTypeDefinitionName? elementType)
            => elementType;

        public MetadataTypeDefinitionName? GetArrayType(
            MetadataTypeDefinitionName? elementType,
            ArrayShape shape)
            => null;

        public MetadataTypeDefinitionName? GetSZArrayType(
            MetadataTypeDefinitionName? elementType)
            => null;

        public MetadataTypeDefinitionName? GetPointerType(
            MetadataTypeDefinitionName? elementType)
            => null;

        public MetadataTypeDefinitionName? GetFunctionPointerType(
            MethodSignature<MetadataTypeDefinitionName?> signature)
            => null;

        public MetadataTypeDefinitionName? GetGenericMethodParameter(
            GenericContext? context,
            int index)
            => null;

        public MetadataTypeDefinitionName? GetGenericTypeParameter(
            GenericContext? context,
            int index)
            => null;

        public MetadataTypeDefinitionName? GetPrimitiveType(
            PrimitiveTypeCode typeCode)
            => primitivesAreLocal
                ? GetLocalPrimitiveDefinition(typeCode)
                : null;
    }

    static void AddFilteredJsonPropertyNameFact(
        ApiType type,
        FilteredJsonPropertyNameKind kind,
        string? associatedMemberName,
        int metadataToken,
        List<string?> propertyNames)
    {
        if (propertyNames.Count > 0)
        {
            type.FilteredJsonPropertyNameFacts.Add(
                new FilteredJsonPropertyNameFact(
                    kind,
                    associatedMemberName,
                    metadataToken,
                    propertyNames));
        }
    }

    static void RetainFilteredRuntimeJsExportFact(
        ApiType type,
        string methodName,
        MethodDefinitionHandle methodHandle,
        RuntimeJsExportAttributeEvidence evidence)
    {
        if (evidence.Count == 0 && !evidence.HasMalformedRow)
            return;

        type.FilteredRuntimeJsExportFacts.Add(new(
            methodName,
            MetadataTokens.GetToken(methodHandle),
            evidence.Count,
            evidence.HasValidRow,
            evidence.HasMalformedRow));
    }

    static void RetainFilteredRuntimeJsExportFacts(
        MetadataReader reader,
        TypeDefinition type,
        ApiSurface surface,
        ExtractionBudget? budget,
        Action<int>? observeDecodeWork)
    {
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            RuntimeJsExportAttributeEvidence evidence =
                AttributeReader.ReadRuntimeJsExportAttributes(
                    reader,
                    method.GetCustomAttributes(),
                    observeDecodeWork);
            if (evidence.Count == 0 && !evidence.HasMalformedRow)
                continue;

            string methodName = DecodeString(
                reader,
                method.Name,
                observeDecodeWork);
            var fact = new FilteredRuntimeJsExportFact(
                methodName,
                MetadataTokens.GetToken(methodHandle),
                evidence.Count,
                evidence.HasValidRow,
                evidence.HasMalformedRow);
            budget?.RetainSurfaceFilteredRuntimeJsExportFact(fact);
            surface.FilteredRuntimeJsExportFacts.Add(fact);
        }
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

        for (int i = 0; i < signature.Length; i++)
        {
            if (signature[i] == '*'
                && (i == 0
                    || i == signature.Length - 1
                    || signature[i - 1] != '['
                    || signature[i + 1] != ']'))
            {
                return true;
            }
        }

        return false;
    }
}
