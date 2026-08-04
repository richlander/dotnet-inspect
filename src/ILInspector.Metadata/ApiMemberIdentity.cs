using CSharpText;
using ILInspector.CSharp;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public sealed record MethodAnchorInfo(
    MemberAnchor Anchor,
    string ReturnType)
{
    MemberAnchor _anchor = Anchor ?? throw new ArgumentNullException(nameof(Anchor));
    string _returnType = ReturnType ?? throw new ArgumentNullException(nameof(ReturnType));

    public MemberAnchor Anchor
    {
        get => _anchor;
        init => _anchor = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ReturnType
    {
        get => _returnType;
        init => _returnType = value ?? throw new ArgumentNullException(nameof(value));
    }
}

public sealed record ExtensionMemberAnchorInfo(
    MemberAnchor Anchor,
    string ReturnType,
    string ExtendedType)
{
    MemberAnchor _anchor = Anchor ?? throw new ArgumentNullException(nameof(Anchor));
    string _returnType = ReturnType ?? throw new ArgumentNullException(nameof(ReturnType));
    string _extendedType = ExtendedType ?? throw new ArgumentNullException(nameof(ExtendedType));

    public MemberAnchor Anchor
    {
        get => _anchor;
        init => _anchor = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ReturnType
    {
        get => _returnType;
        init => _returnType = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ExtendedType
    {
        get => _extendedType;
        init => _extendedType = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// Metadata-owned API identity helpers for durable member selectors. These
/// helpers compose identity strings from queryable metadata facts, not from C#
/// declaration text.
/// </summary>
public static class ApiMemberIdentity
{
    sealed class AnchorSignatureTypeProvider : ISignatureTypeProvider<string, GenericContext?>
    {
        public static readonly AnchorSignatureTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.Void => "System.Void",
                PrimitiveTypeCode.TypedReference => "System.TypedReference",
                _ => typeCode.ToString(),
            };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeResolver.GetFullName(reader, reader.GetTypeDefinition(handle)).Replace('+', '.');

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => TypeResolver.GetTypeNameFromReference(reader, handle).Replace('+', '.');

        public string GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return "System.Object";
            using (scope)
            {
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            }
        }

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape)
            => $"{elementType}[{(shape.Rank <= 1 ? "*" : new string(',', shape.Rank - 1))}]";

        public string GetByReferenceType(string elementType) => $"{elementType}&";

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetPinnedType(string elementType) => $"pinned {elementType}";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => $"{genericType}<{string.Join(",", typeArguments)}>";

        public string GetGenericTypeParameter(GenericContext? context, int index)
            => context is not null && index >= 0 && index < context.TypeParameters.Count
                ? context.TypeParameters[index]
                : $"!{index}";

        public string GetGenericMethodParameter(GenericContext? context, int index)
            => context is not null && index >= 0 && index < context.MethodParameters.Count
                ? context.MethodParameters[index]
                : $"!!{index}";

        public string GetFunctionPointerType(MethodSignature<string> signature)
            => $"delegate*<{string.Join(",", signature.ParameterTypes.Append(signature.ReturnType))}>";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    }

    public static string GetMemberDigest(string canonicalSignature)
        => MemberAnchor.ComputeFingerprint(canonicalSignature);

    public static string GetMemberSelectorName(ApiMember member) => member.Kind switch
    {
        "operator" => $"operator:{member.Name}",
        "explicit-interface-implementation" => $"explicit:{member.Name}",
        "extension-method" => $"extension:{member.Name}",
        _ => member.Name
    };

    public static string GetMemberSelectorName(string metadataMethodName, bool isExtensionMethod = false)
        => metadataMethodName switch
        {
            ".ctor" => ".ctor",
            _ when isExtensionMethod => $"extension:{metadataMethodName}",
            _ when metadataMethodName.StartsWith("op_", StringComparison.Ordinal) => $"operator:{metadataMethodName}",
            _ when metadataMethodName.Contains('.', StringComparison.Ordinal) => $"explicit:{metadataMethodName}",
            _ => metadataMethodName,
        };

    public static ApiMemberHandle CreateHandle(ApiType type, ApiMember member)
        => new(type, member, GetMemberAnchor(type, member));

    /// <summary>
    /// Persists <see cref="ApiMember.CanonicalSignature"/> for every member whose canonical
    /// (identity) spelling diverges from its display <see cref="ApiMember.Signature"/> — i.e.
    /// members carrying C# tuple syntax, whose element names and <c>(...)</c> spelling must
    /// not leak into identity and cannot be recovered from the display text after a JSON
    /// round-trip (<see cref="ApiMember.SignatureModel"/> is not serialized). Computed here,
    /// while the structural model is live, so a round-tripped surface pairs with the same
    /// members read live. Non-divergent (non-tuple) members are left untouched, keeping
    /// their serialized form and digests unchanged.
    /// </summary>
    public static void PopulateCanonicalIdentities(ApiSurface surface)
    {
        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
            {
                if (member.SignatureModel is not { } signature || !HasCanonicalDivergence(member, signature))
                    continue;

                member.CanonicalSignature = GetCanonicalSignature(type, member);
            }
        }
    }

    static bool HasCanonicalDivergence(ApiMember member, ApiSignature signature)
    {
        // Persist canonical identity for any member whose display signature carries C#
        // tuple syntax the text fallback cannot re-canonicalize after a JSON round-trip
        // (SignatureModel is not serialized). Two divergence sources both require it:
        //   * A tuple PARAMETER is part of the identity digest; its erased spelling and
        //     element names must not leak in and cannot be recovered from display text.
        //   * A tuple RETURN type is only part of the digest for conversion operators, but
        //     even for other members its '(...)' parentheses would derail the fallback's
        //     first-'(' parameter-list detection, corrupting the round-tripped identity.
        //     The persisted digest is computed live and correctly omits the non-conversion
        //     return type, so short-circuiting to it keeps live and round-trip in lockstep.
        if (signature.Parameters.Any(parameter =>
                !string.Equals(parameter.EffectiveCanonicalType, parameter.Type, StringComparison.Ordinal)))
        {
            return true;
        }

        //   * A member or type-parameter name carrying a rendering hazard is
        //     respelled in the display signature by containment (issue #3319)
        //     but kept raw in identity. The text fallback locates the member
        //     name by searching the display signature for the raw spelling, so
        //     a respelling makes that search miss and silently drops the
        //     generic arity -- a round-tripped `M<T>(int)` would pair as
        //     `M(int)`. Persisting the live identity keeps the two in lockstep.
        if (CarriesRenderingHazard(member.Name)
            || signature.TypeParameters.Any(parameter => CarriesRenderingHazard(parameter.Name)))
        {
            return true;
        }

        return !string.Equals(signature.EffectiveCanonicalReturnType, signature.ReturnType, StringComparison.Ordinal);
    }

    static bool CarriesRenderingHazard(string? name)
        => name is not null && CSharpIdentifierCore.RequiresContainment(name);

    public static string GetMemberSignatureSortKey(ApiMember member)
    {
        var signature = member.Signature ?? "";
        if (signature.Length == 0 || member.Name.Length == 0)
            return signature;

        var searchStart = 0;
        while (searchStart < signature.Length)
        {
            var nameIndex = signature.IndexOf(member.Name, searchStart, StringComparison.Ordinal);
            if (nameIndex < 0)
                return signature;

            var genericStart = nameIndex + member.Name.Length;
            if (genericStart < signature.Length && signature[genericStart] == '<')
            {
                var depth = 0;
                for (var i = genericStart; i < signature.Length; i++)
                {
                    if (signature[i] == '<')
                        depth++;
                    else if (signature[i] == '>')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            if (i + 1 < signature.Length && signature[i + 1] == '(')
                                return signature.Remove(genericStart, i - genericStart + 1);
                            break;
                        }
                    }
                }
            }

            searchStart = nameIndex + member.Name.Length;
        }

        return signature;
    }

    public static MemberAnchor GetMemberAnchor(ApiType type, ApiMember member)
        => CreateAnchor(type, member, GetCanonicalSignature(type, member));

    public static MemberAnchor CreateMethodAnchor(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        bool isExtensionMethod = false)
        => CreateMethodAnchorInfo(reader, typeHandle, method, isExtensionMethod).Anchor;

    public static MethodAnchorInfo CreateMethodAnchorInfo(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        bool isExtensionMethod = false)
    {
        var shape = CreateMethodAnchorShape(reader, typeHandle, method, isExtensionMethod);
        return new MethodAnchorInfo(shape.Anchor, shape.ReturnType);
    }

    public static ExtensionMemberAnchorInfo CreateExtensionMethodAnchorInfo(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method)
    {
        var shape = CreateMethodAnchorShape(reader, typeHandle, method, isExtensionMethod: true);
        if (shape.ParameterTypes.Length == 0)
            throw new BadImageFormatException("An extension method must have a receiver parameter.");

        return new ExtensionMemberAnchorInfo(
            shape.Anchor,
            shape.ReturnType,
            shape.ParameterTypes[0]);
    }

    internal static ExtensionMemberAnchorInfo CreateExtensionPropertyDeclarationAnchorInfo(
        MetadataReader reader,
        TypeDefinitionHandle extensionClassHandle,
        TypeDefinition markerType,
        MethodDefinition markerMethod,
        PropertyDefinition property)
    {
        var context = GenericContext.ForType(reader, markerType);
        var markerSignature = GuardedProviderDecode.Method(reader, markerMethod, AnchorSignatureTypeProvider.Instance, context, "System.Object");
        if (markerSignature.ParameterTypes.Length != 1)
            throw new BadImageFormatException("An extension marker must have exactly one receiver parameter.");

        var propertySignature = GuardedProviderDecode.Property(reader, property, AnchorSignatureTypeProvider.Instance, context, "System.Object");
        string propertyName = reader.GetString(property.Name);
        string typeFullName = FormatDefinitionName(reader, extensionClassHandle);
        string extendedType = markerSignature.ParameterTypes[0];
        string canonicalSignature = MemberCanonicalSignature.BuildExtensionProperty(
            typeFullName,
            propertyName,
            markerSignature.ParameterTypes.AddRange(propertySignature.ParameterTypes));
        return new ExtensionMemberAnchorInfo(
            CreateAnchor(
                typeFullName,
                $"extension:{propertyName}",
                propertyName,
                canonicalSignature),
            propertySignature.ReturnType,
            extendedType);
    }

    static (MemberAnchor Anchor, string ReturnType, ImmutableArray<string> ParameterTypes) CreateMethodAnchorShape(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        bool isExtensionMethod)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        string methodName = reader.GetString(method.Name);
        var signature = GuardedProviderDecode.Method(
            reader,
            method,
            AnchorSignatureTypeProvider.Instance,
            GenericContext.ForMethod(reader, type, method),
            "System.Object");
        string typeFullName = FormatDefinitionName(reader, typeHandle);
        string memberName = MethodMemberName(reader, methodName, method);
        // Route the SRM-direct producer through the single full-name grammar core so it
        // cannot drift from other producers. Conversion operators overload on return type,
        // so pass the return type for their disambiguation suffix only.
        string canonicalSignature = MemberCanonicalSignature.Build(
            "M",
            typeFullName,
            memberName,
            signature.ParameterTypes,
            IsConversionOperator(methodName) ? signature.ReturnType : null);
        string selectorName = GetMemberSelectorName(methodName, isExtensionMethod);
        return (
            CreateAnchor(typeFullName, selectorName, memberName, canonicalSignature),
            signature.ReturnType,
            signature.ParameterTypes);
    }

    public static MemberAnchor CreateAnchor(ApiType type, ApiMember member, string canonicalSignature)
    {
        var fingerprint = MemberAnchor.ComputeFingerprint(
            canonicalSignature,
            member.SignatureDecodeStatus is SignatureDecodeStatus.Degraded);
        var stableSelector = $"{GetMemberSelectorName(member)}~{fingerprint}";
        return new MemberAnchor(
            stableSelector,
            canonicalSignature,
            fingerprint,
            MetadataTypeNameFormatter.FormatFullName(type),
            member.Name);
    }

    static MemberAnchor CreateAnchor(
        string typeFullName,
        string selectorName,
        string memberName,
        string canonicalSignature)
    {
        var fingerprint = MemberAnchor.ComputeFingerprint(canonicalSignature);
        return new MemberAnchor(
            $"{selectorName}~{fingerprint}",
            canonicalSignature,
            fingerprint,
            typeFullName,
            memberName);
    }

    static string FormatDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var genericNames = type.GetGenericParameters()
            .Select(parameter => reader.GetString(reader.GetGenericParameter(parameter).Name))
            .ToArray();
        string name = reader.GetString(type.Name);
        int tick = name.IndexOf('`');
        string simple = tick < 0 ? name : name[..tick];
        if (genericNames.Length > 0)
            simple += $"<{string.Join(",", genericNames)}>";
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return $"{FormatDefinitionName(reader, declaring)}.{simple}";
        string ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? simple : $"{ns}.{simple}";
    }

    static string MethodMemberName(MetadataReader reader, string methodName, MethodDefinition method)
    {
        if (methodName == ".ctor")
            return "#ctor";
        var genericNames = method.GetGenericParameters()
            .Select(parameter => reader.GetString(reader.GetGenericParameter(parameter).Name))
            .ToArray();
        return genericNames.Length == 0 ? methodName : $"{methodName}<{string.Join(",", genericNames)}>";
    }

    public static string GetCanonicalSignature(ApiType type, ApiMember member)
    {
        // A persisted canonical identity (present for tuple-bearing members whose display
        // signature cannot be re-canonicalized from text after a JSON round-trip) is
        // authoritative: it was computed at extraction from the live structural model and
        // guarantees round-tripped surfaces pair with the same members read live.
        if (!string.IsNullOrEmpty(member.CanonicalSignature))
            return member.CanonicalSignature!;

        if (TryGetCanonicalSignature(type, member, out var canonicalSignature))
            return canonicalSignature;

        var declaringType = string.IsNullOrWhiteSpace(member.DeclaringType)
            ? MetadataTypeNameFormatter.FormatFullName(type)
            : member.DeclaringType!;

        var kindCode = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        if (member.Kind is "field" or "event")
            return $"{kindCode}:{declaringType}.{member.Name}";

        // Note: TryGetCanonicalSignature above always succeeds for "property" (it has its
        // own SignatureModel-or-raw-signature fallback for indexer parameters), so this
        // method never actually reaches a "property" branch here. There is intentionally no
        // duplicate property-handling code below.

        var signature = member.Signature ?? member.ReturnType ?? member.Name;
        var memberName = member.Kind == "constructor"
            ? "#ctor"
            : ExtractMemberNameWithGeneric(signature, member.Name);
        // Raw-signature fallback (used when SignatureModel is absent, e.g. after a JSON
        // round-trip where SignatureModel is [JsonIgnore]). member.Signature is the
        // display string and carries `dynamic`, so scrub it back to `object` for identity
        // exactly as the SignatureModel path does — otherwise a round-tripped member's
        // fingerprint diverges from the same member read live.
        var parameters = XmlDocumentationNotation.NormalizeDynamicToObject(
            ExtractCanonicalParameterList(signature));
        var canonical = $"{kindCode}:{declaringType}.{memberName}{parameters}";
        // Mirror the conversion-operator return-type disambiguation so member identity
        // is not dependent on whether SignatureModel was populated (the Try path above).
        if (IsConversionOperator(member.Name) && !string.IsNullOrWhiteSpace(member.ReturnType))
            canonical += $"~{NormalizeCanonicalCommas(XmlDocumentationNotation.NormalizeDynamicToObject(member.ReturnType!))}";
        return canonical;
    }

    public static bool TryGetCanonicalSignature(ApiType type, ApiMember member, out string canonicalSignature)
    {
        // See GetCanonicalSignature: a persisted canonical identity is authoritative and
        // survives the JSON round-trip that discards SignatureModel.
        if (!string.IsNullOrEmpty(member.CanonicalSignature))
        {
            canonicalSignature = member.CanonicalSignature!;
            return true;
        }

        var declaringType = string.IsNullOrWhiteSpace(member.DeclaringType)
            ? MetadataTypeNameFormatter.FormatFullName(type)
            : member.DeclaringType!;

        var kindCode = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        if (member.Kind is "field" or "event")
        {
            canonicalSignature = $"{kindCode}:{declaringType}.{member.Name}";
            return true;
        }

        if (member.Kind == "property")
        {
            // An indexer is a property with parameters -- include them in identity so
            // overloaded indexers (e.g. this[int] vs this[string]) don't collide on
            // "P:Type.Item" and get paired by declaration order instead of by their actual
            // parameter signature. Ordinary (parameterless) properties are unaffected: their
            // canonical signature format is unchanged from before this check existed.
            //
            // ApiSurface.SignatureModel is [JsonIgnore], so a JSON-round-tripped surface
            // (a supported, tested scenario -- see FallbackCanonicalSignature_* tests) has
            // no SignatureModel. Falling back to "" here would make a JSON-persisted
            // baseline's indexer canonical signature diverge from the same indexer read
            // live from the assembly, breaking pairing between the two. So when
            // SignatureModel is absent, parse the parameter list out of the raw
            // "this[...]" signature text instead, which IS preserved across JSON
            // round-trips.
            var indexerParameters = member.SignatureModel is { Parameters.Count: > 0 } propertySignature
                ? NormalizeCanonicalParameters(propertySignature.CanonicalParameterTypesSummary)
                : XmlDocumentationNotation.NormalizeDynamicToObject(
                    ExtractCanonicalIndexerParameterList(member.Signature));
            canonicalSignature = $"{kindCode}:{declaringType}.{member.Name}{indexerParameters}";
            return true;
        }

        if (member.SignatureModel is not { } signature)
        {
            canonicalSignature = "";
            return false;
        }

        var memberName = member.Kind == "constructor"
            ? "#ctor"
            : LegacyCanonicalMemberName(member.Signature, member.Name)
              ?? (string.IsNullOrWhiteSpace(signature.MemberName)
                  ? member.Name
                  : signature.MemberName!);
        memberName = NormalizeCanonicalCommas(memberName);
        var canonical = $"{kindCode}:{declaringType}.{memberName}{NormalizeCanonicalParameters(signature.CanonicalParameterTypesSummary)}";
        // Conversion operators overload on return type, so the parameter list alone
        // is an ambiguous identity (every System.Decimal.op_Explicit(Decimal) collides).
        // Append a product-owned return-type suffix. It intentionally uses the
        // same "~ReturnType" delimiter as XML doc identity so conversion anchors
        // and XML lookups do not grow divergent spellings for the same fact.
        if (IsConversionOperator(member.Name) && !string.IsNullOrWhiteSpace(signature.EffectiveCanonicalReturnType))
            canonical += $"~{NormalizeCanonicalCommas(XmlDocumentationNotation.NormalizeDynamicToObject(signature.EffectiveCanonicalReturnType!))}";
        canonicalSignature = canonical;
        return true;
    }

    internal static bool TryGetExtensionInstanceProjection(
        ApiType type,
        ApiMember member,
        out string identityKey,
        out string variant)
    {
        bool isExtension = member.Kind == "method" && member.IsExtension && member.IsStatic;
        bool isInstance = member.Kind == "method" && !member.IsExtension && !member.IsStatic;
        if ((!isExtension && !isInstance)
            || member.SignatureModel is not { ReturnType: not null } signature)
        {
            identityKey = "";
            variant = "";
            return false;
        }

        if (isExtension && signature.Parameters.Count == 0)
        {
            identityKey = "";
            variant = "";
            return false;
        }
        if (signature.Parameters.Any(parameter =>
                string.IsNullOrWhiteSpace(parameter.CanonicalTypeWithModifier)))
        {
            identityKey = "";
            variant = "";
            return false;
        }

        string receiver = isExtension
            ? signature.Parameters[0].CanonicalTypeWithModifier
            : type.FullName;
        if (string.IsNullOrWhiteSpace(receiver)
            || string.IsNullOrWhiteSpace(member.Name)
            || string.IsNullOrWhiteSpace(signature.EffectiveCanonicalReturnType))
        {
            identityKey = "";
            variant = "";
            return false;
        }

        var parameters = isExtension
            ? signature.Parameters.Skip(1)
            : signature.Parameters;
        var facets = new List<string>
        {
            NormalizeCorrespondenceType(receiver),
            member.Name,
            signature.TypeParameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizeCorrespondenceType(signature.EffectiveCanonicalReturnType!),
        };
        facets.AddRange(parameters.Select(parameter =>
            NormalizeCorrespondenceType(parameter.CanonicalTypeWithModifier)));

        identityKey = string.Concat(facets.Select(facet => $"{facet.Length}:{facet}"));
        variant = isExtension ? "extension" : "instance";
        return true;
    }

    static string NormalizeCorrespondenceType(string type)
        => XmlDocumentationNotation.NormalizeDynamicToObject(type.Trim())
            .Replace("+", ".", StringComparison.Ordinal)
            .Replace(", ", ",", StringComparison.Ordinal);

    /// <summary>
    /// Locates the index of the parameter-list opening parenthesis in a member display
    /// signature, skipping a leading balanced parenthesized group that represents a C#
    /// tuple return type (e.g. <c>(int count, string name) Parse(...)</c>). A tuple return
    /// puts <c>(</c> at position 0, which is never the parameter list; returns -1 when no
    /// parameter-list parenthesis follows. Ordinary signatures (no leading tuple) resolve
    /// to the first <c>(</c> exactly as before, preserving existing digests.
    /// </summary>
    static int IndexOfParameterListParen(string signature)
    {
        var searchFrom = 0;
        if (signature.Length > 0 && signature[0] == '(')
        {
            var depth = 0;
            for (var i = 0; i < signature.Length; i++)
            {
                if (signature[i] == '(')
                {
                    depth++;
                }
                else if (signature[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        searchFrom = i + 1;
                        break;
                    }
                }
            }
        }

        return signature.IndexOf('(', searchFrom);
    }

    // Preserve the v1 Member Index digest contract for members that already have
    // compatibility signature text. The legacy parser had edge-case behavior
    // around method names inside generic parameter names, and published stable
    // selectors hash that exact canonical string.
    static string? LegacyCanonicalMemberName(string? signature, string memberName)
    {
        if (string.IsNullOrEmpty(signature))
            return null;

        var parenStart = IndexOfParameterListParen(signature);
        if (parenStart <= 0)
            return null;

        var nameIndex = signature.LastIndexOf(memberName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return null;

        var end = nameIndex + memberName.Length;
        if (end < parenStart && signature[end] == '<')
        {
            var depth = 0;
            for (var i = end; i < parenStart; i++)
            {
                if (signature[i] == '<')
                    depth++;
                else if (signature[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                        return signature[nameIndex..(i + 1)];
                }
            }
        }

        return memberName;
    }

    static string NormalizeCanonicalParameters(string parameterTypesSummary)
        => string.IsNullOrEmpty(parameterTypesSummary)
            ? "()"
            : NormalizeCanonicalCommas(
                XmlDocumentationNotation.NormalizeDynamicToObject(parameterTypesSummary));

    static string NormalizeCanonicalCommas(string value)
        => value.Replace(", ", ",", StringComparison.Ordinal).Trim();

    static string ExtractMemberNameWithGeneric(string signature, string memberName)
    {
        var parenStart = IndexOfParameterListParen(signature);
        if (parenStart <= 0)
            return memberName;

        var nameIndex = signature.LastIndexOf(memberName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return memberName;

        var end = nameIndex + memberName.Length;
        if (end < parenStart && signature[end] == '<')
        {
            var depth = 0;
            for (var i = end; i < parenStart; i++)
            {
                if (signature[i] == '<')
                    depth++;
                else if (signature[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                        return NormalizeCanonicalCommas(signature[nameIndex..(i + 1)]);
                }
            }
        }

        return memberName;
    }

    static string ExtractCanonicalParameterList(string signature)
    {
        var abbreviated = AbbreviateSignature(signature);
        var parenStart = abbreviated.IndexOf('(');
        var parenEnd = abbreviated.LastIndexOf(')');
        if (parenStart < 0 || parenEnd < parenStart)
            return "()";

        var parameters = abbreviated[parenStart..(parenEnd + 1)];
        return NormalizeCanonicalCommas(parameters);
    }

    /// <summary>
    /// Extracts the canonical, parenthesized parameter-type list from an indexer's raw
    /// signature text (e.g. "int this[string key] { get; }" -> "(string)"), or "" when the
    /// signature has no "this[...]" indexer parameter list (an ordinary, non-indexed
    /// property). Kept in sync with ApiSurfaceExtractor's raw indexer signature format.
    /// </summary>
    static string ExtractCanonicalIndexerParameterList(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        var indexerKeyword = signature.IndexOf("this[", StringComparison.Ordinal);
        if (indexerKeyword < 0)
            return "";

        var bracketStart = indexerKeyword + "this".Length;
        var depth = 0;
        var bracketEnd = -1;
        for (var i = bracketStart; i < signature.Length; i++)
        {
            if (signature[i] == '[')
                depth++;
            else if (signature[i] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    bracketEnd = i;
                    break;
                }
            }
        }

        if (bracketEnd < 0)
            return "";

        var parameterSection = signature[(bracketStart + 1)..bracketEnd];
        // Reuse the existing parenthesized-parameter-list machinery (type extraction,
        // default-value stripping, generic-depth-aware comma splitting) by round-tripping
        // the bracketed indexer parameters through the parenthesized form it expects.
        return ExtractCanonicalParameterList($"({parameterSection})");
    }

    static string AbbreviateSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return signature;

        int parenEnd = signature.LastIndexOf(')');
        if (parenEnd < parenStart + 1)
            return signature;

        string prefix = signature[..(parenStart + 1)];
        string suffix = signature[parenEnd..];
        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return signature;

        // The signature is a lossy display string. Parameter names (F# quoted
        // identifiers may contain spaces, '=', quotes, brackets, angle brackets,
        // parentheses, and commas), array-rank/type spellings, and default-value
        // literals can all contain characters that look structural, so no parser can
        // be fully robust here. Every deviation from main's splitter risks changing
        // the canonical signature for some compiler-emittable name (e.g. splitting
        // main's combined <(...)> depth into independent counters regresses an F#
        // name like ``x<)`` where '<' and ')' cross-cancel). This fallback therefore
        // reproduces main's splitter EXACTLY, adding only the one thing #2940 needs:
        // it skips a leading attribute list ("[Optional, DateTimeConstant(ticks)]
        // type name") so the comma inside it does not split the parameter list.
        //
        // Bracket nesting is tracked ONLY inside that leading attribute list, at the
        // very start of a parameter. Once the first real (non-space, non-'[') type
        // character is seen, tracking reverts to main's single combined depth counter
        // over '<'/'>'/'('/')' , so brackets in array types ("int[]") or in F#
        // quoted names ("x[") are ordinary text, and generic/tuple commas stay
        // protected — identical to main. String/char literal and default-value
        // tracking are deliberately omitted: any such heuristic is defeatable by a
        // quote or delimiter inside a name, which main treats as ordinary.
        List<string> paramTypes = [];
        int depth = 0;
        int attrBracketDepth = 0;
        int lastSplit = 0;
        bool inLeadingAttributes = true;
        for (int i = 0; i < paramSection.Length; i++)
        {
            char c = paramSection[i];

            if (inLeadingAttributes)
            {
                if (c == '[') { attrBracketDepth++; continue; }
                if (attrBracketDepth > 0)
                {
                    if (c == ']') attrBracketDepth--;
                    continue; // characters inside the attribute list (incl. commas) are skipped
                }
                if (c == ' ') continue; // still in the leading region, between/after attribute lists
                inLeadingAttributes = false; // first real type character; fall through to main's logic
            }

            if (c == '<' || c == '(') depth++;
            else if (c == '>' || c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                paramTypes.Add(ExtractParamType(paramSection[lastSplit..i].Trim()));
                lastSplit = i + 1;
                attrBracketDepth = 0;
                inLeadingAttributes = true;
            }
        }
        paramTypes.Add(ExtractParamType(paramSection[lastSplit..].Trim()));

        return prefix + string.Join(", ", paramTypes) + suffix;
    }

    static string ExtractParamType(string param)
        => XmlDocumentationNotation.ExtractSignatureParameterType(param);

    public static bool TryGetXmlDocMemberIdentity(ApiType type, ApiMember member, out XmlDocMemberIdentity identity)
    {
        var prefix = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        if (member.SignatureModel is not { } signature)
        {
            identity = new XmlDocMemberIdentity("", []);
            return false;
        }

        var conversionReturnType = IsConversionOperator(member.Name)
            && !string.IsNullOrWhiteSpace(signature.EffectiveCanonicalReturnType)
            ? signature.EffectiveCanonicalReturnType
            : null;
        identity = XmlDocumentationNotation.CreateMemberIdentity(
            prefix,
            type.FullName,
            member.Name,
            signature.Parameters.Select(parameter => parameter.CanonicalTypeWithModifier).ToList(),
            type.TypeParameters.Select(parameter => parameter.Name).ToList(),
            signature.MemberName,
            conversionReturnType);
        return true;
    }

    public static bool IsConversionOperator(string memberName)
        => memberName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";
}
