using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

[Collection(ConsoleCollection.Name)]
public sealed class InspectionEnvelopeOutputTests
{
    private static readonly InspectionEnvelopeJsonContract<EnvelopeTestContent>
        Contract = new(
            "test-content",
            7,
            EnvelopeTestContentJsonContext.Default.EnvelopeTestContent);

    [Theory]
    [InlineData(WorkspaceShareFormat.Url)]
    [InlineData(WorkspaceShareFormat.Packet)]
    public async Task EnvelopeShareRemainsAfterHostDiagnostics(WorkspaceShareFormat format)
    {
        var share = new InspectionShare.Available("https://example.test/inspect", "packet");
        (int _, string output, string error) = await ConsoleCapture.RunAsync(async () =>
        {
            using var scope = WorkspaceShareOutput.DeferSideOutput();
            await Task.Yield();
            Assert.Equal(0, WorkspaceShareOutput.Write(share, format));
            CommandError.WriteLine("host metrics");
            return 0;
        });
        Assert.Empty(output);
        Assert.Equal(
            "host metrics" + Environment.NewLine
                + (format == WorkspaceShareFormat.Url ? share.FullUrl : share.Packet)
                + Environment.NewLine,
            error);
    }

    [Fact]
    public async Task ContentAndEnvelopeUseSameOwnerContractAndBaselineFrame()
    {
        var content = new EnvelopeTestContent.Available(
            "owner value",
            42,
            true,
            null,
            EnvelopeTestStatus.Ready);
        var envelope = new InspectionEnvelope<EnvelopeTestContent>(
            content,
            new InspectionShare.Available(
                "https://example.test/inspect?p=packet",
                "packet"),
            [
                new InspectionDiagnostic(
                    "first",
                    InspectionDiagnosticSeverity.Information,
                    "inert first summary"),
                new InspectionDiagnostic(
                    "second",
                    InspectionDiagnosticSeverity.Warning,
                    "inert second summary",
                    "participant:two"),
                new InspectionDiagnostic(
                    "third",
                    InspectionDiagnosticSeverity.Error,
                    "inert third summary",
                    "participant:three"),
            ]);

        (bool contentWritten, string contentJson, string contentError) =
            await WriteAsync(envelope, includeEnvelope: false);
        (bool envelopeWritten, string envelopeJson, string envelopeError) =
            await WriteAsync(envelope, includeEnvelope: true);

        Assert.True(contentWritten);
        Assert.True(envelopeWritten);
        Assert.Empty(contentError);
        Assert.Empty(envelopeError);

        using JsonDocument contentDocument = JsonDocument.Parse(contentJson);
        using JsonDocument envelopeDocument = JsonDocument.Parse(envelopeJson);
        JsonElement root = envelopeDocument.RootElement;

        AssertPropertyNames(root, "schema_version", "result_kind", "content", "share", "diagnostics");
        Assert.Equal(7, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("test-content", root.GetProperty("result_kind").GetString());
        Assert.True(JsonElement.DeepEquals(
            contentDocument.RootElement,
            root.GetProperty("content")));
        Assert.False(root.TryGetProperty("evidence", out _));
        Assert.False(root.TryGetProperty("inspection", out _));

        JsonElement framedContent = root.GetProperty("content");
        AssertPropertyNames(framedContent,
            "ownerCase", "OwnerName", "native_count", "native_enabled", "optional_value", "owner_status");
        Assert.Equal(
            "ownerAvailable",
            framedContent.GetProperty("ownerCase").GetString());
        Assert.Equal(
            JsonValueKind.String,
            framedContent.GetProperty("OwnerName").ValueKind);
        Assert.Equal(
            JsonValueKind.Number,
            framedContent.GetProperty("native_count").ValueKind);
        Assert.Equal(
            JsonValueKind.True,
            framedContent.GetProperty("native_enabled").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            framedContent.GetProperty("optional_value").ValueKind);
        Assert.Equal(
            "Ready",
            framedContent.GetProperty("owner_status").GetString());

        JsonElement share = root.GetProperty("share");
        Assert.Equal("available", share.GetProperty("kind").GetString());
        Assert.Equal(
            "https://example.test/inspect?p=packet",
            share.GetProperty("full_url").GetString());
        Assert.Equal("packet", share.GetProperty("packet").GetString());

        JsonElement[] diagnostics =
            [.. root.GetProperty("diagnostics").EnumerateArray()];
        Assert.Equal(3, diagnostics.Length);
        AssertDiagnostic(
            diagnostics[0],
            "first",
            "Information",
            "inert first summary",
            correspondence: null);
        AssertDiagnostic(
            diagnostics[1],
            "second",
            "Warning",
            "inert second summary",
            "participant:two");
        AssertDiagnostic(
            diagnostics[2],
            "third",
            "Error",
            "inert third summary",
            "participant:three");
    }

    [Fact]
    public async Task NonProjectableShareKeepsOwnerDiscriminatorNullsAndEmptyDiagnostics()
    {
        var envelope = new InspectionEnvelope<EnvelopeTestContent>(
            Content(),
            new InspectionShare.NonProjectable(
                "type/dependencies",
                "cannot project this plan"));

        (bool written, string json, string error) =
            await WriteAsync(envelope, includeEnvelope: true);

        Assert.True(written);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement share = root.GetProperty("share");

        Assert.Equal("nonProjectable", share.GetProperty("kind").GetString());
        AssertPropertyNames(share, "kind", "path", "reason", "full_url", "packet");
        Assert.Equal("type/dependencies", share.GetProperty("path").GetString());
        Assert.Equal(
            "cannot project this plan",
            share.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, share.GetProperty("full_url").ValueKind);
        Assert.Equal(JsonValueKind.Null, share.GetProperty("packet").ValueKind);
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task CompactJsonChangesWhitespaceOnlyForBothOutputBoundaries()
    {
        var envelope = new InspectionEnvelope<EnvelopeTestContent>(
            Content(),
            new InspectionShare.Available(
                "https://example.test/inspect",
                "packet"));

        (bool prettyContentWritten, string prettyContent, _) =
            await WriteAsync(envelope, includeEnvelope: false);
        (bool compactContentWritten, string compactContent, _) =
            await WriteAsync(
                envelope,
                includeEnvelope: false,
                compactJson: true);
        (bool prettyEnvelopeWritten, string prettyEnvelope, _) =
            await WriteAsync(envelope, includeEnvelope: true);
        (bool compactEnvelopeWritten, string compactEnvelope, _) =
            await WriteAsync(
                envelope,
                includeEnvelope: true,
                compactJson: true);

        Assert.True(prettyContentWritten);
        Assert.True(compactContentWritten);
        Assert.True(prettyEnvelopeWritten);
        Assert.True(compactEnvelopeWritten);
        AssertJsonEqual(prettyContent, compactContent);
        AssertJsonEqual(prettyEnvelope, compactEnvelope);
        AssertCompactOutput(compactContent);
        AssertCompactOutput(compactEnvelope);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializationFailureLeavesStdoutEmptyAndReportsError(bool includeEnvelope)
    {
        var contract = new InspectionEnvelopeJsonContract<FailingEnvelopeContent>(
            "failing-content",
            1,
            FailingEnvelopeContentJsonContext.Default.FailingEnvelopeContent);
        var envelope = new InspectionEnvelope<FailingEnvelopeContent>(
            new FailingEnvelopeContent(),
            new InspectionShare.Available(
                "https://example.test/inspect",
                "packet"));
        bool written = true;

        (string output, string error) = await ConsoleCapture.RunAsync(() =>
            written = InspectionEnvelopeOutput.TryWrite(
                envelope,
                contract,
                includeEnvelope));

        Assert.False(written);
        Assert.Empty(output);
        Assert.Contains(
            "Error: content serialization failed after writing began",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EnrichedEnvelopeUsesFlatTransportFrame()
    {
        var inspection = new InspectionEnvelope<EnvelopeTestContent>(
            Content(),
            new InspectionShare.Available(
                "https://example.test/inspect",
                "packet"));
        var enriched =
            new EvidenceInspectionEnvelope<
                EnvelopeTestContent,
                EnvelopeTestEvidence>(
                    inspection,
                    new EnvelopeTestEvidence("retained"));

        bool serialized =
            InspectionEnvelopeOutput.TrySerializeEvidence(
                enriched,
                Contract,
                EnvelopeTestContentJsonContext.Default
                    .EnvelopeTestEvidence,
                compactJson: true,
                out byte[] payload,
                out Exception? exception);
        bool prettySerialized =
            InspectionEnvelopeOutput.TrySerializeEvidence(
                enriched,
                Contract,
                EnvelopeTestContentJsonContext.Default
                    .EnvelopeTestEvidence,
                compactJson: false,
                out byte[] prettyPayload,
                out Exception? prettyException);

        Assert.True(serialized);
        Assert.True(prettySerialized);
        Assert.Null(exception);
        Assert.Null(prettyException);
        Assert.Equal((byte)'\n', payload[^1]);
        AssertJsonEqual(
            System.Text.Encoding.UTF8.GetString(prettyPayload),
            System.Text.Encoding.UTF8.GetString(payload));
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        Assert.Equal(7, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "test-content",
            root.GetProperty("result_kind").GetString());
        Assert.Equal(
            "retained",
            root.GetProperty("evidence")
                .GetProperty("owner_evidence_name")
                .GetString());
        Assert.False(root.TryGetProperty("inspection", out _));
    }

    [Fact]
    public void EvidencePublicationAtomicallyReplacesTheExistingFile()
    {
        using var directory =
            new TemporaryTestDirectory("evidence-publication-");
        string path = Path.Combine(directory.FullName, "evidence.json");
        File.WriteAllText(path, "old");
        byte[] payload = "new\n"u8.ToArray();

        bool published = EvidenceEnvelopeOutput.TryPublish(
            path,
            payload,
            out Exception? exception);

        Assert.True(published);
        Assert.Null(exception);
        Assert.Equal(payload, File.ReadAllBytes(path));
        Assert.Equal(
            [path],
            Directory.EnumerateFiles(directory.FullName));
    }

    [Fact]
    public void EvidencePublicationFailureLeavesAbsentDestinationAbsent()
    {
        using var directory =
            new TemporaryTestDirectory("evidence-publication-failure-");
        string missingParent = Path.Combine(
            directory.FullName,
            "missing");
        string path = Path.Combine(missingParent, "evidence.json");

        bool published = EvidenceEnvelopeOutput.TryPublish(
            path,
            "new\n"u8,
            out Exception? exception);

        Assert.False(published);
        Assert.IsType<DirectoryNotFoundException>(exception);
        Assert.False(Directory.Exists(missingParent));
        Assert.False(File.Exists(path));
        Assert.Equal(
            [],
            Directory.EnumerateFileSystemEntries(directory.FullName));
    }

    [Theory]
    [InlineData(@"C:\work\result.json", @"C:\work\result.json")]
    [InlineData(@"\\?\C:\work\result.json", @"C:\work\result.json")]
    [InlineData(@"\\.\C:\work\result.json", @"C:\work\result.json")]
    [InlineData(
        @"\\?\UNC\server\share\result.json",
        @"\\server\share\result.json")]
    [InlineData(@"\\server\share\result.json", @"\\server\share\result.json")]
    public void WindowsPathNormalizationCollapsesEquivalentFileNamespaces(
        string path,
        string expected)
    {
        Assert.Equal(
            expected,
            EvidenceEnvelopeOutput.NormalizeWindowsPath(path));
    }

    [Fact]
    public void WindowsPathNormalizationRefusesUnsupportedDeviceNamespaces()
    {
        Assert.Null(
            EvidenceEnvelopeOutput.NormalizeWindowsPath(
                @"\\?\Volume{00000000-0000-0000-0000-000000000000}\result.json"));
    }

    private static EnvelopeTestContent Content() =>
        new EnvelopeTestContent.Available(
            "owner value",
            42,
            true,
            null,
            EnvelopeTestStatus.Ready);

    private static async Task<(bool Written, string Output, string Error)>
        WriteAsync(
            InspectionEnvelope<EnvelopeTestContent> envelope,
            bool includeEnvelope,
            bool compactJson = false)
    {
        bool written = false;
        (string output, string error) = await ConsoleCapture.RunAsync(() =>
            written = InspectionEnvelopeOutput.TryWrite(
                envelope,
                Contract,
                includeEnvelope,
                compactJson));
        return (written, output, error);
    }

    private static void AssertDiagnostic(
        JsonElement diagnostic,
        string code,
        string severity,
        string summary,
        string? correspondence)
    {
        AssertPropertyNames(diagnostic, "code", "severity", "summary", "correspondence");
        Assert.Equal(code, diagnostic.GetProperty("code").GetString());
        Assert.Equal(severity, diagnostic.GetProperty("severity").GetString());
        Assert.Equal(summary, diagnostic.GetProperty("summary").GetString());
        Assert.Equal(
            correspondence is null
                ? JsonValueKind.Null
                : JsonValueKind.String,
            diagnostic.GetProperty("correspondence").ValueKind);
        if (correspondence is not null)
        {
            Assert.Equal(
                correspondence,
                diagnostic.GetProperty("correspondence").GetString());
        }
    }

    private static void AssertPropertyNames(JsonElement value, params string[] expected) =>
        Assert.Equal(
            expected.OrderBy(static name => name, StringComparer.Ordinal),
            value.EnumerateObject().Select(static property => property.Name)
                .OrderBy(static name => name, StringComparer.Ordinal));

    private static void AssertJsonEqual(string expected, string actual)
    {
        using JsonDocument expectedDocument = JsonDocument.Parse(expected);
        using JsonDocument actualDocument = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(
            expectedDocument.RootElement,
            actualDocument.RootElement));
    }

    private static void AssertCompactOutput(string output)
    {
        Assert.EndsWith(Environment.NewLine, output);
        string payload = output[..^Environment.NewLine.Length];
        Assert.DoesNotContain("\r", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", payload, StringComparison.Ordinal);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "ownerCase")]
[JsonDerivedType(
    typeof(EnvelopeTestContent.Available),
    typeDiscriminator: "ownerAvailable")]
internal abstract record EnvelopeTestContent
{
    internal sealed record Available(
        [property: JsonPropertyName("OwnerName")] string Name,
        [property: JsonPropertyName("native_count")] int Count,
        [property: JsonPropertyName("native_enabled")] bool Enabled,
        [property: JsonPropertyName("optional_value")] string? OptionalValue,
        [property: JsonPropertyName("owner_status")] EnvelopeTestStatus Status)
        : EnvelopeTestContent;
}

internal enum EnvelopeTestStatus
{
    Ready,
}

internal sealed record EnvelopeTestEvidence(
    [property: JsonPropertyName("owner_evidence_name")] string Name);

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(EnvelopeTestContent))]
[JsonSerializable(typeof(EnvelopeTestEvidence))]
internal partial class EnvelopeTestContentJsonContext : JsonSerializerContext;

[JsonConverter(typeof(FailingEnvelopeContentJsonConverter))]
internal sealed record FailingEnvelopeContent;

internal sealed class FailingEnvelopeContentJsonConverter
    : JsonConverter<FailingEnvelopeContent>
{
    public override FailingEnvelopeContent? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new NotSupportedException();

    public override void Write(
        Utf8JsonWriter writer,
        FailingEnvelopeContent value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("partial", "value");
        throw new JsonException(
            "content serialization failed after writing began");
    }
}

[JsonSerializable(typeof(FailingEnvelopeContent))]
internal partial class FailingEnvelopeContentJsonContext : JsonSerializerContext;
