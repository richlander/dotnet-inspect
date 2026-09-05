using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

/// <summary>
/// Resolves each <c>[JSExport]</c> method's actual JSON wire-contract DTO type(s) by reading the
/// <c>JsonSerializer.Serialize</c>/<c>Deserialize</c> call sites in the method's own IL body (via
/// <see cref="LibraryBodyIndex.DirectCalls"/>), instead of inferring them from every DTO
/// registered anywhere in the assembly's <c>JsonSerializerContext</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two <c>[JSExport]</c> exports commonly share the identical erased signature (both are
/// <c>Task&lt;string&gt;</c>, say) while wiring to entirely different DTOs. Scanning "every
/// registered shape" cannot distinguish them; this resolver reads the one fact that does:
/// which type argument the export's own body actually instantiated
/// <c>JsonSerializer.Serialize&lt;T&gt;</c>/<c>Deserialize&lt;T&gt;</c> with (surfaced as
/// <c>TypeArguments[0]</c> on the call's <c>JsonTypeInfo&lt;T&gt;</c> parameter), and that actual
/// argument is proven to come directly from the matching registered source-generated context
/// property, and the property's receiver is proven to come from the same generated context's
/// <c>Default</c> getter. This relies on <c>DirectCall.Caller</c> already being attributed to the
/// declared method rather than a compiler-generated async state machine or lifted body (see
/// repository issue #4459 / PR #4461).
/// </para>
/// <para>
/// Only the DTO <em>type</em> is resolved this way. Which of the export's own parameters supplied
/// a <c>Deserialize</c> call's JSON-string argument is not resolved — that would need call-site
/// argument data-flow evidence beyond what <see cref="DirectCall"/> carries today. For a method
/// with a single <c>Deserialize</c> call this is unambiguous in practice; for multiple calls in
/// one body, every resolved DTO is reported without attribution to a specific parameter position.
/// This is a residual gap, not a silent guess.
/// </para>
/// <para>
/// A return DTO is resolved only when Analysis proves complete envelope coverage: every
/// synchronous physical <c>ret</c>, runtime-async export <c>ret</c>, or compiler-async
/// <c>AsyncTaskMethodBuilder&lt;T&gt;.SetResult</c> sink is fed exclusively by an exact,
/// authenticated <c>Serialize&lt;T&gt;</c> call for one structural DTO identity. Runtime-async
/// returns require Analysis's explicit <see cref="AsyncLoweringKind.Runtime"/> attribution on the
/// exact exported physical method plus its trusted <c>Task&lt;string&gt;</c> declaration; this
/// layer never infers lowering from matching method names or tokens. Discarded, raw,
/// non-serializer, and unresolved sources therefore leave the wire type unset. A body with more
/// than one distinct proven return DTO (e.g. different DTOs serialized on different branches)
/// remains ambiguous: <see cref="Attach"/> leaves
/// <see cref="JsExportFunction.ReturnWireType"/> unset rather than guessing. "Distinct" is judged
/// by assembly-scoped structural identity, preventing an external type from aliasing an unrelated
/// discovered local DTO that shares its qualified name.
/// When compiler lowering hoists a serialized local across a suspension, this resolver consumes
/// Analysis's typed <see cref="AsyncStateMachineFieldResultSource"/> proof. It never reconstructs
/// state-machine field flow itself.
/// </para>
/// <para>
/// A compiler-async sink is authentic only when Analysis's declared-body mapping proves that its
/// physical <c>MoveNext</c> body belongs to this export; a builder used by an ordinary method does
/// not qualify. Runtime-async evidence must remain on the export itself, so a serializer return
/// from a lifted local function or another method cannot be borrowed. Serializer evidence likewise
/// requires complete argument provenance to a registered context property's getter.
/// <c>JsonWireContractResolverTests.Build_ProducesEqualWireFactsAcrossAsyncLoweringsForDirectSerializerResult</c>,
/// <c>JsonWireContractResolverTests.Build_ProducesEqualWireFactsAcrossAsyncLoweringsForSerializerStoredAcrossSuspension</c>,
/// <c>JsonWireContractResolverTests.Build_RejectsConditionalSerializerStoreAcrossAsyncLowerings</c>,
/// <c>JsonWireContractResolverTests.RuntimeAsyncAuthenticationRejectsForgedAttributionAndMetadata</c>,
/// <c>JsonWireContractResolverTests.Build_RuntimeAsyncRejectsMixedSerializerAndRawReturns</c>,
/// <c>JsonWireContractResolverTests.Build_RuntimeAsyncRejectsIncompleteReturnCoverage</c>, and
/// <c>JsonWireContractResolverTests.Build_RuntimeAsyncRejectsAnotherMethodsSerializerEvidence</c>
/// gate the runtime-async equivalence and close negatives.
/// <c>JsonWireContractResolverTests.Build_RejectsUnrelatedAsyncBuilderResultSink</c> and
/// <c>JsonWireContractResolverTests.Build_RequiresRegisteredContextPropertyArgumentProvenance</c>,
/// plus
/// <c>JsExportSurfaceBuilderTests.Build_RejectsCustomSerializerContextInstanceReceiver</c>,
/// gate these boundaries.
/// </para>
/// </remarks>
public static class JsonWireContractResolver
{
    const string JsonSerializerTypeName = "JsonSerializer";
    const string JsonSerializerNamespace = "System.Text.Json";
    const string SystemTextJsonAssemblyName = "System.Text.Json";
    const string SerializeMethodName = "Serialize";
    const string DeserializeMethodName = "Deserialize";
    const string JsonTypeInfoName = "JsonTypeInfo`1";
    const string JsonTypeInfoNamespace = "System.Text.Json.Serialization.Metadata";

