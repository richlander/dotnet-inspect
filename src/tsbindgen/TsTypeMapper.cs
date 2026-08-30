using ILInspector.Analysis;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

enum TsTypeMappingContext
{
    JsInterop,
    JsonWire,
}

enum TsLocalTypeKind
{
    Reference,
    Value,
}

sealed record TsDelegateMappingContext(
    IReadOnlySet<string> RecordNames,
    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TsLocalTypeKind> LocalTypeKinds,
    ApiAssemblyIdentity? ContainingAssembly);

readonly record struct AuthenticatedMappingNames(
    string DisplayType,
    IReadOnlySet<string> RecordNames,
    IReadOnlySet<string>? BlockedAliases);

/// <summary>
/// Rewrites C# signature-text type names into TypeScript type text. All target-language opinion
/// lives here — <c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c> unwrap to <c>Promise&lt;T&gt;</c>,
/// C# built-ins map to TS primitives, and locally-declared record types are passed through by
/// name (their declarations are emitted separately by <see cref="DtsEmitter"/>).
/// </summary>
static class TsTypeMapper
{
    const int MaximumDelegateParameterCount = 3;

    public static bool IsAsyncReturnType(string csharpType)
    {
        string trimmed = csharpType.Trim();
        return TryUnwrapGeneric(trimmed, "System.Threading.Tasks.Task", out _)
            || TryUnwrapGeneric(trimmed, "Task", out _)
            || TryUnwrapGeneric(trimmed, "System.Threading.Tasks.ValueTask", out _)
            || TryUnwrapGeneric(trimmed, "ValueTask", out _)
            || trimmed is "System.Threading.Tasks.Task" or "Task"
                or "System.Threading.Tasks.ValueTask" or "ValueTask";
    }

    public static bool IsJsonEnvelopeReturnType(string csharpType)
    {
        string trimmed = csharpType.Trim();
        if (trimmed is "string" or "System.String")
            return true;

        return ((TryUnwrapGeneric(trimmed, "System.Threading.Tasks.Task", out string? taskArg)
                    || TryUnwrapGeneric(trimmed, "Task", out taskArg))
                && taskArg!.Trim() is "string" or "System.String")
            || ((TryUnwrapGeneric(trimmed, "System.Threading.Tasks.ValueTask", out string? valueTaskArg)
                    || TryUnwrapGeneric(trimmed, "ValueTask", out valueTaskArg))
                && valueTaskArg!.Trim() is "string" or "System.String");
    }

