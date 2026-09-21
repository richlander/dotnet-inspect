using System.Text.Json;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class InspectionEnvelopeTests
{
    [Fact]
    public void EvidenceConstructionPreservesBaselineAndEvidence()
    {
        InspectionEnvelope<string> inspection = CreateEquivalent();
        var envelope = new EvidenceInspectionEnvelope<string, int[]>(
            inspection,
            [1, 2, 3]);

        Assert.Same(inspection, envelope.Inspection);
        Assert.Equal([1, 2, 3], envelope.Evidence);
    }

    [Fact]
    public void EvidenceEnvelopeEqualityIncludesBaselineAndEvidence()
    {
        var first = new EvidenceInspectionEnvelope<string, string>(
            CreateEquivalent(),
            "evidence");
        var equal = new EvidenceInspectionEnvelope<string, string>(
            CreateEquivalent(),
            "evidence");
        var differentEvidence =
            new EvidenceInspectionEnvelope<string, string>(
                CreateEquivalent(),
                "different");

        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, differentEvidence);
    }

    [Fact]
    public void EvidenceEnvelopeRejectsNullValues()
    {
        Assert.Throws<ArgumentNullException>(
            () => new EvidenceInspectionEnvelope<string, string>(
                null!,
                "evidence"));
        Assert.Throws<ArgumentNullException>(
            () => new EvidenceInspectionEnvelope<string, string>(
                CreateEquivalent(),
                null!));
    }

    [Fact]
    public void ConstructionPreservesRequiredValuesAndDiagnosticOrder()
    {
        var first = new InspectionDiagnostic(
            "type-dependency.participant-rejected",
            InspectionDiagnosticSeverity.Warning,
            "Rejected participant\r\n");
        var second = new InspectionDiagnostic(
            "type-dependency.row-selection-failed",
            InspectionDiagnosticSeverity.Error,
            "Selection failed");
        var envelope = new InspectionEnvelope<string>(
            new ResourcePath("type-dependencies"),
            InspectionContentKind.Result,
            "content",
            new InspectionPortableProjection.Available(
                "https://dotnet-inspect.net/?w=encoded",
                "encoded"),
            [first, second]);

        Assert.Equal(InspectionContentKind.Result, envelope.ContentKind);
        Assert.Equal("type-dependencies", envelope.ResourcePath.Value);
        Assert.Equal("content", envelope.Content);
        Assert.Equal(
            "https://dotnet-inspect.net/?w=encoded",
            Assert.IsType<InspectionPortableProjection.Available>(envelope.PortableProjection).FullUrl);
        Assert.Equal(
            "encoded",
            Assert.IsType<InspectionPortableProjection.Available>(envelope.PortableProjection).Packet);
        Assert.Collection(
            envelope.Diagnostics,
            diagnostic =>
            {
                Assert.Equal(first.Code, diagnostic.Code);
                Assert.Equal(
                    "Rejected participant\\^M\\^J",
                    diagnostic.Summary.ToString());
            },
            diagnostic => Assert.Equal(second.Code, diagnostic.Code));
    }

    [Fact]
    public void DiagnosticSummaryIsContainedAtConstruction()
    {
        var diagnostic = new InspectionDiagnostic(
            "type-dependency.warning",
            InspectionDiagnosticSeverity.Warning,
            "line\u001b[31m");

        Assert.Equal("line\\^[[31m", diagnostic.Summary.ToString());
        Assert.Equal(TextConcern.Control, diagnostic.Summary.Concerns);
    }

    [Fact]
    public void NonProjectableRetainsTypedReasonLocationAndExplanation()
    {
        var portableProjection =
            new InspectionPortableProjection.NonProjectable(
            InspectionPortableProjectionFailureReason.NotSupported,
            location: "workspace.contexts[0]",
            explanation: "This operation has no portable form.");

        Assert.Equal("workspace.contexts[0]", portableProjection.Location);
        Assert.Equal(
            InspectionPortableProjectionFailureReason.NotSupported,
            portableProjection.Reason);
        Assert.Equal(
            "This operation has no portable form.",
            portableProjection.Explanation);
    }

    [Fact]
    public void ConstructionRejectsUnknownContentKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InspectionEnvelope<string>(
                new ResourcePath("workspace"),
                (InspectionContentKind)int.MaxValue,
                "content",
                NonProjectable()));
    }

    [Fact]
    public void NonProjectableRejectsUnknownReason()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InspectionPortableProjection.NonProjectable(
                (InspectionPortableProjectionFailureReason)int.MaxValue));
    }

    [Fact]
    public void NonProjectableRejectsEmptyExplanation()
    {
        Assert.Throws<ArgumentException>(
            () => new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported,
                explanation: ""));
    }

    [Fact]
    public void AvailablePortableProjectionRequiresHttpsUrl()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new InspectionPortableProjection.Available(
                "http://example.test/share",
                "encoded"));

        Assert.Contains("HTTPS", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AvailablePortableProjectionRequiresPacket()
    {
        Assert.Throws<ArgumentException>(
            () => new InspectionPortableProjection.Available(
                "https://dotnet-inspect.net/?w=encoded",
                ""));
    }

    [Fact]
    public void AvailablePortableProjectionSerializesBothConsumerValues()
    {
        var portableProjection =
            new InspectionPortableProjection.Available(
            "https://dotnet-inspect.net/?w=encoded",
            "encoded");

        string json = JsonSerializer.Serialize(
            portableProjection,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"fullUrl\":\"https://dotnet-inspect.net/?w=encoded\"", json);
        Assert.Contains("\"packet\":\"encoded\"", json);
    }

    [Fact]
    public void EquivalentEnvelopesCompareByDiagnosticValues()
    {
        InspectionEnvelope<string> first = CreateEquivalent();
        InspectionEnvelope<string> second = CreateEquivalent();

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Theory]
    [InlineData(InspectionContentKind.Result, "\"result\"")]
    [InlineData(InspectionContentKind.Document, "\"document\"")]
    [InlineData(InspectionContentKind.Outcome, "\"outcome\"")]
    public void ContentKindSerializesWithStableValue(
        InspectionContentKind contentKind,
        string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(contentKind));
    }

    [Fact]
    public void EnvelopeEqualityIncludesContentKind()
    {
        InspectionEnvelope<string> result = CreateEquivalent();
        var document = new InspectionEnvelope<string>(
            result.ResourcePath,
            InspectionContentKind.Document,
            result.Content,
            result.PortableProjection,
            result.Diagnostics);

        Assert.NotEqual(result, document);
    }

    [Fact]
    public void EnvelopeEqualityIncludesResourcePath()
    {
        InspectionEnvelope<string> baseline = CreateEquivalent();
        var other = new InspectionEnvelope<string>(
            new ResourcePath("other-resource"),
            baseline.ContentKind,
            baseline.Content,
            baseline.PortableProjection,
            baseline.Diagnostics);

        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ResourcePathDoesNotDependOnProjectionAvailability()
    {
        var resourcePath = new ResourcePath("type-dependencies");
        var available = new InspectionEnvelope<string>(
            resourcePath,
            InspectionContentKind.Result,
            "content",
            new InspectionPortableProjection.Available(
                "https://dotnet-inspect.net/?w=encoded",
                "encoded"));
        var nonProjectable = new InspectionEnvelope<string>(
            resourcePath,
            InspectionContentKind.Result,
            "content",
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported,
                location: "subject"));

        Assert.Equal(resourcePath, available.ResourcePath);
        Assert.Equal(resourcePath, nonProjectable.ResourcePath);
    }

    [Fact]
    public void EnvelopeEqualityIncludesPortableProjectionDetails()
    {
        InspectionEnvelope<string> baseline = CreateEquivalent();
        var differentReason = new InspectionEnvelope<string>(
            baseline.ResourcePath,
            InspectionContentKind.Result,
            "content",
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.Unavailable),
            baseline.Diagnostics);
        var differentExplanation = new InspectionEnvelope<string>(
            baseline.ResourcePath,
            InspectionContentKind.Result,
            "content",
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported,
                explanation: "No portable form."),
            baseline.Diagnostics);

        Assert.NotEqual(baseline, differentReason);
        Assert.NotEqual(baseline, differentExplanation);
    }

    private static InspectionPortableProjection NonProjectable() =>
        new InspectionPortableProjection.NonProjectable(
            InspectionPortableProjectionFailureReason.NotSupported);

    private static InspectionEnvelope<string> CreateEquivalent() =>
        new(
            new ResourcePath("test-resource"),
            InspectionContentKind.Result,
            "content",
            NonProjectable(),
            [
                new InspectionDiagnostic(
                    "type-dependency.warning",
                    InspectionDiagnosticSeverity.Warning,
                    "A warning"),
            ]);
}
