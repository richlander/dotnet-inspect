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
    public static string Emit(ILInspector.JsExportSurface.JsExportSurface surface)
    {
        var recordNames = new HashSet<string>(
            surface.Records.Select(r => r.Name),
            StringComparer.Ordinal);

        var sb = new StringBuilder();

        foreach (ApiType record in surface.Records.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            EmitRecord(sb, record, recordNames);
        }

        foreach (JsExportFunction function in surface.Functions.OrderBy(
            f => f.Name, StringComparer.Ordinal))
        {
            EmitFunction(sb, function, recordNames);
        }

        return sb.ToString();
    }

    static void EmitRecord(StringBuilder sb, ApiType record, IReadOnlySet<string> recordNames)
    {
        sb.Append("export interface ").Append(record.Name).Append(" {\n");

        foreach (ApiMember member in record.Members)
        {
            if (member.Kind != "property")
            {
                continue;
            }

            string tsName = CamelCase.FromPascalCase(member.Name);
            string propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType ?? "unknown";
            string tsType = TsTypeMapper.MapParameterType(propertyType, recordNames);
            sb.Append("  ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void EmitFunction(
        StringBuilder sb, JsExportFunction function, IReadOnlySet<string> recordNames)
    {
        string tsName = CamelCase.FromPascalCase(function.Name);
        string returnType = function.ReturnWireType is { } returnWireType
            ? TsTypeMapper.MapReturnEnvelope(function.ReturnType, returnWireType, recordNames)
            : TsTypeMapper.MapReturnType(function.ReturnType, recordNames);

        // ParameterWireTypes is not consumed here: JsonWireContractResolver does not attribute a
        // resolved DTO to a specific parameter position (documented residual gap), so applying it
        // to "the" string parameter would silently guess wrong whenever an export has more than
        // one string-typed parameter (e.g. RenameWidget's widgetJson and newName). Parameters
        // keep their raw signature-text mapping until that attribution exists.
        var parameters = function.Parameters.Select(p =>
            $"{CamelCase.FromPascalCase(p.Name)}: {TsTypeMapper.MapParameterType(p.Type, recordNames)}");

        sb.Append("export declare function ")
          .Append(tsName)
          .Append('(')
          .Append(string.Join(", ", parameters))
          .Append("): ")
          .Append(returnType)
          .Append(";\n");
    }
}
