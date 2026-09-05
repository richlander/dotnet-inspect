using System.Collections.Immutable;
using System.Globalization;
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
    const string UnsupportedContextOptionsReason =
        "serializer context options are unsupported";

    /// <summary>
    /// Builds the declaration-only view used by metadata-focused tests and
    /// hand-composed surfaces. Runtime publication requires the overload that
    /// supplies Analysis body evidence.
    /// </summary>
    public static JsExportSurface Build(ApiSurface surface) =>
        Build(surface, bodyIndex: null);

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
        foreach (FilteredRuntimeJsExportFact fact
            in surface.FilteredRuntimeJsExportFacts)
        {
            ValidateFilteredJsExportEvidence(fact);
        }
        foreach (ApiType type in surface.Types)
        {
            foreach (FilteredRuntimeJsExportFact fact
                in type.FilteredRuntimeJsExportFacts)
            {
                ValidateFilteredJsExportEvidence(fact);
            }
            foreach (ApiMember member in type.Members)
            {
                if (!HasJsExportEvidence(member))
                    continue;
                ValidateJsExportEvidence(type, member);
                if (member.Kind != "method")
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "JS exports must be ordinary methods");
                }
                if (!member.IsStatic)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "JS exports must be static");
                }
                if (member.GenericArity != 0)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "generic JS exports have no runtime wrapper");
                }
                if (member.HasMethodBody == false)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "bodyless JS exports have no runtime wrapper");
                }
                int managedNameExportCount =
                    type.Members.Count(candidate =>
                        HasJsExportEvidence(candidate)
                        && candidate.Name == member.Name);
                string? runtimeDispatchKey = null;
                IReadOnlyList<JsExportDelegateParameter>
                    delegateParameters = [];
                if (bodyIndex is null
                        ? member.HasRuntimeJsExportWrapperCandidate
                            == false
                        : member.HasRuntimeJsExportWrapperCandidate
                                != true
                            || !TryGetAuthenticatedRuntimeJsExportWrapper(
                                bodyIndex,
                                surface.AssemblyIdentity,
                                type,
                                member,
                                incompleteBodyTokens,
                                managedNameExportCount,
                                out runtimeDispatchKey,
                                out delegateParameters))
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "JS export has no compiler-generated runtime wrapper");
                }

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
                    DeclaringType = type.FullName,
                    Name = member.Name,
                    RuntimeDispatchKey = runtimeDispatchKey,
                    ReturnType = signature.ReturnType ?? member.ReturnType ?? "void",
                    ReturnTypeReferences =
                        signature.ReturnTypeReferences,
                    Parameters = signature.Parameters,
                    DelegateParameters = delegateParameters,
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
        var registeredJsonTypeInfoGetterModes =
            new Dictionary<int, JsonSourceGenerationMode>();
        var registeredJsonTypeInfoDefaultGetterTokens =
            new Dictionary<int, int>();
        var registeredJsonTypeInfoShapes =
            new Dictionary<int, ApiTypeShape>();
        var unsupportedJsonTypeInfoGetterReasons =
            new Dictionary<int, string>();
        var processedContextScopesByType =
            new Dictionary<ApiType, HashSet<string?>>(
                ReferenceEqualityComparer.Instance);
        var queue = new Queue<(
            string? Name,
            ApiTypeReferenceIdentity? Identity,
            JsonWireNamingPolicy Policy,
            MetadataTypeDefinitionName? ContextDefinitionName)>();

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
            RegisteredRootProperties?
                registeredRootProperties =
                surface.AssemblyIdentity is null
                    ? null
                    : GetRegisteredRootProperties(type);
            int? defaultContextGetterToken =
                surface.AssemblyIdentity is { } currentAssembly
                    ? GetDefaultContextGetterToken(
                        type,
                        currentAssembly,
                        requireStructuredIdentity:
                            bodyIndex is not null)
                    : null;

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
                bool hasUnsupportedContextOptions =
                    policy == JsonWireNamingPolicy.Unsupported;
                ApiSignature? signature = member.SignatureModel;
                IReadOnlyList<ApiTypeReferenceIdentity>? references =
                    signature?.ReturnTypeReferences;
                if (registeredRootProperties is not null)
                {
                    switch (TryGetRegisteredRootForProperty(
                            member,
                            signature,
                            registeredRootProperties,
                            out ApiJsonSerializableRoot? root))
                    {
                        case RegisteredRootPropertyMatch.Supported:
                            if (member.GetterToken is { } getterToken)
                            {
                                if (type
                                    .HasSystemTextJsonSourceGenerationMarker
                                    == false)
                                {
                                    unsupportedJsonTypeInfoGetterReasons[
                                        getterToken] =
                                            "serializer context has no authentic System.Text.Json source-generation marker";
                                }
                                else if (hasUnsupportedContextOptions)
                                {
                                    unsupportedJsonTypeInfoGetterReasons[
                                        getterToken] =
                                        UnsupportedContextOptionsReason;
                                }
                                else if (type
                                        .HasSystemTextJsonSourceGenerationMarker
                                            == true
                                    && defaultContextGetterToken is null)
                                {
                                    unsupportedJsonTypeInfoGetterReasons[
                                        getterToken] =
                                            "serializer context has no authentic default-instance getter";
                                }
                                else if (bodyIndex is not null
                                    && type
                                        .HasSystemTextJsonSourceGenerationMarker
                                            == true
                                    && defaultContextGetterToken is { } generatedDefaultGetter
                                    && !HasAuthenticatedGeneratedContextImplementation(
                                        bodyIndex,
                                        type,
                                        member,
                                        getterToken,
                                        generatedDefaultGetter,
                                        incompleteBodyTokens))
                                {
                                    unsupportedJsonTypeInfoGetterReasons[
                                        getterToken] =
                                            "serializer context has no authentic source-generated implementation";
                                }
                                else
                                {
                                    JsonSourceGenerationMode effectiveMode =
                                        GetEffectiveGenerationMode(
                                            type.JsonSourceGenerationMode,
                                            root!.GenerationMode);
                                    if (!registeredJsonTypeInfoGetterModes.TryAdd(
                                            getterToken,
                                            effectiveMode)
                                        && registeredJsonTypeInfoGetterModes[
                                                getterToken] != effectiveMode)
                                    {
                                        throw new UnsupportedJsExportSurfaceException(
                                            FormatMemberLocation(type, member),
                                            "serializer-context property generation modes conflict");
                                    }
                                    if (defaultContextGetterToken is { } defaultGetter
                                        && !registeredJsonTypeInfoDefaultGetterTokens.TryAdd(
                                            getterToken,
                                            defaultGetter)
                                        && registeredJsonTypeInfoDefaultGetterTokens[
                                                getterToken] != defaultGetter)
                                    {
                                        throw new UnsupportedJsExportSurfaceException(
                                            FormatMemberLocation(type, member),
                                            "serializer-context default-instance evidence conflicts");
                                    }
                                    if (!registeredJsonTypeInfoShapes.TryAdd(
                                            getterToken,
                                            root!.Type!)
                                        && !registeredJsonTypeInfoShapes[
                                                getterToken].Equals(
                                                root.Type))
                                    {
                                        throw new UnsupportedJsExportSurfaceException(
                                            FormatMemberLocation(type, member),
                                            "serializer-context root shapes conflict");
                                    }
                                }
                            }
                            foreach (ApiTypeReferenceIdentity reference
                                in EnumerateNamedTypes(root!.Type!))
                            {
                                queue.Enqueue((
                                    null,
                                    reference,
                                    policy,
                                    type.DefinitionName));
                            }
                            break;
                        case RegisteredRootPropertyMatch.Unsupported:
                            if (member.GetterToken is { } unsupportedGetter)
                            {
                                string reason =
                                    !IsGeneratedRootPropertyShape(
                                        member,
                                        signature)
                                        ? "serializer-context property is not the parameterless generated getter"
                                    : registeredRootProperties
                                        .DuplicatePropertyNames
                                        .Contains(member.Name)
                                        ? "serializer-context property identity is duplicated"
                                    : root?.UnsupportedReason
                                        ?? "serializer root property identity is ambiguous";
                                if (!unsupportedJsonTypeInfoGetterReasons.TryAdd(
                                        unsupportedGetter,
                                        reason)
                                    && unsupportedJsonTypeInfoGetterReasons[
                                            unsupportedGetter] != reason)
                                {
                                    throw new UnsupportedJsExportSurfaceException(
                                        FormatMemberLocation(type, member),
                                        "serializer-context property failure evidence conflicts");
                                }
                            }
                            break;
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
                        queue.Enqueue((
                            null,
                            reference,
                            policy,
                            type.DefinitionName));
                    }
                }
                else
                {
                    foreach (string candidate
                        in ExtractCandidateTypeNames(rootTypeName))
                    {
                        queue.Enqueue((
                            candidate,
                            null,
                            policy,
                            type.DefinitionName));
                    }
                }
            }
        }

        while (queue.Count > 0)
        {
            (
                string? name,
                ApiTypeReferenceIdentity? identity,
                JsonWireNamingPolicy namingPolicy,
                MetadataTypeDefinitionName? contextDefinitionName) =
                queue.Dequeue();
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

            policies.Add(namingPolicy);

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

            if (!TryRegisterContextScope(
                    processedContextScopesByType,
                    type,
                    contextDefinitionName))
            {
                continue;
            }

            if (type.Kind == "enum"
                || type.JsonConverterAttributeCount > 0)
                continue;

            foreach (ApiMember member in type.Members)
            {
                if (JsonWireMemberRules
                    .RequiresContextRelativeValueTypeAccessibilityEvidence(
                        member,
                        surface.AssemblyIdentity,
                        typesByScopedIdentity,
                        contextDefinitionName))
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "[JsonInclude] members whose same-assembly value types depend on nested JsonSerializerContext accessibility are unsupported");
                }
                if (!JsonWireMemberRules.IsSerialized(
                        member,
                        surface.AssemblyIdentity,
                        typesByScopedIdentity)
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
                        queue.Enqueue((
                            null,
                            reference,
                            namingPolicy,
                            contextDefinitionName));
                    }
                }
                else
                {
                    foreach (string candidate
                        in ExtractCandidateTypeNames(propertyType))
                    {
                        queue.Enqueue((
                            candidate,
                            null,
                            namingPolicy,
                            contextDefinitionName));
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
                        registeredJsonTypeInfoGetterModes,
                        registeredJsonTypeInfoDefaultGetterTokens,
                        registeredJsonTypeInfoShapes,
                        unsupportedJsonTypeInfoGetterReasons);
                }
            }
        }

        return new JsExportSurface
        {
            AssemblyIdentity = surface.AssemblyIdentity,
            Functions = functions,
            Records = records,
            Enums = enums,
            AllTypes = surface.Types,
            WireDirections = ResolveWireDirections(
                functions,
                surface.AssemblyIdentity,
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
    /// Discovery separately walks the direction-independent
    /// <see cref="JsonWireMemberRules.IsSerialized(ApiMember)"/> union so
    /// directional declarations cannot orphan types. This pass follows only
    /// members present in the active direction so an absent edge cannot falsely
    /// make a nested type bidirectional. Gated by
    /// <c>DtsEmitterTests.Emit_DoesNotOrphanTypesReachedOnlyThroughDirectionalMembers</c>
    /// and
    /// <c>DtsEmitterTests.Emit_PropagatesOnlyMembersPresentInTheActiveDirection</c>.
    /// </remarks>
    static Dictionary<ApiType, JsonWireDirection> ResolveWireDirections(
        List<JsExportFunction> functions,
        ApiAssemblyIdentity? assemblyIdentity,
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
                if (!JsonWireMemberRules.IsSerialized(
                        member,
                        direction,
                        assemblyIdentity,
                        typesByScopedIdentity)
                    || member.JsonConverterAttributeCount > 0
                    || member.SignatureModel is null)
                {
                    continue;
                }

                Seed(member.SignatureModel.ReturnTypeReferences, direction);
            }
        }

        if (directions.Count > 0)
        {
            foreach (ApiType type in discovered)
                directions.TryAdd(type, JsonWireDirection.None);
        }

        return directions;
    }

    static bool HasJsExportEvidence(ApiMember member) =>
        member.HasRuntimeJsExport
        || member.RuntimeJsExportAttributeCount > 0
        || member.HasMalformedRuntimeJsExportAttribute;

    static bool TryRegisterContextScope(
        Dictionary<ApiType, HashSet<string?>> processedContextScopesByType,
        ApiType type,
        MetadataTypeDefinitionName? contextDefinitionName)
    {
        if (!processedContextScopesByType.TryGetValue(
                type,
                out HashSet<string?>? scopes))
        {
            scopes = new HashSet<string?>(StringComparer.Ordinal);
            processedContextScopesByType.Add(type, scopes);
        }

        return scopes.Add(
            contextDefinitionName is null
                ? null
                : $"{contextDefinitionName.Namespace}:{string.Join(".", contextDefinitionName.Segments)}");
    }

    static int? GetDefaultContextGetterToken(
        ApiType context,
        ApiAssemblyIdentity assembly,
        bool requireStructuredIdentity)
    {
        if (requireStructuredIdentity
            && context.DefinitionName is null)
            return null;

        var expectedReturnType = new ApiTypeReferenceIdentity(
            assembly,
            context.FullName,
            context.DefinitionName);
        ApiMember[] candidates =
        [
            .. context.Members.Where(member =>
                member.Kind == "property"
                && member.Name == "Default"
                && member.IsStatic
                && member.HasGetter == true
                && member.GetterToken is not null
                && member.SignatureModel is { ParameterCount: 0 } signature
                && signature.ReturnTypeReferences.Count == 1
                && signature.ReturnTypeReferences[0].Equals(
                    expectedReturnType)),
        ];
        return candidates is [{ GetterToken: { } getterToken }]
            ? getterToken
            : null;
    }

    static void ValidateJsExportEvidence(ApiType type, ApiMember member)
    {
        // A manually composed surface from an older producer carries only the
        // legacy Boolean. Extracted surfaces always carry a count, so retain
        // that compatibility path while failing closed for every authentic row.
        if (member.RuntimeJsExportAttributeCount == 0
            && !member.HasMalformedRuntimeJsExportAttribute)
        {
            return;
        }

        if (member.RuntimeJsExportAttributeCount != 1
            || member.HasMalformedRuntimeJsExportAttribute
            || !member.HasRuntimeJsExport)
        {
            throw new UnsupportedJsExportSurfaceException(
                FormatMemberLocation(type, member),
                "JSExport metadata is malformed or declares duplicate authentic rows");
        }
    }

    static void ValidateFilteredJsExportEvidence(
        FilteredRuntimeJsExportFact fact)
    {
        string location = $"member 0x{fact.MetadataToken:X8}";
        if (fact.AttributeCount != 1
            || fact.HasMalformedRow
            || !fact.HasValidRow)
        {
            throw new UnsupportedJsExportSurfaceException(
                location,
                "JSExport metadata is malformed or declares duplicate authentic rows");
        }

        throw new UnsupportedJsExportSurfaceException(
            location,
            "JS exports on filtered MethodDefs are not ordinary API methods");
    }

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
    /// and <c>JsExportSurfaceBuilderTests.Build_DefersUnreachedAmbiguousAndRejectsMalformedGeneratedPropertyIdentities</c>
    /// gate this boundary.
    /// </remarks>
    static RegisteredRootProperties
        GetRegisteredRootProperties(ApiType context)
    {
        var roots = new Dictionary<string, ApiJsonSerializableRoot>(
            StringComparer.Ordinal);
        var ambiguousPropertyNames = new HashSet<string>(
            StringComparer.Ordinal);
        var unnamedUnsupportedRoots = new List<ApiJsonSerializableRoot>();
        foreach (ApiJsonSerializableRoot root in context.JsonSerializableRoots)
        {
            string? propertyName = root.TypeInfoPropertyName
                ?? GetDefaultTypeInfoPropertyName(root);
            if (propertyName is null && root.Type is null)
            {
                // The row itself is authentic and retained as unsupported
                // evidence. It can only block generation if an otherwise
                // unmatched trusted getter in this context reaches an export.
                unnamedUnsupportedRoots.Add(root);
                continue;
            }
            if (string.IsNullOrEmpty(propertyName))
            {
                throw new UnsupportedJsExportSurfaceException(
                    FormatTypeLocation(context),
                    "JsonSerializable source-generated property identity is ambiguous or malformed");
            }

            if (ambiguousPropertyNames.Contains(propertyName))
                continue;
            if (!roots.TryAdd(propertyName, root))
            {
                roots.Remove(propertyName);
                ambiguousPropertyNames.Add(propertyName);
            }
        }
        var duplicatePropertyNames = new HashSet<string>(
            roots
                .Where(candidate =>
                    context.Members.Count(member =>
                        IsMatchingGeneratedRootProperty(
                            member,
                            candidate.Key,
                            candidate.Value))
                        > 1)
                .Select(candidate => candidate.Key),
            StringComparer.Ordinal);
        return new(
            roots,
            ambiguousPropertyNames,
            duplicatePropertyNames,
            unnamedUnsupportedRoots);
    }

    static bool IsMatchingGeneratedRootProperty(
        ApiMember member,
        string propertyName,
        ApiJsonSerializableRoot root)
    {
        ApiSignature? signature = member.SignatureModel;
        return member.Kind == "property"
            && member.Name == propertyName
            && signature is not null
            && IsGeneratedRootPropertyShape(member, signature)
            && TryGetTrustedJsonTypeInfoArgument(
                signature,
                out ApiTypeShape? propertyRoot)
            && root.Type is not null
            && propertyRoot is not null
            && AreEquivalentGeneratedRootShapes(
                root.Type,
                propertyRoot);
    }

    static RegisteredRootPropertyMatch TryGetRegisteredRootForProperty(
        ApiMember member,
        ApiSignature? signature,
        RegisteredRootProperties registeredRootProperties,
        out ApiJsonSerializableRoot? root)
    {
        root = null;
        if (member.GetterToken is null || signature is null)
        {
            return RegisteredRootPropertyMatch.None;
        }
        if (!IsGeneratedRootPropertyShape(member, signature))
        {
            if (registeredRootProperties.Roots.TryGetValue(
                    member.Name,
                    out ApiJsonSerializableRoot? invalidCandidate)
                && IsTrustedJsonTypeInfoProperty(signature))
            {
                root = invalidCandidate;
                return RegisteredRootPropertyMatch.Unsupported;
            }

            return RegisteredRootPropertyMatch.None;
        }
        if (registeredRootProperties.AmbiguousPropertyNames.Contains(
                member.Name)
            && IsTrustedJsonTypeInfoProperty(signature))
        {
            return RegisteredRootPropertyMatch.Unsupported;
        }
        if (!TryGetTrustedJsonTypeInfoArgument(
                signature,
                out ApiTypeShape? propertyRoot))
        {
            return RegisteredRootPropertyMatch.None;
        }
        if (!registeredRootProperties.Roots.TryGetValue(
                member.Name,
                out ApiJsonSerializableRoot? candidate))
        {
            if (registeredRootProperties.UnnamedUnsupportedRoots.Count == 0)
                return RegisteredRootPropertyMatch.None;

            root = registeredRootProperties.UnnamedUnsupportedRoots.Count == 1
                ? registeredRootProperties.UnnamedUnsupportedRoots[0]
                : null;
            return RegisteredRootPropertyMatch.Unsupported;
        }

        root = candidate;
        if (candidate.Type is null)
        {
            return RegisteredRootPropertyMatch.Unsupported;
        }

        if (propertyRoot is null
            || !AreEquivalentGeneratedRootShapes(
                candidate.Type,
                propertyRoot))
        {
            return RegisteredRootPropertyMatch.None;
        }

        if (registeredRootProperties.DuplicatePropertyNames.Contains(
                member.Name))
        {
            return RegisteredRootPropertyMatch.Unsupported;
        }

        return candidate.UnsupportedReason is null
            ? RegisteredRootPropertyMatch.Supported
            : RegisteredRootPropertyMatch.Unsupported;
    }

    static bool IsGeneratedRootPropertyShape(
        ApiMember member,
        ApiSignature? signature) =>
        signature is not null
        && !member.IsStatic
        && member.HasSetter == false
        && (member.IndexParameterCount
                ?? signature.ParameterCount)
            == 0;

    /// <summary>
    /// Authenticates the generated wrapper chain for one export: a registration
    /// whose name and signature hash are exact, a reachable
    /// wrapper-&gt;stub-&gt;export call path, and a descriptor whose elements are
    /// linked to that registration's span argument.
    /// </summary>
    /// <remarks>
    /// Reachability is required at every hop because a body can retain the
    /// expected calls behind a <c>ret</c> that makes none of them run.
    /// <c>GeneratedJsExportAuthenticationTests.Build_RejectsUnreachableGeneratedWrapperEntry</c>
    /// gates that.
    /// </remarks>
    static bool TryGetAuthenticatedRuntimeJsExportWrapper(
        LibraryBodyIndex bodyIndex,
        ApiAssemblyIdentity? assemblyIdentity,
        ApiType declaringType,
        ApiMember export,
        IReadOnlySet<int> incompleteBodyTokens,
        int managedNameExportCount,
        out string? runtimeDispatchKey,
        out IReadOnlyList<JsExportDelegateParameter> delegateParameters)
    {
        runtimeDispatchKey = null;
        delegateParameters = [];
        if (export.MetadataToken is not { } exportToken
            || export.RuntimeJsExportWrapperCandidates is not
                { Count: > 0 } candidates)
            return false;
        string? runtimeBindingName = RuntimeBindingName(
            assemblyIdentity,
            declaringType,
            export.Name);
        if (runtimeBindingName is null)
            return false;

        IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
            callsByEvidenceMethod =
                bodyIndex.GetDirectCallsByEvidenceMethod();
        foreach (ImmutableArray<DirectCall> wrapperCalls
            in callsByEvidenceMethod.Values)
        {
            MethodIdentity wrapper = wrapperCalls[0].EvidenceMethod;
            RuntimeJsExportWrapperCandidate? candidate =
                candidates.FirstOrDefault(candidate =>
                    candidate.WrapperMethodToken
                        == wrapper.MetadataToken);
            if (candidate is null
                || candidate.ModuleVersionId is not
                    { } moduleVersionId
                || moduleVersionId == Guid.Empty
                || moduleVersionId != wrapper.ModuleVersionId
                || !IsGeneratedRuntimeWrapper(wrapper, export.Name)
                || !IsAuthenticatedRuntimeRegistration(
                    callsByEvidenceMethod,
                    candidate,
                    runtimeBindingName,
                    wrapper,
                    export.Name,
                    incompleteBodyTokens,
                    managedNameExportCount,
                    out DirectCall? registration,
                    out int signatureHash))
                continue;
            if (incompleteBodyTokens.Contains(wrapper.MetadataToken))
                continue;

            foreach (DirectCall wrapperCall in wrapperCalls)
            {
                if (wrapperCall.Kind != CallKind.Call
                    || wrapperCall.IsReachable != true
                    || !callsByEvidenceMethod.TryGetValue(
                        wrapperCall.CalleeDefinitionToken,
                        out ImmutableArray<DirectCall>
                            stubCalls))
                {
                    continue;
                }

                MethodIdentity stub = stubCalls[0].EvidenceMethod;
                if (!IsGeneratedRuntimeWrapperStub(
                        wrapper,
                        stub)
                    || incompleteBodyTokens.Contains(
                        stub.MetadataToken))
                {
                    continue;
                }

                DirectCall? exportCall = stubCalls.FirstOrDefault(call =>
                    call.Kind == CallKind.Call
                    && call.IsReachable == true
                    && call.CalleeDefinitionToken == exportToken
                    && call.Callee.DeclaringType.Equals(
                        wrapper.DeclaringType));
                if (exportCall is null)
                    continue;

                if (!IsAuthenticatedRegistrationDescriptor(
                        callsByEvidenceMethod,
                        candidate,
                        registration!,
                        exportCall.Callee,
                        declaringType,
                        export,
                        out IReadOnlyList<JsExportDelegateParameter>
                            authenticatedDelegates))
                {
                    continue;
                }

                runtimeDispatchKey =
                    export.Name
                    + "."
                    + signatureHash.ToString(
                        CultureInfo.InvariantCulture);
                delegateParameters = authenticatedDelegates;
                return true;
            }
        }

        return false;
    }

    static bool IsAuthenticatedRuntimeRegistration(
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
            callsByEvidenceMethod,
        RuntimeJsExportWrapperCandidate candidate,
        string runtimeBindingName,
        MethodIdentity wrapper,
        string exportName,
        IReadOnlySet<int> incompleteBodyTokens,
        int managedNameExportCount,
        out DirectCall? registration,
        out int signatureHash)
    {
        registration = null;
        signatureHash = 0;
        if (candidate.RegistrationCount <= 0
            || candidate.ModuleVersionId is not { } moduleVersionId
            || incompleteBodyTokens.Contains(
                candidate.RegistrationMethodToken)
            || !callsByEvidenceMethod.TryGetValue(
                candidate.RegistrationMethodToken,
                out ImmutableArray<DirectCall> registrationCalls))
        {
            return false;
        }

        if (!registrationCalls.All(call =>
                call.EvidenceMethod.ModuleVersionId == moduleVersionId))
        {
            return false;
        }

        DirectCall[] bindings =
        [
            .. registrationCalls.Where(call =>
                call.Kind == CallKind.Call
                && IsRuntimeBindManagedFunction(call.Callee)),
        ];
        if (bindings.Length != candidate.RegistrationCount)
            return false;

        if (!RuntimeJsExportWrapperName.TryGetSignatureHash(
                wrapper.Name,
                exportName,
                out uint expectedSignatureHash))
        {
            return false;
        }

        DirectCall[] named =
        [
            .. bindings.Where(call => string.Equals(
                call.FirstArgumentStringLiteral,
                runtimeBindingName,
                StringComparison.Ordinal)),
        ];
        if (named.Length != managedNameExportCount)
        {
            return false;
        }

        DirectCall[] matching =
        [
            .. named.Where(call => HasSignatureHash(
                call,
                expectedSignatureHash)),
        ];
        if (matching is not [var match]
            || match.IsReachable != true
            || match.ResolvedArgumentValues[1].Single is not
                {
                    Kind: ResolvedValueSourceKind.Int32Literal,
                    Int32Value: { } matchedSignatureHash,
                })
        {
            return false;
        }

        registration = match;
        signatureHash = matchedSignatureHash;
        return true;
    }

    static bool HasSignatureHash(
        DirectCall registration,
        uint expectedSignatureHash) =>
        registration.ResolvedArgumentValues.Count == 3
        && registration.ResolvedArgumentValues[1].Single is
            {
                Kind: ResolvedValueSourceKind.Int32Literal,
                Int32Value: { } signatureHash,
            }
        && unchecked((uint)signatureHash) == expectedSignatureHash;

    /// <summary>
    /// Requires the registration's marshaler descriptor to be the span Analysis
    /// linked to this <c>BindManagedFunction</c> call, one element per marshaled
    /// position, and each element's factory to be compatible with the export's
    /// own managed signature.
    /// </summary>
    /// <remarks>
    /// Deliberately not a reimplementation of the runtime generator's marshaling
    /// policy. It recognizes the descriptor graph the compiler actually emitted
    /// and rejects a factory outside the compatible set for that managed type,
    /// which is what closes a swapped element such as <c>Task()</c> becoming
    /// <c>get_String</c>. A managed type outside the recognized marshaling set
    /// fails visibly rather than being waved through.
    /// <c>GeneratedJsExportAuthenticationTests.Build_RejectsRegistrationWithSwappedDescriptorElement</c>
    /// gates the swap.
    /// </remarks>
    static bool IsAuthenticatedRegistrationDescriptor(
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
            callsByEvidenceMethod,
        RuntimeJsExportWrapperCandidate candidate,
        DirectCall registration,
        MemberRef exportSignature,
        ApiType declaringType,
        ApiMember export,
        out IReadOnlyList<JsExportDelegateParameter> delegateParameters)
    {
        delegateParameters = [];
        if (!callsByEvidenceMethod.TryGetValue(
                candidate.RegistrationMethodToken,
                out ImmutableArray<DirectCall> registrationCalls))
        {
            return false;
        }

        var callsByOffset = new Dictionary<int, DirectCall>();
        foreach (DirectCall call in registrationCalls)
            callsByOffset[call.ILOffset] = call;

        SpanArgumentElements? descriptor =
            registration.SpanArgumentSources.ForArgument(2);
        if (descriptor is not { IsResolved: true }
            || descriptor.Elements.Count
                != exportSignature.ParameterTypes.Length + 1)
        {
            return false;
        }

        string location = FormatMemberLocation(declaringType, export);
        if (!DescribesManagedType(
                descriptor.Elements[0],
                exportSignature.ReturnType,
                isReturn: true,
                callsByOffset,
                location))
        {
            return false;
        }

        for (int index = 0;
            index < exportSignature.ParameterTypes.Length;
            index++)
        {
            if (!DescribesManagedType(
                    descriptor.Elements[index + 1],
                    exportSignature.ParameterTypes[index],
                    isReturn: false,
                    callsByOffset,
                    location))
            {
                return false;
            }
        }

        var delegates = new List<JsExportDelegateParameter>();
        for (int index = 0;
            index < exportSignature.ParameterTypes.Length;
            index++)
        {
            if (TryGetDelegateShape(
                    exportSignature.ParameterTypes[index],
                    out JsExportDelegateKind kind,
                    out ImmutableArray<TypeRef> parameterTypes,
                    out TypeRef? returnType))
            {
                delegates.Add(new JsExportDelegateParameter
                {
                    ParameterIndex = index,
                    Kind = kind,
                    ParameterTypes = parameterTypes,
                    ReturnType = returnType,
                });
            }
        }
        delegateParameters = delegates;
        return true;
    }

    static bool DescribesManagedType(
        ResolvedValueSet element,
        TypeRef managed,
        bool isReturn,
        IReadOnlyDictionary<int, DirectCall> callsByOffset,
        string location)
    {
        if (TryGetDelegateShape(
                managed,
                out _,
                out _,
                out TypeRef? delegateReturnType)
            && delegateReturnType is not null
            && IsTaskType(delegateReturnType))
        {
            throw new UnsupportedJsExportSurfaceException(
                location,
                $"JS export marshaling of '{managed.ToDisplayString()}' is "
                    + "recognized but not supported: Promise-returning "
                    + "delegates are not synchronous callbacks");
        }

        if (!TryGetMarshalerRule(
                managed,
                isReturn,
                out string[] factoryNames,
                out string[] unsupportedFactoryNames,
                out ImmutableArray<TypeRef> elementTypes))
        {
            throw new UnsupportedJsExportSurfaceException(
                location,
                $"JS export marshaling of '{managed.ToDisplayString()}' is not recognized");
        }

        if (element.Single is not
                {
                    Kind: ResolvedValueSourceKind.CallResult,
                    ILOffset: var factoryOffset,
                }
            || !callsByOffset.TryGetValue(
                factoryOffset,
                out DirectCall? factory)
            || factory.IsReachable != true
            || !IsJsMarshalerTypeFactory(factory.Callee))
        {
            return false;
        }

        // An authentic descriptor the TypeScript type system cannot describe is a supported
        // wire shape this tool does not support yet, which is a different answer from "this
        // is not a generated JS export" and has to read that way.
        if (unsupportedFactoryNames.Contains(
            factory.Callee.Name,
            StringComparer.Ordinal))
        {
            throw new UnsupportedJsExportSurfaceException(
                location,
                $"JS export marshaling of '{managed.ToDisplayString()}' as "
                    + $"'{factory.Callee.Name}' is recognized but not supported: "
                    + "TypeScript emission describes every 'long' as 'number', "
                    + "which does not describe a JavaScript BigInt. Use "
                    + "[JSMarshalAs<JSType.Number>] instead");
        }

        if (!factoryNames.Contains(
            factory.Callee.Name,
            StringComparer.Ordinal))
        {
            return false;
        }

        if (factory.Callee.ParameterTypes.Length != elementTypes.Length
            || factory.ResolvedArgumentValues.Count != elementTypes.Length)
            return false;

        for (int index = 0; index < elementTypes.Length; index++)
        {
            if (!DescribesManagedType(
                    factory.ResolvedArgumentValues[index],
                    elementTypes[index],
                    isReturn: false,
                    callsByOffset,
                    location))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The <c>JSMarshalerType</c> factories compatible with one managed type,
    /// and the element type whose own descriptor a composite factory must carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A managed type maps to a small set rather than to one name because
    /// <c>[JSMarshalAs]</c> legitimately selects among the alternatives for the
    /// same declared type. Returning false means "not recognized", which the
    /// caller reports rather than silently accepting.
    /// </para>
    /// <para>
    /// <paramref name="unsupportedFactoryNames"/> holds the descriptors that are
    /// authentic for the managed type but that this tool cannot describe in
    /// TypeScript yet. <c>get_BigInt64</c> is the case: <c>TsTypeMapper</c> emits
    /// every <c>long</c> as <c>number</c>, which silently misdescribes a
    /// JavaScript <c>BigInt</c>, so the surface rejects it visibly instead.
    /// <c>JsExportSurfaceBuilderTests.Build_RejectsBigIntMarshaledLongExport</c>
    /// and
    /// <c>JsExportSurfaceBuilderTests.Build_PublishesNumberMarshaledLongExport</c>
    /// gate the pair.
    /// </para>
    /// </remarks>
    static bool TryGetMarshalerRule(
        TypeRef managed,
        bool isReturn,
        out string[] factoryNames,
        out string[] unsupportedFactoryNames,
        out ImmutableArray<TypeRef> elementTypes)
    {
        factoryNames = [];
        unsupportedFactoryNames = [];
        elementTypes = [];
        if (TryGetDelegateShape(
                managed,
                out JsExportDelegateKind delegateKind,
                out ImmutableArray<TypeRef> delegateParameterTypes,
                out TypeRef? delegateReturnType))
        {
            factoryNames =
            [
                delegateKind == JsExportDelegateKind.Action
                    ? "Action"
                    : "Function",
            ];
            elementTypes = delegateReturnType is null
                ? delegateParameterTypes
                : [.. delegateParameterTypes, delegateReturnType];
            return true;
        }

        switch (managed.Kind)
        {
            case TypeRefKind.SzArray when managed.ElementType is { } array:
                factoryNames = ["Array"];
                elementTypes = [array];
                return true;
            case TypeRefKind.GenericInstance
                when managed is
                {
                    ElementType: { } definition,
                    TypeArguments: [var argument],
                }:
            {
                if (IsCoreLibType(
                    definition,
                    "System.Threading.Tasks",
                    "Task`1"))
                {
                    factoryNames = ["Task"];
                    elementTypes = [argument];
                    return true;
                }
                if (IsCoreLibType(definition, "System", "Nullable`1"))
                {
                    factoryNames = ["Nullable"];
                    elementTypes = [argument];
                    return true;
                }
                if (IsCoreLibType(definition, "System", "Span`1"))
                {
                    factoryNames = ["Span"];
                    elementTypes = [argument];
                    return true;
                }
                if (IsCoreLibType(definition, "System", "ArraySegment`1"))
                {
                    factoryNames = ["ArraySegment"];
                    elementTypes = [argument];
                    return true;
                }
                return false;
            }
            case TypeRefKind.Definition:
                break;
            default:
                return false;
        }

        if (IsTrustedRuntimeJavaScriptType(managed, "JSObject"))
        {
            factoryNames = ["get_JSObject"];
            return true;
        }
        if (IsCoreLibType(managed, "System.Threading.Tasks", "Task"))
        {
            factoryNames = ["Task"];
            return true;
        }
        if (managed.Assembly != TypeRef.CoreLibrary
            || !managed.TrustedFrameworkAssembly)
        {
            return false;
        }

        factoryNames = (managed.Namespace, managed.Name) switch
        {
            ("System", "Void") when isReturn => ["get_Void", "get_Discard"],
            ("System", "String") => ["get_String"],
            ("System", "Boolean") => ["get_Boolean"],
            ("System", "Char") => ["get_Char"],
            ("System", "Byte") => ["get_Byte"],
            ("System", "Int16") => ["get_Int16"],
            ("System", "Int32") => ["get_Int32"],
            ("System", "Int64") => ["get_Int52"],
            ("System", "Single") => ["get_Single"],
            ("System", "Double") => ["get_Double"],
            ("System", "IntPtr") => ["get_IntPtr"],
            ("System", "DateTime") => ["get_DateTime"],
            ("System", "DateTimeOffset") => ["get_DateTimeOffset"],
            ("System", "Exception") => ["get_Exception"],
            ("System", "Object") => ["get_Object"],
            _ => [],
        };
        if (managed is { Namespace: "System", Name: "Int64" })
        {
            unsupportedFactoryNames = ["get_BigInt64"];
        }

        return factoryNames.Length != 0
            && HasExactDefinitionName(
                managed,
                managed.Namespace,
                managed.Name);
    }

    internal static bool TryGetDelegateShape(
        TypeRef managed,
        out JsExportDelegateKind kind,
        out ImmutableArray<TypeRef> parameterTypes,
        out TypeRef? returnType)
    {
        kind = default;
        parameterTypes = [];
        returnType = null;

        if (IsCoreLibType(managed, "System", "Action"))
        {
            kind = JsExportDelegateKind.Action;
            return true;
        }

        if (managed is not
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType: { } definition,
                TypeArguments: { Length: > 0 } arguments,
            })
        {
            return false;
        }

        if (IsCoreLibType(
                definition,
                "System",
                $"Action`{arguments.Length}")
            && arguments.Length <= 3)
        {
            kind = JsExportDelegateKind.Action;
            parameterTypes = arguments;
            return true;
        }

        if (!IsCoreLibType(
                definition,
                "System",
                $"Func`{arguments.Length}")
            || arguments.Length - 1 > 3)
        {
            return false;
        }

        kind = JsExportDelegateKind.Func;
        parameterTypes = arguments[..^1];
        returnType = arguments[^1];
        return true;
    }

    static bool IsTaskType(TypeRef type) =>
        IsCoreLibType(
            type,
            "System.Threading.Tasks",
            "Task")
        || type is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { } definition,
        }
        && IsCoreLibType(
            definition,
            "System.Threading.Tasks",
            "Task`1");

    static bool IsJsMarshalerTypeFactory(MemberRef method) =>
        method.Kind == MemberKind.Method
        && !method.HasThis
        && method.GenericArity == 0
        && IsTrustedRuntimeJavaScriptType(
            method.DeclaringType,
            "JSMarshalerType")
        && IsTrustedRuntimeJavaScriptType(
            method.ReturnType,
            "JSMarshalerType")
        && method.ParameterTypes.All(parameter =>
            IsTrustedRuntimeJavaScriptType(parameter, "JSMarshalerType"));

    static bool IsCoreLibType(
        TypeRef type,
        string expectedNamespace,
        string expectedName) =>
        type is
        {
            Kind: TypeRefKind.Definition,
            Assembly: TypeRef.CoreLibrary,
            TrustedFrameworkAssembly: true,
        }
        && type.Namespace == expectedNamespace
        && type.Name == expectedName
        && HasExactDefinitionName(type, expectedNamespace, expectedName);

    static string? RuntimeBindingName(
        ApiAssemblyIdentity? assemblyIdentity,
        ApiType declaringType,
        string exportName)
    {
        if (assemblyIdentity is null
            || declaringType.DefinitionName is not
                { Segments.Length: > 0 } definitionName)
        {
            return null;
        }

        string typeName = string.Join(
            '/',
            definitionName.Segments.Select(
                MetadataNameArity.StripFromSegment));
        if (!string.IsNullOrEmpty(definitionName.Namespace))
        {
            typeName =
                $"{definitionName.Namespace}.{typeName}";
        }

        return $"[{assemblyIdentity.Name}]{typeName}:{exportName}";
    }

    static bool IsRuntimeBindManagedFunction(MemberRef method) =>
        method.Kind == MemberKind.Method
        && !method.HasThis
        && method.GenericArity == 0
        && method.Name == "BindManagedFunction"
        && IsTrustedRuntimeJavaScriptType(
            method.DeclaringType,
            "JSFunctionBinding")
        && IsTrustedRuntimeJavaScriptType(
            method.ReturnType,
            "JSFunctionBinding")
        && method.ParameterTypes is
        [
            var name,
            var signatureHash,
            var marshalerTypes,
        ]
        && IsCoreType(name, "String")
        && IsCoreType(signatureHash, "Int32")
        && marshalerTypes is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { } spanType,
            TypeArguments:
            [
                var marshalerType,
            ],
        }
        && IsCoreType(spanType, "ReadOnlySpan`1")
        && IsTrustedRuntimeJavaScriptType(
            marshalerType,
            "JSMarshalerType");

    static bool IsTrustedRuntimeJavaScriptType(
        TypeRef type,
        string name) =>
        type is
        {
            Kind: TypeRefKind.Definition,
            Assembly:
                "System.Runtime.InteropServices.JavaScript",
            Namespace:
                "System.Runtime.InteropServices.JavaScript",
            TrustedFrameworkAssembly: true,
        }
        && type.Name == name;

    static bool IsCoreType(TypeRef type, string name) =>
        IsCoreLibType(type, "System", name);

    static bool IsGeneratedRuntimeWrapper(
        MethodIdentity method,
        string exportName) =>
        RuntimeJsExportWrapperName.IsCandidateFor(
            method.Name,
            exportName)
        && method.IsStatic
        && method.GenericArity == 0
        && IsCoreVoid(method.ReturnType)
        && method.ParameterTypes is [var parameter]
        && parameter.Kind == TypeRefKind.Pointer
        && parameter.ElementType is { } marshalerArgument
        && IsTrustedRuntimeJavaScriptType(
            marshalerArgument,
            "JSMarshalerArgument");

    static bool IsGeneratedRuntimeWrapperStub(
        MethodIdentity wrapper,
        MethodIdentity stub) =>
        stub.IsStatic
        && stub.GenericArity == 0
        && stub.AssemblyName == wrapper.AssemblyName
        && stub.ModuleVersionId == wrapper.ModuleVersionId
        && stub.DeclaringType.Equals(wrapper.DeclaringType)
        && stub.Name.StartsWith(
            $"<{wrapper.Name}>g____Stub|",
            StringComparison.Ordinal);

    static bool IsCoreVoid(TypeRef type) =>
        IsCoreType(type, "Void");

    static bool HasAuthenticatedGeneratedContextImplementation(
        LibraryBodyIndex bodyIndex,
        ApiType context,
        ApiMember rootProperty,
        int rootGetterToken,
        int defaultGetterToken,
        IReadOnlySet<int> incompleteBodyTokens)
    {
        if (incompleteBodyTokens.Contains(rootGetterToken)
            || incompleteBodyTokens.Contains(defaultGetterToken)
            || context.DefinitionName is null)
        {
            return false;
        }

        MethodIdentity? rootGetter = bodyIndex.Methods.SingleOrDefault(
            method => method.MetadataToken == rootGetterToken);
        MethodIdentity? defaultGetter = bodyIndex.Methods.SingleOrDefault(
            method => method.MetadataToken == defaultGetterToken);
        MethodIdentity? staticConstructor = bodyIndex.Methods.SingleOrDefault(
            method => method.Name == ".cctor"
                && IsContextType(method.DeclaringType, context));
        if (rootGetter is null
            || defaultGetter is null
            || staticConstructor is null
            || incompleteBodyTokens.Contains(
                staticConstructor.MetadataToken)
            || rootGetter.Name != $"get_{rootProperty.Name}"
            || rootGetter.IsStatic
            || rootGetter.GenericArity != 0
            || !rootGetter.ParameterTypes.IsEmpty
            || !IsContextType(rootGetter.DeclaringType, context)
            || defaultGetter.Name != "get_Default"
            || !defaultGetter.IsStatic
            || defaultGetter.GenericArity != 0
            || !defaultGetter.ParameterTypes.IsEmpty
            || !IsContextType(defaultGetter.DeclaringType, context)
            || !IsContextType(defaultGetter.ReturnType, context)
            || !staticConstructor.IsStatic
            || staticConstructor.GenericArity != 0
            || !staticConstructor.ParameterTypes.IsEmpty
            || !IsCoreVoid(staticConstructor.ReturnType))
        {
            return false;
        }

        IReadOnlyDictionary<int, ImmutableArray<DirectCall>> callsByMethod =
            bodyIndex.GetDirectCallsByEvidenceMethod();
        if (!callsByMethod.TryGetValue(
                rootGetterToken,
                out ImmutableArray<DirectCall> rootCalls)
            || rootCalls.Length != 3)
        {
            return false;
        }

        DirectCall[] optionsGetters =
        [
            .. rootCalls.Where(
                call => IsJsonSerializerContextOptionsGetter(
                    call.Callee)),
        ];
        DirectCall[] runtimeTypes =
        [
            .. rootCalls.Where(
                call => IsSystemTypeGetTypeFromHandle(
                    call.Callee)),
        ];
        DirectCall[] getTypeInfos =
        [
            .. rootCalls.Where(
                call => IsJsonSerializerOptionsGetTypeInfo(
                    call.Callee)),
        ];
        if (optionsGetters is not [var optionsGetter]
            || runtimeTypes is not [var runtimeType]
            || getTypeInfos is not [var getTypeInfo]
            || optionsGetter.IsReachable != true
            || runtimeType.IsReachable != true
            || getTypeInfo.IsReachable != true
            || getTypeInfo.ReceiverSource is not
                {
                    IsComplete: true,
                    SourceCallOffsets: var receiverOffsets,
                }
            || receiverOffsets is not [var receiverOffset]
            || receiverOffset != optionsGetter.ILOffset
            || getTypeInfo.ArgumentSources.Count != 1
            || getTypeInfo.ArgumentSources[0] is not
                {
                    ArgumentIndex: 0,
                    IsComplete: true,
                    SourceCallOffsets: var argumentOffsets,
                }
            || argumentOffsets is not [var argumentOffset]
            || argumentOffset != runtimeType.ILOffset)
        {
            return false;
        }

        // The options the generated getter reads belong to this context
        // instance, and the runtime type handle names the very root this
        // property is registered for.
        if (optionsGetter.ResolvedReceiverValue?.Single is not
                {
                    Kind: ResolvedValueSourceKind.Argument,
                    ArgumentIndex: 0,
                }
            || rootGetter.ReturnType is not
                {
                    Kind: TypeRefKind.GenericInstance,
                    ElementType: { } jsonTypeInfoDefinition,
                    TypeArguments: [var registeredRoot],
                }
            || !IsTrustedSystemTextJsonType(
                jsonTypeInfoDefinition,
                "System.Text.Json.Serialization.Metadata",
                "JsonTypeInfo`1")
            || runtimeType.ResolvedArgumentValues.Count != 1
            || runtimeType.ResolvedArgumentValues[0].Single is not
                {
                    Kind: ResolvedValueSourceKind.TypeHandle,
                    Type: { } handleType,
                }
            || !handleType.Equals(registeredRoot))
        {
            return false;
        }

        if (!HasAuthenticatedRootCacheFlow(
                bodyIndex,
                context,
                rootGetterToken,
                getTypeInfo.ILOffset))
        {
            return false;
        }

        return HasAuthenticatedDefaultInstanceChain(
            bodyIndex,
            context,
            defaultGetterToken,
            staticConstructor,
            callsByMethod,
            incompleteBodyTokens);
    }

    /// <summary>
    /// Requires the <c>GetTypeInfo</c> result to reach the generated cache
    /// field on this instance, the getter's cached read to come from that same
    /// field, and every reachable return to hand back one of those two values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated getter returns a value merged from the cached-read path and
    /// the freshly-created path, so call-only result-sink completeness cannot
    /// prove it. Linking the store and the load instead is what rejects a body
    /// that keeps every trusted call but drops the result on the floor, and the
    /// Analysis-owned <see cref="MethodReturnFlow"/> is what rejects a body that
    /// stores the fresh value correctly and then returns something else.
    /// </para>
    /// <para>
    /// The store and load are linked by <see cref="FieldIdentity"/> rather than
    /// by metadata token, because a <c>MemberRef</c> alias would otherwise let a
    /// second write to the same field masquerade as a write to another one.
    /// <c>GeneratedJsExportAuthenticationTests.Build_RejectsGeneratedRootGetterThatDiscardsTypeInfo</c>,
    /// <c>GeneratedJsExportAuthenticationTests.Build_RejectsGeneratedRootGetterThatReturnsNullOnTheFreshPath</c>,
    /// and
    /// <c>GeneratedJsExportAuthenticationTests.PatchedRootGetter_ReportsNullAsAProvenReturnAlternative</c>
    /// gate it.
    /// </para>
    /// </remarks>
    static bool HasAuthenticatedRootCacheFlow(
        LibraryBodyIndex bodyIndex,
        ApiType context,
        int rootGetterToken,
        int typeInfoCallOffset)
    {
        FieldStoreFact[] stores =
        [
            .. bodyIndex.FieldStores.Where(store =>
                store.EvidenceMethod.MetadataToken == rootGetterToken),
        ];
        if (stores is not [var cacheStore]
            || cacheStore.IsStatic
            || cacheStore.IsReachable != true
            || cacheStore.ReceiverArgumentIndex != 0
            || cacheStore.Identity is not { } cacheField
            || !IsContextType(cacheField.DeclaringType, context)
            || cacheStore.Value.Single is not
                {
                    Kind: ResolvedValueSourceKind.CallResult,
                    ILOffset: var storedOffset,
                }
            || storedOffset != typeInfoCallOffset)
        {
            return false;
        }

        FieldLoadFact[] loads =
        [
            .. bodyIndex.FieldLoads.Where(load =>
                load.EvidenceMethod.MetadataToken == rootGetterToken),
        ];
        if (loads is not [var cacheLoad]
            || cacheLoad.IsStatic
            || cacheLoad.IsReachable != true
            || cacheLoad.Identity is not { } loadedField
            || !loadedField.Equals(cacheField)
            || cacheLoad.ReceiverArgumentIndex != 0)
        {
            return false;
        }

        return HasAuthenticatedRootReturnFlow(
            bodyIndex,
            rootGetterToken,
            cacheField,
            cacheLoad.ILOffset,
            typeInfoCallOffset);
    }

    /// <summary>
    /// Requires every reachable return of the generated root getter to hand back
    /// either the authenticated cache read or the freshly authenticated
    /// <c>GetTypeInfo</c> result, and nothing else.
    /// </summary>
    /// <remarks>
    /// Both alternatives must be present: a getter that only ever returns the
    /// cache field never proves the fresh path, and a getter that only ever
    /// returns the fresh result is not the caching shape this authentication
    /// describes. An unresolved fact, a null, or any third source fails closed.
    /// </remarks>
    static bool HasAuthenticatedRootReturnFlow(
        LibraryBodyIndex bodyIndex,
        int rootGetterToken,
        FieldIdentity cacheField,
        int cacheLoadOffset,
        int typeInfoCallOffset)
    {
        MethodReturnFlow[] flows =
        [
            .. bodyIndex.ReturnFlows.Where(flow =>
                flow.EvidenceMethod.MetadataToken == rootGetterToken),
        ];
        if (flows is not [{ Value.IsResolved: true } returnFlow]
            || returnFlow.ReturnOffsets.IsDefaultOrEmpty)
        {
            return false;
        }

        bool cached = false;
        bool fresh = false;
        foreach (ResolvedValueSource source in returnFlow.Value.Sources)
        {
            switch (source)
            {
                case
                {
                    Kind: ResolvedValueSourceKind.InstanceFieldLoad,
                    ArgumentIndex: 0,
                    FieldIdentity: { } returnedField,
                }
                    when source.ILOffset == cacheLoadOffset
                        && cacheField.Equals(returnedField):
                    cached = true;
                    break;
                case { Kind: ResolvedValueSourceKind.CallResult }
                    when source.ILOffset == typeInfoCallOffset:
                    fresh = true;
                    break;
                default:
                    return false;
            }
        }

        return cached && fresh;
    }

    /// <summary>
    /// Requires the linked default-instance chain the source generator emits:
    /// a default <c>JsonSerializerOptions</c> stored in a static field, a copy
    /// constructed from that field, the context constructed from that copy, the
    /// context stored in a static field, and <c>get_Default</c> returning that
    /// field.
    /// </summary>
    /// <remarks>
    /// Every link is followed by value provenance rather than counted, so a real
    /// generated context that also initializes unrelated user statics still
    /// authenticates, while a second write to either linked field fails closed.
    /// <c>GeneratedJsExportAuthenticationTests.Build_RejectsGeneratedContextWithUnlinkedDefaultInstance</c>
    /// and
    /// <c>GeneratedJsExportAuthenticationTests.Build_AcceptsGeneratedContextWithUnrelatedStaticOptions</c>
    /// gate the pair, and
    /// <c>GeneratedJsExportAuthenticationTests.Build_RejectsGeneratedContextConstructorThatDropsOptions</c>
    /// gates the constructor's own forwarding.
    /// </remarks>
    static bool HasAuthenticatedDefaultInstanceChain(
        LibraryBodyIndex bodyIndex,
        ApiType context,
        int defaultGetterToken,
        MethodIdentity staticConstructor,
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>> callsByMethod,
        IReadOnlySet<int> incompleteBodyTokens)
    {
        MethodResultSink[] returns =
        [
            .. bodyIndex.ResultSinks.Where(sink =>
                sink.EvidenceMethod.MetadataToken == defaultGetterToken
                && sink.Kind == MethodResultSinkKind.MethodReturn),
        ];
        if (returns is not [var defaultReturn]
            || defaultReturn.ResolvedValue?.Single is not
                {
                    Kind: ResolvedValueSourceKind.StaticFieldLoad,
                    FieldIdentity: { } instanceField,
                }
            || !IsContextType(instanceField.DeclaringType, context)
            || !callsByMethod.TryGetValue(
                staticConstructor.MetadataToken,
                out ImmutableArray<DirectCall> initializerCalls))
        {
            return false;
        }

        if (!TryGetSingleStaticInitialization(
                bodyIndex,
                staticConstructor,
                instanceField,
                out ResolvedValueSource? instanceValue)
            || instanceValue is not
                {
                    Kind: ResolvedValueSourceKind.NewObjectResult,
                    ILOffset: var contextOffset,
                })
        {
            return false;
        }

        DirectCall? contextConstruction = initializerCalls.FirstOrDefault(
            call => call.ILOffset == contextOffset);
        if (contextConstruction is not
                { Kind: CallKind.NewObject, IsReachable: true }
            || contextConstruction.Callee.Name != ".ctor"
            || !IsContextType(
                contextConstruction.Callee.DeclaringType,
                context)
            || contextConstruction.Callee.ParameterTypes is not [var options]
            || !IsTrustedSystemTextJsonType(
                options,
                "System.Text.Json",
                "JsonSerializerOptions")
            || contextConstruction.ResolvedArgumentValues.Count != 1
            || contextConstruction.ResolvedArgumentValues[0].Single is not
                {
                    Kind: ResolvedValueSourceKind.NewObjectResult,
                    ILOffset: var copyOffset,
                }
            || !HasAuthenticatedContextConstructorForwarding(
                bodyIndex,
                context,
                contextConstruction,
                incompleteBodyTokens))
        {
            return false;
        }

        DirectCall? copyConstruction = initializerCalls.FirstOrDefault(
            call => call.ILOffset == copyOffset);
        if (copyConstruction is not
                { Kind: CallKind.NewObject, IsReachable: true }
            || !IsJsonSerializerOptionsConstructor(
                copyConstruction.Callee,
                copy: true)
            || copyConstruction.ResolvedArgumentValues.Count != 1
            || copyConstruction.ResolvedArgumentValues[0].Single is not
                {
                    Kind: ResolvedValueSourceKind.StaticFieldLoad,
                    FieldIdentity: { } optionsField,
                }
            || !IsContextType(optionsField.DeclaringType, context))
        {
            return false;
        }

        if (!TryGetSingleStaticInitialization(
                bodyIndex,
                staticConstructor,
                optionsField,
                out ResolvedValueSource? optionsValue)
            || optionsValue is not
                {
                    Kind: ResolvedValueSourceKind.NewObjectResult,
                    ILOffset: var optionsOffset,
                })
        {
            return false;
        }

        DirectCall? optionsConstruction = initializerCalls.FirstOrDefault(
            call => call.ILOffset == optionsOffset);
        return optionsConstruction is
                { Kind: CallKind.NewObject, IsReachable: true }
            && IsJsonSerializerOptionsConstructor(
                optionsConstruction.Callee,
                copy: false);
    }

    /// <summary>
    /// The provenance of the one reachable static write to
    /// <paramref name="field"/> anywhere in the assembly, which must live in
    /// the context's static constructor. A second write — proven or not — fails
    /// closed; writes to unrelated fields are ignored.
    /// </summary>
    /// <remarks>
    /// Candidates are selected by <see cref="FieldIdentity"/>, not by metadata
    /// token, so a second write through a <c>MemberRef</c> alias for the same
    /// field is still a second write. Selection uses
    /// <see cref="FieldIdentity.MightBeSameFieldAs"/> rather than equality:
    /// a static store whose own identity could not be resolved, or which named
    /// this field without canonicalizing to its local definition, is counted as
    /// a candidate rather than skipped, because "might be this field" has to
    /// fail closed the same way "is this field twice" does. Selecting by
    /// equality alone would silently drop exactly the writes that are least
    /// proven.
    /// <c>JsExportSurfaceBuilderTests.Build_RejectsAliasedSecondWriteToGeneratedDefaultInstanceField</c>
    /// gates the aliasing case and
    /// <c>JsExportSurfaceBuilderTests.Build_RejectsUnprovenSecondStaticWriteNamingTheSameField</c>
    /// gates the unproven case.
    /// </remarks>
    static bool TryGetSingleStaticInitialization(
        LibraryBodyIndex bodyIndex,
        MethodIdentity staticConstructor,
        FieldIdentity field,
        out ResolvedValueSource? value)
    {
        value = null;
        FieldStoreFact[] stores =
        [
            .. bodyIndex.FieldStores.Where(store =>
                store.IsStatic
                && field.MightBeSameFieldAs(store.Identity)),
        ];
        if (stores is not [{ Identity: { } storedField } store]
            || !field.Equals(storedField)
            || store.EvidenceMethod.MetadataToken
                != staticConstructor.MetadataToken
            || store.IsReachable != true)
        {
            return false;
        }

        value = store.Value.Single;
        return value is not null;
    }

    /// <summary>
    /// Requires the generated context constructor to forward its own arguments
    /// to <c>JsonSerializerContext..ctor(JsonSerializerOptions)</c>: the base
    /// call's receiver must be the original <c>this</c> and its options
    /// argument the original options parameter.
    /// </summary>
    /// <remarks>
    /// Without this, authenticating the caller's construction inputs proves only
    /// what was handed to the constructor, not what the constructor did with
    /// them — a body that passes <c>null</c> to the base would still present a
    /// perfectly linked static-initializer chain. The argument provenance this
    /// relies on is the hardened original-argument form, so a body that
    /// reassigns or takes the address of either parameter first cannot pass.
    /// The base call must also dominate every normal constructor return, so a
    /// conditional early return cannot bypass initialization while leaving one
    /// authentic call reachable.
    /// <c>JsExportSurfaceBuilderTests.Build_RejectsContextBaseConstructorCallThatCanBeSkipped</c>
    /// gates that boundary.
    /// </remarks>
    static bool HasAuthenticatedContextConstructorForwarding(
        LibraryBodyIndex bodyIndex,
        ApiType context,
        DirectCall contextConstruction,
        IReadOnlySet<int> incompleteBodyTokens)
    {
        int constructorToken = contextConstruction.CalleeDefinitionToken;
        if (constructorToken == 0
            || incompleteBodyTokens.Contains(constructorToken))
        {
            return false;
        }

        MethodIdentity? constructor = bodyIndex.Methods.SingleOrDefault(
            method => method.MetadataToken == constructorToken);
        if (constructor is null
            || constructor.IsStatic
            || constructor.Name != ".ctor"
            || !IsContextType(constructor.DeclaringType, context))
        {
            return false;
        }

        if (!bodyIndex.GetDirectCallsByEvidenceMethod().TryGetValue(
                constructorToken,
                out ImmutableArray<DirectCall> constructorCalls))
        {
            return false;
        }

        DirectCall[] baseCalls =
        [
            .. constructorCalls.Where(call =>
                IsJsonSerializerContextConstructor(call.Callee)),
        ];
        return baseCalls is [var baseCall]
            && baseCall.Kind == CallKind.Call
            && baseCall.IsReachable == true
            && baseCall.DominatesEveryNormalReturn
            && baseCall.ResolvedReceiverValue?.Single is
                {
                    Kind: ResolvedValueSourceKind.Argument,
                    ArgumentIndex: 0,
                }
            && baseCall.ResolvedArgumentValues.Count == 1
            && baseCall.ResolvedArgumentValues[0].Single is
                {
                    Kind: ResolvedValueSourceKind.Argument,
                    ArgumentIndex: 1,
                };
    }

    static bool IsJsonSerializerContextConstructor(MemberRef method) =>
        method.Kind is MemberKind.Constructor or MemberKind.Method
        && method.HasThis
        && method.GenericArity == 0
        && method.Name == ".ctor"
        && IsTrustedSystemTextJsonType(
            method.DeclaringType,
            "System.Text.Json.Serialization",
            "JsonSerializerContext")
        && IsCoreVoid(method.ReturnType)
        && method.ParameterTypes is [var options]
        && IsTrustedSystemTextJsonType(
            options,
            "System.Text.Json",
            "JsonSerializerOptions");

    static bool IsJsonSerializerContextOptionsGetter(
        MemberRef method) =>
        method.Kind == MemberKind.Method
        && method.HasThis
        && method.GenericArity == 0
        && method.Name == "get_Options"
        && method.ParameterTypes.IsEmpty
        && IsTrustedSystemTextJsonType(
            method.DeclaringType,
            "System.Text.Json.Serialization",
            "JsonSerializerContext")
        && IsTrustedSystemTextJsonType(
            method.ReturnType,
            "System.Text.Json",
            "JsonSerializerOptions");

    static bool IsSystemTypeGetTypeFromHandle(MemberRef method) =>
        method.Kind == MemberKind.Method
        && !method.HasThis
        && method.GenericArity == 0
        && method.Name == "GetTypeFromHandle"
        && IsCoreType(method.DeclaringType, "Type")
        && IsCoreType(method.ReturnType, "Type")
        && method.ParameterTypes is [var handle]
        && IsCoreType(handle, "RuntimeTypeHandle");

    static bool IsJsonSerializerOptionsGetTypeInfo(
        MemberRef method) =>
        method.Kind == MemberKind.Method
        && method.HasThis
        && method.GenericArity == 0
        && method.Name == "GetTypeInfo"
        && IsTrustedSystemTextJsonType(
            method.DeclaringType,
            "System.Text.Json",
            "JsonSerializerOptions")
        && IsTrustedSystemTextJsonType(
            method.ReturnType,
            "System.Text.Json.Serialization.Metadata",
            "JsonTypeInfo")
        && method.ParameterTypes is [var type]
        && IsCoreType(type, "Type");

    static bool IsJsonSerializerOptionsConstructor(
        MemberRef method,
        bool copy) =>
        method.Kind == MemberKind.Constructor
        && method.HasThis
        && method.GenericArity == 0
        && method.Name == ".ctor"
        && IsTrustedSystemTextJsonType(
            method.DeclaringType,
            "System.Text.Json",
            "JsonSerializerOptions")
        && IsCoreVoid(method.ReturnType)
        && (copy
            ? method.ParameterTypes is [var options]
                && IsTrustedSystemTextJsonType(
                    options,
                    "System.Text.Json",
                    "JsonSerializerOptions")
            : method.ParameterTypes.IsEmpty
                || method.ParameterTypes is [var defaults]
                && IsTrustedSystemTextJsonType(
                    defaults,
                    "System.Text.Json",
                    "JsonSerializerDefaults"));

    static bool IsTrustedSystemTextJsonType(
        TypeRef type,
        string expectedNamespace,
        string expectedName) =>
        type is
        {
            Kind: TypeRefKind.Definition,
            Assembly: SystemTextJsonAssemblyName,
            TrustedFrameworkAssembly: true,
        }
        && type.Namespace == expectedNamespace
        && type.Name == expectedName
        && HasExactDefinitionName(
            type,
            expectedNamespace,
            expectedName);

    static bool IsContextType(TypeRef type, ApiType context) =>
        type is
        {
            Kind: TypeRefKind.Definition,
            Resolution:
            {
                Origin: TypeReferenceOrigin.CurrentAssembly,
                Type: { } definitionName,
            },
        }
        && definitionName == context.DefinitionName;

    static bool HasExactDefinitionName(
        TypeRef type,
        string expectedNamespace,
        string expectedName) =>
        type.Resolution?.Type is
        {
            Namespace: var actualNamespace,
            Segments: var segments,
        }
        && actualNamespace == expectedNamespace
        && segments is [var actualName]
        && actualName == expectedName;

    /// <summary>
    /// Compares the root shape captured from the serialized
    /// <c>[JsonSerializable]</c> argument with the generated
    /// <c>JsonTypeInfo&lt;T&gt;</c> signature. The serialized type-name grammar
    /// retains rank but no bounds, while a C# signature can explicitly retain
    /// the default zero lower bounds. Only that omitted/default representation
    /// difference is normalized.
    /// </summary>
    /// <remarks>
    /// This normalization is intentionally local to generated-property
    /// authentication. <see cref="ApiTypeShape.Equals(ApiTypeShape?)"/> retains
    /// exact ECMA array shape identity for every general metadata consumer.
    /// <c>JsExportSurfaceBuilderTests.Build_RejectsReachedMultidimensionalSerializerRoot</c>
    /// and <c>Build_DoesNotNormalizeNonDefaultMultidimensionalArrayBounds</c>
    /// gate the generated-root boundary.
    /// </remarks>
    static bool AreEquivalentGeneratedRootShapes(
        ApiTypeShape registeredRoot,
        ApiTypeShape propertyRoot)
    {
        var pending = new Stack<(ApiTypeShape Left, ApiTypeShape Right)>();
        pending.Push((registeredRoot, propertyRoot));
        while (pending.Count > 0)
        {
            (ApiTypeShape left, ApiTypeShape right) = pending.Pop();
            if (left.Kind != right.Kind
                || left.Primitive != right.Primitive
                || left.Definition != right.Definition
                || left.ArrayRank != right.ArrayRank
                || left.TypeArguments.Length != right.TypeArguments.Length
                || (left.ElementType is null) != (right.ElementType is null)
                || (left.Kind == ApiTypeShapeKind.Array
                    && !HaveEquivalentGeneratedArrayBounds(left, right)))
            {
                return false;
            }

            if (left.ElementType is not null)
                pending.Push((left.ElementType, right.ElementType!));
            for (int index = 0; index < left.TypeArguments.Length; index++)
            {
                pending.Push((
                    left.TypeArguments[index],
                    right.TypeArguments[index]));
            }
        }

        return true;
    }

    static bool HaveEquivalentGeneratedArrayBounds(
        ApiTypeShape left,
        ApiTypeShape right)
    {
        if (left.ArraySizes.AsSpan().SequenceEqual(right.ArraySizes.AsSpan())
            && left.ArrayLowerBounds.AsSpan().SequenceEqual(
                right.ArrayLowerBounds.AsSpan()))
        {
            return true;
        }

        // Reflection-serialized type names omit bounds entirely, while a C#
        // signature for the same zero-based multidimensional array can retain
        // explicit zero lower bounds. Do not collapse a non-default bounded
        // array into that source-generator shape.
        return HasOnlyDefaultArrayBounds(left)
            && HasOnlyDefaultArrayBounds(right);
    }

    static bool HasOnlyDefaultArrayBounds(ApiTypeShape shape) =>
        shape.ArraySizes.All(static size => size == 0)
        && shape.ArrayLowerBounds.All(static bound => bound == 0);

    static bool IsTrustedJsonTypeInfoProperty(ApiSignature signature) =>
        signature.ReturnTypeShape is
        {
            Kind: ApiTypeShapeKind.GenericInstance,
            Definition: { } definition,
        }
        && IsTrustedSystemTextJsonType(
            definition,
            JsonTypeInfoMetadataName);

    static bool TryGetTrustedJsonTypeInfoArgument(
        ApiSignature signature,
        out ApiTypeShape? argument)
    {
        argument = null;
        if (signature.ReturnTypeShape is not
            {
                Kind: ApiTypeShapeKind.GenericInstance,
                Definition: { } definition,
                TypeArguments: [var typeArgument],
            }
            || !IsTrustedSystemTextJsonType(
                definition,
                JsonTypeInfoMetadataName))
        {
            return false;
        }

        argument = typeArgument;
        return true;
    }

    static string? GetDefaultTypeInfoPropertyName(
        ApiJsonSerializableRoot root) =>
        root.Type is { } type
            ? GetGeneratedTypeInfoPropertyName(type)
            : null;

    static string? GetGeneratedTypeInfoPropertyName(ApiTypeShape type)
    {
        return type.Kind switch
        {
            ApiTypeShapeKind.Primitive => type.Primitive?.ToString(),
            ApiTypeShapeKind.Named => GetTypeInfoPropertyLeaf(
                type.Definition),
            ApiTypeShapeKind.GenericInstance =>
                GetGeneratedGenericTypeInfoPropertyName(type),
            ApiTypeShapeKind.SzArray =>
                GetGeneratedTypeInfoPropertyName(type.ElementType!)
                    is { } element
                    ? element + "Array"
                    : null,
            ApiTypeShapeKind.Array =>
                GetGeneratedTypeInfoPropertyName(type.ElementType!)
                    is { } element
                    ? element + "Array" + type.ArrayRank + "D"
                    : null,
            _ => null,
        };
    }

    static string? GetGeneratedGenericTypeInfoPropertyName(
        ApiTypeShape type)
    {
        string? name = GetTypeInfoPropertyLeaf(type.Definition);
        if (name is null)
            return null;

        var builder = new System.Text.StringBuilder(name);
        foreach (ApiTypeShape argument in type.TypeArguments)
        {
            string? argumentName =
                GetGeneratedTypeInfoPropertyName(argument);
            if (argumentName is null)
                return null;
            builder.Append(argumentName);
        }
        return builder.ToString();
    }

    static string? GetTypeInfoPropertyLeaf(
        ApiTypeReferenceIdentity? definition)
    {
        if (definition?.DefinitionName?.Segments is not
            [.., var leaf])
        {
            return null;
        }

        int aritySeparator = leaf.LastIndexOf('`');
        return aritySeparator > 0
            && leaf[(aritySeparator + 1)..].All(char.IsAsciiDigit)
                ? leaf[..aritySeparator]
                : leaf;
    }

    static IEnumerable<ApiTypeReferenceIdentity> EnumerateNamedTypes(
        ApiTypeShape type)
    {
        var pending = new Stack<ApiTypeShape>();
        pending.Push(type);
        while (pending.Count > 0)
        {
            ApiTypeShape current = pending.Pop();
            if (current.Definition is { } definition)
                yield return definition;
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            for (int index = current.TypeArguments.Length - 1;
                index >= 0;
                index--)
            {
                pending.Push(current.TypeArguments[index]);
            }
        }
    }

    static JsonSourceGenerationMode GetEffectiveGenerationMode(
        JsonSourceGenerationMode contextMode,
        JsonSourceGenerationMode rootMode) =>
        rootMode == JsonSourceGenerationMode.Default
            ? contextMode
            : rootMode;

    enum RegisteredRootPropertyMatch
    {
        None,
        Supported,
        Unsupported,
    }

    sealed record RegisteredRootProperties(
        IReadOnlyDictionary<string, ApiJsonSerializableRoot> Roots,
        IReadOnlySet<string> AmbiguousPropertyNames,
        IReadOnlySet<string> DuplicatePropertyNames,
        IReadOnlyList<ApiJsonSerializableRoot> UnnamedUnsupportedRoots);

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
