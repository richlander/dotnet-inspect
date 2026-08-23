using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

/// <summary>
/// Builds a <see cref="JsExportSurface"/> from an already-extracted <see cref="ApiSurface"/>.
/// </summary>
public static class JsExportSurfaceBuilder
{
    const string JsonTypeInfoPrefix = "System.Text.Json.Serialization.Metadata.JsonTypeInfo<";
    const string JsonTypeInfoMetadataName =
        "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1";
    const string JsonSerializerContextBaseType = "System.Text.Json.Serialization.JsonSerializerContext";
    const string SystemTextJsonAssemblyName = "System.Text.Json";

    public static JsExportSurface Build(ApiSurface surface) => Build(surface, bodyIndex: null);

    public static JsExportSurface Build(ApiSurface surface, LibraryBodyIndex? bodyIndex)
    {
        var typesByIdentity = surface.Types
            .SelectMany(type =>
                new[] { type.Name, type.FullName, type.MetadataName }
                    .Where(identity => !string.IsNullOrEmpty(identity))
                    .Distinct(StringComparer.Ordinal)
                    .Select(identity => (Identity: identity!, Type: type)))
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Where(group => group.Select(candidate => candidate.Type)
                .Distinct()
                .Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.First().Type,
                StringComparer.Ordinal);
        var typesByScopedIdentity =
            surface.AssemblyIdentity is { } assemblyIdentity
                ? surface.Types
                    .Select(type => (
                        Identity: new ApiTypeReferenceIdentity(
                            assemblyIdentity,
                            type.FullName,
                            type.DefinitionName),
                        Type: type))
                    .GroupBy(candidate => candidate.Identity)
                    .Where(group => group
                        .Select(candidate => candidate.Type)
                        .Distinct()
                        .Count() == 1)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().Type)
                : [];

        var incompleteBodyTokens = new HashSet<int>();
        if (bodyIndex is not null)
        {
                foreach (AnalysisDiagnostic diagnostic in bodyIndex.Diagnostics)
                {
                    incompleteBodyTokens.Add(diagnostic.MethodToken);
                    if (diagnostic.SourceMethodToken is { } sourceMethodToken)
                        incompleteBodyTokens.Add(sourceMethodToken);
                }
        }

        var functions = new List<JsExportFunction>();
        var functionTokens = new Dictionary<JsExportFunction, int>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                if (!member.IsStatic || !HasJsExportAttribute(member))
                    continue;

