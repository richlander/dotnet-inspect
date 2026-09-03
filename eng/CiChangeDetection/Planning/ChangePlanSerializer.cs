using System.Text;
using System.Text.Json;

namespace CiChangeDetection.Planning;

/// <summary>
/// The plan's canonical JSON boundary. Serialization is hand-written so the
/// property order, spelling, and byte ceiling are part of the contract rather
/// than a serializer default, and deserialization is strict so a malformed,
/// duplicated, unknown, or mistyped field is rejected instead of defaulted.
/// </summary>
internal static class ChangePlanSerializer
{
    private static readonly string[] ValidationNames =
    [
        "test",
        "csharpDiffSmoke",
        "decompilerGates",
        "markdownlint",
        "ilDiffSmoke",
        "ilRoundTrip",
        "pack",
        "buildNet10",
        "inspectWeb",
        "skillGate",
        "tla",
        "codeqlActions",
        "codeqlCSharp",
        "codeqlJavaScript",
    ];

    /// <summary>
    /// Serializes a plan to compact UTF-8 containing only ASCII, with no
    /// newline and no path bytes. The CLI adds the single trailing newline.
    /// </summary>
    /// <param name="plan">The plan to serialize.</param>
    /// <returns>The serialized plan bytes.</returns>
    internal static byte[] Serialize(ChangePlan plan)
    {
        StringBuilder builder = new();
        builder.Append("{\"schemaVersion\":").Append(plan.SchemaVersion);
        builder.Append(",\"status\":\"").Append(plan.Status).Append('"');
        builder.Append(",\"provenance\":{\"kind\":\"")
            .Append(CandidateProvenance.KindName(plan.Provenance.Kind))
            .Append("\",\"baseObjectId\":\"")
            .Append(plan.Provenance.BaseObjectId)
            .Append("\",\"candidateObjectId\":\"")
            .Append(plan.Provenance.CandidateObjectId)
            .Append("\"}");
        builder.Append(",\"input\":{\"recordCount\":")
            .Append(plan.Input.RecordCount)
            .Append(",\"sha256\":\"")
            .Append(plan.Input.Sha256)
            .Append("\"}");

        builder.Append(",\"validations\":{");
        bool[] values = ValidationValues(plan.Validations);
        for (int index = 0; index < ValidationNames.Length; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(ValidationNames[index]).Append("\":")
                .Append(values[index] ? "true" : "false");
        }

        builder.Append('}');

        builder.Append(",\"scopes\":{");
        for (int index = 0; index < plan.Scopes.Count; index++)
        {
            PlanScopeDescriptor scope = plan.Scopes[index];
            if (index != 0)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(scope.Scope)
                .Append("\":{\"artifact\":\"").Append(scope.Artifact)
                .Append("\",\"framing\":\"").Append(scope.Framing)
                .Append("\",\"recordCount\":").Append(scope.RecordCount)
                .Append(",\"sha256\":\"").Append(scope.Sha256)
                .Append("\"}");
        }

        builder.Append('}');

        builder.Append(",\"diagnostics\":[");
        for (int index = 0; index < plan.Diagnostics.Count; index++)
        {
            if (index != 0)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(plan.Diagnostics[index]).Append('"');
        }

        builder.Append("]}");

        string text = builder.ToString();
        foreach (char character in text)
        {
            if (character is < ' ' or > '~')
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.PlanSerialization,
                    "the serialized plan contains non-ASCII content");
            }
        }

        byte[] serialized = Encoding.UTF8.GetBytes(text);
        if (serialized.Length > ChangePlan.MaximumSerializedBytes)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanOverflow,
                "the serialized plan exceeded its byte ceiling");
        }

        return serialized;
    }

    /// <summary>
    /// Parses and validates a serialized plan, rejecting duplicate, unknown,
    /// missing, and mistyped fields as well as unsupported versions, statuses,
    /// invariants, digests, and object IDs.
    /// </summary>
    /// <param name="serialized">The serialized plan bytes.</param>
    /// <returns>The validated plan.</returns>
    internal static ChangePlan Deserialize(ReadOnlySpan<byte> serialized)
    {
        if (serialized.Length > ChangePlan.MaximumSerializedBytes)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanOverflow,
                "the serialized plan exceeded its byte ceiling");
        }

        foreach (byte value in serialized)
        {
            if (value is < 0x20 or > 0x7e)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.PlanSerialization,
                    "the serialized plan is not printable ASCII");
            }
        }

        JsonDocument document;
        long consumed;
        try
        {
            Utf8JsonReader reader = new(
                serialized,
                new JsonReaderOptions
                {
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                });
            document = JsonDocument.ParseValue(ref reader);
            consumed = reader.BytesConsumed;
        }
        catch (JsonException)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                "the serialized plan is not well-formed JSON");
        }

        using (document)
        {
            if (consumed != serialized.Length)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.PlanSerialization,
                    "the serialized plan has trailing content");
            }

            RejectDuplicateNames(serialized);
            JsonElement root = RequireObject(document.RootElement, "plan");
            RequireExactMembers(
                root,
                "plan",
                [
                    "schemaVersion",
                    "status",
                    "provenance",
                    "input",
                    "validations",
                    "scopes",
                    "diagnostics",
                ]);

            int schemaVersion = RequireInt32(root, "schemaVersion");
            string status = RequireString(root, "status");
            JsonElement provenanceElement =
                RequireObject(root.GetProperty("provenance"), "provenance");
            RequireExactMembers(
                provenanceElement,
                "provenance",
                ["kind", "baseObjectId", "candidateObjectId"]);
            CandidateProvenance provenance = CandidateProvenance.Create(
                CandidateProvenance.ParseKindName(
                    RequireString(provenanceElement, "kind")),
                RequireString(provenanceElement, "baseObjectId"),
                RequireString(provenanceElement, "candidateObjectId"));

            JsonElement inputElement =
                RequireObject(root.GetProperty("input"), "input");
            RequireExactMembers(
                inputElement,
                "input",
                ["recordCount", "sha256"]);
            PlanInputDescriptor input = new(
                RequireInt32(inputElement, "recordCount"),
                RequireString(inputElement, "sha256"));

            JsonElement validationsElement =
                RequireObject(root.GetProperty("validations"), "validations");
            RequireExactMembers(
                validationsElement,
                "validations",
                ValidationNames);
            bool[] values = new bool[ValidationNames.Length];
            for (int index = 0; index < ValidationNames.Length; index++)
            {
                values[index] =
                    RequireBoolean(validationsElement, ValidationNames[index]);
            }

            ValidationSelections validations = new(
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5],
                values[6],
                values[7],
                values[8],
                values[9],
                values[10],
                values[11],
                values[12],
                values[13]);

            JsonElement scopesElement =
                RequireObject(root.GetProperty("scopes"), "scopes");
            List<PlanScopeDescriptor> scopes = [];
            foreach (JsonProperty property in scopesElement.EnumerateObject())
            {
                JsonElement scopeElement =
                    RequireObject(property.Value, "scope");
                RequireExactMembers(
                    scopeElement,
                    "scope",
                    ["artifact", "framing", "recordCount", "sha256"]);
                scopes.Add(new PlanScopeDescriptor(
                    property.Name,
                    RequireString(scopeElement, "artifact"),
                    RequireString(scopeElement, "framing"),
                    RequireInt32(scopeElement, "recordCount"),
                    RequireString(scopeElement, "sha256")));
            }

            JsonElement diagnosticsElement = root.GetProperty("diagnostics");
            if (diagnosticsElement.ValueKind != JsonValueKind.Array)
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.PlanSerialization,
                    "diagnostics is not an array");
            }

            List<string> diagnostics = [];
            foreach (JsonElement diagnostic in
                diagnosticsElement.EnumerateArray())
            {
                if (diagnostic.ValueKind != JsonValueKind.String)
                {
                    throw new PlanRefusalException(
                        PlanRefusalCategory.PlanSerialization,
                        "a diagnostic is not a string");
                }

                diagnostics.Add(diagnostic.GetString()!);
            }

            ChangePlan plan = new(
                schemaVersion,
                status,
                provenance,
                input,
                validations,
                scopes,
                diagnostics);
            if (!Serialize(plan).AsSpan().SequenceEqual(serialized))
            {
                throw new PlanRefusalException(
                    PlanRefusalCategory.PlanSerialization,
                    "the serialized plan is not canonical");
            }

            return plan;
        }
    }

    private static bool[] ValidationValues(ValidationSelections validations) =>
    [
        validations.Test,
        validations.CSharpDiffSmoke,
        validations.DecompilerGates,
        validations.Markdownlint,
        validations.IlDiffSmoke,
        validations.IlRoundTrip,
        validations.Pack,
        validations.BuildNet10,
        validations.InspectWeb,
        validations.SkillGate,
        validations.Tla,
        validations.CodeqlActions,
        validations.CodeqlCSharp,
        validations.CodeqlJavaScript,
    ];

    /// <summary>
    /// Rejects duplicate object member names anywhere in the document.
    /// <see cref="JsonDocument"/> keeps the last writer, which would let a
    /// duplicated field silently override a validated one.
    /// </summary>
    /// <param name="serialized">The serialized plan bytes.</param>
    private static void RejectDuplicateNames(ReadOnlySpan<byte> serialized)
    {
        Utf8JsonReader reader = new(serialized);
        Stack<HashSet<string>> scopes = new();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    _ = scopes.Pop();
                    break;
                case JsonTokenType.PropertyName
                    when !scopes.Peek().Add(reader.GetString()!):
                    throw new PlanRefusalException(
                        PlanRefusalCategory.PlanSerialization,
                        "the serialized plan contains a duplicate member");
                default:
                    break;
            }
        }
    }

    private static JsonElement RequireObject(JsonElement element, string role)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                $"{role} is not an object");
        }

        return element;
    }

    private static void RequireExactMembers(
        JsonElement element,
        string role,
        IReadOnlyList<string> names)
    {
        HashSet<string> actual = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            _ = actual.Add(property.Name);
        }

        if (!actual.SetEquals(names))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                $"{role} does not carry exactly its defined members");
        }
    }

    private static string RequireString(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                $"{name} is not a string");
        }

        return value.GetString()!;
    }

    private static bool RequireBoolean(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                $"{name} is not a boolean"),
        };
    }

    private static int RequireInt32(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int parsed))
        {
            throw new PlanRefusalException(
                PlanRefusalCategory.PlanSerialization,
                $"{name} is not a 32-bit integer");
        }

        return parsed;
    }
}