    /// <summary>
    /// Returns <paramref name="function"/> with
    /// <see cref="JsExportFunction.ReturnWireType"/>,
    /// <see cref="JsExportFunction.ReturnWireTypeShape"/>, and
    /// <see cref="JsExportFunction.ParameterWireTypes"/> populated from the
    /// direct calls found in <paramref name="bodyIndex"/> for the method
    /// identified by <paramref name="metadataToken"/>.
    /// </summary>
    public static JsExportFunction Attach(
        LibraryBodyIndex bodyIndex,
        JsExportFunction function,
        int metadataToken,
        IReadOnlyDictionary<int, JsonSourceGenerationMode>
            registeredJsonTypeInfoGetterModes,
        IReadOnlyDictionary<int, string>
            registeredJsonTypeInfoContextScopeKeys,
        IReadOnlyDictionary<int, int>
            registeredJsonTypeInfoDefaultGetterTokens,
        IReadOnlyDictionary<int, ApiTypeShape>
            registeredJsonTypeInfoShapes,
        IReadOnlyDictionary<int, string>
            unsupportedJsonTypeInfoGetterReasons)
    {
        var parameterTypes = new List<TypeRef>();
        var parameterContextScopeKeys = new HashSet<string>(
            StringComparer.Ordinal);
        var wireTypeContextPaths =
            new List<JsExportWireTypeContextPath>();

        foreach (DirectCall call in bodyIndex.DirectCalls)
        {
            if (call.Caller.MetadataToken != metadataToken
                || call.Callee.DeclaringType.Name != JsonSerializerTypeName
                || call.Callee.DeclaringType.Namespace != JsonSerializerNamespace)
            {
                continue;
            }
            if (!IsTrustedJsonSerializerType(
                    call.Callee.DeclaringType))
            {
                continue;
            }

            if (ResolveDeserializeDto(call.Callee) is { } dto
                && HasAuthenticatedJsonTypeInfoArgument(
                    bodyIndex,
                    call,
                    dto,
                    registeredJsonTypeInfoGetterModes,
                    registeredJsonTypeInfoContextScopeKeys,
                    registeredJsonTypeInfoDefaultGetterTokens,
                    registeredJsonTypeInfoShapes,
                    unsupportedJsonTypeInfoGetterReasons,
                    JsonWireDirection.Deserialize,
                    out _,
                    out ImmutableArray<string> contextScopeKeys))
            {
                parameterTypes.Add(dto);
                parameterContextScopeKeys.UnionWith(contextScopeKeys);
                wireTypeContextPaths.Add(
                    new JsExportWireTypeContextPath
                    {
                        Direction = JsonWireDirection.Deserialize,
                        TypeReferences =
                            [.. ReferencedTypes(dto).Distinct()],
                        ContextScopeKeys = contextScopeKeys,
                    });
            }
        }

        AuthenticatedWireType? returnType = ResolveCompleteReturnWireType(
            bodyIndex,
            metadataToken,
            registeredJsonTypeInfoGetterModes,
            registeredJsonTypeInfoContextScopeKeys,
            registeredJsonTypeInfoDefaultGetterTokens,
            registeredJsonTypeInfoShapes,
            unsupportedJsonTypeInfoGetterReasons);
        return new JsExportFunction
        {
            DeclaringType = function.DeclaringType,
            Name = function.Name,
            RuntimeDispatchKey = function.RuntimeDispatchKey,
            ReturnType = function.ReturnType,
            ReturnTypeReferences =
                function.ReturnTypeReferences,
            Parameters = function.Parameters,
            DelegateParameters = function.DelegateParameters,
            ReturnWireType = returnType is not null
                ? returnType.Value.Type.ToQualifiedDisplayString()
                : null,
            ReturnWireTypeReferences = returnType is not null
                ? [.. ReferencedTypes(returnType.Value.Type).Distinct()]
                : [],
            ReturnWireContextScopeKeys = returnType is not null
                ? returnType.Value.ContextScopeKeys
                : [],
            ReturnWireTypeShape = returnType?.Shape,
            ParameterWireTypes =
                [.. parameterTypes.Select(
                    type => type.ToQualifiedDisplayString())],
            ParameterWireTypeReferences =
                [.. parameterTypes
                    .SelectMany(ReferencedTypes)
                    .Distinct()],
            ParameterWireContextScopeKeys =
                [.. parameterContextScopeKeys],
            WireTypeContextPaths = returnType is not null
                ? [.. wireTypeContextPaths,
                    new JsExportWireTypeContextPath
                    {
                        Direction = JsonWireDirection.Serialize,
                        TypeReferences =
                            [.. ReferencedTypes(
                                returnType.Value.Type).Distinct()],
                        ContextScopeKeys =
                            [.. returnType.Value.ContextScopeKeys],
                    }]
                : [.. wireTypeContextPaths],
        };
    }

