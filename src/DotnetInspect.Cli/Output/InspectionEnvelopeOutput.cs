using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal sealed record InspectionEnvelopeJsonContract<TContent>(
    string ResultKind,
    int SchemaVersion,
    JsonTypeInfo<TContent> ContentTypeInfo);

internal static class InspectionEnvelopeOutput
{
    internal static bool TryWrite<TContent>(
        InspectionEnvelope<TContent> envelope,
        InspectionEnvelopeJsonContract<TContent> contract,
        bool includeEnvelope,
        bool compactJson = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(contract);

        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            using var writer = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions
                {
                    Indented = !compactJson,
                });

            if (includeEnvelope)
                WriteEnvelope(writer, envelope, contract);
            else
                JsonSerializer.Serialize(
                    writer,
                    envelope.Content,
                    contract.ContentTypeInfo);

            writer.Flush();
        }
        catch (Exception exception)
            when (exception is JsonException or NotSupportedException)
        {
            CommandError.Write(exception);
            return false;
        }

        Console.Out.Write(
            string.Concat(
                Encoding.UTF8.GetString(buffer.WrittenSpan),
                Environment.NewLine));
        return true;
    }

    private static void WriteEnvelope<TContent>(
        Utf8JsonWriter writer,
        InspectionEnvelope<TContent> envelope,
        InspectionEnvelopeJsonContract<TContent> contract)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", contract.SchemaVersion);
        writer.WriteString("result_kind", contract.ResultKind);

        writer.WritePropertyName("content");
        JsonSerializer.Serialize(
            writer,
            envelope.Content,
            contract.ContentTypeInfo);

        writer.WritePropertyName("share");
        JsonSerializer.Serialize(
            writer,
            envelope.Share,
            InspectionEnvelopeJsonContext.Default.InspectionShare);

        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (InspectionDiagnostic diagnostic in envelope.Diagnostics)
        {
            JsonSerializer.Serialize(
                writer,
                diagnostic,
                InspectionEnvelopeJsonContext.Default.InspectionDiagnostic);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
