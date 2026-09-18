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

internal sealed record EvidenceInspectionEnvelopeJsonContract<
    TContent,
    TEvidence>(
    InspectionEnvelopeJsonContract<TContent> Inspection,
    JsonTypeInfo<TEvidence> EvidenceTypeInfo);

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

    internal static bool TryWriteEvidence<TContent, TEvidence>(
        EvidenceInspectionEnvelope<TContent, TEvidence> envelope,
        EvidenceInspectionEnvelopeJsonContract<TContent, TEvidence> contract,
        string path,
        bool compactJson = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);

        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            using var writer = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions
                {
                    Indented = !compactJson,
                });

            writer.WriteStartObject();
            writer.WriteNumber(
                "schema_version",
                contract.Inspection.SchemaVersion);
            writer.WriteString(
                "result_kind",
                contract.Inspection.ResultKind);
            WriteInspectionMembers(
                writer,
                envelope.Inspection,
                contract.Inspection);
            writer.WritePropertyName("evidence");
            JsonSerializer.Serialize(
                writer,
                envelope.Evidence,
                contract.EvidenceTypeInfo);
            writer.WriteEndObject();
            writer.Flush();
        }
        catch (Exception exception)
            when (exception is JsonException or NotSupportedException)
        {
            CommandError.Write(
                $"The evidence envelope for '{path}' could not be serialized: "
                    + exception.Message);
            return false;
        }

        string directory = Path.GetDirectoryName(path)!;
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] newline = Encoding.UTF8.GetBytes(Environment.NewLine);
            byte[] payload = GC.AllocateUninitializedArray<byte>(
                buffer.WrittenCount + newline.Length);
            buffer.WrittenSpan.CopyTo(payload);
            newline.CopyTo(payload.AsSpan(buffer.WrittenCount));
            File.WriteAllBytes(temporaryPath, payload);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or System.Security.SecurityException)
        {
            CommandError.Write(
                $"The evidence envelope could not be published to '{path}': "
                    + exception.Message);
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                CommandError.WriteWarning(
                    $"The temporary evidence envelope file '{temporaryPath}' could not be removed: "
                        + exception.Message);
            }
        }

        CommandError.WriteLine($"Evidence envelope: {path}");
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
        WriteInspectionMembers(writer, envelope, contract);
        writer.WriteEndObject();
    }

    private static void WriteInspectionMembers<TContent>(
        Utf8JsonWriter writer,
        InspectionEnvelope<TContent> envelope,
        InspectionEnvelopeJsonContract<TContent> contract)
    {
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
    }
}
