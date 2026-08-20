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
        var knownTypeNames = new HashSet<string>(
            surface.Records.Select(r => r.Name).Concat(surface.Enums.Select(e => e.Name)),
            StringComparer.Ordinal);

        var sb = new StringBuilder();

        foreach (ApiType enumType in surface.Enums.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            EmitEnum(sb, enumType);
        }

        foreach (ApiType record in surface.Records.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            EmitRecord(sb, record, knownTypeNames);
        }

        foreach (JsExportFunction function in surface.Functions.OrderBy(
            f => f.Name, StringComparer.Ordinal))
        {
            EmitFunction(sb, function, knownTypeNames);
        }

        return sb.ToString();
    }

    static void EmitEnum(StringBuilder sb, ApiType enumType)
    {
        // STJ's JsonStringEnumConverter serializes an enum member as its declared name (a plain
        // string), not an object — so the wire shape is a string-literal union, not an interface.
        // The enum's storage slot (`value__`, its underlying-type instance field) is captured
        // alongside its named values as Kind == "field" but is not itself a member value — filter
        // to IsConst, which is only set for the literal enum members.
        IEnumerable<string> memberNames = enumType.Members
            .Where(m => m.Kind == "field" && m.IsConst)
            .Select(m => m.Name);
        string union = string.Join(" | ", memberNames.Select(n => $"\"{n}\""));
        sb.Append("export type ").Append(enumType.Name).Append(" = ").Append(union).Append(";\n\n");
    }

    static void EmitRecord(StringBuilder sb, ApiType record, IReadOnlySet<string> knownTypeNames)
    {
        sb.Append("export interface ").Append(record.Name).Append(" {\n");

        foreach (ApiMember member in record.Members)
        {
            if (member.Kind != "property"
                // Compiler-synthesized record infrastructure (e.g. a positional record's
                // EqualityContract getter) is never public, so a non-null Accessibility here means
                // this is not one of the type's real, public data properties — never a wire-shape
                // concern, only reachable at all when includeAll surfaces non-public members
                // alongside the type's public ones.
                || member.Accessibility is not null)
            {
                continue;
            }

            string tsName = CamelCase.FromPascalCase(member.Name);
            string propertyType = member.SignatureModel?.ReturnType ?? member.ReturnType ?? "unknown";
            string tsType = TsTypeMapper.MapParameterType(propertyType, knownTypeNames);
            sb.Append("  ").Append(tsName).Append(": ").Append(tsType).Append(";\n");
        }

        sb.Append("}\n\n");
    }

    static void EmitFunction(
        StringBuilder sb, JsExportFunction function, IReadOnlySet<string> knownTypeNames)
    {
        string tsName = CamelCase.FromPascalCase(function.Name);
        string returnType = function.ReturnWireType is { } returnWireType
            ? TsTypeMapper.MapReturnEnvelope(function.ReturnType, returnWireType, knownTypeNames)
            : TsTypeMapper.MapReturnType(function.ReturnType, knownTypeNames);

        // ParameterWireTypes is not consumed here: JsonWireContractResolver does not attribute a
        // resolved DTO to a specific parameter position (documented residual gap), so applying it
        // to "the" string parameter would silently guess wrong whenever an export has more than
        // one string-typed parameter (e.g. RenameWidget's widgetJson and newName). Parameters
        // keep their raw signature-text mapping until that attribution exists.
        var parameters = function.Parameters.Select(p =>
            $"{CamelCase.FromPascalCase(p.Name)}: {TsTypeMapper.MapParameterType(p.Type, knownTypeNames)}");

        sb.Append("export declare function ")
          .Append(tsName)
          .Append('(')
          .Append(string.Join(", ", parameters))
          .Append("): ")
          .Append(returnType)
          .Append(";\n");
    }
}
