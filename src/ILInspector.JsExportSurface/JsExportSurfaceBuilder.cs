using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

/// <summary>
/// Builds a <see cref="JsExportSurface"/> from an already-extracted <see cref="ApiSurface"/>.
/// </summary>
public static class JsExportSurfaceBuilder
{
    const string JsExportAttributeName = "System.Runtime.InteropServices.JavaScript.JSExport";
    const string JsonTypeInfoPrefix = "System.Text.Json.Serialization.Metadata.JsonTypeInfo<";
    const string JsonSerializerContextBaseType = "System.Text.Json.Serialization.JsonSerializerContext";

    public static JsExportSurface Build(ApiSurface surface) => Build(surface, bodyIndex: null);

    public static JsExportSurface Build(ApiSurface surface, LibraryBodyIndex? bodyIndex)
    {
        var typesByName = surface.Types
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);

        var functions = new List<JsExportFunction>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                if (!member.IsStatic || !HasJsExportAttribute(member))
                    continue;

                if (member.IsUnsafe)
                    throw new InvalidOperationException(
                        $"'{type.Name}.{member.Name}' is [JSExport] but reports IsUnsafe; this should be unreachable given JSExport's compile-time marshalability check.");

                ApiSignature? signature = member.SignatureModel;
                if (signature is null)
                    throw new InvalidOperationException(
                        $"'{type.Name}.{member.Name}' is [JSExport] but has no signature model; extraction must run with signature models populated.");

                var function = new JsExportFunction
                {
                    DeclaringType = type.Name,
                    Name = member.Name,
                    ReturnType = signature.ReturnType ?? member.ReturnType ?? "void",
                    Parameters = signature.Parameters,
                };

                if (bodyIndex is not null && member.MetadataToken is { } token)
                    function = JsonWireContractResolver.Attach(bodyIndex, function, token);

                functions.Add(function);
            }
        }

        var records = new List<ApiType>();
        var enums = new List<ApiType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string Name, JsonWireNamingPolicy? Policy)>();
        var resolvedPoliciesByTypeName = new Dictionary<string, JsonWireNamingPolicy?>(StringComparer.Ordinal);

        foreach (ApiType type in surface.Types)
        {
            if (type.BaseType != JsonSerializerContextBaseType)
                continue;

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
                    queue.Enqueue((candidate, type.JsonPropertyNamingPolicy));
            }
        }

        while (queue.Count > 0)
        {
            (string name, JsonWireNamingPolicy? namingPolicy) = queue.Dequeue();
            if (!seen.Add(name) || !typesByName.TryGetValue(name, out ApiType? type))
                continue;

            resolvedPoliciesByTypeName[name] = namingPolicy;
            type.JsonPropertyNamingPolicy = namingPolicy;

            if (type.Kind == "enum")
            {
                enums.Add(type);
                continue;
            }

            records.Add(type);

            foreach (ApiMember member in type.Members)
            {
                if (member.Kind != "property"
                    || member.IsCompilerGenerated
                    || member.HasJsonIgnore
                    || (member.Accessibility is not null && !member.HasJsonInclude))
                {
                    continue;
                }

                string? propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType;
                foreach (string candidate in ExtractCandidateTypeNames(propertyType))
                {
                    JsonWireNamingPolicy? nestedPolicy = resolvedPoliciesByTypeName.TryGetValue(type.Name, out JsonWireNamingPolicy? current)
                        ? current
                        : type.JsonPropertyNamingPolicy;
                    queue.Enqueue((candidate, nestedPolicy));
                }
            }
        }

        return new JsExportSurface { Functions = functions, Records = records, Enums = enums };
    }

    static bool HasJsExportAttribute(ApiMember member) =>
        member.Attributes.Any(a => a == JsExportAttributeName || a.EndsWith(".JSExport", StringComparison.Ordinal));

    static IEnumerable<string> ExtractCandidateTypeNames(JsExportFunction function)
    {
        foreach (string name in ExtractCandidateTypeNames(function.ReturnType))
            yield return name;

        foreach (ApiParameter parameter in function.Parameters)
        {
            foreach (string name in ExtractCandidateTypeNames(parameter.Type))
                yield return name;
        }
    }

    static IEnumerable<string> ExtractCandidateTypeNames(string? signatureText)
    {
        if (string.IsNullOrEmpty(signatureText))
            yield break;

        string trimmed = signatureText.Trim();
        while (trimmed.EndsWith("[]", StringComparison.Ordinal) || trimmed.EndsWith("?", StringComparison.Ordinal))
            trimmed = trimmed.EndsWith("[]", StringComparison.Ordinal) ? trimmed[..^2] : trimmed[..^1];

        int genericStart = trimmed.IndexOf('<');
        string leading = genericStart >= 0 ? trimmed[..genericStart] : trimmed;
        int lastDot = leading.LastIndexOf('.');
        yield return lastDot >= 0 ? leading[(lastDot + 1)..] : leading;

        if (genericStart < 0)
            yield break;

        int genericEnd = trimmed.LastIndexOf('>');
        if (genericEnd <= genericStart)
            yield break;

        string inner = trimmed[(genericStart + 1)..genericEnd];
        int depth = 0;
        int segmentStart = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                foreach (string candidate in ExtractCandidateTypeNames(inner[segmentStart..i]))
                    yield return candidate;
                segmentStart = i + 1;
            }
        }

        foreach (string candidate in ExtractCandidateTypeNames(inner[segmentStart..]))
            yield return candidate;
    }
}
