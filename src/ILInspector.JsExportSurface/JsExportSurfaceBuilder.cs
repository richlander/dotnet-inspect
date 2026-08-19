using System.Text.RegularExpressions;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

/// <summary>
/// Builds a <see cref="JsExportSurface"/> from an already-extracted <see cref="ApiSurface"/>.
/// </summary>
/// <remarks>
/// <para>
/// Function discovery scans for <c>[JSExport]</c>-attributed static members directly — the true
/// root of the wasm/JS boundary, since the compiler itself rejects a non-marshalable signature at
/// that attribute.
/// </para>
/// <para>
/// Record discovery instead reads the assembly's <c>System.Text.Json.Serialization.JsonSerializerContext</c>-derived
/// type (the source-generated context STJ itself uses to serialize each <c>[JSExport]</c>
/// method's payload). Each <c>[JsonSerializable(typeof(T))]</c> attribute on that context compiles
/// to a real <c>JsonTypeInfo&lt;T&gt;</c>-typed property, readable from metadata alone — no IL-body
/// analysis needed, and no risk of missing a root the way scanning exported *method signatures*
/// would (those are always plain strings/<c>Task&lt;string&gt;</c>; the actual DTO only appears
/// inside the method body's <c>JsonSerializer.Serialize</c> call, invisible to signature-only
/// discovery). This list is not a heuristic: STJ's fast (non-reflection) serialization path
/// requires every (de)serialized type to be registered here, so it is exactly the set of shapes
/// that can flow across the boundary via this ABI style.
/// </para>
/// </remarks>
public static class JsExportSurfaceBuilder
{
    const string JsExportAttributeName = "System.Runtime.InteropServices.JavaScript.JSExport";
    const string JsonTypeInfoPrefix = "System.Text.Json.Serialization.Metadata.JsonTypeInfo<";
    const string JsonSerializerContextBaseType = "System.Text.Json.Serialization.JsonSerializerContext";

    public static JsExportSurface Build(ApiSurface surface)
    {
        var typesByName = surface.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);

        var functions = new List<JsExportFunction>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                if (!member.IsStatic || !HasJsExportAttribute(member))
                {
                    continue;
                }

                if (member.IsUnsafe)
                {
                    // [JSExport] rejects unsafe signatures at compile time; a member reaching
                    // here with IsUnsafe set would indicate an extractor/attribute mismatch worth
                    // investigating, not a case to silently skip.
                    throw new InvalidOperationException(
                        $"'{type.Name}.{member.Name}' is [JSExport] but reports IsUnsafe; "
                        + "this should be unreachable given JSExport's compile-time marshalability check.");
                }

                ApiSignature? signature = member.SignatureModel;
                if (signature is null)
                {
                    throw new InvalidOperationException(
                        $"'{type.Name}.{member.Name}' is [JSExport] but has no signature model; "
                        + "extraction must run with signature models populated.");
                }

                functions.Add(new JsExportFunction
                {
                    DeclaringType = type.Name,
                    Name = member.Name,
                    ReturnType = signature.ReturnType ?? member.ReturnType ?? "void",
                    Parameters = signature.Parameters,
                });
            }
        }

        // Record roots come from the assembly's JsonSerializerContext-derived type: each
        // [JsonSerializable(typeof(T))] on it compiles to a JsonTypeInfo<T> property, so T's name
        // is readable directly from that property's return-type text.
        var records = new List<ApiType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        foreach (ApiType type in surface.Types)
        {
            if (type.BaseType != JsonSerializerContextBaseType)
            {
                continue;
            }

            foreach (ApiMember member in type.Members)
            {
                string? returnType = member.SignatureModel?.ReturnType ?? member.ReturnType;
                if (member.Kind != "property"
                    || returnType is null
                    || !returnType.StartsWith(JsonTypeInfoPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string rootTypeName = returnType[JsonTypeInfoPrefix.Length..^1];
                foreach (string candidate in ExtractCandidateTypeNames(rootTypeName))
                {
                    queue.Enqueue(candidate);
                }
            }
        }

        // Transitive closure: a registered root record can itself reference other locally-declared
        // record types through its properties, even when those nested types are never independently
        // registered on the JsonSerializerContext (STJ only requires the outermost type to be
        // registered).
        while (queue.Count > 0)
        {
            string name = queue.Dequeue();
            if (!seen.Add(name) || !typesByName.TryGetValue(name, out ApiType? type))
            {
                continue;
            }

            records.Add(type);

            foreach (ApiMember member in type.Members)
            {
                if (member.Kind != "property")
                {
                    continue;
                }

                string? propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType;
                foreach (string candidate in ExtractCandidateTypeNames(propertyType))
                {
                    queue.Enqueue(candidate);
                }
            }
        }

        return new JsExportSurface { Functions = functions, Records = records };
    }

    static bool HasJsExportAttribute(ApiMember member) =>
        member.Attributes.Any(a => a == JsExportAttributeName || a.EndsWith(".JSExport", StringComparison.Ordinal));

    // A local type name referenced from a signature: a leading identifier, optionally
    // dotted/nested, ignoring array/nullable/generic decoration. Matches simple record-style
    // names such as "BrowserTypeMetadata" or "BrowserTypeGraphNode[]" -> "BrowserTypeGraphNode".
    static readonly Regex TypeNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_.]*", RegexOptions.Compiled);

    static IEnumerable<string> ExtractCandidateTypeNames(JsExportFunction function)
    {
        foreach (string name in ExtractCandidateTypeNames(function.ReturnType))
        {
            yield return name;
        }

        foreach (ApiParameter parameter in function.Parameters)
        {
            foreach (string name in ExtractCandidateTypeNames(parameter.Type))
            {
                yield return name;
            }
        }
    }

    static IEnumerable<string> ExtractCandidateTypeNames(string? signatureText)
    {
        if (string.IsNullOrEmpty(signatureText))
        {
            yield break;
        }

        foreach (Match match in TypeNamePattern.Matches(signatureText))
        {
            string name = match.Value;
            int lastDot = name.LastIndexOf('.');
            yield return lastDot >= 0 ? name[(lastDot + 1)..] : name;
        }

        // Also walk inside a generic argument list, e.g. Task<BrowserTypeMetadata>.
        int genericStart = signatureText.IndexOf('<');
        if (genericStart >= 0)
        {
            int genericEnd = signatureText.LastIndexOf('>');
            if (genericEnd > genericStart)
            {
                string inner = signatureText[(genericStart + 1)..genericEnd];
                foreach (string name in ExtractCandidateTypeNames(inner))
                {
                    yield return name;
                }
            }
        }
    }
}
