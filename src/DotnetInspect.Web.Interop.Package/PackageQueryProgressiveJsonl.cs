using System.Buffers;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DotnetInspector.InspectionContracts;
using DotnetInspector.Sections;
using TsJsExport;

namespace DotnetInspect.Web.Interop.Package;

internal sealed class PackageQueryProgressiveJsonlRowWriter
    : IProgressiveJsonlRecordWriter<BrowserPackageQueryRow>
{
    internal static PackageQueryProgressiveJsonlRowWriter Instance { get; } =
        new();

    public ProgressiveJsonlRecordEncodingOutcome Encode(
        BrowserPackageQueryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartArray();
            writer.WriteStringValue(row.PackageId);
            writer.WriteStringValue(row.Version);
            Serialize(writer, row.Tier);
            Serialize(writer, row.Answers);
            Serialize(writer, row.Evidence);
            if (row.TotalDownloads is long totalDownloads)
                writer.WriteNumberValue(totalDownloads);
            else
                writer.WriteNullValue();
            if (row.Verified is bool verified)
                writer.WriteBooleanValue(verified);
            else
                writer.WriteNullValue();
            writer.WriteStringValue(row.Producer);
            writer.WriteStringValue(row.Description);
            writer.WriteStringValue(row.RootRequest);
            Serialize(writer, row.Owners);
            Serialize(writer, row.Manifest);
            Serialize(writer, row.EcosystemAdmission);
            writer.WriteEndArray();
            writer.Flush();
            return new ProgressiveJsonlRecordEncodingOutcome.Encoded(
                Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
        catch (JsonException)
        {
            return SerializationFailure();
        }
        catch (NotSupportedException)
        {
            return SerializationFailure();
        }
    }

    private static void Serialize<T>(Utf8JsonWriter writer, T value) =>
        JsonSerializer.Serialize(
            writer,
            value,
            typeof(T),
            BrowserPackageJsonContext.Default);

    private static ProgressiveJsonlRecordEncodingOutcome.Failed
        SerializationFailure() =>
        new(
            new(
                ProgressiveJsonlFailureKind.RecordSerializationFailed));
}

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(InspectPackageQueryProgressiveJsonlProfile),
    typeof(InspectionEnvelope<ProgressiveJsonlProfile>))]
public static partial class PackageExports
{
    [JSExport]
    public static string InspectPackageQueryProgressiveJsonlProfile() =>
        JsonSerializer.Serialize(
            PackageQueryProgressiveJsonlContract.Inspect(),
            BrowserPackageJsonContext.Default
                .InspectionEnvelopeProgressiveJsonlProfile);
}