    static AuthenticatedWireType? ResolveCompleteReturnWireType(
        LibraryBodyIndex bodyIndex,
        int metadataToken,
        IReadOnlyDictionary<int, JsonSourceGenerationMode>
            registeredJsonTypeInfoGetterModes,
        IReadOnlyDictionary<int, string>
            registeredJsonTypeInfoContextScopeKeys,
        IReadOnlyDictionary<int, int>
            registeredJsonTypeInfoDefaultGetterTokens,
        IReadOnlyDictionary<int, ApiTypeShape>
            registeredJsonTypeInfoShapes,
        IReadOnlyDictionary<int, string>
            unsupportedJsonTypeInfoGetterReasons)
    {
        var sinks = new List<MethodResultSink>();
        foreach (MethodResultSink sink in bodyIndex.ResultSinks)
        {
            if (sink.Caller.MetadataToken != metadataToken)
                continue;

            if (sink.Kind == MethodResultSinkKind.MethodReturn)
            {
                if (IsAuthenticSynchronousResultSink(
                        bodyIndex,
                        sink,
                        metadataToken)
                    || IsAuthenticRuntimeAsyncResultSink(
                        bodyIndex,
                        sink,
                        metadataToken))
                {
                    sinks.Add(sink);
                }
                continue;
            }

            if (sink.Kind != MethodResultSinkKind.SingleArgumentCall)
                continue;

            DirectCall? consumer = CallAt(
                bodyIndex,
                sink.EvidenceMethod,
                sink.ILOffset);
            if (consumer is not null
                && IsTrustedAsyncResultSink(consumer.Callee)
                && IsAuthenticStateMachineResultSink(
                    bodyIndex,
                    sink,
                    metadataToken))
            {
                sinks.Add(sink);
            }
        }

        if (sinks.Count == 0)
            return null;

        TypeRef? dto = null;
        ApiTypeShape? dtoShape = null;
        var contextScopeKeys = new HashSet<string>(
            StringComparer.Ordinal);
        foreach (MethodResultSink sink in sinks)
        {
            if (!TryGetCompleteSourceCallOffsets(
                    sink,
                    out ImmutableArray<int> sourceCallOffsets))
            {
                return null;
            }

            foreach (int sourceOffset in sourceCallOffsets)
            {
                DirectCall? source = CallAt(
                    bodyIndex,
                    sink.EvidenceMethod,
                    sourceOffset);
                TypeRef? sourceDto = source is null
                    ? null
                    : ResolveSerializeDto(source.Callee);
                if (source is null
                    || sourceDto is null
                    || !HasAuthenticatedJsonTypeInfoArgument(
                        bodyIndex,
                        source,
                        sourceDto,
                        registeredJsonTypeInfoGetterModes,
                        registeredJsonTypeInfoContextScopeKeys,
                        registeredJsonTypeInfoDefaultGetterTokens,
                        registeredJsonTypeInfoShapes,
                        unsupportedJsonTypeInfoGetterReasons,
                        JsonWireDirection.Serialize,
                        out ApiTypeShape? sourceShape,
                        out ImmutableArray<string> sourceContextScopeKeys)
                    || sourceShape is null)
                    return null;
                contextScopeKeys.UnionWith(sourceContextScopeKeys);
                if (dto is null)
                {
                    dto = sourceDto;
                    dtoShape = sourceShape;
                }
                else if (!WireTypesEqual(dto, sourceDto)
                    || !dtoShape!.Equals(sourceShape))
                {
                    return null;
                }
            }
        }

        return dto is not null && dtoShape is not null
            ? new(dto, dtoShape, [.. contextScopeKeys])
            : null;
    }

