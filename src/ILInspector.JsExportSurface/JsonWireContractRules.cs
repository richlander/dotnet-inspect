using System.Text.Json;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

public readonly record struct JsonWirePolymorphicMember(
    ApiType DeclaringType,
    ApiMember Member,
    string PropertyName);

public static class JsonWireContractRules
{
    public const string InertStringFullName = "InertText.InertString";

    public static string ResolvePropertyName(
        ApiType declaringType,
        ApiMember member)
    {
        JsonWireNamingPolicy namingPolicy =
            declaringType.JsonPropertyNamingPolicy
            ?? JsonWireNamingPolicy.None;
        if (namingPolicy == JsonWireNamingPolicy.Unsupported)
        {
            throw new UnsupportedJsExportSurfaceException(
                declaringType.FullName,
                "serializer-context property naming is unsupported");
        }

        return member.JsonPropertyName
            ?? namingPolicy switch
            {
                JsonWireNamingPolicy.None => member.Name,
                JsonWireNamingPolicy.CamelCase =>
                    JsonNamingPolicy.CamelCase.ConvertName(member.Name),
                JsonWireNamingPolicy.SnakeCaseLower =>
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(member.Name),
                JsonWireNamingPolicy.SnakeCaseUpper =>
                    JsonNamingPolicy.SnakeCaseUpper.ConvertName(member.Name),
                JsonWireNamingPolicy.KebabCaseLower =>
                    JsonNamingPolicy.KebabCaseLower.ConvertName(member.Name),
                JsonWireNamingPolicy.KebabCaseUpper =>
                    JsonNamingPolicy.KebabCaseUpper.ConvertName(member.Name),
                _ => throw new UnsupportedJsExportSurfaceException(
                    declaringType.FullName,
                    "serializer-context property naming is unsupported"),
            };
    }

    public static bool UsesStringEnumConverter(ApiType type) =>
        type.HasJsonStringEnumConverter
        || type.JsonUseStringEnumConverter;

    public static bool HasUnsupportedJsonConverter(ApiType type) =>
        type.JsonConverterAttributeCount > 0
        && (type.Kind != "enum"
            || !UsesStringEnumConverter(type)
            || type.JsonConverterAttributeCount != 1);

    public static bool HasApprovedInertStringConverter(ApiMember member) =>
        member.JsonConverterAttributeCount == 1
        && ContainsInertString(member.SignatureModel?.ReturnTypeShape);

