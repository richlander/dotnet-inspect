using System.Text.Json;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Shared JSON lowering for Analysis execution receipts and diagnostics.
/// </summary>
public static class AnalysisExecutionJson
{
    public static void WriteReceipt(
        Utf8JsonWriter writer,
        LibraryBodyAnalysisReceipt receipt)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceName", receipt.SourceName);
        writer.WritePropertyName("moduleIdentity");
        WriteModuleIdentity(writer, receipt.ModuleIdentity);
        writer.WriteString("features", receipt.Features.ToString());
        writer.WriteNumber("featureMask", (int)receipt.Features);
        writer.WriteBoolean(
            "hasFullMethodEvidenceScope",
            receipt.HasFullMethodEvidenceScope);
        writer.WritePropertyName("diagnostics");
        WriteDiagnostics(writer, receipt.Diagnostics);
        writer.WriteEndObject();
    }

    public static void WriteAssemblyIdentity(
        Utf8JsonWriter writer,
        AssemblyReferenceIdentity identity)
    {
        writer.WriteStartObject();
        writer.WriteString("name", identity.Name);
        WriteString(writer, "version", identity.Version?.ToString());
        WriteString(writer, "culture", identity.Culture);
        WriteString(
            writer,
            "publicKeyToken",
            identity.PublicKeyToken);
        writer.WriteEndObject();
    }

    public static void WriteDiagnostics(
        Utf8JsonWriter writer,
        IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        writer.WriteStartArray();
        foreach (AnalysisDiagnostic diagnostic in diagnostics)
            WriteDiagnostic(writer, diagnostic);
        writer.WriteEndArray();
    }

    private static void WriteModuleIdentity(
        Utf8JsonWriter writer,
        LibraryBodyModuleIdentity identity)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("assemblyIdentity");
        if (identity.AssemblyIdentity is { } assembly)
            WriteAssemblyIdentity(writer, assembly);
        else
            writer.WriteNullValue();
        writer.WriteString(
            "moduleVersionId",
            identity.ModuleVersionId);
        writer.WriteEndObject();
    }

    public static void WriteDiagnostic(
        Utf8JsonWriter writer,
        AnalysisDiagnostic diagnostic)
    {
        writer.WriteStartObject();
        writer.WriteNumber("methodToken", diagnostic.MethodToken);
        writer.WriteString("method", diagnostic.Method);
        writer.WriteString("message", diagnostic.Message);
        WriteNumber(
            writer,
            "sourceMethodToken",
            diagnostic.SourceMethodToken);
        writer.WritePropertyName("declaringType");
        if (diagnostic.DeclaringType is { } declaringType)
            AnalysisIdentityJson.WriteType(writer, declaringType);
        else
            writer.WriteNullValue();
        writer.WritePropertyName("sourceDeclaringType");
        if (diagnostic.SourceDeclaringType is { } sourceDeclaringType)
            AnalysisIdentityJson.WriteType(writer, sourceDeclaringType);
        else
            writer.WriteNullValue();
        writer.WriteEndObject();
    }

    private static void WriteString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value);
    }

    private static void WriteNumber(
        Utf8JsonWriter writer,
        string propertyName,
        int? value)
    {
        if (value is { } number)
            writer.WriteNumber(propertyName, number);
        else
            writer.WriteNull(propertyName);
    }
}