    static bool TryGetCompleteSourceCallOffsets(
        MethodResultSink sink,
        out ImmutableArray<int> sourceCallOffsets)
    {
        if (sink.IsComplete
            && !sink.SourceCallOffsets.IsDefaultOrEmpty)
        {
            sourceCallOffsets = sink.SourceCallOffsets;
            return true;
        }
        if (sink.StateMachineFieldSource is
            {
                SourceCallOffsets.IsDefaultOrEmpty: false,
            } fieldSource)
        {
            sourceCallOffsets = fieldSource.SourceCallOffsets;
            return true;
        }

        sourceCallOffsets = [];
        return false;
    }

    static bool IsAuthenticSynchronousResultSink(
        LibraryBodyIndex bodyIndex,
        MethodResultSink sink,
        int exportMetadataToken)
        => sink.Caller.MetadataToken == exportMetadataToken
            && sink.Caller == sink.EvidenceMethod
            && sink.AsyncBody is null
            && bodyIndex.DeclaredMethods.Contains(
                sink.EvidenceMethod)
            && IsTrustedSystemString(
                sink.EvidenceMethod.ReturnType);

    internal static bool IsAuthenticRuntimeAsyncResultSink(
        LibraryBodyIndex bodyIndex,
        MethodResultSink sink,
        int exportMetadataToken)
        => sink.Caller.MetadataToken == exportMetadataToken
            && sink.Caller == sink.EvidenceMethod
            && sink.AsyncBody is
            {
                Lowering: AsyncLoweringKind.Runtime,
            } asyncBody
            && asyncBody.SourceMethod == sink.Caller
            && bodyIndex.DeclaredMethods.Contains(
                sink.EvidenceMethod)
            && IsTrustedTaskOfString(
                sink.EvidenceMethod.ReturnType);

    static bool IsAuthenticStateMachineResultSink(
        LibraryBodyIndex bodyIndex,
        MethodResultSink sink,
        int exportMetadataToken)
        => sink.Caller.MetadataToken == exportMetadataToken
            && sink.Caller != sink.EvidenceMethod
            && sink.EvidenceMethod.Name == "MoveNext"
            && sink.AsyncBody is
            {
                Lowering: AsyncLoweringKind.StateMachine,
            } asyncBody
            && asyncBody.SourceMethod == sink.Caller
            && bodyIndex.ResolveDeclaredMethod(
                sink.EvidenceMethod)
                == sink.Caller;

