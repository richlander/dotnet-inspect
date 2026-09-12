using System.Text.Json;
using DotnetInspector.Core;
using InertText;

namespace DotnetInspector.Services.Tests;

public sealed class InspectionEnvelopeTests
{
    [Fact]
    public void ConstructionPreservesRequiredContentShareAndDiagnosticOrder()
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
            "content",
            new InspectionShare.Available(
                "https://dotnet-inspect.net/?w=encoded",
                "encoded"),
            [first, second]);

        Assert.Equal("content", envelope.Content);
        Assert.Equal(
            "https://dotnet-inspect.net/?w=encoded",
            Assert.IsType<InspectionShare.Available>(envelope.Share).FullUrl);
        Assert.Equal(
            "encoded",
            Assert.IsType<InspectionShare.Available>(envelope.Share).Packet);
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
    public void NonProjectableShareRetainsContainedReason()
    {
        var share = new InspectionShare.NonProjectable(
            "workspace.contexts[0]",
            "unsupported\u001b[31m");

        Assert.Equal("workspace.contexts[0]", share.Path);
        Assert.Equal("unsupported\\^[[31m", share.Reason.ToString());
    }

    [Fact]
    public void AvailableShareRequiresHttpsUrl()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new InspectionShare.Available(
                "http://example.test/share",
                "encoded"));

        Assert.Contains("HTTPS", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AvailableShareRequiresPacket()
    {
        Assert.Throws<ArgumentException>(
            () => new InspectionShare.Available(
                "https://dotnet-inspect.net/?w=encoded",
                ""));
    }

    [Fact]
    public void AvailableShareSerializesBothConsumerValues()
    {
        var share = new InspectionShare.Available(
            "https://dotnet-inspect.net/?w=encoded",
            "encoded");

        string json = JsonSerializer.Serialize(
            share,
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

        static InspectionEnvelope<string> CreateEquivalent() =>
            new(
                "content",
                new InspectionShare.NonProjectable(
                    "share",
                    "not available"),
                [
                    new InspectionDiagnostic(
                        "type-dependency.warning",
                        InspectionDiagnosticSeverity.Warning,
                        "A warning"),
                ]);
    }
}