    public static string MapReturnType(
        string csharpType,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics = null,
        string? location = null,
        IReadOnlySet<string>? blockedAliases = null)
    {
        string trimmed = csharpType.Trim();
        if (IsBlockedType(trimmed, blockedAliases))
        {
            diagnostics?.ReportUnmappedType(
                location ?? trimmed,
                trimmed);
            return "unknown";
        }

        if (TryUnwrapGeneric(trimmed, "System.Threading.Tasks.Task", out string? taskArg)
            || TryUnwrapGeneric(trimmed, "Task", out taskArg))
        {
            return $"Promise<{Map(
                taskArg!,
                recordNames,
                diagnostics,
                location,
                blockedAliases,
                TsTypeMappingContext.JsInterop)}>";
        }

        if (TryUnwrapGeneric(trimmed, "System.Threading.Tasks.ValueTask", out string? valueTaskArg)
            || TryUnwrapGeneric(trimmed, "ValueTask", out valueTaskArg))
        {
            return $"Promise<{Map(
                valueTaskArg!,
                recordNames,
                diagnostics,
                location,
                blockedAliases,
                TsTypeMappingContext.JsInterop)}>";
        }

        if (trimmed is "System.Threading.Tasks.Task" or "Task"
            or "System.Threading.Tasks.ValueTask" or "ValueTask")
        {
            return "Promise<void>";
        }

        return Map(
            trimmed,
            recordNames,
            diagnostics,
            location,
            blockedAliases,
            TsTypeMappingContext.JsInterop);
    }

    /// <summary>
    /// Maps a return type the same way as <see cref="MapReturnType"/>, but substitutes
    /// <paramref name="wireDtoName"/> — the DTO type <see cref="JsonWireContractResolver"/>
    /// resolved from the method's own <c>JsonSerializer.Serialize</c> call site — for the erased
    /// JSON-envelope payload (a bare <c>string</c>, possibly wrapped in <c>Task&lt;&gt;</c>/
    /// <c>ValueTask&lt;&gt;</c>). Without this, an export's declared <c>Task&lt;string&gt;</c>
    /// signature would map to the useless <c>Promise&lt;string&gt;</c> instead of the DTO shape
    /// callers actually receive after JSON-parsing the string.
    /// </summary>
    public static string MapReturnEnvelope(
        string csharpType,
        string wireDtoName,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics = null,
        string? location = null,
        IReadOnlySet<string>? blockedAliases = null)
    {
        string trimmed = csharpType.Trim();
        if (IsBlockedType(trimmed, blockedAliases))
        {
            diagnostics?.ReportUnmappedType(
                location ?? trimmed,
                trimmed);
            return "unknown";
        }
        string dtoType = Map(
            wireDtoName,
            recordNames,
            diagnostics,
            location,
            blockedAliases,
            TsTypeMappingContext.JsonWire);

        if (IsJsonEnvelopeReturnType(trimmed))
        {
            string envelopeType = trimmed;
            if (TryUnwrapGeneric(
                    trimmed,
                    "System.Threading.Tasks.Task",
                    out string? taskArg)
                || TryUnwrapGeneric(trimmed, "Task", out taskArg)
                || TryUnwrapGeneric(
                    trimmed,
                    "System.Threading.Tasks.ValueTask",
                    out taskArg)
                || TryUnwrapGeneric(trimmed, "ValueTask", out taskArg))
            {
                envelopeType = taskArg!;
            }
            if (IsBlockedType(envelopeType, blockedAliases))
            {
                diagnostics?.ReportUnmappedType(
                    location ?? envelopeType,
                    envelopeType);
                return "unknown";
            }
            return IsAsyncReturnType(trimmed) ? $"Promise<{dtoType}>" : dtoType;
        }

        return MapReturnType(
            csharpType,
            recordNames,
            diagnostics,
            location,
            blockedAliases);
    }

    public static string MapParameterType(
        string csharpType,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics = null,
        string? location = null,
        IReadOnlySet<string>? blockedAliases = null,
        JsExportDelegateParameter? delegateParameter = null,
        TsDelegateMappingContext? delegateMappingContext = null)
    {
        string trimmed = csharpType.Trim();
        return delegateParameter is null
            ? Map(
                trimmed,
                recordNames,
                diagnostics,
                location,
                blockedAliases,
                TsTypeMappingContext.JsInterop)
            : MapAuthenticatedDelegate(
                trimmed,
                delegateParameter,
                diagnostics,
                location,
                blockedAliases,
                delegateMappingContext
                    ?? new TsDelegateMappingContext(
                        recordNames,
                        new Dictionary<
                            MetadataTypeDefinitionName,
                            TsLocalTypeKind>(),
                        ContainingAssembly: null));
    }

    public static string MapJsonWireType(
        string csharpType,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics = null,
        string? location = null,
        IReadOnlySet<string>? blockedAliases = null) =>
        Map(
            csharpType.Trim(),
            recordNames,
            diagnostics,
            location,
            blockedAliases,
            TsTypeMappingContext.JsonWire);

    static string Map(
        string csharpType,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics,
        string? location,
        IReadOnlySet<string>? blockedAliases,
        TsTypeMappingContext mappingContext)
    {
        string trimmed = csharpType.Trim();
        if (IsBlockedType(trimmed, blockedAliases))
        {
            diagnostics?.ReportUnmappedType(
                location ?? trimmed,
                trimmed);
            return "unknown";
        }

        // System.Text.Json encodes a byte[] value as one Base64 JSON string. Direct JS interop
        // signatures instead retain their marshalled numeric-array shape.
        if (mappingContext == TsTypeMappingContext.JsonWire
            && (trimmed is "byte[]" or "System.Byte[]"))
            return "string";

        if (trimmed.EndsWith("[]", StringComparison.Ordinal))
        {
            string element = trimmed[..^2];
            string mappedElement = Map(
                element,
                recordNames,
                diagnostics,
                location,
                blockedAliases,
                mappingContext);
            if (mappingContext == TsTypeMappingContext.JsonWire)
            {
                return $"ReadonlyArray<{mappedElement}>";
            }

            return mappedElement.Contains(" | ", StringComparison.Ordinal)
                ? $"({mappedElement})[]"
                : $"{mappedElement}[]";
        }

        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            string inner = trimmed[..^1];
            return $"{Map(
                inner,
                recordNames,
                diagnostics,
                location,
                blockedAliases,
                mappingContext)} | null";
        }

        if (TryUnwrapGeneric(trimmed, "System.Nullable", out string? nullableArg)
            || TryUnwrapGeneric(trimmed, "Nullable", out nullableArg))
        {
            return $"{Map(
                nullableArg!,
                recordNames,
                diagnostics,
                location,
                blockedAliases,
                mappingContext)} | null";
        }

        if (TryMapDictionary(
                trimmed,
                recordNames,
                diagnostics,
                location,
                blockedAliases,
                mappingContext,
                out string? dictionaryType))
        {
            return dictionaryType!;
        }

        if (recordNames.Contains(trimmed))
        {
            if (blockedAliases?.Contains(trimmed) == true)
            {
                diagnostics?.ReportUnmappedType(
                    location ?? trimmed,
                    trimmed);
                return "unknown";
            }
            return LastSegment(trimmed);
        }

        // JsonElement is STJ's own representation of arbitrary/untyped JSON — there is no more
        // specific TS shape to recover here, so "unknown" is the deliberately correct mapping
        // (not a reporting gap the way an unrecognized type like Guid/DateTime/Dictionary is).
        if (trimmed is "System.Text.Json.JsonElement" or "JsonElement")
        {
            return "unknown";
        }

        // JSObject is an intentionally opaque direct-interop handle. Its members are owned by
        // the JavaScript caller, so a generated structural type would be less accurate than
        // requiring the caller to supply an explicit host object.
        if (mappingContext == TsTypeMappingContext.JsInterop
            && trimmed is (
                "System.Runtime.InteropServices.JavaScript.JSObject"
                or "JSObject"))
        {
            return "unknown";
        }
        if (mappingContext == TsTypeMappingContext.JsInterop
            && trimmed is "nint" or "IntPtr" or "System.IntPtr")
        {
            return "number";
        }

        string mapped = trimmed switch
        {
            "string" or "System.String" or "char" or "System.Char" => "string",
            "bool" or "System.Boolean" => "boolean",
            "byte" or "System.Byte" or "sbyte" or "System.SByte"
                or "short" or "System.Int16" or "ushort" or "System.UInt16"
                or "int" or "System.Int32" or "uint" or "System.UInt32"
                or "long" or "System.Int64" or "ulong" or "System.UInt64"
                or "double" or "System.Double" or "float" or "System.Single"
                or "decimal" or "System.Decimal" => "number",
            "void" or "System.Void" => "void",
            _ => "unknown",
        };

        if (mapped == "unknown")
        {
            diagnostics?.ReportUnmappedType(location ?? trimmed, trimmed);
        }

        return mapped;
    }

    static bool TryMapDictionary(
        string typeName,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics,
        string? location,
        IReadOnlySet<string>? blockedAliases,
        TsTypeMappingContext mappingContext,
        out string? mappedType)
    {
        if (!TryUnwrapGeneric(typeName, "System.Collections.Generic.Dictionary", out string? dictionaryArgs)
            && !TryUnwrapGeneric(typeName, "Dictionary", out dictionaryArgs)
            && !TryUnwrapGeneric(typeName, "System.Collections.Generic.IReadOnlyDictionary", out dictionaryArgs)
            && !TryUnwrapGeneric(typeName, "IReadOnlyDictionary", out dictionaryArgs))
        {
            mappedType = null;
            return false;
        }

        if (!TrySplitTopLevelGenericArguments(dictionaryArgs!, out string? keyType, out string? valueType))
        {
            diagnostics?.ReportUnmappedType(location ?? typeName, typeName);
            mappedType = "unknown";
            return true;
        }

        string mappedKey = Map(
            keyType!,
            recordNames,
            diagnostics,
            location,
            blockedAliases,
            mappingContext);
        string mappedValue = Map(
            valueType!,
            recordNames,
            diagnostics,
            location,
            blockedAliases,
            mappingContext);
        if (mappedKey != "string")
        {
            diagnostics?.ReportUnmappedType(location ?? typeName, typeName);
            mappedType = "unknown";
            return true;
        }

        string recordType = $"Record<string, {mappedValue}>";
        mappedType = mappingContext == TsTypeMappingContext.JsonWire
            ? $"Readonly<{recordType}>"
            : recordType;
        return true;
    }

    static string MapAuthenticatedDelegate(
        string csharpType,
        JsExportDelegateParameter delegateParameter,
        TsBindGenDiagnostics? diagnostics,
        string? location,
        IReadOnlySet<string>? blockedAliases,
        TsDelegateMappingContext mappingContext)
    {
        bool nullable = csharpType.EndsWith("?", StringComparison.Ordinal);
        string delegateType = RemoveGlobalPrefix(
            nullable ? csharpType[..^1] : csharpType);
        IReadOnlyList<string> genericArguments = [];
        bool recognized = delegateParameter.Kind switch
        {
            JsExportDelegateKind.Action =>
                delegateType is "Action" or "System.Action"
                || TryUnwrapGenericArguments(
                    delegateType,
                    "Action",
                    "System.Action",
                    out genericArguments),
            JsExportDelegateKind.Func =>
                TryUnwrapGenericArguments(
                    delegateType,
                    "Func",
                    "System.Func",
                    out genericArguments),
            _ => false,
        };

        int expectedGenericArguments =
            delegateParameter.ParameterTypes.Count
            + (delegateParameter.ReturnType is null ? 0 : 1);
        if (!recognized
            || !IsSupportedDelegateSignature(delegateParameter)
            || genericArguments.Count != expectedGenericArguments
            )
        {
            diagnostics?.ReportUnmappedType(
                location ?? csharpType,
                csharpType);
            return "unknown";
        }

        if (delegateParameter.ReturnType is not null
            && IsAsyncManagedType(delegateParameter.ReturnType))
        {
            diagnostics?.ReportUnmappedType(
                location ?? csharpType,
                csharpType);
            return "unknown";
        }

        for (int index = 0;
            index < delegateParameter.ParameterTypes.Count;
            index++)
        {
            if (!MatchesAuthenticatedType(
                    genericArguments[index],
                    delegateParameter.ParameterTypes[index],
                    mappingContext))
            {
                diagnostics?.ReportUnmappedType(
                    location ?? csharpType,
                    csharpType);
                return "unknown";
            }
        }
        if (delegateParameter.ReturnType is not null
            && !MatchesAuthenticatedType(
                genericArguments[^1],
                delegateParameter.ReturnType,
                mappingContext))
        {
            diagnostics?.ReportUnmappedType(
                location ?? csharpType,
                csharpType);
            return "unknown";
        }

        var parameters = new string[delegateParameter.ParameterTypes.Count];
        for (int index = 0; index < parameters.Length; index++)
        {
            AuthenticatedMappingNames mappingNames =
                MappingNamesForAuthenticatedType(
                    genericArguments[index],
                    delegateParameter.ParameterTypes[index],
                    mappingContext.RecordNames,
                    blockedAliases);
            parameters[index] =
                $"arg{index}: {Map(
                    mappingNames.DisplayType,
                    mappingNames.RecordNames,
                    diagnostics,
                    location,
                    mappingNames.BlockedAliases,
                    TsTypeMappingContext.JsInterop)}";
        }

        string returnType;
        if (delegateParameter.ReturnType is null)
        {
            returnType = "undefined";
        }
        else
        {
            AuthenticatedMappingNames mappingNames =
                MappingNamesForAuthenticatedType(
                    genericArguments[^1],
                    delegateParameter.ReturnType,
                    mappingContext.RecordNames,
                    blockedAliases);
            returnType = Map(
                mappingNames.DisplayType,
                mappingNames.RecordNames,
                diagnostics,
                location,
                mappingNames.BlockedAliases,
                TsTypeMappingContext.JsInterop);
        }
        string functionType =
            $"({string.Join(", ", parameters)}) => {returnType}";
        return nullable ? $"({functionType}) | null" : functionType;
    }

    static AuthenticatedMappingNames MappingNamesForAuthenticatedType(
        string displayType,
        TypeRef authenticatedType,
        IReadOnlySet<string> recordNames,
        IReadOnlySet<string>? blockedAliases)
    {
        var frameworkSpellings =
            new HashSet<string>(StringComparer.Ordinal);
        string normalizedDisplay =
            NormalizeAuthenticatedFrameworkDisplay(
            displayType,
            authenticatedType,
            frameworkSpellings);
        if (frameworkSpellings.Count == 0)
        {
            return new AuthenticatedMappingNames(
                normalizedDisplay,
                recordNames,
                blockedAliases);
        }

        var filteredRecordNames = new HashSet<string>(
            recordNames,
            StringComparer.Ordinal);
        filteredRecordNames.ExceptWith(frameworkSpellings);
        if (blockedAliases is null)
        {
            return new AuthenticatedMappingNames(
                normalizedDisplay,
                filteredRecordNames,
                BlockedAliases: null);
        }

        var filteredBlockedAliases = new HashSet<string>(
            blockedAliases,
            StringComparer.Ordinal);
        filteredBlockedAliases.ExceptWith(frameworkSpellings);
        return new AuthenticatedMappingNames(
            normalizedDisplay,
            filteredRecordNames,
            filteredBlockedAliases);
    }

    static string NormalizeAuthenticatedFrameworkDisplay(
        string displayType,
        TypeRef authenticatedType,
        ISet<string> frameworkSpellings)
    {
        string trimmed = RemoveGlobalPrefix(displayType.Trim());
        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            TypeRef nullableType = authenticatedType is
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType: { } nullableDefinition,
                TypeArguments: [var nullableArgument],
            }
            && IsType(
                nullableDefinition,
                "System",
                "Nullable`1")
                ? nullableArgument
                : authenticatedType;
            return $"{NormalizeAuthenticatedFrameworkDisplay(
                trimmed[..^1],
                nullableType,
                frameworkSpellings)}?";
        }

        if (authenticatedType is
            {
                Kind: TypeRefKind.SzArray,
                ElementType: { } arrayElement,
            }
            && trimmed.EndsWith("[]", StringComparison.Ordinal))
        {
            return $"{NormalizeAuthenticatedFrameworkDisplay(
                trimmed[..^2],
                arrayElement,
                frameworkSpellings)}[]";
        }

        if (authenticatedType is
            {
                Kind: TypeRefKind.GenericInstance,
                TypeArguments: var authenticatedArguments,
            }
            && TryParseGenericType(
                trimmed,
                out string? displayDefinition,
                out IReadOnlyList<string> displayArguments)
            && displayArguments.Count
                == authenticatedArguments.Length)
        {
            var normalizedArguments =
                new string[authenticatedArguments.Length];
            for (int index = 0;
                index < authenticatedArguments.Length;
                index++)
            {
                normalizedArguments[index] =
                    NormalizeAuthenticatedFrameworkDisplay(
                    displayArguments[index],
                    authenticatedArguments[index],
                    frameworkSpellings);
            }
            return $"{displayDefinition}<"
                + $"{string.Join(", ", normalizedArguments)}>";
        }

        if (IsFrameworkMappingIdentity(authenticatedType)
            && IsAuthenticFrameworkMapping(authenticatedType))
        {
            string canonicalDisplay =
                authenticatedType.ToQualifiedDisplayString();
            frameworkSpellings.Add(trimmed);
            frameworkSpellings.Add(canonicalDisplay);
            return canonicalDisplay;
        }

        return trimmed;
    }

    static bool MatchesAuthenticatedType(
        string displayType,
        TypeRef authenticatedType,
        TsDelegateMappingContext mappingContext)
    {
        string trimmed = RemoveGlobalPrefix(displayType.Trim());
        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            string inner = trimmed[..^1].TrimEnd();
            if (authenticatedType is
                {
                    Kind: TypeRefKind.GenericInstance,
                    ElementType: { } nullableDefinition,
                    TypeArguments: [var nullableArgument],
                }
                && IsType(
                    nullableDefinition,
                    "System",
                    "Nullable`1")
                && ClassifyAuthenticatedType(
                    nullableArgument,
                    mappingContext)
                    is TsLocalTypeKind.Value)
            {
                return MatchesAuthenticatedType(
                    inner,
                    nullableArgument,
                    mappingContext);
            }

            if (ClassifyAuthenticatedType(
                    authenticatedType,
                    mappingContext)
                is not TsLocalTypeKind.Reference)
            {
                return false;
            }

            return MatchesAuthenticatedType(
                inner,
                authenticatedType,
                mappingContext);
        }

        if (authenticatedType is
            {
                Kind: TypeRefKind.SzArray,
                ElementType: { } arrayElement,
            })
        {
            return trimmed.EndsWith("[]", StringComparison.Ordinal)
                && MatchesAuthenticatedType(
                    trimmed[..^2],
                    arrayElement,
                    mappingContext);
        }

        if (authenticatedType is
            {
                Kind: TypeRefKind.GenericInstance,
                ElementType: { } genericDefinition,
                TypeArguments: var authenticatedArguments,
            })
        {
            if (!TryParseGenericType(
                    trimmed,
                    out string? displayDefinition,
                    out IReadOnlyList<string> displayArguments)
                || displayArguments.Count
                    != authenticatedArguments.Length
                || !MatchesDefinitionName(
                    displayDefinition!,
                    genericDefinition,
                    mappingContext,
                    displayArguments.Count))
            {
                return false;
            }
            if (IsType(
                    genericDefinition,
                    "System",
                    "Nullable`1")
                && (authenticatedArguments is not [var nullableArgument]
                    || ClassifyAuthenticatedType(
                        nullableArgument,
                        mappingContext)
                        is not TsLocalTypeKind.Value))
            {
                return false;
            }

            for (int index = 0;
                index < authenticatedArguments.Length;
                index++)
            {
                if (!MatchesAuthenticatedType(
                        displayArguments[index],
                        authenticatedArguments[index],
                        mappingContext))
                {
                    return false;
                }
            }

            return true;
        }

        if (authenticatedType.Kind != TypeRefKind.Definition
            || !MatchesDefinitionName(
                trimmed,
                authenticatedType,
                mappingContext))
        {
            return false;
        }
        return true;
    }

    static bool IsAsyncManagedType(TypeRef type)
    {
        if (type.Kind == TypeRefKind.Definition)
        {
            return IsType(
                type,
                "System.Threading.Tasks",
                "Task")
                || IsType(
                    type,
                    "System.Threading.Tasks",
                    "ValueTask");
        }

        return type is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { } definition,
        }
        && (IsType(
                definition,
                "System.Threading.Tasks",
                "Task`1")
            || IsType(
                definition,
                "System.Threading.Tasks",
                "ValueTask`1"));
    }

    static bool MatchesDefinitionName(
        string displayName,
        TypeRef authenticatedType,
        TsDelegateMappingContext mappingContext,
        int genericArity = 0)
    {
        displayName = RemoveGlobalPrefix(displayName.Trim());
        if (genericArity > 0
            && !authenticatedType.Name.EndsWith(
                $"`{genericArity}",
                StringComparison.Ordinal))
        {
            return false;
        }

        string simpleName = authenticatedType.Name;
        int arity = simpleName.IndexOf('`');
        if (arity >= 0)
            simpleName = simpleName[..arity];

        bool nameMatches =
            displayName == simpleName
            || displayName
                == $"{authenticatedType.Namespace}.{simpleName}"
            || displayName == authenticatedType.ToDisplayString()
            || displayName
                == authenticatedType.ToQualifiedDisplayString();
        if (authenticatedType.Namespace == "System")
        {
            string? alias = authenticatedType.Name switch
            {
                "Void" => "void",
                "String" => "string",
                "Boolean" => "bool",
                "Char" => "char",
                "Byte" => "byte",
                "SByte" => "sbyte",
                "Int16" => "short",
                "UInt16" => "ushort",
                "Int32" => "int",
                "UInt32" => "uint",
                "Int64" => "long",
                "UInt64" => "ulong",
                "Single" => "float",
                "Double" => "double",
                "Decimal" => "decimal",
                "IntPtr" => "nint",
                "Object" => "object",
                _ => null,
            };
            if (displayName == alias)
                nameMatches = true;
        }

        if (!nameMatches)
            return false;

        if (IsFrameworkMappingIdentity(authenticatedType))
            return IsAuthenticFrameworkMapping(authenticatedType);
        if (IsReservedFrameworkDisplayName(displayName))
            return false;

        string displayLastSegment = LastSegment(displayName);
        if (!mappingContext.RecordNames.Contains(displayName)
            && !mappingContext.RecordNames.Contains(displayLastSegment))
        {
            return true;
        }

        return mappingContext.RecordNames.Contains(
                authenticatedType.ToQualifiedDisplayString())
            && TryGetLocalTypeKind(
                authenticatedType,
                mappingContext,
                out _);
    }

    static bool IsFrameworkMappingIdentity(TypeRef type) =>
        type.Namespace switch
        {
            "System" => type.Name is
                "Void"
                or "String"
                or "Boolean"
                or "Char"
                or "Byte"
                or "SByte"
                or "Int16"
                or "UInt16"
                or "Int32"
                or "UInt32"
                or "Int64"
                or "UInt64"
                or "Single"
                or "Double"
                or "Decimal"
                or "IntPtr"
                or "DateTime"
                or "DateTimeOffset"
                or "Exception"
                or "Object"
                or "Nullable`1"
                or "Span`1"
                or "ArraySegment`1",
            "System.Threading.Tasks" => type.Name is
                "Task"
                or "Task`1"
                or "ValueTask"
                or "ValueTask`1",
            "System.Collections.Generic" => type.Name is
                "Dictionary`2"
                or "IReadOnlyDictionary`2",
            "System.Text.Json" => type.Name == "JsonElement",
            "System.Runtime.InteropServices.JavaScript" =>
                type.Name == "JSObject",
            _ => false,
        };

    static bool IsReservedFrameworkDisplayName(string displayName) =>
        displayName is
            "void" or "Void" or "System.Void"
            or "string" or "String" or "System.String"
            or "bool" or "Boolean" or "System.Boolean"
            or "char" or "Char" or "System.Char"
            or "byte" or "Byte" or "System.Byte"
            or "sbyte" or "SByte" or "System.SByte"
            or "short" or "Int16" or "System.Int16"
            or "ushort" or "UInt16" or "System.UInt16"
            or "int" or "Int32" or "System.Int32"
            or "uint" or "UInt32" or "System.UInt32"
            or "long" or "Int64" or "System.Int64"
            or "ulong" or "UInt64" or "System.UInt64"
            or "float" or "Single" or "System.Single"
            or "double" or "Double" or "System.Double"
            or "decimal" or "Decimal" or "System.Decimal"
            or "nint" or "IntPtr" or "System.IntPtr"
            or "DateTime" or "System.DateTime"
            or "DateTimeOffset" or "System.DateTimeOffset"
            or "Exception" or "System.Exception"
            or "object" or "Object" or "System.Object"
            or "Nullable" or "System.Nullable"
            or "Span" or "System.Span"
            or "ArraySegment" or "System.ArraySegment"
            or "Task" or "System.Threading.Tasks.Task"
            or "ValueTask" or "System.Threading.Tasks.ValueTask"
            or "Dictionary"
            or "System.Collections.Generic.Dictionary"
            or "IReadOnlyDictionary"
            or "System.Collections.Generic.IReadOnlyDictionary"
            or "JsonElement" or "System.Text.Json.JsonElement"
            or "JSObject"
            or "System.Runtime.InteropServices.JavaScript.JSObject";

    static bool IsAuthenticNonCoreFrameworkType(
        TypeRef type,
        string expectedNamespace,
        string expectedName) =>
        type.Kind == TypeRefKind.Definition
        && type.TrustedFrameworkAssembly
        && type.Namespace == expectedNamespace
        && type.Name == expectedName
        && IsAuthenticFrameworkMapping(type);

    static bool IsAuthenticFrameworkMapping(TypeRef type)
    {
        if (!type.TrustedFrameworkAssembly)
            return false;

        return type.Namespace switch
        {
            "System" or "System.Threading.Tasks" =>
                type.Assembly == TypeRef.CoreLibrary,
            "System.Collections.Generic" =>
                type.Assembly == TypeRef.CoreLibrary
                || StringComparer.OrdinalIgnoreCase.Equals(
                    type.Assembly,
                    "System.Collections"),
            "System.Text.Json" =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    type.Assembly,
                    "System.Text.Json"),
            "System.Runtime.InteropServices.JavaScript" =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    type.Assembly,
                    "System.Runtime.InteropServices.JavaScript"),
            _ => false,
        };
    }

    static bool IsType(
        TypeRef type,
        string expectedNamespace,
        string expectedName) =>
        type.Kind == TypeRefKind.Definition
        && type.Assembly == TypeRef.CoreLibrary
        && type.TrustedFrameworkAssembly
        && type.Namespace == expectedNamespace
        && type.Name == expectedName;

    static TsLocalTypeKind? ClassifyAuthenticatedType(
        TypeRef type,
        TsDelegateMappingContext mappingContext)
    {
        if (type.Kind == TypeRefKind.SzArray)
            return TsLocalTypeKind.Reference;

        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        if (IsType(definition, "System", "String")
            || IsType(definition, "System", "Object")
            || IsType(
                definition,
                "System.Threading.Tasks",
                "Task")
            || IsType(
                definition,
                "System.Threading.Tasks",
                "Task`1")
            || IsAuthenticNonCoreFrameworkType(
                definition,
                "System.Collections.Generic",
                "Dictionary`2")
            || IsAuthenticNonCoreFrameworkType(
                definition,
                "System.Collections.Generic",
                "IReadOnlyDictionary`2")
            || IsAuthenticNonCoreFrameworkType(
                definition,
                "System.Runtime.InteropServices.JavaScript",
                "JSObject"))
        {
            return TsLocalTypeKind.Reference;
        }

        if (definition.Kind == TypeRefKind.Definition
            && definition.Assembly == TypeRef.CoreLibrary
            && definition.TrustedFrameworkAssembly
            && definition.Namespace == "System"
            && definition.Name is
                "Boolean"
                or "Char"
                or "Byte"
                or "SByte"
                or "Int16"
                or "UInt16"
                or "Int32"
                or "UInt32"
                or "Int64"
                or "UInt64"
                or "Single"
                or "Double"
                or "Decimal"
                or "IntPtr"
                or "UIntPtr"
                or "DateTime"
                or "DateTimeOffset"
                or "Nullable`1")
        {
            return TsLocalTypeKind.Value;
        }

        if (IsAuthenticNonCoreFrameworkType(
                definition,
                "System.Text.Json",
                "JsonElement"))
        {
            return TsLocalTypeKind.Value;
        }

        if (TryGetLocalTypeKind(
                definition,
                mappingContext,
                out TsLocalTypeKind kind))
        {
            return kind;
        }

        return null;
    }

    static bool TryGetLocalTypeKind(
        TypeRef type,
        TsDelegateMappingContext mappingContext,
        out TsLocalTypeKind kind)
    {
        if (type.Resolution?.Type is not
                MetadataTypeDefinitionName definitionName
            || !MatchesContainingAssembly(
                type,
                mappingContext.ContainingAssembly))
        {
            kind = default;
            return false;
        }

        return mappingContext.LocalTypeKinds.TryGetValue(
            definitionName,
            out kind);
    }

    static bool MatchesContainingAssembly(
        TypeRef type,
        ApiAssemblyIdentity? containingAssembly)
    {
        if (containingAssembly is null
            || type.Resolution?.Origin is not TypeReferenceOrigin origin)
        {
            return false;
        }

        var expected = new AssemblyReferenceIdentity(
            containingAssembly.Name,
            containingAssembly.Version,
            containingAssembly.Culture,
            containingAssembly.PublicKeyToken);
        return origin switch
        {
            TypeReferenceOrigin.CurrentAssembly current =>
                current.Assembly is not null
                && current.Assembly.IsEquivalentTo(expected),
            TypeReferenceOrigin.AssemblyReference reference =>
                reference.Assembly.IsEquivalentTo(expected),
            _ => false,
        };
    }

    static bool IsSupportedDelegateSignature(
        JsExportDelegateParameter signature)
    {
        if (signature.ParameterTypes.Count
            > MaximumDelegateParameterCount)
        {
            return false;
        }

        if (signature.ParameterTypes.Any(
                type => IsType(type, "System", "Void")))
        {
            return false;
        }

        return signature.Kind switch
        {
            JsExportDelegateKind.Action =>
                signature.ReturnType is null,
            JsExportDelegateKind.Func =>
                signature.ReturnType is not null
                && !IsType(
                    signature.ReturnType,
                    "System",
                    "Void"),
            _ => false,
        };
    }

    static string RemoveGlobalPrefix(string typeName) =>
        typeName.StartsWith("global::", StringComparison.Ordinal)
            ? typeName["global::".Length..]
            : typeName;

    static bool TryParseGenericType(
        string typeName,
        out string? definition,
        out IReadOnlyList<string> arguments)
    {
        int genericStart = typeName.IndexOf('<');
        if (genericStart <= 0
            || !typeName.EndsWith(">", StringComparison.Ordinal))
        {
            definition = null;
            arguments = [];
            return false;
        }

        definition = typeName[..genericStart].Trim();
        return TrySplitGenericArguments(
            typeName[(genericStart + 1)..^1],
            out arguments);
    }

    static bool TrySplitTopLevelGenericArguments(
        string arguments,
        out string? first,
        out string? second)
    {
        int depth = 0;
        for (int i = 0; i < arguments.Length; i++)
        {
            char c = arguments[i];
            if (c == '<')
            {
                depth++;
                continue;
            }

            if (c == '>')
            {
                depth--;
                continue;
            }

            if (c == ',' && depth == 0)
            {
                first = arguments[..i].Trim();
                second = arguments[(i + 1)..].Trim();
                return first.Length > 0 && second.Length > 0;
            }
        }

        first = null;
        second = null;
        return false;
    }

    static bool TryUnwrapGenericArguments(
        string typeName,
        string shortBaseName,
        string qualifiedBaseName,
        out IReadOnlyList<string> arguments)
    {
        if (!TryUnwrapGeneric(
                typeName,
                shortBaseName,
                out string? argumentText)
            && !TryUnwrapGeneric(
                typeName,
                qualifiedBaseName,
                out argumentText))
        {
            arguments = [];
            return false;
        }

        return TrySplitGenericArguments(argumentText!, out arguments);
    }

    static bool TrySplitGenericArguments(
        string argumentText,
        out IReadOnlyList<string> arguments)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int index = 0; index < argumentText.Length; index++)
        {
            switch (argumentText[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    if (depth < 0)
                    {
                        arguments = [];
                        return false;
                    }
                    break;
                case ',' when depth == 0:
                    string argument =
                        argumentText[start..index].Trim();
                    if (argument.Length == 0)
                    {
                        arguments = [];
                        return false;
                    }
                    result.Add(argument);
                    start = index + 1;
                    break;
            }
        }

        string finalArgument = argumentText[start..].Trim();
        if (depth != 0 || finalArgument.Length == 0)
        {
            arguments = [];
            return false;
        }
        result.Add(finalArgument);
        arguments = result;
        return true;
    }

    static string LastSegment(string typeName)
    {
        int dot = typeName.LastIndexOf('.');
        return dot >= 0 ? typeName[(dot + 1)..] : typeName;
    }

    static bool TryUnwrapGeneric(string typeName, string genericBaseName, out string? argument)
    {
        string prefix = genericBaseName + "<";
        if (typeName.StartsWith(prefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
        {
            argument = typeName[prefix.Length..^1];
            return true;
        }

        argument = null;
        return false;
    }

    static bool IsBlockedType(
        string typeName,
        IReadOnlySet<string>? blockedAliases)
    {
        if (blockedAliases is null)
            return false;
        if (blockedAliases.Contains(typeName))
            return true;

        while (typeName.EndsWith("[]", StringComparison.Ordinal)
            || typeName.EndsWith("?", StringComparison.Ordinal))
        {
            typeName = typeName.EndsWith("[]", StringComparison.Ordinal)
                ? typeName[..^2]
                : typeName[..^1];
            if (blockedAliases.Contains(typeName))
                return true;
        }

        int genericStart = typeName.IndexOf('<');
        return genericStart > 0
            && blockedAliases.Contains(typeName[..genericStart]);
    }
}