    static bool HasAuthenticatedJsonTypeInfoArgument(
        LibraryBodyIndex bodyIndex,
        DirectCall serializerCall,
        TypeRef dto,
        IReadOnlyDictionary<int, JsonSourceGenerationMode>
            registeredJsonTypeInfoGetterModes,
        IReadOnlyDictionary<int, string>
            registeredJsonTypeInfoContextScopeKeys,
        IReadOnlyDictionary<int, int>
            registeredJsonTypeInfoDefaultGetterTokens,
        IReadOnlyDictionary<int, ApiTypeShape>
            registeredJsonTypeInfoShapes,
        IReadOnlyDictionary<int, string>
            unsupportedJsonTypeInfoGetterReasons,
        JsonWireDirection direction,
        out ApiTypeShape? authenticatedShape,
        out ImmutableArray<string> authenticatedContextScopeKeys)
    {
        authenticatedShape = null;
        var authenticatedContexts = new HashSet<string>(
            StringComparer.Ordinal);
        CallArgumentSource? argument =
            serializerCall.ArgumentSources.FirstOrDefault(
                source => source.ArgumentIndex == 1);
        if (argument is not { IsComplete: true }
            || argument.SourceCallOffsets.IsDefaultOrEmpty)
        {
            authenticatedContextScopeKeys = [];
            return false;
        }

        foreach (int sourceOffset in argument.SourceCallOffsets)
        {
            DirectCall? source = CallAt(
                bodyIndex,
                serializerCall.EvidenceMethod,
                sourceOffset);
            if (source is null)
            {
                authenticatedContextScopeKeys = [];
                return false;
            }
            if (unsupportedJsonTypeInfoGetterReasons.TryGetValue(
                    source.CalleeDefinitionToken,
                    out string? unsupportedReason))
            {
                throw new UnsupportedJsExportSurfaceException(
                    "serializer context",
                    unsupportedReason);
            }
            if (!registeredJsonTypeInfoGetterModes.TryGetValue(
                    source.CalleeDefinitionToken,
                    out JsonSourceGenerationMode generationMode)
                || !SupportsDirection(generationMode, direction)
                || !registeredJsonTypeInfoShapes.TryGetValue(
                    source.CalleeDefinitionToken,
                    out ApiTypeShape? sourceShape)
                || !IsTrustedJsonTypeInfoOf(
                    source.Callee.ReturnType,
                    dto))
            {
                authenticatedContextScopeKeys = [];
                return false;
            }
            if (registeredJsonTypeInfoContextScopeKeys.TryGetValue(
                    source.CalleeDefinitionToken,
                    out string? contextScopeKey))
            {
                authenticatedContexts.Add(contextScopeKey);
            }
            if (registeredJsonTypeInfoDefaultGetterTokens.TryGetValue(
                    source.CalleeDefinitionToken,
                    out int defaultContextGetterToken)
                && !HasAuthenticatedDefaultContextReceiver(
                    bodyIndex,
                    source,
                    defaultContextGetterToken))
            {
                throw new UnsupportedJsExportSurfaceException(
                    "serializer context",
                    "generated JsonTypeInfo getter receiver is not the authenticated default context");
            }
            if (authenticatedShape is null)
            {
                authenticatedShape = sourceShape;
            }
            else if (!authenticatedShape.Equals(sourceShape))
            {
                authenticatedContextScopeKeys = [];
                return false;
            }
        }

        authenticatedContextScopeKeys = [.. authenticatedContexts];
        return authenticatedShape is not null;
    }

    static bool HasAuthenticatedDefaultContextReceiver(
        LibraryBodyIndex bodyIndex,
        DirectCall getterCall,
        int defaultContextGetterToken)
    {
        if (!getterCall.Callee.HasThis
            || getterCall.ReceiverSource is not
            {
                IsComplete: true,
                SourceCallOffsets.IsDefaultOrEmpty: false,
            } receiver)
        {
            return false;
        }

        foreach (int sourceOffset in receiver.SourceCallOffsets)
        {
            DirectCall? source = CallAt(
                bodyIndex,
                getterCall.EvidenceMethod,
                sourceOffset);
            if (source?.CalleeDefinitionToken
                != defaultContextGetterToken)
            {
                return false;
            }
        }
        return true;
    }

    static bool SupportsDirection(
        JsonSourceGenerationMode generationMode,
        JsonWireDirection direction) =>
        direction switch
        {
            JsonWireDirection.Serialize =>
                generationMode is JsonSourceGenerationMode.Default
                    or JsonSourceGenerationMode.Metadata
                    or JsonSourceGenerationMode.Serialization
                    or JsonSourceGenerationMode.MetadataAndSerialization,
            JsonWireDirection.Deserialize =>
                generationMode is JsonSourceGenerationMode.Default
                    or JsonSourceGenerationMode.Metadata
                    or JsonSourceGenerationMode.MetadataAndSerialization,
            _ => false,
        };

