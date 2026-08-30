using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace ILInspector.TypeScriptGeneration;

enum TsTypeMappingContext
{
    JsInterop,
    JsonWire,
}

/// <summary>
/// Rewrites C# signature-text type names into TypeScript type text. All target-language opinion
/// lives here — <c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c> unwrap to <c>Promise&lt;T&gt;</c>,
/// C# built-ins map to TS primitives, and locally-declared record types are passed through by
/// name (their declarations are emitted separately by <see cref="DtsEmitter"/>).
/// </summary>
static class TsTypeMapper
{
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
        IReadOnlySet<string>? blockedAliases = null,
        IReadOnlyDictionary<string, string>? mappedTypeNames = null)
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
                mappedTypeNames,
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
                mappedTypeNames,
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
            mappedTypeNames,
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
        IReadOnlySet<string>? blockedAliases = null,
        IReadOnlyDictionary<string, string>? mappedTypeNames = null)
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
            mappedTypeNames,
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
            blockedAliases,
            mappedTypeNames);
    }

    public static string MapParameterType(
        string csharpType,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics = null,
        string? location = null,
        IReadOnlySet<string>? blockedAliases = null,
        IReadOnlyDictionary<string, string>? mappedTypeNames = null) =>
        Map(
            csharpType.Trim(),
            recordNames,
            diagnostics,
            location,
            blockedAliases,
            mappedTypeNames,
            TsTypeMappingContext.JsInterop);

    public static string MapJsonWireType(
        string csharpType,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics = null,
        string? location = null,
        IReadOnlySet<string>? blockedAliases = null,
        IReadOnlyDictionary<string, string>? mappedTypeNames = null) =>
        Map(
            csharpType.Trim(),
            recordNames,
            diagnostics,
            location,
            blockedAliases,
            mappedTypeNames,
            TsTypeMappingContext.JsonWire);

    static string Map(
        string csharpType,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics,
        string? location,
        IReadOnlySet<string>? blockedAliases,
        IReadOnlyDictionary<string, string>? mappedTypeNames,
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
                mappedTypeNames,
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
                mappedTypeNames,
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
                mappedTypeNames,
                mappingContext)} | null";
        }

        if (TryMapDictionary(
                trimmed,
                recordNames,
                diagnostics,
                location,
                blockedAliases,
                mappedTypeNames,
                mappingContext,
                out string? dictionaryType))
        {
            return dictionaryType!;
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

        if (mapped != "unknown")
            return mapped;

        if (mappedTypeNames?.TryGetValue(
                trimmed,
                out string? mappedTypeName) == true)
        {
            return mappedTypeName;
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

        diagnostics?.ReportUnmappedType(location ?? trimmed, trimmed);
        return mapped;
    }

    static bool TryMapDictionary(
        string typeName,
        IReadOnlySet<string> recordNames,
        TsBindGenDiagnostics? diagnostics,
        string? location,
        IReadOnlySet<string>? blockedAliases,
        IReadOnlyDictionary<string, string>? mappedTypeNames,
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
            mappedTypeNames,
            mappingContext);
        string mappedValue = Map(
            valueType!,
            recordNames,
            diagnostics,
            location,
            blockedAliases,
            mappedTypeNames,
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
