using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

public static class JsonWireMemberRules
{
    /// <summary>
    /// The legacy direction-independent rule for consumers that can represent
    /// only unconditional members.
    /// </summary>
    public static bool IsSerialized(ApiMember member) =>
        IsSerialized(member, JsonWireDirection.Both);

    /// <summary>
    /// The legacy direction-independent unconditional-membership rule,
    /// additionally requiring every same-assembly named value type to remain
    /// accessible to the generated serializer context.
    /// </summary>
    public static bool IsSerialized(
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity) =>
        IsSerialized(
            member,
            JsonWireDirection.Both,
            assemblyIdentity,
            typesByScopedIdentity);

    /// <summary>
    /// True when the member is unconditionally present in at least one of the
    /// requested <paramref name="directions"/>.
    /// </summary>
    /// <remarks>
    /// A member whose <c>[JsonIgnore]</c> or <c>[JsonInclude]</c> metadata is
    /// duplicated or malformed is excluded from every direction: the intent is
    /// real but unreadable, and <c>DtsEmitter</c> refuses to emit such a
    /// declaration at all. Conditional members return false in their
    /// conditional direction; use <see cref="GetPresence(ApiMember,
    /// JsonWireDirection)"/> when a consumer can represent them. Gated by
    /// <c>JsonWireMemberRulesTests.DirectionalIgnoreConditionsSelectPresence</c>.
    /// </remarks>
    public static bool IsSerialized(
        ApiMember member,
        JsonWireDirection directions)
        => HasPresence(
            member,
            directions,
            JsonWireMemberPresence.Present);

    /// <summary>
    /// True when the member is unconditionally present in at least one of the
    /// requested <paramref name="directions"/>, and every same-assembly named
    /// value type remains accessible to the generated serializer context.
    /// </summary>
    public static bool IsSerialized(
        ApiMember member,
        JsonWireDirection directions,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity) =>
        HasAccessibleValueType(
            member,
            assemblyIdentity,
            typesByScopedIdentity)
            && IsSerialized(member, directions);

    /// <summary>
    /// True when the member's presence differs between serialization and
    /// deserialization, which is exactly when one declaration cannot describe
    /// both directions.
    /// </summary>
    public static bool IsDirectionSensitive(ApiMember member) =>
        GetPresence(member, JsonWireDirection.Serialize)
            != GetPresence(member, JsonWireDirection.Deserialize);

    /// <summary>
    /// True when the member's presence differs between serialization and
    /// deserialization after accounting for same-assembly value-type
    /// accessibility.
    /// </summary>
    public static bool IsDirectionSensitive(
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity) =>
        GetPresence(
            member,
            JsonWireDirection.Serialize,
            assemblyIdentity,
            typesByScopedIdentity)
            != GetPresence(
                member,
                JsonWireDirection.Deserialize,
                assemblyIdentity,
                typesByScopedIdentity);

    /// <summary>
    /// The member's authenticated presence in one wire direction.
    /// </summary>
    /// <remarks>
    /// <c>WhenWritingNull</c> is conditional while serializing only for an
    /// authenticated null-capable member type; <c>WhenWritingDefault</c> is
    /// conditional while serializing for every supported type. Both are
    /// present while deserializing. This fact is target-language-neutral:
    /// consumers decide how to lower conditional key presence. An invalid or
    /// unauthenticated condition/type combination, or a malformed, duplicated,
    /// or unknown authentic condition, is
    /// <see cref="JsonWireMemberPresence.Unsupported"/>, never absence.
    /// </remarks>
    public static JsonWireMemberPresence GetPresence(
        ApiMember member,
        JsonWireDirection direction)
        => GetPresenceCore(
            member,
            direction,
            assemblyIdentity: null,
            typesByScopedIdentity: null);

    static JsonWireMemberPresence GetPresenceCore(
        ApiMember member,
        JsonWireDirection direction,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>?
            typesByScopedIdentity)
    {
        if (direction is not JsonWireDirection.Serialize
            and not JsonWireDirection.Deserialize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "Member presence requires one wire direction.");
        }

