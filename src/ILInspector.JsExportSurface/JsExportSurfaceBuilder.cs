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
        var typesByIdentity = surface.Types
            .SelectMany(type =>
                new[] { type.Name, type.FullName, type.MetadataName }
                    .Where(identity => !string.IsNullOrEmpty(identity))
                    .Distinct(StringComparer.Ordinal)
                    .Select(identity => (Identity: identity!, Type: type)))
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Where(group => group.Select(candidate => candidate.Type)
                .Distinct()
                .Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.First().Type,
                StringComparer.Ordinal);

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
        var discovered = new HashSet<ApiType>();
        var policiesByType = new Dictionary<ApiType, HashSet<JsonWireNamingPolicy>>();
        var queue = new Queue<(string Name, JsonWireNamingPolicy Policy)>();

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
                    queue.Enqueue((candidate, type.JsonPropertyNamingPolicy ?? JsonWireNamingPolicy.None));
            }
        }

        while (queue.Count > 0)
        {
            (string name, JsonWireNamingPolicy namingPolicy) = queue.Dequeue();
            if (!typesByIdentity.TryGetValue(name, out ApiType? type))
                continue;

            if (!policiesByType.TryGetValue(type, out HashSet<JsonWireNamingPolicy>? policies))
            {
                policies = [];
                policiesByType.Add(type, policies);
            }

            if (!policies.Add(namingPolicy))
                continue;

            type.JsonPropertyNamingPolicy = policies.Count == 1
                ? namingPolicy
                : JsonWireNamingPolicy.Unsupported;

            if (discovered.Add(type))
            {
                if (type.Kind == "enum")
                    enums.Add(type);
                else
                    records.Add(type);
            }

            if (type.Kind == "enum")
                continue;

            foreach (ApiMember member in type.Members)
            {
                if (!JsonWireMemberRules.IsSerialized(member))
                    continue;

                string? propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType;
                foreach (string candidate in ExtractCandidateTypeNames(propertyType))
                    queue.Enqueue((candidate, namingPolicy));
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
        yield return leading;

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
