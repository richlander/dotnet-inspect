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
/// property. This relies on <c>DirectCall.Caller</c> already being attributed to the declared
/// method rather than a compiler-generated async state machine or lifted body (see repository
/// issue #4459 / PR #4461).
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
/// synchronous physical <c>ret</c>, or every authentic async
/// <c>AsyncTaskMethodBuilder&lt;T&gt;.SetResult</c> sink, is fed exclusively by an exact,
/// authenticated <c>Serialize&lt;T&gt;</c> call for one structural DTO identity. Discarded,
/// raw, non-serializer, and unresolved sources therefore leave the wire type unset. A body with
/// more than one distinct proven return DTO (e.g. different DTOs serialized on different branches)
/// remains ambiguous: <see cref="Attach"/> leaves
/// <see cref="JsExportFunction.ReturnWireType"/> unset rather than guessing. "Distinct" is judged
/// by assembly-scoped structural identity, preventing an external type from aliasing an unrelated
/// discovered local DTO that shares its qualified name.
/// </para>
/// <para>
/// An async sink is authentic only when Analysis's declared-body mapping proves that its physical
/// <c>MoveNext</c> body belongs to this export; a builder used by an ordinary method does not
/// qualify. Serializer evidence likewise requires complete argument provenance to a registered
/// context property's getter.
/// <c>JsonWireContractResolverTests.Build_RejectsUnrelatedAsyncBuilderResultSink</c> and
/// <c>JsonWireContractResolverTests.Build_RequiresRegisteredContextPropertyArgumentProvenance</c>
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
    /// Returns <paramref name="function"/> with <see cref="JsExportFunction.ReturnWireType"/> and
    /// <see cref="JsExportFunction.ParameterWireTypes"/> populated from the direct calls found in
    /// <paramref name="bodyIndex"/> for the method identified by <paramref name="metadataToken"/>.
    /// </summary>
    public static JsExportFunction Attach(
        LibraryBodyIndex bodyIndex,
        JsExportFunction function,
        int metadataToken,
        IReadOnlyDictionary<int, JsonSourceGenerationMode>
            registeredJsonTypeInfoGetterModes,
        IReadOnlyDictionary<int, string>
            unsupportedJsonTypeInfoGetterReasons)
    {
        var parameterTypes = new List<TypeRef>();

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
                    unsupportedJsonTypeInfoGetterReasons,
                    JsonWireDirection.Deserialize))
            {
                parameterTypes.Add(dto);
            }
        }

        TypeRef? returnType = ResolveCompleteReturnWireType(
            bodyIndex,
            metadataToken,
            registeredJsonTypeInfoGetterModes,
            unsupportedJsonTypeInfoGetterReasons);
        return new JsExportFunction
        {
            DeclaringType = function.DeclaringType,
            Name = function.Name,
            ReturnType = function.ReturnType,
            ReturnTypeReferences =
                function.ReturnTypeReferences,
            Parameters = function.Parameters,
            ReturnWireType = returnType is not null
                ? returnType.ToQualifiedDisplayString()
                : null,
            ReturnWireTypeReferences = returnType is not null
                ? [.. ReferencedTypes(returnType).Distinct()]
                : [],
            ParameterWireTypes =
                [.. parameterTypes.Select(
                    type => type.ToQualifiedDisplayString())],
            ParameterWireTypeReferences =
                [.. parameterTypes
                    .SelectMany(ReferencedTypes)
                    .Distinct()],
        };
    }

    static TypeRef? ResolveCompleteReturnWireType(
        LibraryBodyIndex bodyIndex,
        int metadataToken,
        IReadOnlyDictionary<int, JsonSourceGenerationMode>
            registeredJsonTypeInfoGetterModes,
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
                if (sink.EvidenceMethod.MetadataToken == metadataToken
                    && IsTrustedSystemString(
                        sink.EvidenceMethod.ReturnType))
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
                && IsAuthenticAsyncResultSink(
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
        foreach (MethodResultSink sink in sinks)
        {
            if (!sink.IsComplete
                || sink.SourceCallOffsets.IsDefaultOrEmpty)
            {
                return null;
            }

            foreach (int sourceOffset in sink.SourceCallOffsets)
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
                        unsupportedJsonTypeInfoGetterReasons,
                        JsonWireDirection.Serialize))
                    return null;
                if (dto is null)
                {
                    dto = sourceDto;
                }
                else if (!WireTypesEqual(dto, sourceDto))
                {
                    return null;
                }
            }
        }

        return dto;
    }

    static bool IsAuthenticAsyncResultSink(
        LibraryBodyIndex bodyIndex,
        MethodResultSink sink,
        int exportMetadataToken)
        => sink.Caller.MetadataToken == exportMetadataToken
            && sink.Caller != sink.EvidenceMethod
            && sink.EvidenceMethod.Name == "MoveNext"
            && sink.AsyncStateMachineSource?.MetadataToken
                == exportMetadataToken
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
            unsupportedJsonTypeInfoGetterReasons,
        JsonWireDirection direction)
    {
        CallArgumentSource? argument =
            serializerCall.ArgumentSources.FirstOrDefault(
                source => source.ArgumentIndex == 1);
        if (argument is not { IsComplete: true }
            || argument.SourceCallOffsets.IsDefaultOrEmpty)
        {
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
                || !IsTrustedJsonTypeInfoOf(
                    source.Callee.ReturnType,
                    dto))
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
                    type.ToQualifiedDisplayString(),
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
                || PlatformKeys.IsPlatform(
                    assembly?.PublicKeyToken));
    }

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
}
