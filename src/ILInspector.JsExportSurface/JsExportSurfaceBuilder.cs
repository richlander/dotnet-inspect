using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

/// <summary>
/// Builds a <see cref="JsExportSurface"/> from an already-extracted <see cref="ApiSurface"/>.
/// </summary>
public static class JsExportSurfaceBuilder
{
    const string JsonTypeInfoPrefix = "System.Text.Json.Serialization.Metadata.JsonTypeInfo<";
    const string JsonTypeInfoMetadataName =
        "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1";
    const string JsonSerializerContextBaseType = "System.Text.Json.Serialization.JsonSerializerContext";
    const string SystemTextJsonAssemblyName = "System.Text.Json";

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
        var typesByScopedIdentity =
            surface.AssemblyIdentity is { } assemblyIdentity
                ? surface.Types
                    .Select(type => (
                        Identity: new ApiTypeReferenceIdentity(
                            assemblyIdentity,
                            type.FullName),
                        Type: type))
                    .GroupBy(candidate => candidate.Identity)
                    .Where(group => group
                        .Select(candidate => candidate.Type)
                        .Distinct()
                        .Count() == 1)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().Type)
                : [];

        var incompleteBodyTokens = new HashSet<int>();
        if (bodyIndex is not null)
        {
                foreach (AnalysisDiagnostic diagnostic in bodyIndex.Diagnostics)
                {
                    incompleteBodyTokens.Add(diagnostic.MethodToken);
                    if (diagnostic.SourceMethodToken is { } sourceMethodToken)
                        incompleteBodyTokens.Add(sourceMethodToken);
                }
        }

        var functions = new List<JsExportFunction>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                if (!member.IsStatic || !HasJsExportAttribute(member))
                    continue;

                if (member.IsUnsafe)
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "unsafe JS exports are not supported");
                if (member.SignatureDecodeStatus
                    == SignatureDecodeStatus.Degraded)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "JS export signature metadata is degraded");
                }

                ApiSignature? signature = member.SignatureModel;
                if (signature is null)
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "JS export signature metadata is unavailable");

                var function = new JsExportFunction
                {
                    DeclaringType = type.Name,
                    Name = member.Name,
                    ReturnType = signature.ReturnType ?? member.ReturnType ?? "void",
                    Parameters = signature.Parameters,
                };

                if (bodyIndex is not null && member.MetadataToken is { } token)
                {
                    if (incompleteBodyTokens.Contains(token))
                    {
                        throw new UnsupportedJsExportSurfaceException(
                            FormatMemberLocation(type, member),
                            "JS export body analysis is incomplete");
                    }
                    function = JsonWireContractResolver.Attach(bodyIndex, function, token);
                }

                functions.Add(function);
            }
        }

        var records = new List<ApiType>();
        var enums = new List<ApiType>();
        var discovered = new HashSet<ApiType>();
        var policiesByType = new Dictionary<ApiType, HashSet<JsonWireNamingPolicy>>();
        var queue = new Queue<(
            string? Name,
            ApiTypeReferenceIdentity? Identity,
            JsonWireNamingPolicy Policy)>();

        foreach (ApiType type in surface.Types)
        {
            if (type.BaseType != JsonSerializerContextBaseType)
                continue;
            if (surface.AssemblyIdentity is not null
                && !IsTrustedSystemTextJsonType(
                    type.BaseTypeReference,
                    JsonSerializerContextBaseType))
            {
                continue;
            }

            foreach (ApiMember member in type.Members)
            {
                if (member.Kind != "property")
                    continue;
                if (member.SignatureDecodeStatus
                    == SignatureDecodeStatus.Degraded)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "serializer-context property signature metadata is degraded");
                }

                string? returnType = member.SignatureModel?.ReturnType ?? member.ReturnType;
                if (returnType is null
                    || !returnType.StartsWith(JsonTypeInfoPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string rootTypeName = returnType[JsonTypeInfoPrefix.Length..^1];
                JsonWireNamingPolicy policy =
                    type.JsonPropertyNamingPolicy
                        ?? JsonWireNamingPolicy.None;
                IReadOnlyList<ApiTypeReferenceIdentity>? references =
                    member.SignatureModel?.ReturnTypeReferences;
                if (surface.AssemblyIdentity is not null
                    && (references is null
                        || !references.Any(reference =>
                            IsTrustedSystemTextJsonType(
                                reference,
                                JsonTypeInfoMetadataName))))
                {
                    continue;
                }

                if (references?.Count > 0)
                {
                    foreach (ApiTypeReferenceIdentity reference
                        in references)
                    {
                        if (IsTrustedSystemTextJsonType(
                                reference,
                                JsonTypeInfoMetadataName))
                        {
                            continue;
                        }
                        queue.Enqueue((null, reference, policy));
                    }
                }
                else
                {
                    foreach (string candidate
                        in ExtractCandidateTypeNames(rootTypeName))
                    {
                        queue.Enqueue((candidate, null, policy));
                    }
                }
            }
        }

        while (queue.Count > 0)
        {
            (string? name, ApiTypeReferenceIdentity? identity,
                JsonWireNamingPolicy namingPolicy) = queue.Dequeue();
            ApiType? type = null;
            if (identity is not null)
                typesByScopedIdentity.TryGetValue(identity, out type);
            else if (name is not null)
                typesByIdentity.TryGetValue(name, out type);
            if (type is null)
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

            if (type.Kind == "enum"
                || type.JsonConverterAttributeCount > 0)
                continue;

            foreach (ApiMember member in type.Members)
            {
                if (!JsonWireMemberRules.IsSerialized(member)
                    || member.JsonConverterAttributeCount > 0)
                    continue;
                if (member.SignatureDecodeStatus
                    == SignatureDecodeStatus.Degraded)
                {
                    throw new UnsupportedJsExportSurfaceException(
                        FormatMemberLocation(type, member),
                        "serialized member signature metadata is degraded");
                }

                string? propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType;
                if (member.SignatureModel?.ReturnTypeReferences.Count > 0)
                {
                    foreach (ApiTypeReferenceIdentity reference
                        in member.SignatureModel.ReturnTypeReferences)
                    {
                        queue.Enqueue((null, reference, namingPolicy));
                    }
                }
                else
                {
                    foreach (string candidate
                        in ExtractCandidateTypeNames(propertyType))
                    {
                        queue.Enqueue((candidate, null, namingPolicy));
                    }
                }
            }
        }

        return new JsExportSurface
        {
            AssemblyIdentity = surface.AssemblyIdentity,
            Functions = functions,
            Records = records,
            Enums = enums,
        };
    }

    static bool HasJsExportAttribute(ApiMember member) =>
        member.HasRuntimeJsExport;

    static string FormatMemberLocation(ApiType type, ApiMember member) =>
        member.MetadataToken is { } memberToken
            ? $"member 0x{memberToken:X8}"
            : type.MetadataToken is { } typeToken
                ? $"type 0x{typeToken:X8} member"
                : "JS export member";

    static bool IsTrustedSystemTextJsonType(
        ApiTypeReferenceIdentity? reference,
        string fullName) =>
        reference is not null
        && reference.FullName == fullName
        && reference.Assembly.Name == SystemTextJsonAssemblyName
        && PlatformKeys.IsPlatform(
            reference.Assembly.PublicKeyToken);

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
