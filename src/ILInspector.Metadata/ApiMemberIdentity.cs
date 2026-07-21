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

    public sealed record XmlDocMemberIdentity(
        string LookupKey,
        IReadOnlyList<string> NormalizedParameters,
        string? NormalizedReturnType = null);

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
        var parameters = ExtractCanonicalParameterList(signature);
        var canonical = $"{kindCode}:{declaringType}.{memberName}{parameters}";
        // Mirror the conversion-operator return-type disambiguation so member identity
        // is not dependent on whether SignatureModel was populated (the Try path above).
        if (IsConversionOperator(member.Name) && !string.IsNullOrWhiteSpace(member.ReturnType))
            canonical += $"~{NormalizeCanonicalCommas(member.ReturnType!)}";
        return canonical;
    }

    public static bool TryGetCanonicalSignature(ApiType type, ApiMember member, out string canonicalSignature)
    {
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
                ? NormalizeCanonicalParameters(propertySignature.ParameterTypesSummary)
                : ExtractCanonicalIndexerParameterList(member.Signature);
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
        var canonical = $"{kindCode}:{declaringType}.{memberName}{NormalizeCanonicalParameters(signature.ParameterTypesSummary)}";
        // Conversion operators overload on return type, so the parameter list alone
        // is an ambiguous identity (every System.Decimal.op_Explicit(Decimal) collides).
        // Append a product-owned return-type suffix. It intentionally uses the
        // same "~ReturnType" delimiter as XML doc identity so conversion anchors
        // and XML lookups do not grow divergent spellings for the same fact.
        if (IsConversionOperator(member.Name) && !string.IsNullOrWhiteSpace(signature.ReturnType))
            canonical += $"~{NormalizeCanonicalCommas(signature.ReturnType!)}";
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
                string.IsNullOrWhiteSpace(parameter.TypeWithModifier)))
        {
            identityKey = "";
            variant = "";
            return false;
        }

        string receiver = isExtension
            ? signature.Parameters[0].TypeWithModifier
            : type.FullName;
        if (string.IsNullOrWhiteSpace(receiver)
            || string.IsNullOrWhiteSpace(member.Name)
            || string.IsNullOrWhiteSpace(signature.ReturnType))
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
            NormalizeCorrespondenceType(signature.ReturnType),
        };
        facets.AddRange(parameters.Select(parameter =>
            NormalizeCorrespondenceType(parameter.TypeWithModifier)));

        identityKey = string.Concat(facets.Select(facet => $"{facet.Length}:{facet}"));
        variant = isExtension ? "extension" : "instance";
        return true;
    }

    static string NormalizeCorrespondenceType(string type)
        => type.Trim()
            .Replace("+", ".", StringComparison.Ordinal)
            .Replace(", ", ",", StringComparison.Ordinal);

    // Preserve the v1 Member Index digest contract for members that already have
    // compatibility signature text. The legacy parser had edge-case behavior
    // around method names inside generic parameter names, and published stable
    // selectors hash that exact canonical string.
    static string? LegacyCanonicalMemberName(string? signature, string memberName)
    {
        if (string.IsNullOrEmpty(signature))
            return null;

        var parenStart = signature.IndexOf('(');
        if (parenStart < 0)
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
            : NormalizeCanonicalCommas(parameterTypesSummary);

    static string NormalizeCanonicalCommas(string value)
        => value.Replace(", ", ",", StringComparison.Ordinal).Trim();

    static string ExtractMemberNameWithGeneric(string signature, string memberName)
    {
        var parenStart = signature.IndexOf('(');
        if (parenStart < 0)
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
        => ExtractSignatureParameterType(param);

    public static bool TryGetXmlDocMemberIdentity(ApiType type, ApiMember member, out XmlDocMemberIdentity identity)
    {
        var typeXmlName = ToXmlDocName(type.FullName);
        var memberName = member.Name == ".ctor" ? "#ctor" : ToXmlDocMemberName(member.Name);
        var prefix = member.Kind switch
        {
            "property" => "P",
            "field" => "F",
            "event" => "E",
            _ => "M"
        };

        var lookupKey = $"{prefix}:{typeXmlName}.{memberName}";
        if (member.SignatureModel is not { } signature)
        {
            identity = new XmlDocMemberIdentity("", []);
            return false;
        }

        var typeParameterMap = type.TypeParameters
            .Select((p, i) => (p.Name, Index: i))
            .ToDictionary(p => p.Name, p => p.Index, StringComparer.Ordinal);
        var methodParameterMap = GetMethodGenericParameterMap(signature.MemberName);
        var parameters = signature.Parameters
            .Select(parameter => NormalizeXmlDocParameterType(parameter.TypeWithModifier, typeParameterMap, methodParameterMap))
            .ToList();
        var returnType = IsConversionOperator(member.Name) && !string.IsNullOrWhiteSpace(signature.ReturnType)
            ? NormalizeXmlDocParameterType(signature.ReturnType!, typeParameterMap, methodParameterMap)
            : null;
        identity = new XmlDocMemberIdentity(lookupKey, parameters, returnType);
        return true;
    }

    public static string NormalizeXmlDocParameterType(string parameter)
        => NormalizeXmlDocParameterType(parameter, EmptyParameterMap, EmptyParameterMap);

    public static string NormalizeXmlDocSignatureParameter(
        string parameter,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap)
        => NormalizeXmlDocParameterType(ExtractSignatureParameterType(parameter), typeParameterMap, methodParameterMap);

    static readonly IReadOnlyDictionary<string, int> EmptyParameterMap =
        new Dictionary<string, int>(StringComparer.Ordinal);

    static readonly HashSet<string> KnownNullableValueTypes = new(StringComparer.Ordinal)
    {
        "System.DateTime",
        "System.DateTimeOffset",
        "System.Decimal",
        "System.Guid",
        "System.TimeSpan",
        "System.IntPtr",
        "System.UIntPtr"
    };

    public static string NormalizeXmlDocParameterType(
        string parameter,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap)
    {
        var type = StripLeadingAttributes(parameter.Trim());
        var isByRef = false;
        foreach (var prefix in (string[])["ref ", "out ", "in ", "params ", "this "])
        {
            if (type.StartsWith(prefix, StringComparison.Ordinal))
            {
                isByRef = prefix is "ref " or "out " or "in ";
                type = type[prefix.Length..].TrimStart();
                break;
            }
        }

        if (type.EndsWith('@'))
        {
            isByRef = true;
            type = type.TrimEnd('@');
        }
        var nullableValueType = false;
        if (type.EndsWith("?", StringComparison.Ordinal))
        {
            var unwrapped = type[..^1];
            if (PrimitiveTypeNames.TryToClrFullName(unwrapped, out var primitive)
                && primitive is not ("System.String" or "System.Object" or "System.Void"))
            {
                type = $"System.Nullable<{primitive}>";
                nullableValueType = true;
            }
            else if (KnownNullableValueTypes.Contains(unwrapped))
            {
                type = $"System.Nullable<{unwrapped}>";
                nullableValueType = true;
            }
            else
            {
                type = unwrapped;
            }
        }

        string normalized;
        if (!nullableValueType && TryNormalizeGenericParameterReference(type, typeParameterMap, methodParameterMap, out var genericParameter))
        {
            normalized = genericParameter;
        }
        else if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            normalized = $"{NormalizeXmlDocParameterType(type[..^2], typeParameterMap, methodParameterMap)}[]";
        }
        else if (type.EndsWith("*", StringComparison.Ordinal))
        {
            normalized = $"{NormalizeXmlDocParameterType(type[..^1], typeParameterMap, methodParameterMap)}*";
        }
        else if (TryGetArraySuffix(type, out var arrayElementType, out var arraySuffix))
        {
            normalized = $"{NormalizeXmlDocParameterType(arrayElementType, typeParameterMap, methodParameterMap)}{arraySuffix}";
        }
        else
        {
            var genericStart = IndexOfAny(type, '<', '{');
            if (genericStart >= 0 && TryGetGenericParts(type, genericStart, out var genericType, out var genericArgs))
            {
                var normalizedType = PrimitiveTypeNames.ToClrFullName(genericType);
                var normalizedArgs = SplitParameters(genericArgs)
                    .Select(p => NormalizeXmlDocParameterType(p, typeParameterMap, methodParameterMap));
                normalized = $"{normalizedType}{{{string.Join(",", normalizedArgs)}}}";
            }
            else
            {
                normalized = PrimitiveTypeNames.ToClrFullName(type);
            }
        }

        return isByRef ? $"{normalized}@" : normalized;
    }

    static string ExtractSignatureParameterType(string parameter)
    {
        parameter = StripLeadingAttributes(parameter.TrimStart());
        var eqIndex = parameter.IndexOf('=');
        if (eqIndex >= 0)
            parameter = parameter[..eqIndex].Trim();

        var depth = 0;
        var lastSpace = -1;
        for (var i = 0; i < parameter.Length; i++)
        {
            var c = parameter[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ' ' && depth == 0)
                lastSpace = i;
        }

        return lastSpace > 0 ? parameter[..lastSpace] : parameter;
    }

    static string StripLeadingAttributes(string parameter)
    {
        while (parameter.StartsWith('['))
        {
            var depth = 0;
            var end = -1;
            for (var i = 0; i < parameter.Length; i++)
            {
                if (parameter[i] == '[') depth++;
                else if (parameter[i] == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (end < 0)
                return parameter;
            parameter = parameter[(end + 1)..].TrimStart();
        }

        return parameter;
    }

    static Dictionary<string, int> GetMethodGenericParameterMap(string? memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        var memberSegmentStart = memberName.LastIndexOf('.');
        var memberSegment = memberSegmentStart >= 0 ? memberName[(memberSegmentStart + 1)..] : memberName;
        var genericStart = memberSegment.IndexOf('<');
        if (genericStart < 0)
            return new Dictionary<string, int>(StringComparer.Ordinal);

        if (!TryGetGenericParts(memberSegment, genericStart, out _, out var parameters))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        return SplitParameters(parameters)
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .Where(p => p.Name.Length > 0)
            .ToDictionary(p => p.Name, p => p.Index, StringComparer.Ordinal);
    }

    static IEnumerable<string> SplitParameters(string parameters)
    {
        var depth = 0;
        var lastSplit = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            var c = parameters[i];
            if (c is '<' or '{' or '(')
                depth++;
            else if (c is '>' or '}' or ')')
                depth--;
            else if (c == ',' && depth == 0)
            {
                yield return parameters[lastSplit..i].Trim();
                lastSplit = i + 1;
            }
        }

        yield return parameters[lastSplit..].Trim();
    }

    static int IndexOfAny(string value, char first, char second)
    {
        var firstIndex = value.IndexOf(first);
        var secondIndex = value.IndexOf(second);
        return (firstIndex, secondIndex) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => secondIndex,
            (_, < 0) => firstIndex,
            _ => Math.Min(firstIndex, secondIndex)
        };
    }

    static bool TryGetGenericParts(string type, int genericStart, out string genericType, out string genericArgs)
    {
        genericType = type[..genericStart];
        genericArgs = "";

        var open = type[genericStart];
        var close = open == '<' ? '>' : '}';
        var depth = 0;
        for (var i = genericStart; i < type.Length; i++)
        {
            var c = type[i];
            if (c == open)
                depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    genericArgs = type[(genericStart + 1)..i];
                    return true;
                }
            }
        }

        return false;
    }

    static bool TryNormalizeGenericParameterReference(
        string type,
        IReadOnlyDictionary<string, int> typeParameterMap,
        IReadOnlyDictionary<string, int> methodParameterMap,
        out string normalized)
    {
        normalized = "";
        if (type.StartsWith("``", StringComparison.Ordinal) && int.TryParse(type[2..], out var methodIndex))
        {
            normalized = $"M{methodIndex}";
            return true;
        }

        if (type.StartsWith('`') && int.TryParse(type[1..], out var typeIndex))
        {
            normalized = $"T{typeIndex}";
            return true;
        }

        if (methodParameterMap.TryGetValue(type, out methodIndex))
        {
            normalized = $"M{methodIndex}";
            return true;
        }

        if (typeParameterMap.TryGetValue(type, out typeIndex))
        {
            normalized = $"T{typeIndex}";
            return true;
        }

        return false;
    }

    static string ToXmlDocName(string typeName)
        => typeName.Replace('+', '.');

    static string ToXmlDocMemberName(string memberName)
        => memberName is ".cctor"
            ? memberName
            : memberName
                .Replace('.', '#')
                .Replace('<', '{')
                .Replace('>', '}');

    public static bool IsConversionOperator(string memberName)
        => memberName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";

    static bool TryGetArraySuffix(string type, out string elementType, out string suffix)
    {
        elementType = "";
        suffix = "";
        if (!type.EndsWith("]", StringComparison.Ordinal))
            return false;

        var open = type.LastIndexOf('[');
        if (open <= 0)
            return false;

        var rankSpec = type[(open + 1)..^1];
        if (rankSpec.Length == 0)
            return false;

        elementType = type[..open];
        var rank = rankSpec.Count(c => c == ',') + 1;
        suffix = "[" + new string(',', rank - 1) + "]";
        return true;
    }
}