        if (HasUnsupportedJsonIgnoreMetadata(member)
            || HasUnsupportedJsonIncludeMetadata(member))
        {
            return JsonWireMemberPresence.Unsupported;
        }

        if (member.IsStatic
            || member.IsCompilerGenerated
            || (!ParticipatesStructurally(
                    member,
                    JsonWireDirection.Serialize)
                && !ParticipatesStructurally(
                    member,
                    JsonWireDirection.Deserialize)))
        {
            return JsonWireMemberPresence.Absent;
        }

        JsonWireMemberPresence presence = GetConditionPresence(
            member,
            direction,
            assemblyIdentity,
            typesByScopedIdentity);
        if (presence == JsonWireMemberPresence.Unsupported)
            return presence;

        return ParticipatesStructurally(member, direction)
            ? presence
            : JsonWireMemberPresence.Absent;
    }

    /// <summary>
    /// The member's authenticated presence in one wire direction, additionally
    /// requiring every same-assembly named value type to remain accessible to
    /// the generated serializer context.
    /// </summary>
    public static JsonWireMemberPresence GetPresence(
        ApiMember member,
        JsonWireDirection direction,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
    {
        JsonWireMemberPresence presence = GetPresenceCore(
            member,
            direction,
            assemblyIdentity,
            typesByScopedIdentity);
        if (presence == JsonWireMemberPresence.Unsupported)
            return presence;

        return HasAccessibleValueType(
                member,
                assemblyIdentity,
                typesByScopedIdentity)
            ? presence
            : JsonWireMemberPresence.Absent;
    }

    /// <summary>
    /// Normalizes authentic Metadata evidence into the effective context
    /// default consumed by wire-shape composition.
    /// </summary>
    public static JsonWireContextDefaultIgnoreCondition
        GetContextDefaultIgnoreCondition(
            JsonSourceGenerationDefaultIgnoreConditionEvidence evidence)
    {
        if (evidence.HasUnsupportedRow
            || evidence.AttributeCount < 0
            || evidence.AttributeCount > 1)
        {
            return JsonWireContextDefaultIgnoreCondition.Unsupported;
        }

        if (evidence.AttributeCount == 0)
        {
            return evidence.Value is null
                ? JsonWireContextDefaultIgnoreCondition.Never
                : JsonWireContextDefaultIgnoreCondition.Unsupported;
        }

        return evidence.Value switch
        {
            JsonWireIgnoreCondition.Never =>
                JsonWireContextDefaultIgnoreCondition.Never,
            JsonWireIgnoreCondition.WhenWritingNull =>
                JsonWireContextDefaultIgnoreCondition.WhenWritingNull,
            _ => JsonWireContextDefaultIgnoreCondition.Unsupported,
        };
    }

    /// <summary>
    /// Returns the owner-issued effective context default for one surfaced
    /// type. A missing entry is the framework default used by hand-composed
    /// declaration-only surfaces.
    /// </summary>
    public static JsonWireContextDefaultIgnoreCondition
        GetContextDefaultIgnoreCondition(
            JsExportSurface surface,
            ApiType declaringType) =>
        surface.ContextDefaultIgnoreConditions.TryGetValue(
            declaringType,
            out JsonWireContextDefaultIgnoreCondition condition)
            ? condition
            : JsonWireContextDefaultIgnoreCondition.Never;

    /// <summary>
    /// The member's effective authenticated presence after composing its
    /// metadata with the serializer contexts that reached its declaring type.
    /// </summary>
    public static JsonWireMemberPresence GetPresence(
        JsExportSurface surface,
        ApiType declaringType,
        ApiMember member,
        JsonWireDirection direction,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
    {
        JsonWireContextDefaultIgnoreCondition defaultIgnoreCondition =
            GetContextDefaultIgnoreCondition(surface, declaringType);

        if (defaultIgnoreCondition
            == JsonWireContextDefaultIgnoreCondition.Unsupported)
        {
            return JsonWireMemberPresence.Unsupported;
        }

        JsonWireMemberPresence presence = GetPresence(
            member,
            direction,
            assemblyIdentity,
            typesByScopedIdentity);
        if (presence != JsonWireMemberPresence.Present
            || direction != JsonWireDirection.Serialize
            || member.JsonIgnoreConditions.Count != 0
            || defaultIgnoreCondition
                == JsonWireContextDefaultIgnoreCondition.Never)
        {
            return presence;
        }

        if (defaultIgnoreCondition
            != JsonWireContextDefaultIgnoreCondition.WhenWritingNull)
        {
            return JsonWireMemberPresence.Unsupported;
        }

        return CanMemberValueBeNull(
                member,
                assemblyIdentity,
                typesByScopedIdentity) switch
            {
                true => JsonWireMemberPresence.Conditional,
                false => JsonWireMemberPresence.Present,
                null => JsonWireMemberPresence.Unsupported,
            };
    }

    public static bool IsDirectionSensitive(
        JsExportSurface surface,
        ApiType declaringType,
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity) =>
        GetPresence(
            surface,
            declaringType,
            member,
            JsonWireDirection.Serialize,
            assemblyIdentity,
            typesByScopedIdentity)
        != GetPresence(
            surface,
            declaringType,
            member,
            JsonWireDirection.Deserialize,
            assemblyIdentity,
            typesByScopedIdentity);

    /// <summary>
    /// True when the member is present or conditionally present in at least one
    /// requested wire direction.
    /// </summary>
    public static bool ParticipatesInWireContract(
        ApiMember member,
        JsonWireDirection directions) =>
        HasPresence(
            member,
            directions,
            JsonWireMemberPresence.Present,
            JsonWireMemberPresence.Conditional);

    /// <summary>
    /// True when the member is present or conditionally present in at least one
    /// requested wire direction, and every same-assembly named value type
    /// remains accessible to the generated serializer context.
    /// </summary>
    public static bool ParticipatesInWireContract(
        ApiMember member,
        JsonWireDirection directions,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity) =>
        HasPresence(
            member,
            directions,
            JsonWireMemberPresence.Present,
            JsonWireMemberPresence.Conditional,
            assemblyIdentity,
            typesByScopedIdentity);

    /// <summary>
    /// True when deserialization may bind a getter-only property, or a
    /// property without a participating setter, through a constructor shape
    /// this projection does not currently model.
    /// </summary>
    public static bool RequiresConstructorBindingEvidence(
        ApiType declaringType,
        ApiMember member)
    {
        int? indexParameterCount =
            member.IndexParameterCount
            ?? member.SignatureModel?.ParameterCount;
        return member.Kind == "property"
            && !member.IsStatic
            && !HasUnsupportedJsonIgnoreMetadata(member)
            && !HasUnsupportedJsonIncludeMetadata(member)
            && GetConditionPresence(
                    member,
                    JsonWireDirection.Deserialize,
                    assemblyIdentity: null,
                    typesByScopedIdentity: null)
                == JsonWireMemberPresence.Present
            && indexParameterCount == 0
            && !IsIncludedAccessor(
                member.HasSetter,
                member.SetterAccessibility,
                member.Accessibility,
                member.HasJsonInclude)
            && (member.HasSetter == false
                || declaringType.Members
                    .Where(candidate =>
                        candidate.Kind == "constructor")
                    .SelectMany(candidate =>
                        candidate.SignatureModel?.Parameters
                            ?? [])
                    .Any(parameter =>
                        string.Equals(
                            parameter.Name,
                            member.Name,
                            StringComparison.OrdinalIgnoreCase)))
            && IsIncludedAccessor(
                member.HasGetter,
                member.GetterAccessibility,
                member.Accessibility,
                member.HasJsonInclude);
    }

    /// <summary>
    /// True when deserialization may bind the member through a constructor
    /// shape this projection does not currently model, provided the member's
    /// value type remains accessible to the generated serializer context.
    /// </summary>
    public static bool RequiresConstructorBindingEvidence(
        ApiType declaringType,
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity) =>
        HasAccessibleValueType(
            member,
            assemblyIdentity,
            typesByScopedIdentity)
            && RequiresConstructorBindingEvidence(
                declaringType,
                member);

    /// <summary>
    /// True when a <c>[JsonInclude]</c> member references a same-assembly value
    /// type that ordinary top-level source generation cannot access, but a
    /// nested serializer context rooted inside the same declaring type could.
    /// This is a real runtime distinction that the current surface model does
    /// not project, so callers must fail visibly rather than silently drop the
    /// member.
    /// </summary>
    public static bool RequiresContextRelativeValueTypeAccessibilityEvidence(
        ApiType declaringType,
        ApiMember member,
        JsonWireDirection directions,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity,
        MetadataTypeDefinitionName? contextDefinitionName)
    {
        if (assemblyIdentity is null
            || contextDefinitionName is null
            || !member.HasJsonInclude
            || !RequiresWireParticipationOrConstructorBinding(
                declaringType,
                member,
                directions,
                assemblyIdentity,
                typesByScopedIdentity))
        {
            return false;
        }

        IReadOnlyList<ApiTypeReferenceIdentity>? references =
            member.SignatureModel?.ReturnTypeReferences;
        if (references is null || references.Count == 0)
            return false;

        return !HasAccessibleValueType(
                member,
                assemblyIdentity,
                typesByScopedIdentity,
                contextDefinitionName: null)
            && HasAccessibleValueType(
                member,
                assemblyIdentity,
                typesByScopedIdentity,
                contextDefinitionName);
    }

    public static bool RequiresContextRelativeValueTypeAccessibilityEvidence(
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity,
        MetadataTypeDefinitionName? contextDefinitionName) =>
        RequiresContextRelativeValueTypeAccessibilityEvidence(
            new ApiType(),
            member,
            JsonWireDirection.Both,
            assemblyIdentity,
            typesByScopedIdentity,
            contextDefinitionName);

    /// <summary>
    /// True when the member carries authentic <c>[JsonIgnore]</c> metadata that
    /// cannot be honored: more than one row, or a row whose constructor or
    /// <c>Condition</c> argument could not be read.
    /// </summary>
    public static bool HasUnsupportedJsonIgnoreMetadata(ApiMember member) =>
        member.JsonIgnoreConditions.Count > 1
        || member.JsonIgnoreConditions.Contains(null)
        || member.JsonIgnoreConditions.Any(
            condition => condition is { } value
                && !Enum.IsDefined(value));

    /// <summary>
    /// True when the member carries an authentic <c>[JsonInclude]</c> row whose
    /// constructor or value blob could not be read.
    /// </summary>
    public static bool HasUnsupportedJsonIncludeMetadata(ApiMember member) =>
        member.HasMalformedJsonInclude;

    static bool ParticipatesStructurally(
        ApiMember member,
        JsonWireDirection direction)
    {
        if (member.Kind == "field")
            return member.HasJsonInclude;
        if (member.Kind != "property")
            return false;

        int? indexParameterCount =
            member.IndexParameterCount
            ?? member.SignatureModel?.ParameterCount;
        if (indexParameterCount != 0)
            return false;

        bool serialize = IsIncludedAccessor(
            member.HasGetter,
            member.GetterAccessibility,
            member.Accessibility,
            member.HasJsonInclude);
        bool deserialize = member.HasSetter is null
            ? serialize
            : IsIncludedAccessor(
                member.HasSetter,
                member.SetterAccessibility,
                member.Accessibility,
                member.HasJsonInclude);
        return direction == JsonWireDirection.Serialize
            ? serialize
            : deserialize;
    }

    static JsonWireMemberPresence GetConditionPresence(
        ApiMember member,
        JsonWireDirection direction,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>?
            typesByScopedIdentity)
    {
        if (member.JsonIgnoreConditions is not [var condition])
            return JsonWireMemberPresence.Present;

        if (condition == JsonWireIgnoreCondition.WhenWritingNull
            && CanMemberValueBeNull(
                member,
                assemblyIdentity,
                typesByScopedIdentity) != true)
        {
            return JsonWireMemberPresence.Unsupported;
        }

        return condition switch
            {
                JsonWireIgnoreCondition.Never =>
                    JsonWireMemberPresence.Present,
                JsonWireIgnoreCondition.Always =>
                    JsonWireMemberPresence.Absent,
                JsonWireIgnoreCondition.WhenWritingDefault
                    or JsonWireIgnoreCondition.WhenWritingNull =>
                    direction == JsonWireDirection.Serialize
                        ? JsonWireMemberPresence.Conditional
                        : JsonWireMemberPresence.Present,
                JsonWireIgnoreCondition.WhenWriting =>
                    direction == JsonWireDirection.Serialize
                        ? JsonWireMemberPresence.Absent
                        : JsonWireMemberPresence.Present,
                JsonWireIgnoreCondition.WhenReading =>
                    direction == JsonWireDirection.Serialize
                        ? JsonWireMemberPresence.Present
                        : JsonWireMemberPresence.Absent,
                _ => JsonWireMemberPresence.Unsupported,
            };
    }

    static bool HasPresence(
        ApiMember member,
        JsonWireDirection directions,
        JsonWireMemberPresence expected) =>
        HasPresence(member, directions, expected, expected);

    static bool HasPresence(
        ApiMember member,
        JsonWireDirection directions,
        JsonWireMemberPresence first,
        JsonWireMemberPresence second,
        ApiAssemblyIdentity? assemblyIdentity = null,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>?
            typesByScopedIdentity = null)
    {
        JsonWireMemberPresence Presence(JsonWireDirection direction) =>
            typesByScopedIdentity is null
                ? GetPresence(member, direction)
                : GetPresence(
                    member,
                    direction,
                    assemblyIdentity,
                    typesByScopedIdentity);

        bool Matches(JsonWireDirection direction)
        {
            JsonWireMemberPresence presence = Presence(direction);
            return presence == first || presence == second;
        }

        return ((directions & JsonWireDirection.Serialize)
                    != JsonWireDirection.None
                && Matches(JsonWireDirection.Serialize))
            || ((directions & JsonWireDirection.Deserialize)
                    != JsonWireDirection.None
                && Matches(JsonWireDirection.Deserialize));
    }

    static bool IsIncludedAccessor(
        bool? hasAccessor,
        string? accessorAccessibility,
        string? memberAccessibility,
        bool hasJsonInclude)
    {
        if (hasAccessor is false)
            return false;

        string? accessibility = hasAccessor is true
            ? accessorAccessibility
            : memberAccessibility;
        return hasJsonInclude || accessibility is null;
    }

    static bool RequiresWireParticipationOrConstructorBinding(
        ApiType declaringType,
        ApiMember member,
        JsonWireDirection directions,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity) =>
        HasPresenceWithoutAccessibility(
            member,
            directions,
            assemblyIdentity,
            typesByScopedIdentity)
        || ((directions & JsonWireDirection.Deserialize)
                != JsonWireDirection.None
            && RequiresConstructorBindingEvidence(
                declaringType,
                member));

    static bool HasPresenceWithoutAccessibility(
        ApiMember member,
        JsonWireDirection directions,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
    {
        bool Matches(JsonWireDirection direction)
        {
            JsonWireMemberPresence presence = GetPresenceCore(
                member,
                direction,
                assemblyIdentity,
                typesByScopedIdentity);
            return presence is JsonWireMemberPresence.Present
                or JsonWireMemberPresence.Conditional;
        }

        return ((directions & JsonWireDirection.Serialize)
                    != JsonWireDirection.None
                && Matches(JsonWireDirection.Serialize))
            || ((directions & JsonWireDirection.Deserialize)
                    != JsonWireDirection.None
                && Matches(JsonWireDirection.Deserialize));
    }

    static bool? CanMemberValueBeNull(
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>?
            typesByScopedIdentity)
    {
        if (member.SignatureModel?.ReturnTypeShape is { } shape)
        {
            return CanTypeValueBeNull(
                shape,
                assemblyIdentity,
                typesByScopedIdentity);
        }

        if (IsRenderedArrayType(member.ReturnType))
            return true;

        IReadOnlyList<ApiTypeReferenceIdentity>? references =
            member.SignatureModel?.ReturnTypeReferences;
        if (references is { Count: > 0 }
            && member.ReturnType?.EndsWith(
                "?",
                StringComparison.Ordinal) == true)
        {
            return true;
        }
        if (references is [var reference])
        {
            bool? capability = ResolveNamedTypeNullCapability(
                reference,
                assemblyIdentity,
                typesByScopedIdentity);
            if (capability is not null)
                return capability;
        }

        return CanRenderedTypeValueBeNull(member.ReturnType);
    }

    static bool IsRenderedArrayType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;

        int end = typeName.EndsWith("?", StringComparison.Ordinal)
            ? typeName.Length - 1
            : typeName.Length;
        return end > 0
            && typeName[end - 1] == ']'
            && typeName.LastIndexOf('[', end - 1) >= 0;
    }

    static bool? CanTypeValueBeNull(
        ApiTypeShape shape,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>?
            typesByScopedIdentity)
    {
        switch (shape.Kind)
        {
            case ApiTypeShapeKind.SzArray:
            case ApiTypeShapeKind.Array:
                return true;
            case ApiTypeShapeKind.GenericParameter:
                return null;
            case ApiTypeShapeKind.GenericInstance:
                if (shape.Definition?.FullName is
                    "System.Nullable" or "System.Nullable`1")
                {
                    return true;
                }

                return ResolveNamedTypeNullCapability(
                    shape.Definition,
                    assemblyIdentity,
                    typesByScopedIdentity);
            case ApiTypeShapeKind.Named:
                return ResolveNamedTypeNullCapability(
                    shape.Definition,
                    assemblyIdentity,
                    typesByScopedIdentity);
            case ApiTypeShapeKind.Primitive:
                return CanPrimitiveValueBeNull(shape.Primitive);
            default:
                return null;
        }
    }

    static bool? ResolveNamedTypeNullCapability(
        ApiTypeReferenceIdentity? reference,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>?
            typesByScopedIdentity)
    {
        if (reference is null)
            return null;

        if (reference.FullName is "System.String" or "System.Object")
            return true;

        if (assemblyIdentity is null
            || typesByScopedIdentity is null
            || !reference.Assembly.Equals(assemblyIdentity)
            || !typesByScopedIdentity.TryGetValue(reference, out ApiType? type))
        {
            return null;
        }

        return type.Kind switch
        {
            "class" or "interface" or "delegate" => true,
            "struct" or "enum" => false,
            _ => null,
        };
    }

    static bool? CanPrimitiveValueBeNull(ApiPrimitiveType? primitive) =>
        primitive switch
        {
            ApiPrimitiveType.String or ApiPrimitiveType.Object => true,
            null => null,
            _ => false,
        };

    static bool? CanRenderedTypeValueBeNull(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        if (typeName.EndsWith("[]", StringComparison.Ordinal)
            || typeName.StartsWith(
                "System.Nullable<",
                StringComparison.Ordinal)
            || typeName is "string" or "string?"
                or "System.String" or "System.String?"
                or "object" or "object?"
                or "System.Object" or "System.Object?"
                or "dynamic" or "dynamic?")
        {
            return true;
        }

        return typeName switch
        {
            "bool?" or "System.Boolean?"
                or "byte?" or "System.Byte?"
                or "sbyte?" or "System.SByte?"
                or "char?" or "System.Char?"
                or "decimal?" or "System.Decimal?"
                or "double?" or "System.Double?"
                or "float?" or "System.Single?"
                or "int?" or "System.Int32?"
                or "uint?" or "System.UInt32?"
                or "long?" or "System.Int64?"
                or "ulong?" or "System.UInt64?"
                or "short?" or "System.Int16?"
                or "ushort?" or "System.UInt16?"
                or "nint?" or "System.IntPtr?"
                or "nuint?" or "System.UIntPtr?" => true,
            "bool" or "System.Boolean"
                or "byte" or "System.Byte"
                or "sbyte" or "System.SByte"
                or "char" or "System.Char"
                or "decimal" or "System.Decimal"
                or "double" or "System.Double"
                or "float" or "System.Single"
                or "int" or "System.Int32"
                or "uint" or "System.UInt32"
                or "long" or "System.Int64"
                or "ulong" or "System.UInt64"
                or "short" or "System.Int16"
                or "ushort" or "System.UInt16"
                or "nint" or "System.IntPtr"
                or "nuint" or "System.UIntPtr" => false,
            _ => null,
        };
    }

    static bool HasAccessibleValueType(
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
        => HasAccessibleValueType(
            member,
            assemblyIdentity,
            typesByScopedIdentity,
            contextDefinitionName: null);

    static bool HasAccessibleValueType(
        ApiMember member,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity,
        MetadataTypeDefinitionName? contextDefinitionName)
    {
        if (assemblyIdentity is null)
            return true;

        IReadOnlyList<ApiTypeReferenceIdentity>? references =
            member.SignatureModel?.ReturnTypeReferences;
        if (references is null || references.Count == 0)
            return true;

        return references.All(reference =>
            IsAccessibleValueTypeReference(
                reference,
                assemblyIdentity,
                typesByScopedIdentity,
                contextDefinitionName));
    }

    static bool IsAccessibleValueTypeReference(
        ApiTypeReferenceIdentity reference,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
        => IsAccessibleValueTypeReference(
            reference,
            assemblyIdentity,
            typesByScopedIdentity,
            contextDefinitionName: null);

    static bool IsAccessibleValueTypeReference(
        ApiTypeReferenceIdentity reference,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity,
        MetadataTypeDefinitionName? contextDefinitionName)
    {
        if (!reference.Assembly.Equals(assemblyIdentity))
            return true;

        return typesByScopedIdentity.TryGetValue(
                reference,
                out ApiType? type)
            && IsAccessibleValueType(
                type,
                assemblyIdentity,
                typesByScopedIdentity,
                contextDefinitionName);
    }

    static bool IsAccessibleValueType(
        ApiType type,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
        => IsAccessibleValueType(
            type,
            assemblyIdentity,
            typesByScopedIdentity,
            contextDefinitionName: null);

    static bool IsAccessibleValueType(
        ApiType type,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity,
        MetadataTypeDefinitionName? contextDefinitionName)
    {
        if (!IsSourceGeneratorTypeAccessible(
                type,
                assemblyIdentity,
                typesByScopedIdentity,
                contextDefinitionName))
            return false;

        if (type.DefinitionName?.Segments.Length is not > 1)
            return true;

        ApiTypeReferenceIdentity? declaringTypeIdentity =
            DeclaringTypeIdentity(type, assemblyIdentity);
        return declaringTypeIdentity is not null
            && typesByScopedIdentity.TryGetValue(
                declaringTypeIdentity,
                out ApiType? declaringType)
            && IsAccessibleValueType(
                declaringType,
                assemblyIdentity,
                typesByScopedIdentity,
                contextDefinitionName);
    }

    static ApiTypeReferenceIdentity? DeclaringTypeIdentity(
        ApiType type,
        ApiAssemblyIdentity assemblyIdentity)
    {
        if (type.DefinitionName?.Segments.Length is not > 1)
            return null;

        int separator = type.FullName.LastIndexOf('.');
        if (separator < 0)
            return null;

        MetadataTypeDefinitionNameResult parentName =
            MetadataTypeDefinitionName.Create(
                type.DefinitionName.Namespace,
                [.. type.DefinitionName.Segments[..^1]]);
        return parentName is MetadataTypeDefinitionNameResult.Valid
            {
                Name: var definitionName,
            }
            ? new ApiTypeReferenceIdentity(
                assemblyIdentity,
                type.FullName[..separator],
                definitionName)
            : null;
    }

    static bool IsSourceGeneratorTypeAccessible(
        ApiType type,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity,
        MetadataTypeDefinitionName? contextDefinitionName)
    {
        if (type.Accessibility is null or "internal" or "protected internal")
            return true;

        if (contextDefinitionName is null
            || type.DefinitionName is not { Segments.Length: > 1 } definitionName)
        {
            return false;
        }

        return type.Accessibility switch
        {
            "private"
                when contextDefinitionName.Namespace
                    == definitionName.Namespace =>
                ContextIsNestedWithin(
                    contextDefinitionName.Segments,
                    definitionName.Segments[..^1]),
            "protected" or "private protected" =>
                ContextCanReachProtectedDeclaringType(
                    definitionName,
                    contextDefinitionName,
                    assemblyIdentity,
                    typesByScopedIdentity),
            _ => false,
        };
    }

    static bool ContextCanReachProtectedDeclaringType(
        MetadataTypeDefinitionName definitionName,
        MetadataTypeDefinitionName contextDefinitionName,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
    {
        IReadOnlyList<string> declaringSegments =
            definitionName.Segments[..^1];
        foreach (MetadataTypeDefinitionName contextContainer
            in ContextContainerDefinitionNames(contextDefinitionName))
        {
            if (DefinitionNamesEqual(
                    contextContainer.Namespace,
                    contextContainer.Segments,
                    definitionName.Namespace,
                    declaringSegments))
            {
                return true;
            }

            if (TryGetType(
                    contextContainer,
                    assemblyIdentity,
                    typesByScopedIdentity,
                    out ApiType? contextType)
                && contextType is not null
                && InheritsFrom(
                    contextType,
                    definitionName.Namespace,
                    declaringSegments,
                    assemblyIdentity,
                    typesByScopedIdentity))
            {
                return true;
            }
        }

        return false;
    }

    static IEnumerable<MetadataTypeDefinitionName>
        ContextContainerDefinitionNames(
        MetadataTypeDefinitionName contextDefinitionName)
    {
        for (int count = 1;
            count < contextDefinitionName.Segments.Length;
            count++)
        {
            MetadataTypeDefinitionNameResult containerName =
                MetadataTypeDefinitionName.Create(
                    contextDefinitionName.Namespace,
                    [.. contextDefinitionName.Segments[..count]]);
            if (containerName is MetadataTypeDefinitionNameResult.Valid
                { Name: var definitionName })
            {
                yield return definitionName;
            }
        }
    }

    static bool TryGetType(
        MetadataTypeDefinitionName definitionName,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity,
        out ApiType? type)
    {
        string fullName = string.IsNullOrEmpty(definitionName.Namespace)
            ? string.Join(".", definitionName.Segments)
            : $"{definitionName.Namespace}."
                + string.Join(".", definitionName.Segments);
        return typesByScopedIdentity.TryGetValue(
            new ApiTypeReferenceIdentity(
                assemblyIdentity,
                fullName,
                definitionName),
            out type);
    }

    static bool InheritsFrom(
        ApiType type,
        string expectedNamespace,
        IReadOnlyList<string> expectedSegments,
        ApiAssemblyIdentity assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
    {
        var visited = new HashSet<ApiType>(
            ReferenceEqualityComparer.Instance);
        ApiType? current = type;
        while (current is not null
            && visited.Add(current)
            && current.BaseTypeReference is { } baseTypeReference
            && baseTypeReference.Assembly.Equals(assemblyIdentity))
        {
            if (DefinitionNamesEqual(
                    baseTypeReference.DefinitionName?.Namespace,
                    baseTypeReference.DefinitionName?.Segments,
                    expectedNamespace,
                    expectedSegments))
            {
                return true;
            }

            if (!typesByScopedIdentity.TryGetValue(
                    baseTypeReference,
                    out current))
            {
                return false;
            }
        }

        return false;
    }

    static bool DefinitionNamesEqual(
        string? leftNamespace,
        IReadOnlyList<string>? leftSegments,
        string rightNamespace,
        IReadOnlyList<string> rightSegments)
    {
        if (!string.Equals(
                leftNamespace,
                rightNamespace,
                StringComparison.Ordinal)
            || leftSegments is null
            || leftSegments.Count != rightSegments.Count)
        {
            return false;
        }

        for (int index = 0; index < leftSegments.Count; index++)
        {
            if (!string.Equals(
                    leftSegments[index],
                    rightSegments[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    static bool ContextIsNestedWithin(
        IReadOnlyList<string> contextSegments,
        IReadOnlyList<string> declaringSegments)
    {
        if (contextSegments.Count <= declaringSegments.Count)
            return false;

        for (int index = 0; index < declaringSegments.Count; index++)
        {
            if (!string.Equals(
                    contextSegments[index],
                    declaringSegments[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
