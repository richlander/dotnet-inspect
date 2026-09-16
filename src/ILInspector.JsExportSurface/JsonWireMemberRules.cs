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
    /// <c>WhenWritingNull</c> and <c>WhenWritingDefault</c> are conditional
    /// while serializing and present while deserializing. This fact is
    /// target-language-neutral: consumers decide how to lower conditional key
    /// presence. A malformed, duplicated, or unknown authentic condition is
    /// <see cref="JsonWireMemberPresence.Unsupported"/>, never absence.
    /// </remarks>
    public static JsonWireMemberPresence GetPresence(
        ApiMember member,
        JsonWireDirection direction)
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
            || !ParticipatesStructurally(member, direction))
        {
            return JsonWireMemberPresence.Absent;
        }

        return GetConditionPresence(member, direction);
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
            typesByScopedIdentity) =>
        HasAccessibleValueType(
            member,
            assemblyIdentity,
            typesByScopedIdentity)
            ? GetPresence(member, direction)
            : JsonWireMemberPresence.Absent;

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
                    JsonWireDirection.Deserialize)
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
                directions))
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
        JsonWireDirection direction) =>
        member.JsonIgnoreConditions is [var condition]
            ? condition switch
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
            }
            : JsonWireMemberPresence.Present;

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
        JsonWireDirection directions) =>
        IsSerialized(member, directions)
        || ((directions & JsonWireDirection.Deserialize)
                != JsonWireDirection.None
            && RequiresConstructorBindingEvidence(
                declaringType,
                member));

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
