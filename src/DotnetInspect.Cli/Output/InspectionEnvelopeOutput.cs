using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal sealed record InspectionEnvelopeJsonContract<TContent>
{
    internal InspectionEnvelopeJsonContract(
        string resultKind,
        int schemaVersion,
        JsonTypeInfo<TContent> contentTypeInfo)
    {
        ResultKind = resultKind;
        SchemaVersion = schemaVersion;
        ContentTypeInfo = contentTypeInfo;
    }

    internal InspectionEnvelopeJsonContract(
        string resultKind,
        int schemaVersion,
        Action<Utf8JsonWriter, TContent> writeContent)
    {
        ResultKind = resultKind;
        SchemaVersion = schemaVersion;
        WriteContent = writeContent;
    }

    internal string ResultKind { get; }
    internal int SchemaVersion { get; }
    internal JsonTypeInfo<TContent>? ContentTypeInfo { get; }
    internal Action<Utf8JsonWriter, TContent>? WriteContent { get; }

    internal void Write(Utf8JsonWriter writer, TContent content)
    {
        if (WriteContent is not null)
        {
            WriteContent(writer, content);
            return;
        }

        JsonSerializer.Serialize(writer, content, ContentTypeInfo!);
    }
}

internal static class InspectionEnvelopeOutput
{
    internal static bool TryWrite<TContent>(
        InspectionEnvelope<TContent> envelope,
        InspectionEnvelopeJsonContract<TContent> contract,
        bool includeEnvelope,
        bool compactJson = false,
        string? outputPath = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(contract);

        if (!TrySerialize(
                envelope,
                contract,
                includeEnvelope,
                compactJson,
                out byte[] payload,
                out Exception? exception))
        {
            CommandError.Write(exception!);
            return false;
        }

        OutputDestination.Write(
            outputPath,
            rowWindow: null,
            writer =>
            {
                writer.Write(Encoding.UTF8.GetString(
                    payload.AsSpan(0, payload.Length - 1)));
                writer.WriteLine();
            });
        return true;
    }

    internal static bool TrySerializeEvidence<TContent, TEvidence>(
        EvidenceInspectionEnvelope<TContent, TEvidence> envelope,
        InspectionEnvelopeJsonContract<TContent> contract,
        JsonTypeInfo<TEvidence> evidenceTypeInfo,
        bool compactJson,
        out byte[] payload,
        out Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidenceTypeInfo);

        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            using var writer = CreateWriter(buffer, compactJson);
            writer.WriteStartObject();
            WriteEnvelopeProperties(
                writer,
                envelope.Inspection,
                contract);
            writer.WritePropertyName("evidence");
            JsonSerializer.Serialize(
                writer,
                envelope.Evidence,
                evidenceTypeInfo);
            writer.WriteEndObject();
            writer.Flush();

            payload = CompletePayload(buffer);
            exception = null;
            return true;
        }
        catch (Exception caught)
            when (caught is JsonException or NotSupportedException)
        {
            payload = [];
            exception = caught;
            return false;
        }
    }

    private static bool TrySerialize<TContent>(
        InspectionEnvelope<TContent> envelope,
        InspectionEnvelopeJsonContract<TContent> contract,
        bool includeEnvelope,
        bool compactJson,
        out byte[] payload,
        out Exception? exception)
    {
        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            using var writer = CreateWriter(buffer, compactJson);
            if (includeEnvelope)
                WriteEnvelope(writer, envelope, contract);
            else
                contract.Write(writer, envelope.Content);

            writer.Flush();
            payload = CompletePayload(buffer);
            exception = null;
            return true;
        }
        catch (Exception caught)
            when (caught is JsonException or NotSupportedException)
        {
            payload = [];
            exception = caught;
            return false;
        }
    }

    private static Utf8JsonWriter CreateWriter(
        IBufferWriter<byte> buffer,
        bool compactJson) =>
        new(
            buffer,
            new JsonWriterOptions
            {
                Indented = !compactJson,
            });

    private static byte[] CompletePayload(ArrayBufferWriter<byte> buffer)
    {
        byte[] payload = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(payload);
        payload[^1] = (byte)'\n';
        return payload;
    }

    private static void WriteEnvelope<TContent>(
        Utf8JsonWriter writer,
        InspectionEnvelope<TContent> envelope,
        InspectionEnvelopeJsonContract<TContent> contract)
    {
        writer.WriteStartObject();
        WriteEnvelopeProperties(writer, envelope, contract);
        writer.WriteEndObject();
    }

    private static void WriteEnvelopeProperties<TContent>(
        Utf8JsonWriter writer,
        InspectionEnvelope<TContent> envelope,
        InspectionEnvelopeJsonContract<TContent> contract)
    {
        writer.WriteNumber("schema_version", contract.SchemaVersion);
        writer.WriteString("result_kind", contract.ResultKind);
        writer.WriteString(
            "content_kind",
            envelope.ContentKind switch
            {
                InspectionContentKind.Result => "result",
                InspectionContentKind.Document => "document",
                InspectionContentKind.Outcome => "outcome",
                _ => throw new InvalidOperationException(
                    "Unknown inspection content kind."),
            });

        writer.WritePropertyName("content");
        contract.Write(writer, envelope.Content);

        writer.WritePropertyName("portable_projection");
        JsonSerializer.Serialize(
            writer,
            envelope.PortableProjection,
            InspectionEnvelopeJsonContext.Default
                .InspectionPortableProjection);

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
    }
}
