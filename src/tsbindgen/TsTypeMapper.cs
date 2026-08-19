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

    public static string MapParameterType(string csharpType, IReadOnlySet<string> recordNames) =>
        Map(csharpType.Trim(), recordNames);

    static string Map(string csharpType, IReadOnlySet<string> recordNames)
    {
        string trimmed = csharpType.Trim();

        if (trimmed.EndsWith("[]", StringComparison.Ordinal))
        {
            string element = trimmed[..^2];
            return $"{Map(element, recordNames)}[]";
        }

        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            string inner = trimmed[..^1];
            return $"{Map(inner, recordNames)} | null";
        }

        string simpleName = LastSegment(trimmed);

        if (recordNames.Contains(simpleName))
        {
            return simpleName;
        }

        return trimmed switch
        {
            "string" or "System.String" => "string",
            "bool" or "System.Boolean" => "boolean",
            "int" or "System.Int32" or "long" or "System.Int64"
                or "short" or "System.Int16" or "double" or "System.Double"
                or "float" or "System.Single" => "number",
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
