using System.Collections.Immutable;
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
                if (bodyIndex is null
                        ? member.HasRuntimeJsExportWrapperCandidate
                            == false
                        : member.HasRuntimeJsExportWrapperCandidate
                                != true
                            || !HasAuthenticatedRuntimeJsExportWrapper(
                                bodyIndex,
                                surface.AssemblyIdentity,
                                type,
                                member,
                                incompleteBodyTokens))
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
        var registeredJsonTypeInfoGetterModes =
            new Dictionary<int, JsonSourceGenerationMode>();
        var registeredJsonTypeInfoDefaultGetterTokens =
            new Dictionary<int, int>();
        var unsupportedJsonTypeInfoGetterReasons =
            new Dictionary<int, string>();
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
            RegisteredRootProperties?
                registeredRootProperties =
                surface.AssemblyIdentity is null
                    ? null
                    : GetRegisteredRootProperties(type);
            int? defaultContextGetterToken =
                surface.AssemblyIdentity is { } currentAssembly
                    ? GetDefaultContextGetterToken(
                        type,
                        currentAssembly)
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
                                }
                            }
                            foreach (ApiTypeReferenceIdentity reference
                                in EnumerateNamedTypes(root!.Type!))
                            {
                                queue.Enqueue((null, reference, policy));
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
                        registeredJsonTypeInfoGetterModes,
                        registeredJsonTypeInfoDefaultGetterTokens,
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
                if (!JsonWireMemberRules.IsSerialized(member, direction)
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

    static int? GetDefaultContextGetterToken(
        ApiType context,
        ApiAssemblyIdentity assembly)
    {
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
                && signature.ReturnTypeReferences[0].Assembly.Equals(
                    assembly)
                && signature.ReturnTypeReferences[0].FullName
                    == context.FullName),
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

    static bool HasAuthenticatedRuntimeJsExportWrapper(
        LibraryBodyIndex bodyIndex,
        ApiAssemblyIdentity? assemblyIdentity,
        ApiType declaringType,
        ApiMember export,
        IReadOnlySet<int> incompleteBodyTokens)
    {
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
                || !IsAuthenticatedRuntimeRegistration(
                    callsByEvidenceMethod,
                    candidate,
                    runtimeBindingName,
                    incompleteBodyTokens)
                || !IsGeneratedRuntimeWrapper(wrapper, export.Name))
                continue;
            if (incompleteBodyTokens.Contains(wrapper.MetadataToken))
                continue;

            foreach (DirectCall wrapperCall in wrapperCalls)
            {
                if (wrapperCall.Kind != CallKind.Call
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

                if (stubCalls.Any(call =>
                        call.Kind == CallKind.Call
                        && call.CalleeDefinitionToken == exportToken
                        && call.Callee.DeclaringType.Equals(
                            wrapper.DeclaringType)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    static bool IsAuthenticatedRuntimeRegistration(
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
            callsByEvidenceMethod,
        RuntimeJsExportWrapperCandidate candidate,
        string runtimeBindingName,
        IReadOnlySet<int> incompleteBodyTokens)
    {
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

        return registrationCalls.All(call =>
                call.EvidenceMethod.ModuleVersionId
                    == moduleVersionId)
            && registrationCalls.Count(call =>
                call.Kind == CallKind.Call
                && IsRuntimeBindManagedFunction(call.Callee))
                == candidate.RegistrationCount
            && registrationCalls.Count(call =>
                call.Kind == CallKind.Call
                && IsRuntimeBindManagedFunction(call.Callee)
                && string.Equals(
                    call.FirstArgumentStringLiteral,
                    runtimeBindingName,
                    StringComparison.Ordinal))
                == 1;
    }

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
            ElementType:
            {
                Kind: TypeRefKind.Definition,
                Assembly: TypeRef.CoreLibrary,
                Namespace: "System",
                Name: "ReadOnlySpan`1",
            },
            TypeArguments:
            [
                var marshalerType,
            ],
        }
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
        type is
        {
            Kind: TypeRefKind.Definition,
            Assembly: TypeRef.CoreLibrary,
            Namespace: "System",
        }
        && type.Name == name;

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
        type is
        {
            Kind: TypeRefKind.Definition,
            Assembly: TypeRef.CoreLibrary,
            Namespace: "System",
            Name: "Void",
        };

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
