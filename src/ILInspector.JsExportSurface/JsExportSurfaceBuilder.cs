using ILInspector.Analysis;
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

    /// <summary>
    /// Builds a <see cref="JsExportSurface"/> from <paramref name="surface"/>, without wire-contract
    /// resolution (<see cref="JsExportFunction.ReturnWireType"/>/
    /// <see cref="JsExportFunction.ParameterWireTypes"/> stay unset). See the
    /// <see cref="Build(ApiSurface, LibraryBodyIndex)"/> overload to resolve them.
    /// </summary>
    public static JsExportSurface Build(ApiSurface surface) => Build(surface, bodyIndex: null);

    /// <summary>
    /// Builds a <see cref="JsExportSurface"/> from <paramref name="surface"/>. When
    /// <paramref name="bodyIndex"/> is supplied (the same assembly's IL-body evidence), each
    /// function's <see cref="JsExportFunction.ReturnWireType"/> and
    /// <see cref="JsExportFunction.ParameterWireTypes"/> are additionally resolved from its own
    /// body's <c>JsonSerializer.Serialize</c>/<c>Deserialize</c> call sites — see
    /// <see cref="JsonWireContractResolver"/>. Without it, both remain unset.
    /// </summary>
    /// <remarks>
    /// A separate overload rather than an optional parameter: this keeps the two-argument call
    /// site's arity stable for any binary caller compiled against the single-argument overload
    /// (default-parameter values are baked into the caller at compile time, not looked up at the
    /// callee, so adding one to the existing method would have been a breaking change for any
    /// pre-existing compiled caller).
    /// </remarks>
    public static JsExportSurface Build(ApiSurface surface, LibraryBodyIndex? bodyIndex)
    {
        // Keyed by simple (last-dotted-segment) name, since that's what's recoverable from
        // signature text alone (see remarks above). Two distinct types sharing a simple name in
        // different namespaces are ambiguous under this scheme; rather than throwing (which would
        // fail discovery for an assembly's entire surface over an unrelated collision), such names
        // are dropped from the lookup so they simply fail to resolve as a known record.
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

                var function = new JsExportFunction
                {
                    DeclaringType = type.Name,
                    Name = member.Name,
                    ReturnType = signature.ReturnType ?? member.ReturnType ?? "void",
                    Parameters = signature.Parameters,
                };

                if (bodyIndex is not null && member.MetadataToken is { } token)
                {
                    function = JsonWireContractResolver.Attach(bodyIndex, function, token);
                }

                functions.Add(function);
            }
        }

        // Record roots come from the assembly's JsonSerializerContext-derived type: each
        // [JsonSerializable(typeof(T))] on it compiles to a JsonTypeInfo<T> property, so T's name
        // is readable directly from that property's return-type text.
        var records = new List<ApiType>();
        var enums = new List<ApiType>();
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

            // An enum has no properties to project as an interface — STJ's JsonStringEnumConverter
            // serializes it as one of its member names (a string), not an object. Route it to
            // Enums instead of emitting a property-less {} interface for it, and skip the property
            // walk below (an enum's members are its values, not properties to traverse for further
            // nested-type roots).
            if (type.Kind == "enum")
            {
                enums.Add(type);
                continue;
            }

            records.Add(type);

            foreach (ApiMember member in type.Members)
            {
                if (member.Kind != "property"
                    // Compiler-synthesized record infrastructure (e.g. a positional record's
                    // `EqualityContract` getter) is never intended as wire-contract shape.
                    // Detected directly via [CompilerGenerated] rather than accessibility, since a
                    // legitimate non-public property opted into the wire contract via
                    // [JsonInclude] would otherwise look identical (non-null Accessibility) to
                    // synthesized infrastructure.
                    || member.IsCompilerGenerated
                    || (member.Accessibility is not null && !member.HasJsonInclude))
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

        return new JsExportSurface { Functions = functions, Records = records, Enums = enums };
    }

    static bool HasJsExportAttribute(ApiMember member) =>
        member.Attributes.Any(a => a == JsExportAttributeName || a.EndsWith(".JSExport", StringComparison.Ordinal));

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

        string trimmed = signatureText.Trim();
        // Strip array/nullable decoration before extracting the leading name, so e.g.
        // "WidgetOwner[]" and "WidgetOwner?" both still yield "WidgetOwner".
        while (trimmed.EndsWith("[]", StringComparison.Ordinal) || trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            trimmed = trimmed.EndsWith("[]", StringComparison.Ordinal) ? trimmed[..^2] : trimmed[..^1];
        }

        int genericStart = trimmed.IndexOf('<');
        // The leading type name: everything before the generic argument list, or the whole text
        // when there isn't one (e.g. a bare "BrowserTypeMetadata" or "Dictionary").
        string leading = genericStart >= 0 ? trimmed[..genericStart] : trimmed;
        int lastDot = leading.LastIndexOf('.');
        yield return lastDot >= 0 ? leading[(lastDot + 1)..] : leading;

        if (genericStart < 0)
        {
            yield break;
        }

        int genericEnd = trimmed.LastIndexOf('>');
        if (genericEnd <= genericStart)
        {
            yield break;
        }

        // Recurse into every top-level comma-separated generic argument, not just the first
        // (e.g. both "string" and "WidgetOwner" in Dictionary<string, WidgetOwner>). Nesting depth
        // is tracked so a comma inside a nested generic argument (e.g.
        // Dictionary<string, List<Widget>>) doesn't split that argument in two.
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
                foreach (string name in ExtractCandidateTypeNames(inner[segmentStart..i]))
                {
                    yield return name;
                }

                segmentStart = i + 1;
            }
        }

        foreach (string name in ExtractCandidateTypeNames(inner[segmentStart..]))
        {
            yield return name;
        }
    }
}
