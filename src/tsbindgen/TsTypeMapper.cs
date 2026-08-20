using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

/// <summary>
/// Rewrites C# signature-text type names into TypeScript type text. All target-language opinion
/// lives here — <c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c> unwrap to <c>Promise&lt;T&gt;</c>,
/// C# built-ins map to TS primitives, and locally-declared record types are passed through by
/// name (their declarations are emitted separately by <see cref="DtsEmitter"/>).
/// </summary>
static class TsTypeMapper
{
    public static string MapReturnType(string csharpType, IReadOnlySet<string> recordNames)
    {
        string trimmed = csharpType.Trim();

        if (TryUnwrapGeneric(trimmed, "System.Threading.Tasks.Task", out string? taskArg)
            || TryUnwrapGeneric(trimmed, "Task", out taskArg))
        {
            return $"Promise<{Map(taskArg!, recordNames)}>";
        }

        if (TryUnwrapGeneric(trimmed, "System.Threading.Tasks.ValueTask", out string? valueTaskArg)
            || TryUnwrapGeneric(trimmed, "ValueTask", out valueTaskArg))
        {
            return $"Promise<{Map(valueTaskArg!, recordNames)}>";
        }

        if (trimmed is "System.Threading.Tasks.Task" or "Task"
            or "System.Threading.Tasks.ValueTask" or "ValueTask")
        {
            return "Promise<void>";
        }

        return Map(trimmed, recordNames);
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
        string csharpType, string wireDtoName, IReadOnlySet<string> recordNames)
    {
        string trimmed = csharpType.Trim();
        string dtoType = Map(wireDtoName, recordNames);

        if ((TryUnwrapGeneric(trimmed, "System.Threading.Tasks.Task", out string? taskArg)
                || TryUnwrapGeneric(trimmed, "Task", out taskArg))
            && taskArg!.Trim() is "string" or "System.String")
        {
            return $"Promise<{dtoType}>";
        }

        if ((TryUnwrapGeneric(trimmed, "System.Threading.Tasks.ValueTask", out string? valueTaskArg)
                || TryUnwrapGeneric(trimmed, "ValueTask", out valueTaskArg))
            && valueTaskArg!.Trim() is "string" or "System.String")
        {
            return $"Promise<{dtoType}>";
        }

        if (trimmed is "string" or "System.String")
        {
            return dtoType;
        }

        // The resolved DTO doesn't correspond to a string-shaped envelope this method knows how
        // to substitute into (an unexpected shape) — fall back to the raw signature mapping
        // rather than silently applying the DTO to something it wasn't resolved against.
        return MapReturnType(csharpType, recordNames);
    }

    public static string MapParameterType(string csharpType, IReadOnlySet<string> recordNames) =>
        Map(csharpType.Trim(), recordNames);

    static string Map(string csharpType, IReadOnlySet<string> recordNames)
    {
        string trimmed = csharpType.Trim();

        if (trimmed.EndsWith("[]", StringComparison.Ordinal))
        {
            string element = trimmed[..^2];
            string mappedElement = Map(element, recordNames);
            // Parenthesize a union element (e.g. "T | null") before appending "[]": TS applies
            // "[]" tighter than "|", so an unparenthesized "T | null[]" means "T, or an array of
            // null" rather than the intended "an array of (T or null)".
            return mappedElement.Contains(" | ", StringComparison.Ordinal)
                ? $"({mappedElement})[]"
                : $"{mappedElement}[]";
        }

        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            string inner = trimmed[..^1];
            return $"{Map(inner, recordNames)} | null";
        }

        if (TryUnwrapGeneric(trimmed, "System.Nullable", out string? nullableArg)
            || TryUnwrapGeneric(trimmed, "Nullable", out nullableArg))
        {
            return $"{Map(nullableArg!, recordNames)} | null";
        }

        string simpleName = LastSegment(trimmed);

        if (recordNames.Contains(simpleName))
        {
            return simpleName;
        }

        return trimmed switch
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
}
