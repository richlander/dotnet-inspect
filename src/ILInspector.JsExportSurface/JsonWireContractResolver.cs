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
/// <c>TypeArguments[0]</c> on the call's <c>JsonTypeInfo&lt;T&gt;</c> parameter). This relies on
/// <c>DirectCall.Caller</c> already being attributed to the declared method rather than a
/// compiler-generated async state machine or lifted body (see repository issue #4459 / PR #4461).
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
/// A <c>Serialize&lt;T&gt;</c> call contributes a return DTO only when Analysis proves its result
/// reaches the physical method return or an authentic
/// <c>AsyncTaskMethodBuilder&lt;T&gt;.SetResult</c> sink. Discarded and unresolved result flows are
/// ignored. A body with more than one distinct proven return DTO (e.g. different DTOs serialized
/// on different branches) remains ambiguous: <see cref="Attach"/> leaves
/// <see cref="JsExportFunction.ReturnWireType"/> unset rather than guessing. "Distinct" is judged
/// by assembly-scoped structural identity, preventing an external type from aliasing an unrelated
/// discovered local DTO that shares its qualified name.
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
        int metadataToken)
    {
        // Every distinct Serialize<T> DTO found for the return position, in call-site order.
        // Kept as a list (not folded into a single "first wins" value) so ambiguity between
        // multiple distinct DTOs can be detected and left unresolved rather than guessed — see
        // remarks above.
        var returnTypes = new List<TypeRef>();
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

            TypeRef? dto = ResolveJsonTypeInfoArgument(call.Callee);
            if (dto is null)
            {
                continue;
            }

            if (call.Callee.Name == SerializeMethodName
                && IsTrustedSystemString(call.Callee.ReturnType)
                && ResultFlowsToExportReturn(call, bodyIndex))
            {
                if (!returnTypes.Any(existing =>
                    WireTypesEqual(existing, dto)))
                {
                    returnTypes.Add(dto);
                }
            }
            else if (call.Callee.Name == DeserializeMethodName)
            {
                parameterTypes.Add(dto);
            }
        }

        return new JsExportFunction
        {
            DeclaringType = function.DeclaringType,
            Name = function.Name,
            ReturnType = function.ReturnType,
            ReturnTypeReferences =
                function.ReturnTypeReferences,
            Parameters = function.Parameters,
            ReturnWireType = returnTypes.Count == 1
                ? returnTypes[0].ToQualifiedDisplayString()
                : null,
            ReturnWireTypeReferences = returnTypes.Count == 1
                ? [.. ReferencedTypes(returnTypes[0]).Distinct()]
                : [],
            ParameterWireTypes =
                [.. parameterTypes.Select(
                    type => type.ToQualifiedDisplayString())],
        };
    }

    static bool ResultFlowsToExportReturn(
        DirectCall serialize,
        LibraryBodyIndex bodyIndex)
    {
        if (serialize.ResultUse == DirectCallResultUse.MethodReturn)
            return true;
        if (serialize.ResultUse != DirectCallResultUse.CallArgument
            || serialize.ResultConsumerOffset is not { } consumerOffset)
        {
            return false;
        }

        DirectCall? consumer = bodyIndex.DirectCalls.FirstOrDefault(call =>
            call.EvidenceMethod == serialize.EvidenceMethod
            && call.ILOffset == consumerOffset);
        return consumer is not null
            && IsTrustedAsyncResultSink(consumer.Callee)
            && consumer.Callee.ParameterTypes.Length == 1
            && consumer.Callee.ReturnType.Equals(
                TypeRef.CoreLib("System", "Void"));
    }

    static bool IsTrustedAsyncResultSink(MemberRef callee)
    {
        if (callee.Name != "SetResult")
            return false;
        TypeRef identity =
            callee.DeclaringType.Kind == TypeRefKind.GenericInstance
                && callee.DeclaringType.ElementType is { } element
                ? element
                : callee.DeclaringType;
        return identity.Name is "AsyncTaskMethodBuilder`1"
                or "AsyncValueTaskMethodBuilder`1"
            && IsTrustedFrameworkType(
                identity,
                "System.Runtime.CompilerServices",
                identity.Name,
                "System.Runtime");
    }

    internal static bool WireTypesEqual(
        TypeRef left,
        TypeRef right) =>
        left.Equals(right)
        && ReferencedTypes(left).SequenceEqual(
            ReferencedTypes(right));

    static TypeRef? ResolveJsonTypeInfoArgument(MemberRef callee)
    {
        foreach (TypeRef parameter in callee.ParameterTypes)
        {
            if (parameter.Kind == TypeRefKind.GenericInstance
                && parameter.ElementType is { } elementType
                && elementType.Name == JsonTypeInfoName
                && elementType.Namespace == JsonTypeInfoNamespace
                && IsTrustedSystemTextJsonType(
                    elementType,
                    JsonTypeInfoNamespace,
                    JsonTypeInfoName)
                && parameter.TypeArguments.Length == 1)
            {
                // ToDisplayString (not .Name) so a container DTO — e.g. WidgetDto[] — renders as
                // C#-syntax text rather than the empty string TypeRef.Name carries for
                // non-Definition kinds (GenericInstance/SzArray/Array). TsTypeMapper's Map already
                // parses this exact array ("[]") syntax for every other signature-derived type
                // string in this pipeline, so an array-of-DTO return resolves to a correct TS
                // array type instead of silently collapsing to "unknown". This does not extend
                // support to arbitrary generic containers (List<T>, Dictionary<K,V>): Map has
                // never parsed C# generic-argument syntax for any type in this pipeline (see its
                // WidgetCatalog.OwnersByKey property, which already renders "unknown" for exactly
                // this reason, independent of this resolver). Recovering the correct display text
                // here does not change that pre-existing, system-wide boundary.
                return parameter.TypeArguments[0];
            }
        }

        return null;
    }

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