    static bool IsTrustedAsyncResultSink(MemberRef callee)
    {
        if (callee.Name != "SetResult")
            return false;
        if (!callee.HasThis
            || callee.GenericArity != 0
            || !callee.TypeArguments.IsEmpty
            || callee.ParameterTypes.Length != 1
            || !callee.ReturnType.Equals(
                TypeRef.CoreLib("System", "Void"))
            || callee.DeclaringType.Kind != TypeRefKind.GenericInstance
            || callee.DeclaringType.ElementType is not { } identity
            || callee.DeclaringType.TypeArguments.Length != 1
            || !WireTypesEqual(
                callee.DeclaringType.TypeArguments[0],
                callee.ParameterTypes[0]))
        {
            return false;
        }
        return identity.Name is "AsyncTaskMethodBuilder`1"
                or "AsyncValueTaskMethodBuilder`1"
            && IsTrustedFrameworkType(
                identity,
                "System.Runtime.CompilerServices",
                identity.Name,
                "System.Runtime");
    }

    static bool IsTrustedTaskOfString(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } identity
            && IsTrustedFrameworkType(
                identity,
                "System.Threading.Tasks",
                "Task`1",
                "System.Runtime")
            && type.TypeArguments.Length == 1
            && IsTrustedSystemString(
                type.TypeArguments[0]);

    static DirectCall? CallAt(
        LibraryBodyIndex bodyIndex,
        MethodIdentity evidenceMethod,
        int offset)
        => bodyIndex.DirectCalls.FirstOrDefault(call =>
            call.EvidenceMethod == evidenceMethod
            && call.ILOffset == offset);

    internal static bool WireTypesEqual(
        TypeRef left,
        TypeRef right) =>
        left.Equals(right)
        && ReferencedTypes(left).SequenceEqual(
            ReferencedTypes(right));

    internal static TypeRef? ResolveSerializeDto(MemberRef callee)
    {
        if (!IsTrustedJsonSerializerType(callee.DeclaringType)
            || !HasExactStaticGenericShape(
                callee,
                SerializeMethodName,
                parameterCount: 2)
            || !IsTrustedSystemString(callee.ReturnType)
            || !IsTrustedSystemString(callee.OpenSignatureReturn))
        {
            return null;
        }

        TypeRef dto = callee.TypeArguments[0];
        return WireTypesEqual(callee.ParameterTypes[0], dto)
            && IsTrustedJsonTypeInfoOf(callee.ParameterTypes[1], dto)
            && IsMethodGenericParameterZero(
                callee.OpenSignatureParameters[0])
            && IsJsonTypeInfoOfMethodGenericParameter(
                callee.OpenSignatureParameters[1])
            ? dto
            : null;
    }

    internal static TypeRef? ResolveDeserializeDto(MemberRef callee)
    {
        if (!IsTrustedJsonSerializerType(callee.DeclaringType)
            || !HasExactStaticGenericShape(
                callee,
                DeserializeMethodName,
                parameterCount: 2)
            || !IsTrustedSystemString(callee.ParameterTypes[0])
            || !IsTrustedSystemString(
                callee.OpenSignatureParameters[0]))
        {
            return null;
        }

        TypeRef dto = callee.TypeArguments[0];
        return WireTypesEqual(callee.ReturnType, dto)
            && IsMethodGenericParameterZero(callee.OpenSignatureReturn)
            && IsTrustedJsonTypeInfoOf(callee.ParameterTypes[1], dto)
            && IsJsonTypeInfoOfMethodGenericParameter(
                callee.OpenSignatureParameters[1])
            ? dto
            : null;
    }

    static bool HasExactStaticGenericShape(
        MemberRef callee,
        string name,
        int parameterCount)
        => callee.Kind == MemberKind.Method
            && callee.Name == name
            && !callee.HasThis
            && callee.GenericArity == 1
            && callee.TypeArguments.Length == 1
            && callee.ParameterTypes.Length == parameterCount
            && callee.OpenSignatureParameters.Length == parameterCount
            && (callee.SignatureHeader & 0x1F) == 0x10;

