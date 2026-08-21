using System.Text;
using System.Text.RegularExpressions;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

static partial class DtsEmitter
{
    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TsIdentifierRegex();

    public static string Emit(
        ILInspector.JsExportSurface.JsExportSurface surface,
        TsBindGenDiagnostics? diagnostics = null)
    {
        var knownTypeNames = new HashSet<string>(
            surface.Records.Select(r => r.Name).Concat(surface.Enums.Select(e => e.Name)),
            StringComparer.Ordinal);

        var sb = new StringBuilder();

        foreach (ApiType enumType in surface.Enums.OrderBy(e => e.Name, StringComparer.Ordinal))
            EmitEnum(sb, enumType);

        foreach (ApiType record in surface.Records.OrderBy(r => r.Name, StringComparer.Ordinal))
            EmitRecord(sb, record, knownTypeNames, diagnostics);

        sb.Append(
            "export declare function initializeEngine(onStatus?: (status: string) => void): Promise<unknown>;\n");

        foreach (JsExportFunction function in surface.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
            EmitFunction(sb, function, knownTypeNames, diagnostics);

        return sb.ToString();
    }

    static void EmitEnum(StringBuilder sb, ApiType enumType)
    {
        if (!enumType.HasJsonStringEnumConverter)
        {
            sb.Append("export type ").Append(enumType.Name).Append(" = number;\n\n");
            return;
        }

        if (enumType.IsFlagsEnum)
        {
            sb.Append("export type ").Append(enumType.Name).Append(" = string;\n\n");
            return;
        }

        IEnumerable<string> memberNames = enumType.Members.Where(m => m.Kind == "field" && m.IsConst).Select(m => m.Name);
        string union = string.Join(" | ", memberNames.Select(n => $"\"{n}\""));
        sb.Append("export type ").Append(enumType.Name).Append(" = ").Append(union).Append(";\n\n");
    }

    static void EmitRecord(
        StringBuilder sb,
        ApiType record,
        IReadOnlySet<string> knownTypeNames,
        TsBindGenDiagnostics? diagnostics)
    {
        JsonWireNamingPolicy namingPolicy = record.JsonPropertyNamingPolicy ?? JsonWireNamingPolicy.None;
        if (namingPolicy == JsonWireNamingPolicy.Unsupported)
        {
            diagnostics?.ReportUnmappedType(
                $"{record.Name} JsonSerializerContext.PropertyNamingPolicy",
                "unsupported JsonKnownNamingPolicy");
            namingPolicy = JsonWireNamingPolicy.None;
        }

        sb.Append("export interface ").Append(record.Name).Append(" {\n");

        foreach (ApiMember member in record.Members)
        {
            if (member.Kind != "property"
                || member.IsCompilerGenerated
                || member.HasJsonIgnore
                || (member.Accessibility is not null && !member.HasJsonInclude))
            {
                continue;
            }

            string resolvedName = member.JsonPropertyName ?? ApplyNamingPolicy(member.Name, namingPolicy);
            string tsName = FormatPropertyKey(resolvedName);
            string propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType ?? "unknown";
            string tsType = TsTypeMapper.MapParameterType(propertyType, knownTypeNames, diagnostics, $"{record.Name}.{member.Name}");
            sb.Append("  ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void EmitFunction(
        StringBuilder sb,
        JsExportFunction function,
        IReadOnlySet<string> knownTypeNames,
        TsBindGenDiagnostics? diagnostics)
    {
        string returnType = function.ReturnWireType is { } returnWireType
            ? TsTypeMapper.MapReturnEnvelope(function.ReturnType, returnWireType, knownTypeNames, diagnostics, $"{function.Name} return")
            : TsTypeMapper.MapReturnType(function.ReturnType, knownTypeNames, diagnostics, $"{function.Name} return");

        var parameters = function.Parameters.Select(p =>
            $"{CamelCase.FromPascalCase(p.Name)}: {TsTypeMapper.MapParameterType(p.Type, knownTypeNames, diagnostics, $"{function.Name}.{p.Name}")}");

        sb.Append("export declare function ")
          .Append(CamelCase.FromPascalCase(function.Name))
          .Append('(')
          .Append(string.Join(", ", parameters))
          .Append("): ")
          .Append(returnType)
          .Append(";\n");
    }

    static string ApplyNamingPolicy(string name, JsonWireNamingPolicy namingPolicy) => namingPolicy switch
    {
        JsonWireNamingPolicy.None => name,
        JsonWireNamingPolicy.CamelCase => CamelCase.FromPascalCase(name),
        JsonWireNamingPolicy.SnakeCaseLower => JsonNamingPolicies.SnakeCaseLower(name),
        JsonWireNamingPolicy.SnakeCaseUpper => JsonNamingPolicies.SnakeCaseUpper(name),
        JsonWireNamingPolicy.KebabCaseLower => JsonNamingPolicies.KebabCaseLower(name),
        JsonWireNamingPolicy.KebabCaseUpper => JsonNamingPolicies.KebabCaseUpper(name),
        _ => name,
    };

    static string FormatPropertyKey(string name) =>
        TsIdentifierRegex().IsMatch(name) ? name : $"\"{EscapeString(name)}\"";

    static string EscapeString(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
