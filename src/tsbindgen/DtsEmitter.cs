using System.Text;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace tsbindgen;

/// <summary>
/// Projects a <see cref="JsExportSurface.JsExportSurface"/> into <c>.d.ts</c> text. Purely
/// mechanical: all type-mapping and naming-policy decisions are delegated to
/// <see cref="TsTypeMapper"/> and <see cref="CamelCase"/>; this type only handles layout/output.
/// </summary>
static class DtsEmitter
{
    public static string Emit(
        ILInspector.JsExportSurface.JsExportSurface surface,
        TsBindGenDiagnostics? diagnostics = null)
    {
        var knownTypeNames = new HashSet<string>(
            surface.Records.Select(r => r.Name).Concat(surface.Enums.Select(e => e.Name)),
            StringComparer.Ordinal);

        JsonWireNamingPolicy namingPolicy = ResolveNamingPolicy(surface);
        if (namingPolicy == JsonWireNamingPolicy.Unsupported)
        {
            diagnostics?.ReportUnmappedType(
                "JsonSerializerContext.PropertyNamingPolicy",
                "unsupported JsonKnownNamingPolicy");
        }

        var sb = new StringBuilder();

        foreach (ApiType enumType in surface.Enums.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            EmitEnum(sb, enumType);
        }

        foreach (ApiType record in surface.Records.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            EmitRecord(sb, record, knownTypeNames, namingPolicy, diagnostics);
        }

        foreach (JsExportFunction function in surface.Functions.OrderBy(
            f => f.Name, StringComparer.Ordinal))
        {
            EmitFunction(sb, function, knownTypeNames, diagnostics);
        }

        return sb.ToString();
    }

    static JsonWireNamingPolicy ResolveNamingPolicy(ILInspector.JsExportSurface.JsExportSurface surface)
    {
        JsonWireNamingPolicy? resolved = null;
        foreach (ApiType record in surface.Records)
        {
            if (record.JsonPropertyNamingPolicy is null)
            {
                continue;
            }

            if (resolved is null)
            {
                resolved = record.JsonPropertyNamingPolicy.Value;
                continue;
            }

            if (resolved != record.JsonPropertyNamingPolicy.Value)
            {
                return JsonWireNamingPolicy.Unsupported;
            }
        }

        return resolved ?? JsonWireNamingPolicy.None;
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

        IEnumerable<string> memberNames = enumType.Members
            .Where(m => m.Kind == "field" && m.IsConst)
            .Select(m => m.Name);
        string union = string.Join(" | ", memberNames.Select(n => $"\"{n}\""));
        sb.Append("export type ").Append(enumType.Name).Append(" = ").Append(union).Append(";\n\n");
    }

    static void EmitRecord(
        StringBuilder sb,
        ApiType record,
        IReadOnlySet<string> knownTypeNames,
        JsonWireNamingPolicy namingPolicy,
        TsBindGenDiagnostics? diagnostics)
    {
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

            string tsName = member.JsonPropertyName
                ?? ApplyNamingPolicy(member.Name, namingPolicy);
            string propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType ?? "unknown";
            string tsType = TsTypeMapper.MapParameterType(
                propertyType,
                knownTypeNames,
                diagnostics,
                $"{record.Name}.{member.Name}");
            sb.Append("  ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void EmitFunction(
        StringBuilder sb, JsExportFunction function, IReadOnlySet<string> knownTypeNames, TsBindGenDiagnostics? diagnostics)
    {
        string tsName = CamelCase.FromPascalCase(function.Name);
        string returnType = function.ReturnWireType is { } returnWireType
            ? TsTypeMapper.MapReturnEnvelope(
                function.ReturnType,
                returnWireType,
                knownTypeNames,
                diagnostics,
                $"{function.Name} return")
            : TsTypeMapper.MapReturnType(function.ReturnType, knownTypeNames, diagnostics, $"{function.Name} return");

        var parameters = function.Parameters.Select(p =>
            $"{CamelCase.FromPascalCase(p.Name)}: {TsTypeMapper.MapParameterType(p.Type, knownTypeNames, diagnostics, $"{function.Name}.{p.Name}")}");

        sb.Append("export declare function ")
          .Append(tsName)
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
}