                if (member.IsUnsafe)
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "unsafe JS exports are not supported");
                if (member.SignatureDecodeStatus
                    == SignatureDecodeStatus.Degraded)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "JS export signature metadata is degraded");
                }

                ApiSignature? signature = member.SignatureModel;
                if (signature is null)
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "JS export signature metadata is unavailable");

                var function = new JsExportFunction
                {
                    DeclaringType = type.Name,
                    Name = member.Name,
                    ReturnType = signature.ReturnType ?? member.ReturnType ?? "void",
                    ReturnTypeReferences =
                        signature.ReturnTypeReferences,
                    Parameters = signature.Parameters,
                };

                if (bodyIndex is not null && member.MetadataToken is { } token)
                {
                    if (incompleteBodyTokens.Contains(token))
                    {
                        throw new UnsupportedJsExportSurfaceException(
                            FormatMemberLocation(type, member),
                            "JS export body analysis is incomplete");
                    }
                    functionTokens.Add(function, token);
                }

                functions.Add(function);
            }
        }

        var records = new List<ApiType>();
        var enums = new List<ApiType>();
        var discovered = new HashSet<ApiType>();
        var policiesByType = new Dictionary<ApiType, HashSet<JsonWireNamingPolicy>>();
        var registeredJsonTypeInfoGetterTokens = new HashSet<int>();
        var queue = new Queue<(
            string? Name,
            ApiTypeReferenceIdentity? Identity,
            JsonWireNamingPolicy Policy)>();

        foreach (ApiType type in surface.Types)
        {
            if (type.BaseType != JsonSerializerContextBaseType)
                continue;
            if (surface.AssemblyIdentity is not null
                && !IsTrustedSystemTextJsonType(
                    type.BaseTypeReference,
                    JsonSerializerContextBaseType))
            {
                continue;
            }
            if (surface.AssemblyIdentity is not null
                && type.JsonSerializableAttributeCount
                    != type.JsonSerializableRoots.Count)
            {
                throw new UnsupportedJsExportSurfaceException(
                    FormatTypeLocation(type),
                    "JsonSerializable metadata is malformed or unsupported");
            }
            IReadOnlyDictionary<string, ApiJsonSerializableRoot>?
                registeredRootProperties =
                surface.AssemblyIdentity is null
                    ? null
                    : GetRegisteredRootProperties(type);

            foreach (ApiMember member in type.Members)
            {
                if (member.Kind != "property")
                    continue;
                if (member.SignatureDecodeStatus
                    == SignatureDecodeStatus.Degraded)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "serializer-context property signature metadata is degraded");
                }

                JsonWireNamingPolicy policy =
                    type.JsonPropertyNamingPolicy
                        ?? JsonWireNamingPolicy.None;
                ApiSignature? signature = member.SignatureModel;
                IReadOnlyList<ApiTypeReferenceIdentity>? references =
                    signature?.ReturnTypeReferences;
                if (registeredRootProperties is not null)
                {
                    if (TryGetRegisteredRootForProperty(
                            member,
                            signature,
                            registeredRootProperties,
                            out ApiJsonSerializableRoot root))
                    {
                        if (member.GetterToken is { } getterToken)
                            registeredJsonTypeInfoGetterTokens.Add(getterToken);
                        queue.Enqueue((null, root.ElementType, policy));
                    }
                    continue;
                }

                string? returnType = member.SignatureModel?.ReturnType ?? member.ReturnType;
                if (returnType is null
                    || !returnType.StartsWith(JsonTypeInfoPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string rootTypeName = returnType[JsonTypeInfoPrefix.Length..^1];

                if (references?.Count > 0)
                {
                    foreach (ApiTypeReferenceIdentity reference
                        in references)
                    {
                        if (IsTrustedSystemTextJsonType(
                                reference,
                                JsonTypeInfoMetadataName))
                        {
                            continue;
                        }
                        queue.Enqueue((null, reference, policy));
                    }
                }
                else
                {
                    foreach (string candidate
                        in ExtractCandidateTypeNames(rootTypeName))
                    {
                        queue.Enqueue((candidate, null, policy));
                    }
                }
            }
        }

        while (queue.Count > 0)
        {
            (string? name, ApiTypeReferenceIdentity? identity,
                JsonWireNamingPolicy namingPolicy) = queue.Dequeue();
            ApiType? type = null;
            if (identity is not null)
                typesByScopedIdentity.TryGetValue(identity, out type);
            else if (name is not null)
                typesByIdentity.TryGetValue(name, out type);
            if (type is null)
                continue;

            if (!policiesByType.TryGetValue(type, out HashSet<JsonWireNamingPolicy>? policies))
            {
                policies = [];
                policiesByType.Add(type, policies);
            }

            if (!policies.Add(namingPolicy))
                continue;

            type.JsonPropertyNamingPolicy = policies.Count == 1
                ? namingPolicy
                : JsonWireNamingPolicy.Unsupported;

            if (discovered.Add(type))
            {
                if (type.Kind == "enum")
                    enums.Add(type);
                else
                    records.Add(type);
            }

            if (type.Kind == "enum"
                || type.JsonConverterAttributeCount > 0)
                continue;

            foreach (ApiMember member in type.Members)
            {
                if (!JsonWireMemberRules.IsSerialized(member)
                    || member.JsonConverterAttributeCount > 0)
                    continue;
                if (member.SignatureDecodeStatus
                    == SignatureDecodeStatus.Degraded)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "serialized member signature metadata is degraded");
                }

                string? propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType;
                if (member.SignatureModel?.ReturnTypeReferences.Count > 0)
                {
                    foreach (ApiTypeReferenceIdentity reference
                        in member.SignatureModel.ReturnTypeReferences)
                    {
                        queue.Enqueue((null, reference, namingPolicy));
                    }
                }
                else
                {
                    foreach (string candidate
                        in ExtractCandidateTypeNames(propertyType))
                    {
                        queue.Enqueue((candidate, null, namingPolicy));
                    }
                }
            }
        }

        if (bodyIndex is not null)
        {
            for (int index = 0; index < functions.Count; index++)
            {
                JsExportFunction function = functions[index];
                if (functionTokens.TryGetValue(function, out int token))
                {
                    functions[index] = JsonWireContractResolver.Attach(
                        bodyIndex,
                        function,
                        token,
                        registeredJsonTypeInfoGetterTokens);
                }
            }
        }

        return new JsExportSurface
        {
            AssemblyIdentity = surface.AssemblyIdentity,
            Functions = functions,
            Records = records,
            Enums = enums,
            WireDirections = ResolveWireDirections(
                functions,
                typesByScopedIdentity,
                discovered),
        };
    }

    /// <summary>
    /// Propagates the direction each declared type is reached in: a function's
    /// resolved return wire type seeds <see cref="JsonWireDirection.Serialize"/>
    /// and its resolved parameter wire types seed
    /// <see cref="JsonWireDirection.Deserialize"/>, then both flow through the
    /// members that participate in the contract.
    /// </summary>
    /// <remarks>
    /// Propagation deliberately walks the direction-independent
    /// <see cref="JsonWireMemberRules.IsSerialized(ApiMember)"/> union, which is
    /// a superset of any single direction's member set. Emission can therefore
    /// never reference a type this pass failed to reach, so a directional
    /// declaration cannot orphan a type. Gated by
    /// <c>DtsEmitterTests.Emit_DoesNotOrphanTypesReachedOnlyThroughDirectionalMembers</c>.
    /// </remarks>
    static Dictionary<ApiType, JsonWireDirection> ResolveWireDirections(
        List<JsExportFunction> functions,
        Dictionary<ApiTypeReferenceIdentity, ApiType> typesByScopedIdentity,
        HashSet<ApiType> discovered)
    {
        var directions = new Dictionary<ApiType, JsonWireDirection>();
        var queue = new Queue<(ApiType Type, JsonWireDirection Direction)>();

        void Seed(
            IReadOnlyList<ApiTypeReferenceIdentity> references,
            JsonWireDirection direction)
        {
            foreach (ApiTypeReferenceIdentity reference in references)
            {
                if (typesByScopedIdentity.TryGetValue(
                        reference,
                        out ApiType? type)
                    && discovered.Contains(type))
                {
                    queue.Enqueue((type, direction));
                }
            }
        }

        foreach (JsExportFunction function in functions)
        {
            Seed(
                function.ReturnWireTypeReferences,
                JsonWireDirection.Serialize);
            Seed(
                function.ParameterWireTypeReferences,
                JsonWireDirection.Deserialize);
        }

        while (queue.Count > 0)
        {
            (ApiType type, JsonWireDirection direction) = queue.Dequeue();
            directions.TryGetValue(type, out JsonWireDirection existing);
            JsonWireDirection updated = existing | direction;
            if (updated == existing)
                continue;
            directions[type] = updated;

            if (type.Kind == "enum" || type.JsonConverterAttributeCount > 0)
                continue;

            foreach (ApiMember member in type.Members)
            {
                if (!JsonWireMemberRules.IsSerialized(member)
                    || member.JsonConverterAttributeCount > 0
                    || member.SignatureModel is null)
                {
                    continue;
                }

                Seed(member.SignatureModel.ReturnTypeReferences, direction);
            }
        }

        return directions;
    }

    static bool HasJsExportAttribute(ApiMember member) =>
        member.HasRuntimeJsExport;

    static string FormatTypeLocation(ApiType type) =>
        type.MetadataToken is { } token
            ? $"type 0x{token:X8}"
            : "serializer context";

    static string FormatMemberLocation(ApiType type, ApiMember member) =>
        member.MetadataToken is { } memberToken
            ? $"member 0x{memberToken:X8}"
            : type.MetadataToken is { } typeToken
                ? $"type 0x{typeToken:X8} member"
                : "JS export member";

    static bool IsTrustedSystemTextJsonType(
        ApiTypeReferenceIdentity? reference,
        string fullName) =>
        reference is not null
        && reference.FullName == fullName
        && HasTopLevelDefinitionName(
            reference.DefinitionName,
            fullName)
        && reference.Assembly.Name == SystemTextJsonAssemblyName
        && PlatformKeys.IsPlatform(
            reference.Assembly.PublicKeyToken);

    /// <summary>
    /// Binds a source-generated context property to the exact
    /// <c>[JsonSerializable]</c> row that generated it. The property metadata
    /// name and the row's structured root identity are both required; a
    /// same-<c>T</c> handwritten getter cannot inherit registration trust.
    /// </summary>
    /// <remarks>
    /// <c>JsonWireContractResolverTests.Build_AuthenticatesOnlyGeneratedCustomNamedContextProperty</c>
    /// and <c>JsExportSurfaceBuilderTests.Build_RejectsAmbiguousOrMalformedGeneratedPropertyIdentities</c>
    /// gate this boundary.
    /// </remarks>
    static IReadOnlyDictionary<string, ApiJsonSerializableRoot>
        GetRegisteredRootProperties(ApiType context)
    {
        var roots = new Dictionary<string, ApiJsonSerializableRoot>(
            StringComparer.Ordinal);
        foreach (ApiJsonSerializableRoot root in context.JsonSerializableRoots)
        {
            string? propertyName = root.TypeInfoPropertyName
                ?? GetDefaultTypeInfoPropertyName(root);
            if (string.IsNullOrEmpty(propertyName)
                || !roots.TryAdd(propertyName, root))
            {
                throw new UnsupportedJsExportSurfaceException(
                    FormatTypeLocation(context),
                    "JsonSerializable source-generated property identity is ambiguous or malformed");
            }
        }
        return roots;
    }

    static bool TryGetRegisteredRootForProperty(
        ApiMember member,
        ApiSignature? signature,
        IReadOnlyDictionary<string, ApiJsonSerializableRoot>
            registeredRootProperties,
        out ApiJsonSerializableRoot root)
    {
        root = null!;
        if (member.GetterToken is null || signature is null)
            return false;
        if (!registeredRootProperties.TryGetValue(
                member.Name,
                out ApiJsonSerializableRoot? candidate)
            || !IsTrustedSystemTextJsonType(
                signature.ReturnTypeDefinitionReference,
                JsonTypeInfoMetadataName))
        {
            return false;
        }

        ApiTypeReferenceIdentity[] propertyRoots =
            signature.ReturnTypeReferences
                .Where(reference => !IsTrustedSystemTextJsonType(
                    reference,
                    JsonTypeInfoMetadataName))
                .Distinct()
                .ToArray();
        if (propertyRoots.Length == 1
            && propertyRoots[0] == candidate.ElementType)
        {
            root = candidate;
            return true;
        }

        if (propertyRoots.Length == 0
            && IsRegisteredIntrinsicStringRoot(candidate))
        {
            root = candidate;
            return true;
        }

        return false;
    }

    static string? GetDefaultTypeInfoPropertyName(
        ApiJsonSerializableRoot root)
    {
        MetadataTypeDefinitionName? definitionName =
            root.ElementType.DefinitionName;
        if (definitionName is null || definitionName.Segments.Length == 0)
            return null;

        return string.Concat(definitionName.Segments)
            + (root.IsArray ? "Array" : "");
    }

    static bool IsRegisteredIntrinsicStringRoot(
        ApiJsonSerializableRoot root) =>
        IsTrustedSystemString(root.ElementType);

    static bool IsTrustedSystemString(
        ApiTypeReferenceIdentity reference) =>
        reference.FullName == "System.String"
        && HasTopLevelDefinitionName(
            reference.DefinitionName,
            "System.String")
        && PlatformKeys.IsPlatform(
            reference.Assembly.PublicKeyToken);

    static bool HasTopLevelDefinitionName(
        MetadataTypeDefinitionName? definitionName,
        string fullName)
    {
        if (definitionName is null)
            return false;
        int separator = fullName.LastIndexOf('.');
        string expectedNamespace = separator < 0
            ? ""
            : fullName[..separator];
        string expectedName = separator < 0
            ? fullName
            : fullName[(separator + 1)..];
        return definitionName.Namespace == expectedNamespace
            && definitionName.Segments is [var segment]
            && segment == expectedName;
    }

    static IEnumerable<string> ExtractCandidateTypeNames(JsExportFunction function)
    {
        foreach (string name in ExtractCandidateTypeNames(function.ReturnType))
            yield return name;

        foreach (ApiParameter parameter in function.Parameters)
        {
            foreach (string name in ExtractCandidateTypeNames(parameter.Type))
                yield return name;
        }
    }

    static IEnumerable<string> ExtractCandidateTypeNames(string? signatureText)
    {
        if (string.IsNullOrEmpty(signatureText))
            yield break;

        string trimmed = signatureText.Trim();
        while (trimmed.EndsWith("[]", StringComparison.Ordinal) || trimmed.EndsWith("?", StringComparison.Ordinal))
            trimmed = trimmed.EndsWith("[]", StringComparison.Ordinal) ? trimmed[..^2] : trimmed[..^1];

        int genericStart = trimmed.IndexOf('<');
        string leading = genericStart >= 0 ? trimmed[..genericStart] : trimmed;
        yield return leading;

        if (genericStart < 0)
            yield break;

        int genericEnd = trimmed.LastIndexOf('>');
        if (genericEnd <= genericStart)
            yield break;

        string inner = trimmed[(genericStart + 1)..genericEnd];
        int depth = 0;
        int segmentStart = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                foreach (string candidate in ExtractCandidateTypeNames(inner[segmentStart..i]))
                    yield return candidate;
                segmentStart = i + 1;
            }
        }

        foreach (string candidate in ExtractCandidateTypeNames(inner[segmentStart..]))
            yield return candidate;
    }
}