    static bool IsTrustedJsonTypeInfoOf(
        TypeRef type,
        TypeRef dto)
        => type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } elementType
            && IsTrustedSystemTextJsonType(
                elementType,
                JsonTypeInfoNamespace,
                JsonTypeInfoName)
            && type.TypeArguments.Length == 1
            && WireTypesEqual(type.TypeArguments[0], dto);

    static bool IsJsonTypeInfoOfMethodGenericParameter(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } elementType
            && IsTrustedSystemTextJsonType(
                elementType,
                JsonTypeInfoNamespace,
                JsonTypeInfoName)
            && type.TypeArguments.Length == 1
            && IsMethodGenericParameterZero(type.TypeArguments[0]);

    static bool IsMethodGenericParameterZero(TypeRef type)
        => type.Kind == TypeRefKind.MethodGenericParameter
            && type.GenericParameterIndex == 0;

    static IEnumerable<ApiTypeReferenceIdentity> ReferencedTypes(
        TypeRef type)
    {
        if (type.Kind == TypeRefKind.Definition)
        {
            ApiAssemblyIdentity? assembly =
                GetAssemblyIdentity(type);
            if (assembly is not null)
            {
                yield return new(
                    assembly,
                    type.Resolution?.Type?.ToMetadataFullName()
                        ?? type.ToQualifiedDisplayString(),
                    type.Resolution?.Type);
            }
        }

        if (type.ElementType is not null)
        {
            foreach (ApiTypeReferenceIdentity reference
                in ReferencedTypes(type.ElementType))
            {
                yield return reference;
            }
        }

        foreach (TypeRef argument in type.TypeArguments)
        {
            foreach (ApiTypeReferenceIdentity reference
                in ReferencedTypes(argument))
            {
                yield return reference;
            }
        }

    }

    static bool IsTrustedSystemTextJsonType(
        TypeRef type,
        string expectedNamespace,
        string expectedName)
    {
        ApiAssemblyIdentity? assembly = GetAssemblyIdentity(type);
        return type.Namespace == expectedNamespace
            && type.Name == expectedName
            && assembly?.Name == SystemTextJsonAssemblyName
            && PlatformKeys.IsPlatform(
                assembly.PublicKeyToken);
    }

    internal static bool IsTrustedJsonSerializerType(TypeRef type) =>
        IsTrustedSystemTextJsonType(
            type,
            JsonSerializerNamespace,
            JsonSerializerTypeName);

    static bool IsTrustedFrameworkType(
        TypeRef type,
        string expectedNamespace,
        string expectedName,
        string expectedAssembly)
    {
        TypeRef identity = type.Kind == TypeRefKind.GenericInstance
                && type.ElementType is { } element
            ? element
            : type;
        ApiAssemblyIdentity? assembly = GetAssemblyIdentity(identity);
        return identity.Namespace == expectedNamespace
            && identity.Name == expectedName
            && assembly?.Name == expectedAssembly
            && PlatformKeys.IsPlatform(assembly.PublicKeyToken);
    }

    static bool IsTrustedSystemString(TypeRef type)
    {
        ApiAssemblyIdentity? assembly = GetAssemblyIdentity(type);
        return type.Namespace == "System"
            && type.Name == "String"
            && (type.Resolution?.Origin
                is TypeReferenceOrigin.IntrinsicCoreLibrary
                || assembly is
                {
                    Name: "System.Private.CoreLib"
                        or "System.Runtime"
                        or "mscorlib"
                        or "netstandard",
                }
                && PlatformKeys.IsPlatform(
                    assembly.PublicKeyToken)
                && HasTopLevelDefinitionName(
                    type,
                    "System",
                    "String"));
    }

    static bool HasTopLevelDefinitionName(
        TypeRef type,
        string expectedNamespace,
        string expectedName) =>
        type.Resolution?.Type is
        {
            Namespace: var actualNamespace,
            Segments: var segments,
        }
        && actualNamespace == expectedNamespace
        && segments.Length == 1
        && segments[0] == expectedName;

    static ApiAssemblyIdentity? GetAssemblyIdentity(TypeRef type)
    {
        AssemblyReferenceIdentity? identity =
            type.Resolution?.Origin switch
            {
                TypeReferenceOrigin.AssemblyReference reference =>
                    reference.Assembly,
                TypeReferenceOrigin.CurrentAssembly current =>
                    current.Assembly,
                _ => null,
            };
        if (identity is not null)
        {
            return new(
                identity.Name,
                identity.Version,
                identity.Culture,
                identity.PublicKeyToken);
        }

        return type.Assembly.Length > 0
            ? new(type.Assembly, null, null, null)
            : null;
    }

    readonly record struct AuthenticatedWireType(
        TypeRef Type,
        ApiTypeShape Shape,
        IReadOnlyList<string> ContextScopeKeys);
}