    public static bool HasUnsupportedRecordWireShape(
        ApiType type,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
    {
        if (type.HasUnsupportedJsonWireAttributes
            || type.Members.Any(member =>
                member.HasUnsupportedJsonWireAttributes
                && !HasApprovedInertStringConverter(member)
                && JsonWireMemberRules.ParticipatesInWireContract(
                    member,
                    JsonWireDirection.Both,
                    assemblyIdentity,
                    typesByScopedIdentity)))
        {
            return true;
        }

        if (type.BaseType is null)
            return false;
        string expectedBaseType = type.Kind == "struct"
            ? "System.ValueType"
            : "System.Object";
        if (type.BaseType != expectedBaseType)
            return true;
        return type.BaseTypeReference is { } reference
            && !PlatformKeys.IsPlatform(
                reference.Assembly.PublicKeyToken);
    }

    public static bool? CanValueBeNull(
        ApiTypeShape shape,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
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
                    typesByScopedIdentity) ?? !shape.IsValueType;
            case ApiTypeShapeKind.Named:
                return ResolveNamedTypeNullCapability(
                    shape.Definition,
                    assemblyIdentity,
                    typesByScopedIdentity) ?? !shape.IsValueType;
            case ApiTypeShapeKind.Primitive:
                return shape.Primitive switch
                {
                    ApiPrimitiveType.String or ApiPrimitiveType.Object => true,
                    null => null,
                    _ => false,
                };
            default:
                return null;
        }
    }

    public static IReadOnlyList<JsonWirePolymorphicMember>
        GetPolymorphicCaseMembers(
            JsExportSurface surface,
            JsExportPolymorphicUnion union,
            JsExportPolymorphicCase @case,
            JsonWireDirection direction,
            IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
                typesByScopedIdentity)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(union);
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(typesByScopedIdentity);
        ApiType root = union.Definition;
        ApiType caseType = @case.Definition;
        string discriminatorPropertyName =
            union.TypeDiscriminatorPropertyName
            ?? throw new UnsupportedJsExportSurfaceException(
                root.FullName,
                "polymorphic discriminator property is unavailable");

        if (caseType.JsonPropertyNamingPolicy
            != root.JsonPropertyNamingPolicy)
        {
            throw new UnsupportedJsExportSurfaceException(
                caseType.FullName,
                "polymorphic root and case naming policies differ");
        }
        JsonWireContextDefaultIgnoreCondition rootDefaultIgnoreCondition =
            JsonWireMemberRules.GetContextDefaultIgnoreCondition(
                surface,
                root);
        JsonWireContextDefaultIgnoreCondition caseDefaultIgnoreCondition =
            JsonWireMemberRules.GetContextDefaultIgnoreCondition(
                surface,
                caseType);
        if (rootDefaultIgnoreCondition
                == JsonWireContextDefaultIgnoreCondition.Unsupported
            || caseDefaultIgnoreCondition
                == JsonWireContextDefaultIgnoreCondition.Unsupported
            || caseDefaultIgnoreCondition != rootDefaultIgnoreCondition
            || caseType.JsonUseStringEnumConverter
                != root.JsonUseStringEnumConverter)
        {
            throw new UnsupportedJsExportSurfaceException(
                caseType.FullName,
                "polymorphic root and case serializer options differ");
        }

        var members = new List<JsonWirePolymorphicMember>();
        var resolvedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            discriminatorPropertyName,
        };
        foreach (ApiType declaringType in new[] { root, caseType })
        {
            foreach (ApiMember member in declaringType.Members.Where(
                member => JsonWireMemberRules.ParticipatesInWireContract(
                    member,
                    direction,
                    surface.AssemblyIdentity,
                    typesByScopedIdentity)))
            {
                int overriddenIndex = -1;
                if (ReferenceEquals(declaringType, caseType)
                    && member.IsOverride)
                {
                    overriddenIndex = members.FindIndex(
                        candidate => candidate.Member.Name.Equals(
                            member.Name,
                            StringComparison.Ordinal));
                    if (overriddenIndex >= 0)
                    {
                        resolvedNames.Remove(
                            members[overriddenIndex].PropertyName);
                        members.RemoveAt(overriddenIndex);
                    }
                }

                string resolvedName = ResolvePropertyName(
                    declaringType,
                    member);
                string location =
                    $"{declaringType.FullName}.{member.Name}";
                if (resolvedName.Any(char.IsControl))
                {
                    throw new UnsupportedJsExportSurfaceException(
                        location,
                        "control-character JSON property names are unsupported");
                }
                if (!resolvedNames.Add(resolvedName))
                {
                    throw new UnsupportedJsExportSurfaceException(
                        location,
                        resolvedName == discriminatorPropertyName
                            ? "serialized member collides with the "
                                + "polymorphic discriminator property"
                            : "inherited and declared members resolve "
                                + "to the same JSON property name");
                }

                var result = new JsonWirePolymorphicMember(
                    declaringType,
                    member,
                    resolvedName);
                if (overriddenIndex >= 0)
                    members.Insert(overriddenIndex, result);
                else
                    members.Add(result);
            }
        }

        return members;
    }

    static bool ContainsInertString(ApiTypeShape? type)
    {
        if (type is null)
            return false;

        var pending = new Stack<ApiTypeShape>();
        pending.Push(type);
        while (pending.TryPop(out ApiTypeShape? current))
        {
            if (current.Definition is
                {
                    FullName: InertStringFullName,
                    Assembly.Name: "InertText",
                })
            {
                return true;
            }
            if (current.ElementType is { } element)
                pending.Push(element);
            foreach (ApiTypeShape argument in current.TypeArguments)
                pending.Push(argument);
        }

        return false;
    }

    static bool? ResolveNamedTypeNullCapability(
        ApiTypeReferenceIdentity? reference,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            typesByScopedIdentity)
    {
        if (reference is null)
            return null;

        if (reference.FullName is "System.String" or "System.Object")
            return true;

        if (assemblyIdentity is null
            || !typesByScopedIdentity.TryGetValue(
                reference,
                out ApiType? type))
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
}
